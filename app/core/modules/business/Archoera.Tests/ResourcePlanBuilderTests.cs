using Archoera.Application.Resources;
using Archoera.Domain.Downloads;
using Archoera.Domain.Resources;

namespace Archoera.Tests;

public sealed class ResourcePlanBuilderTests
{
    private static readonly string Root = Path.Combine(
        Path.GetTempPath(),
        "archoera-resource-plan");

    private static readonly string Sha1 = new('1', 40);
    private static readonly string Sha512 = new('2', 128);

    [Fact]
    public void Build_creates_a_single_mod_file_plan_with_sha512()
    {
        var version = Version("v1", ["1.21.1"], ["fabric"], File("sodium.jar", Sha1, Sha512));
        var target = ResourceTarget.ForMod("1.21.1", "fabric");

        var plan = ResourceDownloadPlanBuilder.Build(Root, ResourceKind.Mod, target, version);

        Assert.Equal(Root, plan.Root);
        Assert.Equal(1234, plan.TotalBytes);
        Assert.Equal(64, plan.PlanId.Length);

        var file = Assert.Single(plan.Files);
        Assert.Equal("mods/sodium.jar", file.Dest);
        Assert.Equal("https://cdn.modrinth.com/data/AANobbMI/versions/v1/sodium.jar", file.Url);
        Assert.Equal(1234, file.Size);
        Assert.Equal(DownloadFileMode.Ensure, file.Mode);
        Assert.Equal(DownloadHashAlgorithm.Sha512, file.Hash.Algorithm);
        Assert.Equal(Sha512, file.Hash.Hex);
        // The MCIM mirror follows the official CDN as a fallback.
        Assert.Equal(
            ["https://mod.mcimirror.top/data/AANobbMI/versions/v1/sodium.jar"],
            file.Mirrors);
        Assert.Empty(file.Headers);
        Assert.False(file.Executable);
    }

    [Fact]
    public void Mirror_preference_puts_mcim_first()
    {
        var version = Version("v1", ["1.21.1"], ["fabric"], File("sodium.jar", Sha1, Sha512));

        var plan = ResourceDownloadPlanBuilder.Build(
            Root,
            ResourceKind.Mod,
            ResourceTarget.ForMod("1.21.1", "fabric"),
            version,
            sourceMode: Archoera.Application.Downloads.DownloadSourceMode.Mirror);

        var file = Assert.Single(plan.Files);
        Assert.StartsWith("https://mod.mcimirror.top/", file.Url, StringComparison.Ordinal);
        Assert.Equal(
            ["https://cdn.modrinth.com/data/AANobbMI/versions/v1/sodium.jar"],
            file.Mirrors);
    }

    [Fact]
    public void Build_falls_back_to_sha1_and_accepts_an_explicit_file()
    {
        var secondary = new ResourceFile(
            "sodium-extra.jar",
            "https://cdn.modrinth.com/data/AANobbMI/versions/v1/extra.jar",
            9,
            Sha1,
            Sha512: null,
            Primary: false);
        var version = Version(
            "v1",
            ["1.21.1"],
            ["fabric"],
            File("sodium.jar", Sha1, Sha512),
            secondary);

        var plan = ResourceDownloadPlanBuilder.Build(
            Root,
            ResourceKind.Mod,
            ResourceTarget.ForMod("1.21.1", "fabric"),
            version,
            secondary);

        var file = Assert.Single(plan.Files);
        Assert.Equal("mods/sodium-extra.jar", file.Dest);
        Assert.Equal(DownloadHashAlgorithm.Sha1, file.Hash.Algorithm);
        Assert.Equal(Sha1, file.Hash.Hex);
        Assert.Equal(9, file.Size);
    }

    [Fact]
    public void Build_uses_the_real_shader_and_resource_pack_folders()
    {
        var shaderPlan = ResourceDownloadPlanBuilder.Build(
            Root,
            ResourceKind.Shader,
            ResourceTarget.ForShader("1.21.1", "iris"),
            Version("shader", ["1.21.1"], ["iris"], File("complementary.zip", Sha1, Sha512)));

        Assert.Equal("shaderpacks/complementary.zip", Assert.Single(shaderPlan.Files).Dest);

        var packPlan = ResourceDownloadPlanBuilder.Build(
            Root,
            ResourceKind.ResourcePack,
            ResourceTarget.ForResourcePack("1.21.1"),
            Version("pack", ["1.21.1"], ["minecraft"], File("faithful.zip", Sha1, Sha512)));

        Assert.Equal("resourcepacks/faithful.zip", Assert.Single(packPlan.Files).Dest);
    }

    [Fact]
    public void Build_rejects_an_incompatible_version()
    {
        var exception = Assert.Throws<ResourceProviderException>(
            () => ResourceDownloadPlanBuilder.Build(
                Root,
                ResourceKind.Mod,
                ResourceTarget.ForMod("1.21.1", "forge"),
                Version("v1", ["1.21.1"], ["fabric"], File("sodium.jar", Sha1, Sha512))));

        Assert.Equal(ResourceErrorCode.NoCompatibleBuild, exception.Code);
    }

    [Fact]
    public void Build_rejects_a_file_without_a_url()
    {
        var file = new ResourceFile("sodium.jar", string.Empty, 1234, Sha1, Sha512, Primary: true);

        var exception = Assert.Throws<ResourceProviderException>(
            () => ResourceDownloadPlanBuilder.Build(
                Root,
                ResourceKind.Mod,
                ResourceTarget.ForMod("1.21.1", "fabric"),
                Version("v1", ["1.21.1"], ["fabric"], file)));

        Assert.Equal(ResourceErrorCode.DistributionDenied, exception.Code);
    }

    [Fact]
    public void Build_rejects_unsafe_file_names_and_missing_hashes()
    {
        var unsafeName = Assert.Throws<ResourceProviderException>(
            () => ResourceDownloadPlanBuilder.Build(
                Root,
                ResourceKind.Mod,
                ResourceTarget.ForMod("1.21.1", "fabric"),
                Version("v1", ["1.21.1"], ["fabric"], File("../sodium.jar", Sha1, Sha512))));
        Assert.Equal(ResourceErrorCode.InvalidResponse, unsafeName.Code);

        var noHash = Assert.Throws<DownloadPlanException>(
            () => ResourceDownloadPlanBuilder.Build(
                Root,
                ResourceKind.Mod,
                ResourceTarget.ForMod("1.21.1", "fabric"),
                Version("v1", ["1.21.1"], ["fabric"], File("sodium.jar", null, null))));
        Assert.Equal(DownloadPlanErrorCode.InvalidHash, noHash.Code);
    }

    private static ResourceVersion Version(
        string id,
        IReadOnlyList<string> gameVersions,
        IReadOnlyList<string> loaders,
        params ResourceFile[] files)
        => new(
            id,
            "AANobbMI",
            "Sodium",
            "1.0.0",
            gameVersions,
            loaders,
            ResourceVersionType.Release,
            DateTimeOffset.Parse("2026-09-01T12:00:00Z"),
            Files: files);

    private static ResourceFile File(string name, string? sha1, string? sha512)
        => new(
            name,
            $"https://cdn.modrinth.com/data/AANobbMI/versions/v1/{name}",
            1234,
            sha1,
            sha512,
            Primary: true);
}
