using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Archoera.Application.JavaRuntime;
using Archoera.Domain.Downloads;

namespace Archoera.Infrastructure.Java.Distributions;

/// <summary>
/// Shared HTTP primitives for JDK catalogue clients. Responses are read with
/// a hard byte cap so a hostile or broken vendor endpoint cannot exhaust
/// memory before JSON parsing rejects it.
/// </summary>
internal static partial class JavaDistributionHttp
{
    public const string UserAgent =
        "ArchoeraCX/" + Archoera.Application.ArchoeraBuildInfo.Version +
        " (+https://github.com/BetaStudio2/ArchoeraCX)";

    private const int MaxCatalogueBytes = 16 * 1024 * 1024;
    private const int MaxHashFileBytes = 48 * 1024;

    public static async Task<JsonDocument> GetJsonAsync(
        HttpClient http,
        Uri uri,
        CancellationToken cancellationToken,
        int maxBytes = MaxCatalogueBytes)
    {
        var text = await GetTextAsync(http, uri, cancellationToken, maxBytes)
            .ConfigureAwait(false);
        try
        {
            return JsonDocument.Parse(text);
        }
        catch (JsonException exception)
        {
            throw new JavaDistributionException(
                "JAVA_CATALOGUE_INVALID",
                $"{uri.Host} returned invalid JSON: {exception.Message}");
        }
    }

    public static async Task<string> GetTextAsync(
        HttpClient http,
        Uri uri,
        CancellationToken cancellationToken,
        int maxBytes = MaxCatalogueBytes)
    {
        using var response = await http
            .GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new JavaDistributionException(
                "JAVA_CATALOGUE_UNAVAILABLE",
                $"{uri.Host} returned HTTP {(int)response.StatusCode}");
        }

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
                throw new JavaDistributionException(
                    "JAVA_CATALOGUE_TOO_LARGE",
                    $"{uri.Host} response exceeds {maxBytes} bytes");
            }

            memory.Write(buffer, 0, read);
        }

        return Encoding.UTF8.GetString(memory.GetBuffer(), 0, (int)memory.Length);
    }

    /// <summary>Reads the checksum text and returns the first SHA-256 token.</summary>
    public static async Task<string> GetSha256Async(
        HttpClient http,
        Uri uri,
        CancellationToken cancellationToken)
    {
        var text = await GetTextAsync(http, uri, cancellationToken, MaxHashFileBytes)
            .ConfigureAwait(false);
        var hash = ParseSha256(text);
        return hash ?? throw new JavaDistributionException(
            "JAVA_HASH_MISSING",
            $"{uri.Host} did not publish a valid SHA-256 digest");
    }

    /// <summary>Extracts the first digest of the expected length from a checksum file.</summary>
    public static string? ParseHash(DownloadHashAlgorithm algorithm, string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var length = algorithm switch
        {
            DownloadHashAlgorithm.Md5 => 32,
            DownloadHashAlgorithm.Sha1 => 40,
            DownloadHashAlgorithm.Sha256 => 64,
            DownloadHashAlgorithm.Sha512 => 128,
            _ => 0,
        };
        if (length == 0) return null;
        var match = Regex.Match(
            text,
            $"(?<![0-9a-fA-F])[0-9a-fA-F]{{{length}}}(?![0-9a-fA-F])");
        return match.Success ? match.Value.ToLowerInvariant() : null;
    }

    /// <summary>Extracts the first 64-hex token, as published in checksum files.</summary>
    public static string? ParseSha256(string? text)
        => ParseHash(DownloadHashAlgorithm.Sha256, text);

    /// <summary>
    /// HEAD request used only when a vendor catalogue omits the archive size.
    /// The redirect chain is followed by HttpClient, so the final length is
    /// the CDN payload, not the aka.ms stub.
    /// </summary>
    public static async Task<long> GetContentLengthAsync(
        HttpClient http,
        Uri uri,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Head, uri);
        using var response = await http
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new JavaDistributionException(
                "JAVA_CATALOGUE_UNAVAILABLE",
                $"{uri.Host} returned HTTP {(int)response.StatusCode}");
        }

        var length = response.Content.Headers.ContentLength;
        if (length is null or <= 0)
        {
            throw new JavaDistributionException(
                "JAVA_SIZE_MISSING",
                $"{uri.Host} did not report the archive size");
        }

        return length.Value;
    }

    public static Uri RequireHttps(string? value, string what)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps ||
            !string.IsNullOrEmpty(uri.UserInfo))
        {
            throw new JavaDistributionException(
                "JAVA_CATALOGUE_INVALID",
                $"{what} is not an HTTPS URL");
        }

        return uri;
    }

    /// <summary>
    /// Enforces the vendor's documented download host so a compromised
    /// catalogue response cannot redirect the executor to an arbitrary origin.
    /// </summary>
    public static Uri RequireAllowedHost(
        string? value,
        string what,
        params string[] allowedHosts)
    {
        var uri = RequireHttps(value, what);
        foreach (var host in allowedHosts)
        {
            if (string.Equals(uri.Host, host, StringComparison.OrdinalIgnoreCase))
            {
                return uri;
            }
        }

        throw new JavaDistributionException(
            "JAVA_CATALOGUE_INVALID",
            $"{what} points outside the vendor's download hosts");
    }

    public static bool IsSafeVersionId(string? value, int maxLength = 256)
        => !string.IsNullOrWhiteSpace(value) &&
           value.Length <= maxLength &&
           VersionIdPattern().IsMatch(value);

    [GeneratedRegex("^[A-Za-z0-9._+~|:-]+$")]
    private static partial Regex VersionIdPattern();
}
