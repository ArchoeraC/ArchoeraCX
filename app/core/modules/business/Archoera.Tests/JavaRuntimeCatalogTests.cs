using Archoera.Domain.Java;
using Archoera.Domain.Mojang;

namespace Archoera.Tests;

public sealed class JavaRuntimeCatalogTests
{
    [Theory]
    [InlineData(MojangSystemOs.Linux, MojangSystemArch.X64, JavaRuntimePlatform.Linux)]
    [InlineData(MojangSystemOs.Linux, MojangSystemArch.X86, JavaRuntimePlatform.LinuxI386)]
    [InlineData(MojangSystemOs.Windows, MojangSystemArch.X64, JavaRuntimePlatform.WindowsX64)]
    [InlineData(MojangSystemOs.Windows, MojangSystemArch.X86, JavaRuntimePlatform.WindowsX86)]
    [InlineData(MojangSystemOs.Windows, MojangSystemArch.Arm64, JavaRuntimePlatform.WindowsArm64)]
    [InlineData(MojangSystemOs.MacOs, MojangSystemArch.X64, JavaRuntimePlatform.MacOs)]
    [InlineData(MojangSystemOs.MacOs, MojangSystemArch.Arm64, JavaRuntimePlatform.MacOsArm64)]
    public void Platform_mapping_covers_every_mojang_runtime_key(
        MojangSystemOs os,
        MojangSystemArch arch,
        string expected)
    {
        var platform = MojangPlatform.Create(os, arch);

        Assert.Equal(expected, JavaRuntimePlatform.FromMojang(platform));
        Assert.True(JavaRuntimePlatform.TryFromMojang(platform, out var key));
        Assert.Equal(expected, key);
    }

    [Theory]
    [InlineData(MojangSystemOs.Linux, MojangSystemArch.Arm64)]
    [InlineData(MojangSystemOs.Linux, MojangSystemArch.Arm)]
    [InlineData(MojangSystemOs.Windows, MojangSystemArch.Arm)]
    [InlineData(MojangSystemOs.MacOs, MojangSystemArch.X86)]
    public void Unsupported_platforms_fail_with_a_stable_code(
        MojangSystemOs os,
        MojangSystemArch arch)
    {
        var platform = MojangPlatform.Create(os, arch);

        Assert.False(JavaRuntimePlatform.TryFromMojang(platform, out _));

        var exception = Assert.Throws<JavaRuntimeException>(
            () => JavaRuntimePlatform.FromMojang(platform));

        Assert.Equal(JavaRuntimeErrorCode.UnsupportedPlatform, exception.Code);
    }

    [Fact]
    public void ParseCatalog_preserves_empty_gamecore_arrays_without_breaking_linux()
    {
        var json = JavaRuntimeTestData.CatalogJson(JavaRuntimeTestData.HappyManifestJson());

        var catalog = JavaRuntimeTestData.ParseCatalog(json);

        Assert.True(catalog.HasPlatform(JavaRuntimePlatform.GameCore));
        Assert.Empty(catalog.GetComponents(JavaRuntimePlatform.GameCore));
        Assert.Empty(catalog.GetComponentNames(JavaRuntimePlatform.GameCore));

        Assert.True(catalog.HasPlatform(JavaRuntimePlatform.Linux));
        var component = Assert.Single(catalog.GetComponents(JavaRuntimePlatform.Linux));
        Assert.Equal("java-runtime-delta", component.Name);
        Assert.True(component.IsAvailable);
        Assert.Equal("21.0.7", component.Version.Name);
        Assert.Equal(JavaRuntimeTestData.ManifestUrl, component.Manifest.Url);

        var selected = catalog.Select(
            JavaRuntimePlatform.Linux,
            "java-runtime-delta");
        Assert.NotNull(selected);
        Assert.Equal(JavaRuntimeTestData.Sha1Of(JavaRuntimeTestData.HappyManifestJson()),
            selected.Manifest.Sha1);

        // An empty gamecore array means "nothing published there", not
        // "the whole catalog failed".
        Assert.Null(catalog.Select(JavaRuntimePlatform.GameCore, "java-runtime-delta"));
    }

