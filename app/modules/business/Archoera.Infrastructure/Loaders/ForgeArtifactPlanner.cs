using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Archoera.Application.Loaders;
using Archoera.Domain.Loaders;

namespace Archoera.Infrastructure.Loaders;

/// <summary>
/// Builds a relocatable Forge/NeoForge manifest from an installer zip.
/// This is a pure parser: no Java, no network and no writes.
/// </summary>
public sealed class ForgeArtifactPlanner : IForgeArtifactPlanner
{
    public async Task<LoaderArtifactManifest> ParseAsync(
        string installerJar,
        string gameDir,
        string librariesDir,
        LoaderKind kind,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(installerJar);
        ArgumentException.ThrowIfNullOrWhiteSpace(gameDir);
        ArgumentException.ThrowIfNullOrWhiteSpace(librariesDir);
        cancellationToken.ThrowIfCancellationRequested();

        var installerFull = Path.GetFullPath(installerJar);
        var rootFull = Path.GetFullPath(gameDir);
        var librariesFull = Path.GetFullPath(librariesDir);

        if (!File.Exists(installerFull))
        {
            throw new LoaderManifestException(
                "E_INSTALLER_MISSING",
                $"installer jar not found: {installerFull}");
        }

        if (!LoaderPaths.TryRelativeWithin(rootFull, librariesFull, out var librariesRelative))
        {
            throw new LoaderManifestException(
                "E_LIBRARIES_OUTSIDE_ROOT",
                "the libraries directory must be inside the instance root so manifest paths stay relative");
        }

        var installerSha1 = await LoaderPaths.Sha1FileAsync(installerFull, cancellationToken);

        ZipArchive archive;
        try
        {
            archive = ZipFile.OpenRead(installerFull);
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException)
        {
            throw new LoaderManifestException(
                "E_INSTALLER_INVALID",
                $"installer is not a readable zip/jar: {exception.Message}",
                exception);
        }

        try
        {
            using var profileDocument = ReadJsonEntry(archive, "install_profile.json");
            using var versionDocument = ReadJsonEntry(archive, "version.json");

            var spec = ReadInt(profileDocument.RootElement, "spec", 0);
            if (spec != 1)
            {
                throw new LoaderManifestException(
                    "E_UNSUPPORTED_SPEC",
                    $"install_profile.json spec {spec} is not supported by the processor planner (need spec 1)");
            }

            var builder = new PlanBuilder(
                archive,
                installerFull,
                rootFull,
                librariesFull,
                librariesRelative,
                installerSha1,
                kind);

            return builder.Build(profileDocument.RootElement, versionDocument.RootElement);
        }
        finally
        {
            archive.Dispose();
        }
    }

    private static JsonDocument ReadJsonEntry(ZipArchive archive, string name)
    {
        var entry = FindEntry(archive, name)
                    ?? throw new LoaderManifestException(
                        "E_INSTALLER_ENTRY_MISSING",
                        $"installer does not contain {name}");

        using var stream = entry.Open();
        using var memory = new MemoryStream(checked((int)Math.Max(0, entry.Length)));
        stream.CopyTo(memory);
        var bytes = memory.ToArray();

        var offset = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF
            ? 3
            : 0;

        try
        {
            return JsonDocument.Parse(new ReadOnlyMemory<byte>(bytes, offset, bytes.Length - offset));
        }
        catch (JsonException exception)
        {
            throw new LoaderManifestException(
                "E_INVALID_JSON",
                $"{name} is not valid JSON: {exception.Message}",
                exception);
        }
    }

    internal static ZipArchiveEntry? FindEntry(ZipArchive archive, string name)
    {
        foreach (var entry in archive.Entries)
        {
            if (string.Equals(entry.FullName, name, StringComparison.OrdinalIgnoreCase))
            {
                return entry;
            }
        }

        return null;
    }

    private static int ReadInt(JsonElement element, string name, int fallback)
        => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number &&
           value.TryGetInt32(out var number)
            ? number
            : fallback;
}

internal sealed partial class PlanBuilder
{
    private static readonly Regex VariableRegex = new(
        @"\{([A-Za-z0-9_]+)\}",
        RegexOptions.CultureInvariant);

    private static readonly Regex MavenRegex = new(
        @"\[([^\[\]\s]+)\]",
        RegexOptions.CultureInvariant);

