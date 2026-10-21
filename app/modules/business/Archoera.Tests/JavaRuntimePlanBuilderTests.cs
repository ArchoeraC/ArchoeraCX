using Archoera.Application.Downloads;
using Archoera.Application.JavaRuntime;
using Archoera.Domain.Downloads;
using Archoera.Domain.Java;
using Archoera.Domain.Mojang;

namespace Archoera.Tests;

public sealed class JavaRuntimePlanBuilderTests
{
    [Fact]
    public void Build_plans_raw_files_below_the_major_prefix_and_copies_the_executable_bit()
    {
        var manifest = JavaRuntimeTestData.ParseManifest(
            JavaRuntimeTestData.HappyManifestJson());

        var plan = JavaRuntimePlanBuilder.Build(
            JavaRuntimeTestData.Root,
            21,
            JavaRuntimeTestData.Catalog(),
            manifest,
            MojangPlatform.LinuxX64);

        Assert.Equal(2, plan.Files.Count);
        Assert.Equal(2, plan.Files.Select(file => file.Dest).Distinct().Count());

        var java = plan.Files.Single(file => file.Dest == "runtime/java-21/bin/java");
        Assert.Equal(JavaRuntimeTestData.JavaUrl, java.Url);
        Assert.Equal(JavaRuntimeTestData.Sha1('a'), java.Hash.Hex);
        Assert.Equal(DownloadHashAlgorithm.Sha1, java.Hash.Algorithm);
        Assert.Equal(123456, java.Size);
        Assert.True(java.Executable);

        var library = plan.Files.Single(
            file => file.Dest == "runtime/java-21/lib/libjli.so");
        Assert.Equal(JavaRuntimeTestData.Sha1('b'), library.Hash.Hex);
        Assert.Equal(4567, library.Size);
        Assert.False(library.Executable);

        Assert.DoesNotContain(plan.Files, file => file.Dest.Contains("modules"));
        Assert.DoesNotContain(plan.Files, file => file.Dest.Contains("readlink"));
        Assert.Equal(128_023, plan.TotalBytes);
        Assert.Equal(64, plan.PlanId.Length);
        Assert.Empty(plan.Post);
    }

    [Fact]
    public void Build_is_deterministic_and_round_trips_through_plan_json()
    {
        var manifest = JavaRuntimeTestData.ParseManifest(
            JavaRuntimeTestData.HappyManifestJson());
        var catalog = JavaRuntimeTestData.Catalog();

        var first = JavaRuntimePlanBuilder.Build(
            JavaRuntimeTestData.Root, 21, catalog, manifest, MojangPlatform.LinuxX64);
        var second = JavaRuntimePlanBuilder.Build(
            JavaRuntimeTestData.Root, 21, catalog, manifest, MojangPlatform.LinuxX64);

        Assert.Equal(first.PlanId, second.PlanId);

        var bytes = DownloadPlanJson.Build(first);
        Assert.True(
            DownloadPlanJson.TryParse(bytes, out var parsed, out var error),
            error?.ToString());

        Assert.NotNull(parsed);
        Assert.Equal(first.PlanId, parsed.PlanId);
        Assert.Equal(first.TotalBytes, parsed.TotalBytes);
        Assert.Equal(
            first.Files.Select(file => (file.Dest, file.Hash.Hex, file.Size, file.Executable)),
            parsed.Files.Select(file => (file.Dest, file.Hash.Hex, file.Size, file.Executable)));
    }

    [Fact]
    public void Build_rejects_lzma_only_files_instead_of_planning_compressed_bytes()
    {
        var manifest = JavaRuntimeTestData.ParseManifest(
            JavaRuntimeTestData.LzmaOnlyManifestJson());

        var exception = Assert.Throws<JavaRuntimeException>(() => JavaRuntimePlanBuilder.Build(
            JavaRuntimeTestData.Root,
            21,
            JavaRuntimeTestData.Catalog(),
            manifest,
            MojangPlatform.LinuxX64));

        Assert.Equal(JavaRuntimeErrorCode.LzmaOnlyUnsupported, exception.Code);
    }

