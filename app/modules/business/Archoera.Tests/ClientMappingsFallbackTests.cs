using System.Net;
using System.Security.Cryptography;
using System.Text;
using Archoera.Application.Mojang;
using Archoera.Domain.Mojang;
using Archoera.Infrastructure.Mojang;

namespace Archoera.Tests;

public sealed class ClientMappingsFallbackTests
{
    private static readonly byte[] Mappings =
        Encoding.UTF8.GetBytes("synthetic client mappings\n");

    [Fact]
    public async Task Writes_the_mappings_when_hash_and_size_match()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "archoera-tests",
            $"client-mappings-{Guid.NewGuid():N}");
        var target = Path.Combine(directory, "client.txt");
        try
        {
            var handler = new FakeHttpMessageHandler();
            handler.Enqueue(() => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(Mappings),
            });
            var fallback = new ClientMappingsFallback(
                new StubMojang(VersionJson(Sha1(), Mappings.Length)),
                new HttpClient(handler));

            Assert.True(await fallback.TryResolveAsync("1.21.1", target, default));
            Assert.Equal(Mappings, await File.ReadAllBytesAsync(target));
        }
        finally
        {
            Delete(directory);
        }
    }

    [Fact]
    public async Task Rejects_mappings_that_do_not_match_their_hash()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "archoera-tests",
            $"client-mappings-{Guid.NewGuid():N}");
        var target = Path.Combine(directory, "client.txt");
        try
        {
            var handler = new FakeHttpMessageHandler();
            handler.Enqueue(() => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(Mappings),
            });
            var fallback = new ClientMappingsFallback(
                new StubMojang(VersionJson(new string('0', 40), Mappings.Length)),
                new HttpClient(handler));

            Assert.False(await fallback.TryResolveAsync("1.21.1", target, default));
            Assert.False(File.Exists(target));
        }
        finally
        {
            Delete(directory);
        }
    }

    private static string VersionJson(string sha1, int size) =>
        $$"""
        {
          "downloads": {
            "client_mappings": {
              "url": "https://example.invalid/client-mappings.txt",
              "sha1": "{{sha1}}",
              "size": {{size}}
            }
          }
        }
        """;

    private static string Sha1() =>
        Convert.ToHexString(SHA1.HashData(Mappings)).ToLowerInvariant();

    private static void Delete(string directory)
    {
        try
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
        catch (IOException)
        {
            // best effort
        }
    }

    private sealed class StubMojang : IMojangMetadataClient
    {
        private readonly string _versionJson;

        public StubMojang(string versionJson) => _versionJson = versionJson;

        public Task<MojangVersionManifest> GetManifestAsync(
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<MojangVersion> GetVersionAsync(
            string versionId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<byte[]> GetVersionJsonAsync(
            string versionId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Encoding.UTF8.GetBytes(_versionJson));

        public Task<MojangAssetIndex> GetAssetIndexAsync(
            MojangAssetIndexRef index,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
