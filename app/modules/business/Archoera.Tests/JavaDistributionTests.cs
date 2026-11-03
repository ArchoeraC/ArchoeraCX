using System.Formats.Tar;
using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Archoera.Application.JavaRuntime;
using Archoera.Domain.Downloads;
using Archoera.Infrastructure.Downloads;
using Archoera.Infrastructure.Java;
using Archoera.Infrastructure.Java.Distributions;

namespace Archoera.Tests;

public sealed partial class JavaDistributionTests
{
    [Fact]
    public void Tar_extraction_strips_the_top_level_and_sets_the_exec_bit()
    {
        var root = TempDirectory();
        try
        {
            var archive = Path.Combine(root, "jdk.tar.gz");
            WriteTarGz(
                archive,
                [
                    ("jdk-21.0.13/bin/java", "#!/bin/sh\n", true),
                    ("jdk-21.0.13/lib/modules", "modules", false),
                ]);
            var extracted = Path.Combine(root, "out");

            JavaArchiveExtractor.Extract(
                archive,
                "tar.gz",
                extracted,
                CancellationToken.None);

            var java = Path.Combine(extracted, "jdk-21.0.13", "bin", "java");
            Assert.True(File.Exists(java));
            if (!OperatingSystem.IsWindows())
            {
                Assert.True((File.GetUnixFileMode(java) & UnixFileMode.UserExecute) != 0);
            }
        }
        finally
        {
            Delete(root);
        }
    }

    [Fact]
    public void Tar_extraction_rejects_traversal_and_links()
    {
        var root = TempDirectory();
        try
        {
            var traversal = Path.Combine(root, "traversal.tar.gz");
            WriteTarGz(traversal, [("../evil", "x", false)]);
            var exception = Assert.Throws<JavaDistributionException>(() =>
                JavaArchiveExtractor.Extract(
                    traversal,
                    "tar.gz",
                    Path.Combine(root, "out1"),
                    CancellationToken.None));
            Assert.Equal("JAVA_ARCHIVE_UNSAFE", exception.Code);
            Assert.False(File.Exists(Path.Combine(root, "evil")));

            var link = Path.Combine(root, "link.tar.gz");
            WriteTarLink(link);
            var linkException = Assert.Throws<JavaDistributionException>(() =>
                JavaArchiveExtractor.Extract(
                    link,
                    "tar.gz",
                    Path.Combine(root, "out2"),
                    CancellationToken.None));
            Assert.Equal("JAVA_ARCHIVE_UNSAFE", linkException.Code);
        }
        finally
        {
            Delete(root);
        }
    }

    [Fact]
    public void Tar_extraction_allows_relative_symlinks_inside_the_root()
    {
        var root = TempDirectory();
        try
        {
            var archive = Path.Combine(root, "links.tar.gz");
            using (var file = File.Create(archive))
            using (var gzip = new GZipStream(file, CompressionLevel.Fastest, leaveOpen: false))
            using (var writer = new TarWriter(gzip, TarEntryFormat.Pax, leaveOpen: false))
            {
                writer.WriteEntry(new PaxTarEntry(
                    TarEntryType.RegularFile,
                    "jdk/legal/java.base/LICENSE")
                {
                    DataStream = new MemoryStream(Encoding.UTF8.GetBytes("license")),
                });
                writer.WriteEntry(new PaxTarEntry(
                    TarEntryType.SymbolicLink,
                    "jdk/legal/jdk.jdeps/LICENSE")
                {
                    LinkName = "../java.base/LICENSE",
                });
            }

            var extracted = Path.Combine(root, "out");
            JavaArchiveExtractor.Extract(
                archive,
                "tar.gz",
                extracted,
                CancellationToken.None);

            var link = Path.Combine(extracted, "jdk", "legal", "jdk.jdeps", "LICENSE");
            Assert.True(File.Exists(link));
            Assert.Equal(
                "license",
                File.ReadAllText(link));
        }
        finally
        {
            Delete(root);
        }
    }

