using System.Security.Cryptography;
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
    public void Finds_a_Forge_installer_when_the_instance_stores_the_profile_id()
    {
        var root = CreateRoot();
        try
        {
            var installer = Path.Combine(
                root,
                "libraries",
                "net",
                "minecraftforge",
                "forge",
                "47.4.18-installer",
                "forge-47.4.18-installer.jar");
            WriteZip(installer);

            // SJMCL/LauncherX imports keep "1.20.1-forge-47.4.18" in the
            // loader version; the installer path uses the bare build.
            var instance = Instance("forge", "1.20.1-forge-47.4.18", "1.20.1", root);

            Assert.Equal(
                installer,
                PackInstallService.FindLocalForgeInstaller(
                    instance,
                    LoaderKind.Forge));
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

    [Fact]
    public void Accepts_an_installer_with_a_matching_sidecar()
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
            File.WriteAllText(
                installer + ".sha1",
                Sha1(File.ReadAllBytes(installer)) + "\n");

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
    public void Rejects_an_installer_tampered_after_its_sidecar_was_written()
    {
        var root = CreateRoot();
        try
        {
            var instance = Instance("forge", "47.4.18", "1.20.1", root);
            var installer = Path.Combine(
                instance.RootPath,
                "downloads",
                "loaders",
                "forge",
                "1.20.1-47.4.18-installer.jar");
            WriteZip(installer);
            File.WriteAllText(
                installer + ".sha1",
                Sha1(File.ReadAllBytes(installer)) + "\n");

            // Replace the bytes after the digest was recorded: the installer
            // still starts with PK, so only the sidecar can expose this.
            File.WriteAllBytes(installer, [0x50, 0x4B, 0x03, 0x04, 0xFF, 0x00]);

            Assert.Null(
                PackInstallService.FindLocalForgeInstaller(instance, LoaderKind.Forge));
        }
        finally
        {
            Delete(root);
        }
    }

    [Fact]
    public void Rejects_an_installer_with_a_malformed_sidecar()
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
            File.WriteAllText(installer + ".sha1", "not-a-digest\n");

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

    private static string Sha1(byte[] bytes)
        => Convert.ToHexStringLower(SHA1.HashData(bytes));

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
