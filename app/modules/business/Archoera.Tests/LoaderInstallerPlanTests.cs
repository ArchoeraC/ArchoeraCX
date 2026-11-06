using System.Net;
using System.Text.Json;
using Archoera.Infrastructure.Loaders;
using Archoera.Infrastructure.Modules;
using Archoera.Infrastructure.Settings;

namespace Archoera.Tests;

/// <summary>
/// The UI downloads the vendor installer before it can read the profile.
/// `loader.installerPlan` must stay a single-file plan rooted exactly where the
/// later `loader.plan` call expects the installer.
/// </summary>
public sealed class LoaderInstallerPlanTests
{
    [Fact]
    public async Task Plans_the_installer_inside_the_loader_root()
    {
        var handler = new FakeHttpMessageHandler();
        handler.Enqueue(() => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(ForgeInstallerFixture.Sha('a')),
        });
        handler.Enqueue(() => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(new byte[4_321]),
        });

        var baseDirectory = Path.Combine(
            Path.GetTempPath(),
            "archoera-tests",
            $"installer-plan-{Guid.NewGuid():N}");
        var instanceRoot = Path.Combine(baseDirectory, "instance");
        Directory.CreateDirectory(instanceRoot);

        try
        {
            var service = new LoaderModuleService(
                httpClient: new HttpClient(handler),
                settings: new JsonAppSettingsStore(
                    Path.Combine(baseDirectory, "data")),
                regionPolicy: new RegionPolicy(regionOverride: "GLOBAL"));
            var request = JsonSerializer.Serialize(new
            {
                root = instanceRoot,
                kind = "forge",
                gameVersion = "1.20.1",
                loaderVersion = "47.4.18",
                // Per-instance files keep the expected path unambiguous.
                shareLoaderFiles = false,
            });

            var response = await service.HandleAsync(
                "loader.installerPlan",
                request,
                CancellationToken.None);

            using var document = JsonDocument.Parse(response);
            Assert.True(document.RootElement.GetProperty("ok").GetBoolean());
            var data = document.RootElement.GetProperty("data");
            var files = data.GetProperty("plan").GetProperty("files");
            var file = Assert.Single(files.EnumerateArray());
            Assert.Equal(
                "downloads/loaders/forge/1.20.1-47.4.18-installer.jar",
                file.GetProperty("dest").GetString());
            Assert.Contains(
                "forge-1.20.1-47.4.18-installer.jar",
                file.GetProperty("url").GetString(),
                StringComparison.Ordinal);
            Assert.Equal(
                Path.Combine(
                    instanceRoot,
                    "downloads",
                    "loaders",
                    "forge",
                    "1.20.1-47.4.18-installer.jar"),
                data.GetProperty("installerPath").GetString());
        }
        finally
        {
            try
            {
                Directory.Delete(baseDirectory, recursive: true);
            }
            catch (IOException)
            {
                // best effort
            }
        }
    }

    [Fact]
    public async Task Rejects_a_request_without_a_root()
    {
        var service = new LoaderModuleService(
            settings: new JsonAppSettingsStore(
                Path.Combine(Path.GetTempPath(), "archoera-tests", "installer-plan")),
            regionPolicy: new RegionPolicy(regionOverride: "GLOBAL"));

        var response = await service.HandleAsync(
            "loader.installerPlan",
            "{}",
            CancellationToken.None);

        using var document = JsonDocument.Parse(response);
        Assert.False(document.RootElement.GetProperty("ok").GetBoolean());
    }
}
