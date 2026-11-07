using Archoera.Application.Packs;
using Archoera.Domain.Downloads;

namespace Archoera.Tests;

public sealed class ModpackPlanBuilderTests
{
    [Fact]
    public void Build_skips_bundled_and_server_unsupported_files()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "archoera-pack-plan",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var index = new ModpackIndex(
                1,
                "minecraft",
                "v1",
                "Plan test",
                string.Empty,
                [
                    File("mods/bundled.jar", client: false, server: false),
                    File("config/client-only.json", client: true, server: false),
                    File("mods/fabric-api.jar", client: false, server: false),
                    File("mods/sodium.jar", client: false, server: true),
                ],
                new Dictionary<string, string>
                {
                    ["minecraft"] = "1.21.1",
                    ["fabric-loader"] = "0.19.3",
                },
                new ModpackLoader("fabric", "0.19.3"));

            var plan = ModpackPlanBuilder.Build(
                root,
                index,
                ["mods/bundled.jar"],
                clientSide: true);

            Assert.Equal(
                ["mods/fabric-api.jar", "mods/sodium.jar"],
                plan.Files.Select(file => file.Dest).ToArray());
            Assert.Equal(2, plan.Files.Count);
            Assert.All(plan.Files, file =>
                Assert.Equal(DownloadFileMode.Ensure, file.Mode));
            var fabric = plan.Files[0];
            Assert.Equal(DownloadHashAlgorithm.Sha512, fabric.Hash.Algorithm);
            Assert.Single(fabric.Mirrors);
        }
        finally
        {
            try
            {
                Directory.Delete(root, recursive: true);
            }
            catch
            {
                // best effort
            }
        }
    }

    [Fact]
    public void Build_uses_sha1_when_sha512_is_absent()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "archoera-pack-plan",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var index = new ModpackIndex(
                1,
                "minecraft",
                "v1",
                "Plan test",
                string.Empty,
                [File("mods/a.jar", client: false, server: false, sha512: false)],
                new Dictionary<string, string> { ["minecraft"] = "1.21.1" },
                new ModpackLoader("vanilla", string.Empty));

            var plan = ModpackPlanBuilder.Build(root, index);
            Assert.Single(plan.Files);
            Assert.Equal(DownloadHashAlgorithm.Sha1, plan.Files[0].Hash.Algorithm);
        }
        finally
        {
            try
            {
                Directory.Delete(root, recursive: true);
            }
            catch
            {
                // best effort
            }
        }
    }

    private static ModpackFile File(
        string path,
        bool client,
        bool server,
        bool sha512 = true)
        => new(
            path,
            new string('a', 40),
            sha512 ? new string('b', 128) : null,
            10,
            [
                $"https://cdn.example.invalid/{path}",
                $"https://mirror.example.invalid/{path}",
            ],
            client,
            server);
}
