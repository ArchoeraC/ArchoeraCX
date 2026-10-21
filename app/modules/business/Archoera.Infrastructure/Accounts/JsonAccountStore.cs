using System.Text.Json;
using Archoera.Application.Accounts;
using Archoera.Domain.Accounts;
using Archoera.Infrastructure.Settings;

namespace Archoera.Infrastructure.Accounts;

/// <summary>
/// Account metadata and credentials in separate files. Metadata is safe to
/// show in diagnostics; the credential file contains refresh/Minecraft tokens
/// and is written with user-only permissions on Unix.
///
/// The repository is deliberately C#-only: Flutter never reads either file.
/// </summary>
public sealed class JsonAccountStore : IAccountRepository, IAccountCredentialStore
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string _dataRoot;
    private readonly string _accountsPath;
    private readonly string _credentialsPath;

    public JsonAccountStore(string? dataRoot = null)
    {
        _dataRoot = AppDataPaths.Normalize(dataRoot ?? AppDataPaths.ResolveDataRoot());
        _accountsPath = Path.Combine(_dataRoot, "accounts.json");
        _credentialsPath = Path.Combine(_dataRoot, "accounts.credentials.json");
    }

    public string DataRoot => _dataRoot;
    public string AccountsPath => _accountsPath;
    public string CredentialsPath => _credentialsPath;

    public async Task<IReadOnlyList<AccountRecord>> ListAsync(
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return (await ReadAccountsAsync(cancellationToken).ConfigureAwait(false)).Items;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<AccountRecord?> GetAsync(
        string id,
        CancellationToken cancellationToken = default)
    {
        var items = await ListAsync(cancellationToken).ConfigureAwait(false);
        return items.FirstOrDefault(item => string.Equals(item.Id, id, StringComparison.Ordinal));
    }

    public async Task<AccountRecord?> GetActiveAsync(
        CancellationToken cancellationToken = default)
    {
        var document = await ListAsync(cancellationToken).ConfigureAwait(false);
        return document.FirstOrDefault(item => item.Active);
    }

    public async Task<AccountRecord> SaveAsync(
        AccountRecord account,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var document = await ReadAccountsAsync(cancellationToken).ConfigureAwait(false);
            var items = document.Items.ToList();
            var index = items.FindIndex(
                item => string.Equals(item.Id, account.Id, StringComparison.Ordinal));

            // Only one account may be active. Saving a non-active account does
            // not silently change the current selection.
            if (account.Active)
            {
                for (var i = 0; i < items.Count; i++)
                {
                    items[i] = items[i] with { Active = false };
                }
            }

            if (index >= 0)
            {
                items[index] = account;
            }
            else
            {
                items.Add(account);
            }

            var activeId = items.FirstOrDefault(item => item.Active)?.Id ?? document.ActiveAccountId;
            await WriteAccountsAsync(activeId, items, cancellationToken).ConfigureAwait(false);
            return account;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<bool> DeleteAsync(
        string id,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var document = await ReadAccountsAsync(cancellationToken).ConfigureAwait(false);
            var items = document.Items
                .Where(item => !string.Equals(item.Id, id, StringComparison.Ordinal))
                .ToList();
            if (items.Count == document.Items.Count)
            {
                return false;
            }

            var activeId = items.FirstOrDefault(item => item.Active)?.Id;
            await WriteAccountsAsync(activeId, items, cancellationToken).ConfigureAwait(false);
            return true;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<AccountRecord?> SetActiveAsync(
        string id,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var document = await ReadAccountsAsync(cancellationToken).ConfigureAwait(false);
            var items = document.Items.ToList();
            var target = items.FirstOrDefault(
                item => string.Equals(item.Id, id, StringComparison.Ordinal));
            if (target is null)
            {
                return null;
            }

            for (var i = 0; i < items.Count; i++)
            {
                items[i] = items[i] with
                {
                    Active = string.Equals(items[i].Id, id, StringComparison.Ordinal),
                };
            }

            await WriteAccountsAsync(id, items, cancellationToken).ConfigureAwait(false);
            return items.First(item => item.Active);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<AccountCredential?> GetCredentialAsync(
        string accountId,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var items = await ReadCredentialsAsync(cancellationToken).ConfigureAwait(false);
            return items.FirstOrDefault(
                item => string.Equals(item.AccountId, accountId, StringComparison.Ordinal));
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SaveCredentialAsync(
        AccountCredential credential,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var items = (await ReadCredentialsAsync(cancellationToken).ConfigureAwait(false)).ToList();
            var index = items.FindIndex(item =>
                string.Equals(item.AccountId, credential.AccountId, StringComparison.Ordinal));
            if (index >= 0)
            {
                items[index] = credential;
            }
            else
            {
                items.Add(credential);
            }

            await WriteCredentialsAsync(items, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task DeleteCredentialAsync(
        string accountId,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var items = (await ReadCredentialsAsync(cancellationToken).ConfigureAwait(false))
                .Where(item => !string.Equals(item.AccountId, accountId, StringComparison.Ordinal))
                .ToList();
            await WriteCredentialsAsync(items, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<AccountsDocument> ReadAccountsAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_accountsPath))
        {
            return new AccountsDocument(null, []);
        }

        try
        {
            await using var stream = OpenRead(_accountsPath);
            using var document = await JsonDocument
                .ParseAsync(stream, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return new AccountsDocument(null, []);
            }

            var activeId = String(root, "activeAccountId");
            var items = new List<AccountRecord>();
            if (root.TryGetProperty("items", out var array) &&
                array.ValueKind == JsonValueKind.Array)
            {
                foreach (var element in array.EnumerateArray())
                {
                    var account = ParseAccount(element);
                    if (account is not null) items.Add(account);
                }
            }

            if (!string.IsNullOrWhiteSpace(activeId) &&
                items.Count > 0 &&
                items.All(item => !item.Active))
            {
                var index = items.FindIndex(
                    item => string.Equals(item.Id, activeId, StringComparison.Ordinal));
                if (index >= 0)
                {
                    items[index] = items[index] with { Active = true };
                }
            }

            return new AccountsDocument(activeId, items);
        }
        catch (Exception exception) when (
            exception is IOException or JsonException or UnauthorizedAccessException)
        {
            return new AccountsDocument(null, []);
        }
    }

    private async Task<List<AccountCredential>> ReadCredentialsAsync(
        CancellationToken cancellationToken)
    {
        if (!File.Exists(_credentialsPath))
        {
            return [];
        }

        try
        {
            await using var stream = OpenRead(_credentialsPath);
            using var document = await JsonDocument
                .ParseAsync(stream, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("items", out var array) ||
                array.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            var items = new List<AccountCredential>();
            foreach (var element in array.EnumerateArray())
            {
                if (element.ValueKind != JsonValueKind.Object) continue;
                var accountId = String(element, "accountId");
                if (string.IsNullOrWhiteSpace(accountId)) continue;
                items.Add(new AccountCredential(
                    accountId,
                    String(element, "microsoftRefreshToken"),
                    String(element, "minecraftAccessToken"),
                    Date(String(element, "minecraftAccessTokenExpiresAt")),
                    String(element, "certificatesJson"),
                    Date(String(element, "updatedAt")) ?? DateTimeOffset.UtcNow));
            }

            return items;
        }
        catch (Exception exception) when (
            exception is IOException or JsonException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private async Task WriteAccountsAsync(
        string? activeAccountId,
        IReadOnlyList<AccountRecord> items,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_dataRoot);
        var temporary = _accountsPath + ".tmp";
        await using (var stream = OpenWrite(temporary))
        {
            await using var writer = new Utf8JsonWriter(stream, new JsonWriterOptions
            {
                Indented = true,
            });
            writer.WriteStartObject();
            writer.WriteNumber("schemaVersion", AccountRecord.CurrentSchemaVersion);
            writer.WriteString("activeAccountId", activeAccountId);
            writer.WriteStartArray("items");
            foreach (var item in items)
            {
                WriteAccount(writer, item);
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
            await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        }

        File.Move(temporary, _accountsPath, overwrite: true);
    }

    private async Task WriteCredentialsAsync(
        IReadOnlyList<AccountCredential> items,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_dataRoot);
        var temporary = _credentialsPath + ".tmp";
        await using (var stream = OpenWrite(temporary))
        {
            RestrictToCurrentUser(temporary);
            await using var writer = new Utf8JsonWriter(stream, new JsonWriterOptions
            {
                Indented = true,
            });
            writer.WriteStartObject();
            writer.WriteNumber("schemaVersion", 1);
            writer.WriteStartArray("items");
            foreach (var item in items)
            {
                writer.WriteStartObject();
                writer.WriteString("accountId", item.AccountId);
                writer.WriteString("microsoftRefreshToken", item.MicrosoftRefreshToken);
                writer.WriteString("minecraftAccessToken", item.MinecraftAccessToken);
                writer.WriteString(
                    "minecraftAccessTokenExpiresAt",
                    item.MinecraftAccessTokenExpiresAt?.ToString("O"));
                writer.WriteString("certificatesJson", item.CertificatesJson);
                writer.WriteString("updatedAt", item.UpdatedAt.ToString("O"));
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
            await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        }

        File.Move(temporary, _credentialsPath, overwrite: true);
        RestrictToCurrentUser(_credentialsPath);
    }

    private static FileStream OpenRead(string path)
        => new(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, useAsync: true);

    private static FileStream OpenWrite(string path)
        => new(path, FileMode.Create, FileAccess.Write, FileShare.None, 4096, useAsync: true);

    private static void RestrictToCurrentUser(string path)
    {
        if (OperatingSystem.IsWindows()) return;
        try
        {
            File.SetUnixFileMode(
                path,
                UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            // Best effort; the data directory is still user-scoped.
        }
    }

    private static AccountRecord? ParseAccount(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object) return null;
        var id = String(element, "id");
        var name = String(element, "displayName");
        if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(name)) return null;
        var kindText = String(element, "kind");
        var kind = Enum.TryParse<AccountKind>(kindText, ignoreCase: true, out var parsed)
            ? parsed
            : AccountKind.Offline;
        return new AccountRecord(
            id,
            kind,
            name,
            String(element, "uuid"),
            String(element, "xuid"),
            String(element, "skinUrl"),
            String(element, "capeUrl"),
            Bool(element, "active"),
            Date(String(element, "createdAt")) ?? DateTimeOffset.UtcNow,
            Date(String(element, "updatedAt")) ?? DateTimeOffset.UtcNow,
            Date(String(element, "lastLoginAt")));
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

    private static string? String(JsonElement root, string name)
        => root.TryGetProperty(name, out var element) && element.ValueKind == JsonValueKind.String
            ? element.GetString()
            : null;

    private static bool Bool(JsonElement root, string name)
        => root.TryGetProperty(name, out var element) &&
           element.ValueKind is JsonValueKind.True or JsonValueKind.False &&
           element.GetBoolean();

    private static DateTimeOffset? Date(string? text)
        => DateTimeOffset.TryParse(text, out var parsed) ? parsed : null;

    private sealed record AccountsDocument(
        string? ActiveAccountId,
        IReadOnlyList<AccountRecord> Items);
}
