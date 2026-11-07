using System.Text;
using System.Text.RegularExpressions;
using Archoera.Infrastructure.Build;

namespace Archoera.Tests;

public sealed class ClientIdObfuscationTests
{
    private static readonly Regex GuidShape = new(
        "^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$",
        RegexOptions.IgnoreCase);

    [Fact]
    public void Round_trips_a_client_id()
    {
        const string clientId = "5f1c0a3e-9b2d-4c7a-8e61-2d4b7a9f0c53";

        var blob = ClientIdObfuscation.Encode(clientId);

        Assert.NotEmpty(blob);
        Assert.DoesNotContain(clientId, blob, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(16 + clientId.Length, Convert.FromBase64String(blob).Length);
        Assert.Equal(clientId, ClientIdObfuscation.Decode(blob));
    }

    [Fact]
    public void Each_encode_uses_a_fresh_nonce()
    {
        const string clientId = "5f1c0a3e-9b2d-4c7a-8e61-2d4b7a9f0c53";

        var first = ClientIdObfuscation.Encode(clientId);
        var second = ClientIdObfuscation.Encode(clientId);

        Assert.NotEqual(first, second);
        Assert.Equal(clientId, ClientIdObfuscation.Decode(first));
        Assert.Equal(clientId, ClientIdObfuscation.Decode(second));
    }

    [Fact]
    public void Blob_is_base64_and_has_no_readable_text()
    {
        var blob = ClientIdObfuscation.Encode("aaaaaaaa-bbbb-4ccc-8ddd-eeeeeeeeeeee");
        var raw = Convert.FromBase64String(blob);
        var text = Encoding.UTF8.GetString(raw);

        Assert.DoesNotContain("aaaa", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("eeee", text, StringComparison.OrdinalIgnoreCase);
        // At least one byte outside printable ASCII: the payload is not literal.
        Assert.Contains(raw, b => b < 0x20 || b > 0x7E);
    }

    [Fact]
    public void Missing_or_broken_blobs_decode_to_empty()
    {
        Assert.Equal(string.Empty, ClientIdObfuscation.Encode(null));
        Assert.Equal(string.Empty, ClientIdObfuscation.Encode("   "));
        Assert.Equal(string.Empty, ClientIdObfuscation.Decode(null));
        Assert.Equal(string.Empty, ClientIdObfuscation.Decode(""));
        Assert.Equal(string.Empty, ClientIdObfuscation.Decode("not base64!"));
        Assert.Equal(string.Empty, ClientIdObfuscation.Decode("AAAA"));
    }

    [Fact]
    public void Compiled_blob_round_trips_the_injected_id()
    {
        var blob = MicrosoftBuildConfig.ClientIdBlob;
        var clientId = MicrosoftBuildConfig.ClientId;

        if (string.IsNullOrEmpty(blob))
        {
            Assert.Equal(string.Empty, clientId);
            return;
        }

        Assert.Matches(GuidShape, clientId);
        Assert.Equal(clientId, ClientIdObfuscation.Decode(blob));
        // The plaintext id must not sit in the blob or the generated source.
        Assert.DoesNotContain(
            clientId.ToLowerInvariant(),
            blob,
            StringComparison.OrdinalIgnoreCase);
    }
}
