using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using Archoera.Application.Accounts;
using Archoera.Infrastructure.Accounts;

namespace Archoera.Tests;

public sealed class OAuthAuthorizationCodeTests
{
    [Fact]
    public void Authorization_challenge_contains_pkce_state_redirect_and_scopes()
    {
        var auth = new MicrosoftAuthClient(
            new HttpClient(new FakeHttpMessageHandler()),
            TestOptions());

        var redirectUri = "http://localhost:54321/callback";
        var challenge = auth.CreateAuthorizationCodeChallenge(redirectUri);
        var url = challenge.AuthorizationUrl;

        Assert.Equal(redirectUri, challenge.RedirectUri);
        Assert.False(string.IsNullOrWhiteSpace(challenge.FlowId));
        Assert.False(string.IsNullOrWhiteSpace(challenge.State));
        Assert.InRange(challenge.CodeVerifier.Length, 43, 96);
        Assert.Matches("^[A-Za-z0-9_-]+$", challenge.CodeVerifier);
        Assert.InRange(
            challenge.ExpiresAt,
            DateTimeOffset.UtcNow.AddMinutes(4),
            DateTimeOffset.UtcNow.AddMinutes(6));

        var expectedChallenge = Base64UrlEncode(
            SHA256.HashData(Encoding.ASCII.GetBytes(challenge.CodeVerifier)));
        Assert.StartsWith(
            "https://login.test/tenant/oauth2/v2.0/authorize?",
            url,
            StringComparison.Ordinal);
        Assert.Contains("response_type=code", url, StringComparison.Ordinal);
        Assert.Contains(
            "redirect_uri=http%3A%2F%2Flocalhost%3A54321%2Fcallback",
            url,
            StringComparison.Ordinal);
        Assert.Contains("response_mode=query", url, StringComparison.Ordinal);
        Assert.Contains(
            "scope=XboxLive.signin%20offline_access",
            url,
            StringComparison.Ordinal);
        Assert.Contains($"state={challenge.State}", url, StringComparison.Ordinal);
        Assert.Contains($"code_challenge={expectedChallenge}", url, StringComparison.Ordinal);
        Assert.Contains("code_challenge_method=S256", url, StringComparison.Ordinal);
        Assert.Contains("prompt=select_account", url, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Complete_authorization_code_posts_pkce_fields_and_returns_tokens()
    {
        var fake = new FakeHttpMessageHandler();
        fake.EnqueueJson(
            """{"access_token":"ms-access","refresh_token":"ms-refresh","expires_in":3600,"scope":"XboxLive.signin offline_access","token_type":"Bearer"}""");
        using var recorder = new RecordingHandler(fake);
        var auth = new MicrosoftAuthClient(new HttpClient(recorder), TestOptions());
        var challenge = auth.CreateAuthorizationCodeChallenge(
            "http://localhost:54321/callback");

        var tokens = await auth.CompleteAuthorizationCodeAsync(
            challenge,
            "the-authorization-code",
            CancellationToken.None);

        Assert.Equal("ms-access", tokens.AccessToken);
        Assert.Equal("ms-refresh", tokens.RefreshToken);
        Assert.Equal("XboxLive.signin offline_access", tokens.Scope);

        var request = Assert.Single(fake.Requests);
        Assert.Equal("POST", request.Method);
        Assert.Equal("https://login.test/tenant/oauth2/v2.0/token", request.Uri.ToString());

        var form = ParseForm(Assert.IsType<string>(recorder.Body));
        Assert.Equal("authorization_code", form["grant_type"]);
        Assert.Equal("test-client", form["client_id"]);
        Assert.Equal("the-authorization-code", form["code"]);
        Assert.Equal(challenge.RedirectUri, form["redirect_uri"]);
        Assert.Equal(challenge.CodeVerifier, form["code_verifier"]);
    }

    [Fact]
    public async Task Loopback_listener_receives_callback_on_loopback_and_parses_values()
    {
        using var listener = new LoopbackOAuthListener();
        Assert.Equal($"http://localhost:{listener.Port}/callback", listener.RedirectUri);

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var pending = listener.WaitForCallbackAsync(timeout.Token);

        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, listener.Port, timeout.Token);
        var stream = client.GetStream();
        const string request =
            "GET /callback?code=abc%2Bdef&state=state-123&error=access_denied&error_description=user%20cancelled HTTP/1.1\r\n" +
            "Host: localhost\r\n" +
            "Connection: close\r\n\r\n";
        await stream.WriteAsync(Encoding.ASCII.GetBytes(request), timeout.Token);

        var callback = await pending;
        Assert.Equal("abc+def", callback.Code);
        Assert.Equal("state-123", callback.State);
        Assert.Equal("access_denied", callback.Error);
        Assert.Equal("user cancelled", callback.ErrorDescription);

        using var reader = new StreamReader(stream, Encoding.ASCII);
        var response = await reader.ReadToEndAsync(timeout.Token);
        Assert.StartsWith("HTTP/1.1 200 OK", response, StringComparison.Ordinal);
        Assert.Contains("Sign-in complete", response, StringComparison.Ordinal);
    }

    private static MicrosoftAuthOptions TestOptions()
        => new()
        {
            ClientId = "test-client",
            LoginBase = new Uri("https://login.test/tenant/oauth2/v2.0/"),
        };

    private static Dictionary<string, string> ParseForm(string body)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in body.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = pair.IndexOf('=');
            var name = separator < 0 ? pair : pair[..separator];
            var value = separator < 0 ? string.Empty : pair[(separator + 1)..];
            values[Uri.UnescapeDataString(name.Replace('+', ' '))] =
                Uri.UnescapeDataString(value.Replace('+', ' '));
        }

        return values;
    }

    private static string Base64UrlEncode(ReadOnlySpan<byte> bytes)
        => Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

    /// <summary>Captures the form body while delegating the actual response to the fake transport.</summary>
    private sealed class RecordingHandler : DelegatingHandler
    {
        public RecordingHandler(HttpMessageHandler innerHandler)
            : base(innerHandler)
        {
        }

        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Body = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);
            return await base.SendAsync(request, cancellationToken);
        }
    }
}