    [Theory]
    [InlineData("../escape")]
    [InlineData("/etc/passwd")]
    public void Build_rejects_escaping_or_absolute_manifest_paths(string path)
    {
        var manifest = JavaRuntimeTestData.ParseManifest($$"""
            {
              "files": {
                "{{path}}": {
                  "type": "file",
                  "downloads": {
                    "raw": {
                      "url": "{{JavaRuntimeTestData.JavaUrl}}",
                      "sha1": "{{JavaRuntimeTestData.Sha1('a')}}",
                      "size": 1
                    }
                  }
                }
              }
            }
            """);

        var exception = Assert.Throws<JavaRuntimeException>(() => JavaRuntimePlanBuilder.Build(
            JavaRuntimeTestData.Root,
            21,
            JavaRuntimeTestData.Catalog(),
            manifest,
            MojangPlatform.LinuxX64));

        Assert.Equal(JavaRuntimeErrorCode.PathEscape, exception.Code);
    }

    [Fact]
    public void Build_rejects_backslash_paths()
    {
        var manifest = JavaRuntimeTestData.ParseManifest($$"""
            {
              "files": {
                "bin\\java": {
                  "type": "file",
                  "downloads": {
                    "raw": {
                      "url": "{{JavaRuntimeTestData.JavaUrl}}",
                      "sha1": "{{JavaRuntimeTestData.Sha1('a')}}",
                      "size": 1
                    }
                  }
                }
              }
            }
            """);

        var exception = Assert.Throws<JavaRuntimeException>(() => JavaRuntimePlanBuilder.Build(
            JavaRuntimeTestData.Root,
            21,
            JavaRuntimeTestData.Catalog(),
            manifest,
            MojangPlatform.LinuxX64));

        Assert.Equal(DownloadPlanErrorCode.InvalidPlan, exception.Code);
    }

    [Fact]
    public void Build_rejects_missing_raw_hash_and_size()
    {
        var noHash = JavaRuntimeTestData.ParseManifest($$"""
            {
              "files": {
                "bin/java": {
                  "type": "file",
                  "downloads": {
                    "raw": {
                      "url": "{{JavaRuntimeTestData.JavaUrl}}",
                      "size": 10
                    }
                  }
                }
              }
            }
            """);

        var hashException = Assert.Throws<JavaRuntimeException>(() => JavaRuntimePlanBuilder.Build(
            JavaRuntimeTestData.Root,
            21,
            JavaRuntimeTestData.Catalog(),
            noHash,
            MojangPlatform.LinuxX64));
        Assert.Equal(JavaRuntimeErrorCode.MissingHash, hashException.Code);

        var noSize = JavaRuntimeTestData.ParseManifest($$"""
            {
              "files": {
                "bin/java": {
                  "type": "file",
                  "downloads": {
                    "raw": {
                      "url": "{{JavaRuntimeTestData.JavaUrl}}",
                      "sha1": "{{JavaRuntimeTestData.Sha1('a')}}"
                    }
                  }
                }
              }
            }
            """);

        var sizeException = Assert.Throws<JavaRuntimeException>(() => JavaRuntimePlanBuilder.Build(
            JavaRuntimeTestData.Root,
            21,
            JavaRuntimeTestData.Catalog(),
            noSize,
            MojangPlatform.LinuxX64));
        Assert.Equal(JavaRuntimeErrorCode.MissingSize, sizeException.Code);
    }

    [Fact]
    public void Build_rejects_duplicate_destinations_case_insensitively()
    {
        var raw = new JavaRuntimeDownloadRef(
            JavaRuntimeTestData.JavaUrl,
            JavaRuntimeTestData.Sha1('a'),
            10);

        var files = new Dictionary<string, JavaRuntimeFileEntry>(StringComparer.Ordinal)
        {
            ["BIN/JAVA"] = new("BIN/JAVA", "file", false, raw, null),
            ["bin/java"] = new("bin/java", "file", false, raw, null),
        };

        var exception = Assert.Throws<JavaRuntimeException>(() => JavaRuntimePlanBuilder.Build(
            JavaRuntimeTestData.Root,
            21,
            JavaRuntimeTestData.Catalog(),
            new JavaRuntimeManifest(files),
            MojangPlatform.LinuxX64));

        Assert.Equal(JavaRuntimeErrorCode.DuplicateDest, exception.Code);
    }

