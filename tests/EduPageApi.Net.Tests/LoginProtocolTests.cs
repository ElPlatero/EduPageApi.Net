using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.Json;
using EduPageApi.Protocol;

namespace EduPageApi.Net.Tests;

public sealed class LoginProtocolTests
{
    [Fact]
    public async Task SendsTokenThenCredentialsAndReadsParentPage()
    {
        using var handler = new ScriptedHandler();
        using var http = new HttpClient(handler);
        var children = await new LoginProtocol(http, new Uri("https://school.example/"))
            .ConnectAsync("parent@example.invalid", "synthetic-password");
        Assert.Equal("Ada", Assert.Single(children).FirstName);
        Assert.Equal(4, handler.Calls);
        Assert.Equal(["getToken", "login"], handler.Actions);
    }

    [Theory]
    [InlineData("{\"status\":\"ERROR\",\"err\":\"private-server-details\"}")]
    [InlineData("{\"status\":\"OK\",\"need2fa\":true,\"redirectUrl\":\"/user/\"}")]
    [InlineData("{\"status\":\"OK\",\"redirectUrl\":\"https://other.example/user/\"}")]
    [InlineData("{\"status\":\"OK\",\"redirectUrl\":\"http://school.example/user/\"}")]
    [InlineData("{\"status\":\"OK\",\"redirectUrl\":\"https://user@school.example/user/\"}")]
    [InlineData("{\"status\":\"OK\"}")]
    [InlineData("[]")]
    [InlineData("<html>private-server-details</html>")]
    public async Task StopsOnFailureChallengeOrUnexpectedNavigation(string response)
    {
        using var handler = new ScriptedHandler { LoginResponse = response };
        using var http = new HttpClient(handler);
        var exception = await Assert.ThrowsAsync<ProtocolException>(() =>
            new LoginProtocol(http, new Uri("https://school.example/"))
                .ConnectAsync("parent@example.invalid", "synthetic-password"));
        Assert.Equal(3, handler.Calls);
        Assert.DoesNotContain("private-server-details", exception.ToString());
        Assert.DoesNotContain("synthetic-password", exception.ToString());
    }

    [Fact]
    public async Task CancellationStopsBeforeNetworkExchange()
    {
        using var handler = new ScriptedHandler();
        using var http = new HttpClient(handler);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new LoginProtocol(http, new Uri("https://school.example/"))
                .ConnectAsync("parent@example.invalid", "synthetic-password", cancellation.Token));
        Assert.Equal(0, handler.Calls);
    }

    private sealed class ScriptedHandler : HttpMessageHandler
    {
        internal int Calls { get; private set; }
        internal List<string> Actions { get; } = [];
        internal string LoginResponse { get; init; } = """
            {"status":"OK","need2fa":null,"redirectUrl":"/user/","canRemember":true}
            """;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls++;
            Assert.Equal("school.example", request.RequestUri!.Host);
            if (Calls == 1)
            {
                Assert.Equal(HttpMethod.Get, request.Method);
                Assert.Equal("/login/", request.RequestUri.AbsolutePath);
                return Reply("<html>Login</html>");
            }
            if (Calls == 4)
            {
                Assert.Equal(HttpMethod.Get, request.Method);
                Assert.Equal("/user/", request.RequestUri.AbsolutePath);
                return Reply("""
                    <script>x.userhome({"parentStudentids":[-101],"dbi":{"students":{
                      "-101":{"id":"-101","firstname":"Ada","lastname":"Example"}
                    }}});</script>
                    """);
            }
            Assert.Equal(HttpMethod.Post, request.Method);
            var expectedAction = Calls == 2 ? "getToken" : "login";
            Assert.Contains("akcia=" + expectedAction + "&", request.RequestUri.Query);
            Actions.Add(expectedAction);
            var form = await request.Content!.ReadAsStringAsync(cancellationToken);
            var fields = form.Split('&').Select(x => x.Split('=', 2)).ToDictionary(x => x[0], x => WebUtility.UrlDecode(x[1]));
            using var input = new MemoryStream(Convert.FromBase64String(fields["eqap"][3..]));
            using var deflate = new DeflateStream(input, CompressionMode.Decompress);
            using var reader = new StreamReader(deflate, Encoding.UTF8);
            var inner = await reader.ReadToEndAsync(cancellationToken);
            Assert.StartsWith("rpcparams=", inner);
            using var json = JsonDocument.Parse(WebUtility.UrlDecode(inner[10..]));
            Assert.Equal("parent@example.invalid", json.RootElement.GetProperty("username").GetString());
            if (Calls == 3)
            {
                Assert.Equal("synthetic-token", json.RootElement.GetProperty("userToken").GetString());
                Assert.Equal("synthetic-password", json.RootElement.GetProperty("password").GetString());
            }
            return Reply("eqz:" + Convert.ToBase64String(Encoding.UTF8.GetBytes(
                Calls == 2 ? "{\"token\":\"synthetic-token\"}" : LoginResponse)));
        }

        private static HttpResponseMessage Reply(string body) => new(HttpStatusCode.OK)
        {
            Content = new StringContent(body)
        };
    }
}