    private readonly ZipArchive _archive;
    private readonly string _installerFull;
    private readonly string _rootFull;
    private readonly string _librariesFull;
    private readonly string _librariesRelative;
    private readonly string _installerSha1;
    private readonly LoaderKind _kind;
    private readonly string _installerPath;
    private readonly bool _installerOutsideRoot;

    private readonly List<LoaderArtifact> _artifacts = [];
    private readonly Dictionary<string, int> _artifactIndex = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (string? Sha1, long? Size)> _entryDigests = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _rawData = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _resolvedData = new(StringComparer.Ordinal);
    private readonly HashSet<string> _resolvingData = new(StringComparer.Ordinal);
    private readonly List<ProcessorStep> _processors = [];

    private string _gameVersion = string.Empty;
    private string _loaderVersion = string.Empty;
    private string _versionId = string.Empty;
    private string? _binpatchRelative;

    public PlanBuilder(
        ZipArchive archive,
        string installerFull,
        string rootFull,
        string librariesFull,
        string librariesRelative,
        string installerSha1,
        LoaderKind kind)
    {
        _archive = archive;
        _installerFull = installerFull;
        _rootFull = rootFull;
        _librariesFull = librariesFull;
        _librariesRelative = librariesRelative;
        _installerSha1 = installerSha1;
        _kind = kind;

        _installerOutsideRoot = !LoaderPaths.TryRelativeWithin(rootFull, installerFull, out var installerRelative) ||
                                installerRelative == ".";
        _installerPath = _installerOutsideRoot
            ? Path.GetFileName(installerFull)
            : installerRelative;

        if (string.IsNullOrWhiteSpace(_installerPath))
        {
            _installerPath = "installer.jar";
        }
    }

    public LoaderArtifactManifest Build(JsonElement profile, JsonElement versionJson)
    {
        ParseMetadata(profile, versionJson);
        CollectRawData(profile);
        ResolveBinpatchCoordinate(profile, versionJson);

        AddEmbeddedMavenArtifacts();

        // Resolving data early creates the client.lzma (BINPATCH) extraction
        // artifact and validates every variable used by processors.
        foreach (var key in _rawData.Keys.ToArray())
        {
            _ = ResolveData(key, 0);
        }

        BuildProcessors(profile);
        AddVersionLibraries(versionJson);

        // `PATCHED` is not always referenced by version.json.libraries and is
        // not always declared in processors[].outputs (notably Forge 1.20.1 and
        // NeoForge 21.1). Record the data-variable artifacts explicitly so the
        // launch preflight can verify them.
        AddDataArtifacts();

        ValidateManifest();

        var finalByIdentity = _artifacts.ToDictionary(
            static artifact => Identity(artifact.Location, artifact.RelativePath),
            StringComparer.Ordinal);

        var processors = _processors
            .Select(step => step with
            {
                Outputs = step.Outputs
                    .Select(output => finalByIdentity[Identity(output.Location, output.RelativePath)])
                    .ToArray(),
            })
            .ToArray();

        return new LoaderArtifactManifest
        {
            SchemaVersion = LoaderArtifactManifest.CurrentSchemaVersion,
            LoaderId = new LoaderId(_kind, _gameVersion, _loaderVersion, _installerSha1),
            InstallerPath = _installerPath,
            InstallerOutsideRoot = _installerOutsideRoot,
            VersionJsonPath = $"versions/{_versionId}/{_versionId}.json",
            VersionId = _versionId,
            LibrariesPath = _librariesRelative,
            Artifacts = _artifacts.ToArray(),
            ProcessorCount = processors.Length,
            Processors = processors,
            CreatedAt = DateTimeOffset.UtcNow,
        };
    }

    private void ParseMetadata(JsonElement profile, JsonElement versionJson)
    {
        var profileVersion = ReadString(profile, "version");
        var profileMinecraft = ReadString(profile, "minecraft");
        var versionId = ReadString(versionJson, "id");
        var inheritsFrom = ReadString(versionJson, "inheritsFrom");

        _gameVersion = FirstNonEmpty(profileMinecraft, inheritsFrom, versionId)
                       ?? throw new LoaderManifestException(
                           "E_GAME_VERSION_MISSING",
                           "installer profile does not declare a Minecraft version");

        _versionId = FirstNonEmpty(versionId, profileVersion)
                     ?? $"{_gameVersion}-{_kind.ToString().ToLowerInvariant()}";

        ValidateVersionId(_versionId, "version id");

        _loaderVersion = NormalizeLoaderVersion(
            FirstNonEmpty(profileVersion, versionId) ?? string.Empty,
            _gameVersion);

        if (string.IsNullOrWhiteSpace(_loaderVersion))
        {
            _loaderVersion = _versionId;
        }
    }

