using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Archoera.Application.Accounts;
using Archoera.Application.Modules;
using Archoera.Domain.Accounts;
using Archoera.Infrastructure.Modules;

namespace Archoera.Infrastructure.Accounts;

/// <summary>
/// Registered <c>account.*</c> handler. It owns pending device-code and
/// authorization-code + PKCE flows and performs the Microsoft → XBL → XSTS →
/// Minecraft chain. Tokens, verifiers and state stay in C#; the JSON envelope
/// only exposes account/profile metadata and public challenge details.
/// </summary>
public sealed class AccountModuleService : IModuleMethodHandler
{
    private static readonly string[] SupportedMethods =
    [
        "account.list",
        "account.current",
        "account.select",
        "account.login.microsoft.start",
        "account.login.microsoft.poll",
        "account.login.microsoft.cancel",
        "account.login.offline",
        "account.refresh",
        "account.remove",
    ];

    private const int AuthorizationCodeExpiresIn = 300;
    private const int DefaultOAuthPort = 53682;

    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromSeconds(60),
    };

    private readonly IAccountRepository _accounts;
    private readonly IAccountCredentialStore _credentials;
    private readonly MicrosoftAuthClient _auth;
    private readonly MicrosoftAccountGate _premiumGate;
    private readonly string? _diagnosticsPath;
    private readonly object _logGate = new();
    private readonly object _flowGate = new();
    private readonly Dictionary<string, PendingFlow> _flows = new(StringComparer.Ordinal);

    public AccountModuleService(
        IAccountRepository? accounts = null,
        IAccountCredentialStore? credentials = null,
        MicrosoftAuthClient? auth = null,
        string? dataRoot = null)
    {
        if (accounts is null || credentials is null)
        {
            var store = new JsonAccountStore(dataRoot);
            accounts ??= store;
            credentials ??= store;
        }

        _accounts = accounts;
        _credentials = credentials;
        _auth = auth ?? new MicrosoftAuthClient(Http, new MicrosoftAuthOptions());
        _premiumGate = new MicrosoftAccountGate(_accounts, _credentials, _auth);

        var diagnosticRoot = dataRoot;
        if (diagnosticRoot is null && accounts is JsonAccountStore jsonStore)
        {
            diagnosticRoot = jsonStore.DataRoot;
        }

        _diagnosticsPath = string.IsNullOrWhiteSpace(diagnosticRoot)
            ? null
            : Path.Combine(diagnosticRoot, "logs", "account.log");
    }

    public IReadOnlyCollection<string> Methods => SupportedMethods;

    public async Task<byte[]> HandleAsync(
        string method,
        string? paramsJson,
        CancellationToken cancellationToken)
    {
        try
        {
            return method switch
            {
                "account.list" => await ListAsync(cancellationToken).ConfigureAwait(false),
                "account.current" => await CurrentAsync(cancellationToken).ConfigureAwait(false),
                "account.select" => await SelectAsync(paramsJson, cancellationToken)
                    .ConfigureAwait(false),
                "account.login.microsoft.start" => await StartMicrosoftAsync(
                        paramsJson,
                        cancellationToken)
                    .ConfigureAwait(false),
                "account.login.microsoft.poll" => await PollMicrosoftAsync(paramsJson, cancellationToken)
                    .ConfigureAwait(false),
                "account.login.microsoft.cancel" => CancelMicrosoft(paramsJson),
                "account.login.offline" => await LoginOfflineAsync(paramsJson, cancellationToken)
                    .ConfigureAwait(false),
                "account.refresh" => await RefreshAsync(paramsJson, cancellationToken)
                    .ConfigureAwait(false),
                "account.remove" => await RemoveAsync(paramsJson, cancellationToken)
                    .ConfigureAwait(false),
                _ => ModuleJson.Error("E_UNSUPPORTED", $"unsupported account method '{method}'"),
            };
        }
        catch (AccountAuthException exception)
        {
            Log(method, exception.Code, exception.Message);
            return ModuleJson.Error(exception.Code, exception.Message);
        }
        catch (CredentialVaultException exception)
        {
            Log(method, "E_VAULT", exception.Message);
            return ModuleJson.Error("E_VAULT", exception.Message);
        }
        catch (ArgumentException exception)
        {
            Log(method, "E_INVALID", exception.Message);
            return ModuleJson.Error("E_INVALID", exception.Message);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or HttpRequestException)
        {
            Log(method, "E_INTERNAL", exception.Message);
            return ModuleJson.Error("E_INTERNAL", exception.Message);
        }
    }

    private async Task<byte[]> ListAsync(CancellationToken cancellationToken)
    {
        var items = await _accounts.ListAsync(cancellationToken).ConfigureAwait(false);
        return ModuleJson.Envelope(writer =>
        {
            writer.WriteStartArray("items");
            foreach (var item in items)
            {
                WriteAccount(writer, item);
            }

            writer.WriteEndArray();
            writer.WriteString(
                "activeAccountId",
                items.FirstOrDefault(item => item.Active)?.Id);
        });
    }

    private async Task<byte[]> CurrentAsync(CancellationToken cancellationToken)
    {
        var active = await _accounts.GetActiveAsync(cancellationToken).ConfigureAwait(false);
        return ModuleJson.Envelope(writer =>
        {
            writer.WritePropertyName("account");
            if (active is null)
            {
                writer.WriteNullValue();
            }
            else
            {
                WriteAccount(writer, active);
            }
        });
    }

    private async Task<byte[]> SelectAsync(
        string? paramsJson,
        CancellationToken cancellationToken)
    {
        var id = RequireString(paramsJson, "id");
        var requireMicrosoft = ReadBool(paramsJson, "requireMicrosoft");
        await EnsurePremiumAsync(ReadBool(paramsJson, "requirePremium"), cancellationToken)
            .ConfigureAwait(false);
        var candidate = await _accounts.GetAsync(id, cancellationToken).ConfigureAwait(false)
                        ?? throw new ArgumentException("account not found");
        if (requireMicrosoft && candidate.Kind != AccountKind.Microsoft)
        {
            throw new AccountAuthException(
                "MICROSOFT_ACCOUNT_ONLY",
                "Only a verified Microsoft account may be selected in this region.");
        }

        var account = await _accounts.SetActiveAsync(id, cancellationToken).ConfigureAwait(false)
                      ?? throw new ArgumentException("account not found");
        return ModuleJson.Envelope(writer =>
        {
            writer.WritePropertyName("account");
            WriteAccount(writer, account);
        });
    }

    private async Task<byte[]> StartMicrosoftAsync(
        string? paramsJson,
        CancellationToken cancellationToken)
    {
        var flow = ReadOptionalString(paramsJson, "flow") ?? "device_code";
        return flow.Trim().ToLowerInvariant() switch
        {
            "device_code" => await StartDeviceCodeFlowAsync(cancellationToken)
                .ConfigureAwait(false),
            "authorization_code" => await StartAuthorizationCodeFlowAsync(cancellationToken)
                .ConfigureAwait(false),
            _ => throw new ArgumentException(
                $"unsupported sign-in flow '{flow}'; use device_code or authorization_code"),
        };
    }

    private async Task<byte[]> StartDeviceCodeFlowAsync(CancellationToken cancellationToken)
    {
        var challenge = await _auth.StartDeviceCodeAsync(cancellationToken).ConfigureAwait(false);
        var response = ModuleJson.Envelope(writer =>
        {
            writer.WriteString("flowId", challenge.FlowId);
            writer.WriteString("mode", "device_code");
            writer.WriteString("userCode", challenge.UserCode);
            writer.WriteString("verificationUri", challenge.VerificationUri);
            writer.WriteNumber("expiresIn", challenge.ExpiresIn);
            writer.WriteNumber("interval", challenge.Interval);
            writer.WriteString("expiresAt", challenge.ExpiresAt.ToString("O"));
            writer.WriteString(
                "message",
                $"Open {challenge.VerificationUri} and enter {challenge.UserCode}.");
        });

        // Starting a new flow invalidates any abandoned flow from this process.
        // The device code is never persisted.
        ReplaceFlow(PendingFlow.ForDeviceCode(challenge));
        return response;
    }

    private async Task<byte[]> StartAuthorizationCodeFlowAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var oauthPort = ReadOAuthPort();
        LoopbackOAuthListener listener;
        try
        {
            listener = LoopbackOAuthListener.Start(oauthPort);
        }
        catch (System.Net.Sockets.SocketException exception)
        {
            throw new AccountAuthException(
                "OAUTH_PORT_UNAVAILABLE",
                $"The local OAuth callback port {oauthPort} is unavailable. " +
                "Close the program using it or set ARCHOERACX_OAUTH_PORT and register the matching redirect URI.",
                exception.Message);
        }

        try
        {
            var challenge = _auth.CreateAuthorizationCodeChallenge(listener.RedirectUri);
            cancellationToken.ThrowIfCancellationRequested();
            var response = ModuleJson.Envelope(writer =>
            {
                writer.WriteString("flowId", challenge.FlowId);
                writer.WriteString("mode", "authorization_code");
                writer.WriteString("authorizationUrl", challenge.AuthorizationUrl);
                writer.WriteString("redirectUri", challenge.RedirectUri);
                writer.WriteNumber("expiresIn", AuthorizationCodeExpiresIn);
                writer.WriteString("expiresAt", challenge.ExpiresAt.ToString("O"));
            });

            ReplaceFlow(PendingFlow.ForAuthorizationCode(challenge, listener));
            return response;
        }
        catch
        {
            // The listener is not registered on failure; nothing else owns it.
            listener.Dispose();
            throw;
        }
    }

    private async Task<byte[]> PollMicrosoftAsync(
        string? paramsJson,
        CancellationToken cancellationToken)
    {
        var flowId = RequireString(paramsJson, "flowId");
        PendingFlow pending;
        lock (_flowGate)
        {
            if (!_flows.TryGetValue(flowId, out pending!))
            {
                throw new ArgumentException("unknown or already completed sign-in flow");
            }

            if (!pending.TryBeginPoll())
            {
                throw new ArgumentException("sign-in flow is already being polled");
            }
        }

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            pending.Cancellation.Token);
        try
        {
            return pending.AuthorizationCodeChallenge is { } challenge
                ? await PollAuthorizationCodeAsync(challenge, pending, linked.Token)
                    .ConfigureAwait(false)
                : await PollDeviceCodeAsync(pending, linked.Token).ConfigureAwait(false);
        }
        finally
        {
            FinishPoll(flowId, pending);
        }
    }

    private async Task<byte[]> PollDeviceCodeAsync(
        PendingFlow pending,
        CancellationToken cancellationToken)
    {
        var challenge = pending.DeviceCodeChallenge
                        ?? throw new AccountAuthException(
                            "AUTH_FLOW_INVALID",
                            "The sign-in flow has no device code challenge.");
        var microsoftToken = await _auth
            .WaitForDeviceTokenAsync(challenge, cancellationToken)
            .ConfigureAwait(false);
        Log("account.login.microsoft.poll", "MICROSOFT_TOKEN_OK", "device authorization completed");
        return await CompleteSignInAsync(microsoftToken, cancellationToken).ConfigureAwait(false);
    }

    private async Task<byte[]> PollAuthorizationCodeAsync(
        MicrosoftAuthorizationCodeChallenge challenge,
        PendingFlow pending,
        CancellationToken cancellationToken)
    {
        var listener = pending.Listener
                       ?? throw new AccountAuthException(
                           "AUTH_FLOW_INVALID",
                           "The sign-in flow has no loopback listener.");

        var remaining = challenge.ExpiresAt - DateTimeOffset.UtcNow;
        if (remaining <= TimeSpan.Zero)
        {
            throw new AccountAuthException(
                "AUTH_CODE_EXPIRED",
                "The Microsoft sign-in request expired. Start again.");
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(remaining);

        LoopbackOAuthCallback callback;
        try
        {
            callback = await listener.WaitForCallbackAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new AccountAuthException(
                "AUTH_CODE_EXPIRED",
                "The Microsoft sign-in request expired. Start again.");
        }
        catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested)
        {
            throw new OperationCanceledException(cancellationToken);
        }

        // Validate state before acting on the callback, including error
        // callbacks: an unrelated local request must not finish the flow.
        if (!string.Equals(callback.State, challenge.State, StringComparison.Ordinal))
        {
            throw new AccountAuthException(
                "AUTH_STATE_MISMATCH",
                "The Microsoft sign-in response did not match this request. Start again.");
        }

        if (!string.IsNullOrWhiteSpace(callback.Error))
        {
            throw MapLoopbackError(callback.Error, callback.ErrorDescription);
        }

        if (string.IsNullOrWhiteSpace(callback.Code))
        {
            throw new AccountAuthException(
                "AUTH_CALLBACK_INVALID",
                "Microsoft sign-in returned no authorization code. Start again.");
        }

        var microsoftToken = await _auth
            .CompleteAuthorizationCodeAsync(challenge, callback.Code, cancellationToken)
            .ConfigureAwait(false);
        Log(
            "account.login.microsoft.poll",
            "MICROSOFT_TOKEN_OK",
            "authorization code exchanged");

        return await CompleteSignInAsync(microsoftToken, cancellationToken).ConfigureAwait(false);
    }

    private async Task<byte[]> CompleteSignInAsync(
        MicrosoftTokenSet microsoftToken,
        CancellationToken cancellationToken)
    {
        var session = await _auth
            .CompleteMinecraftLoginAsync(microsoftToken, cancellationToken)
            .ConfigureAwait(false);
        Log("account.login.microsoft.poll", "MINECRAFT_CHAIN_OK", "profile resolved");

        var refreshToken = microsoftToken.RefreshToken
                           ?? throw new AccountAuthException(
                               "TOKEN_RESPONSE_INVALID",
                               "Microsoft returned no reusable token. Sign in again.");
        var account = await UpsertMicrosoftAccountAsync(
                session,
                refreshToken,
                cancellationToken)
            .ConfigureAwait(false);

        return ModuleJson.Envelope(writer =>
        {
            writer.WritePropertyName("account");
            WriteAccount(writer, account);
        });
    }

    private byte[] CancelMicrosoft(string? paramsJson)
    {
        var flowId = RequireString(paramsJson, "flowId");
        PendingFlow? pending;
        lock (_flowGate)
        {
            _flows.Remove(flowId, out pending);
        }

        // A running poll owns disposal and finishes on cancellation; an idle
        // flow is disposed here so its loopback listener cannot leak.
        pending?.CancelOrDispose();

        return ModuleJson.Envelope(writer => writer.WriteBoolean("cancelled", true));
    }

    private async Task<byte[]> LoginOfflineAsync(
        string? paramsJson,
        CancellationToken cancellationToken)
    {
        if (ReadBool(paramsJson, "requireMicrosoft"))
        {
            throw new AccountAuthException(
                "MICROSOFT_ACCOUNT_ONLY",
                "Offline accounts are disabled in this region.");
        }
        await EnsurePremiumAsync(ReadBool(paramsJson, "requirePremium"), cancellationToken)
            .ConfigureAwait(false);
        var name = RequireString(paramsJson, "name").Trim();
        if (name.Length is < 3 or > 16 ||
            name.Any(character => !(char.IsAsciiLetterOrDigit(character) || character == '_')))
        {
            throw new ArgumentException(
                "offline name must be 3–16 ASCII letters, digits or underscores");
        }

        var existing = (await _accounts.ListAsync(cancellationToken).ConfigureAwait(false))
            .FirstOrDefault(item =>
                item.Kind == AccountKind.Offline &&
                string.Equals(item.DisplayName, name, StringComparison.OrdinalIgnoreCase));
        var active = await _accounts.GetActiveAsync(cancellationToken).ConfigureAwait(false);
        var now = DateTimeOffset.UtcNow;
        var account = new AccountRecord(
            existing?.Id ?? $"offline-{Guid.NewGuid():N}"[..24],
            AccountKind.Offline,
            name,
            OfflineUuid(name),
            null,
            null,
            null,
            existing?.Active ?? active is null,
            existing?.CreatedAt ?? now,
            now,
            now);
        await _accounts.SaveAsync(account, cancellationToken).ConfigureAwait(false);
        return ModuleJson.Envelope(writer =>
        {
            writer.WritePropertyName("account");
            WriteAccount(writer, account);
        });
    }

    private async Task EnsurePremiumAsync(
        bool required,
        CancellationToken cancellationToken)
    {
        if (!required) return;
        if (await _premiumGate.HasValidAccountAsync(cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        throw new AccountAuthException(
            "PREMIUM_ACCOUNT_REQUIRED",
            "Sign in with a Microsoft account that owns Minecraft before using another account.");
    }

    private static int ReadOAuthPort()
    {
        var raw = Environment.GetEnvironmentVariable("ARCHOERACX_OAUTH_PORT");
        return int.TryParse(raw, out var port) && port is > 0 and <= 65535
            ? port
            : DefaultOAuthPort;
    }

    private static bool ReadBool(string? json, string name)
    {
        if (string.IsNullOrWhiteSpace(json)) return false;
        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.TryGetProperty(name, out var element) &&
                   element.ValueKind is JsonValueKind.True or JsonValueKind.False &&
                   element.GetBoolean();
        }
        catch (JsonException exception)
        {
            throw new ArgumentException($"request is not valid JSON: {exception.Message}");
        }
    }

    private async Task<byte[]> RefreshAsync(
        string? paramsJson,
        CancellationToken cancellationToken)
    {
        var account = await ResolveAccountAsync(paramsJson, cancellationToken).ConfigureAwait(false);
        if (account.Kind != AccountKind.Microsoft)
        {
            throw new ArgumentException("only Microsoft accounts can be refreshed");
        }

        var credential = await _credentials
            .GetCredentialAsync(account.Id, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new AccountAuthException(
                "AUTH_REQUIRED",
                "No stored session for this account. Sign in again.");
        if (!credential.HasRefreshToken)
        {
            throw new AccountAuthException(
                "AUTH_REQUIRED",
                "No refresh token is stored. Sign in again.");
        }

        var microsoftToken = await _auth
            .RefreshMicrosoftAsync(credential.MicrosoftRefreshToken!, cancellationToken)
            .ConfigureAwait(false);
        Log("account.refresh", "MICROSOFT_TOKEN_OK", "refresh completed");

        var session = await _auth
            .CompleteMinecraftLoginAsync(microsoftToken, cancellationToken)
            .ConfigureAwait(false);
        Log("account.refresh", "MINECRAFT_CHAIN_OK", "profile resolved");
        var updated = await UpsertMicrosoftAccountAsync(
                session,
                microsoftToken.RefreshToken!,
                cancellationToken,
                account)
            .ConfigureAwait(false);
        return ModuleJson.Envelope(writer =>
        {
            writer.WritePropertyName("account");
            WriteAccount(writer, updated);
        });
    }

    private async Task<byte[]> RemoveAsync(
        string? paramsJson,
        CancellationToken cancellationToken)
    {
        var id = RequireString(paramsJson, "id");
        var removed = await _accounts.GetAsync(id, cancellationToken).ConfigureAwait(false);
        // Offline profiles have no vault entry; do not initialize the vault
        // merely to delete an account that never stored credentials.
        if (removed is { Kind: not AccountKind.Offline })
        {
            await _credentials.DeleteCredentialAsync(id, cancellationToken).ConfigureAwait(false);
        }
        var deleted = await _accounts.DeleteAsync(id, cancellationToken).ConfigureAwait(false);

        // Removing the active account should leave a usable current account
        // when other accounts remain.
        if (deleted && removed?.Active == true)
        {
            var remaining = await _accounts.ListAsync(cancellationToken).ConfigureAwait(false);
            var next = remaining.FirstOrDefault();
            if (next is not null)
            {
                await _accounts.SetActiveAsync(next.Id, cancellationToken).ConfigureAwait(false);
            }
        }

        return ModuleJson.Envelope(writer => writer.WriteBoolean("deleted", deleted));
    }

    private async Task<AccountRecord> UpsertMicrosoftAccountAsync(
        MinecraftAuthenticatedSession session,
        string refreshToken,
        CancellationToken cancellationToken,
        AccountRecord? existing = null)
    {
        var all = await _accounts.ListAsync(cancellationToken).ConfigureAwait(false);
        existing ??= all.FirstOrDefault(item =>
            item.Kind == AccountKind.Microsoft &&
            string.Equals(item.Uuid, session.Uuid, StringComparison.OrdinalIgnoreCase));
        var active = await _accounts.GetActiveAsync(cancellationToken).ConfigureAwait(false);
        var now = DateTimeOffset.UtcNow;
        var account = new AccountRecord(
            existing?.Id ?? $"ms-{session.Uuid.ToLowerInvariant()}",
            AccountKind.Microsoft,
            session.Name,
            session.Uuid,
            session.Xuid,
            session.SkinUrl,
            session.CapeUrl,
            existing?.Active ?? active is null,
            existing?.CreatedAt ?? now,
            now,
            now);
        await _accounts.SaveAsync(account, cancellationToken).ConfigureAwait(false);
        await _credentials.SaveCredentialAsync(
                new AccountCredential(
                    account.Id,
                    refreshToken,
                    session.AccessToken,
                    session.ExpiresAt,
                    session.CertificatesJson,
                    now),
                cancellationToken)
            .ConfigureAwait(false);
        return account;
    }

    private async Task<AccountRecord> ResolveAccountAsync(
        string? paramsJson,
        CancellationToken cancellationToken)
    {
        var id = ReadOptionalString(paramsJson, "id");
        if (!string.IsNullOrWhiteSpace(id))
        {
            return await _accounts.GetAsync(id, cancellationToken).ConfigureAwait(false)
                   ?? throw new ArgumentException("account not found");
        }

        return await _accounts.GetActiveAsync(cancellationToken).ConfigureAwait(false)
               ?? throw new ArgumentException("no active account");
    }

    private void Log(string method, string code, string? message)
    {
        if (_diagnosticsPath is null) return;
        try
        {
            var text = (message ?? string.Empty)
                .Replace('\r', ' ')
                .Replace('\n', ' ');
            if (text.Length > 500) text = text[..500];
            var line =
                $"{DateTimeOffset.UtcNow:O}\t{method}\t{code}\t{text}{Environment.NewLine}";
            lock (_logGate)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_diagnosticsPath)!);
                File.AppendAllText(_diagnosticsPath, line);
            }
        }
        catch
        {
            // Diagnostics are best effort; never fail auth because logging did.
        }
    }

    private void ReplaceFlow(PendingFlow pending)
    {
        List<PendingFlow> replaced;
        lock (_flowGate)
        {
            replaced = _flows.Values.ToList();
            _flows.Clear();
            _flows[pending.FlowId] = pending;
        }

        foreach (var old in replaced)
        {
            old.CancelOrDispose();
        }
    }

    private void FinishPoll(string flowId, PendingFlow pending)
    {
        lock (_flowGate)
        {
            _flows.Remove(flowId);
        }

        // This poll owns disposal for a flow that is still marked active.
        pending.EndPollAndDispose();
    }

    private static AccountAuthException MapLoopbackError(
        string error,
        string? description)
    {
        var message = string.IsNullOrWhiteSpace(description)
            ? "Microsoft sign-in failed."
            : description;
        return error switch
        {
            "access_denied" or "authorization_declined" => new AccountAuthException(
                "AUTH_DECLINED",
                "Microsoft sign-in was declined.",
                error),
            "expired_token" or "invalid_grant" => new AccountAuthException(
                "AUTH_CODE_EXPIRED",
                "The Microsoft sign-in request expired. Start again.",
                error),
            "invalid_client" => new AccountAuthException(
                "AUTH_CLIENT_INVALID",
                message,
                error),
            "invalid_request" or "invalid_scope" or "unsupported_response_type" =>
                new AccountAuthException(
                    "AUTH_CALLBACK_INVALID",
                    message,
                    error),
            "server_error" or "temporarily_unavailable" => new AccountAuthException(
                "AUTH_UNAVAILABLE",
                message,
                error),
            _ => new AccountAuthException("AUTH_CALLBACK_FAILED", message, error),
        };
    }

    private static string RequireString(string? json, string name)
        => ReadOptionalString(json, name)
           ?? throw new ArgumentException($"{name} is required");

    private static string? ReadOptionalString(string? json, string name)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.TryGetProperty(name, out var element) &&
                   element.ValueKind == JsonValueKind.String &&
                   !string.IsNullOrWhiteSpace(element.GetString())
                ? element.GetString()
                : null;
        }
        catch (JsonException exception)
        {
            throw new ArgumentException($"request is not valid JSON: {exception.Message}");
        }
    }

    private static void WriteAccount(Utf8JsonWriter writer, AccountRecord account)
    {
        writer.WriteStartObject();
        writer.WriteString("id", account.Id);
        writer.WriteString("kind", account.Kind.ToString().ToLowerInvariant());
        writer.WriteString("displayName", account.DisplayName);
        writer.WriteString("uuid", account.Uuid);
        writer.WriteString("xuid", account.Xuid);
        writer.WriteString("skinUrl", account.SkinUrl);
        writer.WriteString("capeUrl", account.CapeUrl);
        writer.WriteBoolean("active", account.Active);
        writer.WriteString("createdAt", account.CreatedAt.ToString("O"));
        writer.WriteString("updatedAt", account.UpdatedAt.ToString("O"));
        writer.WriteString("lastLoginAt", account.LastLoginAt?.ToString("O"));
        writer.WriteEndObject();
    }

    private static string OfflineUuid(string name)
    {
        var bytes = MD5.HashData(Encoding.UTF8.GetBytes($"OfflinePlayer:{name}"));
        bytes[6] = (byte)((bytes[6] & 0x0F) | 0x30);
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);
        var hex = Convert.ToHexStringLower(bytes);
        return $"{hex[..8]}-{hex[8..12]}-{hex[12..16]}-{hex[16..20]}-{hex[20..]}";
    }

    private sealed class PendingFlow : IDisposable
    {
        private int _polling;
        private int _disposed;

        private PendingFlow(
            MicrosoftDeviceCodeChallenge? deviceCodeChallenge,
            MicrosoftAuthorizationCodeChallenge? authorizationCodeChallenge,
            LoopbackOAuthListener? listener)
        {
            DeviceCodeChallenge = deviceCodeChallenge;
            AuthorizationCodeChallenge = authorizationCodeChallenge;
            Listener = listener;
        }

        public static PendingFlow ForDeviceCode(MicrosoftDeviceCodeChallenge challenge)
            => new(challenge, null, null);

        public static PendingFlow ForAuthorizationCode(
            MicrosoftAuthorizationCodeChallenge challenge,
            LoopbackOAuthListener listener)
            => new(null, challenge, listener);

        public MicrosoftDeviceCodeChallenge? DeviceCodeChallenge { get; }
        public MicrosoftAuthorizationCodeChallenge? AuthorizationCodeChallenge { get; }
        public LoopbackOAuthListener? Listener { get; }
        public CancellationTokenSource Cancellation { get; } = new();

        public string FlowId =>
            DeviceCodeChallenge?.FlowId ??
            AuthorizationCodeChallenge?.FlowId ??
            string.Empty;

        private bool IsPolling => Volatile.Read(ref _polling) != 0;

        /// <summary>
        /// Called under the flow lock so cleanup cannot dispose the token source
        /// between the dictionary lookup and the poll starting.
        /// </summary>
        public bool TryBeginPoll() => Interlocked.CompareExchange(ref _polling, 1, 0) == 0;

        /// <summary>
        /// Stops an abandoned flow immediately, or signals an in-flight poll.
        /// The running poll disposes after it observes cancellation, which keeps
        /// its linked token source valid.
        /// </summary>
        public void CancelOrDispose()
        {
            if (IsPolling)
            {
                TryCancel();
                return;
            }

            Dispose();
        }

        public void EndPollAndDispose()
        {
            Volatile.Write(ref _polling, 0);
            Dispose();
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            TryCancel();
            Cancellation.Dispose();
            Listener?.Dispose();
        }

        private void TryCancel()
        {
            try
            {
                Cancellation.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // Already torn down.
            }
            catch (AggregateException)
            {
                // A listener callback failed; cleanup still proceeds.
            }
        }
    }
}
