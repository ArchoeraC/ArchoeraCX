using Archoera.Domain.Resources;
using Archoera.Infrastructure.Packs.Platform;

namespace Archoera.Tests;

public sealed class FtbPackClientTests
{
    [Fact]
    public async Task Search_maps_the_public_pack_catalogue()
    {
        var handler = new FakeHttpMessageHandler();
        handler.EnqueueJson("""{"status":"success","packs":[130],"count":1}""");
        handler.EnqueueJson(
            """
            {"id":130,"name":"FTB StoneBlock 4","slug":"ftb-stoneblock-4","synopsis":"Stone world",
             "installs":123,"updated":1761664502,
             "authors":[{"name":"FTB Team"}],
             "art":[{"url":"https://cdn.example/icon.webp","type":"square"}]}
            """);
        using var http = new HttpClient(handler);
        var client = new FtbPackClient(http, new Uri("https://test.local/"));

        var (items, total) = await client.SearchAsync("stone", 0, 20, CancellationToken.None);

        Assert.Equal(1, total);
        var pack = Assert.Single(items);
        Assert.Equal("ftb", pack.Provider);
        Assert.Equal("130", pack.Id);
        Assert.Equal("FTB StoneBlock 4", pack.Name);
        Assert.Equal("FTB Team", pack.Author);
        Assert.Equal("https://cdn.example/icon.webp", pack.IconUrl);
        Assert.Contains("stone", handler.Requests[0].Uri.Query, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Versions_map_numeric_ids_and_targets()
    {
        var handler = new FakeHttpMessageHandler();
        handler.EnqueueJson(
            """
            {"name":"Pack","versions":[
              {"id":100140,"name":"1.0.0","type":"release","released":1761664502,
               "targets":[{"type":"game","name":"minecraft","version":"1.21.1"},
                          {"type":"modloader","name":"neoforge","version":"21.1.209"}]}
            ]}
            """);
        using var http = new HttpClient(handler);
        var client = new FtbPackClient(http, new Uri("https://test.local/"));

        var versions = await client.GetVersionsAsync("130", CancellationToken.None);

        var version = Assert.Single(versions);
        Assert.Equal("100140", version.VersionId);
        Assert.Equal("1.21.1", version.GameVersion);
        Assert.Equal("neoforge", version.Loader);
        Assert.Equal("21.1.209", version.LoaderVersion);
    }

    [Fact]
    public async Task BuildIndex_maps_files_paths_and_hashes()
    {
        var handler = new FakeHttpMessageHandler();
        handler.EnqueueJson(
            """
            {"name":"FTB Pack","synopsis":"Summary","versions":[{"id":1}]}
            """);
        handler.EnqueueJson(
            """
            {"name":"1.0.0","targets":[
               {"type":"game","name":"minecraft","version":"1.21.1"},
               {"type":"modloader","name":"fabric","version":"0.16.9"}],
             "files":[
               {"name":"mod.jar","path":"./mods","url":"https://cdn.example/mod.jar",
                "size":123,"hashes":{"sha1":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"},
                "clientonly":true,"serveronly":false,"optional":false}
             ]}
            """);
        using var http = new HttpClient(handler);
        var client = new FtbPackClient(http, new Uri("https://test.local/"));

        var index = await client.BuildIndexAsync("130", "1", CancellationToken.None);

        Assert.Equal("FTB Pack", index.Name);
        Assert.Equal("1.0.0", index.VersionId);
        Assert.Equal("1.21.1", index.MinecraftVersion);
        Assert.Equal("fabric", index.Loader.Kind);
        var file = Assert.Single(index.Files);
        Assert.Equal("mods/mod.jar", file.Path);
        Assert.Equal(123, file.Size);
        Assert.Equal("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", file.Sha1);
        Assert.True(file.ServerUnsupported);
        Assert.False(file.ClientUnsupported);
    }
}
