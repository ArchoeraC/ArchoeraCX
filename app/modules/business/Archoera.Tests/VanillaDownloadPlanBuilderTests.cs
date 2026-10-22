using Archoera.Application.Downloads;
using Archoera.Application.Mojang;
using Archoera.Domain.Downloads;
using Archoera.Domain.Mojang;

namespace Archoera.Tests;

public sealed class VanillaDownloadPlanBuilderTests
{
    [Fact]
    public void Build_includes_client_libraries_natives_assets_and_logging()
    {
        var plan = VanillaDownloadPlanBuilder.Build(
            MojangTestData.Root,
            MojangTestData.Version(),
            MojangTestData.AssetIndex(),
            MojangPlatform.LinuxX64);

        var destinations = plan.Files.Select(file => file.Dest).ToArray();

        Assert.Equal(9, plan.Files.Count);
        Assert.Equal(plan.Files.Count, destinations.Distinct(StringComparer.Ordinal).Count());

        Assert.Contains(MojangTestData.ClientPath, destinations);
        Assert.Contains(MojangTestData.CommonPath, destinations);
        Assert.Contains(MojangTestData.LinuxPath, destinations);
        Assert.Contains(MojangTestData.X64Path, destinations);
        Assert.Contains(MojangTestData.NativePath, destinations);
        Assert.Contains(MojangTestData.AssetIndexPath, destinations);
        Assert.Contains(MojangTestData.AssetAPath, destinations);
        Assert.Contains(MojangTestData.AssetBPath, destinations);
        Assert.Contains(MojangTestData.LoggingPath, destinations);

        Assert.DoesNotContain(MojangTestData.WindowsPath, destinations);
        Assert.DoesNotContain(MojangTestData.MacOsPath, destinations);
        Assert.DoesNotContain(MojangTestData.Arm64Path, destinations);

        var client = plan.Files.Single(file => file.Dest == MojangTestData.ClientPath);
        Assert.Equal(DownloadHashAlgorithm.Sha1, client.Hash.Algorithm);
        Assert.Equal(MojangTestData.Sha1('1'), client.Hash.Hex);
        Assert.Equal(1_000, client.Size);
        Assert.StartsWith("https://piston-data.mojang.com/", client.Url, StringComparison.Ordinal);
        Assert.Contains(
            $"https://bmclapi2.bangbang93.com/version/{MojangTestData.Version().Id}/client",
            client.Mirrors);

        var native = plan.Files.Single(file => file.Dest == MojangTestData.NativePath);
        Assert.Equal(MojangTestData.Sha1('6'), native.Hash.Hex);
        Assert.Equal(20, native.Size);

        var asset = plan.Files.Single(file => file.Dest == MojangTestData.AssetAPath);
        Assert.Equal(
            $"https://resources.download.minecraft.net/aa/{MojangTestData.Sha1('a')}",
            asset.Url);
        Assert.Contains(
            $"https://bmclapi2.bangbang93.com/assets/aa/{MojangTestData.Sha1('a')}",
            asset.Mirrors);
        var common = plan.Files.Single(file => file.Dest == MojangTestData.CommonPath);
        Assert.Contains(
            common.Mirrors,
            mirror => mirror.StartsWith(
                "https://bmclapi2.bangbang93.com/libraries/",
                StringComparison.Ordinal));

        var logging = plan.Files.Single(file => file.Dest == MojangTestData.LoggingPath);
        Assert.Equal(42, logging.Size);
        Assert.Equal(MojangTestData.Sha1('7'), logging.Hash.Hex);

        Assert.Equal(64, plan.PlanId.Length);
        Assert.Empty(plan.Post);
    }

    [Fact]
    public void Mirror_preference_swaps_primary_and_fallback()
    {
        var official = VanillaDownloadPlanBuilder.Build(
            MojangTestData.Root,
            MojangTestData.Version(),
            MojangTestData.AssetIndex(),
            MojangPlatform.LinuxX64,
            sourceMode: DownloadSourceMode.Official);
        var mirror = VanillaDownloadPlanBuilder.Build(
            MojangTestData.Root,
            MojangTestData.Version(),
            MojangTestData.AssetIndex(),
            MojangPlatform.LinuxX64,
            sourceMode: DownloadSourceMode.Mirror);

        var officialClient = official.Files.Single(
            file => file.Dest == MojangTestData.ClientPath);
        Assert.StartsWith("https://piston-data.mojang.com/", officialClient.Url, StringComparison.Ordinal);
        Assert.Contains(officialClient.Mirrors, mirrorUrl =>
            mirrorUrl.StartsWith("https://bmclapi2.bangbang93.com/", StringComparison.Ordinal));

        var mirrorClient = mirror.Files.Single(
            file => file.Dest == MojangTestData.ClientPath);
        Assert.StartsWith("https://bmclapi2.bangbang93.com/", mirrorClient.Url, StringComparison.Ordinal);
        Assert.Contains(mirrorClient.Mirrors, officialUrl =>
            officialUrl.StartsWith("https://piston-data.mojang.com/", StringComparison.Ordinal));

        // Assets follow the same order.
        var mirrorAsset = mirror.Files.Single(file => file.Dest == MojangTestData.AssetAPath);
        Assert.StartsWith("https://bmclapi2.bangbang93.com/assets/", mirrorAsset.Url, StringComparison.Ordinal);
        Assert.Contains(mirrorAsset.Mirrors, officialUrl =>
            officialUrl.StartsWith("https://resources.download.minecraft.net/", StringComparison.Ordinal));
    }

