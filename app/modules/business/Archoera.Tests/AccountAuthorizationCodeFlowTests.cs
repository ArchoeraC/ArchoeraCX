using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Archoera.Infrastructure.Accounts;

namespace Archoera.Tests;

/// <summary>
/// Focused coverage for the sign-in flow selector. The authorization-code
/// exchange itself is covered by the auth primitives and their own tests.
/// </summary>
public sealed class AccountAuthorizationCodeFlowTests
{
    [Fact]
    public async Task Device_code_start_identifies_the_flow_mode()
    {
        var directory = TempDirectory();
        try
        {
            var handler = new FakeHttpMessageHandler();
            handler.EnqueueJson(
                """{"device_code":"device-secret","user_code":"ABCD-EFGH","verification_uri":"https://microsoft.com/devicelogin","expires_in":900,"interval":5}""");
            var store = new JsonAccountStore(directory);
            var auth = new MicrosoftAuthClient(new HttpClient(handler), Options());
            var service = new AccountModuleService(store, store, auth);

            var result = await service.HandleAsync(
                "account.login.microsoft.start",
                "{}",
                CancellationToken.None);

            var text = Encoding.UTF8.GetString(result);
            Assert.DoesNotContain("device-secret", text, StringComparison.Ordinal);
            using var document = JsonDocument.Parse(result);
            var data = document.RootElement.GetProperty("data");
            Assert.Equal("device_code", data.GetProperty("mode").GetString());
            Assert.False(string.IsNullOrWhiteSpace(data.GetProperty("flowId").GetString()));
        }
        finally
        {
            Delete(directory);
        }
    }

    [Fact]
    public async Task Unknown_flow_is_rejected_before_calling_microsoft()
    {
        var directory = TempDirectory();
        try
        {
            var handler = new FakeHttpMessageHandler();
            var store = new JsonAccountStore(directory);
            var auth = new MicrosoftAuthClient(new HttpClient(handler), Options());
            var service = new AccountModuleService(store, store, auth);

            var result = await service.HandleAsync(
                "account.login.microsoft.start",
                """{"flow":"implicit"}""",
                CancellationToken.None);

            using var document = JsonDocument.Parse(result);
            Assert.False(document.RootElement.GetProperty("ok").GetBoolean());
            Assert.Equal(
                "E_INVALID",
                document.RootElement.GetProperty("error").GetProperty("code").GetString());
            Assert.Empty(handler.Requests);
        }
        finally
        {
            Delete(directory);
        }
    }

