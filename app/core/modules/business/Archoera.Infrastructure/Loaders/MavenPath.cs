using Archoera.Application.Loaders;

namespace Archoera.Infrastructure.Loaders;

/// <summary>
/// Minimal Maven coordinate parser used for install-profile libraries,
/// processor jars and data variables. It deliberately supports only the
/// forms Forge/NeoForge emit: <c>g:a:v[:classifier][:extension]</c>,
/// <c>g:a:v[:classifier]@ext</c> and the bracketed data form <c>[g:a:v]</c>.
/// </summary>
internal sealed record MavenCoordinate(
    string Group,
    string Artifact,
    string Version,
    string? Classifier,
    string Extension)
{
    public string RelativePath
    {
        get
        {
            var fileName = $"{Artifact}-{Version}" +
                           (string.IsNullOrEmpty(Classifier) ? string.Empty : $"-{Classifier}") +
                           $".{Extension}";
            return $"{Group.Replace('.', '/')}/{Artifact}/{Version}/{fileName}";
        }
    }

    public string Coordinate
        => $"{Group}:{Artifact}:{Version}" +
           (string.IsNullOrEmpty(Classifier) ? string.Empty : $":{Classifier}") +
           (Extension == "jar" ? string.Empty : $"@{Extension}");

    public MavenCoordinate WithClassifier(string classifier, string extension)
        => this with { Classifier = classifier, Extension = extension };
}

internal static class MavenPath
{
    private static readonly HashSet<string> KnownExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        "jar", "zip", "lzma", "tsrg", "txt", "json", "xml",
    };

    public static bool TryParse(string? value, out MavenCoordinate coordinate)
    {
        coordinate = null!;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var text = value.Trim();
        if (text.Length >= 2 && text[0] == '[' && text[^1] == ']')
        {
            text = text[1..^1].Trim();
        }

        var extension = "jar";
        var at = text.LastIndexOf('@');
        if (at >= 0)
        {
            extension = text[(at + 1)..].Trim();
            text = text[..at];
            if (extension.Length == 0)
            {
                extension = "jar";
            }
        }

        var parts = text.Split(':');
        if (parts.Length < 3)
        {
            return false;
        }

        var group = parts[0].Trim();
        var artifact = parts[1].Trim();
        var version = parts[2].Trim();
        string? classifier = null;

        if (parts.Length >= 4 && parts[3].Length > 0)
        {
            var fourth = parts[3].Trim();
            if (parts.Length == 4 && KnownExtensions.Contains(fourth) && at < 0)
            {
                // g:a:v:jar means extension, not a classifier.
                extension = fourth;
            }
            else
            {
                classifier = fourth;
            }
        }

        if (parts.Length >= 5 && parts[4].Length > 0)
        {
            extension = parts[4].Trim();
        }

        if (group.Length == 0 || artifact.Length == 0 || version.Length == 0)
        {
            return false;
        }

        if (!IsSafeSegment(group) || !IsSafeSegment(artifact) || !IsSafeSegment(version) ||
            (classifier is not null && !IsSafeSegment(classifier)) || !IsSafeSegment(extension))
        {
            return false;
        }

        coordinate = new MavenCoordinate(group, artifact, version, classifier, extension);
        return true;
    }

    public static string ResolvePath(string? coordinate, string what)
    {
        if (!TryParse(coordinate, out var parsed))
        {
            throw new LoaderManifestException(
                "E_MAVEN_COORDINATE",
                $"{what}: invalid maven coordinate '{coordinate}'");
        }

        return parsed.RelativePath;
    }

    private static bool IsSafeSegment(string value)
    {
        if (value.Length == 0 || value == "." || value == "..")
        {
            return false;
        }

        foreach (var character in value)
        {
            if (character is '/' or '\\' or ':' or '\0' || char.IsControl(character))
            {
                return false;
            }
        }

        return true;
    }
}
