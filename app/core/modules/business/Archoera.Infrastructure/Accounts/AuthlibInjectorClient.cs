using System.Diagnostics.CodeAnalysis;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Archoera.Application.Accounts;

namespace Archoera.Infrastructure.Accounts;

/// <summary>Metadata an authlib-injector server publishes at its root URL.</summary>
internal sealed record AuthlibServerMetadata(
    string ServerUrl,
    string ApiRoot,
    string ServerName,
    string? Homepage,
    string RawJson);

/// <summary>Yggdrasil login result; tokens stay inside C#.</summary>
internal sealed record AuthlibSession(
    string AccessToken,
    string ClientToken,
    string Uuid,
    string Name,
    string? SkinUrl);

/// <summary>
/// Minimal Yggdrasil client for authlib-injector servers: metadata discovery,
/// authenticate, refresh and profile texture lookup. Every response is bounded;
/// passwords/tokens are never logged.
/// </summary>
internal sealed class AuthlibInjectorClient
{
    private const int MaxJsonBytes = 1024 * 1024;
    private static readonly HttpClient SharedHttp = new()
    {
        Timeout = TimeSpan.FromSeconds(45),
    };

    private readonly HttpClient _http;

    public AuthlibInjectorClient(HttpClient? http = null)
    {
        _http = http ?? SharedHttp;
    }

    public async Task<AuthlibServerMetadata> ResolveAsync(
        string server,
        CancellationToken cancellationToken)
    {
        var serverUri = ValidateServer(server);
        var body = await SendAsync(
                HttpMethod.Get,
                serverUri,
                payload: null,
                cancellationToken,
                "AUTHLIB_METADATA_FAILED")
            .ConfigureAwait(false);

        string apiRoot = serverUri.AbsoluteUri.TrimEnd('/');
        if (_lastApiLocation is { } location)
        {
            apiRoot = ResolveApiRoot(serverUri, location);
            _lastApiLocation = null;
        }

        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;
        var meta = root.ValueKind == JsonValueKind.Object &&
                   root.TryGetProperty("meta", out var metaElement) &&
                   metaElement.ValueKind == JsonValueKind.Object
            ? metaElement
            : default;
        var name = meta.ValueKind == JsonValueKind.Object
            ? String(meta, "serverName") ?? serverUri.Host
            : serverUri.Host;
        var homepage = meta.ValueKind == JsonValueKind.Object &&
                       meta.TryGetProperty("links", out var links) &&
                       links.ValueKind == JsonValueKind.Object
            ? String(links, "homepage")
            : null;

        return new AuthlibServerMetadata(
            serverUri.AbsoluteUri.TrimEnd('/'),
            apiRoot,
            string.IsNullOrWhiteSpace(name) ? serverUri.Host : name,
            homepage,
            body);
    }

