using System.IO.Compression;
using Archoera.Application.Packs;
using Archoera.Infrastructure.Packs;

namespace Archoera.Tests;

public sealed class NativePackArchiveTests
{
    [Fact]
    public void Reads_a_technic_forge_legacy_archive()
    {
        var directory = NewDirectory();
        try
        {
            var path = Path.Combine(directory, "RLCraft.zip");
            CreateArchive(path, archive =>
            {
                WriteNestedJar(
                    archive,
                    "bin/modpack.jar",
                    "install_profile.json",
                    """
                    {"profile":"forge","minecraft":"1.12.2","version":"1.12.2-forge-14.23.5.2860"}
                    """);
                Write(archive, "mods/example.jar", "mod");
                Write(archive, "config/example.cfg", "value=1");
            });

            Assert.True(NativePackArchive.TryRead(path, out var pack));
            Assert.Equal(NativePackArchive.TechnicFormat, pack.Format);
            Assert.Equal("1.12.2", pack.GameVersion);
            Assert.Equal("forge", pack.Loader.Kind);
            Assert.Equal("14.23.5.2860", pack.Loader.Version);
            Assert.Equal(
                ["config/example.cfg", "mods/example.jar"],
                pack.Files.Select(entry => entry.RelativePath).OrderBy(item => item));
        }
        finally
        {
            Delete(directory);
        }
    }

    [Fact]
    public void Reads_a_technic_fabric_version_profile()
    {
        var directory = NewDirectory();
        try
        {
            var path = Path.Combine(directory, "Tekxit.zip");
            CreateArchive(path, archive =>
            {
                Write(
                    archive,
                    "bin/version.json",
                    """
                    {"id":"1.19.2","inheritsFrom":"1.19.2","libraries":[
                      {"name":"net.fabricmc:fabric-loader:0.18.4"}
                    ]}
                    """);
                Write(archive, "mods/example.jar", "mod");
            });

            Assert.True(NativePackArchive.TryRead(path, out var pack));
            Assert.Equal(NativePackArchive.TechnicFormat, pack.Format);
            Assert.Equal("1.19.2", pack.GameVersion);
            Assert.Equal("fabric", pack.Loader.Kind);
            Assert.Equal("0.18.4", pack.Loader.Version);
            Assert.Equal("mods/example.jar", Assert.Single(pack.Files).RelativePath);
        }
        finally
        {
            Delete(directory);
        }
    }

    [Fact]
    public void Ignores_a_bin_installer_without_technic_payload()
    {
        var directory = NewDirectory();
        try
        {
            var path = Path.Combine(directory, "installer.zip");
            CreateArchive(path, archive =>
                Write(archive, "bin/modpack.jar", "not a zip"));

            Assert.False(NativePackArchive.TryRead(path, out _));
        }
        finally
        {
            Delete(directory);
        }
    }

    [Fact]
    public void Reads_a_multimc_instance_export()
    {
        var directory = NewDirectory();
        try
        {
            var path = Path.Combine(directory, "My Pack.zip");
            CreateArchive(path, archive =>
            {
                Write(archive, "mmc-pack.json",
                    """
                    {"formatVersion":1,"components":[
                      {"uid":"net.minecraft","version":"1.20.1"},
                      {"uid":"net.fabricmc.fabric-loader","version":"0.15.11"}
                    ]}
                    """);
                Write(archive, "instance.cfg", "name=My Pack\nInstanceType=OneSix\n");
                Write(archive, ".minecraft/mods/example.jar", "mod");
                Write(archive, ".minecraft/config/example.json", "{}");
            });

            Assert.True(NativePackArchive.TryRead(path, out var pack));
            Assert.Equal(NativePackArchive.MultiMcFormat, pack.Format);
            Assert.Equal("My Pack", pack.Name);
            Assert.Equal("1.20.1", pack.GameVersion);
            Assert.Equal("fabric", pack.Loader.Kind);
            Assert.Equal("0.15.11", pack.Loader.Version);
            Assert.Equal(
                ["config/example.json", "mods/example.jar"],
                pack.Files.Select(entry => entry.RelativePath).OrderBy(item => item));
        }
        finally
        {
            Delete(directory);
        }
    }

