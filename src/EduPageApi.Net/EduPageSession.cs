namespace EduPageApi;

/// <summary>An authenticated session owning its transport until disposed by the caller.</summary>
public sealed class EduPageSession : IDisposable, IAsyncDisposable
{
    private readonly SessionTransport transport;
    private readonly IReadOnlyList<EduPageChild> children;
    private int disposed;

    internal EduPageSession(SessionTransport transport, EduPageChild[] children)
    {
        this.transport = transport;
        this.children = Array.AsReadOnly(children);
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
            transport.Dispose();
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