    public async Task<AuthlibSession> AuthenticateAsync(
        AuthlibServerMetadata metadata,
        string username,
        string password,
        CancellationToken cancellationToken)
    {
        var clientToken = Guid.NewGuid().ToString("N");
        var payload = AuthenticationPayload(username, password, clientToken);
        var body = await SendAsync(
                HttpMethod.Post,
                new Uri($"{metadata.ApiRoot}/authserver/authenticate"),
                payload,
                cancellationToken,
                "AUTHLIB_LOGIN_FAILED")
            .ConfigureAwait(false);
        return await ParseSessionAsync(body, clientToken, metadata, null, null, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<AuthlibSession> RefreshAsync(
        AuthlibServerMetadata metadata,
        string accessToken,
        string clientToken,
        string fallbackUuid,
        string fallbackName,
        CancellationToken cancellationToken)
    {
        var payload = TokenPayload(accessToken, clientToken);
        var body = await SendAsync(
                HttpMethod.Post,
                new Uri($"{metadata.ApiRoot}/authserver/refresh"),
                payload,
                cancellationToken,
                "AUTHLIB_REFRESH_FAILED")
            .ConfigureAwait(false);
        return await ParseSessionAsync(
                body,
                clientToken,
                metadata,
                accessToken,
                (fallbackUuid, fallbackName),
                cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<bool> ValidateAsync(
        AuthlibServerMetadata metadata,
        string accessToken,
        string clientToken,
        CancellationToken cancellationToken)
    {
        try
        {
            var payload = TokenPayload(accessToken, clientToken);
            using var response = await SendMessageAsync(
                    HttpMethod.Post,
                    new Uri($"{metadata.ApiRoot}/authserver/validate"),
                    payload,
                    cancellationToken)
                .ConfigureAwait(false);
            return response.IsSuccessStatusCode;
        }
        catch (AccountAuthException)
        {
            return false;
        }
    }

    private static string AuthenticationPayload(
        string username,
        string password,
        string clientToken)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("username", username);
            writer.WriteString("password", password);
            writer.WriteString("clientToken", clientToken);
            writer.WriteStartObject("agent");
            writer.WriteString("name", "Minecraft");
            writer.WriteNumber("version", 1);
            writer.WriteEndObject();
            writer.WriteBoolean("requestUser", false);
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static string TokenPayload(string accessToken, string clientToken)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("accessToken", accessToken);
            writer.WriteString("clientToken", clientToken);
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private async Task<AuthlibSession> ParseSessionAsync(
        string body,
        string fallbackClientToken,
        AuthlibServerMetadata metadata,
        string? fallbackAccessToken,
        (string Uuid, string Name)? fallbackProfile,
        CancellationToken cancellationToken)
    {
        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new AccountAuthException(
                "AUTHLIB_METADATA_INVALID",
                "The authlib server returned an unexpected login response.");
        }

        var accessToken = String(root, "accessToken") ?? fallbackAccessToken;
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            throw new AccountAuthException(
                "AUTHLIB_TOKEN_MISSING",
                "The authlib server returned no access token.");
        }

        var clientToken = String(root, "clientToken") ?? fallbackClientToken;
        string? uuid = null;
        string? name = null;
        if (root.TryGetProperty("selectedProfile", out var selected) &&
            selected.ValueKind == JsonValueKind.Object)
        {
            uuid = String(selected, "id");
            name = String(selected, "name");
        }
        else if (root.TryGetProperty("availableProfiles", out var available) &&
                 available.ValueKind == JsonValueKind.Array &&
                 available.GetArrayLength() > 0 &&
                 available[0].ValueKind == JsonValueKind.Object)
        {
            uuid = String(available[0], "id");
            name = String(available[0], "name");
        }

        if (string.IsNullOrWhiteSpace(uuid))
        {
            uuid = fallbackProfile?.Uuid;
            name = fallbackProfile?.Name;
        }

        if (string.IsNullOrWhiteSpace(uuid) || string.IsNullOrWhiteSpace(name))
        {
            throw new AccountAuthException(
                "AUTHLIB_NO_PROFILE",
                "The authlib account has no selected Minecraft profile.");
        }

        var hyphenated = Hyphenate(uuid);
        var skin = await TryGetSkinAsync(metadata, uuid, cancellationToken)
            .ConfigureAwait(false);
        return new AuthlibSession(
            accessToken,
            clientToken,
            hyphenated,
            name,
            skin);
    }

    private async Task<string?> TryGetSkinAsync(
        AuthlibServerMetadata metadata,
        string compactUuid,
        CancellationToken cancellationToken)
    {
        try
        {
            var body = await SendAsync(
                    HttpMethod.Get,
                    new Uri(
                        $"{metadata.ApiRoot}/sessionserver/session/minecraft/profile/{compactUuid}"),
                    payload: null,
                    cancellationToken,
                    "AUTHLIB_PROFILE_FAILED")
                .ConfigureAwait(false);
            using var document = JsonDocument.Parse(body);
            if (!document.RootElement.TryGetProperty("properties", out var properties) ||
                properties.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            foreach (var property in properties.EnumerateArray())
            {
                if (property.ValueKind != JsonValueKind.Object ||
                    !string.Equals(String(property, "name"), "textures", StringComparison.Ordinal))
                {
                    continue;
                }

                var encoded = String(property, "value");
                if (string.IsNullOrWhiteSpace(encoded)) continue;
                var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(encoded));
                using var textures = JsonDocument.Parse(decoded);
                if (textures.RootElement.TryGetProperty("textures", out var outer) &&
                    outer.TryGetProperty("SKIN", out var skin) &&
                    skin.TryGetProperty("url", out var url) &&
                    url.ValueKind == JsonValueKind.String)
                {
                    return url.GetString();
                }
            }
        }
        catch (Exception exception) when (
            exception is AccountAuthException or JsonException or FormatException or
            InvalidOperationException)
        {
            // Skin lookup is best-effort; login must not fail for a texture.
        }

        return null;
    }

    private string? _lastApiLocation;

    private async Task<string> SendAsync(
        HttpMethod method,
        Uri uri,
        string? payload,
        CancellationToken cancellationToken,
        string fallbackCode)
    {
        using var response = await SendMessageAsync(method, uri, payload, cancellationToken)
            .ConfigureAwait(false);
        var body = await ReadBoundedAsync(response, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            var code = response.StatusCode is System.Net.HttpStatusCode.Unauthorized or
                System.Net.HttpStatusCode.Forbidden
                ? "AUTHLIB_INVALID_CREDENTIALS"
                : fallbackCode;
            throw new AccountAuthException(
                code,
                $"The authlib server returned {(int)response.StatusCode}.");
        }

        return body;
    }

    private async Task<HttpResponseMessage> SendMessageAsync(
        HttpMethod method,
        Uri uri,
        string? payload,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, uri);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.UserAgent.ParseAdd("ArchoeraCX");
        if (payload is not null)
        {
            request.Content = new StringContent(payload, Encoding.UTF8, "application/json");
        }

        HttpResponseMessage response;
        try
        {
            response = await _http
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new AccountAuthException(
                "AUTHLIB_NETWORK",
                "The authlib server did not answer in time.");
        }
        catch (HttpRequestException exception)
        {
            throw new AccountAuthException(
                "AUTHLIB_NETWORK",
                "Cannot reach the authlib server.",
                exception.Message);
        }

        _lastApiLocation = response.Headers.TryGetValues(
            "X-Authlib-Injector-API-Location",
            out var values)
            ? values.FirstOrDefault()
            : null;
        return response;
    }

    private static async Task<string> ReadBoundedAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        await using var stream = await response.Content
            .ReadAsStreamAsync(cancellationToken)
            .ConfigureAwait(false);
        using var memory = new MemoryStream();
        var buffer = new byte[16 * 1024];
        while (true)
        {
            var read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read == 0) break;
            if (memory.Length + read > MaxJsonBytes)
            {
                throw new AccountAuthException(
                    "AUTHLIB_METADATA_INVALID",
                    "The authlib server response is too large.");
            }

            memory.Write(buffer, 0, read);
        }

