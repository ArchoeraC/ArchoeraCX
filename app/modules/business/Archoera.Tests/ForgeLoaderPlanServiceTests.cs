using System.Net;
using Archoera.Application.Loaders;
using Archoera.Domain.Loaders;
using Archoera.Infrastructure.Loaders;

namespace Archoera.Tests;

public sealed class ForgeLoaderPlanServiceTests
{
    private static readonly string Root = Path.Combine(
        Path.GetTempPath(),
        "archoera-tests",
        "forge-loader-plan-service");

    [Theory]
    [InlineData(LoaderKind.Forge, "1.20.1", "47.2.0",
        "https://maven.minecraftforge.net/net/minecraftforge/forge/1.20.1-47.2.0/forge-1.20.1-47.2.0-installer.jar")]
    [InlineData(LoaderKind.NeoForge, "21.1.209", "21.1.209",
        "https://maven.neoforged.net/releases/net/neoforged/neoforge/21.1.209/neoforge-21.1.209-installer.jar")]
    [InlineData(LoaderKind.NeoForge, "1.21.1", "21.1.209",
        "https://maven.neoforged.net/releases/net/neoforged/neoforge/21.1.209/neoforge-21.1.209-installer.jar")]
    public void Installer_uses_the_official_maven_path(
        LoaderKind kind,
        string gameVersion,
        string loaderVersion,
        string expected)
    {
        Assert.Equal(
            expected,
            ForgeMavenUrls.Installer(kind, gameVersion, loaderVersion).AbsoluteUri);
    }

    [Fact]
    public void Installer_accepts_a_callers_mirror_root()
    {
        var uri = ForgeMavenUrls.Installer(
            LoaderKind.Forge,
            "1.20.1",
            "47.2.0",
            new Uri("https://mirror.example.invalid/maven"));

        Assert.Equal(
            "https://mirror.example.invalid/maven/net/minecraftforge/forge/1.20.1-47.2.0/forge-1.20.1-47.2.0-installer.jar",
            uri.AbsoluteUri);
    }

    [Fact]
    public void Sidecar_appends_sha1_to_the_path()
    {
        var installer = ForgeMavenUrls.Installer(LoaderKind.Forge, "1.20.1", "47.2.0");

        Assert.Equal(
            installer.AbsoluteUri + ".sha1",
            ForgeMavenUrls.Sidecar(installer).AbsoluteUri);
    }

    [Theory]
    [InlineData("../1.20.1", "47.2.0")]
    [InlineData("1.20.1", "47/2.0")]
    [InlineData("", "47.2.0")]
    public void Installer_rejects_unsafe_versions(string gameVersion, string loaderVersion)
    {
        var exception = Assert.Throws<ForgeDownloadException>(() =>
            ForgeMavenUrls.Installer(LoaderKind.Forge, gameVersion, loaderVersion));

        Assert.Equal(ForgeDownloadErrorCode.InvalidRequest, exception.Code);
    }

    [Fact]
    public async Task BuildAsync_uses_sidecar_metadata_and_the_installer_reader()
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

        using var http = new HttpClient(handler);
        var service = new ForgeLoaderPlanService(http);
        var bytes = ForgeInstallerFixture.CreateInstaller(
            $$"""
            [
              {{ForgeInstallerFixture.Library(
                  "net.minecraftforge:installertools:1.3.0",
                  "net/minecraftforge/installertools/1.3.0/installertools-1.3.0.jar",
                  "https://maven.minecraftforge.net/net/minecraftforge/installertools/1.3.0/installertools-1.3.0.jar",
                  ForgeInstallerFixture.Sha('b'),
                  1_234)}}
            ]
            """,
            "[]");

        var result = await service.BuildAsync(
            Root,
            LoaderKind.Forge,
            "1.20.1",
            "47.2.0",
            _ => Task.FromResult<Stream>(new MemoryStream(bytes)));

        var installer = Assert.Single(
            result.Plan.Files,
            file => file.Dest == "downloads/loaders/forge/1.20.1-47.2.0-installer.jar");
        Assert.Equal(ForgeInstallerFixture.ForgeInstallerUrl, installer.Url);
        Assert.Equal(ForgeInstallerFixture.Sha('a'), installer.Hash.Hex);
        Assert.Equal(4_321, installer.Size);

