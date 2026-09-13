using System.Net;
using Microsoft.Extensions.DependencyInjection;

namespace EduPageApi.Net.Tests;

public sealed class SessionTests
{
    [Fact]
    public void RegistrationIsIdempotentAndFactoryIsSharedAcrossScopes()
    {
        var services = new ServiceCollection();
        Assert.Same(services, services.AddEduPageSessions());
        services.AddEduPageSessions();
        var registration = Assert.Single(services);
        Assert.Equal(typeof(EduPageSessionFactory), registration.ServiceType);
        Assert.Equal(ServiceLifetime.Singleton, registration.Lifetime);
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        using var scope = provider.CreateScope();
        Assert.Same(provider.GetRequiredService<EduPageSessionFactory>(),
            scope.ServiceProvider.GetRequiredService<EduPageSessionFactory>());
    }

    [Fact]
    public void CookieStoresAreIndependentAndDisposalDoesNotAffectAnotherTransport()
    {
        using var first = new SessionTransport();
        using var second = new SessionTransport();
        var origin = new Uri("https://school.edupage.org/");
        first.Cookies.SetCookies(origin, "session=first; Secure; Path=/");
        Assert.Empty(second.Cookies.GetCookies(origin));
        second.Cookies.SetCookies(origin, "session=second; Secure; Path=/");
        first.Dispose();
        Assert.Equal("second", second.Cookies.GetCookies(origin)["session"]!.Value);
        Assert.NotSame(first.Client, second.Client);
    }

    [Theory]
    [InlineData("school")]
    [InlineData("school.edupage.org")]
    [InlineData("https://school.edupage.org/")]
    public async Task ReusingSelectionCreatesIndependentCallerOwnedSessions(string school)
    {
        var handlers = new List<RecordingHandler>();
        var factory = new EduPageSessionFactory(() =>
        {
            var handler = new RecordingHandler();
            handlers.Add(handler);
            return new SessionTransport(handler);
        });
        var selection = factory.ForSchool(school);
        Assert.Empty(handlers);
        using var first = await selection.ConnectAsync("first", "synthetic");
        await using var second = await selection.ConnectAsync("second", "synthetic");
        Assert.Equal(2, handlers.Count);
        first.Dispose();
        first.Dispose();
        Assert.Equal(1, handlers[0].Disposals);
        Assert.Equal(0, handlers[1].Disposals);
        Assert.Empty(second.Children);
        Assert.Throws<ObjectDisposedException>(() => first.Children);
        await second.DisposeAsync();
        Assert.Equal(1, handlers[1].Disposals);
    }

    [Fact]
    public async Task DisposingServiceProviderDoesNotDisposeCallerOwnedSession()
    {
        var handler = new RecordingHandler();
        var factory = new EduPageSessionFactory(() => new SessionTransport(handler));
        var provider = new ServiceCollection().AddSingleton(factory).AddEduPageSessions().BuildServiceProvider();
        await using var session = await provider.GetRequiredService<EduPageSessionFactory>()
            .ForSchool("school").ConnectAsync("parent", "synthetic");
        provider.Dispose();
        Assert.Equal(0, handler.Disposals);
        Assert.Empty(session.Children);
    }

    [Fact]
    public async Task FailedConnectionDisposesTransportAndMapsInternalException()
    {
        var handler = new RecordingHandler { Fail = true };
        var selection = new EduPageSessionFactory(() => new SessionTransport(handler)).ForSchool("school");
        var exception = await Assert.ThrowsAsync<EduPageException>(() => selection.ConnectAsync("parent", "synthetic"));
        Assert.Null(exception.InnerException);
        Assert.Equal(1, handler.Disposals);
    }

    [Fact]
    public async Task CancellationDuringConnectDisposesTransport()
    {
        var handler = new RecordingHandler { Cancel = true };
        var selection = new EduPageSessionFactory(() => new SessionTransport(handler)).ForSchool("school");
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => selection.ConnectAsync("parent", "synthetic"));
        Assert.Equal(1, handler.Disposals);
    }

    [Theory]
    [InlineData("http://school.edupage.org")]
    [InlineData("https://other.example/")]
    [InlineData("https://school.edupage.org/login/")]
    [InlineData("https://user@school.edupage.org/")]
    [InlineData("https://school.edupage.org/?secret=value")]
    [InlineData("https://school.edupage.org:444/")]
    public void InvalidSchoolDoesNotCreateTransport(string school)
    {
        var created = false;
        var factory = new EduPageSessionFactory(() => { created = true; return new SessionTransport(); });
        Assert.Throws<ArgumentException>(() => factory.ForSchool(school));
        Assert.False(created);
    }

    [Fact]
    public async Task PreCancelledConnectionDoesNotCreateTransport()
    {
        var created = false;
        var factory = new EduPageSessionFactory(() => { created = true; return new SessionTransport(); });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            factory.ForSchool("school").ConnectAsync("parent", "synthetic", new CancellationToken(true)));
        Assert.False(created);
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        internal int Disposals { get; private set; }
        internal bool Fail { get; init; }
        internal bool Cancel { get; init; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (Cancel) throw new OperationCanceledException(cancellationToken);
            Assert.Equal("school.edupage.org", request.RequestUri!.Host);
            var body = request.RequestUri.AbsolutePath == "/user/"
                ? "<script>x.userhome({\"parentStudentids\":[],\"dbi\":{\"students\":{}}});</script>"
                : request.RequestUri.Query.Contains("akcia=getToken", StringComparison.Ordinal)
                    ? "{\"token\":\"synthetic-token\"}"
                    : request.RequestUri.Query.Contains("akcia=login", StringComparison.Ordinal)
                        ? "{\"status\":\"OK\",\"redirectUrl\":\"/user/\"}"
                        : "<html>Login</html>";
            return Task.FromResult(new HttpResponseMessage(Fail ? HttpStatusCode.Forbidden : HttpStatusCode.OK)
            {
                Content = new StringContent(body)
            });
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) Disposals++;
            base.Dispose(disposing);
        }
    }
}
