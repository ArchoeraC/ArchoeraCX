using System.Text.Json;
using Archoera.Domain;
using Archoera.Domain.Downloads;

namespace Archoera.Application.Packs;

/// <summary>
/// Stable failure codes for Modrinth <c>.mrpack</c> installation. Provider and
/// download failures keep their existing codes and are mapped by the module
/// facade.
/// </summary>
public static class PackErrorCode
{
    public const string Invalid = "PACK_INVALID";
    public const string Unsupported = "PACK_UNSUPPORTED";
    public const string ArchiveInvalid = "PACK_ARCHIVE_INVALID";
    public const string PathEscape = "PACK_PATH_ESCAPE";
    public const string TooLarge = "PACK_TOO_LARGE";
    public const string DuplicateFile = "PACK_DUPLICATE_FILE";
    public const string LoaderUnsupported = "PACK_LOADER_UNSUPPORTED";
    public const string LoaderMetadata = "PACK_LOADER_METADATA_FAILED";
    public const string InstallerFailed = "PACK_INSTALLER_FAILED";
    public const string LoaderRepairFailed = "PACK_LOADER_REPAIR_FAILED";
    public const string DownloadIncomplete = "PACK_DOWNLOAD_INCOMPLETE";
    public const string InstanceFailed = "PACK_INSTANCE_FAILED";
}

/// <summary>Raised for every pack-format failure before/while applying a pack.</summary>
public sealed class PackException : Exception
{
    public PackException(string code, string message)
        : base(message)
    {
        Code = code;
    }

    public PackException(string code, string message, Exception inner)
        : base(message, inner)
    {
        Code = code;
    }

    /// <summary>Stable error code from <see cref="PackErrorCode"/>.</summary>
    public string Code { get; }
}

/// <summary>Loader declared by <c>modrinth.index.json</c> dependencies.</summary>
/// <param name="Kind">Lower-case loader id: vanilla, fabric, quilt, forge or neoforge.</param>
/// <param name="Version">Exact loader build; empty for vanilla.</param>
public sealed record ModpackLoader(string Kind, string Version)
{
    public bool IsVanilla => string.Equals(Kind, "vanilla", StringComparison.Ordinal);
}

/// <summary>One file entry from <c>modrinth.index.json</c>.</summary>
public sealed record ModpackFile(
    string Path,
    string? Sha1,
    string? Sha512,
    long Size,
    IReadOnlyList<string> Downloads,
    bool ClientUnsupported,
    bool ServerUnsupported)
{
    /// <summary>Preferred hash: SHA-512, then SHA-1. Never null after parsing.</summary>
    public DownloadHash Hash =>
        Sha512 is not null
            ? DownloadHash.Create(DownloadHashAlgorithm.Sha512, Sha512)
            : DownloadHash.Create(DownloadHashAlgorithm.Sha1, Sha1!);

    /// <summary>True when this payload applies to the requested side.</summary>
    public bool AppliesTo(bool clientSide)
        => clientSide ? !ClientUnsupported : !ServerUnsupported;
}

/// <summary>Parsed, normalized Modrinth pack index.</summary>
public sealed record ModpackIndex(
    int FormatVersion,
    string Game,
    string VersionId,
    string Name,
    string Summary,
    IReadOnlyList<ModpackFile> Files,
    IReadOnlyDictionary<string, string> Dependencies,
    ModpackLoader Loader)
{
    /// <summary>Exact Minecraft version from <c>dependencies.minecraft</c>.</summary>
    public string MinecraftVersion =>
        Dependencies.TryGetValue("minecraft", out var value) ? value : string.Empty;

    /// <summary>Sum of declared index file sizes.</summary>
    public long TotalBytes => Files.Sum(file => file.Size);
}

