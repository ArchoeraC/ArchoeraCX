using System.Text;
using System.Text.Json;
using Archoera.Infrastructure.Launch;

namespace Archoera.Tests;

/// <summary>
/// The launch error envelope carries enough context for the UI to explain the
/// failure without re-reading state: the stage that failed, whether retrying
/// can help, and the Java/log paths that make a report actionable.
/// </summary>
public sealed class LaunchFailureDetailsTests
{
    [Fact]
    public void Error_writes_the_diagnostic_object()
    {
        var envelope = LaunchFailureDetails.Error(
            "JAVA_MISSING",
            "no Java runtime found",
            LaunchProgress.StageJava,
            "instance-1",
            "/opt/java/bin/java",
            "21.0.2",
            "/logs/launch.log");

        var error = Parse(envelope).GetProperty("error");
        Assert.Equal("JAVA_MISSING", error.GetProperty("code").GetString());
        Assert.Equal(
            "no Java runtime found",
            error.GetProperty("message").GetString());

        var details = error.GetProperty("details");
        Assert.Equal("instance-1", details.GetProperty("instanceId").GetString());
        Assert.Equal("java", details.GetProperty("stage").GetString());
        Assert.True(details.GetProperty("retryable").GetBoolean());
        Assert.Equal("/opt/java/bin/java", details.GetProperty("javaPath").GetString());
        Assert.Equal("21.0.2", details.GetProperty("javaVersion").GetString());
        Assert.Equal("/logs/launch.log", details.GetProperty("logPath").GetString());
    }

    [Fact]
    public void Error_omits_missing_optional_values()
    {
        var envelope = LaunchFailureDetails.Error(
            "INSTANCE_NOT_FOUND",
            "instance not found",
            LaunchProgress.StageInstance,
            string.Empty,
            null,
            null,
            null);

        var details = Parse(envelope).GetProperty("error").GetProperty("details");
        Assert.Equal("instance", details.GetProperty("stage").GetString());
        Assert.False(details.GetProperty("retryable").GetBoolean());
        Assert.False(details.TryGetProperty("instanceId", out _));
        Assert.False(details.TryGetProperty("javaPath", out _));
        Assert.False(details.TryGetProperty("javaVersion", out _));
        Assert.False(details.TryGetProperty("logPath", out _));
    }

    [Theory]
    [InlineData("LIBRARIES_MISSING", true)]
    [InlineData("ASSET_MISSING", true)]
    [InlineData("JAVA_VERSION_MISMATCH", true)]
    [InlineData("LAUNCH_FILES_MISSING", true)]
    [InlineData("VERSION_NOT_MERGED", true)]
    [InlineData("AUTO_REPAIR_FAILED", true)]
    [InlineData("ACCOUNT_REQUIRED", false)]
    [InlineData("MICROSOFT_ACCOUNT_ONLY", false)]
    [InlineData("PREMIUM_ACCOUNT_REQUIRED", false)]
    [InlineData("E_INVALID", false)]
    public void Retryable_codes_match_the_launch_flow(string code, bool expected)
    {
        Assert.Equal(expected, LaunchFailureDetails.IsRetryable(code));
    }

    private static JsonElement Parse(byte[] envelope) =>
        JsonDocument.Parse(Encoding.UTF8.GetString(envelope)).RootElement;
}
