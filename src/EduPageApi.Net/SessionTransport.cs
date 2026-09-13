using System.Net;

namespace EduPageApi;

internal sealed class SessionTransport : IDisposable
{
    internal CookieContainer Cookies { get; } = new();
    internal HttpClient Client { get; }

    internal SessionTransport()
    {
        Client = new HttpClient(new SocketsHttpHandler
        {
            CookieContainer = Cookies,
            UseCookies = true,
            AllowAutoRedirect = false,
            // Refresh connections/DNS without replacing this session's cookie container.
            PooledConnectionLifetime = TimeSpan.FromMinutes(5)
        });
    }

    // Offline test seam, never exposed through the public API or DI.
    internal SessionTransport(HttpMessageHandler handler) => Client = new HttpClient(handler);

    public void Dispose() => Client.Dispose();
}
