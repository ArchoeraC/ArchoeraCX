using System.Security.Cryptography;
using System.Text.Json;
using Archoera.Application.Accounts;
using Archoera.Infrastructure.Settings;

namespace Archoera.Infrastructure.Accounts;

/// <summary>
/// Downloads and caches the authlib-injector jar. The jar is fetched from the
/// official artifact metadata (with the BMCLAPI mirror as fallback) and
/// verified against its published SHA-256. Nothing is bundled or redistributed
/// by this repository; the download starts only after the user accepted the
/// third-party server terms in <c>account.login.authlib</c>.
/// </summary>
internal sealed class AuthlibInjectorManager
{
    private static readonly Uri OfficialLatest =
        new("https://authlib-injector.yushi.moe/artifact/latest.json");

    private static readonly Uri MirrorLatest =
        new("https://bmclapi2.bangbang93.com/mirrors/authlib-injector/artifact/latest.json");

    private const int MaxLatestBytes = 256 * 1024;
    private const int MaxJarBytes = 32 * 1024 * 1024;
    private static readonly HttpClient SharedHttp = new()
    {
        Timeout = TimeSpan.FromMinutes(2),
    };

    private readonly string _directory;
    private readonly HttpClient _http;
    private readonly IReadOnlyList<Uri> _latestUris;

    public AuthlibInjectorManager(
        string? dataRoot = null,
        HttpClient? http = null,
        IReadOnlyList<Uri>? latestUris = null)
    {
        var root = AppDataPaths.Normalize(dataRoot ?? AppDataPaths.ResolveDataRoot());
        _directory = Path.Combine(root, "authlib-injector");
        _http = http ?? SharedHttp;
        _latestUris = latestUris ?? [OfficialLatest, MirrorLatest];
    }

    /// <summary>Returns a verified local jar path, downloading it if needed.</summary>
    public async Task<string> EnsureAsync(CancellationToken cancellationToken)
    {
        var latest = await ReadLatestAsync(cancellationToken).ConfigureAwait(false);
        var target = Path.Combine(
            _directory,
            $"authlib-injector-{latest.Version}.jar");
        if (File.Exists(target) && await MatchesAsync(target, latest.Sha256, cancellationToken)
                .ConfigureAwait(false))
        {
            return target;
        }

        Directory.CreateDirectory(_directory);
        var temporary = $"{target}.{Guid.NewGuid():N}.tmp";
        try
        {
            var candidates = DownloadCandidates(latest.DownloadUrl);
            Exception? last = null;
            foreach (var uri in candidates)
            {
                try
                {
                    await DownloadAsync(uri, temporary, latest.Sha256, cancellationToken)
                        .ConfigureAwait(false);
                    File.Move(temporary, target, overwrite: true);
                    return target;
                }
                catch (Exception exception) when (
                    exception is IOException or HttpRequestException or
                    AccountAuthException or TaskCanceledException)
                {
                    last = exception;
                    if (cancellationToken.IsCancellationRequested) throw;
                }
            }

            throw new AccountAuthException(
                "AUTHLIB_JAR_DOWNLOAD_FAILED",
                "Could not download authlib-injector.",
                last?.Message);
        }
        finally
        {
            try
            {
                if (File.Exists(temporary)) File.Delete(temporary);
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException)
            {
                // Best effort; the next download overwrites the temp path.
            }
        }
    }

