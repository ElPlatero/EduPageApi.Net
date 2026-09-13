using System.Net;

namespace EduPageApi.Net.Tests;

public sealed class GradesQueryTests
{
    private static EduPageSession Session(HttpMessageHandler handler) => new(new SessionTransport(handler),
        new Uri("https://school.edupage.org/"), [new("-101", "Ada", "Example"), new("-102", "Ben", "Example")]);

    [Fact]
    public async Task QueryIsLazyAndMapsGrades()
    {
        var handler = new Handler();
        using var session = Session(handler);
        var query = session.ForChild(session.Children[0]).Grades;
        Assert.Empty(handler.Paths);
        var grade = Assert.Single(await query.GetAsync());
        Assert.Equal("Mathematik", grade.Subject);
        Assert.Equal("2 (Kommentar)", grade.Value);
        Assert.Equal("-101", grade.StudentId);
        Assert.Equal(["/login/switchchild?studentid=-101", "/znamky/"], handler.Paths);
    }

    [Fact]
    public void ForeignChildIsRejectedEvenWithMatchingIdentifier()
    {
        var handler = new Handler();
        using var session = Session(handler);
        using var other = Session(new Handler());
        Assert.Throws<ArgumentException>(() => session.ForChild(other.Children[0]));
        Assert.Empty(handler.Paths);
    }

    [Fact]
    public async Task ConcurrentQueriesKeepSwitchAndReadTogether()
    {
        var handler = new Handler { PauseFirstSwitch = true };
        using var session = Session(handler);
        var first = session.ForChild(session.Children[0]).Grades.GetAsync();
        await handler.SwitchStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var second = session.ForChild(session.Children[1]).Grades.GetAsync();
        Assert.Single(handler.Paths);
        handler.Continue.TrySetResult();
        var results = await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("-101", Assert.Single(results[0]).StudentId);
        Assert.Equal("-102", Assert.Single(results[1]).StudentId);
        Assert.Equal(["/login/switchchild?studentid=-101", "/znamky/",
            "/login/switchchild?studentid=-102", "/znamky/"], handler.Paths);
    }

    [Fact]
    public async Task CancelledSwitchReleasesGateAndNextCallSelectsAgain()
    {
        var handler = new Handler { PauseFirstSwitch = true };
        using var session = Session(handler);
        using var cancel = new CancellationTokenSource();
        var first = session.ForChild(session.Children[0]).Grades.GetAsync(cancel.Token);
        await handler.SwitchStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
        var grades = await session.ForChild(session.Children[1]).Grades.GetAsync();
        Assert.Equal("-102", Assert.Single(grades).StudentId);
        Assert.Equal(3, handler.Paths.Count);
    }

    [Fact]
    public async Task CancelledWaitDoesNotSendARequest()
    {
        var handler = new Handler { PauseFirstSwitch = true };
        using var session = Session(handler);
        var first = session.ForChild(session.Children[0]).Grades.GetAsync();
        await handler.SwitchStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        using var cancel = new CancellationTokenSource();
        var waiting = session.ForChild(session.Children[1]).Grades.GetAsync(cancel.Token);
        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting);
        Assert.Single(handler.Paths);
        handler.Continue.TrySetResult();
        await first;
    }

    [Theory]
    [InlineData("wrong-child")]
    [InlineData("login-page")]
    [InlineData("switch-failure")]
    [InlineData("redirect")]
    public async Task InvalidResponsesNeverProduceGrades(string failure)
    {
        var handler = new Handler { Failure = failure };
        using var session = Session(handler);
        await Assert.ThrowsAsync<EduPageException>(() => session.ForChild(session.Children[0]).Grades.GetAsync());
        Assert.Equal(failure == "switch-failure" ? 1 : 2, handler.Paths.Count);
    }

    [Fact]
    public async Task DisposalCancelsActiveAndWaitingOperations()
    {
        var handler = new Handler { PauseFirstSwitch = true };
        using var session = Session(handler);
        var query = session.ForChild(session.Children[0]).Grades;
        var first = query.GetAsync();
        await handler.SwitchStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var waiting = query.GetAsync();
        session.Dispose();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => query.GetAsync());
    }

    private sealed class Handler : HttpMessageHandler
    {
        internal List<string> Paths { get; } = [];
        internal bool PauseFirstSwitch { get; init; }
        internal string? Failure { get; init; }
        internal TaskCompletionSource SwitchStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Continue { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private string child = "";

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Paths.Add(request.RequestUri!.PathAndQuery);
            Assert.Equal(HttpMethod.Get, request.Method);
            if (request.RequestUri.AbsolutePath == "/login/switchchild")
            {
                child = request.RequestUri.Query.Split('=')[1];
                SwitchStarted.TrySetResult();
                if (PauseFirstSwitch && Paths.Count == 1) await Continue.Task.WaitAsync(cancellationToken);
                return Reply("", Failure == "switch-failure" ? HttpStatusCode.Forbidden : HttpStatusCode.OK);
            }
            if (Failure == "redirect") return Reply("", HttpStatusCode.Found);
            if (Failure == "login-page") return Reply("<html>Login</html>");
            var id = Failure == "wrong-child" ? "-999" : child;
            return Reply($$$$"""
                <script>x.znamkyStudentViewer({"studentid":"{{{{id}}}}",
                  "vsetkyZnamky":[{"studentid":"{{{{id}}}}","znamkaid":"1","provider":"edupage",
                    "predmetid":"10","udalostid":"20","data":"2 (Kommentar)","datum":"2025-01-02 10:00:00"}],
                  "predmety":{"10":{"p_meno":"Mathematik"}},
                  "vsetkyUdalosti":{"edupage":{"20":{"p_meno":"Übung","p_vaha":1,"p_typ_udalosti":1}}}
                });</script>
                """);
        }

        private static HttpResponseMessage Reply(string text, HttpStatusCode status = HttpStatusCode.OK) =>
            new(status) { Content = new StringContent(text) };
    }
}
