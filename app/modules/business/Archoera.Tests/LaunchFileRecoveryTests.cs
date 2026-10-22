using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Archoera.Domain.Instances;
using Archoera.Domain.Mojang;
using Archoera.Infrastructure.Launch;

namespace Archoera.Tests;

/// <summary>
/// Missing libraries/assets are copied from another local game root when the
/// metadata hash and size match, before the download repair is considered.
/// </summary>
public sealed class LaunchFileRecoveryTests
{
    [Fact]
    public void Copies_a_missing_library_from_another_root()
    {
        var source = CreateRoot();
        var target = CreateRoot();
        try
        {
            var bytes = "library-bytes"u8.ToArray();
            var relative = "org/example/lib/1.0/lib-1.0.jar";
            WriteFile(source, $"libraries/{relative}", bytes);

            var version = Version(
                libraries:
                [
                    Library(
                        "org.example:lib:1.0",
                        relative,
                        bytes),
                ]);
            var paths = Resolve(target);

            var repaired = LaunchFileRecovery.TryRecover(
                Instance(paths.InstanceRoot),
                version,
                paths,
                [source, target]);

            Assert.Equal(1, repaired);
            Assert.Equal(bytes, File.ReadAllBytes(Path.Combine(paths.LibrariesRoot, relative)));
        }
        finally
        {
            Delete(source);
            Delete(target);
        }
    }

    [Fact]
    public void Copies_a_native_classifier_from_another_root()
    {
        var source = CreateRoot();
        var target = CreateRoot();
        try
        {
            var bytes = "native-bytes"u8.ToArray();
            var relative = "org/example/native/1.0/native-1.0-natives-linux.jar";
            WriteFile(source, $"libraries/{relative}", bytes);

            var classifiers = new Dictionary<string, MojangDownload>(
                StringComparer.OrdinalIgnoreCase)
            {
                ["natives-linux"] = Descriptor(relative, bytes),
            };
            var version = Version(
                libraries:
                [
                    new MojangLibrary(
                        "org.example:native:1.0",
                        new MojangLibraryDownloads(null, classifiers),
                        Natives: new Dictionary<string, string>(
                            StringComparer.OrdinalIgnoreCase)
                        {
                            ["linux"] = "natives-linux",
                        }),
                ]);
            var paths = Resolve(target);

            var repaired = LaunchFileRecovery.TryRecover(
                Instance(paths.InstanceRoot),
                version,
                paths,
                [source, target]);

            Assert.Equal(1, repaired);
            Assert.Equal(bytes, File.ReadAllBytes(Path.Combine(paths.LibrariesRoot, relative)));
        }
        finally
        {
            Delete(source);
            Delete(target);
        }
    }

    [Fact]
    public void Does_not_copy_a_file_with_a_wrong_hash()
    {
        var source = CreateRoot();
        var target = CreateRoot();
        try
        {
            var expected = "expected-bytes"u8.ToArray();
            var relative = "org/example/lib/1.0/lib-1.0.jar";
            WriteFile(source, $"libraries/{relative}", "other-bytes!"u8.ToArray());

            var version = Version(
                libraries:
                [
                    Library("org.example:lib:1.0", relative, expected),
                ]);
            var paths = Resolve(target);

            Assert.Equal(
                0,
                LaunchFileRecovery.TryRecover(
                    Instance(paths.InstanceRoot),
                    version,
                    paths,
                    [source, target]));
            Assert.False(File.Exists(Path.Combine(paths.LibrariesRoot, relative)));
        }
        finally
        {
            Delete(source);
            Delete(target);
        }
    }

