using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Archoera.Application.Accounts;
using Archoera.Domain.Accounts;
using Archoera.Infrastructure.Settings;

namespace Archoera.Infrastructure.Accounts;

/// <summary>
/// Credential owner backed by the Archoera vault process.
///
/// The vault is an on-demand, encrypted file process. This store handles
/// initialization, one-shot serve sessions, handshake verification, and the
/// one-time migration of the legacy plaintext <c>accounts.credentials.json</c>.
/// Metadata remains in accounts.json; only tokens/refresh material go here.
/// </summary>
public sealed class VaultCredentialStore : IAccountCredentialStore
{
    private const string ProductMarker = "ARCHOERA-VAULT-PROD-";
    private static readonly TimeSpan CliTimeout = TimeSpan.FromSeconds(30);
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string _dataRoot;
    private readonly string _binary;
    private readonly string _anchorPath;
    private bool _ready;

    public VaultCredentialStore(string? dataRoot = null, string? binaryPath = null)
    {
        _dataRoot = AppDataPaths.Normalize(dataRoot ?? AppDataPaths.ResolveDataRoot());
        _binary = string.IsNullOrWhiteSpace(binaryPath)
            ? ResolveBinary()
            : Path.GetFullPath(binaryPath);
        _anchorPath = Path.Combine(_dataRoot, "vault.auth");
    }

    public string DataRoot => _dataRoot;
    public string BinaryPath => _binary;

    public async Task<AccountCredential?> GetCredentialAsync(
        string accountId,
        CancellationToken cancellationToken = default)
    {
        await EnsureReadyAsync(cancellationToken).ConfigureAwait(false);
        var encoded = await GetCoreAsync(accountId, cancellationToken).ConfigureAwait(false);
        return encoded;
    }

    public async Task SaveCredentialAsync(
        AccountCredential credential,
        CancellationToken cancellationToken = default)
    {
        await EnsureReadyAsync(cancellationToken).ConfigureAwait(false);
        await SaveCoreAsync(credential, cancellationToken).ConfigureAwait(false);
    }

    public async Task DeleteCredentialAsync(
        string accountId,
        CancellationToken cancellationToken = default)
    {
        await EnsureReadyAsync(cancellationToken).ConfigureAwait(false);
        await using var session = await VaultSession.OpenAsync(
            _binary,
            _dataRoot,
            _anchorPath,
            cancellationToken).ConfigureAwait(false);
        await session.SendAsync(
            $"delete {CredentialUid(accountId)}",
            payload: null,
            cancellationToken).ConfigureAwait(false);
    }

