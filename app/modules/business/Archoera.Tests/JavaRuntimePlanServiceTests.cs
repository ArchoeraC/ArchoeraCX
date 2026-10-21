using Archoera.Application.Downloads;
using Archoera.Application.JavaRuntime;
using Archoera.Domain.Java;
using Archoera.Domain.Mojang;
using Archoera.Infrastructure.Mojang;

namespace Archoera.Tests;

public sealed class JavaRuntimePlanServiceTests
{
    [Fact]
    public async Task BuildAsync_fetches_catalog_and_manifest_and_exposes_diagnostics()
    {
        var manifestJson = JavaRuntimeTestData.HappyManifestJson();
        var handler = new FakeHttpMessageHandler();
        handler.EnqueueJson(JavaRuntimeTestData.CatalogJson(manifestJson));
        handler.EnqueueJson(manifestJson);

        var service = new JavaRuntimePlanService(CreateClient(handler));
        var result = await service.BuildAsync(
            JavaRuntimeTestData.Root,
            21,
            MojangPlatform.LinuxX64);

        Assert.Equal(JavaRuntimePlatform.Linux, result.PlatformKey);
        Assert.Equal("java-runtime-delta", result.ComponentName);
        Assert.True(result.Selection.IsDefault);
        Assert.Equal("21.0.7", result.VersionName);
        Assert.Equal(
            DateTimeOffset.Parse("2025-01-01T00:00:00+00:00"),
            result.Released);
        Assert.Equal(2, result.FileCount);
        Assert.Equal(128_023, result.TotalBytes);

        Assert.Contains(
            result.Plan.Files,
            file => file.Dest == "runtime/java-21/bin/java" && file.Executable);
        Assert.Contains(
            result.Plan.Files,
            file => file.Dest == "runtime/java-21/lib/libjli.so");

        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal(new Uri(JavaRuntimeTestData.CatalogUrl), handler.Requests[0].Uri);
        Assert.Equal(new Uri(JavaRuntimeTestData.ManifestUrl), handler.Requests[1].Uri);

        Assert.True(
            DownloadPlanJson.TryParse(
                DownloadPlanJson.Build(result.Plan),
                out var parsed,
                out var error),
            error?.ToString());
        Assert.NotNull(parsed);
        Assert.Equal(result.Plan.PlanId, parsed.PlanId);
    }

    [Fact]
    public async Task BuildAsync_honours_an_explicit_component()
    {
        var manifestJson = JavaRuntimeTestData.HappyManifestJson();
        var handler = new FakeHttpMessageHandler();
        handler.EnqueueJson(JavaRuntimeTestData.CatalogJson(
            manifestJson,
            component: "java-runtime-gamma"));
        handler.EnqueueJson(manifestJson);

        var service = new JavaRuntimePlanService(CreateClient(handler));
        var result = await service.BuildAsync(
            JavaRuntimeTestData.Root,
            21,
            MojangPlatform.LinuxX64,
            component: "java-runtime-gamma");

        Assert.False(result.Selection.IsDefault);
        Assert.Equal("java-runtime-gamma", result.ComponentName);
        Assert.Equal("runtime/java-21/bin/java", result.Plan.Files[0].Dest);
    }

    [Fact]
    public async Task BuildAsync_fails_an_unknown_major_before_touching_the_network()
    {
        var handler = new FakeHttpMessageHandler();
        var service = new JavaRuntimePlanService(CreateClient(handler));

        var exception = await Assert.ThrowsAsync<JavaRuntimeException>(
            () => service.BuildAsync(
                JavaRuntimeTestData.Root,
                11,
                MojangPlatform.LinuxX64));

        Assert.Equal(JavaRuntimeErrorCode.UnsupportedJavaVersion, exception.Code);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task BuildAsync_rejects_a_relative_root_before_touching_the_network()
    {
        var handler = new FakeHttpMessageHandler();
        var service = new JavaRuntimePlanService(CreateClient(handler));

        var exception = await Assert.ThrowsAsync<JavaRuntimeException>(
            () => service.BuildAsync("relative/root", 21, MojangPlatform.LinuxX64));

        Assert.Equal(JavaRuntimeErrorCode.InvalidRequest, exception.Code);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task BuildAsync_reports_no_runtime_for_a_missing_platform_or_component()
    {
        var manifestJson = JavaRuntimeTestData.HappyManifestJson();
        var handler = new FakeHttpMessageHandler();
        handler.EnqueueJson(JavaRuntimeTestData.CatalogJson(manifestJson));

        var service = new JavaRuntimePlanService(CreateClient(handler));

        var platformException = await Assert.ThrowsAsync<JavaRuntimeException>(
            () => service.BuildAsync(
                JavaRuntimeTestData.Root,
                21,
                MojangPlatform.WindowsX64));
        Assert.Equal(JavaRuntimeErrorCode.NoRuntimeAvailable, platformException.Code);
        Assert.Single(handler.Requests);

        handler.EnqueueJson(JavaRuntimeTestData.CatalogJson(manifestJson));

        var componentException = await Assert.ThrowsAsync<JavaRuntimeException>(
            () => service.BuildAsync(
                JavaRuntimeTestData.Root,
                17,
                MojangPlatform.LinuxX64));
        Assert.Equal(JavaRuntimeErrorCode.NoRuntimeAvailable, componentException.Code);
        Assert.Equal(2, handler.Requests.Count);
    }

    private static JavaRuntimeClient CreateClient(FakeHttpMessageHandler handler)
        => new(
            new HttpClient(handler),
            new MojangClientOptions
            {
                UserAgent = JavaRuntimeTestData.UserAgent,
            });
}
