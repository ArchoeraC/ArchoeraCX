using System.Text;
using Archoera.Application.Packs;
using Archoera.Domain.Downloads;

namespace Archoera.Tests;

public sealed class ModpackIndexTests
{
    [Fact]
    public void Parse_reads_dependencies_environment_and_hashes()
    {
        var index = ModpackIndexJson.Parse(
            Encoding.UTF8.GetBytes(ValidIndex()));

        Assert.Equal("Example pack", index.Name);
        Assert.Equal("26.2", index.MinecraftVersion);
        Assert.Equal("fabric", index.Loader.Kind);
        Assert.Equal("0.19.3", index.Loader.Version);
        Assert.Equal(2, index.Files.Count);
        Assert.True(index.Files[0].ClientUnsupported);
        Assert.False(index.Files[0].ServerUnsupported);
        Assert.Equal(2, index.Files[1].Downloads.Count);
        Assert.Equal(DownloadHashAlgorithm.Sha512, index.Files[1].Hash.Algorithm);
    }

    [Fact]
    public void Parse_rejects_parent_escape()
    {
        var exception = Assert.Throws<PackException>(
            () => ModpackIndexJson.Parse(
                Encoding.UTF8.GetBytes(ValidIndex(path: "../evil.jar"))));
        Assert.Equal(PackErrorCode.PathEscape, exception.Code);
    }

    [Fact]
    public void Parse_rejects_metadata_target()
    {
        var exception = Assert.Throws<PackException>(
            () => ModpackIndexJson.Parse(
                Encoding.UTF8.GetBytes(ValidIndex(path: ".archoera/instance.json"))));
        Assert.Equal(PackErrorCode.PathEscape, exception.Code);
    }

    [Fact]
    public void Parse_rejects_launcher_managed_roots()
    {
        var exception = Assert.Throws<PackException>(
            () => ModpackIndexJson.Parse(
                Encoding.UTF8.GetBytes(
                    ValidIndex(path: "versions/fabric-loader/x.json"))));
        Assert.Equal(PackErrorCode.PathEscape, exception.Code);
    }

    [Fact]
    public void Parse_rejects_duplicate_paths()
    {
        var json = ValidIndex().Replace(
            "config/client-only.json",
            "mods/fabric-api.jar",
            StringComparison.Ordinal);
        var exception = Assert.Throws<PackException>(
            () => ModpackIndexJson.Parse(Encoding.UTF8.GetBytes(json)));
        Assert.Equal(PackErrorCode.DuplicateFile, exception.Code);
    }

    [Fact]
    public void Parse_rejects_unknown_environment_value()
    {
        var json = ValidIndex().Replace(
            "\"client\": \"unsupported\"",
            "\"client\": \"maybe\"",
            StringComparison.Ordinal);
        var exception = Assert.Throws<PackException>(
            () => ModpackIndexJson.Parse(Encoding.UTF8.GetBytes(json)));
        Assert.Equal(PackErrorCode.Invalid, exception.Code);
    }

    [Fact]
    public void Parse_rejects_unsupported_format_version()
    {
        var json = ValidIndex().Replace(
            "\"formatVersion\": 1",
            "\"formatVersion\": 2",
            StringComparison.Ordinal);
        var exception = Assert.Throws<PackException>(
            () => ModpackIndexJson.Parse(Encoding.UTF8.GetBytes(json)));
        Assert.Equal(PackErrorCode.Unsupported, exception.Code);
    }

    [Fact]
    public void Vanilla_pack_has_no_loader()
    {
        var json = """
                   {
                     "formatVersion": 1,
                     "game": "minecraft",
                     "versionId": "vanilla-1",
                     "name": "Vanilla",
                     "files": [],
                     "dependencies": { "minecraft": "1.21.1" }
                   }
                   """;
        var index = ModpackIndexJson.Parse(Encoding.UTF8.GetBytes(json));
        Assert.True(index.Loader.IsVanilla);
        Assert.Empty(index.Files);
    }

    private static string ValidIndex(string path = "mods/fabric-api.jar")
        => $$"""
           {
             "formatVersion": 1,
             "game": "minecraft",
             "versionId": "v1",
             "name": "Example pack",
             "summary": "test",
             "files": [
               {
                 "path": "config/client-only.json",
                 "hashes": {
                   "sha1": "{{new string('a', 40)}}",
                   "sha512": "{{new string('b', 128)}}"
                 },
                 "fileSize": 12,
                 "downloads": [
                   "https://cdn.example.invalid/client-only.json"
                 ],
                 "env": { "client": "unsupported", "server": "required" }
               },
               {
                 "path": "{{path}}",
                 "hashes": {
                   "sha1": "{{new string('c', 40)}}",
                   "sha512": "{{new string('d', 128)}}"
                 },
                 "fileSize": 34,
                 "downloads": [
                   "https://cdn.example.invalid/fabric-api.jar",
                   "https://mirror.example.invalid/fabric-api.jar"
                 ],
                 "env": null
               }
             ],
             "dependencies": {
               "minecraft": "26.2",
               "fabric-loader": "0.19.3"
             }
           }
           """;
}
