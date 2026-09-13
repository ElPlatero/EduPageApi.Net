using System.Text.RegularExpressions;
using EduPageApi.Protocol;

namespace EduPageApi;

/// <summary>Creates independent caller-owned sessions without retaining credentials or sessions.</summary>
public sealed class EduPageSessionFactory
{
    private readonly Func<SessionTransport> createTransport;

    /// <summary>Creates a factory whose sessions each own a separate transport.</summary>
    public EduPageSessionFactory() : this(() => new SessionTransport()) { }

    internal EduPageSessionFactory(Func<SessionTransport> createTransport) => this.createTransport = createTransport;

    /// <summary>Chooses a school by subdomain name or HTTPS origin. Performs no network request.</summary>
    public EduPageConnection ForSchool(string school)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(school);
        school = school.Trim();
        if (!school.Contains("://", StringComparison.Ordinal))
            school = "https://" + (school.EndsWith(".edupage.org", StringComparison.OrdinalIgnoreCase)
                ? school : school + ".edupage.org");
        if (!Uri.TryCreate(school, UriKind.Absolute, out var origin)
            || origin.Scheme != Uri.UriSchemeHttps || !origin.IsDefaultPort
            || origin.AbsolutePath != "/" || origin.UserInfo.Length != 0
            || origin.Query.Length != 0 || origin.Fragment.Length != 0
            || !Regex.IsMatch(origin.Host, @"\A[a-z0-9](?:[a-z0-9-]*[a-z0-9])?\.edupage\.org\z",
                RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)))
            throw new ArgumentException("A school subdomain or HTTPS school origin is required.", nameof(school));
        return new EduPageConnection(origin, createTransport);
    }
}

/// <summary>An immutable school selection that can create independent authenticated sessions.</summary>
public sealed class EduPageConnection
{
    private readonly Uri origin;
    private readonly Func<SessionTransport> createTransport;

    internal EduPageConnection(Uri origin, Func<SessionTransport> createTransport)
    {
        this.origin = origin;
        this.createTransport = createTransport;
    }

    /// <summary>Authenticates and returns a session owned by the caller. Does not retain credentials.</summary>
    public async Task<EduPageSession> ConnectAsync(string username, string password,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(username);
        ArgumentException.ThrowIfNullOrEmpty(password);
        cancellationToken.ThrowIfCancellationRequested();
        var transport = createTransport();
        try
        {
            var children = await new LoginProtocol(transport.Client, origin)
                .ConnectAsync(username, password, cancellationToken).ConfigureAwait(false);
            return new EduPageSession(transport, children.Select(child =>
                new EduPageChild(child.Id, child.FirstName, child.LastName)).ToArray());
        }
        catch (ProtocolException error)
        {
            transport.Dispose();
            throw new EduPageException(error.Message);
        }
        catch
        {
            transport.Dispose();
            throw;
        }
    }
}
