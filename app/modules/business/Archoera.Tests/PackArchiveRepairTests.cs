using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Archoera.Application.Packs;
using Archoera.Infrastructure.Packs;

namespace Archoera.Tests;

public sealed class PackArchiveRepairTests
{
    [Fact]
    public void ReadWithRepair_normalizes_backslash_paths_and_keeps_a_backup()
    {
        var directory = NewDirectory();
        try
        {
            var archivePath = Path.Combine(directory, "pack.mrpack");
            CreateArchive(archivePath, archive =>
            {
                WriteIndex(archive, "mods\\example.jar");
                Write(archive, "overrides/config/bundled.txt", "bundled");
            });
            var original = File.ReadAllBytes(archivePath);

            var (contents, repair) = PackArchiveRepair.ReadWithRepair(archivePath);

            Assert.NotNull(repair);
            Assert.Equal(1, repair!.RepairedPaths);
            Assert.Equal(archivePath + ".bak", repair.BackupPath);
            Assert.True(File.Exists(repair.BackupPath));
            Assert.Equal(original, File.ReadAllBytes(repair.BackupPath));
            Assert.Equal("mods/example.jar", contents.Index.Files[0].Path);
            Assert.Single(contents.Overrides);

            // The file on disk is the repaired archive, not just a parse-time fix.
            Assert.Equal(
                "mods/example.jar",
                PackArchive.Read(archivePath, clientSide: true).Index.Files[0].Path);
        }
        finally
        {
            Delete(directory);
        }
    }

    [Fact]
    public void ReadWithRepair_does_not_repair_paths_that_stay_unsafe()
    {
        var directory = NewDirectory();
        try
        {
            var archivePath = Path.Combine(directory, "pack.mrpack");
            CreateArchive(archivePath, archive =>
            {
                WriteIndex(archive, "..\\evil.jar");
            });

            var exception = Assert.Throws<PackException>(
                () => PackArchiveRepair.ReadWithRepair(archivePath));

            Assert.Equal(PackErrorCode.Invalid, exception.Code);
            Assert.False(File.Exists(archivePath + ".bak"));
        }
        finally
        {
            Delete(directory);
        }
    }

    [Fact]
    public void ReadWithRepair_leaves_archives_without_backslash_paths_alone()
    {
        var directory = NewDirectory();
        try
        {
            var archivePath = Path.Combine(directory, "pack.mrpack");
            CreateArchive(archivePath, archive =>
            {
                WriteIndex(
                    archive,
                    "mods/example.jar",
                    summary: "back\\slash in a non-path string");
            });
            var original = File.ReadAllBytes(archivePath);

            var (contents, repair) = PackArchiveRepair.ReadWithRepair(archivePath);

            Assert.Null(repair);
            Assert.Equal("mods/example.jar", contents.Index.Files[0].Path);
            Assert.Equal(original, File.ReadAllBytes(archivePath));
            Assert.False(File.Exists(archivePath + ".bak"));
        }
        finally
        {
            Delete(directory);
        }
    }

    [Fact]
    public void ReadWithRepair_uses_the_next_free_backup_name()
    {
        var directory = NewDirectory();
        try
        {
            var archivePath = Path.Combine(directory, "pack.mrpack");
            CreateArchive(archivePath, archive =>
            {
                WriteIndex(archive, "mods\\example.jar");
            });
            File.WriteAllText(archivePath + ".bak", "older backup");

            var (_, repair) = PackArchiveRepair.ReadWithRepair(archivePath);

            Assert.NotNull(repair);
            Assert.Equal(archivePath + ".bak2", repair!.BackupPath);
            Assert.Equal("older backup", File.ReadAllText(archivePath + ".bak"));
        }
        finally
        {
            Delete(directory);
        }
    }

    [Fact]
    public void Read_normalizes_backslash_override_entry_names()
    {
        var directory = NewDirectory();
        try
        {
            var archivePath = Path.Combine(directory, "pack.mrpack");
            CreateArchive(archivePath, archive =>
            {
                WriteIndex(archive, "mods/example.jar");
                Write(archive, "client-overrides\\config\\client.txt", "client");
                Write(archive, "overrides\\config\\shared.txt", "shared");
            });

            var contents = PackArchive.Read(archivePath, clientSide: true);

            Assert.Equal(2, contents.Overrides.Count);
            Assert.Contains(
                contents.Overrides,
                entry => entry.RelativePath == "config/client.txt" &&
                         entry.EntryName == "client-overrides\\config\\client.txt");
            Assert.Contains(
                contents.Overrides,
                entry => entry.RelativePath == "config/shared.txt");

            var root = Path.Combine(directory, "instance");
            Directory.CreateDirectory(root);
            var extracted = PackArchive.Extract(archivePath, root, contents.Overrides);

            Assert.Equal(2, extracted.Count);
            Assert.Equal(
                "client",
                File.ReadAllText(Path.Combine(root, "config", "client.txt")));
            Assert.Equal(
                "shared",
                File.ReadAllText(Path.Combine(root, "config", "shared.txt")));
        }
        finally
        {
            Delete(directory);
        }
    }

    private static void WriteIndex(
        ZipArchive archive,
        string filePath,
        string summary = "")
    {
        var json = new
        {
            formatVersion = 1,
            game = "minecraft",
            versionId = "v1",
            name = "Repair test",
            summary,
            files = new[]
            {
                new
                {
                    path = filePath,
                    hashes = new { sha1 = new string('a', 40) },
                    fileSize = 4,
                    downloads = new[] { "https://cdn.example.invalid/example.jar" },
                },
            },
            dependencies = new Dictionary<string, string>
            {
                ["minecraft"] = "1.21.1",
                ["fabric-loader"] = "0.19.3",
            },
        };

        var entry = archive.CreateEntry(ModpackIndexJson.IndexEntryName);
        using var stream = entry.Open();
        JsonSerializer.Serialize(stream, json);
    }

    private static void Write(ZipArchive archive, string name, string text)
    {
        var entry = archive.CreateEntry(name);
        using var stream = entry.Open();
        using var writer = new StreamWriter(stream, new UTF8Encoding(false));
        writer.Write(text);
    }

    private static void CreateArchive(string path, Action<ZipArchive> write)
    {
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        write(archive);
    }

    private static string NewDirectory()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "archoera-pack-repair",
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