    [Fact]
    public void Zip_extraction_writes_regular_files()
    {
        var root = TempDirectory();
        try
        {
            var archive = Path.Combine(root, "jdk.zip");
            using (var zip = ZipFile.Open(archive, ZipArchiveMode.Create))
            {
                using (var writer = new StreamWriter(
                           zip.CreateEntry("jdk/bin/java.exe").Open()))
                {
                    writer.Write("exe");
                }

                using (var writer = new StreamWriter(
                           zip.CreateEntry("jdk/lib/modules").Open()))
                {
                    writer.Write("modules");
                }
            }

            var extracted = Path.Combine(root, "out");
            JavaArchiveExtractor.Extract(
                archive,
                "zip",
                extracted,
                CancellationToken.None);

            Assert.Equal(
                "exe",
                File.ReadAllText(Path.Combine(extracted, "jdk", "bin", "java.exe")));
            Assert.Equal(
                "modules",
                File.ReadAllText(Path.Combine(extracted, "jdk", "lib", "modules")));
        }
        finally
        {
            Delete(root);
        }
    }

    [Fact]
    public void Zip_extraction_rejects_traversal()
    {
        var root = TempDirectory();
        try
        {
            var archive = Path.Combine(root, "jdk.zip");
            using (var zip = ZipFile.Open(archive, ZipArchiveMode.Create))
            {
                var entry = zip.CreateEntry("../evil.txt");
                using var writer = new StreamWriter(entry.Open());
                writer.Write("x");
            }

            var exception = Assert.Throws<JavaDistributionException>(() =>
                JavaArchiveExtractor.Extract(
                    archive,
                    "zip",
                    Path.Combine(root, "out"),
                    CancellationToken.None));
            Assert.Equal("JAVA_ARCHIVE_UNSAFE", exception.Code);
            Assert.False(File.Exists(Path.Combine(root, "evil.txt")));
        }
        finally
        {
            Delete(root);
        }
    }

    [Fact]
    public async Task Install_downloads_extracts_and_refuses_a_non_empty_target()
    {
        var root = TempDirectory();
        try
        {
            var tar = Path.Combine(root, "source.tar.gz");
            WriteTarGz(
                tar,
                [
                    ("jdk-21/bin/java", "#!/bin/sh\n", true),
                    ("jdk-21/lib/modules", "modules", false),
                ]);
            var bytes = await File.ReadAllBytesAsync(tar);
            var sha = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            var provider = new FakeProvider(sha, bytes.LongLength);
            var registry = new JavaDistributionRegistry([provider]);
            var runner = new WritingRunner(bytes);
            var service = new JavaDistributionService(registry, root, runner);
            var target = Path.Combine(root, "custom-jdk");

            var result = await service.InstallAsync("fake", "v1", target, autoExtract: true, autoDelete: true, allowOutdated: false, acknowledgeCves: false, CancellationToken.None);

            Assert.Equal(target, result.Home);
            Assert.Equal(Path.Combine(target, "bin", "java"), result.Executable);
            Assert.True(File.Exists(result.Executable));
            Assert.Equal(bytes.LongLength, result.Bytes);
            Assert.DoesNotContain(
                Directory.EnumerateDirectories(root),
                path => Path.GetFileName(path).StartsWith('.'));

            var blocked = Path.Combine(root, "occupied");
            Directory.CreateDirectory(blocked);
            await File.WriteAllTextAsync(Path.Combine(blocked, "keep.txt"), "keep");
            var exception = await Assert.ThrowsAsync<JavaDistributionException>(() =>
                service.InstallAsync("fake", "v1", blocked, autoExtract: true, autoDelete: true, allowOutdated: false, acknowledgeCves: false, CancellationToken.None));
            Assert.Equal("JAVA_DIR_NOT_EMPTY", exception.Code);
        }
        finally
        {
            Delete(root);
        }
    }

