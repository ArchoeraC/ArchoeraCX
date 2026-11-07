using System.Text.Json;
using Archoera.Application.Java;
using Archoera.Application.JavaRuntime;
using Archoera.Domain.Java;
using Archoera.Infrastructure.Java;
using Archoera.Infrastructure.Settings;

namespace Archoera.Tests;

/// <summary>
/// Java runtime discovery/install wiring: the module must expose the Mojang
/// plan method and always look at the launcher-managed runtime directory.
/// </summary>
public sealed class JavaRuntimeModuleTests
{
    [Fact]
    public async Task Runtime_plan_rejects_an_unsupported_major()
    {
        var root = TempDirectory();
        try
        {
            var service = CreateService(root, new RecordingScanner());

            var document = JsonDocument.Parse(
                await service.HandleAsync(
                    "java.runtime.plan",
                    """{"major":3}""",
                    CancellationToken.None));

            Assert.False(document.RootElement.GetProperty("ok").GetBoolean());
            Assert.Equal(
                "E_INVALID",
                document.RootElement.GetProperty("error").GetProperty("code").GetString());
        }
        finally
        {
            Delete(root);
        }
    }

    [Fact]
    public async Task Scan_includes_the_managed_runtime_root()
    {
        var root = TempDirectory();
        try
        {
            var scanner = new RecordingScanner();
            var service = CreateService(root, scanner);

            await service.HandleAsync("java.scan", "{}", CancellationToken.None);

            Assert.NotNull(scanner.LastRequest);
            Assert.Contains(
                ManagedJavaRuntimes.Root(root),
                scanner.LastRequest!.Roots);
            Assert.True(scanner.LastRequest.IncludeDefaults);
        }
        finally
        {
            Delete(root);
        }
    }

    [Fact]
    public async Task Scan_keeps_caller_roots_next_to_the_managed_root()
    {
        var root = TempDirectory();
        try
        {
            var scanner = new RecordingScanner();
            var service = CreateService(root, scanner);
            var custom = Path.Combine(root, "custom-jdks");

            await service.HandleAsync(
                "java.scan",
                $$"""{"roots":["{{custom.Replace("\\", "\\\\")}}"]}""",
                CancellationToken.None);

            Assert.NotNull(scanner.LastRequest);
            Assert.Contains(custom, scanner.LastRequest!.Roots);
            Assert.Contains(ManagedJavaRuntimes.Root(root), scanner.LastRequest.Roots);
        }
        finally
        {
            Delete(root);
        }
    }

    private static JavaModuleService CreateService(string dataRoot, IJavaScanner scanner)
        => new(
            scanner: scanner,
            settings: new JsonAppSettingsStore(dataRoot));

    private static string TempDirectory()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            "archoera-java-runtime-module-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void Delete(string path)
    {
        try
        {
            if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
        }
        catch
        {
            // best effort
        }
    }

    private sealed class RecordingScanner : IJavaScanner
    {
        public JavaScanRequest? LastRequest { get; private set; }

        public IReadOnlyList<JavaRuntimeInfo> Scan(
            JavaScanRequest request,
            CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            return [];
        }

        public IReadOnlyList<JavaRuntimeInfo> Scan(
            CancellationToken cancellationToken = default) =>
            Scan(JavaScanRequest.Default, cancellationToken);
    }
}
