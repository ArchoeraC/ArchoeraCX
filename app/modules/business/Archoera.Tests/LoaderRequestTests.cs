using Archoera.Application.Loaders;
using Archoera.Domain.Loaders;

namespace Archoera.Tests;

public sealed class LoaderRequestTests
{
    private static string Absolute(string name)
        => Path.GetFullPath(Path.Combine(Path.GetTempPath(), "archoera-requests", name));

    [Fact]
    public void TryParse_accepts_forge_verify_request_and_defaults_libraries()
    {
        var root = Absolute("instance");
        var installer = Absolute("forge-installer.jar");

        var ok = LoaderRequestJson.TryParse(
            $$"""{"installerPath":"{{installer}}","root":"{{root}}","kind":"forge"}""",
            out var request,
            out var code,
            out var message);

        Assert.True(ok);
        Assert.Equal(string.Empty, code);
        Assert.Equal(string.Empty, message);
        Assert.NotNull(request);
        Assert.Equal(Path.GetFullPath(installer), request.InstallerPath);
        Assert.Equal(Path.GetFullPath(root), request.Root);
        Assert.Equal("libraries", request.LibrariesPath);
        Assert.Equal(LoaderKind.Forge, request.Kind);
        Assert.Null(request.JavaPath);
    }

    [Fact]
    public void TryParse_accepts_neoforge_repair_request_with_java()
    {
        var root = Absolute("instance");
        var installer = Absolute("neoforge-installer.jar");
        var java = Absolute("bin/java");
        var libraries = "game/libraries";

        var ok = LoaderRequestJson.TryParse(
            $$"""{"installerPath":"{{installer}}","root":"{{root}}","librariesPath":"{{libraries}}","kind":"neoforge","javaPath":"{{java}}"}""",
            out var request,
            out _,
            out _);

        Assert.True(ok);
        Assert.NotNull(request);
        Assert.Equal("game/libraries", request.LibrariesPath);
        Assert.Equal(LoaderKind.NeoForge, request.Kind);
        Assert.Equal(Path.GetFullPath(java), request.JavaPath);
    }

    [Theory]
    [InlineData("relative/installer.jar", "forge")]
    [InlineData("__ABS__", "fabric")]
    [InlineData("__ABS__", "forge", "__ABS_LIBS__")]
    public void TryParse_rejects_relative_or_unknown_inputs(
        string installer,
        string kind,
        string? libraries = null)
    {
        var root = Absolute("instance");
        installer = installer == "__ABS__" ? Absolute("installer.jar") : installer;
        libraries = libraries == "__ABS_LIBS__" ? Absolute("libraries") : libraries;
        var librariesJson = libraries is null
            ? string.Empty
            : $",\"librariesPath\":\"{libraries}\"";
        var json =
            $$"""{"installerPath":"{{installer}}","root":"{{root}}","kind":"{{kind}}"{{librariesJson}}}""";

        var ok = LoaderRequestJson.TryParse(json, out var request, out var code, out _);

        Assert.False(ok);
        Assert.Null(request);
        Assert.Equal("E_INVALID", code);
    }
}