    [Fact]
    public async Task Authorization_code_start_returns_loopback_details_without_secrets()
    {
        var directory = TempDirectory();
        try
        {
            var store = new JsonAccountStore(directory);
            var auth = new MicrosoftAuthClient(
                new HttpClient(new FakeHttpMessageHandler()),
                Options());
            var service = new AccountModuleService(store, store, auth);

            var result = await service.HandleAsync(
                "account.login.microsoft.start",
                """{"flow":"authorization_code"}""",
                CancellationToken.None);

            var text = Encoding.UTF8.GetString(result);
            Assert.DoesNotContain("codeVerifier", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("device_code", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("device-secret", text, StringComparison.Ordinal);

            using var document = JsonDocument.Parse(result);
            var data = document.RootElement.GetProperty("data");
            Assert.Equal("authorization_code", data.GetProperty("mode").GetString());
            Assert.False(string.IsNullOrWhiteSpace(data.GetProperty("flowId").GetString()));
            Assert.False(string.IsNullOrWhiteSpace(data.GetProperty("authorizationUrl").GetString()));
            Assert.False(string.IsNullOrWhiteSpace(data.GetProperty("redirectUri").GetString()));
            Assert.Equal(300, data.GetProperty("expiresIn").GetInt32());
            Assert.True(DateTimeOffset.TryParse(
                data.GetProperty("expiresAt").GetString(),
                out _));
            Assert.False(data.TryGetProperty("codeVerifier", out _));
            Assert.False(data.TryGetProperty("state", out _));

            var flowId = data.GetProperty("flowId").GetString();
            Assert.NotNull(flowId);
            var cancelled = await service.HandleAsync(
                "account.login.microsoft.cancel",
                $$"""{"flowId":"{{flowId}}"}""",
                CancellationToken.None);
            using var cancelledDocument = JsonDocument.Parse(cancelled);
            Assert.True(
                cancelledDocument.RootElement
                    .GetProperty("data")
                    .GetProperty("cancelled")
                    .GetBoolean());
        }
        finally
        {
            Delete(directory);
        }
    }

    [Fact]
    public async Task Authorization_code_poll_rejects_a_mismatched_state()
    {
        var directory = TempDirectory();
        try
        {
            var store = new JsonAccountStore(directory);
            var auth = new MicrosoftAuthClient(
                new HttpClient(new FakeHttpMessageHandler()),
                Options());
            var service = new AccountModuleService(store, store, auth);

            var start = await service.HandleAsync(
                "account.login.microsoft.start",
                """{"flow":"authorization_code"}""",
                CancellationToken.None);
            using var startDocument = JsonDocument.Parse(start);
            var data = startDocument.RootElement.GetProperty("data");
            var flowId = data.GetProperty("flowId").GetString();
            var redirectUri = data.GetProperty("redirectUri").GetString();
            Assert.NotNull(flowId);
            Assert.NotNull(redirectUri);

            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var pollTask = service.HandleAsync(
                "account.login.microsoft.poll",
                $$"""{"flowId":"{{flowId}}"}""",
                CancellationToken.None);
            await SendCallbackAsync(
                new Uri(redirectUri!),
                "code=test-code&state=wrong-state",
                timeout.Token);
            var poll = await pollTask.WaitAsync(timeout.Token);

            using var pollDocument = JsonDocument.Parse(poll);
            Assert.False(pollDocument.RootElement.GetProperty("ok").GetBoolean());
            Assert.Equal(
                "AUTH_STATE_MISMATCH",
                pollDocument.RootElement.GetProperty("error").GetProperty("code").GetString());
            Assert.Empty(await store.ListAsync());
        }
        finally
        {
            Delete(directory);
        }
    }

    [Fact]
    public async Task Authorization_code_poll_maps_a_declined_callback()
    {
        var directory = TempDirectory();
        try
        {
            var store = new JsonAccountStore(directory);
            var auth = new MicrosoftAuthClient(
                new HttpClient(new FakeHttpMessageHandler()),
                Options());
            var service = new AccountModuleService(store, store, auth);

            var start = await service.HandleAsync(
                "account.login.microsoft.start",
                """{"flow":"authorization_code"}""",
                CancellationToken.None);
            using var startDocument = JsonDocument.Parse(start);
            var data = startDocument.RootElement.GetProperty("data");
            var flowId = data.GetProperty("flowId").GetString();
            var redirectUri = data.GetProperty("redirectUri").GetString();
            var authorizationUrl = data.GetProperty("authorizationUrl").GetString();
            Assert.NotNull(flowId);
            Assert.NotNull(redirectUri);
            Assert.NotNull(authorizationUrl);
            var state = QueryValue(new Uri(authorizationUrl!), "state");
            Assert.False(string.IsNullOrWhiteSpace(state));

            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var pollTask = service.HandleAsync(
                "account.login.microsoft.poll",
                $$"""{"flowId":"{{flowId}}"}""",
                CancellationToken.None);
            await SendCallbackAsync(
                new Uri(redirectUri!),
                $"error=access_denied&error_description=user%20cancelled&state={Uri.EscapeDataString(state)}",
                timeout.Token);
            var poll = await pollTask.WaitAsync(timeout.Token);

            using var pollDocument = JsonDocument.Parse(poll);
            Assert.False(pollDocument.RootElement.GetProperty("ok").GetBoolean());
            Assert.Equal(
                "AUTH_DECLINED",
                pollDocument.RootElement.GetProperty("error").GetProperty("code").GetString());
            Assert.Empty(await store.ListAsync());
        }
        finally
        {
            Delete(directory);
        }
    }

    private static async Task SendCallbackAsync(
        Uri redirectUri,
        string query,
        CancellationToken cancellationToken)
    {
        using var client = new TcpClient();
        await client.ConnectAsync(
            IPAddress.Loopback,
            redirectUri.Port,
            cancellationToken);
        var stream = client.GetStream();
        var request =
            $"GET {redirectUri.AbsolutePath}?{query} HTTP/1.1\r\n" +
            "Host: localhost\r\n" +
            "Connection: close\r\n\r\n";
        await stream.WriteAsync(Encoding.ASCII.GetBytes(request), cancellationToken);
        using var reader = new StreamReader(stream, Encoding.ASCII);
        await reader.ReadToEndAsync(cancellationToken);
    }

    private static string QueryValue(Uri uri, string name)
    {
        foreach (var pair in uri.Query.TrimStart('?')
                     .Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = pair.IndexOf('=');
            var key = separator < 0 ? pair : pair[..separator];
            if (!string.Equals(Uri.UnescapeDataString(key), name, StringComparison.Ordinal))
            {
                continue;
            }

            return separator < 0
                ? string.Empty
                : Uri.UnescapeDataString(pair[(separator + 1)..]);
        }

        return string.Empty;
    }

    private static MicrosoftAuthOptions Options()
        => new()
        {
            ClientId = "test-client",
            LoginBase = new Uri("https://login.test/tenant/oauth2/v2.0/"),
        };

    private static string TempDirectory()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            "archoera-authcode-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void Delete(string path)
    {
        try
        {
            if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
        }
        catch
        {
            // best effort
        }
    }
}
