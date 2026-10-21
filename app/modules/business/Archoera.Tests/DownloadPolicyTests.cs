using Archoera.Infrastructure.Modules;

namespace Archoera.Tests;

public sealed class DownloadPolicyTests
{
    [Fact]
    public void Global_build_is_enabled_without_a_config_file()
    {
        var directory = CreateTempDirectory();
        try
        {
            var policy = new DownloadPolicy(directory, chinaBuild: false);
            var state = policy.Read();
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
    public void China_build_requires_explicit_risk_acceptance()
    {
        var directory = CreateTempDirectory();
        try
        {
            var policy = new DownloadPolicy(directory, chinaBuild: true);
            var state = policy.Read();
            Assert.False(state.Enabled);
            Assert.Equal("CONSENT_REQUIRED", state.Reason);
            Assert.Contains("acceptRisk", state.MissingFields);
            Assert.Throws<DownloadPolicyException>(() => policy.EnsureEnabled());

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

            var enabled = policy.Read();
            Assert.True(enabled.Enabled);
            Assert.Equal("L2", enabled.DownloadLevel);
            policy.EnsureEnabled();
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Incomplete_china_config_reports_missing_fields()
    {
        var directory = CreateTempDirectory();
        try
        {
            var policy = new DownloadPolicy(directory, chinaBuild: true);
            File.WriteAllText(
                policy.ConfigPath,
                """{"schemaVersion":1,"developerMode":true,"acceptRisk":true}""");
            var state = policy.Read();
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
