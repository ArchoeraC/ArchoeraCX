using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Archoera.Application.Mojang;
using Archoera.Domain.Mojang;
using Archoera.Infrastructure.Loaders;
using Archoera.Infrastructure.Modules;
using Archoera.Infrastructure.Settings;

namespace Archoera.Tests;

/// <summary>
/// Pure Java mode runs the vendor installer. These tests use a shell script in
/// place of a real JVM, so they only run on POSIX hosts.
/// </summary>
public sealed class ForgeInstallerRunnerTests
{
    [Fact]
    public async Task Run_passes_install_client_and_mirror()
    {
        if (OperatingSystem.IsWindows()) return;

        var directory = CreateDirectory("installer-run");
        try
        {
            var record = Path.Combine(directory, "args.txt");
            var java = WriteScript(
                directory,
                $"#!/bin/sh\nprintf '%s\\n' \"$@\" > '{record}'\nexit 0\n");
            var installer = Path.Combine(directory, "installer.jar");
            File.WriteAllBytes(
                installer,
                ForgeInstallerFixture.CreateZip(
                    ("version.json", """{"id":"1.12.2-forge-14.23.5.2860"}""")));

            var result = await new ForgeInstallerRunner().RunAsync(
                java,
                installer,
                directory,
                new Uri("https://bmclapi2.bangbang93.com/maven/"),
                TimeSpan.FromSeconds(30));

            Assert.Equal(0, result.ExitCode);
            var arguments = File.ReadAllLines(record);
            Assert.Contains("-jar", arguments);
            Assert.Contains(installer, arguments);
            Assert.Contains("--installClient", arguments);
            Assert.Contains(directory, arguments);
            Assert.Contains("--mirror", arguments);
            Assert.Contains("https://bmclapi2.bangbang93.com/maven/", arguments);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Install_java_imports_the_generated_profile()
    {
        if (OperatingSystem.IsWindows()) return;

        var directory = CreateDirectory("installer-import");
        try
        {
            const string versionId = "1.12.2-forge-14.23.5.2860";
            var java = WriteScript(
                directory,
                $$"""
                #!/bin/sh
                root="$4"
                mkdir -p "$root/versions/{{versionId}}"
                cat > "$root/versions/{{versionId}}/{{versionId}}.json" <<'JSON'
                {"id":"{{versionId}}","inheritsFrom":"1.12.2","mainClass":"net.minecraft.launchwrapper.Launch","libraries":[]}
                JSON
                exit 0
                """);
            var installer = Path.Combine(directory, "installer.jar");
            File.WriteAllBytes(
                installer,
                ForgeInstallerFixture.CreateZip(
                    ("version.json", $$"""{"id":"{{versionId}}","inheritsFrom":"1.12.2"}""")));

            var dataRoot = Path.Combine(directory, "data");
            Directory.CreateDirectory(dataRoot);
            var region = new RegionPolicy(regionOverride: "GLOBAL");
            var service = new LoaderModuleService(
                policy: new DownloadPolicy(dataRoot, region),
                settings: new JsonAppSettingsStore(dataRoot),
                regionPolicy: region,
                mojang: new FakeMojang());

            var payload = JsonSerializer.Serialize(new
            {
                root = directory,
                installerPath = installer,
                kind = "forge",
                gameVersion = "1.12.2",
                loaderVersion = "14.23.5.2860",
                javaPath = java,
            });
            var bytes = await service.HandleAsync(
                "loader.installJava",
                payload,
                CancellationToken.None);
            using var document = JsonDocument.Parse(bytes);
            var root = document.RootElement;
            Assert.True(root.GetProperty("ok").GetBoolean(), root.GetRawText());

            var data = root.GetProperty("data");
            Assert.Equal(versionId, data.GetProperty("versionId").GetString());
            var profilePath = data.GetProperty("profilePath").GetString();
            Assert.NotNull(profilePath);
            Assert.True(File.Exists(profilePath));

            using var profile = JsonDocument.Parse(File.ReadAllText(profilePath!));
            Assert.False(profile.RootElement.TryGetProperty("inheritsFrom", out _));
            Assert.Equal(
                "net.minecraft.launchwrapper.Launch",
                profile.RootElement.GetProperty("mainClass").GetString());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static string CreateDirectory(string name)
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "archoera-installer-" + name + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static string WriteScript(string directory, string content)
    {
        var path = Path.Combine(directory, "fake-java");
        File.WriteAllText(path, content);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(
                path,
                UnixFileMode.UserRead |
                UnixFileMode.UserWrite |
                UnixFileMode.UserExecute);
        }

        return path;
    }

    private sealed class FakeMojang : IMojangMetadataClient
    {
        private static readonly string VanillaJson =
            $$"""
              {
                "id": "1.12.2",
                "mainClass": "net.minecraft.client.main.Main",
                "assets": "1.12",
                "assetIndex": {
                  "id": "1.12",
                  "url": "https://piston-meta.mojang.com/v1/packages/index/1.12.json",
                  "sha1": "{{MojangTestData.Sha1('8')}}",
                  "size": 500,
                  "totalSize": 2000
                },
                "downloads": {
                  "client": {
                    "url": "https://piston-data.mojang.com/v1/objects/client/client.jar",
                    "sha1": "{{MojangTestData.Sha1('1')}}",
                    "size": 1000
                  }
                },
                "libraries": []
              }
              """;

        public Task<MojangVersionManifest> GetManifestAsync(
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<MojangVersion> GetVersionAsync(
            string versionId,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<byte[]> GetVersionJsonAsync(
            string versionId,
            CancellationToken cancellationToken = default)
            => Task.FromResult(Encoding.UTF8.GetBytes(VanillaJson));

        public Task<MojangAssetIndex> GetAssetIndexAsync(
            MojangAssetIndexRef index,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }
}
