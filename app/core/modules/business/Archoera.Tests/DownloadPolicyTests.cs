using Archoera.Infrastructure.Modules;

namespace Archoera.Tests;

public sealed class DownloadPolicyTests
{
    [Fact]
    public async Task Global_region_is_enabled_without_a_config_file()
    {
        var directory = CreateTempDirectory();
        try
        {
            var policy = new DownloadPolicy(
                directory,
                new RegionPolicy(regionOverride: "GLOBAL"));
            var state = await policy.ReadAsync();
            Assert.True(state.Enabled);
            Assert.Equal("ENABLED", state.Reason);
            Assert.Empty(state.MissingFields);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Mainland_region_requires_explicit_risk_acceptance()
    {
        var directory = CreateTempDirectory();
        try
        {
            var policy = new DownloadPolicy(
                directory,
                new RegionPolicy(regionOverride: "CN"));
            var state = await policy.ReadAsync();
            Assert.False(state.Enabled);
            Assert.Equal("CONSENT_REQUIRED", state.Reason);
            Assert.Contains("acceptRisk", state.MissingFields);
            await Assert.ThrowsAsync<DownloadPolicyException>(
                () => policy.EnsureEnabledAsync());

            File.WriteAllText(
                policy.ConfigPath,
                """
                {
                  "schemaVersion": 1,
                  "developerMode": true,
                  "acceptRisk": true,
                  "downloadLevel": "L2",
                  "acknowledgement": "ENABLE-DOWNLOADS 1"
                }
                """);

            var enabled = await policy.ReadAsync();
            Assert.True(enabled.Enabled);
            Assert.Equal("L2", enabled.DownloadLevel);
            await policy.EnsureEnabledAsync();
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Incomplete_mainland_config_reports_missing_fields()
    {
        var directory = CreateTempDirectory();
        try
        {
            var policy = new DownloadPolicy(
                directory,
                new RegionPolicy(regionOverride: "CN"));
            File.WriteAllText(
                policy.ConfigPath,
                """{"schemaVersion":1,"developerMode":true,"acceptRisk":true}""");
            var state = await policy.ReadAsync();
            Assert.False(state.Enabled);
            Assert.Contains("downloadLevel", state.MissingFields);
            Assert.Contains("acknowledgement", state.MissingFields);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            "archoera-policy-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