    private async Task EnsureReadyAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_ready) return;
            if (!File.Exists(_binary))
            {
                throw new CredentialVaultException(
                    $"credential vault binary not found: {_binary}");
            }
            Directory.CreateDirectory(_dataRoot);

            var status = await RunCliAsync(["status", _dataRoot], cancellationToken)
                .ConfigureAwait(false);
            var initialized = status.ExitCode == 0 &&
                              status.Stdout.Contains("\"initialized\":true", StringComparison.Ordinal);
            if (!initialized)
            {
                // Prefer the OS secure store. Headless Linux / missing Secret
                // Service falls back to a user-only secret.key file.
                var init = await RunCliAsync(["init-crypto", _dataRoot], cancellationToken)
                    .ConfigureAwait(false);
                if (init.ExitCode != 0)
                {
                    init = await RunCliAsync(["init-file", _dataRoot], cancellationToken)
                        .ConfigureAwait(false);
                }
                if (init.ExitCode != 0)
                {
                    throw new CredentialVaultException(
                        $"credential vault initialization failed: {Clean(init.Stdout)}");
                }
                var anchor = ParseAnchor(init.Stdout)
                    ?? throw new CredentialVaultException(
                        "credential vault initialization returned no anchor");
                await WriteAnchorAsync(anchor, cancellationToken).ConfigureAwait(false);
            }

            _ready = true;
            await MigrateLegacyCredentialsAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<AccountCredential?> GetCoreAsync(
        string accountId,
        CancellationToken cancellationToken)
    {
        await using var session = await VaultSession.OpenAsync(
            _binary,
            _dataRoot,
            _anchorPath,
            cancellationToken).ConfigureAwait(false);
        var response = await session.SendAsync(
            $"get {CredentialUid(accountId)}",
            payload: null,
            cancellationToken).ConfigureAwait(false);
        if (response == "null" || string.IsNullOrEmpty(response)) return null;
        try
        {
            var bytes = Convert.FromBase64String(response);
            var dto = JsonSerializer.Deserialize(bytes, VaultCredentialJsonContext.Default.VaultCredentialDto);
            return dto?.ToDomain();
        }
        catch (Exception exception) when (
            exception is FormatException or JsonException or ArgumentException)
        {
            throw new CredentialVaultException(
                $"credential vault returned malformed data: {exception.Message}");
        }
    }

    private async Task SaveCoreAsync(
        AccountCredential credential,
        CancellationToken cancellationToken)
    {
        var dto = VaultCredentialDto.FromDomain(credential);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(dto, VaultCredentialJsonContext.Default.VaultCredentialDto);
        var payload = Convert.ToBase64String(bytes);
        await using var session = await VaultSession.OpenAsync(
            _binary,
            _dataRoot,
            _anchorPath,
            cancellationToken).ConfigureAwait(false);
        await session.SendAsync(
            $"set {CredentialUid(credential.AccountId)}",
            payload,
            cancellationToken).ConfigureAwait(false);
    }

    private async Task MigrateLegacyCredentialsAsync(CancellationToken cancellationToken)
    {
        var legacy = Path.Combine(_dataRoot, "accounts.credentials.json");
        if (!File.Exists(legacy)) return;

        try
        {
            using var document = JsonDocument.Parse(
                await File.ReadAllTextAsync(legacy, cancellationToken).ConfigureAwait(false));
            if (document.RootElement.ValueKind == JsonValueKind.Object &&
                document.RootElement.TryGetProperty("items", out var items) &&
                items.ValueKind == JsonValueKind.Array)
            {
                foreach (var element in items.EnumerateArray())
                {
                    var credential = ParseLegacy(element);
                    if (credential is not null)
                    {
                        await SaveCoreAsync(credential, cancellationToken).ConfigureAwait(false);
                    }
                }
            }

            // Do not leave a plaintext backup behind after a complete migration.
            File.Delete(legacy);
            var temporary = legacy + ".tmp";
            if (File.Exists(temporary)) File.Delete(temporary);
        }
        catch (Exception exception) when (
            exception is IOException or JsonException or UnauthorizedAccessException)
        {
            // A malformed legacy file must not prevent vault initialization.
            // It stays in place for manual diagnosis; no plaintext is imported.
        }
    }

    private async Task WriteAnchorAsync(string anchor, CancellationToken cancellationToken)
    {
        var temporary = _anchorPath + ".tmp";
        await File.WriteAllTextAsync(temporary, anchor, cancellationToken).ConfigureAwait(false);
        RestrictToCurrentUser(temporary);
        File.Move(temporary, _anchorPath, overwrite: true);
        RestrictToCurrentUser(_anchorPath);
    }

    private async Task<CliResult> RunCliAsync(
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = _binary,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);
        SanitizeEnvironment(startInfo);

        using var process = Process.Start(startInfo)
            ?? throw new CredentialVaultException("credential vault could not start");
        var stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderr = process.StandardError.ReadToEndAsync(cancellationToken);
        try
        {
            await process.WaitForExitAsync(cancellationToken)
                .WaitAsync(CliTimeout, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            TryKill(process);
            throw new CredentialVaultException("credential vault command timed out");
        }
        return new CliResult(
            process.ExitCode,
            await stdout.ConfigureAwait(false),
            await stderr.ConfigureAwait(false));
    }

    private static AccountCredential? ParseLegacy(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object) return null;
        var accountId = Text(element, "accountId");
        if (string.IsNullOrWhiteSpace(accountId)) return null;
        return new AccountCredential(
            accountId,
            Text(element, "microsoftRefreshToken"),
            Text(element, "minecraftAccessToken"),
            Date(element, "minecraftAccessTokenExpiresAt"),
            Text(element, "certificatesJson"),
            Date(element, "updatedAt") ?? DateTimeOffset.UtcNow);
    }

    private static string? ParseAnchor(string output)
    {
        var line = output.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault();
        if (line is null || !line.StartsWith("ok ", StringComparison.Ordinal)) return null;
        return line[3..].Trim();
    }

    private static string CredentialUid(string accountId) => $"account:{accountId}";

    private static string? Text(JsonElement element, string name)
        => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static DateTimeOffset? Date(JsonElement element, string name)
        => DateTimeOffset.TryParse(Text(element, name), out var value) ? value : null;

    private static string Clean(string value)
    {
        var text = value.Trim();
        return text.Length > 300 ? text[..300] : text;
    }

    private static void SanitizeEnvironment(ProcessStartInfo startInfo)
    {
        foreach (var name in new[]
                 {
                     "LD_PRELOAD", "LD_AUDIT", "LD_LIBRARY_PATH",
                     "DYLD_INSERT_LIBRARIES", "DYLD_LIBRARY_PATH", "DYLD_FRAMEWORK_PATH",
                 })
        {
            startInfo.Environment.Remove(name);
        }
    }

    private static void RestrictToCurrentUser(string path)
    {
        if (OperatingSystem.IsWindows()) return;
        try
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            // Best effort; the data directory is user-scoped.
        }
    }

    private static string ResolveBinary()
    {
        string? cwd = null;
        var includeDevelopmentProbes = false;
#if DEBUG
        includeDevelopmentProbes = true;
        try
        {
            cwd = Directory.GetCurrentDirectory();
        }
        catch
        {
            // Deleted/replaced working directory; packaged paths still work.
        }
#endif

        var candidates = BuildBinaryCandidates(
            AppContext.BaseDirectory,
            cwd,
            includeDevelopmentProbes);
        return SelectBinary(
                   Environment.GetEnvironmentVariable("ARCHOERACX_VAULT_BIN"),
                   candidates)
               ?? Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, ExecutableName()));
    }

    private static string ExecutableName()
        => OperatingSystem.IsWindows() ? "archoera-vault.exe" : "archoera-vault";

    /// <summary>
    /// Candidate locations in precedence order. Release builds never probe the
    /// working directory: a writable CWD must not be able to supply the vault
    /// executable. Pure so tests can lock in the order.
    /// </summary>
    internal static IReadOnlyList<string> BuildBinaryCandidates(
        string baseDirectory,
        string? currentDirectory,
        bool includeDevelopmentProbes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseDirectory);
        var executable = ExecutableName();
        var candidates = new List<string>
        {
            Path.Combine(baseDirectory, executable),
            Path.Combine(baseDirectory, "lib", executable),
            Path.Combine(baseDirectory, "..", executable),
            Path.Combine(baseDirectory, "..", "..", executable),
            Path.Combine(baseDirectory, "..", "..", "..", executable),
        };

        if (includeDevelopmentProbes && !string.IsNullOrWhiteSpace(currentDirectory))
        {
            candidates.Add(Path.Combine(currentDirectory, "build", "native", executable));
            candidates.Add(Path.Combine(currentDirectory, "app", "build", "native", executable));
        }

        return candidates;
    }

    /// <summary>
    /// Returns the explicit override when it is an existing file, otherwise the
    /// first existing candidate. Non-files (missing paths, directories, broken
    /// links) are ignored.
    /// </summary>
    internal static string? SelectBinary(
        string? configuredPath,
        IEnumerable<string> candidates)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        if (TryExistingFile(configuredPath) is { } explicitPath)
        {
            return explicitPath;
        }

        foreach (var candidate in candidates)
        {
            if (TryExistingFile(candidate) is { } resolved)
            {
                return resolved;
            }
        }

        return null;
    }

    private static string? TryExistingFile(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        try
        {
            var full = Path.GetFullPath(path);
            return File.Exists(full) ? full : null;
        }
        catch (Exception exception) when (
            exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
            // Already exited.
        }
    }

    private sealed record CliResult(int ExitCode, string Stdout, string Stderr);

    private sealed class VaultSession : IAsyncDisposable
    {
        private readonly Process _process;
        private readonly StreamWriter _input;
        private readonly StreamReader _output;
        private bool _closed;

        private VaultSession(Process process)
        {
            _process = process;
            _input = process.StandardInput;
            _output = process.StandardOutput;
        }

        public static async Task<VaultSession> OpenAsync(
            string binary,
            string dataRoot,
            string anchorPath,
            CancellationToken cancellationToken)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = binary,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            startInfo.ArgumentList.Add("serve");
            startInfo.ArgumentList.Add(dataRoot);
            SanitizeEnvironment(startInfo);

            var process = Process.Start(startInfo)
                ?? throw new CredentialVaultException("credential vault could not start");
            var session = new VaultSession(process);
            // Consume stderr so a verbose vault cannot block on a full pipe.
            _ = process.StandardError.ReadToEndAsync(cancellationToken);

            try
            {
                var h = RandomNumberGenerator.GetBytes(32);
                var c = RandomNumberGenerator.GetBytes(16);
                var line = $"handshake {Convert.ToBase64String(h)} {Convert.ToBase64String(c)}";
                await session.SendLineAsync(line, cancellationToken).ConfigureAwait(false);
                var response = await session.ReadLineAsync(cancellationToken).ConfigureAwait(false)
                    ?? throw new CredentialVaultException("credential vault closed during handshake");
                var parts = response.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 4 || parts[0] != "ok" || parts[1] != "handshake")
                {
                    throw new CredentialVaultException(
                        $"credential vault rejected handshake: {Clean(response)}");
                }
                if (parts.Length < 5 ||
                    !parts[4].StartsWith(ProductMarker, StringComparison.Ordinal))
                {
                    throw new CredentialVaultException(
                        "credential vault build marker is missing or not a production build");
                }

                using var hmac = new HMACSHA256(h);
                var expected = hmac.ComputeHash(c);
                var actual = Convert.FromBase64String(parts[3]);
                if (!CryptographicOperations.FixedTimeEquals(expected, actual))
                {
                    throw new CredentialVaultException("credential vault handshake MAC mismatch");
                }

                var anchor = parts[2];
                if (File.Exists(anchorPath))
                {
                    var expectedAnchor = (await File.ReadAllTextAsync(anchorPath, cancellationToken)
                        .ConfigureAwait(false)).Trim();
                    if (!string.Equals(expectedAnchor, anchor, StringComparison.Ordinal))
                    {
                        throw new CredentialVaultException(
                            "credential vault anchor mismatch; refusing to use this vault");
                    }
                }
                else
                {
                    await File.WriteAllTextAsync(anchorPath, anchor, cancellationToken)
                        .ConfigureAwait(false);
                    RestrictToCurrentUser(anchorPath);
                }
                return session;
            }
            catch
            {
                await session.DisposeAsync().ConfigureAwait(false);
                throw;
            }
        }

        public async Task<string> SendAsync(
            string command,
            string? payload,
            CancellationToken cancellationToken)
        {
            await SendLineAsync(command, cancellationToken).ConfigureAwait(false);
            if (payload is not null)
            {
                await SendLineAsync(payload, cancellationToken).ConfigureAwait(false);
            }
            var response = await ReadLineAsync(cancellationToken).ConfigureAwait(false)
                ?? throw new CredentialVaultException("credential vault closed unexpectedly");
            if (response.StartsWith("err ", StringComparison.Ordinal))
            {
                throw new CredentialVaultException(response[4..]);
            }
            return response.StartsWith("ok", StringComparison.Ordinal)
                ? response.Length > 2 ? response[3..] : string.Empty
                : response;
        }

        private async Task SendLineAsync(string line, CancellationToken cancellationToken)
        {
            await _input.WriteLineAsync(line).WaitAsync(CliTimeout, cancellationToken)
                .ConfigureAwait(false);
            await _input.FlushAsync(cancellationToken).ConfigureAwait(false);
        }

        private async Task<string?> ReadLineAsync(CancellationToken cancellationToken)
            => await _output.ReadLineAsync(cancellationToken)
                .AsTask()
                .WaitAsync(CliTimeout, cancellationToken)
                .ConfigureAwait(false);

        public async ValueTask DisposeAsync()
        {
            if (_closed) return;
            _closed = true;
            try
            {
                await SendLineAsync("quit", CancellationToken.None).ConfigureAwait(false);
                await _process.WaitForExitAsync(CancellationToken.None)
                    .WaitAsync(TimeSpan.FromSeconds(3), CancellationToken.None)
                    .ConfigureAwait(false);
            }
            catch
            {
                TryKill(_process);
            }
            _process.Dispose();
        }
    }
}

internal sealed record VaultCredentialDto(
    string AccountId,
    string? MicrosoftRefreshToken,
    string? MinecraftAccessToken,
    DateTimeOffset? MinecraftAccessTokenExpiresAt,
    string? CertificatesJson,
    DateTimeOffset UpdatedAt)
{
    public static VaultCredentialDto FromDomain(AccountCredential credential) => new(
        credential.AccountId,
        credential.MicrosoftRefreshToken,
        credential.MinecraftAccessToken,
        credential.MinecraftAccessTokenExpiresAt,
        credential.CertificatesJson,
        credential.UpdatedAt);

    public AccountCredential ToDomain() => new(
        AccountId,
        MicrosoftRefreshToken,
        MinecraftAccessToken,
        MinecraftAccessTokenExpiresAt,
        CertificatesJson,
        UpdatedAt);
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(VaultCredentialDto))]
internal partial class VaultCredentialJsonContext : JsonSerializerContext;

public sealed class CredentialVaultException : Exception
{
    public CredentialVaultException(string message) : base(message) { }
}