    [Fact]
    public void Build_asset_objects_are_deduped_by_hash()
    {
        var plan = VanillaDownloadPlanBuilder.Build(
            MojangTestData.Root,
            MojangTestData.Version(),
            MojangTestData.AssetIndex(),
            MojangPlatform.LinuxX64);

        Assert.Single(plan.Files, file => file.Dest.StartsWith("assets/objects/aa/", StringComparison.Ordinal));
        Assert.Single(plan.Files, file => file.Dest.StartsWith("assets/objects/bb/", StringComparison.Ordinal));
        Assert.Equal(
            2,
            plan.Files.Count(file => file.Dest.StartsWith("assets/objects/", StringComparison.Ordinal)));
    }

    [Fact]
    public void Build_selects_windows_libraries()
    {
        var plan = VanillaDownloadPlanBuilder.Build(
            MojangTestData.Root,
            MojangTestData.Version(),
            MojangTestData.AssetIndex(),
            MojangPlatform.WindowsX64);

        var destinations = plan.Files.Select(file => file.Dest).ToArray();

        Assert.Contains(MojangTestData.WindowsPath, destinations);
        Assert.Contains(MojangTestData.CommonPath, destinations);
        Assert.Contains(MojangTestData.X64Path, destinations);
        Assert.DoesNotContain(MojangTestData.LinuxPath, destinations);
        Assert.DoesNotContain(MojangTestData.MacOsPath, destinations);
        Assert.DoesNotContain(MojangTestData.Arm64Path, destinations);
        Assert.DoesNotContain(MojangTestData.NativePath, destinations);
    }

    [Fact]
    public void Build_selects_macos_arm64_libraries()
    {
        var plan = VanillaDownloadPlanBuilder.Build(
            MojangTestData.Root,
            MojangTestData.Version(),
            MojangTestData.AssetIndex(),
            MojangPlatform.MacOsArm64);

        var destinations = plan.Files.Select(file => file.Dest).ToArray();

        Assert.Contains(MojangTestData.MacOsPath, destinations);
        Assert.Contains(MojangTestData.Arm64Path, destinations);
        Assert.Contains(MojangTestData.CommonPath, destinations);
        Assert.DoesNotContain(MojangTestData.LinuxPath, destinations);
        Assert.DoesNotContain(MojangTestData.WindowsPath, destinations);
        Assert.DoesNotContain(MojangTestData.X64Path, destinations);
        Assert.DoesNotContain(MojangTestData.NativePath, destinations);
    }