    [Fact]
    public void Copies_the_asset_index_and_objects()
    {
        var source = CreateRoot();
        var target = CreateRoot();
        try
        {
            var objectBytes = "asset-bytes"u8.ToArray();
            var objectHash = Sha1(objectBytes);
            var indexBytes = Encoding.UTF8.GetBytes(
                "{\"objects\":{\"icons/a.png\":{\"hash\":\"" + objectHash +
                "\",\"size\":" + objectBytes.Length + "}}}");
            var indexHash = Sha1(indexBytes);
            WriteFile(source, "assets/indexes/17.json", indexBytes);
            WriteFile(source, $"assets/objects/{objectHash[..2]}/{objectHash}", objectBytes);

            var version = new MojangVersion
            {
                Id = "1.21.1",
                MainClass = "x",
                Assets = "17",
                AssetIndex = new MojangAssetIndexRef(
                    "17",
                    "https://piston-meta.mojang.com/v1/packages/17.json",
                    indexHash,
                    indexBytes.Length),
            };
            var paths = Resolve(target);

            var repaired = LaunchFileRecovery.TryRecover(
                Instance(paths.InstanceRoot),
                version,
                paths,
                [source, target]);

            Assert.Equal(2, repaired);
            Assert.True(File.Exists(Path.Combine(paths.AssetsRoot, "indexes", "17.json")));
            Assert.Equal(
                objectBytes,
                File.ReadAllBytes(
                    Path.Combine(paths.AssetsRoot, "objects", objectHash[..2], objectHash)));
        }
        finally
        {
            Delete(source);
            Delete(target);
        }
    }

    [Fact]
    public void Copies_the_client_jar_from_the_game_version()
    {
        var source = CreateRoot();
        var target = CreateRoot();
        try
        {
            var bytes = "client-jar"u8.ToArray();
            WriteFile(source, "versions/1.21.1/1.21.1.jar", bytes);

            var version = new MojangVersion
            {
                Id = "1.21.1",
                MainClass = "x",
                Downloads = new MojangVersionDownloads(
                    Descriptor("1.21.1.jar", bytes) with { Path = null }),
            };
            var paths = Resolve(target);

            var repaired = LaunchFileRecovery.TryRecover(
                Instance(paths.InstanceRoot),
                version,
                paths,
                [source, target]);

            Assert.Equal(1, repaired);
            Assert.Equal(
                bytes,
                File.ReadAllBytes(
                    Path.Combine(paths.SharedRoot, "versions", "1.21.1", "1.21.1.jar")));
        }
        finally
        {
            Delete(source);
            Delete(target);
        }
    }

    private static MojangVersion Version(
        IReadOnlyList<MojangLibrary> libraries)
        => new()
        {
            Id = "1.21.1",
            MainClass = "x",
            Libraries = libraries,
        };

    private static MojangLibrary Library(
        string name,
        string relative,
        byte[] bytes)
        => new(
            name,
            new MojangLibraryDownloads(Descriptor(relative, bytes)));

    private static MojangDownload Descriptor(string relative, byte[] bytes)
        => new(
            "https://libraries.minecraft.net/" + relative,
            Sha1(bytes),
            bytes.Length,
            relative);

    private static string Sha1(byte[] bytes)
        => Convert.ToHexString(SHA1.HashData(bytes)).ToLowerInvariant();

    private static string CreateRoot()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "archoera-file-recovery",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "libraries"));
        Directory.CreateDirectory(Path.Combine(root, "assets"));
        Directory.CreateDirectory(Path.Combine(root, "versions"));
        return root;
    }

    private static LaunchPaths Resolve(string target)
    {
        var instanceRoot = Path.Combine(target, "versions", "MyPack");
        Directory.CreateDirectory(instanceRoot);
        return LaunchPaths.Resolve(instanceRoot);
    }

    private static GameInstanceRecord Instance(string root)
        => new(
            "id",
            "MyPack",
            "1.21.1",
            "neoforge",
            "21.1.248",
            root,
            0,
            false,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            null);

    private static void WriteFile(string root, string relative, byte[] bytes)
    {
        var path = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, bytes);
    }

    private static void Delete(string path)
    {
        try
        {
            if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
        }
        catch
        {
            // best effort
        }
    }
}
