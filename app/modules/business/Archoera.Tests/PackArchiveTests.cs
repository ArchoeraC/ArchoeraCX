using System.IO.Compression;
using System.Text;
using Archoera.Application.Packs;
using Archoera.Infrastructure.Packs;

namespace Archoera.Tests;

public sealed class PackArchiveTests
{
    [Fact]
    public void Read_applies_client_overrides_and_reports_server_files()
    {
        var directory = NewDirectory();
        try
        {
            var archivePath = Path.Combine(directory, "pack.mrpack");
            CreateArchive(archivePath, writer =>
            {
                WriteIndex(writer);
                Write(writer, "overrides/mods/bundled.jar", "bundled");
                Write(writer, "overrides/config/shared.txt", "shared");
                Write(writer, "client-overrides/config/shared.txt", "client");
                Write(writer, "server-overrides/server-only.txt", "server");
            });

            var contents = PackArchive.Read(archivePath, clientSide: true);

            Assert.Equal(2, contents.Overrides.Count);
            Assert.Equal(1, contents.IgnoredServerFiles);
            Assert.Contains(
                contents.Overrides,
                entry => entry.RelativePath == "config/shared.txt" &&
                         entry.EntryName.StartsWith("client-overrides/", StringComparison.Ordinal));

            var root = Path.Combine(directory, "instance");
            Directory.CreateDirectory(root);
            var extracted = PackArchive.Extract(archivePath, root, contents.Overrides);

            Assert.Equal(2, extracted.Count);
            Assert.Equal(
                "client",
                File.ReadAllText(Path.Combine(root, "config", "shared.txt")));
            Assert.True(File.Exists(Path.Combine(root, "mods", "bundled.jar")));
            Assert.False(File.Exists(Path.Combine(root, "server-only.txt")));

            var plan = ModpackPlanBuilder.Build(
                root,
                contents.Index,
                extracted,
                clientSide: true);
            Assert.Single(plan.Files);
            Assert.Equal("mods/index.jar", plan.Files[0].Dest);
        }
        finally
        {
            Delete(directory);
        }
    }

    [Fact]
    public void Read_rejects_parent_escape_in_overrides()
    {
        var directory = NewDirectory();
        try
        {
            var archivePath = Path.Combine(directory, "pack.mrpack");
            CreateArchive(archivePath, writer =>
            {
                WriteIndex(writer);
                Write(writer, "overrides/../evil.txt", "evil");
            });

            var exception = Assert.Throws<PackException>(
                () => PackArchive.Read(archivePath, clientSide: true));
            Assert.Equal(PackErrorCode.PathEscape, exception.Code);
        }
        finally
        {
            Delete(directory);
        }
    }

    [Fact]
    public void Read_rejects_symlink_entries()
    {
        var directory = NewDirectory();
        try
        {
            var archivePath = Path.Combine(directory, "pack.mrpack");
            CreateArchive(archivePath, writer =>
            {
                WriteIndex(writer);
                var entry = writer.CreateEntry("overrides/config/link");
                entry.ExternalAttributes = unchecked((int)0xA1FF0000);
                using var stream = entry.Open();
                using var text = new StreamWriter(stream);
                text.Write("/etc/passwd");
            });

            var exception = Assert.Throws<PackException>(
                () => PackArchive.Read(archivePath, clientSide: true));
            Assert.Equal(PackErrorCode.ArchiveInvalid, exception.Code);
        }
        finally
        {
            Delete(directory);
        }
    }

    [Fact]
    public void Extract_rejects_sidecar_target_names()
    {
        var directory = NewDirectory();
        try
        {
            var archivePath = Path.Combine(directory, "pack.mrpack");
            CreateArchive(archivePath, writer =>
            {
                WriteIndex(writer);
                Write(writer, "overrides/config/settings.json.part", "partial");
            });

            var exception = Assert.Throws<PackException>(
                () => PackArchive.Read(archivePath, clientSide: true));
            Assert.Equal(PackErrorCode.ArchiveInvalid, exception.Code);
        }
        finally
        {
            Delete(directory);
        }
    }

    private static void WriteIndex(ZipArchive archive)
    {
        var entry = archive.CreateEntry(ModpackIndexJson.IndexEntryName);
        using var stream = entry.Open();
        using var writer = new StreamWriter(stream, new UTF8Encoding(false));
        writer.Write("""
                     {
                       "formatVersion": 1,
                       "game": "minecraft",
                       "versionId": "v1",
                       "name": "Archive test",
                       "files": [
                         {
                           "path": "mods/index.jar",
                           "hashes": { "sha1": "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa" },
                           "fileSize": 4,
                           "downloads": ["https://cdn.example.invalid/index.jar"]
                         },
                         {
                           "path": "config/shared.txt",
                           "hashes": { "sha1": "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb" },
                           "fileSize": 6,
                           "downloads": ["https://cdn.example.invalid/shared.txt"],
                           "env": null
                         }
                       ],
                       "dependencies": { "minecraft": "1.21.1", "fabric-loader": "0.19.3" }
                     }
                     """);
    }

    private static void Write(ZipArchive archive, string name, string text)
    {
        var entry = archive.CreateEntry(name);
        using var stream = entry.Open();
        using var writer = new StreamWriter(stream, new UTF8Encoding(false));
        writer.Write(text);
    }

    private static void CreateArchive(
        string path,
        Action<ZipArchive> write)
    {
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        write(archive);
    }

    private static string NewDirectory()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "archoera-pack-archive",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static void Delete(string directory)
    {
        try
        {
            Directory.Delete(directory, recursive: true);
        }
        catch
        {
            // best effort on platforms with transient file handles
        }
    }
}
