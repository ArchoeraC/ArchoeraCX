using System.Security.Cryptography;
using System.Text.Json;
using Archoera.Application.Loaders;
using Archoera.Domain.Loaders;

namespace Archoera.Infrastructure.Loaders;

/// <summary>
/// Variable-resolution half of <see cref="ForgeArtifactPlanner"/>: the
/// installer profile's data-variable graph ({ROOT}/{SIDE}/maven coordinates),
/// the tokens derived from it, and the artifacts those variables reference.
/// The manifest assembly half lives in ForgeArtifactPlanner.cs.
/// </summary>
internal sealed partial class PlanBuilder
{
    private void AddDataArtifacts()
    {
        // Only client processor inputs/outputs (and version-json libraries)
        // belong in a client manifest. A data variable may exist for the
        // server side but never be used by a client processor; MC_UNPACKED in
        // Forge 1.20.1 is one example (the producing processor is server-only).
        var usedTokens = new HashSet<string>(
            _processors.SelectMany(static processor => processor.Arguments),
            StringComparer.Ordinal);

        foreach (var key in _rawData.Keys.ToArray())
        {
            if (key.EndsWith("_SHA", StringComparison.Ordinal) ||
                key is "BINPATCH" or "MINECRAFT_JAR")
            {
                continue;
            }

            string token;
            try
            {
                token = ResolveData(key, 0);
            }
            catch (LoaderManifestException)
            {
                // A variable that cannot be resolved is a planner error elsewhere;
                // do not turn it into a second failure here.
                continue;
            }

            if (!token.StartsWith(LoaderPaths.LibrariesToken, StringComparison.Ordinal))
            {
                continue;
            }

            var (location, relative) = ArtifactTarget(token, $"data variable {key}");
            if (relative == ".")
            {
                continue;
            }

            var alreadyReferenced = _artifactIndex.ContainsKey(Identity(location, relative));
            if (!usedTokens.Contains(token) &&
                !alreadyReferenced &&
                !string.Equals(key, "PATCHED", StringComparison.Ordinal))
            {
                continue;
            }

            string? sha1 = null;
            if (_rawData.ContainsKey(key + "_SHA"))
            {
                try
                {
                    sha1 = NormalizeSha1Literal(ResolveData(key + "_SHA", 0));
                }
                catch (LoaderManifestException)
                {
                    sha1 = null;
                }
            }

            var producer = FindProducerIndex(token);
            AddArtifact(new LoaderArtifact(
                key,
                relative,
                sha1,
                Size: null,
                ProducerIndex: producer,
                RequiredForLaunch: string.Equals(key, "PATCHED", StringComparison.Ordinal),
                Kind: LoaderArtifactKind.Generated,
                Location: location));
        }
    }

    private static string? NormalizeSha1Literal(string raw)
    {
        var value = raw.Trim();
        if (value.Length >= 2 &&
            ((value[0] == '\'' && value[^1] == '\'') ||
             (value[0] == '"' && value[^1] == '"')))
        {
            value = value[1..^1].Trim();
        }

        return LoaderPaths.NormalizeSha1(value);
    }

    private int FindProducerIndex(string resolvedToken)
    {
        for (var index = 0; index < _processors.Count; index++)
        {
            var arguments = _processors[index].Arguments;
            for (var i = 0; i + 1 < arguments.Count; i++)
            {
                if ((string.Equals(arguments[i], "--output", StringComparison.Ordinal) ||
                     string.Equals(arguments[i], "-o", StringComparison.Ordinal)) &&
                    string.Equals(arguments[i + 1], resolvedToken, StringComparison.Ordinal))
                {
                    return index;
                }
            }
        }

        return -1;
    }

    private string ResolveArgument(string raw)
    {
        var text = raw.Replace("{SIDE}", "client", StringComparison.Ordinal);

        text = VariableRegex.Replace(text, match => ResolveVariable(match.Groups[1].Value));

        text = MavenRegex.Replace(text, match =>
        {
            if (!MavenPath.TryParse(match.Groups[1].Value, out var coordinate))
            {
                return match.Value;
            }

            return LoaderPaths.ToToken(
                LoaderPaths.Combine(_librariesFull, coordinate.RelativePath),
                _rootFull,
                _librariesFull,
                _installerFull);
        });

        if (LoaderPaths.IsAbsoluteLike(text))
        {
            text = LoaderPaths.ToToken(
                Path.GetFullPath(text),
                _rootFull,
                _librariesFull,
                _installerFull);
        }

        return text;
    }

