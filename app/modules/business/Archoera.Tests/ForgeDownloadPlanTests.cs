using Archoera.Application.Downloads;
using Archoera.Application.Loaders;
using Archoera.Domain.Downloads;
using Archoera.Domain.Loaders;
using Archoera.Infrastructure.Loaders;

namespace Archoera.Tests;

public sealed class ForgeDownloadPlanTests
{
    private static readonly string InstallerSha1 = ForgeInstallerFixture.Sha('1');
    private static readonly string Root = Path.Combine(
        Path.GetTempPath(),
        "archoera-tests",
        "forge-download-plan");

    private const long InstallerSize = 9_999;

    [Fact]
    public void Build_includes_installer_and_remote_libraries_only()
    {
        var bytes = ForgeInstallerFixture.CreateInstaller(
            DefaultProfileLibraries(),
            DefaultVersionLibraries());

        var result = Build(bytes);

        var plan = result.Plan;
        Assert.Equal(4, plan.Files.Count);
        Assert.Equal(
            plan.Files.Count,
            plan.Files.Select(file => file.Dest).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(Path.GetFullPath(Root), plan.Root);
        Assert.Empty(plan.Post);
        Assert.Equal(plan.Files.Sum(file => file.Size), plan.TotalBytes);

        var installer = Assert.Single(
            plan.Files,
            file => file.Dest == "downloads/loaders/forge/1.20.1-47.2.0-installer.jar");
        Assert.Equal(ForgeInstallerFixture.ForgeInstallerUrl, installer.Url);
        Assert.Equal(DownloadHashAlgorithm.Sha1, installer.Hash.Algorithm);
        Assert.Equal(InstallerSha1, installer.Hash.Hex);
        Assert.Equal(InstallerSize, installer.Size);

        var installertools = Assert.Single(
            plan.Files,
            file => file.Dest ==
                    "libraries/net/minecraftforge/installertools/1.3.0/installertools-1.3.0.jar");
        Assert.Equal(ForgeInstallerFixture.Sha('a'), installertools.Hash.Hex);
        Assert.Equal(1_234, installertools.Size);

        var binarypatcher = Assert.Single(
            plan.Files,
            file => file.Dest ==
                    "libraries/net/neoforged/installertools/binarypatcher/2.1.2/binarypatcher-2.1.2-fatjar.jar");
        Assert.Equal(ForgeInstallerFixture.Sha('b'), binarypatcher.Hash.Hex);
        Assert.Equal(5_678, binarypatcher.Size);
        Assert.StartsWith("https://maven.neoforged.net/", binarypatcher.Url, StringComparison.Ordinal);

        var asm = Assert.Single(
            plan.Files,
            file => file.Dest == "libraries/org/ow2/asm/asm/9.5/asm-9.5.jar");
        Assert.Equal(ForgeInstallerFixture.Sha('d'), asm.Hash.Hex);
        Assert.Equal(4_321, asm.Size);

        // Empty-URL entries are generated locally (PATCHED / srg) and must never
        // become downloads.
        Assert.DoesNotContain(
            plan.Files,
            file => file.Dest.EndsWith("-client.jar", StringComparison.Ordinal));
        Assert.DoesNotContain(
            plan.Files,
            file => file.Dest.Contains("client.lzma", StringComparison.Ordinal));
        Assert.DoesNotContain(
            plan.Files,
            file => file.Dest.Contains("/maven/", StringComparison.Ordinal));

        Assert.All(plan.Files, file =>
        {
            Assert.False(string.IsNullOrWhiteSpace(file.Url));
            Assert.True(file.Hash.IsCanonicalHex(), file.Dest);
            Assert.True(file.Size >= 0, file.Dest);
        });

        Assert.Equal(
            2,
            result.Warnings.Count(warning => warning.Code == ForgeDownloadWarningCode.EmptyUrl));
        Assert.DoesNotContain(
            result.Warnings,
            warning => warning.Code == ForgeDownloadWarningCode.EmbeddedLibrary);
        Assert.Equal(64, plan.PlanId.Length);
    }

    [Fact]
    public void Build_plan_json_round_trips_through_download_plan_json()
    {
        var result = Build(ForgeInstallerFixture.CreateInstaller(
            DefaultProfileLibraries(),
            DefaultVersionLibraries()));

        var json = DownloadPlanJson.Build(result.Plan);
        Assert.True(
            DownloadPlanJson.TryParse(json, out var parsed, out var error),
            error?.ToString());

        Assert.NotNull(parsed);
        Assert.Equal(result.Plan.PlanId, parsed.PlanId);
        Assert.Equal(result.Plan.Root, parsed.Root);
        Assert.Equal(result.Plan.Files.Count, parsed.Files.Count);
        Assert.Equal(
            result.Plan.Files.Select(file => file.Dest).Order(StringComparer.Ordinal),
            parsed.Files.Select(file => file.Dest).Order(StringComparer.Ordinal));
        Assert.Equal(
            result.Plan.Files.Select(file => file.Hash.Hex).Order(StringComparer.Ordinal),
            parsed.Files.Select(file => file.Hash.Hex).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Build_skips_libraries_without_downloads_metadata()
    {
        var profile = $$"""
            [
              { "name": "com.example:embedded:1.0" },
              {{ForgeInstallerFixture.Library(
                  "net.minecraft:client:1.20.1:srg",
                  "net/minecraft/client/1.20.1/client-1.20.1-srg.jar",
                  string.Empty,
                  ForgeInstallerFixture.Sha('c'),
                  999)}}
            ]
            """;

        var result = Build(ForgeInstallerFixture.CreateInstaller(profile, "[]"));

        Assert.Single(result.Plan.Files);
        Assert.Contains(
            result.Warnings,
            warning => warning.Code == ForgeDownloadWarningCode.EmbeddedLibrary);
    }

    [Fact]
    public void Build_dedupes_identical_destinations_from_profile_and_version_json()
    {
        var asm = ForgeInstallerFixture.Library(
            "org.ow2.asm:asm:9.5",
            "org/ow2/asm/asm/9.5/asm-9.5.jar",
            "https://maven.minecraftforge.net/org/ow2/asm/asm/9.5/asm-9.5.jar",
            ForgeInstallerFixture.Sha('d'),
            4_321);

        var result = Build(ForgeInstallerFixture.CreateInstaller(
            $$"""
            [
              {{ForgeInstallerFixture.Library(
                  "net.minecraftforge:installertools:1.3.0",
                  "net/minecraftforge/installertools/1.3.0/installertools-1.3.0.jar",
                  "https://maven.minecraftforge.net/net/minecraftforge/installertools/1.3.0/installertools-1.3.0.jar",
                  ForgeInstallerFixture.Sha('a'),
                  1_234)}},
              {{asm}}
            ]
            """,
            $$"""
            [
              {{asm}}
            ]
            """));

        Assert.Single(
            result.Plan.Files,
            file => file.Dest == "libraries/org/ow2/asm/asm/9.5/asm-9.5.jar");
        Assert.Equal(3, result.Plan.Files.Count);
    }

    [Fact]
    public void Build_fails_when_the_same_destination_has_a_different_hash()
    {
        var conflicting = ForgeInstallerFixture.Library(
            "org.ow2.asm:asm:9.5",
            "org/ow2/asm/asm/9.5/asm-9.5.jar",
            "https://maven.minecraftforge.net/org/ow2/asm/asm/9.5/asm-9.5.jar",
            ForgeInstallerFixture.Sha('f'),
            4_321);

        var bytes = ForgeInstallerFixture.CreateInstaller(
            $$"""
            [
              {{ForgeInstallerFixture.Library(
                  "org.ow2.asm:asm:9.5",
                  "org/ow2/asm/asm/9.5/asm-9.5.jar",
                  "https://maven.minecraftforge.net/org/ow2/asm/asm/9.5/asm-9.5.jar",
                  ForgeInstallerFixture.Sha('d'),
                  4_321)}}
            ]
            """,
            $$"""
            [
              {{conflicting}}
            ]
            """);

        var exception = Assert.Throws<ForgeDownloadException>(() => Build(bytes));
        Assert.Equal(ForgeDownloadErrorCode.DuplicateDest, exception.Code);
    }

    [Fact]
    public void Build_fails_when_a_remote_library_has_no_hash()
    {
        var profile = $$"""
            [
              {{ForgeInstallerFixture.Library(
                  "org.ow2.asm:asm:9.5",
                  "org/ow2/asm/asm/9.5/asm-9.5.jar",
                  "https://maven.minecraftforge.net/org/ow2/asm/asm/9.5/asm-9.5.jar",
                  sha1: null,
                  size: 4_321)}}
            ]
            """;

        var exception = Assert.Throws<ForgeDownloadException>(() =>
            Build(ForgeInstallerFixture.CreateInstaller(profile, "[]")));

        Assert.Equal(ForgeDownloadErrorCode.MissingHash, exception.Code);
    }

    [Fact]
    public void Build_fails_when_a_remote_library_has_no_size()
    {
        var profile = $$"""
            [
              {{ForgeInstallerFixture.Library(
                  "org.ow2.asm:asm:9.5",
                  "org/ow2/asm/asm/9.5/asm-9.5.jar",
                  "https://maven.minecraftforge.net/org/ow2/asm/asm/9.5/asm-9.5.jar",
                  ForgeInstallerFixture.Sha('d'),
                  size: null)}}
            ]
            """;

        var exception = Assert.Throws<ForgeDownloadException>(() =>
            Build(ForgeInstallerFixture.CreateInstaller(profile, "[]")));

        Assert.Equal(ForgeDownloadErrorCode.MissingSize, exception.Code);
    }

    [Fact]
    public void Build_requires_installer_hash_and_size()
    {
        var noHash = new ForgeInstallerDescriptor(
            ForgeInstallerFixture.ForgeInstallerUrl,
            string.Empty,
            InstallerSize);

        var hashException = Assert.Throws<ForgeDownloadException>(() =>
            ForgeDownloadPlanBuilder.Build(
                Root,
                LoaderKind.Forge,
                "1.20.1",
                "47.2.0",
                noHash,
                archive: null));
        Assert.Equal(ForgeDownloadErrorCode.MissingHash, hashException.Code);

        var noSize = new ForgeInstallerDescriptor(
            ForgeInstallerFixture.ForgeInstallerUrl,
            InstallerSha1,
            Size: null);

        var sizeException = Assert.Throws<ForgeDownloadException>(() =>
            ForgeDownloadPlanBuilder.Build(
                Root,
                LoaderKind.Forge,
                "1.20.1",
                "47.2.0",
                noSize,
                archive: null));
        Assert.Equal(ForgeDownloadErrorCode.MissingSize, sizeException.Code);
    }

    [Fact]
    public void Build_without_an_archive_plans_the_installer_and_warns()
    {
        var result = ForgeDownloadPlanBuilder.Build(
            Root,
            LoaderKind.Forge,
            "1.20.1",
            "47.2.0",
            Descriptor(),
            archive: null);

        Assert.Single(result.Plan.Files);
        Assert.Equal(
            "downloads/loaders/forge/1.20.1-47.2.0-installer.jar",
            result.Plan.Files[0].Dest);
        Assert.Contains(
            result.Warnings,
            warning => warning.Code == ForgeDownloadWarningCode.InstallerMetadataUnavailable);
    }

    [Fact]
    public void Build_honours_a_caller_supplied_installer_destination()
    {
        var result = ForgeDownloadPlanBuilder.Build(
            Root,
            LoaderKind.NeoForge,
            "21.1.209",
            "21.1.209",
            Descriptor(),
            archive: null,
            options: new ForgeDownloadPlanOptions
            {
                InstallerRelativePath = "downloads/loaders/custom/neoforge-installer.jar",
            });

        Assert.Equal(
            "downloads/loaders/custom/neoforge-installer.jar",
            Assert.Single(result.Plan.Files).Dest);
    }

    [Fact]
    public void Build_excludes_the_version_client_jar_by_default()
    {
        var bytes = ForgeInstallerFixture.CreateInstaller(
            DefaultProfileLibraries(),
            "[]",
            clientDownloadsJson: """
                {
                  "client": {
                    "url": "https://piston-data.mojang.com/v1/objects/client/client.jar",
                    "sha1": "0000000000000000000000000000000000000001",
                    "size": 100
                  }
                }
                """);

        var defaultResult = Build(bytes);
        Assert.DoesNotContain(
            defaultResult.Plan.Files,
            file => file.Dest.StartsWith("versions/", StringComparison.Ordinal));

        var bytesForOptIn = ForgeInstallerFixture.CreateInstaller(
            DefaultProfileLibraries(),
            "[]",
            clientDownloadsJson: """
                {
                  "client": {
                    "url": "https://piston-data.mojang.com/v1/objects/client/client.jar",
                    "sha1": "0000000000000000000000000000000000000001",
                    "size": 100
                  }
                }
                """);

        using var archive = ForgeInstallerFixture.OpenZip(bytesForOptIn);
        var optIn = ForgeDownloadPlanBuilder.Build(
            Root,
            LoaderKind.Forge,
            "1.20.1",
            "47.2.0",
            Descriptor(),
            archive,
            new ForgeDownloadPlanOptions { IncludeClientJar = true });

        Assert.Contains(
            optIn.Plan.Files,
            file => file.Dest == "versions/1.20.1-forge-47.2.0/1.20.1-forge-47.2.0.jar");
    }

    [Fact]
    public async Task BuildFromInstallerPath_includes_real_forge_processor_libraries_when_sample_exists()
    {
        var repositoryRoot = FindRepositoryRoot();
        if (repositoryRoot is null)
        {
            return;
        }

        var installerPath = Path.Combine(
            repositoryRoot,
            ".local",
            "forge-samples",
            "forge-1.20.1-47.2.0-installer.jar");
        if (!File.Exists(installerPath))
        {
            return;
        }

        using var http = new HttpClient(new FakeHttpMessageHandler());
        var service = new ForgeLoaderPlanService(http);

        var result = await service.BuildFromInstallerPathAsync(
            Root,
            LoaderKind.Forge,
            "1.20.1",
            "47.2.0",
            installerPath);

        var plan = result.Plan;
        Assert.True(plan.Files.Count > 30, $"only {plan.Files.Count} files");
        Assert.Contains(
            plan.Files,
            file => file.Dest ==
                    "libraries/net/minecraftforge/installertools/1.3.0/installertools-1.3.0.jar");
        Assert.Contains(
            plan.Files,
            file => file.Dest ==
                    "libraries/net/minecraftforge/binarypatcher/1.1.1/binarypatcher-1.1.1.jar");
        Assert.Contains(
            plan.Files,
            file => file.Dest ==
                    "libraries/net/minecraftforge/ForgeAutoRenamingTool/0.1.22/ForgeAutoRenamingTool-0.1.22-all.jar");
        Assert.Contains(
            plan.Files,
            file => file.Dest == "downloads/loaders/forge/1.20.1-47.2.0-installer.jar");

        Assert.DoesNotContain(
            plan.Files,
            file => file.Dest.EndsWith("-client.jar", StringComparison.Ordinal));
        Assert.All(plan.Files, file =>
        {
            Assert.True(file.Hash.IsCanonicalHex(), file.Dest);
            Assert.True(file.Size >= 0, file.Dest);
            Assert.StartsWith("https://", file.Url, StringComparison.Ordinal);
        });

        var json = DownloadPlanJson.Build(plan);
        Assert.True(
            DownloadPlanJson.TryParse(json, out _, out var error),
            error?.ToString());
    }

    private static ForgeDownloadPlanResult Build(byte[] installerBytes)
    {
        using var archive = ForgeInstallerFixture.OpenZip(installerBytes);
        return ForgeDownloadPlanBuilder.Build(
            Root,
            LoaderKind.Forge,
            "1.20.1",
            "47.2.0",
            Descriptor(),
            archive);
    }

    private static ForgeInstallerDescriptor Descriptor()
        => new(
            ForgeInstallerFixture.ForgeInstallerUrl,
            InstallerSha1,
            InstallerSize);

    private static string DefaultProfileLibraries()
        => $$"""
            [
              {{ForgeInstallerFixture.Library(
                  "net.minecraftforge:installertools:1.3.0",
                  "net/minecraftforge/installertools/1.3.0/installertools-1.3.0.jar",
                  "https://maven.minecraftforge.net/net/minecraftforge/installertools/1.3.0/installertools-1.3.0.jar",
                  ForgeInstallerFixture.Sha('a'),
                  1_234)}},
              {{ForgeInstallerFixture.Library(
                  "net.neoforged.installertools:binarypatcher:2.1.2:fatjar",
                  "net/neoforged/installertools/binarypatcher/2.1.2/binarypatcher-2.1.2-fatjar.jar",
                  "https://maven.neoforged.net/releases/net/neoforged/installertools/binarypatcher/2.1.2/binarypatcher-2.1.2-fatjar.jar",
                  ForgeInstallerFixture.Sha('b'),
                  5_678)}},
              {{ForgeInstallerFixture.Library(
                  "net.minecraft:client:1.20.1:srg",
                  "net/minecraft/client/1.20.1/client-1.20.1-srg.jar",
                  string.Empty,
                  ForgeInstallerFixture.Sha('c'),
                  999)}}
            ]
            """;

    private static string DefaultVersionLibraries()
        => $$"""
            [
              {{ForgeInstallerFixture.Library(
                  "org.ow2.asm:asm:9.5",
                  "org/ow2/asm/asm/9.5/asm-9.5.jar",
                  "https://maven.minecraftforge.net/org/ow2/asm/asm/9.5/asm-9.5.jar",
                  ForgeInstallerFixture.Sha('d'),
                  4_321)}},
              {{ForgeInstallerFixture.Library(
                  "net.minecraftforge:forge:1.20.1-47.2.0:client",
                  "net/minecraftforge/forge/1.20.1-47.2.0/forge-1.20.1-47.2.0-client.jar",
                  string.Empty,
                  ForgeInstallerFixture.Sha('e'),
                  111)}}
            ]
            """;

    private static string? FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, ".local", "forge-samples")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        return null;
    }
}
