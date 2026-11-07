namespace Archoera.Domain.Mojang;

/// <summary>
/// Parsed Maven coordinate for a <c>libraries[]</c> entry. The merge key keeps
/// the version out (<c>group:artifact:classifier:extension</c>) so a child layer
/// can replace a parent version without moving the classpath position
/// (docs/08 01 §3.2).
/// </summary>
/// <param name="Group">Maven group, for example <c>com.google.guava</c>.</param>
/// <param name="Artifact">Artifact id, for example <c>guava</c>.</param>
/// <param name="Version">Exact version text from the coordinate.</param>
/// <param name="Classifier">Classifier, or an empty string when absent.</param>
/// <param name="Extension">Extension, defaulting to <c>jar</c>.</param>
public sealed record MojangLibraryCoordinate(
    string Group,
    string Artifact,
    string Version,
    string Classifier,
    string Extension)
{
    private static readonly HashSet<string> KnownExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        "jar", "zip", "lzma", "tsrg", "txt", "json", "xml",
    };

    /// <summary>Version-independent deduplication key.</summary>
    public string Key => $"{Group}:{Artifact}:{Classifier}:{Extension}";

    /// <summary>
    /// Parses <c>group:artifact:version[:classifier][@extension]</c> plus the
    /// Maven shorthand <c>group:artifact:version[:extension]</c>. Returns false
    /// instead of throwing so the merge pass can preserve the raw entry and only
    /// emit a warning.
    /// </summary>
    public static bool TryParse(string? coordinate, out MojangLibraryCoordinate parsed)
    {
        parsed = null!;

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
        if (parts.Length is < 3 or > 5)
        {
            return false;
        }

        var group = parts[0].Trim();
        var artifact = parts[1].Trim();
        var version = parts[2].Trim();
        var classifier = string.Empty;

        if (parts.Length == 4)
        {
            var fourth = parts[3].Trim();
            if (fourth.Length == 0)
            {
                // g:a:v: is not a valid coordinate.
                return false;
            }

            if (at < 0 && KnownExtensions.Contains(fourth))
            {
                // g:a:v:jar means extension, not a classifier.
                extension = fourth;
            }
            else
            {
                classifier = fourth;
            }
        }
        else if (parts.Length == 5)
        {
            classifier = parts[3].Trim();
            if (classifier.Length == 0)
            {
                return false;
            }

            extension = parts[4].Trim();
        }

        if (group.Length == 0 || artifact.Length == 0 || version.Length == 0
            || extension.Length == 0
            || !IsSafeSegment(group) || !IsSafeSegment(artifact)
            || !IsSafeSegment(version) || !IsSafeSegment(extension)
            || (classifier.Length > 0 && !IsSafeSegment(classifier)))
        {
            return false;
        }

        parsed = new MojangLibraryCoordinate(group, artifact, version, classifier, extension);
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
