using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Archoera.Application.Accounts;
using Archoera.Infrastructure.Build;

namespace Archoera.Infrastructure.Accounts;

/// <summary>
/// Microsoft identity + Xbox Live + Minecraft Services authentication.
///
/// The client never logs tokens or device codes, never returns Microsoft
/// tokens to the UI layer, and keeps the device-code secret inside C#. The
/// caller owns persistence and account metadata.
/// </summary>
public sealed class MicrosoftAuthClient
{
    public const string Scope = "XboxLive.signin offline_access";
    public const string DefaultTenant = "consumers";

    private static readonly MediaTypeWithQualityHeaderValue AcceptJson =
        new("application/json");

    private readonly HttpClient _http;
    private readonly MicrosoftAuthOptions _options;

    public MicrosoftAuthClient(HttpClient http, MicrosoftAuthOptions options)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _options = (options ?? throw new ArgumentNullException(nameof(options))).Normalize();
    }

    public MicrosoftAuthOptions Options => _options;

    public async Task<MicrosoftDeviceCodeChallenge> StartDeviceCodeAsync(
        CancellationToken cancellationToken)
    {
        EnsureClientId();

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            _options.DeviceCodeEndpoint);
        request.Headers.Accept.Add(AcceptJson);
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = _options.ClientId,
            ["scope"] = _options.Scope,
        });

        using var response = await _http
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw MapMicrosoftError(body, response.StatusCode, "DEVICE_CODE_FAILED");
        }

        using var document = ParseJson(body, "Microsoft device code response is not JSON");
        var root = document.RootElement;
        var deviceCode = String(root, "device_code");
        var userCode = String(root, "user_code");
        var verificationUri = String(root, "verification_uri") ??
                              String(root, "verification_url");
        if (string.IsNullOrWhiteSpace(deviceCode) ||
            string.IsNullOrWhiteSpace(userCode) ||
            string.IsNullOrWhiteSpace(verificationUri))
        {
            throw new AccountAuthException(
                "DEVICE_CODE_INVALID",
                "Microsoft returned an incomplete device code response.");
        }

        return new MicrosoftDeviceCodeChallenge(
            Guid.NewGuid().ToString("N"),
            deviceCode,
            userCode,
            verificationUri,
            Math.Max(60, Int(root, "expires_in") ?? 900),
            Math.Clamp(Int(root, "interval") ?? 5, 1, 60),
            DateTimeOffset.UtcNow);
    }

    /// <summary>
    /// Builds an Authorization Code + PKCE challenge for the loopback redirect.
    /// The verifier and state stay inside C# and are never logged.
    /// </summary>
    public MicrosoftAuthorizationCodeChallenge CreateAuthorizationCodeChallenge(
        string redirectUri)
    {
        EnsureClientId();
        if (string.IsNullOrWhiteSpace(redirectUri) ||
            !Uri.TryCreate(redirectUri, UriKind.Absolute, out var parsedRedirect) ||
            (parsedRedirect.Scheme != Uri.UriSchemeHttp &&
             parsedRedirect.Scheme != Uri.UriSchemeHttps))
        {
            throw new AccountAuthException(
                "OAUTH_REDIRECT_INVALID",
                "The loopback redirect URI is missing or invalid.");
        }

        var normalizedRedirect = parsedRedirect.AbsoluteUri;
        var codeVerifier = RandomUrlSafeToken(32);
        var codeChallenge = Base64UrlEncode(
            SHA256.HashData(Encoding.ASCII.GetBytes(codeVerifier)));
        var state = RandomUrlSafeToken(32);

        var query = new StringBuilder();
        AppendQuery(query, "client_id", _options.ClientId);
        AppendQuery(query, "response_type", "code");
        AppendQuery(query, "redirect_uri", normalizedRedirect);
        AppendQuery(query, "response_mode", "query");
        AppendQuery(query, "scope", _options.Scope);
        AppendQuery(query, "state", state);
        AppendQuery(query, "code_challenge", codeChallenge);
        AppendQuery(query, "code_challenge_method", "S256");
        AppendQuery(query, "prompt", "select_account");

        var authorizationUrl = new UriBuilder(_options.AuthorizationEndpoint)
        {
            Query = query.ToString(),
        }.Uri.AbsoluteUri;

        return new MicrosoftAuthorizationCodeChallenge(
            Guid.NewGuid().ToString("N"),
            authorizationUrl,
            normalizedRedirect,
            codeVerifier,
            state,
            DateTimeOffset.UtcNow.AddMinutes(5));
    }

    /// <summary>
    /// Exchanges an authorization code for Microsoft tokens using the original
    /// PKCE verifier. Tokens and codes are never logged.
    /// </summary>
    public async Task<MicrosoftTokenSet> CompleteAuthorizationCodeAsync(
        MicrosoftAuthorizationCodeChallenge challenge,
        string code,
        CancellationToken cancellationToken)
    {
        EnsureClientId();
        ArgumentNullException.ThrowIfNull(challenge);
        if (string.IsNullOrWhiteSpace(code))
        {
            throw new AccountAuthException(
                "OAUTH_CODE_MISSING",
                "Microsoft returned no authorization code. Sign in again.");
        }

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            _options.TokenEndpoint);
        request.Headers.Accept.Add(AcceptJson);
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["client_id"] = _options.ClientId,
            ["code"] = code,
            ["redirect_uri"] = challenge.RedirectUri,
            ["code_verifier"] = challenge.CodeVerifier,
        });

        using var response = await _http
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw MapMicrosoftError(body, response.StatusCode, "AUTHORIZATION_CODE_FAILED");
        }

        return ParseMicrosoftTokens(body, previousRefreshToken: null);
    }

    /// <summary>
    /// Polls the token endpoint until the user finishes authorization, the code
    /// expires, or the caller cancels. The delay loop is cancellable and never
    /// uses a background timer.
    /// </summary>
    public async Task<MicrosoftTokenSet> WaitForDeviceTokenAsync(
        MicrosoftDeviceCodeChallenge challenge,
        CancellationToken cancellationToken)
    {
        EnsureClientId();
        var interval = TimeSpan.FromSeconds(Math.Max(1, challenge.Interval));
        var expiresAt = challenge.ExpiresAt;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (DateTimeOffset.UtcNow >= expiresAt)
            {
                throw new AccountAuthException(
                    "DEVICE_CODE_EXPIRED",
                    "The Microsoft sign-in code expired. Start again.");
            }

            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                _options.TokenEndpoint);
            request.Headers.Accept.Add(AcceptJson);
            request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "urn:ietf:params:oauth:grant-type:device_code",
                ["client_id"] = _options.ClientId,
                ["device_code"] = challenge.DeviceCode,
            });

            using var response = await _http
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            if (response.IsSuccessStatusCode)
            {
                return ParseMicrosoftTokens(body, previousRefreshToken: null);
            }

            var error = ErrorCode(body);
            switch (error)
            {
                case "authorization_pending":
                    await Task.Delay(interval, cancellationToken).ConfigureAwait(false);
                    continue;
                case "slow_down":
                    interval += TimeSpan.FromSeconds(5);
                    await Task.Delay(interval, cancellationToken).ConfigureAwait(false);
                    continue;
                case "expired_token":
                    throw new AccountAuthException(
                        "DEVICE_CODE_EXPIRED",
                        "The Microsoft sign-in code expired. Start again.");
                case "authorization_declined":
                case "access_denied":
                    throw new AccountAuthException(
                        "AUTH_DECLINED",
                        "Microsoft sign-in was declined.");
                case "bad_verification_code":
                    throw new AccountAuthException(
                        "DEVICE_CODE_INVALID",
                        "Microsoft rejected the sign-in code.");
                default:
                    throw MapMicrosoftError(body, response.StatusCode, "TOKEN_POLL_FAILED");
            }
        }
    }

    public async Task<MicrosoftTokenSet> RefreshMicrosoftAsync(
        string refreshToken,
        CancellationToken cancellationToken)
    {
        EnsureClientId();
        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            throw new AccountAuthException(
                "AUTH_REQUIRED",
                "No Microsoft refresh token is available. Sign in again.");
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, _options.TokenEndpoint);
        request.Headers.Accept.Add(AcceptJson);
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["client_id"] = _options.ClientId,
            ["refresh_token"] = refreshToken,
            ["scope"] = _options.Scope,
        });

        using var response = await _http
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            var error = ErrorCode(body);
            if (error is "invalid_grant" or "interaction_required" or "login_required")
            {
                throw new AccountAuthException(
                    "AUTH_REQUIRED",
                    "Microsoft session expired. Sign in again.",
                    error);
            }

            throw MapMicrosoftError(body, response.StatusCode, "REFRESH_FAILED");
        }

        return ParseMicrosoftTokens(body, refreshToken);
    }

    public async Task<MinecraftAuthenticatedSession> CompleteMinecraftLoginAsync(
        MicrosoftTokenSet microsoftTokens,
        CancellationToken cancellationToken)
    {
        var xbl = await AuthenticateXboxLiveAsync(
                microsoftTokens.AccessToken,
                cancellationToken)
            .ConfigureAwait(false);
        var xsts = await AuthorizeXstsAsync(xbl.Token, cancellationToken).ConfigureAwait(false);
        var minecraft = await LoginWithXboxAsync(
                xsts.Token,
                xsts.UserHash,
                cancellationToken)
            .ConfigureAwait(false);
        await VerifyEntitlementAsync(
                minecraft.AccessToken,
                cancellationToken)
            .ConfigureAwait(false);
        var profile = await GetProfileAsync(
                minecraft.AccessToken,
                cancellationToken)
            .ConfigureAwait(false);
        var certificates = await TryGetCertificatesAsync(
                minecraft.AccessToken,
                cancellationToken)
            .ConfigureAwait(false);

        return new MinecraftAuthenticatedSession(
            minecraft.AccessToken,
            minecraft.ExpiresAt,
            profile.Uuid,
            profile.Name,
            profile.Xuid,
            profile.SkinUrl,
            profile.CapeUrl,
            certificates?.Json,
            certificates?.ExpiresAt);
    }

    /// <summary>
    /// Reads the official Minecraft: Java Edition entitlement list. This is a
    /// read-only ownership check using the token returned by login_with_xbox;
    /// it is not a substitute for the user's Microsoft/Minecraft login.
    /// </summary>
    private async Task VerifyEntitlementAsync(
        string minecraftAccessToken,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            _options.MinecraftEntitlementsEndpoint);
        request.Headers.Authorization =
            new AuthenticationHeaderValue("Bearer", minecraftAccessToken);
        request.Headers.Accept.Add(AcceptJson);

        using var response = await _http
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Unauthorized)
        {
            throw new AccountAuthException(
                "MINECRAFT_ENTITLEMENT_MISSING",
                "This Microsoft account does not own Minecraft: Java Edition.");
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new AccountAuthException(
                "MINECRAFT_ENTITLEMENT_FAILED",
                "Could not verify Minecraft: Java Edition ownership.");
        }

        using var document = ParseJson(body, "Minecraft entitlement response is not JSON");
        if (!document.RootElement.TryGetProperty("items", out var items) ||
            items.ValueKind != JsonValueKind.Array)
        {
            throw new AccountAuthException(
                "MINECRAFT_ENTITLEMENT_INVALID",
                "Minecraft returned an incomplete entitlement response.");
        }

        foreach (var item in items.EnumerateArray())
        {
            var name = String(item, "name");
            if (string.Equals(name, "product_minecraft", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(name, "game_minecraft", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
        }

        throw new AccountAuthException(
            "MINECRAFT_ENTITLEMENT_MISSING",
            "This Microsoft account does not own Minecraft: Java Edition.");
    }

    public async Task<MicrosoftProfile> GetProfileAsync(
        string minecraftAccessToken,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            _options.MinecraftProfileEndpoint);
        request.Headers.Authorization =
            new AuthenticationHeaderValue("Bearer", minecraftAccessToken);
        request.Headers.Accept.Add(AcceptJson);

        using var response = await _http
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            throw new AccountAuthException(
                "MINECRAFT_PROFILE_NOT_FOUND",
                "This Microsoft account does not own Minecraft: Java Edition.");
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new AccountAuthException(
                "MINECRAFT_PROFILE_FAILED",
                response.StatusCode == HttpStatusCode.Unauthorized
                    ? "Minecraft session was rejected. Sign in again."
                    : "Could not read the Minecraft profile.");
        }

        using var document = ParseJson(body, "Minecraft profile response is not JSON");
        var root = document.RootElement;
        var uuid = String(root, "id");
        var name = String(root, "name");
        if (string.IsNullOrWhiteSpace(uuid) || string.IsNullOrWhiteSpace(name))
        {
            throw new AccountAuthException(
                "MINECRAFT_PROFILE_INVALID",
                "Minecraft returned an incomplete profile.");
        }

        string? skinUrl = null;
        string? capeUrl = null;
        if (root.TryGetProperty("skins", out var skins) && skins.ValueKind == JsonValueKind.Array)
        {
            foreach (var skin in skins.EnumerateArray())
            {
                skinUrl ??= String(skin, "url");
                if (string.Equals(String(skin, "state"), "ACTIVE", StringComparison.OrdinalIgnoreCase))
                {
                    skinUrl = String(skin, "url") ?? skinUrl;
                    break;
                }
            }
        }

        if (root.TryGetProperty("capes", out var capes) && capes.ValueKind == JsonValueKind.Array)
        {
            foreach (var cape in capes.EnumerateArray())
            {
                capeUrl ??= String(cape, "url");
                if (String(cape, "state") is null ||
                    string.Equals(String(cape, "state"), "ACTIVE", StringComparison.OrdinalIgnoreCase))
                {
                    capeUrl = String(cape, "url") ?? capeUrl;
                }
            }
        }

        return new MicrosoftProfile(uuid, name, String(root, "xuid"), skinUrl, capeUrl);
    }

    private async Task<XboxLiveToken> AuthenticateXboxLiveAsync(
        string microsoftAccessToken,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, _options.XboxLiveEndpoint);
        request.Headers.Accept.Add(AcceptJson);
        request.Headers.TryAddWithoutValidation("x-xbl-contract-version", "1");
        request.Content = JsonBody(writer =>
        {
            writer.WriteStartObject();
            writer.WriteStartObject("Properties");
            writer.WriteString("AuthMethod", "RPS");
            writer.WriteString("SiteName", "user.auth.xboxlive.com");
            writer.WriteString("RpsTicket", $"d={microsoftAccessToken}");
            writer.WriteEndObject();
            writer.WriteString("RelyingParty", "http://auth.xboxlive.com");
            writer.WriteString("TokenType", "JWT");
            writer.WriteEndObject();
        });

        using var response = await _http
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new AccountAuthException(
                "XBL_AUTH_FAILED",
                response.StatusCode == HttpStatusCode.Unauthorized
                    ? "Xbox Live rejected the Microsoft token."
                    : "Could not authenticate with Xbox Live.");
        }

        using var document = ParseJson(body, "Xbox Live response is not JSON");
        return ParseXboxToken(document.RootElement, "XBL_INVALID");
    }

    private async Task<XstsToken> AuthorizeXstsAsync(
        string xblToken,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, _options.XstsEndpoint);
        request.Headers.Accept.Add(AcceptJson);
        request.Headers.TryAddWithoutValidation("x-xbl-contract-version", "1");
        request.Content = JsonBody(writer =>
        {
            writer.WriteStartObject();
            writer.WriteStartObject("Properties");
            writer.WriteString("SandboxId", "RETAIL");
            writer.WriteStartArray("UserTokens");
            writer.WriteStringValue(xblToken);
            writer.WriteEndArray();
            writer.WriteEndObject();
            writer.WriteString("RelyingParty", "rp://api.minecraftservices.com/");
            writer.WriteString("TokenType", "JWT");
            writer.WriteEndObject();
        });

        using var response = await _http
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            var xerr = ReadXErr(response);
            throw MapXstsError(xerr, response.StatusCode);
        }

        using var document = ParseJson(body, "XSTS response is not JSON");
        var token = ParseXboxToken(document.RootElement, "XSTS_INVALID");
        return new XstsToken(token.Token, token.UserHash);
    }

    private async Task<MinecraftToken> LoginWithXboxAsync(
        string xstsToken,
        string userHash,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            _options.MinecraftLoginEndpoint);
        request.Headers.Accept.Add(AcceptJson);
        request.Content = JsonBody(writer =>
        {
            writer.WriteStartObject();
            writer.WriteString("identityToken", $"XBL3.0 x={userHash};{xstsToken}");
            writer.WriteEndObject();
        });

        using var response = await _http
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            if (response.StatusCode == HttpStatusCode.Forbidden &&
                body.Contains("Invalid app registration", StringComparison.OrdinalIgnoreCase))
            {
                throw new AccountAuthException(
                    "MINECRAFT_APP_NOT_APPROVED",
                    "Minecraft Services has not approved this application client id. " +
                    "Submit the Application (client) ID at https://aka.ms/mce-reviewappid, then sign in again.");
            }

            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                throw new AccountAuthException(
                    "MINECRAFT_AUTH_FAILED",
                    "Minecraft Services rejected the Xbox token. Sign in again.");
            }

            throw new AccountAuthException(
                "MINECRAFT_LOGIN_FAILED",
                "Could not obtain a Minecraft access token.");
        }

        using var document = ParseJson(body, "Minecraft login response is not JSON");
        var root = document.RootElement;
        var accessToken = String(root, "access_token");
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            throw new AccountAuthException(
                "MINECRAFT_AUTH_INVALID",
                "Minecraft returned no access token.");
        }

        var expiresIn = Int(root, "expires_in") ?? 86_400;
        return new MinecraftToken(
            accessToken,
            DateTimeOffset.UtcNow.AddSeconds(Math.Max(60, expiresIn)));
    }

    private async Task<CertificatesResult?> TryGetCertificatesAsync(
        string minecraftAccessToken,
        CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Get,
                _options.MinecraftCertificatesEndpoint);
            request.Headers.Authorization =
                new AuthenticationHeaderValue("Bearer", minecraftAccessToken);
            request.Headers.Accept.Add(AcceptJson);

            using var response = await _http
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            using var document = ParseJson(body, "Minecraft certificates response is not JSON");
            DateTimeOffset? expiresAt = null;
            if (document.RootElement.TryGetProperty("keyPair", out var keyPair) &&
                keyPair.ValueKind == JsonValueKind.Object)
            {
                expiresAt = Date(String(keyPair, "expiresAt"));
            }

            return new CertificatesResult(body, expiresAt);
        }
        catch (Exception exception) when (
            exception is HttpRequestException or JsonException or TaskCanceledException
                or AccountAuthException)
        {
            if (cancellationToken.IsCancellationRequested) throw;
            // Certificates are optional for login; entitlement and profile are already checked.
            return null;
        }
    }

    private MicrosoftTokenSet ParseMicrosoftTokens(
        string body,
        string? previousRefreshToken)
    {
        using var document = ParseJson(body, "Microsoft token response is not JSON");
        var root = document.RootElement;
        var accessToken = String(root, "access_token");
        var refreshToken = String(root, "refresh_token") ?? previousRefreshToken;
        if (string.IsNullOrWhiteSpace(accessToken) || string.IsNullOrWhiteSpace(refreshToken))
        {
            throw new AccountAuthException(
                "TOKEN_RESPONSE_INVALID",
                "Microsoft returned no reusable token.");
        }

        return new MicrosoftTokenSet(
            accessToken,
            refreshToken,
            DateTimeOffset.UtcNow.AddSeconds(Math.Max(60, Int(root, "expires_in") ?? 3600)),
            String(root, "scope") ?? _options.Scope,
            String(root, "token_type") ?? "Bearer");
    }

    private static XboxLiveToken ParseXboxToken(JsonElement root, string code)
    {
        var token = String(root, "Token");
        string? userHash = null;
        if (root.TryGetProperty("DisplayClaims", out var claims) &&
            claims.ValueKind == JsonValueKind.Object &&
            claims.TryGetProperty("xui", out var xui) &&
            xui.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in xui.EnumerateArray())
            {
                userHash ??= String(item, "uhs");
                if (userHash is not null) break;
            }
        }

        if (string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(userHash))
        {
            throw new AccountAuthException(code, "Xbox returned an incomplete token.");
        }

        return new XboxLiveToken(token, userHash);
    }

    private static AccountAuthException MapXstsError(string? xerr, HttpStatusCode status)
        => xerr switch
        {
            "2148916233" => new AccountAuthException(
                "XSTS_NO_XBOX_ACCOUNT",
                "This Microsoft account has no Xbox profile. Create one first."),
            "2148916235" => new AccountAuthException(
                "XSTS_REGION_UNAVAILABLE",
                "Xbox Live is not available in this account's region."),
            "2148916236" or "2148916237" => new AccountAuthException(
                "XSTS_ADULT_VERIFICATION",
                "This account needs adult verification before Xbox Live sign-in."),
            "2148916238" => new AccountAuthException(
                "XSTS_CHILD_ACCOUNT",
                "A child account must be added to a Microsoft family first."),
            "2148916227" => new AccountAuthException(
                "XSTS_ACCOUNT_BANNED",
                "This Xbox account is suspended."),
            _ => new AccountAuthException(
                "XSTS_FAILED",
                status == HttpStatusCode.Unauthorized
                    ? "Xbox Live authorization was rejected."
                    : "Could not obtain an XSTS token.",
                xerr),
        };

    private static AccountAuthException MapMicrosoftError(
        string body,
        HttpStatusCode status,
        string fallbackCode)
    {
        var code = ErrorCode(body);
        var description = ErrorDescription(body);
        return new AccountAuthException(
            code is null ? fallbackCode : code.ToUpperInvariant(),
            string.IsNullOrWhiteSpace(description)
                ? $"Microsoft sign-in failed ({(int)status})."
                : description,
            code);
    }

    private static string? ErrorCode(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            return document.RootElement.TryGetProperty("error", out var error) &&
                   error.ValueKind == JsonValueKind.String
                ? error.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? ErrorDescription(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            return document.RootElement.TryGetProperty("error_description", out var error) &&
                   error.ValueKind == JsonValueKind.String
                ? error.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? ReadXErr(HttpResponseMessage response)
    {
        foreach (var name in new[] { "XErr", "X-Err", "x-err" })
        {
            if (response.Headers.TryGetValues(name, out var values))
            {
                return values.FirstOrDefault();
            }
        }

        return null;
    }

    private void EnsureClientId()
    {
        if (string.IsNullOrWhiteSpace(_options.ClientId))
        {
            throw new AccountAuthException(
                "MICROSOFT_CLIENT_ID_MISSING",
                "No Microsoft client ID is configured. Debug: set ARCHOERACX_MICROSOFT_CLIENT_ID. Release/CI: build with -p:MicrosoftClientId=<Application (client) ID>.");
        }
    }

    private static JsonDocument ParseJson(string body, string message)
    {
        try
        {
            return JsonDocument.Parse(body);
        }
        catch (JsonException exception)
        {
            throw new AccountAuthException("RESPONSE_INVALID", message, exception.Message);
        }
    }

    private static string RandomUrlSafeToken(int byteCount)
        => Base64UrlEncode(RandomNumberGenerator.GetBytes(byteCount));

    private static string Base64UrlEncode(ReadOnlySpan<byte> bytes)
        => Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

    private static void AppendQuery(StringBuilder builder, string name, string value)
    {
        if (builder.Length > 0)
        {
            builder.Append('&');
        }

        builder.Append(Uri.EscapeDataString(name));
        builder.Append('=');
        builder.Append(Uri.EscapeDataString(value));
    }

    private static StringContent JsonBody(Action<Utf8JsonWriter> write)
    {
        using var memory = new MemoryStream();
        using (var writer = new Utf8JsonWriter(memory))
        {
            write(writer);
        }

        return new StringContent(
            Encoding.UTF8.GetString(memory.ToArray()),
            Encoding.UTF8,
            "application/json");
    }

    private static string? String(JsonElement root, string name)
        => root.TryGetProperty(name, out var element) && element.ValueKind == JsonValueKind.String
            ? element.GetString()
            : null;

    private static int? Int(JsonElement root, string name)
        => root.TryGetProperty(name, out var element) &&
           element.ValueKind == JsonValueKind.Number &&
           element.TryGetInt32(out var value)
            ? value
            : null;

    private static DateTimeOffset? Date(string? value)
        => DateTimeOffset.TryParse(value, out var parsed) ? parsed : null;

    private sealed record MinecraftToken(string AccessToken, DateTimeOffset ExpiresAt);
    private sealed record CertificatesResult(string Json, DateTimeOffset? ExpiresAt);
}

public sealed record MicrosoftProfile(
    string Uuid,
    string Name,
    string? Xuid,
    string? SkinUrl,
    string? CapeUrl);

public sealed record MicrosoftAuthOptions
{
    public string ClientId { get; init; } = ResolveClientId();

    public string Tenant { get; init; } = MicrosoftAuthClient.DefaultTenant;
    public string Scope { get; init; } = MicrosoftAuthClient.Scope;

    /// <summary>Optional override used by deterministic tests/local proxies.</summary>
    public Uri? LoginBase { get; init; }

    /// <summary>
    /// Loopback callback port. Null uses <c>ARCHOERACX_OAUTH_PORT</c> and then
    /// the production default; 0 asks the OS for an ephemeral port, which keeps
    /// parallel tests from competing for one fixed port.
    /// </summary>
    public int? OAuthPort { get; init; }

    public Uri DeviceCodeEndpoint =>
        LoginEndpoint("devicecode");

    public Uri AuthorizationEndpoint =>
        LoginEndpoint("authorize");

    public Uri TokenEndpoint => LoginEndpoint("token");

    public Uri XboxLiveEndpoint { get; init; } =
        new("https://user.auth.xboxlive.com/user/authenticate");

    public Uri XstsEndpoint { get; init; } =
        new("https://xsts.auth.xboxlive.com/xsts/authorize");

    public Uri MinecraftLoginEndpoint { get; init; } =
        new("https://api.minecraftservices.com/authentication/login_with_xbox");

    public Uri MinecraftEntitlementsEndpoint { get; init; } =
        new("https://api.minecraftservices.com/entitlements/mcstore");

    public Uri MinecraftProfileEndpoint { get; init; } =
        new("https://api.minecraftservices.com/minecraft/profile");

    public Uri MinecraftCertificatesEndpoint { get; init; } =
        new("https://api.minecraftservices.com/player/certificates");

    private static string ResolveClientId()
    {
        var runtime = Environment
            .GetEnvironmentVariable("ARCHOERACX_MICROSOFT_CLIENT_ID")
            ?.Trim();
        return string.IsNullOrWhiteSpace(runtime)
            ? MicrosoftBuildConfig.ClientId.Trim()
            : runtime;
    }

    public MicrosoftAuthOptions Normalize()
        => this with
        {
            ClientId = ClientId.Trim(),
            Tenant = string.IsNullOrWhiteSpace(Tenant)
                ? MicrosoftAuthClient.DefaultTenant
                : Tenant.Trim(),
            Scope = string.IsNullOrWhiteSpace(Scope) ? MicrosoftAuthClient.Scope : Scope.Trim(),
        };

    private Uri LoginEndpoint(string name)
    {
        if (LoginBase is not null)
        {
            return new Uri(LoginBase, name);
        }

        return new Uri(
            $"https://login.microsoftonline.com/{Uri.EscapeDataString(Tenant)}/oauth2/v2.0/{name}");
    }
}
