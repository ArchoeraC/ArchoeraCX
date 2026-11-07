using System.Security.Cryptography;
using System.Text;

namespace Archoera.Infrastructure.Build;

/// <summary>
/// Keeps the Microsoft OAuth client id out of plain sight in the compiled
/// binary. Builds store a high-entropy blob instead of the id itself: 16 random
/// bytes (nonce) followed by the id XOR-mixed with a SHA-256 keystream derived
/// from that nonce plus a position term.
/// </summary>
/// <remarks>
/// This is obfuscation, not secrecy: a public OAuth client id must be readable
/// by the client at runtime, so anyone analysing the binary can recover it. The
/// point is to keep the value out of `strings`/grep output, bug reports and log
/// dumps, and to make every build's blob differ (random nonce) so no fixed
/// signature can be harvested. The blob lives only in CI-generated sources; the
/// repository never contains it.
///
/// The encoder is mirrored inside the MSBuild target that generates
/// `MicrosoftBuildConfig.g.cs`; <see cref="Encode"/> exists so tests can prove
/// both directions agree.
/// </remarks>
public static class ClientIdObfuscation
{
    /// <summary>Domain separator for the keystream; must match the build task.</summary>
    public const string Domain = "archoeracx/client-id-blob/v1:";

    private const int NonceLength = 16;

    /// <summary>Encode a client id into a high-entropy blob (empty in, empty out).</summary>
    public static string Encode(string? clientId)
    {
        if (string.IsNullOrWhiteSpace(clientId)) return string.Empty;

        var plain = Encoding.UTF8.GetBytes(clientId.Trim());
        var blob = new byte[NonceLength + plain.Length];
        var nonce = blob.AsSpan(0, NonceLength);
        RandomNumberGenerator.Fill(nonce);
        Mix(plain, nonce, blob.AsSpan(NonceLength));
        return Convert.ToBase64String(blob);
    }

    /// <summary>Recover the client id from a blob (empty or invalid in, empty out).</summary>
    public static string Decode(string? blob)
    {
        if (string.IsNullOrWhiteSpace(blob)) return string.Empty;

        byte[] raw;
        try
        {
            raw = Convert.FromBase64String(blob.Trim());
        }
        catch (FormatException)
        {
            return string.Empty;
        }

        if (raw.Length <= NonceLength) return string.Empty;

        var nonce = raw.AsSpan(0, NonceLength);
        var plain = new byte[raw.Length - NonceLength];
        Mix(raw.AsSpan(NonceLength), nonce, plain);
        return Encoding.UTF8.GetString(plain);
    }

    /// <summary>
    /// XOR-mix <paramref name="input"/> into <paramref name="output"/> with a
    /// SHA-256 keystream combined with a position/nonce term. The transform is
    /// its own inverse, so encoding and decoding share one implementation.
    /// </summary>
    private static void Mix(ReadOnlySpan<byte> input, ReadOnlySpan<byte> nonce, Span<byte> output)
    {
        var key = SHA256.HashData(
            Encoding.UTF8.GetBytes(Domain + Convert.ToHexStringLower(nonce)));
        for (var i = 0; i < input.Length; i++)
        {
            var stream = key[i % key.Length] ^ (i * 31 + nonce[i % nonce.Length]);
            output[i] = (byte)(input[i] ^ stream);
        }
    }
}