    [Fact]
    public void Build_rejects_unknown_file_types_and_empty_manifests()
    {
        var unknownType = new JavaRuntimeManifest(
            new Dictionary<string, JavaRuntimeFileEntry>(StringComparer.Ordinal)
            {
                ["bin/java"] = new(
                    "bin/java",
                    "socket",
                    false,
                    new JavaRuntimeDownloadRef(
                        JavaRuntimeTestData.JavaUrl,
                        JavaRuntimeTestData.Sha1('a'),
                        1),
                    null),
            });

        var typeException = Assert.Throws<JavaRuntimeException>(() => JavaRuntimePlanBuilder.Build(
            JavaRuntimeTestData.Root,
            21,
            JavaRuntimeTestData.Catalog(),
            unknownType,
            MojangPlatform.LinuxX64));
        Assert.Equal(JavaRuntimeErrorCode.InvalidMetadata, typeException.Code);

        var emptyException = Assert.Throws<JavaRuntimeException>(() => JavaRuntimePlanBuilder.Build(
            JavaRuntimeTestData.Root,
            21,
            JavaRuntimeTestData.Catalog(),
            JavaRuntimeManifest.Empty,
            MojangPlatform.LinuxX64));
        Assert.Equal(JavaRuntimeErrorCode.InvalidMetadata, emptyException.Code);
    }

    [Fact]
    public void Build_rejects_a_file_without_any_download_object()
    {
        var manifest = new JavaRuntimeManifest(
            new Dictionary<string, JavaRuntimeFileEntry>(StringComparer.Ordinal)
            {
                ["bin/java"] = new("bin/java", "file", true, null, null),
            });

        var exception = Assert.Throws<JavaRuntimeException>(() => JavaRuntimePlanBuilder.Build(
            JavaRuntimeTestData.Root,
            21,
            JavaRuntimeTestData.Catalog(),
            manifest,
            MojangPlatform.LinuxX64));

        Assert.Equal(JavaRuntimeErrorCode.InvalidMetadata, exception.Code);
    }

    [Fact]
    public void Build_fails_when_the_platform_or_component_is_not_in_the_catalog()
    {
        var manifest = JavaRuntimeTestData.ParseManifest(
            JavaRuntimeTestData.HappyManifestJson());
        var catalog = JavaRuntimeTestData.Catalog();

        var platformException = Assert.Throws<JavaRuntimeException>(() => JavaRuntimePlanBuilder.Build(
            JavaRuntimeTestData.Root,
            21,
            catalog,
            manifest,
            MojangPlatform.WindowsX64));
        Assert.Equal(JavaRuntimeErrorCode.NoRuntimeAvailable, platformException.Code);

        var componentException = Assert.Throws<JavaRuntimeException>(() => JavaRuntimePlanBuilder.Build(
            JavaRuntimeTestData.Root,
            21,
            catalog,
            manifest,
            MojangPlatform.LinuxX64,
            component: "java-runtime-gamma"));
        Assert.Equal(JavaRuntimeErrorCode.NoRuntimeAvailable, componentException.Code);
    }

    [Fact]
    public void Build_rejects_a_platform_without_a_mojang_runtime()
    {
        var exception = Assert.Throws<JavaRuntimeException>(() => JavaRuntimePlanBuilder.Build(
            JavaRuntimeTestData.Root,
            21,
            JavaRuntimeTestData.Catalog(),
            JavaRuntimeTestData.ParseManifest(JavaRuntimeTestData.HappyManifestJson()),
            MojangPlatform.Create(MojangSystemOs.Linux, MojangSystemArch.Arm64)));

        Assert.Equal(JavaRuntimeErrorCode.UnsupportedPlatform, exception.Code);
    }
}