    [Fact]
    public void Build_plan_json_round_trips_and_plan_id_is_deterministic()
    {
        var first = VanillaDownloadPlanBuilder.Build(
            MojangTestData.Root,
            MojangTestData.Version(),
            MojangTestData.AssetIndex(),
            MojangPlatform.LinuxX64);

        var second = VanillaDownloadPlanBuilder.Build(
            MojangTestData.Root,
            MojangTestData.Version(),
            MojangTestData.AssetIndex(),
            MojangPlatform.LinuxX64);

        Assert.Equal(first.PlanId, second.PlanId);

        var bytes = DownloadPlanJson.Build(first);
        Assert.True(
            DownloadPlanJson.TryParse(bytes, out var parsed, out var error),
            error?.ToString());

        Assert.NotNull(parsed);
        Assert.Equal(first.PlanId, parsed.PlanId);
        Assert.Equal(first.Root, parsed.Root);
        Assert.Equal(first.Files.Count, parsed.Files.Count);
        Assert.Equal(
            first.Files.Select(file => file.Dest).Order(StringComparer.Ordinal),
            parsed.Files.Select(file => file.Dest).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Build_rejects_versions_with_inherits_from()
    {
        var version = MojangTestData.Version() with { InheritsFrom = "1.20.1" };

        var exception = Assert.Throws<MojangException>(() => VanillaDownloadPlanBuilder.Build(
            MojangTestData.Root,
            version,
            MojangTestData.AssetIndex(),
            MojangPlatform.LinuxX64));

        Assert.Equal(MojangErrorCode.UnsupportedInherits, exception.Code);
    }

    [Fact]
    public void Build_rejects_missing_client_hash_and_size()
    {
        var noHash = MojangTestData.Version() with
        {
            Downloads = new MojangVersionDownloads(
                new MojangDownload(
                    "https://piston-data.mojang.com/v1/objects/client/client.jar",
                    Sha1: null,
                    1_000)),
        };

        var hashException = Assert.Throws<MojangException>(() => VanillaDownloadPlanBuilder.Build(
            MojangTestData.Root,
            noHash,
            MojangTestData.AssetIndex(),
            MojangPlatform.LinuxX64));
        Assert.Equal(MojangErrorCode.MissingHash, hashException.Code);

        var noSize = MojangTestData.Version() with
        {
            Downloads = new MojangVersionDownloads(
                new MojangDownload(
                    "https://piston-data.mojang.com/v1/objects/client/client.jar",
                    MojangTestData.Sha1('1'),
                    Size: null)),
        };

        var sizeException = Assert.Throws<MojangException>(() => VanillaDownloadPlanBuilder.Build(
            MojangTestData.Root,
            noSize,
            MojangTestData.AssetIndex(),
            MojangPlatform.LinuxX64));
        Assert.Equal(MojangErrorCode.MissingSize, sizeException.Code);
    }

    [Fact]
    public void Build_rejects_missing_asset_hash_and_size()
    {
        var noHash = MojangTestData.AssetIndex() with
        {
            Objects = new Dictionary<string, MojangAssetObject>(StringComparer.Ordinal)
            {
                ["bad.png"] = new(string.Empty, 1),
            },
        };

        var hashException = Assert.Throws<MojangException>(() => VanillaDownloadPlanBuilder.Build(
            MojangTestData.Root,
            MojangTestData.Version(),
            noHash,
            MojangPlatform.LinuxX64));
        Assert.Equal(MojangErrorCode.MissingHash, hashException.Code);

        var noSize = MojangTestData.AssetIndex() with
        {
            Objects = new Dictionary<string, MojangAssetObject>(StringComparer.Ordinal)
            {
                ["bad.png"] = new(MojangTestData.Sha1('a'), Size: null),
            },
        };

        var sizeException = Assert.Throws<MojangException>(() => VanillaDownloadPlanBuilder.Build(
            MojangTestData.Root,
            MojangTestData.Version(),
            noSize,
            MojangPlatform.LinuxX64));
        Assert.Equal(MojangErrorCode.MissingSize, sizeException.Code);
    }

    [Fact]
    public void Build_requires_asset_index_download_metadata()
    {
        var version = MojangTestData.Version() with { AssetIndex = null };

        var exception = Assert.Throws<MojangException>(() => VanillaDownloadPlanBuilder.Build(
            MojangTestData.Root,
            version,
            MojangTestData.AssetIndex(),
            MojangPlatform.LinuxX64));

        Assert.Equal(MojangErrorCode.MissingHash, exception.Code);
    }

    [Fact]
    public void Build_rejects_duplicate_destinations()
    {
        var version = MojangTestData.Version() with
        {
            Libraries =
            [
                MojangTestData.Library("dup", 'a', 1),
                MojangTestData.Library("dup", 'a', 1),
            ],
        };

        var exception = Assert.Throws<MojangException>(() => VanillaDownloadPlanBuilder.Build(
            MojangTestData.Root,
            version,
            assetIndex: null,
            MojangPlatform.LinuxX64));

        Assert.Equal(MojangErrorCode.DuplicateDest, exception.Code);
    }

    [Fact]
    public void Build_resolves_legacy_arch_natives_and_maven_paths()
    {
        var classifier = new MojangDownload(
            "https://libraries.minecraft.net/org/example/natives/1.0/natives-1.0-natives-linux-64.jar",
            MojangTestData.Sha1('6'),
            20,
            Path: null);

        var natives = new MojangLibrary(
            "org.example:natives:1.0",
            new MojangLibraryDownloads(
                null,
                new Dictionary<string, MojangDownload>(StringComparer.OrdinalIgnoreCase)
                {
                    ["natives-linux-64"] = classifier,
                }),
            Natives: new Dictionary<string, string> { ["linux"] = "natives-linux-${arch}" },
            Rules: [MojangTestData.AllowOs("linux", "x86_64")]);

        var version = MojangTestData.Version() with { Libraries = [natives] };

        var plan = VanillaDownloadPlanBuilder.Build(
            MojangTestData.Root,
            version,
            assetIndex: null,
            MojangPlatform.LinuxX64);

        var file = Assert.Single(plan.Files, candidate =>
            candidate.Dest.Contains("natives-linux-64", StringComparison.Ordinal));

        Assert.Equal(
            "libraries/org/example/natives/1.0/natives-1.0-natives-linux-64.jar",
            file.Dest);
        Assert.Equal(MojangTestData.Sha1('6'), file.Hash.Hex);
        Assert.Equal(20, file.Size);
    }

    [Fact]
    public void Build_rejects_a_missing_native_classifier()
    {
        var natives = new MojangLibrary(
            "org.example:natives:1.0",
            new MojangLibraryDownloads(null),
            Natives: new Dictionary<string, string> { ["linux"] = "natives-linux" },
            Rules: [MojangTestData.AllowOs("linux")]);

        var version = MojangTestData.Version() with { Libraries = [natives] };

        var exception = Assert.Throws<MojangException>(() => VanillaDownloadPlanBuilder.Build(
            MojangTestData.Root,
            version,
            assetIndex: null,
            MojangPlatform.LinuxX64));

        Assert.Equal(MojangErrorCode.InvalidMetadata, exception.Code);
    }
}
