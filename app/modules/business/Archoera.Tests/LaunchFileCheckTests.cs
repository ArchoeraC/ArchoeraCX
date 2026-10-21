using Archoera.Domain.Mojang;
using Archoera.Infrastructure.Launch;

namespace Archoera.Tests;

public sealed class LaunchFileCheckTests
{
    [Fact]
    public void Reports_libraries_then_client_then_asset_index()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "archoera-launch-files",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var root = Path.Combine(directory, "instance");
            Directory.CreateDirectory(Path.Combine(root, "libraries"));
            Directory.CreateDirectory(Path.Combine(root, "assets", "indexes"));
            var paths = LaunchPaths.Resolve(root);
            var version = MojangTestData.Version();

            var missingLibraries = LaunchPlanBuilder.CheckFiles(version, paths);
            Assert.False(missingLibraries.Ok);
            Assert.Equal("LIBRARIES_MISSING", missingLibraries.Code);

            foreach (var relative in new[]
                     {
                         MojangTestData.CommonPath,
                         MojangTestData.LinuxPath,
                         MojangTestData.X64Path,
                         MojangTestData.NativePath,
                         "libraries/org/example/natives/1.0/natives-1.0.jar",
                     })
            {
                var full = Path.Combine(root, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(full)!);
                File.WriteAllText(full, "jar");
            }

            var missingClient = LaunchPlanBuilder.CheckFiles(version, paths);
            Assert.False(missingClient.Ok);
            Assert.Equal("CLIENT_JAR_MISSING", missingClient.Code);

            File.WriteAllText(
                Path.Combine(root, $"{MojangTestData.VersionId}.jar"),
                "client");
            var missingIndex = LaunchPlanBuilder.CheckFiles(version, paths);
            Assert.False(missingIndex.Ok);
            Assert.Equal("ASSET_INDEX_MISSING", missingIndex.Code);

            var hash = MojangTestData.Sha1('a');
            File.WriteAllText(
                Path.Combine(root, "assets", "indexes", "5.json"),
                "{\"objects\":{\"icons/a.png\":{\"hash\":\"" + hash + "\"}}}");
            var missingAsset = LaunchPlanBuilder.CheckFiles(version, paths);
            Assert.False(missingAsset.Ok);
            Assert.Equal("ASSET_MISSING", missingAsset.Code);

            var objectPath = Path.Combine(
                root,
                "assets",
                "objects",
                hash[..2],
                hash);
            Directory.CreateDirectory(Path.GetDirectoryName(objectPath)!);
            File.WriteAllText(objectPath, "asset");
            Assert.True(LaunchPlanBuilder.CheckFiles(version, paths).Ok);
        }
        finally
        {
            try
            {
                Directory.Delete(directory, recursive: true);
            }
            catch
            {
                // best effort
            }
        }
    }
}
