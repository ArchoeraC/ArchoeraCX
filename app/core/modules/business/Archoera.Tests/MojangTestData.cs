using Archoera.Domain.Mojang;

namespace Archoera.Tests;

/// <summary>
/// Shared in-memory Mojang metadata used by the plan/rule tests. No test touches
/// the network or a real Minecraft installation.
/// </summary>
internal static class MojangTestData
{
    public static readonly string Root = Path.Combine(
        Path.GetTempPath(),
        "archoera-mojang-plan");

    public const string VersionId = "1.20.1";
    public const string AssetIndexId = "5";
    public const string LoggingFileName = "client-1.12.xml";

    public static string Sha1(char character) => new(character, 40);

    public static string ClientPath => $"versions/{VersionId}/{VersionId}.jar";

    public static string CommonPath => "libraries/org/example/common/1.0/common-1.0.jar";

    public static string LinuxPath => "libraries/org/example/linux/1.0/linux-1.0.jar";

    public static string WindowsPath => "libraries/org/example/windows/1.0/windows-1.0.jar";

    public static string MacOsPath => "libraries/org/example/macos/1.0/macos-1.0.jar";

    public static string X64Path => "libraries/org/example/archx64/1.0/archx64-1.0.jar";

    public static string Arm64Path => "libraries/org/example/arm64/1.0/arm64-1.0.jar";

    public static string NativePath =>
        "libraries/org/example/natives/1.0/natives-1.0-natives-linux.jar";

    public static string AssetIndexPath => $"assets/indexes/{AssetIndexId}.json";

    public static string AssetAPath => $"assets/objects/aa/{Sha1('a')}";

    public static string AssetBPath => $"assets/objects/bb/{Sha1('b')}";

    public static string LoggingPath => $"logging/{LoggingFileName}";

    public static MojangRule AllowOs(string? name, string? arch = null)
        => new(MojangRuleAction.Allow, new MojangRuleOs(name, arch, null));

    public static MojangVersion Version()
    {
        return new MojangVersion
        {
            Id = VersionId,
            Type = "release",
            MainClass = "net.minecraft.client.main.Main",
            Assets = AssetIndexId,
            AssetIndex = new MojangAssetIndexRef(
                AssetIndexId,
                "https://piston-meta.mojang.com/v1/packages/index/5.json",
                Sha1('8'),
                500,
                2_000),
            Downloads = new MojangVersionDownloads(
                new MojangDownload(
                    "https://piston-data.mojang.com/v1/objects/client/client.jar",
                    Sha1('1'),
                    1_000)),
            JavaVersion = new MojangJavaVersion("java-runtime-gamma", 17),
            Logging = new MojangLogging(
                "-Dlog4j.configurationFile=${path}",
                new MojangLoggingFile(
                    LoggingFileName,
                    "https://piston-data.mojang.com/v1/objects/log/client-1.12.xml",
                    Sha1('7'),
                    42,
                    "log4j2-xml")),
            Libraries =
            [
                Library("common", '2', 10),
                Library("linux", '3', 11, [AllowOs("linux")]),
                Library("windows", '4', 12, [AllowOs("windows")]),
                Library("macos", '5', 13, [AllowOs("osx")]),
                Library("archx64", 'c', 14, [AllowOs(null, "x86_64")]),
                Library("arm64", 'd', 15, [AllowOs(null, "arm64")]),
                NativeLibrary(),
            ],
            ReleaseTime = DateTimeOffset.Parse("2024-08-08T12:00:00Z"),
        };
    }

    public static MojangAssetIndex AssetIndex()
    {
        var objects = new Dictionary<string, MojangAssetObject>(StringComparer.Ordinal)
        {
            ["icons/a.png"] = new(Sha1('a'), 5),
            // Same bytes under a second logical name: must be downloaded once.
            ["icons/b.png"] = new(Sha1('a'), 5),
            ["sounds/c.ogg"] = new(Sha1('b'), 7),
        };

        return new MojangAssetIndex(
            AssetIndexId,
            objects,
            Virtual: false,
            MapToResources: false);
    }

    public static MojangLibrary Library(
        string artifact,
        char hashCharacter,
        long size,
        IReadOnlyList<MojangRule>? rules = null)
    {
        var path = $"org/example/{artifact}/1.0/{artifact}-1.0.jar";
        return new MojangLibrary(
            $"org.example:{artifact}:1.0",
            new MojangLibraryDownloads(
                new MojangDownload(
                    "https://libraries.minecraft.net/" + path,
                    Sha1(hashCharacter),
                    size,
                    path)),
            Rules: rules);
    }

    private static MojangLibrary NativeLibrary()
    {
        var classifiers = new Dictionary<string, MojangDownload>(StringComparer.OrdinalIgnoreCase)
        {
            ["natives-linux"] = new(
                "https://libraries.minecraft.net/" + NativePath["libraries/".Length..],
                Sha1('6'),
                20,
                NativePath["libraries/".Length..]),
        };

        return new MojangLibrary(
            "org.example:natives:1.0",
            new MojangLibraryDownloads(null, classifiers),
            Natives: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["linux"] = "natives-linux",
            },
            Rules: [AllowOs("linux")]);
    }
}
