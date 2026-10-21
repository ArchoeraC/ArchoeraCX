namespace Archoera.Application.Mojang;

/// <summary>
/// Minimal Maven coordinate resolver for vanilla library entries. Mojang
/// normally publishes <c>downloads.artifact.path</c>; this is the fallback for
/// legacy/native classifier entries that omit it.
/// </summary>
public static class MojangMavenPath
{
    private static readonly HashSet<string> KnownExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        "jar", "zip", "lzma", "tsrg", "txt", "json", "xml",
    };

    /// <summary>
    /// Resolves <c>group:artifact:version[:classifier][:extension]</c> or
    /// <c>...@extension</c> to a forward-slash path. When
    /// <paramref name="classifierOverride"/> is supplied it replaces the
    /// coordinate classifier (used for legacy <c>natives</c> maps).
    /// </summary>
    public static bool TryResolve(
        string? coordinate,
        string? classifierOverride,
        out string path)
    {
        path = string.Empty;

        if (string.IsNullOrWhiteSpace(coordinate))
        {
            return false;
        }

        var text = coordinate.Trim();
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
            if (parts.Length == 4 && at < 0 && KnownExtensions.Contains(fourth))
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

        if (!string.IsNullOrWhiteSpace(classifierOverride))
        {
            classifier = classifierOverride.Trim();
        }

        if (group.Length == 0 || artifact.Length == 0 || version.Length == 0
            || !IsSafeSegment(group) || !IsSafeSegment(artifact)
            || !IsSafeSegment(version) || !IsSafeSegment(extension)
            || (classifier is not null && !IsSafeSegment(classifier)))
        {
            return false;
        }

        var fileName = $"{artifact}-{version}" +
                       (string.IsNullOrEmpty(classifier) ? string.Empty : $"-{classifier}") +
                       $".{extension}";

        path = $"{group.Replace('.', '/')}/{artifact}/{version}/{fileName}";
        return true;
    }

    private static bool IsSafeSegment(string value)
    {
        if (value.Length == 0 || value is "." or "..")
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
