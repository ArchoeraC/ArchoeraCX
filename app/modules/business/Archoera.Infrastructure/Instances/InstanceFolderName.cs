using System.Buffers;
using System.Globalization;
using System.Text;
using Archoera.Domain.Downloads;

namespace Archoera.Infrastructure.Instances;

/// <summary>
/// Turns a user-visible instance name into an on-disk folder name. Other
/// launchers keep Unicode folder names and only replace characters their host
/// file systems cannot represent; doing the same keeps Chinese pack names
/// distinguishable instead of collapsing them into <c>-</c>.
/// </summary>
internal static class InstanceFolderName
{
    /// <summary>Folder names longer than this are truncated.</summary>
    public const int MaxLength = 80;

    private const int MaxSuffix = 10_000;

    private static readonly SearchValues<char> InvalidCharacters =
        SearchValues.Create("<>:\"/\\|?*");

    /// <summary>
    /// Rewrites Windows-invalid characters to <c>-</c>, trims characters
    /// Windows silently drops, and avoids device names. Unicode is preserved.
    /// </summary>
    public static string FromDisplayName(string name)
    {
        var builder = new StringBuilder(MaxLength);
        foreach (var rune in name.Trim().EnumerateRunes())
        {
            if (builder.Length >= MaxLength) break;
            if (Rune.GetUnicodeCategory(rune) == UnicodeCategory.Control ||
                rune.Value == 0x7F)
            {
                continue;
            }

            builder.Append(
                rune.Value < 0x80 && InvalidCharacters.Contains((char)rune.Value)
                    ? '-'
                    : rune.ToString());
        }

        var folder = builder.ToString().Trim(' ', '.');
        if (folder.Length == 0 || folder is "." or "..")
        {
            return "instance";
        }

        // `CON`, `NUL`, `COM1`... must not become device names on Windows.
        return DownloadPlanPaths.IsWindowsReservedName(folder)
            ? "_" + folder
            : folder;
    }

    /// <summary>
    /// Appends <c> (2)</c>, <c> (3)</c>... until the candidate is unused. Both
    /// repositories use this so SQLite and JSON instances cannot share a root.
    /// </summary>
    public static async Task<string> ResolveUniqueAsync(
        string parent,
        string baseName,
        Func<string, Task<bool>> isUsed,
        CancellationToken cancellationToken)
    {
        var candidate = Path.Combine(parent, baseName);
        var suffix = 2;
        while (await isUsed(candidate).ConfigureAwait(false))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (suffix > MaxSuffix)
            {
                throw new ArgumentException(
                    $"cannot allocate a unique folder name for '{baseName}'");
            }

            candidate = Path.Combine(parent, $"{baseName} ({suffix++})");
        }

        return candidate;
    }
}
