using System.Text.Json;
using Archoera.Infrastructure.Modules;
using Archoera.Infrastructure.Packs;
using Archoera.Infrastructure.Resources;

namespace Archoera.Tests;

/// <summary>
/// CurseForge is deliberately unsupported: online access and zip import both
/// need an API key the project does not bundle or accept. The wire code must
/// say <c>PROVIDER_UNAVAILABLE</c> so the UI does not send users looking for an
/// API key setting that does not exist.
/// </summary>
public sealed class ResourceProviderUnavailableTests
{
    [Theory]
    [InlineData("resource.search", "{\"provider\":\"curseforge\"}")]
    [InlineData("resource.project", "{\"provider\":\"curseforge\",\"projectId\":\"1\"}")]
    [InlineData(
        "resource.versions",
        "{\"provider\":\"curseforge\",\"projectId\":\"1\",\"kind\":\"mod\"}")]
    [InlineData("resource.plan", "{\"provider\":\"curseforge\"}")]
    public async Task Resource_methods_report_curseforge_as_unavailable(
        string method,
        string parameters)
    {
        var directory = CreateTempDirectory();
        try
        {
            var service = CreateService(directory);
            var response = await service.HandleAsync(
                method,
                parameters,
                CancellationToken.None);

            AssertProviderUnavailable(response);
        }
        finally
        {
            DeleteTempDirectory(directory);
        }
    }

    [Fact]
    public async Task Modpack_install_reports_curseforge_as_unavailable()
    {
        var directory = CreateTempDirectory();
        try
        {
            var policy = CreatePolicy(directory);
            var service = new PackModuleService(policy: policy);
            var response = await service.HandleAsync(
                "modpack.install",
                "{\"provider\":\"curseforge\",\"projectId\":\"1\",\"versionId\":\"2\"}",
                CancellationToken.None);

            AssertProviderUnavailable(response);
        }
        finally
        {
            DeleteTempDirectory(directory);
        }
    }

    private static ResourceModuleService CreateService(string directory)
        => new(policy: CreatePolicy(directory));

    private static DownloadPolicy CreatePolicy(string directory)
        => new(directory, new RegionPolicy(regionOverride: RegionPolicy.Global));

    private static void AssertProviderUnavailable(byte[] response)
    {
        using var document = JsonDocument.Parse(response);
        var root = document.RootElement;

        Assert.False(root.GetProperty("ok").GetBoolean());
        Assert.Equal(
            "PROVIDER_UNAVAILABLE",
            root.GetProperty("error").GetProperty("code").GetString());
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            "archoera-tests",
            $"provider-unavailable-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private static void DeleteTempDirectory(string path)
    {
        try
        {
            Directory.Delete(path, recursive: true);
        }
        catch (IOException)
        {
            // Best effort on platforms that still hold a handle.
        }
    }
}