        Assert.Contains(
            result.Plan.Files,
            file => file.Dest ==
                    "libraries/net/minecraftforge/installertools/1.3.0/installertools-1.3.0.jar");

        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal("GET", handler.Requests[0].Method);
        Assert.Equal("HEAD", handler.Requests[1].Method);
    }

    [Fact]
    public async Task BuildAsync_without_a_reader_plans_the_installer_only()
    {
        var handler = new FakeHttpMessageHandler();
        handler.Enqueue(() => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(ForgeInstallerFixture.Sha('a')),
        });
        handler.Enqueue(() => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(new byte[1_024]),
        });

        using var http = new HttpClient(handler);
        var service = new ForgeLoaderPlanService(http);

        var result = await service.BuildAsync(
            Root,
            LoaderKind.NeoForge,
            "21.1.209",
            "21.1.209");

        Assert.Single(result.Plan.Files);
        Assert.Equal(
            "downloads/loaders/neoforge/21.1.209-21.1.209-installer.jar",
            result.Plan.Files[0].Dest);
        Assert.Contains(
            result.Warnings,
            warning => warning.Code == ForgeDownloadWarningCode.InstallerMetadataUnavailable);
    }

    [Fact]
    public async Task BuildFromInstallerPath_is_offline()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "archoera-tests",
            $"forge-service-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var installerPath = Path.Combine(directory, "forge-installer.jar");

        try
        {
            var bytes = ForgeInstallerFixture.CreateInstaller(
                $$"""
                [
                  {{ForgeInstallerFixture.Library(
                      "net.minecraftforge:binarypatcher:1.1.1",
                      "net/minecraftforge/binarypatcher/1.1.1/binarypatcher-1.1.1.jar",
                      "https://maven.minecraftforge.net/net/minecraftforge/binarypatcher/1.1.1/binarypatcher-1.1.1.jar",
                      ForgeInstallerFixture.Sha('b'),
                      222)}}
                ]
                """,
                "[]");
            await File.WriteAllBytesAsync(installerPath, bytes);

            var handler = new FakeHttpMessageHandler();
            using var http = new HttpClient(handler);
            var service = new ForgeLoaderPlanService(http);

            var result = await service.BuildFromInstallerPathAsync(
                Root,
                LoaderKind.Forge,
                "1.20.1",
                "47.2.0",
                installerPath);

            Assert.Empty(handler.Requests);
            Assert.Equal(2, result.Plan.Files.Count);

            var installer = Assert.Single(
                result.Plan.Files,
                file => file.Dest == "downloads/loaders/forge/1.20.1-47.2.0-installer.jar");
            Assert.Equal(ForgeInstallerFixture.ForgeInstallerUrl, installer.Url);
            Assert.Equal(bytes.LongLength, installer.Size);
            Assert.Equal(ForgeInstallerFixture.Sha1(bytes), installer.Hash.Hex);

            Assert.Contains(
                result.Plan.Files,
                file => file.Dest ==
                        "libraries/net/minecraftforge/binarypatcher/1.1.1/binarypatcher-1.1.1.jar");
        }
        finally
        {
            try
            {
                Directory.Delete(directory, recursive: true);
            }
            catch
            {
                // Best-effort cleanup.
            }
        }
    }

    [Fact]
    public async Task BuildFromInstallerPath_reports_a_missing_installer()
    {
        var handler = new FakeHttpMessageHandler();
        using var http = new HttpClient(handler);
        var service = new ForgeLoaderPlanService(http);

        var exception = await Assert.ThrowsAsync<ForgeDownloadException>(() =>
            service.BuildFromInstallerPathAsync(
                Root,
                LoaderKind.Forge,
                "1.20.1",
                "47.2.0",
                Path.Combine(Path.GetTempPath(), "archoera-tests", "missing-installer.jar")));

        Assert.Equal(ForgeDownloadErrorCode.InstallerMissing, exception.Code);
        Assert.Empty(handler.Requests);
    }
}
