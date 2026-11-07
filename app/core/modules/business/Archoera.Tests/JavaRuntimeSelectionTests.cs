using Archoera.Application.Java;
using Archoera.Domain.Java;
using Archoera.Infrastructure.Java;
using Archoera.Infrastructure.Settings;

namespace Archoera.Tests;

public sealed class JavaRuntimeSelectionTests
{
    [Theory]
    [InlineData("26.2", 25)]
    [InlineData("1.21.1", 21)]
    [InlineData("1.20.6", 21)]
    [InlineData("1.20.1", 17)]
    [InlineData("1.19.4", 17)]
    [InlineData("1.18.2", 17)]
    [InlineData("1.17.1", 16)]
    [InlineData("1.16.5", 8)]
    [InlineData("1.12.2", 8)]
    [InlineData("unknown", null)]
    public void InferMajor_covers_loader_processor_requirements(
        string gameVersion,
        int? expected)
    {
        Assert.Equal(expected, JavaRuntimeSelectionService.InferMajor(gameVersion));
    }

    [Fact]
    public async Task Forced_instance_java_path_wins_over_global_settings()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "archoera-java-select-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var store = new JsonAppSettingsStore(root);
            var settings = await store.LoadAsync();
            await store.SaveAsync(settings with
            {
                JavaAuto = false,
                JavaPath = "/global/java",
            });
            var scanner = new RecordingScanner();
            var service = new JavaRuntimeSelectionService(scanner, store);

            var runtime = await service.SelectAsync(
                21,
                "1.21.1",
                CancellationToken.None,
                "/instance/java");

            Assert.Equal("/custom/java", runtime.Path);
            Assert.NotNull(scanner.LastRequest);
            Assert.False(scanner.LastRequest!.IncludeDefaults);
            Assert.Equal(["/instance/java"], scanner.LastRequest.Executables);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Auto_selection_scans_the_managed_runtime_root()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "archoera-java-select-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var store = new JsonAppSettingsStore(root);
            var scanner = new RecordingScanner();
            var service = new JavaRuntimeSelectionService(scanner, store);

            await service.SelectAsync(21, "1.21.1");

            Assert.NotNull(scanner.LastRequest);
            Assert.True(scanner.LastRequest!.IncludeDefaults);
            Assert.Contains(
                Archoera.Application.JavaRuntime.ManagedJavaRuntimes.Root(root),
                scanner.LastRequest.Roots);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Global_manual_java_is_used_without_an_instance_override()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "archoera-java-select-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var store = new JsonAppSettingsStore(root);
            var settings = await store.LoadAsync();
            // Keep the path fully qualified on every host: /global/java is not
            // drive-rooted on Windows and would be dropped by the settings store.
            var java = Path.Combine(root, "global-java");
            await store.SaveAsync(settings with
            {
                JavaAuto = false,
                JavaPath = java,
            });
            var scanner = new RecordingScanner();
            var service = new JavaRuntimeSelectionService(scanner, store);

            await service.SelectAsync(21, "1.21.1");

            Assert.NotNull(scanner.LastRequest);
            Assert.Equal([java], scanner.LastRequest!.Executables);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
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
            return
            [
                new JavaRuntimeInfo(
                    "/custom/java",
                    "21.0.1",
                    21,
                    "Test",
                    "x64",
                    "manual"),
            ];
        }

        public IReadOnlyList<JavaRuntimeInfo> Scan(
            CancellationToken cancellationToken = default) =>
            Scan(JavaScanRequest.Default, cancellationToken);
    }
}
