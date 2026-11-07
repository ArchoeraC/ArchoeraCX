namespace Archoera.Domain.Instances;

/// <summary>
/// A join target accepted by <c>joinServerAddress</c>. The accepted shapes are
/// <c>host</c>, <c>host:port</c>, <c>[v6]</c> and <c>[v6]:port</c>; anything
/// that looks like a URL, a path or contains whitespace/control characters is
/// rejected. Parsing is shared by settings validation and the launch plan so
/// the preview and the actual command line cannot disagree.
/// </summary>
public readonly record struct ServerAddress(string Host, int Port)
{
    /// <summary>Vanilla's default server port.</summary>
    public const int DefaultPort = 25565;

    /// <summary>Maximum accepted length of the raw address.</summary>
    public const int MaxLength = 255;

    /// <summary>
    /// <c>host:port</c> form used by Quick Play. IPv6 literals stay bracketed
    /// so the value can be split again without guessing where the host ends.
    /// </summary>
    public string HostAndPort =>
        Host.Contains(':', StringComparison.Ordinal)
            ? $"[{Host}]:{Port}"
            : $"{Host}:{Port}";

    /// <summary>Parses one of the four accepted address shapes.</summary>
    public static bool TryParse(string? value, out ServerAddress address)
    {
        address = default;
        if (string.IsNullOrWhiteSpace(value) || value.Length > MaxLength)
        {
            return false;
        }

        foreach (var character in value)
        {
            if (char.IsWhiteSpace(character) ||
                char.IsControl(character) ||
                character is '/' or '\\')
            {
                return false;
            }
        }

        // A scheme or authority/path syntax belongs in a URL field, not here.
        if (value.Contains("://", StringComparison.Ordinal))
        {
            return false;
        }

        string host;
        var port = DefaultPort;
        if (value[0] == '[')
        {
            var close = value.IndexOf(']', StringComparison.Ordinal);
            if (close <= 1) return false;
            host = value[1..close];
            if (host.Contains('[', StringComparison.Ordinal) ||
                host.Contains(']', StringComparison.Ordinal))
            {
                return false;
            }

            var rest = value[(close + 1)..];
            if (rest.Length > 0)
            {
                if (rest[0] != ':' || rest.Length == 1) return false;
                if (!TryParsePort(rest[1..], out port)) return false;
            }
        }
        else
        {
            var colon = value.IndexOf(':', StringComparison.Ordinal);
            if (colon < 0)
            {
                host = value;
            }
            else
            {
                // Unbracketed IPv6 is ambiguous; require the bracket form.
                if (value.IndexOf(':', colon + 1) >= 0) return false;
                host = value[..colon];
                if (!TryParsePort(value[(colon + 1)..], out port)) return false;
            }

            if (host.Contains('[', StringComparison.Ordinal) ||
                host.Contains(']', StringComparison.Ordinal))
            {
                return false;
            }
        }

        if (host.Length == 0) return false;
        address = new ServerAddress(host, port);
        return true;
    }

    private static bool TryParsePort(string text, out int port)
    {
        port = 0;
        if (text.Length is 0 or > 5) return false;
        foreach (var character in text)
        {
            if (character is < '0' or > '9') return false;
        }

        return int.TryParse(text, out port) && port is >= 1 and <= 65535;
    }
}