    private string ResolveVariable(string name)
    {
        if (_rawData.ContainsKey(name))
        {
            return ResolveData(name, 0);
        }

        return name switch
        {
            "SIDE" => "client",
            "ROOT" => LoaderPaths.RootToken,
            "INSTALLER" => LoaderPaths.InstallerToken,
            "LIBRARY_DIR" => LoaderPaths.LibrariesToken,
            "MINECRAFT_JAR" => LoaderPaths.MinecraftJarToken,
            _ => throw new LoaderManifestException(
                "E_UNRESOLVED_VARIABLE",
                $"installer profile references unknown data variable '{{{name}}}'"),
        };
    }

    private string ResolveData(string key, int depth)
    {
        if (_resolvedData.TryGetValue(key, out var resolved))
        {
            return resolved;
        }

        if (depth > 8 || !_resolvingData.Add(key))
        {
            throw new LoaderManifestException(
                "E_DATA_CYCLE",
                $"installer data variable '{key}' is circular or too deeply nested");
        }

        try
        {
            if (!_rawData.TryGetValue(key, out var raw))
            {
                var builtin = ResolveVariable(key);
                _resolvedData[key] = builtin;
                return builtin;
            }

            string token;
            if (raw.StartsWith('[') && raw.EndsWith(']') &&
                MavenPath.TryParse(raw, out var coordinate))
            {
                token = LoaderPaths.ToToken(
                    LoaderPaths.Combine(_librariesFull, coordinate.RelativePath),
                    _rootFull,
                    _librariesFull,
                    _installerFull);
            }
            else if (raw.StartsWith('/'))
            {
                token = ResolveInstallerDataPath(key, raw);
            }
            else if (raw.Contains('{'))
            {
                token = VariableRegex.Replace(raw, match => ResolveVariable(match.Groups[1].Value));
            }
            else
            {
                token = raw;
            }

            _resolvedData[key] = token;
            return token;
        }
        finally
        {
            _resolvingData.Remove(key);
        }
    }

    private string ResolveInstallerDataPath(string key, string raw)
    {
        var entryName = raw.TrimStart('/').Replace('\\', '/');

        if (string.Equals(key, "BINPATCH", StringComparison.Ordinal) &&
            _binpatchRelative is not null)
        {
            AddInstallerEntryArtifact(
                entryName,
                LoaderArtifactLocation.Libraries,
                _binpatchRelative,
                "BINPATCH");

            return LoaderPaths.ToToken(
                LoaderPaths.Combine(_librariesFull, _binpatchRelative),
                _rootFull,
                _librariesFull,
                _installerFull);
        }

        var relative = LoaderPaths.NormalizeRelative(
            $".archoera/loader-data/{_installerSha1[..12]}/{entryName}",
            "installer data path");

        AddInstallerEntryArtifact(
            entryName,
            LoaderArtifactLocation.Root,
            relative,
            key);

        return LoaderPaths.ToToken(
            LoaderPaths.Combine(_rootFull, relative),
            _rootFull,
            _librariesFull,
            _installerFull);
    }

    private void AddInstallerEntryArtifact(
        string entryName,
        LoaderArtifactLocation location,
        string relative,
        string key)
    {
        var (sha1, size) = GetEntryDigest(entryName);
        AddArtifact(new LoaderArtifact(
            $"installer:{entryName}",
            relative,
            sha1,
            size,
            ProducerIndex: -1,
            RequiredForLaunch: false,
            Kind: LoaderArtifactKind.Extracted,
            Location: location));
    }

    private (LoaderArtifactLocation Location, string Relative) ArtifactTarget(string token, string what)
    {
        if (token.StartsWith(LoaderPaths.LibrariesToken, StringComparison.Ordinal))
        {
            var suffix = token.Length == LoaderPaths.LibrariesToken.Length
                ? "."
                : token[(LoaderPaths.LibrariesToken.Length + 1)..];
            return (LoaderArtifactLocation.Libraries,
                LoaderPaths.NormalizeRelative(suffix, what));
        }

        if (token.StartsWith(LoaderPaths.RootToken, StringComparison.Ordinal))
        {
            var suffix = token.Length == LoaderPaths.RootToken.Length
                ? "."
                : token[(LoaderPaths.RootToken.Length + 1)..];
            return (LoaderArtifactLocation.Root,
                LoaderPaths.NormalizeRelative(suffix, what));
        }

        if (LoaderPaths.IsAbsoluteLike(token))
        {
            throw new LoaderManifestException(
                "E_ABSOLUTE_PATH",
                $"{what} resolved to an absolute path outside the instance/libraries roots");
        }

        return (LoaderArtifactLocation.Libraries,
            LoaderPaths.NormalizeRelative(token, what));
    }
}