    [Fact]
    public async Task Install_reconciles_a_rounded_catalogue_size_with_the_archive_head()
    {
        var root = TempDirectory();
        try
        {
            var bytes = new byte[4096];
            var handler = new FakeHttpMessageHandler();
            handler.Enqueue(() =>
            {
                var response = new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent([]),
                };
                response.Content.Headers.ContentLength = bytes.LongLength;
                return response;
            });
            var registry = new JavaDistributionRegistry(
            [
                new FakeProvider(
                    new string('a', 64),
                    bytes.LongLength,
                    archiveSize: 5000),
            ]);
            var service = new JavaDistributionService(
                registry,
                root,
                new WritingRunner(bytes),
                http: new HttpClient(handler));

            var result = await service.InstallAsync(
                "fake",
                "v1",
                Path.Combine(root, "download-only"),
                autoExtract: false,
                autoDelete: true,
                allowOutdated: false,
                acknowledgeCves: false,
                CancellationToken.None);

            Assert.Equal(bytes.LongLength, result.Bytes);
            Assert.Equal("HEAD", Assert.Single(handler.Requests).Method);
        }
        finally
        {
            Delete(root);
        }
    }

    [Fact]
    public async Task Install_can_keep_the_archive_and_skip_extraction()
    {
        var root = TempDirectory();
        try
        {
            var tar = Path.Combine(root, "source.tar.gz");
            WriteTarGz(
                tar,
                [
                    ("jdk-21/bin/java", "#!/bin/sh\n", true),
                ]);
            var bytes = await File.ReadAllBytesAsync(tar);
            var sha = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            var registry = new JavaDistributionRegistry(
                [new FakeProvider(sha, bytes.LongLength)]);
            var service = new JavaDistributionService(
                registry,
                root,
                new WritingRunner(bytes));

            var downloadOnlyTarget = Path.Combine(root, "download-only");
            var downloadOnly = await service.InstallAsync(
                "fake",
                "v1",
                downloadOnlyTarget,
                autoExtract: false,
                autoDelete: false,
                allowOutdated: false,
                acknowledgeCves: false,
                CancellationToken.None);
            Assert.False(downloadOnly.Extracted);
            Assert.Empty(downloadOnly.Home);
            Assert.Empty(downloadOnly.Executable);
            Assert.False(Directory.Exists(downloadOnlyTarget));
            Assert.NotNull(downloadOnly.Archive);
            Assert.True(File.Exists(downloadOnly.Archive));

            var extractTarget = Path.Combine(root, "extract-and-keep");
            var extracted = await service.InstallAsync(
                "fake",
                "v1",
                extractTarget,
                autoExtract: true,
                autoDelete: false,
                allowOutdated: false,
                acknowledgeCves: false,
                CancellationToken.None);
            Assert.True(extracted.Extracted);
            Assert.True(File.Exists(Path.Combine(extractTarget, "bin", "java")));
            Assert.NotNull(extracted.Archive);
            Assert.True(File.Exists(extracted.Archive));
            Assert.NotEqual(downloadOnly.Archive, extracted.Archive);
        }
        finally
        {
            Delete(root);
        }
    }

    [Fact]
    public async Task Install_rejects_an_outdated_patch_unless_explicitly_allowed()
    {
        var root = TempDirectory();
        try
        {
            var tar = Path.Combine(root, "source.tar.gz");
            WriteTarGz(tar, [("jdk-21/bin/java", "#!/bin/sh\n", true)]);
            var bytes = await File.ReadAllBytesAsync(tar);
            var sha = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            var registry = new JavaDistributionRegistry(
                [new FakeProvider(sha, bytes.LongLength, latestPatch: false)]);
            var service = new JavaDistributionService(
                registry,
                root,
                new WritingRunner(bytes));

            var exception = await Assert.ThrowsAsync<JavaDistributionException>(() =>
                service.InstallAsync(
                    "fake",
                    "v1",
                    Path.Combine(root, "outdated"),
                    autoExtract: true,
                    autoDelete: true,
                    allowOutdated: false,
                    acknowledgeCves: false,
                    CancellationToken.None));
            Assert.Equal("JAVA_VERSION_OUTDATED", exception.Code);
            Assert.False(Directory.Exists(Path.Combine(root, "outdated")));
        }
        finally
        {
            Delete(root);
        }
    }

    [Fact]
    public async Task Module_exposes_providers_and_rejects_an_unknown_provider()
    {
        var root = TempDirectory();
        try
        {
            var registry = new JavaDistributionRegistry(
            [
                new FakeProvider(new string('a', 64), 1),
            ]);
            var service = new JavaDistributionService(registry, root, new WritingRunner([]));
            var module = new JavaModuleService(null, null, root, null, service);

            var catalog = JsonDocument.Parse(
                await module.HandleAsync("java.distributions", "{}", CancellationToken.None));
            Assert.True(catalog.RootElement.GetProperty("ok").GetBoolean());
            var data = catalog.RootElement.GetProperty("data");
            var providers = data.GetProperty("providers");
            Assert.Equal(1, providers.GetArrayLength());
            Assert.Equal("fake", providers[0].GetProperty("id").GetString());
            var platforms = data.GetProperty("platforms");
            Assert.Equal(JavaDistributionPlatform.Supported.Count, platforms.GetArrayLength());
            Assert.Contains(
                platforms.EnumerateArray(),
                item => item.GetProperty("current").GetBoolean());

            var error = JsonDocument.Parse(
                await module.HandleAsync(
                    "java.distributions.majors",
                    """{"provider":"missing"}""",
                    CancellationToken.None));
            Assert.False(error.RootElement.GetProperty("ok").GetBoolean());
            Assert.Equal(
                "JAVA_PROVIDER_UNSUPPORTED",
                error.RootElement.GetProperty("error").GetProperty("code").GetString());

            var partial = JsonDocument.Parse(
                await module.HandleAsync(
                    "java.distributions.majors",
                    """{"provider":"fake","os":"linux"}""",
                    CancellationToken.None));
            Assert.False(partial.RootElement.GetProperty("ok").GetBoolean());
            Assert.Equal(
                "JAVA_PLATFORM_UNSUPPORTED",
                partial.RootElement.GetProperty("error").GetProperty("code").GetString());
        }
        finally
        {
            Delete(root);
        }
    }

    [Fact]
    public void Platform_parse_normalizes_and_rejects_invalid_triples()
    {
        var windows = JavaDistributionPlatform.Parse("WINDOWS", "x64");
        Assert.Equal("windows", windows.Os);
        Assert.Equal("x64", windows.Arch);
        Assert.Equal("zip", windows.ArchiveType);
        Assert.True(windows.IsWindows);

        var linux = JavaDistributionPlatform.Parse("linux", "aarch64", "tar.gz");
        Assert.Equal("tar.gz", linux.ArchiveType);
        Assert.False(linux.IsWindows);

        var mismatch = Assert.Throws<JavaDistributionException>(() =>
            JavaDistributionPlatform.Parse("windows", "x64", "tar.gz"));
        Assert.Equal("JAVA_PLATFORM_UNSUPPORTED", mismatch.Code);

        var unsupported = Assert.Throws<JavaDistributionException>(() =>
            JavaDistributionPlatform.Parse("linux", "x86"));
        Assert.Equal("JAVA_PLATFORM_UNSUPPORTED", unsupported.Code);
    }

    [Fact]
    public void Install_home_lookup_uses_the_target_executable_name()
    {
        var root = TempDirectory();
        try
        {
            var windowsHome = Path.Combine(root, "windows-jdk");
            Directory.CreateDirectory(Path.Combine(windowsHome, "bin"));
            File.WriteAllText(Path.Combine(windowsHome, "bin", "java.exe"), "stub");
            var linuxHome = Path.Combine(root, "linux-jdk");
            Directory.CreateDirectory(Path.Combine(linuxHome, "bin"));
            File.WriteAllText(Path.Combine(linuxHome, "bin", "java"), "stub");

            var windows = JavaDistributionPlatform.Parse("windows", "x64");
            var linux = JavaDistributionPlatform.Parse("linux", "x64");
            Assert.Equal(
                windowsHome,
                JavaInstallTarget.FindJavaHome(root, windows));
            Assert.Equal(
                linuxHome,
                JavaInstallTarget.FindJavaHome(root, linux));
        }
        finally
        {
            Delete(root);
        }
    }

    [Fact]
    public async Task Install_rejects_a_foreign_platform_without_confirmation()
    {
        var root = TempDirectory();
        try
        {
            var registry = new JavaDistributionRegistry(
            [
                new FakeProvider(new string('a', 64), 1),
            ]);
            var service = new JavaDistributionService(
                registry,
                root,
                new WritingRunner([]));
            var foreign = JavaDistributionPlatform.Supported.First(
                platform => !platform.IsCurrentHost);

            var exception = await Assert.ThrowsAsync<JavaDistributionException>(
                () => service.InstallAsync(
                    "fake",
                    "v1",
                    null,
                    autoExtract: true,
                    autoDelete: true,
                    allowOutdated: false,
                    acknowledgeCves: true,
                    CancellationToken.None,
                    foreign,
                    allowIncompatible: false));
            Assert.Equal("JAVA_PLATFORM_INCOMPATIBLE", exception.Code);
            Assert.False(Directory.Exists(Path.Combine(root, "runtime")));
        }
        finally
        {
            Delete(root);
        }
    }

    private static void WriteTarGz(string path, IReadOnlyList<(string Name, string Text, bool Exec)> files)
    {
        using var file = File.Create(path);
        using var gzip = new GZipStream(file, CompressionLevel.Fastest, leaveOpen: false);
        using var writer = new TarWriter(gzip, TarEntryFormat.Pax, leaveOpen: false);
        foreach (var (name, text, exec) in files)
        {
            var payload = Encoding.UTF8.GetBytes(text);
            var entry = new PaxTarEntry(TarEntryType.RegularFile, name)
            {
                DataStream = new MemoryStream(payload),
                Mode = exec
                    ? UnixFileMode.UserRead | UnixFileMode.UserWrite |
                      UnixFileMode.UserExecute | UnixFileMode.GroupRead |
                      UnixFileMode.GroupExecute | UnixFileMode.OtherRead |
                      UnixFileMode.OtherExecute
                    : UnixFileMode.UserRead | UnixFileMode.UserWrite,
            };
            writer.WriteEntry(entry);
        }
    }

    private static void WriteTarLink(string path)
    {
        using var file = File.Create(path);
        using var gzip = new GZipStream(file, CompressionLevel.Fastest, leaveOpen: false);
        using var writer = new TarWriter(gzip, TarEntryFormat.Pax, leaveOpen: false);
        writer.WriteEntry(new PaxTarEntry(TarEntryType.SymbolicLink, "jdk/bin/java")
        {
            LinkName = "/etc/passwd",
        });
    }

    private static string TempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"archoera-jdk-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private static void Delete(string path)
    {
        try
        {
            if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private sealed class FakeProvider(
        string sha,
        long size,
        bool latestPatch = true,
        long? archiveSize = null) : IJavaDistributionProvider
    {
        public string Id => "fake";
        public string DisplayName => "Fake JDK";
        public string ShortName => "Fake";
        public string Vendor => "Test";
        public string Homepage => "https://example.test/";
        public bool ListsAllVersions => true;
        public int RecommendedMajor => 21;

        public Task<IReadOnlyList<int>> GetMajorsAsync(
            JavaDistributionPlatform platform,
            CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<int>>([21]);

        public Task<IReadOnlyList<JavaDistributionRelease>> GetReleasesAsync(
            int major,
            JavaDistributionPlatform platform,
            CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<JavaDistributionRelease>>(
            [
                new("v1", major, "21.0.1", platform.ArchiveType, size, sha, true, true, null, latestPatch),
            ]);

        public Task<JavaDistributionArchive> ResolveAsync(
            string versionId,
            JavaDistributionPlatform platform,
            CancellationToken cancellationToken)
            => Task.FromResult(new JavaDistributionArchive(
                Id,
                21,
                "21.0.1",
                "https://example.test/jdk.tar.gz",
                DownloadHashAlgorithm.Sha256,
                sha,
                archiveSize ?? size,
                platform.ArchiveType));
    }

    private sealed class WritingRunner(byte[] bytes) : IDownloadPlanRunner
    {
        public bool IsAvailable => true;

        public Task<PlanExecutionResult> ExecuteAsync(
            DownloadPlan plan,
            CancellationToken cancellationToken = default,
            DownloadProfile profile = DownloadProfile.Default)
        {
            var file = Assert.Single(plan.Files);
            var destination = Path.Combine(plan.Root, file.Dest);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.WriteAllBytes(destination, bytes);
            return Task.FromResult(new PlanExecutionResult(
                "completed",
                true,
                string.Empty,
                string.Empty,
                string.Empty,
                0));
        }
    }
}
