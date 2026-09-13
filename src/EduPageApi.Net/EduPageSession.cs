using EduPageApi.Protocol;

namespace EduPageApi;

/// <summary>An authenticated session owning its transport until disposed by the caller.</summary>
public sealed class EduPageSession : IDisposable, IAsyncDisposable
{
    private readonly SessionTransport transport;
    private readonly IReadOnlyList<EduPageChild> children;
    private readonly Uri origin;
    private readonly SemaphoreSlim operations = new(1, 1);
    private readonly CancellationTokenSource lifetime = new();
    private readonly CancellationToken lifetimeToken;
    private int disposed;

    internal EduPageSession(SessionTransport transport, Uri origin, EduPageChild[] children)
    {
        this.transport = transport;
        this.origin = origin;
        lifetimeToken = lifetime.Token;
        this.children = Array.AsReadOnly(children);
    }

    /// <summary>Selects a child from this session without performing a network request.</summary>
    public EduPageChildQuery ForChild(EduPageChild child)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        ArgumentNullException.ThrowIfNull(child);
        if (!children.Any(candidate => ReferenceEquals(candidate, child)))
            throw new ArgumentException("Choose a child from this session's Children collection.", nameof(child));
        return new EduPageChildQuery(this, child.Id);
    }

    internal async Task<IReadOnlyList<EduPageGrade>> GetGradesAsync(string childId, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, lifetimeToken);
        await operations.WaitAsync(linked.Token).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
            // Always re-establish context, including after an interrupted previous operation.
            await GetPageAsync("login/switchchild?studentid=" + Uri.EscapeDataString(childId), linked.Token).ConfigureAwait(false);
            var html = await GetPageAsync("znamky/", linked.Token).ConfigureAwait(false);
            var grades = GradesPageParser.Parse(html, childId);
            return Array.AsReadOnly(grades.Select(grade => new EduPageGrade(grade.Id, grade.StudentId,
                grade.Subject, grade.Assessment, grade.Value, grade.RecordedAt)).ToArray());
        }
        catch (ProtocolException error)
        {
            throw new EduPageException(error.Message);
        }
        finally
        {
            operations.Release();
        }
    }

    private async Task<string> GetPageAsync(string path, CancellationToken cancellationToken)
    {
        using var response = await transport.Client.GetAsync(new Uri(origin, path), cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw new EduPageException("The grades request was unsuccessful or the session has expired.");
        return await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>The children loaded when this session connected. Performs no network request.</summary>
    public IReadOnlyList<EduPageChild> Children
    {
        get
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
            return children;
        }
    }

    /// <summary>Releases the local transport. Does not perform a server-side logout.</summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) == 0)
        {
            lifetime.Cancel();
            transport.Dispose();
            lifetime.Dispose();
            // Pending operations still release the gate; do not dispose it while they unwind.
        }
    }

    /// <summary>Releases the local transport. Does not perform a server-side logout.</summary>
    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }
}

/// <summary>A child associated with an authenticated parent account.</summary>
/// <param name="Id">The child's identifier within the school.</param>
/// <param name="FirstName">The child's first name.</param>
/// <param name="LastName">The child's last name.</param>
public sealed record EduPageChild(string Id, string FirstName, string LastName);

/// <summary>An authentication or remote data-format error with a payload-free message.</summary>
public sealed class EduPageException : Exception
{
    internal EduPageException(string message) : base(message) { }
}