    [Fact]
    public void Select_prefers_available_entries_then_latest_release_with_a_stable_tiebreak()
    {
        var json = """
            {
              "linux": {
                "java-runtime-delta": [
                  {
                    "availability": "unavailable",
                    "manifest": {
                      "url": "https://launchermeta.mojang.com/newest.json",
                      "sha1": "0000000000000000000000000000000000000000",
                      "size": 1
                    },
                    "version": { "name": "22.0.0", "released": "2026-06-01T00:00:00+00:00" }
                  },
                  {
                    "availability": "available",
                    "manifest": {
                      "url": "https://launchermeta.mojang.com/old.json",
                      "sha1": "1111111111111111111111111111111111111111",
                      "size": 1
                    },
                    "version": { "name": "21.0.7", "released": "2025-01-01T00:00:00+00:00" }
                  },
                  {
                    "availability": "available",
                    "manifest": {
                      "url": "https://launchermeta.mojang.com/z.json",
                      "sha1": "2222222222222222222222222222222222222222",
                      "size": 1
                    },
                    "version": { "name": "21.0.9", "released": "2026-01-01T00:00:00+00:00" }
                  },
                  {
                    "availability": "available",
                    "manifest": {
                      "url": "https://launchermeta.mojang.com/a.json",
                      "sha1": "3333333333333333333333333333333333333333",
                      "size": 1
                    },
                    "version": { "name": "21.0.9", "released": "2026-01-01T00:00:00+00:00" }
                  }
                ]
              }
            }
            """;

        var catalog = JavaRuntimeTestData.ParseCatalog(json);
        var selected = catalog.Select("linux", "java-runtime-delta");

        Assert.NotNull(selected);
        Assert.Equal("21.0.9", selected.Version.Name);
        Assert.Equal("https://launchermeta.mojang.com/a.json", selected.Manifest.Url);
    }

    [Theory]
    [InlineData(8, "jre-legacy")]
    [InlineData(17, "java-runtime-gamma")]
    [InlineData(21, "java-runtime-delta")]
    [InlineData(25, "java-runtime-epsilon")]
    public void Component_fallback_matches_the_launcher_table(int major, string component)
    {
        var selection = JavaRuntimeSelection.ForMajor(major);

        Assert.Equal(major, selection.MajorVersion);
        Assert.Equal(component, selection.Component);
        Assert.True(selection.IsDefault);
    }

    [Fact]
    public void Explicit_component_overrides_the_default()
    {
        var selection = JavaRuntimeSelection.ForMajor(21, "java-runtime-gamma");

        Assert.Equal("java-runtime-gamma", selection.Component);
        Assert.False(selection.IsDefault);
    }

    [Theory]
    [InlineData(11)]
    [InlineData(24)]
    [InlineData(26)]
    public void Unknown_major_requires_an_explicit_component(int major)
    {
        var exception = Assert.Throws<JavaRuntimeException>(
            () => JavaRuntimeSelection.ForMajor(major));

        Assert.Equal(JavaRuntimeErrorCode.UnsupportedJavaVersion, exception.Code);
    }

    [Fact]
    public void Invalid_major_and_unsafe_component_are_rejected()
    {
        var major = Assert.Throws<JavaRuntimeException>(
            () => JavaRuntimeSelection.ForMajor(0));
        Assert.Equal(JavaRuntimeErrorCode.InvalidRequest, major.Code);

        var component = Assert.Throws<JavaRuntimeException>(
            () => JavaRuntimeSelection.ForMajor(21, "../escape"));
        Assert.Equal(JavaRuntimeErrorCode.InvalidRequest, component.Code);
    }

    [Fact]
    public void ParseCatalog_accepts_a_fully_empty_document()
    {
        var catalog = JavaRuntimeTestData.ParseCatalog("{}");

        Assert.Empty(catalog.Platforms);
        Assert.False(catalog.HasPlatform(JavaRuntimePlatform.Linux));
        Assert.Empty(catalog.GetComponents(JavaRuntimePlatform.Linux));
    }
}
