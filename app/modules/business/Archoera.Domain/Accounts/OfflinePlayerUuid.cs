using System.Security.Cryptography;
using System.Text;

namespace Archoera.Domain.Accounts;

/// <summary>
/// Minecraft's legacy offline-player identity: the UUID is derived from the
/// display name (<c>OfflinePlayer:</c> prefix + MD5, ver. 3 / variant bits).
/// Both the launch command substitution and the offline sign-in flow must
/// produce the same UUID or servers reject the profile, so this is the single
/// definition for the whole module.
/// </summary>
public static class OfflinePlayerUuid
{
    public static string FromName(string name)
    {
        var bytes = MD5.HashData(Encoding.UTF8.GetBytes($"OfflinePlayer:{name}"));
        bytes[6] = (byte)((bytes[6] & 0x0F) | 0x30);
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);
        var hex = Convert.ToHexStringLower(bytes);
        return $"{hex[..8]}-{hex[8..12]}-{hex[12..16]}-{hex[16..20]}-{hex[20..]}";
    }
}