    private static string NormalizeLoaderVersion(string raw, string gameVersion)
    {
        var prefixes = new[]
        {
            $"{gameVersion}-forge-",
            $"{gameVersion}-neoforge-",
            "forge-",
            "neoforge-",
            $"{gameVersion}-",
        };

        foreach (var prefix in prefixes)
        {
            if (raw.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return raw[prefix.Length..];
            }
        }

        return raw;
    }

    private static void ValidateVersionId(string value, string what)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            value is "." or ".." ||
            value.Contains('/') ||
            value.Contains('\\') ||
            value.Any(char.IsControl) ||
            value.IndexOfAny([':', '*', '?', '"', '<', '>', '|']) >= 0)
        {
            throw new LoaderManifestException(
                "E_VERSION_ID",
                $"{what} '{value}' cannot be used as a directory name");
        }
    }

    private void CollectRawData(JsonElement profile)
    {
        if (!profile.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        foreach (var property in data.EnumerateObject())
        {
            if (property.Value.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var value = ReadString(property.Value, "client");
            value ??= ReadString(property.Value, "server");
            if (!string.IsNullOrWhiteSpace(value))
            {
                _rawData[property.Name] = value;
            }
        }
    }

    private void ResolveBinpatchCoordinate(JsonElement profile, JsonElement versionJson)
    {
        var binpatchRaw = _rawData.TryGetValue("BINPATCH", out var raw) ? raw : null;

        if (binpatchRaw is not null && MavenPath.TryParse(binpatchRaw, out var direct))
        {
            var coordinate = direct.Classifier is null && direct.Extension == "jar"
                ? direct.WithClassifier("clientdata", "lzma")
                : direct;
            _binpatchRelative = coordinate.RelativePath;
            return;
        }

        MavenCoordinate? patched = null;
        if (_rawData.TryGetValue("PATCHED", out var patchedRaw))
        {
            MavenPath.TryParse(patchedRaw, out patched);
        }

        patched ??= FindClientArtifactCoordinate(versionJson, profile);
        if (patched is not null)
        {
            _binpatchRelative = patched.WithClassifier("clientdata", "lzma").RelativePath;
            return;
        }

        var group = _kind == LoaderKind.Forge ? "net.minecraftforge" : "net.neoforged";
        var artifact = _kind == LoaderKind.Forge
            ? "forge"
            : _gameVersion == "1.20.1" ? "forge" : "neoforge";
        var coordinateVersion = _kind == LoaderKind.Forge
            ? $"{_gameVersion}-{_loaderVersion}"
            : _gameVersion == "1.20.1" &&
              !_loaderVersion.StartsWith(_gameVersion + "-", StringComparison.Ordinal)
                ? $"{_gameVersion}-{_loaderVersion}"
                : _loaderVersion;

        var fallback = new MavenCoordinate(
            group,
            artifact,
            coordinateVersion,
            "clientdata",
            "lzma");
        _binpatchRelative = fallback.RelativePath;
    }

    private static MavenCoordinate? FindClientArtifactCoordinate(JsonElement versionJson, JsonElement profile)
    {
        if (versionJson.TryGetProperty("libraries", out var libraries) &&
            libraries.ValueKind == JsonValueKind.Array)
        {
            foreach (var library in libraries.EnumerateArray())
            {
                var name = ReadString(library, "name");
                if (name is null || !MavenPath.TryParse(name, out var coordinate))
                {
                    continue;
                }

                if (!string.Equals(coordinate.Classifier, "client", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (coordinate.Group is "net.minecraftforge" or "net.neoforged" ||
                    coordinate.Group.StartsWith("net.neoforged.", StringComparison.Ordinal))
                {
                    return coordinate;
                }
            }
        }

        foreach (var key in new[] { "path", "profile" })
        {
            var value = ReadString(profile, key);
            if (value is not null &&
                MavenPath.TryParse(value, out var coordinate) &&
                coordinate.Group is "net.minecraftforge" or "net.neoforged")
            {
                return coordinate;
            }
        }

        return null;
    }

    private void AddEmbeddedMavenArtifacts()
    {
        foreach (var entry in _archive.Entries)
        {
            if (entry.FullName.Length == 0 ||
                entry.FullName.EndsWith('/') ||
                !entry.FullName.StartsWith("maven/", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var relative = entry.FullName["maven/".Length..];
            if (!LoaderPaths.TryNormalizeRelative(relative, out var normalized, out _))
            {
                continue;
            }

            var (sha1, size) = GetEntryDigest(entry.FullName);
            AddArtifact(new LoaderArtifact(
                $"installer:{entry.FullName}",
                normalized,
                sha1,
                size,
                ProducerIndex: -1,
                RequiredForLaunch: false,
                Kind: LoaderArtifactKind.Extracted,
                Location: LoaderArtifactLocation.Libraries));
        }
    }

    private void BuildProcessors(JsonElement profile)
    {
        if (!profile.TryGetProperty("processors", out var processors) ||
            processors.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        var index = 0;
        foreach (var processor in processors.EnumerateArray())
        {
            if (!RunsOnClient(processor))
            {
                continue;
            }

            var jarRaw = ReadString(processor, "jar")
                         ?? throw new LoaderManifestException(
                             "E_PROCESSOR_INVALID",
                             "processor is missing a jar coordinate");

            var jarRelative = ResolveLibraryReference(jarRaw, "processor jar");

            var classPath = new List<string>();
            if (processor.TryGetProperty("classpath", out var classpathElement) &&
                classpathElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var entry in classpathElement.EnumerateArray())
                {
                    if (entry.ValueKind != JsonValueKind.String)
                    {
                        throw new LoaderManifestException(
                            "E_PROCESSOR_INVALID",
                            "processor classpath contains a non-string entry");
                    }

                    classPath.Add(ResolveLibraryReference(entry.GetString()!, "processor classpath"));
                }
            }

            var arguments = new List<string>();
            if (processor.TryGetProperty("args", out var argsElement))
            {
                if (argsElement.ValueKind != JsonValueKind.Array)
                {
                    throw new LoaderManifestException(
                        "E_PROCESSOR_INVALID",
                        "processor args is not an array");
                }

                foreach (var argument in argsElement.EnumerateArray())
                {
                    if (argument.ValueKind != JsonValueKind.String)
                    {
                        throw new LoaderManifestException(
                            "E_PROCESSOR_INVALID",
                            "processor args contains a non-string entry");
                    }

                    arguments.Add(ResolveArgument(argument.GetString()!));
                }
            }

            var outputs = new List<LoaderArtifact>();
            if (processor.TryGetProperty("outputs", out var outputsElement) &&
                outputsElement.ValueKind == JsonValueKind.Object)
            {
                foreach (var output in outputsElement.EnumerateObject())
                {
                    var pathToken = ResolveArgument(output.Name);
                    var (location, relative) = ArtifactTarget(pathToken, "processor output");

                    string? sha1 = null;
                    if (output.Value.ValueKind == JsonValueKind.String)
                    {
                        sha1 = LoaderPaths.NormalizeSha1(ResolveArgument(output.Value.GetString()!));
                    }

                    var key = VariableRegex.IsMatch(output.Name) &&
                              VariableRegex.Match(output.Name).Value == output.Name
                        ? output.Name[1..^1]
                        : relative;

                    outputs.Add(AddArtifact(new LoaderArtifact(
                        key,
                        relative,
                        sha1,
                        Size: null,
                        ProducerIndex: index,
                        RequiredForLaunch: false,
                        Kind: LoaderArtifactKind.Generated,
                        Location: location)));
                }
            }

            _processors.Add(new ProcessorStep(
                index,
                jarRelative,
                classPath,
                ReadMainClass(jarRelative),
                arguments,
                outputs));

            index++;
        }
    }

    private static bool RunsOnClient(JsonElement processor)
    {
        if (!processor.TryGetProperty("sides", out var sides) ||
            sides.ValueKind != JsonValueKind.Array ||
            sides.GetArrayLength() == 0)
        {
            return true;
        }

        foreach (var side in sides.EnumerateArray())
        {
            if (side.ValueKind == JsonValueKind.String &&
                string.Equals(side.GetString(), "client", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private void AddVersionLibraries(JsonElement versionJson)
    {
        if (!versionJson.TryGetProperty("libraries", out var libraries) ||
            libraries.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (var library in libraries.EnumerateArray())
        {
            if (!library.TryGetProperty("downloads", out var downloads) ||
                downloads.ValueKind != JsonValueKind.Object ||
                !downloads.TryGetProperty("artifact", out var artifact) ||
                artifact.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var url = ReadString(artifact, "url");
            if (!string.IsNullOrWhiteSpace(url))
            {
                // Downloadable library: the download planner owns it.
                continue;
            }

            var name = ReadString(library, "name");
            var path = ReadString(artifact, "path");
            if (string.IsNullOrWhiteSpace(path))
            {
                if (name is null || !MavenPath.TryParse(name, out var coordinate))
                {
                    continue;
                }

                path = coordinate.RelativePath;
            }

            if (!LoaderPaths.TryNormalizeRelative(path, out var relative, out var error))
            {
                throw new LoaderManifestException(
                    "E_UNSAFE_PATH",
                    $"version library path '{path}': {error}");
            }

            var sha1 = LoaderPaths.NormalizeSha1(ReadString(artifact, "sha1"));
            long? size = null;
            if (artifact.TryGetProperty("size", out var sizeElement) &&
                sizeElement.ValueKind == JsonValueKind.Number &&
                sizeElement.TryGetInt64(out var sizeValue))
            {
                size = sizeValue;
            }

            AddArtifact(new LoaderArtifact(
                name ?? $"version-library:{relative}",
                relative,
                sha1,
                size,
                ProducerIndex: -1,
                RequiredForLaunch: true,
                Kind: LoaderArtifactKind.Generated,
                Location: LoaderArtifactLocation.Libraries));
        }
    }

    private (string? Sha1, long? Size) GetEntryDigest(string entryName)
    {
        if (_entryDigests.TryGetValue(entryName, out var cached))
        {
            return cached;
        }

        var entry = ForgeArtifactPlanner.FindEntry(_archive, entryName);
        if (entry is null)
        {
            var missing = ((string?)null, (long?)null);
            _entryDigests[entryName] = missing;
            return missing;
        }

        using var stream = entry.Open();
        var hash = SHA1.HashData(stream);
        var digest = (LoaderPaths.Hex(hash), (long?)entry.Length);
        _entryDigests[entryName] = digest;
        return digest;
    }

    private string ResolveLibraryReference(string raw, string what)
    {
        if (MavenPath.TryParse(raw, out var coordinate))
        {
            return coordinate.RelativePath;
        }

        if (LoaderPaths.IsAbsoluteLike(raw))
        {
            throw new LoaderManifestException(
                "E_ABSOLUTE_PATH",
                $"{what} must be a maven coordinate or a libraries-relative path");
        }

        return LoaderPaths.NormalizeRelative(raw, what);
    }

    private string ReadMainClass(string jarRelative)
    {
        var jarPath = LoaderPaths.Combine(_librariesFull, jarRelative);
        try
        {
            if (File.Exists(jarPath))
            {
                using var jar = ZipFile.OpenRead(jarPath);
                var main = ReadMainClassFromArchive(jar);
                if (!string.IsNullOrWhiteSpace(main))
                {
                    return main;
                }
            }

            var embedded = ForgeArtifactPlanner.FindEntry(_archive, $"maven/{jarRelative}");
            if (embedded is not null)
            {
                using var stream = embedded.Open();
                using var memory = new MemoryStream();
                stream.CopyTo(memory);
                memory.Position = 0;
                using var jar = new ZipArchive(memory, ZipArchiveMode.Read, leaveOpen: false);
                return ReadMainClassFromArchive(jar);
            }
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            // A missing/corrupt processor jar is a repair problem, not a parse
            // failure: the manifest can still be verified without Java.
        }

        return string.Empty;
    }

    private static string ReadMainClassFromArchive(ZipArchive jar)
    {
        var entry = ForgeArtifactPlanner.FindEntry(jar, "META-INF/MANIFEST.MF");
        if (entry is null)
        {
            return string.Empty;
        }

        using var stream = entry.Open();
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        string? mainClass = null;
        string? currentKey = null;
        var value = new StringBuilder();

        void Flush()
        {
            if (currentKey is not null &&
                currentKey.Equals("Main-Class", StringComparison.OrdinalIgnoreCase))
            {
                mainClass = value.ToString().Trim();
            }

            value.Clear();
        }

        while (reader.ReadLine() is { } line)
        {
            if (line.StartsWith(' ') && currentKey is not null)
            {
                value.Append(line[1..]);
                continue;
            }

            Flush();

            var separator = line.IndexOf(':');
            if (separator <= 0)
            {
                currentKey = null;
                continue;
            }

            currentKey = line[..separator].Trim();
            value.Append(line[(separator + 1)..].Trim());
        }

        Flush();
        return mainClass ?? string.Empty;
    }

    private LoaderArtifact AddArtifact(LoaderArtifact artifact)
    {
        var identity = Identity(artifact.Location, artifact.RelativePath);
        if (_artifactIndex.TryGetValue(identity, out var index))
        {
            var merged = Merge(_artifacts[index], artifact);
            _artifacts[index] = merged;
            return merged;
        }

        _artifactIndex[identity] = _artifacts.Count;
        _artifacts.Add(artifact);
        return artifact;
    }

    private static LoaderArtifact Merge(LoaderArtifact existing, LoaderArtifact incoming)
    {
        string? sha1 = existing.Sha1 ?? incoming.Sha1;
        if (existing.Sha1 is not null && incoming.Sha1 is not null &&
            !string.Equals(existing.Sha1, incoming.Sha1, StringComparison.OrdinalIgnoreCase))
        {
            throw new LoaderManifestException(
                "E_SHA1_CONFLICT",
                $"two installer sources disagree on the SHA-1 of '{existing.RelativePath}'");
        }

        long? size = existing.Size ?? incoming.Size;
        if (existing.Size is not null && incoming.Size is not null && existing.Size != incoming.Size)
        {
            throw new LoaderManifestException(
                "E_SIZE_CONFLICT",
                $"two installer sources disagree on the size of '{existing.RelativePath}'");
        }

        var producer = incoming.ProducerIndex >= 0 ? incoming.ProducerIndex : existing.ProducerIndex;
        var key = existing.Key.StartsWith("installer:", StringComparison.Ordinal) &&
                  !incoming.Key.StartsWith("installer:", StringComparison.Ordinal)
            ? incoming.Key
            : existing.Key;

        return existing with
        {
            Key = key,
            Sha1 = sha1,
            Size = size,
            ProducerIndex = producer,
            RequiredForLaunch = existing.RequiredForLaunch || incoming.RequiredForLaunch,
            Kind = existing.Kind == LoaderArtifactKind.Generated || incoming.Kind == LoaderArtifactKind.Generated
                ? LoaderArtifactKind.Generated
                : LoaderArtifactKind.Extracted,
        };
    }

    private void ValidateManifest()
    {
        LoaderPaths.NormalizeRelative(_installerPath, "installer path");
        LoaderPaths.NormalizeRelative($"versions/{_versionId}/{_versionId}.json", "version json path");
        LoaderPaths.NormalizeRelative(_librariesRelative, "libraries path");

        foreach (var artifact in _artifacts)
        {
            var normalized = LoaderPaths.NormalizeRelative(artifact.RelativePath, "artifact path");
            if (!string.Equals(normalized, artifact.RelativePath, StringComparison.Ordinal))
            {
                throw new LoaderManifestException(
                    "E_UNSAFE_PATH",
                    $"artifact path '{artifact.RelativePath}' is not normalized");
            }
        }

        foreach (var processor in _processors)
        {
            LoaderPaths.NormalizeRelative(processor.JarRelativePath, "processor jar path");
            foreach (var entry in processor.ClassPath)
            {
                LoaderPaths.NormalizeRelative(entry, "processor classpath");
            }

            foreach (var argument in processor.Arguments)
            {
                if (LoaderPaths.IsAbsoluteLike(argument))
                {
                    throw new LoaderManifestException(
                        "E_ABSOLUTE_PATH",
                        $"processor {processor.Index} argument is an absolute path; manifest must stay relocatable");
                }
            }
        }
    }

    private static string Identity(LoaderArtifactLocation location, string relative)
        => $"{(location == LoaderArtifactLocation.Libraries ? "lib" : "root")}|{relative}";

    private static string? ReadString(JsonElement element, string name)
        => element.ValueKind == JsonValueKind.Object &&
           element.TryGetProperty(name, out var value) &&
           value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static string? FirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return null;
    }
}
