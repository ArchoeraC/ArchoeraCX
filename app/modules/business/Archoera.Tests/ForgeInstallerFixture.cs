using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace Archoera.Tests;

/// <summary>
/// Builds a synthetic Forge/NeoForge installer zip in memory. It contains only
/// JSON metadata and placeholder entries, so tests never need Java, the network
/// or a real Minecraft/Forge jar.
/// </summary>
internal static class ForgeInstallerFixture
{
    public const string ForgeGameVersion = "1.20.1";
    public const string ForgeLoaderVersion = "47.2.0";
    public const string ForgeVersionId = "1.20.1-forge-47.2.0";
    public const string ForgeInstallerUrl =
        "https://maven.minecraftforge.net/net/minecraftforge/forge/1.20.1-47.2.0/forge-1.20.1-47.2.0-installer.jar";

    public static string Sha(char character) => new(character, 40);

    public static string Sha1(byte[] bytes)
        => Convert.ToHexString(SHA1.HashData(bytes)).ToLowerInvariant();

    public static byte[] CreateInstaller(
        string profileLibrariesJson,
        string versionLibrariesJson,
        string? clientDownloadsJson = null,
        string gameVersion = ForgeGameVersion,
        string loaderVersion = ForgeLoaderVersion,
        string versionId = ForgeVersionId)
    {
        var profile = $$"""
            {
              "spec": 1,
              "profile": "forge",
              "version": "{{loaderVersion}}",
              "minecraft": "{{gameVersion}}",
              "json": "/version.json",
              "libraries": {{profileLibrariesJson}}
            }
            """;

        var downloads = string.IsNullOrWhiteSpace(clientDownloadsJson)
            ? string.Empty
            : $",\n              \"downloads\": {clientDownloadsJson}";

        var version = $$"""
            {
              "id": "{{versionId}}",
              "inheritsFrom": "{{gameVersion}}",
              "mainClass": "com.example.SyntheticMain"{{downloads}},
              "libraries": {{versionLibrariesJson}}
            }
            """;

        return CreateZip(
            ("install_profile.json", profile),
            ("version.json", version),
            ("data/client.lzma", "synthetic binpatch; never a download"),
            ("maven/com/example/embedded/1.0/embedded-1.0.jar", "synthetic embedded jar"));
    }

    public static ZipArchive OpenZip(byte[] bytes)
        => new(new MemoryStream(bytes, writable: false), ZipArchiveMode.Read);

    public static byte[] CreateZip(params (string Name, string Content)[] entries)
    {
        using var memory = new MemoryStream();
        using (var archive = new ZipArchive(memory, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, content) in entries)
            {
                var entry = archive.CreateEntry(name);
                using var stream = entry.Open();
                var bytes = Encoding.UTF8.GetBytes(content);
                stream.Write(bytes, 0, bytes.Length);
            }
        }

        return memory.ToArray();
    }

    public static string Library(
        string name,
        string path,
        string url,
        string? sha1,
        long? size,
        string hashProperty = "sha1")
    {
        var properties = new List<string>
        {
            $"\"path\": \"{path}\"",
            $"\"url\": \"{url}\"",
        };

        if (sha1 is not null)
        {
            properties.Add($"\"{hashProperty}\": \"{sha1}\"");
        }

        if (size is not null)
        {
            properties.Add($"\"size\": {size}");
        }

        var artifact = string.Join(",\n                  ", properties);

        return $$"""
            {
              "name": "{{name}}",
              "downloads": {
                "artifact": {
                  {{artifact}}
                }
              }
            }
            """;
    }
}
