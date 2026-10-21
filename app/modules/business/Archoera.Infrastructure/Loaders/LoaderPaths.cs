using System.Security.Cryptography;
using Archoera.Application.Loaders;

namespace Archoera.Infrastructure.Loaders;

/// <summary>
/// Path normalization and relocatable-token helpers shared by the planner,
/// verifier and repair service. Every manifest path goes through here.
/// </summary>
internal static class LoaderPaths
{
    public const string RootToken = "{ROOT}";
    public const string LibrariesToken = "{LIBRARIES}";
    public const string InstallerToken = "{INSTALLER}";
    public const string MinecraftJarToken = "{MINECRAFT_JAR}";

    /// <summary>
    /// Normalizes a manifest path to forward slashes and rejects absolute
    /// paths, drive letters, empty segments and <c>..</c> segments.
    /// </summary>
    public static string NormalizeRelative(string? value, string what)
    {
        if (!TryNormalizeRelative(value, out var normalized, out var error))
        {
            throw new LoaderManifestException("E_UNSAFE_PATH", $"{what}: {error}");
        }

        return normalized;
    }

    public static bool TryNormalizeRelative(string? value, out string normalized, out string error)
    {
        normalized = string.Empty;
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(value))
        {
            error = "path is empty";
            return false;
        }

        var text = value.Replace('\\', '/').Trim();
        if (text.Length >= 2 && char.IsLetter(text[0]) && text[1] == ':')
        {
            error = "absolute drive path is not allowed";
            return false;
        }

        if (text.StartsWith('/'))
        {
            error = "absolute path is not allowed";
            return false;
        }

        var segments = new List<string>();
        foreach (var segment in text.Split('/'))
        {
            if (segment.Length == 0 || segment == ".")
            {
                continue;
            }

            if (segment == "..")
            {
                error = "parent traversal is not allowed";
                return false;
            }

            segments.Add(segment);
        }

        if (segments.Count == 0)
        {
            normalized = ".";
            return true;
        }

        normalized = string.Join('/', segments);
        return true;
    }

    public static string Combine(string baseFull, string normalizedRelative)
    {
        return normalizedRelative == "."
            ? Path.GetFullPath(baseFull)
            : Path.GetFullPath(Path.Combine(baseFull, normalizedRelative.Replace('/', Path.DirectorySeparatorChar)));
    }

    /// <summary>
    /// Resolves a manifest-relative path and proves it stays under
    /// <paramref name="baseFull"/>. The returned path is never opened on failure.
    /// </summary>
    public static bool TryResolveWithin(
        string baseFull,
        string? relative,
        out string fullPath,
        out string normalized,
        out string error)
    {
        fullPath = string.Empty;
        if (!TryNormalizeRelative(relative, out normalized, out error))
        {
            return false;
        }

        try
        {
            fullPath = Combine(baseFull, normalized);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            error = exception.Message;
            return false;
        }

        if (!IsWithin(baseFull, fullPath))
        {
            error = "path escapes its base directory";
            return false;
        }

        return true;
    }

    public static bool IsWithin(string baseFull, string candidateFull)
    {
        var comparison = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        var baseTrimmed = Path.TrimEndingDirectorySeparator(Path.GetFullPath(baseFull));
        var baseWithSeparator = Path.EndsInDirectorySeparator(baseTrimmed)
            ? baseTrimmed
            : baseTrimmed + Path.DirectorySeparatorChar;
        var candidate = Path.TrimEndingDirectorySeparator(Path.GetFullPath(candidateFull));

        if (string.Equals(candidate, baseTrimmed, comparison))
        {
            return true;
        }

        return candidate.StartsWith(baseWithSeparator, comparison);
    }

    public static bool TryRelativeWithin(string baseFull, string candidateFull, out string relative)
    {
        relative = string.Empty;
        if (!IsWithin(baseFull, candidateFull))
        {
            return false;
        }

        var value = Path.GetRelativePath(baseFull, candidateFull);
        if (value == ".")
        {
            relative = ".";
            return true;
        }

        return TryNormalizeRelative(value, out relative, out _);
    }

    /// <summary>
    /// Converts an absolute path under a known base into a relocatable token.
    /// Absolute paths outside the known bases are rejected.
    /// </summary>
    public static string ToToken(
        string absolutePath,
        string rootFull,
        string librariesFull,
        string installerFull)
    {
        var full = Path.GetFullPath(absolutePath);

        if (PathsEqual(full, installerFull))
        {
            return InstallerToken;
        }

        if (TryRelativeWithin(librariesFull, full, out var libraryRelative))
        {
            return libraryRelative == "." ? LibrariesToken : $"{LibrariesToken}/{libraryRelative}";
        }

        if (TryRelativeWithin(rootFull, full, out var rootRelative))
        {
            return rootRelative == "." ? RootToken : $"{RootToken}/{rootRelative}";
        }

        throw new LoaderManifestException(
            "E_ABSOLUTE_PATH",
            $"absolute path outside the instance/libraries roots cannot be stored in a manifest: {full}");
    }

    public static string ExpandToken(
        string token,
        string rootFull,
        string librariesFull,
        string installerFull,
        string minecraftJarFull)
    {
        if (token == RootToken)
        {
            return rootFull;
        }

        if (token == LibrariesToken)
        {
            return librariesFull;
        }

        if (token == InstallerToken)
        {
            return installerFull;
        }

        if (token == MinecraftJarToken)
        {
            return minecraftJarFull;
        }

        if (token.StartsWith(RootToken + "/", StringComparison.Ordinal))
        {
            return Combine(rootFull, token[(RootToken.Length + 1)..]);
        }

        if (token.StartsWith(LibrariesToken + "/", StringComparison.Ordinal))
        {
            return Combine(librariesFull, token[(LibrariesToken.Length + 1)..]);
        }

        if (token.StartsWith(InstallerToken + "/", StringComparison.Ordinal))
        {
            return Combine(rootFull, token[(InstallerToken.Length + 1)..]);
        }

        return token;
    }

    public static bool LooksLikeSha1(string? value)
    {
        if (value is null || value.Length != 40)
        {
            return false;
        }

        foreach (var character in value)
        {
            if (!Uri.IsHexDigit(character))
            {
                return false;
            }
        }

        return true;
    }

    public static string? NormalizeSha1(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        return LooksLikeSha1(trimmed) ? trimmed.ToLowerInvariant() : null;
    }

    public static string Hex(byte[] bytes)
        => Convert.ToHexString(bytes).ToLowerInvariant();

    public static async Task<string> Sha1FileAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 81920,
            useAsync: true);
        var hash = await SHA1.HashDataAsync(stream, cancellationToken);
        return Hex(hash);
    }

    public static string Sha1File(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Hex(SHA1.HashData(stream));
    }

    public static string Sha1Bytes(ReadOnlySpan<byte> bytes)
        => Hex(SHA1.HashData(bytes));

    public static bool IsAbsoluteLike(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return false;
        }

        if (value.StartsWith('/') || value.StartsWith('\\'))
        {
            return true;
        }

        return value.Length >= 3 &&
               char.IsLetter(value[0]) &&
               value[1] == ':' &&
               (value[2] == '/' || value[2] == '\\');
    }

    private static bool PathsEqual(string left, string right)
    {
        var comparison = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        return string.Equals(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(left)),
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(right)),
            comparison);
    }
}
