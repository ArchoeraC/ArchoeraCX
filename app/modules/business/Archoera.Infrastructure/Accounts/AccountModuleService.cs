using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Archoera.Application.Accounts;
using Archoera.Application.Modules;
using Archoera.Domain.Accounts;
using Archoera.Infrastructure.Modules;

namespace Archoera.Infrastructure.Accounts;

/// <summary>
/// Registered <c>account.*</c> handler. It owns pending device-code flows and
/// performs the Microsoft → XBL → XSTS → Minecraft chain. Tokens stay in C#;
/// the JSON envelope only exposes account/profile metadata.
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

    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromSeconds(60),
    };

    private readonly IAccountRepository _accounts;
    private readonly IAccountCredentialStore _credentials;
    private readonly MicrosoftAuthClient _auth;
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
                "account.login.microsoft.start" => await StartMicrosoftAsync(cancellationToken)
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

    private async Task<byte[]> StartMicrosoftAsync(CancellationToken cancellationToken)
    {
        var challenge = await _auth.StartDeviceCodeAsync(cancellationToken).ConfigureAwait(false);
        var pending = new PendingFlow(challenge);
        lock (_flowGate)
        {
            // Starting a new flow invalidates any abandoned flow from this
            // process. The device code is never persisted.
            foreach (var old in _flows.Values)
            {
                old.Cancellation.Cancel();
            }

            _flows.Clear();
            _flows[challenge.FlowId] = pending;
        }

        return ModuleJson.Envelope(writer =>
        {
            writer.WriteString("flowId", challenge.FlowId);
            writer.WriteString("userCode", challenge.UserCode);
            writer.WriteString("verificationUri", challenge.VerificationUri);
            writer.WriteNumber("expiresIn", challenge.ExpiresIn);
            writer.WriteNumber("interval", challenge.Interval);
            writer.WriteString("expiresAt", challenge.ExpiresAt.ToString("O"));
            writer.WriteString(
                "message",
                $"Open {challenge.VerificationUri} and enter {challenge.UserCode}.");
        });
    }

    private async Task<byte[]> PollMicrosoftAsync(
        string? paramsJson,
        CancellationToken cancellationToken)
    {
        var flowId = RequireString(paramsJson, "flowId");
        PendingFlow? pending;
        lock (_flowGate)
        {
            if (!_flows.TryGetValue(flowId, out pending))
            {
                throw new ArgumentException("unknown or already completed sign-in flow");
            }
        }

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            pending.Cancellation.Token);
        try
        {
            var microsoftToken = await _auth
                .WaitForDeviceTokenAsync(pending.Challenge, linked.Token)
                .ConfigureAwait(false);
            Log("account.login.microsoft.poll", "MICROSOFT_TOKEN_OK", "device authorization completed");

            var session = await _auth
                .CompleteMinecraftLoginAsync(microsoftToken, linked.Token)
                .ConfigureAwait(false);
            Log("account.login.microsoft.poll", "MINECRAFT_CHAIN_OK", "profile resolved");

            var account = await UpsertMicrosoftAccountAsync(
                    session,
                    microsoftToken.RefreshToken!,
                    linked.Token)
                .ConfigureAwait(false);

            return ModuleJson.Envelope(writer =>
            {
                writer.WritePropertyName("account");
                WriteAccount(writer, account);
            });
        }
        finally
        {
            RemoveFlow(flowId);
        }
    }

    private byte[] CancelMicrosoft(string? paramsJson)
    {
        var flowId = RequireString(paramsJson, "flowId");
        lock (_flowGate)
        {
            if (_flows.TryGetValue(flowId, out var pending))
            {
                pending.Cancellation.Cancel();
                _flows.Remove(flowId);
            }
        }

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
        var accounts = await _accounts.ListAsync(cancellationToken).ConfigureAwait(false);
        if (accounts.Any(account =>
                account.Kind == AccountKind.Microsoft &&
                !string.IsNullOrWhiteSpace(account.Uuid)))
        {
            return;
        }

        throw new AccountAuthException(
            "PREMIUM_ACCOUNT_REQUIRED",
            "Sign in with a Microsoft account that owns Minecraft before using another account.");
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

    private void RemoveFlow(string flowId)
    {
        PendingFlow? pending = null;
        lock (_flowGate)
        {
            if (_flows.Remove(flowId, out pending))
            {
                // Nothing else to do; the CTS is disposed after the poll ends.
            }
        }

        pending?.Cancellation.Dispose();
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

    private sealed class PendingFlow
    {
        public PendingFlow(MicrosoftDeviceCodeChallenge challenge)
        {
            Challenge = challenge;
        }

        public MicrosoftDeviceCodeChallenge Challenge { get; }
        public CancellationTokenSource Cancellation { get; } = new();
    }
}
