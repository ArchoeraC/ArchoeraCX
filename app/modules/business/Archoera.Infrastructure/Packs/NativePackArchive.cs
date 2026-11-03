using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Archoera.Application.Packs;
using Archoera.Domain.Downloads;

namespace Archoera.Infrastructure.Packs;

/// <summary>
/// Parsed local modpack that is not Modrinth. Supported today:
/// <list type="bullet">
/// <item>MultiMC / Prism instance export: <c>mmc-pack.json</c> + <c>instance.cfg</c>,
/// game files under <c>.minecraft/</c> or <c>minecraft/</c>.</item>
/// <item>ATLauncher instance export: root <c>instance.json</c> with a
/// <c>launcher</c> object, game files at the archive root.</item>
/// <item>Technic legacy archive: root <c>bin/modpack.jar</c> (Forge installer)
/// or <c>bin/version.json</c> (Fabric/Quilt/Forge profile) plus bundled
/// <c>mods/</c>, <c>config/</c> and <c>resources/</c>.</item>
/// </list>
/// All formats bundle the game files, so no provider API or account is needed.
/// </summary>
public sealed record NativePackContents(
    string Format,
    string Name,
    string VersionId,
    string Summary,
    string GameVersion,
    ModpackLoader Loader,
    IReadOnlyList<PackArchiveEntry> Files,
    long TotalBytes);

public static class NativePackArchive
{
    public const string MultiMcFormat = "multimc";
    public const string AtLauncherFormat = "atlauncher";
    public const string TechnicFormat = "technic";

    private const int MaxMetadataBytes = 2 * 1024 * 1024;
    private const long MaxInstallerBytes = 64L * 1024 * 1024;
    private const int MaxFiles = 100_000;
    private const long MaxTotalBytes = 16L << 30;

    private static readonly string[] RootMetadata =
    [
        "mmc-pack.json",
        "instance.cfg",
        "instance.json",
        "icon.png",
        "icon.jpg",
        "icon.jpeg",
    ];

    /// <summary>
    /// Returns true when the archive is a supported native instance export.
    /// Malformed native metadata throws <see cref="PackException"/>.
    /// </summary>
    public static bool TryRead(string archivePath, out NativePackContents contents)
    {
        contents = default!;
        var fullPath = Path.GetFullPath(archivePath);
        ZipArchive archive;
        try
        {
            archive = ZipFile.OpenRead(fullPath);
        }
        catch (Exception exception) when (
            exception is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            return false;
        }

        using (archive)
        {
            if (archive.Entries.Count > 250_000) return false;
            var prefix = FindWrapperPrefix(archive);
            var mmc = FindEntry(archive, prefix, "mmc-pack.json");
            var atl = FindEntry(archive, prefix, "instance.json");
            if (mmc is not null)
            {
                contents = ReadMultiMc(archive, fullPath, prefix, mmc);
                return true;
            }

            if (atl is not null && LooksLikeAtLauncher(atl))
            {
                contents = ReadAtLauncher(archive, fullPath, prefix, atl);
                return true;
            }

            var modpackJar = FindEntry(archive, prefix, "bin/modpack.jar");
            var versionJson = FindEntry(archive, prefix, "bin/version.json");
            if ((modpackJar is not null || versionJson is not null) &&
                HasTechnicPayload(archive, prefix))
            {
                contents = ReadTechnic(
                    archive,
                    fullPath,
                    prefix,
                    modpackJar,
                    versionJson);
                return true;
            }

            return false;
        }
    }

    private static NativePackContents ReadMultiMc(
        ZipArchive archive,
        string archivePath,
        string prefix,
        ZipArchiveEntry metaEntry)
    {
        using var document = ParseJson(ReadEntry(metaEntry), "mmc-pack.json");
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("components", out var components) ||
            components.ValueKind != JsonValueKind.Array)
        {
            throw Invalid("mmc-pack.json has no components array");
        }

        string? gameVersion = null;
        string? loaderKind = null;
        string? loaderVersion = null;
        foreach (var component in components.EnumerateArray())
        {
            if (component.ValueKind != JsonValueKind.Object) continue;
            var uid = String(component, "uid");
            var version = String(component, "version");
            if (string.IsNullOrWhiteSpace(uid) || string.IsNullOrWhiteSpace(version))
            {
                continue;
            }

            switch (uid)
            {
                case "net.minecraft":
                    gameVersion = version;
                    break;
                case "net.fabricmc.fabric-loader":
                    SetLoader(ref loaderKind, ref loaderVersion, "fabric", version);
                    break;
                case "org.quiltmc.quilt-loader":
                    SetLoader(ref loaderKind, ref loaderVersion, "quilt", version);
                    break;
                case "net.minecraftforge":
                    SetLoader(ref loaderKind, ref loaderVersion, "forge", version);
                    break;
                case "net.neoforged":
                case "net.neoforged.neoforge":
                    SetLoader(ref loaderKind, ref loaderVersion, "neoforge", version);
                    break;
            }
        }