        return Encoding.UTF8.GetString(memory.ToArray());
    }

    private static Uri ValidateServer(string server)
    {
        if (!Uri.TryCreate(server.Trim(), UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttps &&
             !(uri.Scheme == Uri.UriSchemeHttp && IsLoopback(uri.Host))) ||
            uri.UserInfo.Length > 0 ||
            !string.IsNullOrEmpty(uri.Query) ||
            !string.IsNullOrEmpty(uri.Fragment))
        {
            throw new AccountAuthException(
                "AUTHLIB_SERVER_INVALID",
                "Enter an https authlib server URL (http is allowed for localhost).");
        }

        return new Uri(uri.AbsoluteUri.TrimEnd('/'));
    }

    private static string ResolveApiRoot(Uri server, string location)
    {
        var resolved = Uri.TryCreate(location, UriKind.Absolute, out var absolute)
            ? absolute
            : new Uri(server, location);
        if ((resolved.Scheme != Uri.UriSchemeHttps &&
             !(resolved.Scheme == Uri.UriSchemeHttp && IsLoopback(resolved.Host))) ||
            resolved.UserInfo.Length > 0 ||
            !string.IsNullOrEmpty(resolved.Query) ||
            !string.IsNullOrEmpty(resolved.Fragment))
        {
            throw new AccountAuthException(
                "AUTHLIB_SERVER_INVALID",
                "The authlib server advertised an unsafe API location.");
        }

        return resolved.AbsoluteUri.TrimEnd('/');
    }

    [SuppressMessage(
        "Design",
        "CA1031",
        Justification = "Loopback includes IPv4, IPv6 and localhost.")]
    private static bool IsLoopback(string host)
        => string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase) ||
           System.Net.IPAddress.TryParse(host, out var address) &&
           System.Net.IPAddress.IsLoopback(address);

    private static string Hyphenate(string compact)
        => Guid.TryParseExact(compact, "N", out var parsed)
            ? parsed.ToString("D")
            : compact;

    private static string? String(JsonElement root, string name)
        => root.TryGetProperty(name, out var element) &&
           element.ValueKind == JsonValueKind.String
            ? element.GetString()
            : null;
}