    private async Task<LatestArtifact> ReadLatestAsync(CancellationToken cancellationToken)
    {
        Exception? last = null;
        foreach (var uri in _latestUris)
        {
            try
            {
                using var response = await _http
                    .GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                    .ConfigureAwait(false);
                if (!response.IsSuccessStatusCode) continue;
                var json = await ReadBoundedAsync(
                        response,
                        MaxLatestBytes,
                        cancellationToken)
                    .ConfigureAwait(false);
                using var document = JsonDocument.Parse(json);
                var root = document.RootElement;
                var version = String(root, "version");
                var url = String(root, "download_url");
                var sha256 = root.TryGetProperty("checksums", out var checksums) &&
                             checksums.ValueKind == JsonValueKind.Object
                    ? String(checksums, "sha256")
                    : null;
                if (string.IsNullOrWhiteSpace(version) ||
                    version.Length > 64 ||
                    !version.All(character =>
                        char.IsAsciiLetterOrDigit(character) ||
                        character is '.' or '_' or '-') ||
                    string.IsNullOrWhiteSpace(url) ||
                    !Uri.TryCreate(url, UriKind.Absolute, out var download) ||
                    download.Scheme != Uri.UriSchemeHttps ||
                    string.IsNullOrWhiteSpace(sha256) ||
                    sha256.Length != 64 ||
                    !sha256.All(IsHex))
                {
                    continue;
                }

                return new LatestArtifact(version, download, sha256.ToLowerInvariant());
            }
            catch (Exception exception) when (
                exception is IOException or HttpRequestException or JsonException or
                TaskCanceledException)
            {
                last = exception;
                if (cancellationToken.IsCancellationRequested) throw;
            }
        }

        throw new AccountAuthException(
            "AUTHLIB_JAR_DOWNLOAD_FAILED",
            "Authlib-injector metadata is unavailable.",
            last?.Message);
    }

    private async Task DownloadAsync(
        Uri uri,
        string path,
        string expectedSha256,
        CancellationToken cancellationToken)
    {
        using var response = await _http
            .GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using var input = await response.Content
            .ReadAsStreamAsync(cancellationToken)
            .ConfigureAwait(false);
        await using var output = new FileStream(
            path,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[64 * 1024];
        long total = 0;
        while (true)
        {
            var read = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read == 0) break;
            total += read;
            if (total > MaxJarBytes)
            {
                throw new AccountAuthException(
                    "AUTHLIB_JAR_DOWNLOAD_FAILED",
                    "Authlib-injector is larger than expected.");
            }

            hash.AppendData(buffer, 0, read);
            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken)
                .ConfigureAwait(false);
        }

        var actual = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
        if (!string.Equals(actual, expectedSha256, StringComparison.Ordinal))
        {
            throw new AccountAuthException(
                "AUTHLIB_JAR_DOWNLOAD_FAILED",
                "Authlib-injector failed SHA-256 verification.");
        }
    }

    private async Task<bool> MatchesAsync(
        string path,
        string expectedSha256,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read);
            var hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
            return string.Equals(
                Convert.ToHexString(hash).ToLowerInvariant(),
                expectedSha256,
                StringComparison.Ordinal);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static IEnumerable<Uri> DownloadCandidates(Uri official)
    {
        yield return official;
        if (official.Host.Contains("authlib-injector.yushi.moe", StringComparison.OrdinalIgnoreCase))
        {
            yield return new Uri(official.AbsoluteUri.Replace(
                "authlib-injector.yushi.moe",
                "bmclapi2.bangbang93.com/mirrors/authlib-injector",
                StringComparison.Ordinal));
        }
    }

    private static async Task<string> ReadBoundedAsync(
        HttpResponseMessage response,
        int maxBytes,
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
            if (memory.Length + read > maxBytes)
            {
                throw new IOException("authlib-injector metadata is too large");
            }

            memory.Write(buffer, 0, read);
        }

        return System.Text.Encoding.UTF8.GetString(memory.ToArray());
    }

    private static string? String(JsonElement root, string name)
        => root.TryGetProperty(name, out var element) &&
           element.ValueKind == JsonValueKind.String
            ? element.GetString()
            : null;

    private static bool IsHex(char value)
        => value is (>= '0' and <= '9') or (>= 'a' and <= 'f') or (>= 'A' and <= 'F');

    private sealed record LatestArtifact(string Version, Uri DownloadUrl, string Sha256);
}
