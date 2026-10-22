using System.Text.RegularExpressions;
using System.Text.Json;
using Archoera.Infrastructure.Build;
using Archoera.Infrastructure.SystemInfo;

namespace Archoera.Tests;

public sealed class BuildIdentityTests
{
    [Fact]
    public void Digest_is_domain_separated_sha256_hex()
    {
        var digest = OfficialBuildIdentity.Sha256(
            "5f1c0a3e-9b2d-4c7a-8e61-2d4b7a9f0c53");

        Assert.NotNull(digest);
        Assert.Matches(new Regex("^[0-9a-f]{64}$"), digest);
        // Domain separation: the published digest is not SHA-256(id) alone, so
        // it cannot be matched against generic UUID hash tables.
        Assert.NotEqual(
            "05bde05fc10e0b2c198ef8e9c1bf5b6530beba347c91eb3b17510fb362bc3fe1",
            digest);
        Assert.Equal(
            "6fc1f7afb7e4d21a8f3eb3fd094dd3e611f94f1aa7a7daa1cb6af940b8a9fa5e",
            digest);
    }

    [Fact]
    public void Digest_is_deterministic_and_id_specific()
    {
        var first = OfficialBuildIdentity.Sha256("00000000-0000-4000-8000-000000000000");
        var again = OfficialBuildIdentity.Sha256("00000000-0000-4000-8000-000000000000");
        var other = OfficialBuildIdentity.Sha256("11111111-1111-4111-8111-111111111111");

        Assert.Equal(first, again);
        Assert.NotEqual(first, other);
    }

    [Fact]
    public void Digest_does_not_leak_the_client_id()
    {
        const string clientId = "5f1c0a3e-9b2d-4c7a-8e61-2d4b7a9f0c53";
        var digest = OfficialBuildIdentity.Sha256(clientId)!;

        Assert.DoesNotContain(clientId, digest, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("5f1c0a3e", digest, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("9b2d4c7a", digest, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(64, digest.Length);
    }

    [Fact]
    public void Missing_client_id_is_unconfigured()
    {
        foreach (var value in new string?[] { null, "", "   " })
        {
            var identity = OfficialBuildIdentity.Describe(value);
            Assert.Equal(OfficialBuildIdentity.ChannelUnconfigured, identity.Channel);
            Assert.False(identity.Official);
            Assert.Null(identity.ClientIdSha256);
            Assert.Null(identity.ClientIdFingerprint);
        }
    }

    [Fact]
    public void Foreign_client_id_is_a_custom_build()
    {
        var identity = OfficialBuildIdentity.Describe("aaaaaaaa-bbbb-4ccc-8ddd-eeeeeeeeeeee");

        Assert.Equal(OfficialBuildIdentity.ChannelCustom, identity.Channel);
        Assert.False(identity.Official);
        Assert.NotNull(identity.ClientIdSha256);
        Assert.Equal(12, identity.ClientIdFingerprint!.Length);
    }

    [Fact]
    public void Official_constant_is_a_sha256_digest()
    {
        Assert.Matches(
            new Regex("^[0-9a-f]{64}$"),
            OfficialBuildIdentity.OfficialClientIdSha256);
    }

    [Fact]
    public async Task System_identity_reports_a_digest_only()
    {
        var service = new SystemModuleService();
        using var document = JsonDocument.Parse(
            await service.HandleAsync("system.identity", "{}", CancellationToken.None));

        Assert.True(document.RootElement.GetProperty("ok").GetBoolean());
        var data = document.RootElement.GetProperty("data");
        var channel = data.GetProperty("channel").GetString();
        Assert.Contains(
            channel,
            new[]
            {
                OfficialBuildIdentity.ChannelOfficial,
                OfficialBuildIdentity.ChannelCustom,
                OfficialBuildIdentity.ChannelUnconfigured,
            });

        var official = data.GetProperty("official").GetBoolean();
        Assert.Equal(official, channel == OfficialBuildIdentity.ChannelOfficial);

        var digest = data.GetProperty("clientIdSha256");
        var compiled = MicrosoftBuildConfig.ClientId;
        if (string.IsNullOrWhiteSpace(compiled))
        {
            Assert.Equal(JsonValueKind.Null, digest.ValueKind);
            Assert.Equal(
                OfficialBuildIdentity.ChannelUnconfigured,
                channel);
        }
        else
        {
            Assert.Matches(new Regex("^[0-9a-f]{64}$"), digest.GetString());
            Assert.Equal(OfficialBuildIdentity.Sha256(compiled), digest.GetString());
            // The raw id must never cross the module boundary.
            Assert.DoesNotContain(
                compiled,
                document.RootElement.GetRawText(),
                StringComparison.OrdinalIgnoreCase);
        }

        // An empty secret means the badge stays hidden: no crash, no id.
        Assert.NotEqual(JsonValueKind.Undefined, data.GetProperty("clientIdFingerprint").ValueKind);
    }

    [Fact]
    public async Task System_identity_rejects_other_methods()
    {
        var service = new SystemModuleService();
        using var document = JsonDocument.Parse(
            await service.HandleAsync("system.unknown", "{}", CancellationToken.None));

        Assert.False(document.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal(
            "E_UNSUPPORTED",
            document.RootElement
                .GetProperty("error")
                .GetProperty("code")
                .GetString());
    }
}
