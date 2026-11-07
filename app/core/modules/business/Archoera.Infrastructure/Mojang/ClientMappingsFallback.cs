using System.Security.Cryptography;
using System.Text.Json;
using Archoera.Application.Loaders;
using Archoera.Application.Mojang;

namespace Archoera.Infrastructure.Mojang;

/// <summary>
/// Resolves <c>client_mappings</c> from the vanilla version JSON and writes it
/// where the DOWNLOAD_MOJMAPS processor would have written it.
/// </summary>
public sealed class ClientMappingsFallback : IMojMapsFallback
{
    private readonly IMojangMetadataClient _mojang;
    private readonly HttpClient _http;

    public ClientMappingsFallback(IMojangMetadataClient mojang, HttpClient http)
    {
        _mojang = mojang ?? throw new ArgumentNullException(nameof(mojang));
        _http = http ?? throw new ArgumentNullException(nameof(http));
    }

    public async Task<bool> TryResolveAsync(
        string gameVersion,
        string targetPath,
        CancellationToken cancellationToken)
    {
        try
        {
            var json = await _mojang
                .GetVersionJsonAsync(gameVersion, cancellationToken)
                .ConfigureAwait(false);
            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("downloads", out var downloads) ||
                downloads.ValueKind != JsonValueKind.Object ||
                !downloads.TryGetProperty("client_mappings", out var mappings) ||
                mappings.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            var url = ReadString(mappings, "url");
            var sha1 = ReadString(mappings, "sha1");
            var size = ReadSize(mappings);
            if (string.IsNullOrWhiteSpace(url) ||
                string.IsNullOrWhiteSpace(sha1) ||
                size is null or <= 0 ||
                !Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
                uri.Scheme != Uri.UriSchemeHttps)
            {
                return false;
            }

            using var response = await _http
                .GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            var bytes = await response.Content
                .ReadAsByteArrayAsync(cancellationToken)
                .ConfigureAwait(false);

            if (bytes.Length != size.Value)
            {
                return false;
            }

            var actual = Convert.ToHexString(SHA1.HashData(bytes)).ToLowerInvariant();
            if (!string.Equals(actual, sha1.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var directory = Path.GetDirectoryName(targetPath);
            if (string.IsNullOrEmpty(directory))
            {
                return false;
            }

            Directory.CreateDirectory(directory);
            var temporary = $"{targetPath}.tmp-{Guid.NewGuid():N}";
            try
            {
                await File.WriteAllBytesAsync(temporary, bytes, cancellationToken)
                    .ConfigureAwait(false);
                File.Move(temporary, targetPath, overwrite: true);
            }
            catch
            {
                TryDelete(temporary);
                throw;
            }

            return true;
        }
        catch (Exception exception) when (
            !cancellationToken.IsCancellationRequested &&
            exception is HttpRequestException or IOException or JsonException
                or MojangException or InvalidOperationException
                or UnauthorizedAccessException or NotSupportedException)
        {
            return false;
        }
    }

    private static string? ReadString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static long? ReadSize(JsonElement element) =>
        element.TryGetProperty("size", out var value) &&
        value.ValueKind == JsonValueKind.Number &&
        value.TryGetInt64(out var size)
            ? size
            : null;

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
            // best effort
        }
        catch (UnauthorizedAccessException)
        {
            // best effort
        }
    }
}