    [Fact]
    public void Reads_an_atlauncher_instance_export()
    {
        var directory = NewDirectory();
        try
        {
            var path = Path.Combine(directory, "ATL Pack.zip");
            CreateArchive(path, archive =>
            {
                Write(archive, "instance.json",
                    """
                    {"id":"1.19.2","launcher":{
                      "name":"ATL Pack",
                      "description":"Test pack",
                      "version":"2.0",
                      "loaderVersion":{"type":"Forge","version":"43.2.0"},
                      "mods":[]
                    }}
                    """);
                Write(archive, "mods/example.jar", "mod");
                Write(archive, "config/example.toml", "value=1");
            });

            Assert.True(NativePackArchive.TryRead(path, out var pack));
            Assert.Equal(NativePackArchive.AtLauncherFormat, pack.Format);
            Assert.Equal("ATL Pack", pack.Name);
            Assert.Equal("1.19.2", pack.GameVersion);
            Assert.Equal("forge", pack.Loader.Kind);
            Assert.Equal("43.2.0", pack.Loader.Version);
            Assert.Equal("Test pack", pack.Summary);
            Assert.Equal(
                ["config/example.toml", "mods/example.jar"],
                pack.Files.Select(entry => entry.RelativePath).OrderBy(item => item));
        }
        finally
        {
            Delete(directory);
        }
    }

    [Fact]
    public void Handles_a_single_wrapper_folder()
    {
        var directory = NewDirectory();
        try
        {
            var path = Path.Combine(directory, "wrapped.zip");
            CreateArchive(path, archive =>
            {
                Write(archive, "Wrapper/mmc-pack.json",
                    """
                    {"formatVersion":1,"components":[{"uid":"net.minecraft","version":"1.21.1"},{"uid":"org.quiltmc.quilt-loader","version":"0.24.0"}]}
                    """);
                Write(archive, "Wrapper/.minecraft/mods/a.jar", "mod");
            });

            Assert.True(NativePackArchive.TryRead(path, out var pack));
            Assert.Equal("quilt", pack.Loader.Kind);
            Assert.Equal("mods/a.jar", Assert.Single(pack.Files).RelativePath);
        }
        finally
        {
            Delete(directory);
        }
    }

    [Fact]
    public void Rejects_unsafe_native_paths()
    {
        var directory = NewDirectory();
        try
        {
            var path = Path.Combine(directory, "evil.zip");
            CreateArchive(path, archive =>
            {
                Write(archive, "mmc-pack.json",
                    """{"formatVersion":1,"components":[{"uid":"net.minecraft","version":"1.20.1"}]}""");
                Write(archive, ".minecraft/../evil.txt", "evil");
            });

            var error = Assert.Throws<PackException>(
                () => NativePackArchive.TryRead(path, out _));
            Assert.Equal(PackErrorCode.PathEscape, error.Code);
        }
        finally
        {
            Delete(directory);
        }
    }

    [Fact]
    public void Ignores_archives_without_a_native_marker()
    {
        var directory = NewDirectory();
        try
        {
            var path = Path.Combine(directory, "other.zip");
            CreateArchive(path, archive => Write(archive, "mods/a.jar", "mod"));

            Assert.False(NativePackArchive.TryRead(path, out _));
        }
        finally
        {
            Delete(directory);
        }
    }

    [Fact]
    public async Task Inspect_reports_the_native_format()
    {
        var directory = NewDirectory();
        try
        {
            var path = Path.Combine(directory, "inspect.zip");
            CreateArchive(path, archive =>
            {
                Write(archive, "mmc-pack.json",
                    """{"formatVersion":1,"components":[{"uid":"net.minecraft","version":"1.21.1"},{"uid":"net.fabricmc.fabric-loader","version":"0.16.9"}]}""");
                Write(archive, ".minecraft/mods/a.jar", "mod");
            });

            var service = new PackModuleService();
            var payload = System.Text.Json.JsonSerializer.Serialize(new { path });
            using var document = System.Text.Json.JsonDocument.Parse(
                await service.HandleAsync("modpack.inspect", payload, CancellationToken.None));

            var data = document.RootElement.GetProperty("data");
            Assert.Equal("multimc", data.GetProperty("format").GetString());
            Assert.Equal("1.21.1", data.GetProperty("gameVersion").GetString());
            Assert.Equal("fabric", data.GetProperty("loader").GetString());
        }
        finally
        {
            Delete(directory);
        }
    }

    private static void Write(ZipArchive archive, string name, string text)
    {
        var entry = archive.CreateEntry(name);
        using var stream = entry.Open();
        using var writer = new StreamWriter(stream);
        writer.Write(text);
    }

    private static void WriteNestedJar(
        ZipArchive archive,
        string name,
        string nestedName,
        string text)
    {
        using var memory = new MemoryStream();
        using (var nested = new ZipArchive(memory, ZipArchiveMode.Create, leaveOpen: true))
        {
            Write(nested, nestedName, text);
        }

        memory.Position = 0;
        var entry = archive.CreateEntry(name);
        using var stream = entry.Open();
        memory.CopyTo(stream);
    }

    private static void CreateArchive(string path, Action<ZipArchive> write)
    {
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        write(archive);
    }

    private static string NewDirectory()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            "archoera-native-pack",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
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