/// <summary>
/// Parses and validates <c>modrinth.index.json</c> with the same path rules as
/// the Rust executor. The archive itself is untrusted: absolute paths, parent
/// escapes, sidecar targets and writes into the <c>.archoera</c> metadata
/// directory are rejected before anything is downloaded.
/// </summary>
public static class ModpackIndexJson
{
    public const int SupportedFormatVersion = 1;
    public const int MaxIndexBytes = 8 * 1024 * 1024;
    public const int MaxFiles = 50_000;
    public const long MaxFileBytes = 1L << 40; // 1 TiB
    public const string IndexEntryName = "modrinth.index.json";

    private static readonly string[] LoaderDependencies =
        ["fabric-loader", "quilt-loader", "forge", "neoforge"];

    private static StringComparer PathComparer => Archoera.Domain.PathSemantics.Comparer;

    /// <summary>Parses UTF-8 index bytes. Throws <see cref="PackException"/>.</summary>
    public static ModpackIndex Parse(ReadOnlyMemory<byte> utf8)
    {
        if (utf8.Length > MaxIndexBytes)
        {
            throw new PackException(
                PackErrorCode.TooLarge,
                $"{IndexEntryName} is larger than {MaxIndexBytes} bytes");
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(utf8);
        }
        catch (JsonException exception)
        {
            throw new PackException(
                PackErrorCode.Invalid,
                $"{IndexEntryName} is not valid JSON: {exception.Message}",
                exception);
        }

        using (document)
        {
            return ParseRoot(document.RootElement);
        }
    }

    private static ModpackIndex ParseRoot(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw Invalid("index root must be an object");
        }

        var formatVersion = ReadInt(root, "formatVersion")
            ?? throw Invalid("formatVersion is required");
        if (formatVersion != SupportedFormatVersion)
        {
            throw new PackException(
                PackErrorCode.Unsupported,
                $"unsupported pack formatVersion {formatVersion} " +
                $"(expected {SupportedFormatVersion})");
        }

        var game = ReadString(root, "game") ?? string.Empty;
        if (!string.Equals(game, "minecraft", StringComparison.OrdinalIgnoreCase))
        {
            throw new PackException(
                PackErrorCode.Unsupported,
                $"unsupported pack game '{game}' (expected minecraft)");
        }

        var versionId = ReadString(root, "versionId") ?? string.Empty;
        var name = ReadString(root, "name") ?? string.Empty;
        if (string.IsNullOrWhiteSpace(name))
        {
            throw Invalid("name is required");
        }
        if (name.Length > 256 || name.Any(char.IsControl))
        {
            throw Invalid("name must be at most 256 characters without control characters");
        }
        if (string.IsNullOrWhiteSpace(versionId))
        {
            throw Invalid("versionId is required");
        }
        if (versionId.Length > 256 || versionId.Any(char.IsControl))
        {
            throw Invalid(
                "versionId must be at most 256 characters without control characters");
        }

        var dependencies = ParseDependencies(root);
        if (!dependencies.TryGetValue("minecraft", out var minecraft) ||
            string.IsNullOrWhiteSpace(minecraft))
        {
            throw Invalid("dependencies.minecraft is required");
        }

        var loader = ParseLoader(dependencies);
        var files = ParseFiles(root);

