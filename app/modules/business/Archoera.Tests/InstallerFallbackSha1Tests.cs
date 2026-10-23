using System.Net;
using System.Text;
using Archoera.Application.Packs;
using Archoera.Infrastructure.Packs;

namespace Archoera.Tests;

/// <summary>
/// The BMCLAPI installer bootstrap must never trust mirror bytes: the
/// downloaded installer is hashed while it is written and checked against the
/// sidecar digest. A mismatch throws the file away and retries through the
/// official channel (ForgeMavenMirror's caller-computes-the-SHA-1 contract).
/// </summary>
public sealed class InstallerFallbackSha1Tests
{
    private static readonly Uri OfficialUri = new(
        "https://maven.minecraftforge.net/net/minecraftforge/forge/" +
        "1.20.1-47.4.18/forge-1.20.1-47.4.18-installer.jar");

    [Fact]
    public async Task Mirror_bytes_matching_the_published_sha1_are_accepted()
    {
        var bytes = FakeInstaller("good");
        var handler = new FakeHttpMessageHandler();
        handler.Enqueue(() => Response(bytes));

        var destination = TempFile();
        try
        {
            var sha1 = await PackForgeInstallerBootstrap.DownloadInstallerFallbackAsync(
                new HttpClient(handler),
                OfficialUri,
                destination,
                Sha1(bytes),
                CancellationToken.None);

            Assert.Equal(Sha1(bytes), sha1);
            Assert.Equal(bytes, await File.ReadAllBytesAsync(destination));
            Assert.Single(handler.Requests);
        }
        finally
        {
            Delete(destination);
        }
    }

    [Fact]
    public async Task Mirror_bytes_with_a_mismatched_sha1_fall_back_to_the_official_channel()
    {
        var tampered = FakeInstaller("tampered");
        var official = FakeInstaller("official");
        var handler = new FakeHttpMessageHandler();
        handler.Enqueue(() => Response(tampered));
        handler.Enqueue(() => Response(official));

        var destination = TempFile();
        try
        {
            var sha1 = await PackForgeInstallerBootstrap.DownloadInstallerFallbackAsync(
                new HttpClient(handler),
                OfficialUri,
                destination,
                Sha1(official),
                CancellationToken.None);

            Assert.Equal(Sha1(official), sha1);
            Assert.Equal(official, await File.ReadAllBytesAsync(destination));
            Assert.Equal(2, handler.Requests.Count);
            // The mirror was consulted first; only the official channel restored it.
            Assert.NotEqual(OfficialUri.Host, handler.Requests[0].Uri.Host);
            Assert.Equal(OfficialUri.Host, handler.Requests[1].Uri.Host);
        }
        finally
        {
            Delete(destination);
        }
    }

    [Fact]
    public async Task Mismatched_mirror_bytes_never_survive_a_failed_official_retry()
    {
        var tampered = FakeInstaller("tampered");
        var handler = new FakeHttpMessageHandler();
        handler.Enqueue(() => Response(tampered));
        handler.EnqueueStatus(HttpStatusCode.ServiceUnavailable);

        var destination = TempFile();
        try
        {
            var exception = await Assert.ThrowsAsync<PackException>(
                () => PackForgeInstallerBootstrap.DownloadInstallerFallbackAsync(
                    new HttpClient(handler),
                    OfficialUri,
                    destination,
                    Sha1(FakeInstaller("official")),
                    CancellationToken.None));

            Assert.Equal(PackErrorCode.InstallerFailed, exception.Code);
            // The tampered mirror payload was deleted before the official
            // retry, and the failed retry must not leave anything behind.
            Assert.False(File.Exists(destination));
        }
        finally
        {
            Delete(destination);
        }
    }

    [Fact]
    public async Task Official_bytes_that_also_mismatch_are_rejected_and_deleted()
    {
        var tampered = FakeInstaller("tampered");
        var alsoWrong = FakeInstaller("also-wrong");
        var handler = new FakeHttpMessageHandler();
        handler.Enqueue(() => Response(tampered));
        handler.Enqueue(() => Response(alsoWrong));

        var destination = TempFile();
        try
        {
            var exception = await Assert.ThrowsAsync<PackException>(
                () => PackForgeInstallerBootstrap.DownloadInstallerFallbackAsync(
                    new HttpClient(handler),
                    OfficialUri,
                    destination,
                    Sha1("expected-but-absent"u8.ToArray()),
                    CancellationToken.None));

            Assert.Equal(PackErrorCode.InstallerFailed, exception.Code);
            // The mismatching installer never stays on disk.
            Assert.False(File.Exists(destination));
        }
        finally
        {
            Delete(destination);
        }
    }

    [Fact]
    public async Task Without_a_published_digest_the_computed_sha1_is_returned()
    {
        var bytes = FakeInstaller("unverified");
        var handler = new FakeHttpMessageHandler();
        handler.Enqueue(() => Response(bytes));

        var destination = TempFile();
        try
        {
            var sha1 = await PackForgeInstallerBootstrap.DownloadInstallerFallbackAsync(
                new HttpClient(handler),
                OfficialUri,
                destination,
                expectedSha1: null,
                CancellationToken.None);

            // The computed digest feeds the installer descriptor of the next
            // combined plan, exactly like the primary path.
            Assert.Equal(Sha1(bytes), sha1);
            Assert.Equal(bytes, await File.ReadAllBytesAsync(destination));
        }
        finally
        {
            Delete(destination);
        }
    }

    private static byte[] FakeInstaller(string tag)
        => [0x50, 0x4B, 0x03, 0x04, .. Encoding.UTF8.GetBytes(tag)];

    private static string Sha1(byte[] bytes)
        => Convert.ToHexStringLower(System.Security.Cryptography.SHA1.HashData(bytes));

    private static HttpResponseMessage Response(byte[] bytes)
        => new(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(bytes),
        };

    private static string TempFile()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            "archoera-installer-fallback",
            Guid.NewGuid().ToString("N") + ".jar");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        return path;
    }

    private static void Delete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch
        {
            // best effort
        }
    }
}