        if (string.IsNullOrWhiteSpace(gameVersion))
        {
            throw Invalid("mmc-pack.json has no net.minecraft component");
        }

        var name = ReadInstanceName(archive, prefix) ??
                   Path.GetFileNameWithoutExtension(archivePath);
        var gamePrefix = FindGamePrefix(archive, prefix);
        var files = ReadFiles(
            archive,
            prefix,
            gamePrefix,
            skipRootMetadata: true,
            out var totalBytes);
        return new NativePackContents(
            MultiMcFormat,
            name,
            "local",
            string.Empty,
            gameVersion,
            new ModpackLoader(loaderKind ?? "vanilla", loaderVersion ?? string.Empty),
            files,
            totalBytes);
    }

    private static NativePackContents ReadAtLauncher(
        ZipArchive archive,
        string archivePath,
        string prefix,
        ZipArchiveEntry metaEntry)
    {
        using var document = ParseJson(ReadEntry(metaEntry), "instance.json");
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw Invalid("instance.json must be an object");
        }

        var gameVersion = String(root, "id")
            ?? throw Invalid("instance.json has no Minecraft version (id)");
        var launcher = root.TryGetProperty("launcher", out var launcherElement) &&
                       launcherElement.ValueKind == JsonValueKind.Object
            ? launcherElement
            : default;
        var name = String(launcher, "name")
            ?? String(root, "name")
            ?? Path.GetFileNameWithoutExtension(archivePath);
        var summary = String(launcher, "description") ?? string.Empty;
        var versionId = String(launcher, "version") ?? "local";
        var vanilla = launcher.ValueKind == JsonValueKind.Object &&
                      launcher.TryGetProperty("vanillaInstance", out var vanillaElement) &&
                      vanillaElement.ValueKind == JsonValueKind.True;

        string kind = "vanilla";
        var loaderVersion = string.Empty;
        if (!vanilla &&
            launcher.ValueKind == JsonValueKind.Object &&
            launcher.TryGetProperty("loaderVersion", out var loader) &&
            loader.ValueKind == JsonValueKind.Object)
        {
            var type = String(loader, "type") ?? string.Empty;
            loaderVersion = String(loader, "version")
                ?? String(loader, "rawVersion")
                ?? string.Empty;
            kind = type.ToLowerInvariant() switch
            {
                "fabric" => "fabric",
                "quilt" => "quilt",
                "forge" => "forge",
                "neoforge" => "neoforge",
                "vanilla" or "" => "vanilla",
                _ => throw new PackException(
                    PackErrorCode.LoaderUnsupported,
                    $"ATLauncher loader '{type}' is not supported"),
            };
            if (kind != "vanilla" && string.IsNullOrWhiteSpace(loaderVersion))
            {
                throw new PackException(
                    PackErrorCode.LoaderMetadata,
                    $"ATLauncher loader '{type}' has no version");
            }
        }

        var files = ReadFiles(
            archive,
            prefix,
            gamePrefix: string.Empty,
            skipRootMetadata: true,
            out var totalBytes);
        return new NativePackContents(
            AtLauncherFormat,
            name,
            versionId,
            summary,
            gameVersion,
            new ModpackLoader(kind, loaderVersion),
            files,
            totalBytes);
    }

    private static NativePackContents ReadTechnic(
        ZipArchive archive,
        string archivePath,
        string prefix,
        ZipArchiveEntry? modpackJar,
        ZipArchiveEntry? versionJson)
    {
        var gameVersion = string.Empty;
        var loaderKind = "vanilla";
        var loaderVersion = string.Empty;
        if (modpackJar is not null)
        {
            using var profile = ReadNestedJson(modpackJar, "install_profile.json")
                ?? ReadNestedJson(modpackJar, "version.json")
                ?? throw Invalid("Technic modpack.jar has no install profile");
            ReadForgeProfile(
                profile.RootElement,
                ref gameVersion,
                ref loaderKind,
                ref loaderVersion);
        }
        else if (versionJson is not null)
        {
            using var document = ParseJson(
                ReadEntry(versionJson),
                "bin/version.json");
            ReadVersionProfile(
                document.RootElement,
                ref gameVersion,
                ref loaderKind,
                ref loaderVersion);
        }

        if (string.IsNullOrWhiteSpace(gameVersion))
        {
            throw Invalid("Technic archive does not declare a Minecraft version");
        }

        if (loaderKind != "vanilla" && string.IsNullOrWhiteSpace(loaderVersion))
        {
            throw new PackException(
                PackErrorCode.LoaderMetadata,
                $"Technic archive has no {loaderKind} version");
        }

        var files = ReadFiles(
            archive,
            prefix,
            gamePrefix: string.Empty,
            skipRootMetadata: true,
            out var totalBytes,
            skipPrefix: "bin/");
        if (files.Count == 0)
        {
            throw Invalid("Technic archive contains no game files");
        }

        return new NativePackContents(
            TechnicFormat,
            Path.GetFileNameWithoutExtension(archivePath),
            "local",
            string.Empty,
            gameVersion,
            new ModpackLoader(loaderKind, loaderVersion),
            files,
            totalBytes);
    }

    private static bool HasTechnicPayload(ZipArchive archive, string prefix)
    {
        foreach (var entry in archive.Entries)
        {
            var name = StripPrefix(
                PackArchive.NormalizeEntryName(entry.FullName),
                prefix);
            if (name.StartsWith("mods/", StringComparison.OrdinalIgnoreCase) ||
                name.StartsWith("config/", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static JsonDocument? ReadNestedJson(
        ZipArchiveEntry containerEntry,
        string name)
    {
        var bytes = ReadEntry(containerEntry, MaxInstallerBytes);
        using var memory = new MemoryStream(bytes, writable: false);
        ZipArchive jar;
        try
        {
            jar = new ZipArchive(memory, ZipArchiveMode.Read);
        }
        catch (InvalidDataException exception)
        {
            throw new PackException(
                PackErrorCode.ArchiveInvalid,
                $"{containerEntry.FullName} is not a valid jar: {exception.Message}",
                exception);
        }

        using (jar)
        {
            var entry = jar.GetEntry(name);
            if (entry is null) return null;
            return ParseJson(ReadEntry(entry), $"{containerEntry.FullName}!/{name}");
        }
    }

    private static void ReadForgeProfile(
        JsonElement root,
        ref string gameVersion,
        ref string loaderKind,
        ref string loaderVersion)
    {
        var version = String(root, "version")
            ?? String(root, "id")
            ?? String(root, "path");
        var minecraft = String(root, "minecraft");
        if (string.IsNullOrWhiteSpace(version)) return;

        var profile = String(root, "profile") ?? String(root, "type");
        loaderKind = version.Contains("neoforge", StringComparison.OrdinalIgnoreCase)
            ? "neoforge"
            : version.Contains("forge", StringComparison.OrdinalIgnoreCase) ||
              string.Equals(profile, "forge", StringComparison.OrdinalIgnoreCase)
                ? "forge"
                : version.Contains("fabric", StringComparison.OrdinalIgnoreCase)
                    ? "fabric"
                    : "vanilla";

        var marker = $"-{loaderKind}-";
        var markerIndex = version.LastIndexOf(
            marker,
            StringComparison.OrdinalIgnoreCase);
        if (markerIndex > 0)
        {
            if (string.IsNullOrWhiteSpace(minecraft))
            {
                minecraft = version[..markerIndex];
            }

            loaderVersion = version[(markerIndex + marker.Length)..];
        }
        else
        {
            var colon = version.LastIndexOf(':');
            var tail = colon >= 0 ? version[(colon + 1)..] : version;
            var dash = tail.IndexOf('-');
            if (dash > 0 && string.IsNullOrWhiteSpace(minecraft))
            {
                minecraft = tail[..dash];
                loaderVersion = tail[(dash + 1)..];
            }
            else
            {
                loaderVersion = tail;
            }
        }

        gameVersion = minecraft ?? string.Empty;
    }

    private static void ReadVersionProfile(
        JsonElement root,
        ref string gameVersion,
        ref string loaderKind,
        ref string loaderVersion)
    {
        gameVersion = String(root, "inheritsFrom")
            ?? String(root, "id")
            ?? string.Empty;
        if (!root.TryGetProperty("libraries", out var libraries) ||
            libraries.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (var library in libraries.EnumerateArray())
        {
            if (library.ValueKind != JsonValueKind.Object) continue;
            var name = String(library, "name");
            if (string.IsNullOrWhiteSpace(name)) continue;
            if (name.StartsWith("net.fabricmc:fabric-loader:", StringComparison.OrdinalIgnoreCase))
            {
                loaderKind = "fabric";
                loaderVersion = name["net.fabricmc:fabric-loader:".Length..];
                return;
            }

            if (name.StartsWith("org.quiltmc:quilt-loader:", StringComparison.OrdinalIgnoreCase))
            {
                loaderKind = "quilt";
                loaderVersion = name["org.quiltmc:quilt-loader:".Length..];
                return;
            }

            if (name.StartsWith("net.minecraftforge:forge:", StringComparison.OrdinalIgnoreCase))
            {
                loaderKind = "forge";
                loaderVersion = VersionAfterPrefix(
                    name["net.minecraftforge:forge:".Length..],
                    gameVersion);
                return;
            }

            if (name.StartsWith("net.neoforged:neoforge:", StringComparison.OrdinalIgnoreCase))
            {
                loaderKind = "neoforge";
                loaderVersion = VersionAfterPrefix(
                    name["net.neoforged:neoforge:".Length..],
                    gameVersion);
                return;
            }
        }
    }

    private static string VersionAfterPrefix(string version, string gameVersion)
        => gameVersion.Length > 0 &&
           version.StartsWith(gameVersion + "-", StringComparison.OrdinalIgnoreCase)
            ? version[(gameVersion.Length + 1)..]
            : version;

    private static IReadOnlyList<PackArchiveEntry> ReadFiles(
        ZipArchive archive,
        string wrapperPrefix,
        string gamePrefix,
        bool skipRootMetadata,
        out long totalBytes,
        string? skipPrefix = null)
    {
        var result = new List<PackArchiveEntry>();
        totalBytes = 0;
        var count = 0;
        foreach (var entry in archive.Entries)
        {
            var raw = PackArchive.NormalizeEntryName(entry.FullName);
            var name = StripPrefix(raw, wrapperPrefix);
            if (string.IsNullOrEmpty(name)) continue;
            if (skipPrefix is not null &&
                name.StartsWith(skipPrefix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (name.EndsWith('/')) continue;
            if (IsDirectory(entry)) continue;
            if (skipRootMetadata && IsMetadata(name)) continue;
            if (gamePrefix.Length > 0 &&
                name.StartsWith(gamePrefix, StringComparison.OrdinalIgnoreCase))
            {
                name = name[gamePrefix.Length..];
            }
            else if (gamePrefix.Length == 0 &&
                     name.StartsWith(".minecraft/", StringComparison.OrdinalIgnoreCase))
            {
                name = name[".minecraft/".Length..];
            }
            else if (gamePrefix.Length == 0 &&
                     name.StartsWith("minecraft/", StringComparison.OrdinalIgnoreCase))
            {
                name = name["minecraft/".Length..];
            }

            if (string.IsNullOrEmpty(name)) continue;
            if (IsMetadata(name)) continue;

            var issue = DownloadPlanPaths.TryNormalizeDest(name, out var relative);
            if (issue is not null)
            {
                throw new PackException(
                    issue.Code == DownloadPlanErrorCode.PathEscape
                        ? PackErrorCode.PathEscape
                        : PackErrorCode.ArchiveInvalid,
                    $"unsafe instance path '{raw}': {issue.Message}");
            }

            ModpackIndexJson.RejectReservedTarget(relative);
            PackArchive.RejectLinkEntry(entry);

            var length = entry.Length;
            if (length < 0 || length > PackArchive.MaxOverrideEntryBytes)
            {
                throw new PackException(
                    PackErrorCode.TooLarge,
                    $"{raw}: file is larger than {PackArchive.MaxOverrideEntryBytes} bytes");
            }

            if (PackArchive.ExceedsCompressionRatio(length, entry.CompressedLength))
            {
                throw new PackException(
                    PackErrorCode.ArchiveBomb,
                    $"{raw}: file expands more than {PackArchive.MaxCompressionRatio}:1");
            }

            totalBytes += length;
            if (totalBytes > MaxTotalBytes)
            {
                throw new PackException(
                    PackErrorCode.TooLarge,
                    "native pack exceeds the uncompressed size limit");
            }

            count++;
            if (count > MaxFiles)
            {
                throw new PackException(
                    PackErrorCode.TooLarge,
                    $"native pack contains more than {MaxFiles} files");
            }

            result.Add(new PackArchiveEntry(raw, relative, length, entry.ExternalAttributes));
        }

        return result;
    }

    private static string FindWrapperPrefix(ZipArchive archive)
    {
        string? first = null;
        foreach (var entry in archive.Entries)
        {
            var name = PackArchive.NormalizeEntryName(entry.FullName);
            if (name.EndsWith('/') || IsDirectory(entry)) continue;
            if (name.StartsWith("__MACOSX/", StringComparison.OrdinalIgnoreCase) ||
                name.EndsWith(".DS_Store", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var slash = name.IndexOf('/');
            if (slash <= 0) return string.Empty;
            var segment = name[..(slash + 1)];
            if (first is null)
            {
                first = segment;
            }
            else if (!string.Equals(first, segment, StringComparison.Ordinal))
            {
                return string.Empty;
            }
        }

        return first ?? string.Empty;
    }

    private static ZipArchiveEntry? FindEntry(
        ZipArchive archive,
        string prefix,
        string name)
        => archive.Entries.FirstOrDefault(entry =>
            string.Equals(
                StripPrefix(PackArchive.NormalizeEntryName(entry.FullName), prefix),
                name,
                StringComparison.OrdinalIgnoreCase));

    private static string? ReadInstanceName(ZipArchive archive, string prefix)
    {
        var config = FindEntry(archive, prefix, "instance.cfg");
        if (config is null) return null;
        var text = Encoding.UTF8.GetString(ReadEntry(config));
        foreach (var line in text.Split('\n'))
        {
            var trimmed = line.Trim();
            if (!trimmed.StartsWith("name=", StringComparison.OrdinalIgnoreCase)) continue;
            var name = trimmed["name=".Length..].Trim();
            if (name.Length > 0) return name;
        }

        return null;
    }

    private static string FindGamePrefix(ZipArchive archive, string prefix)
    {
        foreach (var entry in archive.Entries)
        {
            var name = StripPrefix(
                PackArchive.NormalizeEntryName(entry.FullName),
                prefix);
            if (name.StartsWith(".minecraft/", StringComparison.OrdinalIgnoreCase))
            {
                return ".minecraft/";
            }

            if (name.StartsWith("minecraft/", StringComparison.OrdinalIgnoreCase))
            {
                return "minecraft/";
            }
        }

        return string.Empty;
    }

    private static bool LooksLikeAtLauncher(ZipArchiveEntry entry)
    {
        try
        {
            using var document = ParseJson(ReadEntry(entry), "instance.json");
            return document.RootElement.ValueKind == JsonValueKind.Object &&
                   document.RootElement.TryGetProperty("launcher", out var launcher) &&
                   launcher.ValueKind == JsonValueKind.Object;
        }
        catch (PackException)
        {
            return false;
        }
    }

    private static bool IsMetadata(string name)
    {
        foreach (var metadata in RootMetadata)
        {
            if (string.Equals(name, metadata, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return name.StartsWith("patches/", StringComparison.OrdinalIgnoreCase) ||
               name.StartsWith("__MACOSX/", StringComparison.OrdinalIgnoreCase) ||
               name.EndsWith(".DS_Store", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsDirectory(ZipArchiveEntry entry)
    {
        var mode = (uint)entry.ExternalAttributes >> 16;
        return (entry.ExternalAttributes & 0x10) != 0 ||
               (mode & 0xF000) == 0x4000;
    }

    private static string StripPrefix(string name, string prefix)
        => prefix.Length > 0 &&
           name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? name[prefix.Length..]
            : name;

    private static void SetLoader(
        ref string? kind,
        ref string? version,
        string nextKind,
        string nextVersion)
    {
        if (kind is not null && !string.Equals(kind, nextKind, StringComparison.Ordinal))
        {
            throw new PackException(
                PackErrorCode.Unsupported,
                $"native pack declares multiple loaders: {kind}, {nextKind}");
        }

        kind = nextKind;
        version = nextVersion;
    }

    private static JsonDocument ParseJson(byte[] bytes, string what)
    {
        try
        {
            return JsonDocument.Parse(bytes);
        }
        catch (JsonException exception)
        {
            throw new PackException(
                PackErrorCode.Invalid,
                $"{what} is not valid JSON: {exception.Message}",
                exception);
        }
    }

    private static byte[] ReadEntry(ZipArchiveEntry entry)
        => ReadEntry(entry, MaxMetadataBytes);

    private static byte[] ReadEntry(ZipArchiveEntry entry, long maxBytes)
    {
        if (entry.Length > maxBytes)
        {
            throw new PackException(
                PackErrorCode.TooLarge,
                $"{entry.FullName} is larger than {maxBytes} bytes");
        }

        using var stream = entry.Open();
        using var memory = new MemoryStream((int)Math.Max(0, entry.Length));
        stream.CopyTo(memory);
        return memory.ToArray();
    }

    private static string? String(JsonElement root, string name)
        => root.ValueKind == JsonValueKind.Object &&
           root.TryGetProperty(name, out var element) &&
           element.ValueKind == JsonValueKind.String
            ? element.GetString()
            : null;

    private static PackException Invalid(string message)
        => new(PackErrorCode.Invalid, message);
}
