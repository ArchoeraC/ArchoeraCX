using Archoera.Application.Loaders;
using Archoera.Domain.Loaders;
using Archoera.Domain.Downloads;
using Archoera.Infrastructure.Loaders;

namespace Archoera.Tests;

public sealed class ForgeMavenMirrorTests
{
    [Fact]
    public void NeoForge_release_paths_map_to_bmclapi()
    {
        var official = new Uri(
            "https://maven.neoforged.net/releases/net/neoforged/fancymodloader/" +
            "loader/4.0.39/loader-4.0.39.jar");

        Assert.True(ForgeMavenMirror.TryMap(
            official,
            ForgeMavenMirror.Bmclapi,
            out var mapped));
        Assert.Equal(
            "https://mirrors.ustc.edu.cn/bmclapi/net/neoforged/fancymodloader/" +
            "loader/4.0.39/loader-4.0.39.jar",
            mapped.AbsoluteUri);
    }

    [Fact]
    public void Installer_rewrite_keeps_the_official_url_as_a_mirror()
    {
        const string official =
            "https://maven.neoforged.net/releases/net/neoforged/neoforge/" +
            "21.1.176/neoforge-21.1.176-installer.jar";

        Assert.True(ForgeMavenMirror.TryRewrite(
            official,
            ForgeMavenMirror.Bmclapi,
            out var primary,
            out var mirrors));
        Assert.StartsWith(
            "https://mirrors.ustc.edu.cn/bmclapi/",
            primary,
            StringComparison.Ordinal);
        Assert.Equal([official], mirrors);
    }

    [Fact]
    public void Unrelated_hosts_are_not_rewritten()
    {
        Assert.False(ForgeMavenMirror.TryRewrite(
            "https://piston-data.mojang.com/v1/objects/x/client.jar",
            ForgeMavenMirror.Bmclapi,
            out var primary,
            out var mirrors));
        Assert.Equal(
            "https://piston-data.mojang.com/v1/objects/x/client.jar",
            primary);
        Assert.Empty(mirrors);
    }

    [Fact]
    public void Installer_fallbacks_cover_multiple_bmclapi_mirrors()
    {
        var official = ForgeMavenUrls.Installer(
            LoaderKind.NeoForge,
            "1.21.1",
            "21.1.176");
        var mirrors = ForgeMavenMirror.InstallerMirrors(official);
        Assert.Contains(
            mirrors,
            uri => uri.Host == "mirrors.ustc.edu.cn");
        Assert.Contains(
            mirrors,
            uri => uri.Host == "bmclapi2.bangbang93.com");
        Assert.All(
            mirrors,
            uri => Assert.EndsWith(
                "/net/neoforged/neoforge/21.1.176/" +
                "neoforge-21.1.176-installer.jar",
                uri.AbsolutePath,
                StringComparison.Ordinal));
    }

    [Fact]
    public void Combined_plan_rewrites_libraries_from_the_merge_phase()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "archoera-forge-mirror",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var plan = new DownloadPlanBuilder(root)
                .AddFile(
                    "https://maven.neoforged.net/releases/net/neoforged/test/1.0/test-1.0.jar",
                    "libraries/net/neoforged/test/1.0/test-1.0.jar",
                    DownloadHashAlgorithm.Sha1,
                    ForgeInstallerFixture.Sha('a'),
                    10)
                .Build();

            var mirrored = ForgePlanMirror.Apply(plan, ForgeMavenMirror.Bmclapi);
            var file = Assert.Single(mirrored.Files);
            Assert.Equal(
                "https://mirrors.ustc.edu.cn/bmclapi/net/neoforged/test/1.0/test-1.0.jar",
                file.Url);
            Assert.Contains(
                "https://maven.neoforged.net/releases/net/neoforged/test/1.0/test-1.0.jar",
                file.Mirrors);
        }
        finally
        {
            try
            {
                Directory.Delete(root, recursive: true);
            }
            catch
            {
                // best effort
            }
        }
    }

    [Fact]
    public void Plan_builder_prefers_the_mirror_for_neoforge_libraries()
    {
        var installer = ForgeInstallerFixture.CreateInstaller(
            profileLibrariesJson:
            $$"""
              [{{ForgeInstallerFixture.Library(
                  "net.neoforged:test:1.0",
                  "net/neoforged/test/1.0/test-1.0.jar",
                  "https://maven.neoforged.net/releases/net/neoforged/test/1.0/test-1.0.jar",
                  ForgeInstallerFixture.Sha('a'),
                  10)}}]
              """,
            versionLibrariesJson: "[]");
        using var archive = ForgeInstallerFixture.OpenZip(installer);

        var plan = ForgeDownloadPlanBuilder.Build(
            Path.GetTempPath(),
            LoaderKind.NeoForge,
            "1.21.1",
            "21.1.176",
            new ForgeInstallerDescriptor(
                "https://maven.neoforged.net/releases/net/neoforged/neoforge/" +
                "21.1.176/neoforge-21.1.176-installer.jar",
                ForgeInstallerFixture.Sha('b'),
                100),
            archive,
            new ForgeDownloadPlanOptions
            {
                MavenMirror = ForgeMavenMirror.Bmclapi,
            });

        var library = Assert.Single(
            plan.Plan.Files,
            file => file.Dest.EndsWith("test-1.0.jar", StringComparison.Ordinal));
        Assert.Equal(
            "https://mirrors.ustc.edu.cn/bmclapi/net/neoforged/test/1.0/test-1.0.jar",
            library.Url);
        Assert.Contains(
            "https://maven.neoforged.net/releases/net/neoforged/test/1.0/test-1.0.jar",
            library.Mirrors);
    }
}
