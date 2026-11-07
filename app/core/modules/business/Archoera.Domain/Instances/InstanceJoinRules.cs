namespace Archoera.Domain.Instances;

/// <summary>
/// Validation for the per-instance auto-join fields. Keeping it here means the
/// settings dialog, the instance patch handler and the launch plan apply the
/// same limits.
/// </summary>
public static class InstanceJoinRules
{
    /// <summary>Maximum accepted world name length.</summary>
    public const int MaxWorldNameLength = 128;

    /// <summary>
    /// Empty values mean "not configured" and become <c>null</c>; any other
    /// value must be a valid <see cref="ServerAddress"/>.
    /// </summary>
    public static string? NormalizeAddress(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        if (!ServerAddress.TryParse(value, out _))
        {
            throw new ArgumentException(
                "joinServerAddress must be host, host:port, [v6] or [v6]:port");
        }

        return value;
    }

    /// <summary>
    /// Empty values mean "not configured" and become <c>null</c>. World names
    /// are folder names, so separators and parent traversal are rejected.
    /// </summary>
    public static string? NormalizeWorldName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        if (value.Length > MaxWorldNameLength)
        {
            throw new ArgumentException(
                $"joinWorldName must be at most {MaxWorldNameLength} characters");
        }

        foreach (var character in value)
        {
            if (character is '/' or '\\' || char.IsControl(character))
            {
                throw new ArgumentException(
                    "joinWorldName must not contain path separators or control characters");
            }
        }

        if (value.Contains("..", StringComparison.Ordinal))
        {
            throw new ArgumentException("joinWorldName must not contain '..'");
        }

        return value;
    }

    /// <summary>
    /// An enabled join must have a target; otherwise the user would enable the
    /// switch and nothing would happen at launch.
    /// </summary>
    public static void ValidateTarget(bool? joinOnLaunch, string? address, string? world)
    {
        if (joinOnLaunch != true) return;
        if (string.IsNullOrWhiteSpace(address) && string.IsNullOrWhiteSpace(world))
        {
            throw new ArgumentException(
                "joinServerOnLaunch requires joinServerAddress or joinWorldName");
        }

        if (!string.IsNullOrWhiteSpace(address) && !string.IsNullOrWhiteSpace(world))
        {
            throw new ArgumentException(
                "joinServerAddress and joinWorldName are mutually exclusive");
        }
    }
}
