using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Archoera.Domain.Downloads;
using Archoera.Domain.Loaders;
using Archoera.Infrastructure.Downloads;
using Archoera.Infrastructure.Packs;

namespace Archoera.Tests;

/// <summary>
/// The bootstrap writes the digest of the accepted installer next to it, so a
/// later repair can prove the bytes are unchanged before parsing or executing
/// them.
/// </summary>
public sealed class ForgeInstallerBootstrapTests
{
    [Fact]
    public async Task Downloaded_installer_gets_a_matching_sidecar()
    {
        var bytes = new byte[] { 0x50, 0x4B, 0x03, 0x04 }
            .Concat(Encoding.UTF8.GetBytes("fresh-installer"))
            .ToArray();
        var sha1 = Convert.ToHexStringLower(
            System.Security.Cryptography.SHA1.HashData(bytes));

        var handler = new FakeHttpMessageHandler();
        handler.Enqueue(() => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(sha1 + "\n", Encoding.UTF8, "text/plain"),
        });
        handler.Enqueue(() =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.PartialContent)
            {
                Content = new ByteArrayContent([]),
            };
            response.Content.Headers.ContentRange =
                new ContentRangeHeaderValue(0, 0, bytes.Length);
            return response;
        });

        var root = Path.Combine(
            Path.GetTempPath(),
            "archoera-installer-bootstrap",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var bootstrap = new PackForgeInstallerBootstrap(
                new PackGameInstallService(
                    null,
                    new HttpClient(handler),
                    LoaderProfileEndpoints.Default),
                new WritingRunner(bytes),
                new HttpClient(handler));

            var path = await bootstrap.EnsureForgeInstallerAsync(
                root,
                LoaderKind.Forge,
                "1.20.1",
                "47.4.18",
                CancellationToken.None);

            Assert.Equal(bytes, await File.ReadAllBytesAsync(path));
            Assert.Equal(sha1 + "\n", await File.ReadAllTextAsync(path + ".sha1"));
        }
        finally
        {
            try
            {
                if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
            }
            catch
            {
                // best effort
            }
        }
    }

    private sealed class WritingRunner(byte[] bytes) : IDownloadPlanRunner
    {
        public bool IsAvailable => true;

        public Task<PlanExecutionResult> ExecuteAsync(
            DownloadPlan plan,
            CancellationToken cancellationToken = default)
        {
            foreach (var file in plan.Files)
            {
                var path = Path.Combine(
                    plan.Root,
                    file.Dest.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllBytes(path, bytes);
            }

            return Task.FromResult(
                new PlanExecutionResult(
                    "completed",
                    true,
                    string.Empty,
                    string.Empty,
                    string.Empty,
                    0));
        }
    }
}
