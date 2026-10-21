using Archoera.Application.Modules;
using Archoera.Infrastructure.Modules;

namespace Archoera.Tests;

public sealed class ModuleRegistryTests
{
    [Fact]
    public void Default_registry_registers_each_method_exactly_once()
    {
        var directory = TempDirectory();
        try
        {
            var registry = BusinessModuleRegistry.CreateDefault(directory);

            Assert.Contains("java.scan", registry.Methods);
            Assert.Contains("java.select", registry.Methods);
            Assert.Contains("loader.versions", registry.Methods);
            Assert.Contains("loader.verify", registry.Methods);
            Assert.Contains("loader.repair", registry.Methods);
            Assert.Contains("loader.check", registry.Methods);
            Assert.Contains("loader.plan", registry.Methods);
            Assert.Contains("loader.commit", registry.Methods);
            Assert.Contains("version.list", registry.Methods);
            Assert.Contains("version.plan", registry.Methods);
            Assert.Contains("version.commit", registry.Methods);
            Assert.Contains("launch.start", registry.Methods);
            Assert.Contains("launch.prepare", registry.Methods);
            Assert.Contains("resource.search", registry.Methods);
            Assert.Contains("resource.project", registry.Methods);
            Assert.Contains("settings.get", registry.Methods);
            Assert.Contains("instance.list", registry.Methods);
            Assert.Contains("instance.sync", registry.Methods);
            Assert.Contains("instance.mods.list", registry.Methods);
            Assert.Contains("instance.mods.setEnabled", registry.Methods);
            Assert.Contains("instance.mods.remove", registry.Methods);
            Assert.Contains("account.login.microsoft.start", registry.Methods);
            Assert.Contains("account.login.microsoft.poll", registry.Methods);
            Assert.Contains("account.list", registry.Methods);
            Assert.Contains("account.login.offline", registry.Methods);
            Assert.DoesNotContain("account.login.microsoft.unknown", registry.Methods);
            Assert.Equal(
                registry.Methods.Count,
                registry.Methods.Distinct(StringComparer.Ordinal).Count());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Duplicate_registration_is_rejected_at_startup()
    {
        var exception = Assert.Throws<InvalidOperationException>(
            () => new ModuleMethodRegistry([new FakeHandler(["x.y"]), new FakeHandler(["x.y"])]));
        Assert.Contains("x.y", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Unknown_method_throws_before_any_handler_runs()
    {
        var handler = new FakeHandler(["x.y"]);
        var registry = new ModuleMethodRegistry([handler]);
        Assert.False(registry.Supports("x.z"));
        await Assert.ThrowsAsync<NotSupportedException>(
            () => registry.HandleAsync("x.z", "{}", CancellationToken.None));
        Assert.Equal(0, handler.Calls);
    }

    private sealed class FakeHandler : IModuleMethodHandler
    {
        public FakeHandler(IReadOnlyCollection<string> methods)
        {
            Methods = methods;
        }

        public IReadOnlyCollection<string> Methods { get; }
        public int Calls { get; private set; }

        public Task<byte[]> HandleAsync(
            string method,
            string? paramsJson,
            CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult("{}"u8.ToArray());
        }
    }

    private static string TempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "archoera-registry-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
