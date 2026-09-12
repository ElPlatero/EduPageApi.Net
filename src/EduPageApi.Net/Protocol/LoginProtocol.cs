using System.Text.Json;

namespace EduPageApi.Protocol;

internal sealed class LoginProtocol(HttpClient http, Uri schoolOrigin)
{
    internal static HttpClient CreateSessionClient() => new(new HttpClientHandler
    {
        UseCookies = true,
        CookieContainer = new System.Net.CookieContainer(),
        AllowAutoRedirect = false
    });

    // Production callers must use a session-specific CookieContainer and disable automatic redirects.
    // The caller owns HttpClient; no credentials or response objects are retained here.
    internal async Task<IReadOnlyList<ObservedChild>> ConnectAsync(
        string username, string password, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(username);
        ArgumentException.ThrowIfNullOrEmpty(password);
        if (schoolOrigin.Scheme != Uri.UriSchemeHttps || !string.IsNullOrEmpty(schoolOrigin.UserInfo)
            || schoolOrigin.AbsolutePath != "/" || !string.IsNullOrEmpty(schoolOrigin.Query)
            || !string.IsNullOrEmpty(schoolOrigin.Fragment))
            throw new ArgumentException("An HTTPS school origin is required.", nameof(schoolOrigin));

        // Bootstrap is inferred from the browser flow; the new HAR starts after this GET.
        await GetAsync(new Uri(schoolOrigin, "login/"), cancellationToken);

        using var tokenResponse = await RpcAsync("getToken", new { username, edupage = "" }, cancellationToken);
        RequireNoChallenge(tokenResponse.RootElement);
        if (!tokenResponse.RootElement.TryGetProperty("token", out var token)
            || token.ValueKind != JsonValueKind.String || string.IsNullOrEmpty(token.GetString()))
            throw new ProtocolException("The login token was not provided.");

        using var loginResponse = await RpcAsync("login", new
        {
            username, password, userToken = token.GetString(), edupage = "", ctxt = "",
            tu = (string?)null, gu = (string?)null, au = (string?)null
        }, cancellationToken);
        var result = loginResponse.RootElement;
        RequireNoChallenge(result);
        if (!result.TryGetProperty("status", out var status) || status.ValueKind != JsonValueKind.String
            || status.GetString() != "OK")
            throw new ProtocolException("The login was not successful.");

        if (!result.TryGetProperty("redirectUrl", out var location) || location.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(location.GetString())
            || !Uri.TryCreate(schoolOrigin, location.GetString(), out var target)
            || target.Scheme != schoolOrigin.Scheme || target.Host != schoolOrigin.Host
            || target.Port != schoolOrigin.Port || !string.IsNullOrEmpty(target.UserInfo))
            throw new ProtocolException("The login returned an unsupported navigation target.");

        // Do not call rememberUser or manufacture a cookie from the JSON session field.
        var home = await GetAsync(target, cancellationToken);
        return ParentPageParser.Parse(home);
    }

    private async Task<JsonDocument> RpcAsync(string action, object parameters, CancellationToken cancellationToken)
    {
        using var inner = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["rpcparams"] = JsonSerializer.Serialize(parameters)
        });
        var envelope = EnvelopeCodec.Encode(await inner.ReadAsStringAsync(cancellationToken));
        using var request = new HttpRequestMessage(HttpMethod.Post,
            new Uri(schoolOrigin, "login/?cmd=MainLogin&akcia=" + action + "&eqav=1&maxEqav=7"));
        request.Headers.Add("X-Requested-With", "XMLHttpRequest");
        request.Content = new FormUrlEncodedContent(envelope);
        var responseText = await SendAsync(request, cancellationToken);
        try
        {
            var document = JsonDocument.Parse(EnvelopeCodec.Decode(responseText));
            if (document.RootElement.ValueKind == JsonValueKind.Object)
                return document;
            document.Dispose();
            throw new ProtocolException("The login response is not an object.");
        }
        catch (JsonException)
        {
            throw new ProtocolException("The login response is not valid JSON.");
        }
    }

    private async Task<string> GetAsync(Uri uri, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        return await SendAsync(request, cancellationToken);
    }

    private async Task<string> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        using var response = await http.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new ProtocolException("The login HTTP request was unsuccessful.");
        return await response.Content.ReadAsStringAsync(cancellationToken);
    }

    private static void RequireNoChallenge(JsonElement result)
    {
        if (result.TryGetProperty("need2fa", out var challenge)
            && challenge.ValueKind is not (JsonValueKind.Null or JsonValueKind.False))
            throw new ProtocolException("The login requires an additional authentication step.");
    }
}