        return new ModpackIndex(
            formatVersion,
            game,
            versionId.Trim(),
            name.Trim(),
            ReadString(root, "summary")?.Trim() ?? string.Empty,
            files,
            dependencies,
            loader);
    }

    private static IReadOnlyList<ModpackFile> ParseFiles(JsonElement root)
    {
        if (!root.TryGetProperty("files", out var filesElement) ||
            filesElement.ValueKind != JsonValueKind.Array)
        {
            throw Invalid("files must be an array");
        }

        if (filesElement.GetArrayLength() > MaxFiles)
        {
            throw new PackException(
                PackErrorCode.TooLarge,
                $"pack declares more than {MaxFiles} files");
        }

        var seen = new HashSet<string>(PathComparer);
        var files = new List<ModpackFile>(filesElement.GetArrayLength());
        foreach (var item in filesElement.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                throw Invalid("each files[] entry must be an object");
            }

            var rawPath = ReadString(item, "path")
                ?? throw Invalid("files[].path is required");
            var path = NormalizeIndexPath(rawPath);
            if (!seen.Add(path))
            {
                throw new PackException(
                    PackErrorCode.DuplicateFile,
                    $"duplicate pack path '{path}'");
            }

            var size = ReadLong(item, "fileSize")
                ?? throw Invalid($"{path}: fileSize is required");
            if (size < 0 || size > MaxFileBytes)
            {
                throw new PackException(
                    PackErrorCode.TooLarge,
                    $"{path}: fileSize {size} is outside 0..{MaxFileBytes}");
            }

            var (sha1, sha512) = ReadHashes(item, path);
            var downloads = ReadDownloads(item, path);
            var (clientUnsupported, serverUnsupported) = ReadEnvironment(item, path);

            files.Add(new ModpackFile(
                path,
                sha1,
                sha512,
                size,
                downloads,
                clientUnsupported,
                serverUnsupported));
        }

        return files;
    }

    private static (string? Sha1, string? Sha512) ReadHashes(
        JsonElement item,
        string path)
    {
        if (!item.TryGetProperty("hashes", out var hashes) ||
            hashes.ValueKind != JsonValueKind.Object)
        {
            throw Invalid($"{path}: hashes object is required");
        }

        var sha1 = ReadCanonicalHex(hashes, "sha1", 40, path);
        var sha512 = ReadCanonicalHex(hashes, "sha512", 128, path);
        if (sha1 is null && sha512 is null)
        {
            throw Invalid($"{path}: sha1 or sha512 hash is required");
        }

        return (sha1, sha512);
    }

    private static string? ReadCanonicalHex(
        JsonElement hashes,
        string name,
        int length,
        string path)
    {
        if (!hashes.TryGetProperty(name, out var element) ||
            element.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (element.ValueKind != JsonValueKind.String)
        {
            throw Invalid($"{path}: hashes.{name} must be a string");
        }

        var value = element.GetString()?.Trim().ToLowerInvariant() ?? string.Empty;
        if (value.Length != length || !value.All(IsLowerHex))
        {
            throw Invalid($"{path}: hashes.{name} must be {length} hex characters");
        }

        return value;
    }

    private static IReadOnlyList<string> ReadDownloads(JsonElement item, string path)
    {
        if (!item.TryGetProperty("downloads", out var downloads) ||
            downloads.ValueKind != JsonValueKind.Array)
        {
            throw Invalid($"{path}: downloads must be an array");
        }

        var result = new List<string>(downloads.GetArrayLength());
        foreach (var entry in downloads.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.String)
            {
                throw Invalid($"{path}: downloads[] entries must be strings");
            }

            var raw = entry.GetString();
            var issue = DownloadPlanPaths.TryValidateUrl(raw, out var uri);
            if (issue is not null)
            {
                throw new PackException(
                    PackErrorCode.Invalid,
                    $"{path}: {issue.Message}");
            }

            result.Add(uri!.AbsoluteUri);
        }

        if (result.Count == 0)
        {
            throw new PackException(
                PackErrorCode.Invalid,
                $"{path}: at least one download URL is required");
        }

        return result;
    }

    private static (bool Client, bool Server) ReadEnvironment(
        JsonElement item,
        string path)
    {
        if (!item.TryGetProperty("env", out var env) ||
            env.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return (false, false);
        }

        if (env.ValueKind != JsonValueKind.Object)
        {
            throw Invalid($"{path}: env must be an object or null");
        }

        return (
            ReadSupport(env, "client", path) == "unsupported",
            ReadSupport(env, "server", path) == "unsupported");
    }

    private static string ReadSupport(JsonElement env, string name, string path)
    {
        if (!env.TryGetProperty(name, out var element) ||
            element.ValueKind == JsonValueKind.Null)
        {
            return "required";
        }

        if (element.ValueKind != JsonValueKind.String)
        {
            throw Invalid($"{path}: env.{name} must be a string");
        }

        var value = element.GetString()?.Trim().ToLowerInvariant() ?? string.Empty;
        if (value is not ("required" or "optional" or "unsupported"))
        {
            throw Invalid(
                $"{path}: env.{name} must be required, optional or unsupported");
        }

        return value;
    }

    private static IReadOnlyDictionary<string, string> ParseDependencies(JsonElement root)
    {
        if (!root.TryGetProperty("dependencies", out var dependencies) ||
            dependencies.ValueKind != JsonValueKind.Object)
        {
            throw Invalid("dependencies must be an object");
        }

        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in dependencies.EnumerateObject())
        {
            if (property.Value.ValueKind != JsonValueKind.String)
            {
                throw Invalid($"dependencies.{property.Name} must be a string");
            }

            var value = property.Value.GetString()?.Trim() ?? string.Empty;
            if (value.Length == 0)
            {
                throw Invalid($"dependencies.{property.Name} must not be empty");
            }

            result[property.Name] = value;
        }

        return result;
    }

    private static ModpackLoader ParseLoader(IReadOnlyDictionary<string, string> dependencies)
    {
        var found = LoaderDependencies
            .Where(name => dependencies.ContainsKey(name))
            .ToArray();

        if (found.Length > 1)
        {
            throw new PackException(
                PackErrorCode.Unsupported,
                "pack declares multiple loaders: " + string.Join(", ", found));
        }

        if (found.Length == 0)
        {
            return new ModpackLoader("vanilla", string.Empty);
        }

        var dependency = found[0];
        var kind = dependency switch
        {
            "fabric-loader" => "fabric",
            "quilt-loader" => "quilt",
            _ => dependency,
        };

        return new ModpackLoader(kind, dependencies[dependency]);
    }

    private static string NormalizeIndexPath(string raw)
    {
        var issue = DownloadPlanPaths.TryNormalizeDest(raw, out var normalized);
        if (issue is not null)
        {
            throw new PackException(
                issue.Code == DownloadPlanErrorCode.PathEscape
                    ? PackErrorCode.PathEscape
                    : PackErrorCode.Invalid,
                $"unsafe pack path '{raw}': {issue.Message}");
        }

        RejectReservedTarget(normalized);
        return normalized;
    }

    private static readonly string[] ReservedRoots =
    [
        ".archoera", "versions", "libraries", "assets", "natives", "logging",
        // The launcher keeps verified loader installers and other download
        // artifacts here. A pack must not be able to plant a file that the
        // repair/launch preflight later parses and executes as a Forge
        // installer (see PackForgeInstallerBootstrap).
        "downloads",
    ];

    /// <summary>
    /// Block writes into launcher/game-managed roots. Packs own the instance's
    /// mods/config/resource packs, not its version JSON, libraries, assets or
    /// launcher metadata.
    /// </summary>
    public static void RejectReservedTarget(string normalizedPath)
    {
        var first = normalizedPath.Split('/', 2)[0];
        if (ReservedRoots.Contains(first, StringComparer.OrdinalIgnoreCase))
        {
            throw new PackException(
                PackErrorCode.PathEscape,
                $"pack path '{normalizedPath}' targets the reserved '{first}' root");
        }
    }

    private static bool IsLowerHex(char value)
        => value is (>= '0' and <= '9') or (>= 'a' and <= 'f');

    private static string? ReadString(JsonElement root, string name)
        => root.TryGetProperty(name, out var element) &&
           element.ValueKind == JsonValueKind.String
            ? element.GetString()
            : null;

    private static int? ReadInt(JsonElement root, string name)
        => root.TryGetProperty(name, out var element) &&
           element.ValueKind == JsonValueKind.Number &&
           element.TryGetInt32(out var value)
            ? value
            : null;

    private static long? ReadLong(JsonElement root, string name)
        => root.TryGetProperty(name, out var element) &&
           element.ValueKind == JsonValueKind.Number &&
           element.TryGetInt64(out var value)
            ? value
            : null;

    private static PackException Invalid(string message)
        => new(PackErrorCode.Invalid, message);
}
