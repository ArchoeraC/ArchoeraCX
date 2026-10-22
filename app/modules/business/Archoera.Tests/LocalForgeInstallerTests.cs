using Archoera.Domain.Instances;
using Archoera.Domain.Loaders;
using Archoera.Infrastructure.Packs;

namespace Archoera.Tests;

/// <summary>
/// Launch repair prefers an installer that another launcher already put in the
/// shared Maven cache (or a sibling instance) over downloading it again.
/// </summary>
public sealed class LocalForgeInstallerTests
{
    [Fact]
    public void Finds_a_shared_NeoForge_installer()
    {
        var root = CreateRoot();
        try
        {
            var instance = Instance("neoforge", "21.1.248", "1.21.1", root);
            var installer = Path.Combine(
                root,
                "libraries",
                "net",
                "neoforged",
                "neoforge",
                "21.1.248-installer",
                "neoforge-21.1.248-installer.jar");
            WriteZip(installer);

            Assert.Equal(
                installer,
                PackInstallService.FindLocalForgeInstaller(
                    instance,
                    LoaderKind.NeoForge));
        }
        finally
        {
            Delete(root);
        }
    }

    [Fact]
    public void Finds_an_installer_in_the_instance_download_folder()
    {
        var root = CreateRoot();
        try
        {
            var instance = Instance("neoforge", "21.1.248", "1.21.1", root);
            var installer = Path.Combine(
                instance.RootPath,
                "downloads",
                "loaders",
                "neoforge",
                "1.21.1-21.1.248-installer.jar");
            WriteZip(installer);

            Assert.Equal(
                installer,
                PackInstallService.FindLocalForgeInstaller(
                    instance,
                    LoaderKind.NeoForge));
        }
        finally
        {
            Delete(root);
        }
    }

    [Fact]
    public void Finds_a_shared_Forge_installer()
    {
        var root = CreateRoot();
        try
        {
            var instance = Instance("forge", "47.4.18", "1.20.1", root);
            var installer = Path.Combine(
                root,
                "libraries",
                "net",
                "minecraftforge",
                "forge",
                "47.4.18-installer",
                "forge-47.4.18-installer.jar");
            WriteZip(installer);

            Assert.Equal(
                installer,
                PackInstallService.FindLocalForgeInstaller(instance, LoaderKind.Forge));
        }
        finally
        {
            Delete(root);
        }
    }

    [Fact]
    public void Ignores_a_non_zip_file()
    {
        var root = CreateRoot();
        try
        {
            var instance = Instance("neoforge", "21.1.248", "1.21.1", root);
            var installer = Path.Combine(
                root,
                "libraries",
                "net",
                "neoforged",
                "neoforge",
                "21.1.248-installer",
                "neoforge-21.1.248-installer.jar");
            Directory.CreateDirectory(Path.GetDirectoryName(installer)!);
            File.WriteAllText(installer, "not a zip");

            Assert.Null(
                PackInstallService.FindLocalForgeInstaller(
                    instance,
                    LoaderKind.NeoForge));
        }
        finally
        {
            Delete(root);
        }
    }

    private static GameInstanceRecord Instance(
        string loader,
        string loaderVersion,
        string gameVersion,
        string root)
    {
        var path = Path.Combine(root, "versions", "MyPack");
        Directory.CreateDirectory(path);
        return new GameInstanceRecord(
            "id",
            "MyPack",
            gameVersion,
            loader,
            loaderVersion,
            path,
            0,
            false,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            null);
    }

    private static string CreateRoot()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "archoera-local-installer",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "libraries"));
        Directory.CreateDirectory(Path.Combine(root, "assets"));
        Directory.CreateDirectory(Path.Combine(root, "versions"));
        return root;
    }

    private static void WriteZip(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, [0x50, 0x4B, 0x03, 0x04]);
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
}
