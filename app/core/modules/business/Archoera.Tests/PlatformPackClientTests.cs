using Archoera.Infrastructure.Packs;
using Archoera.Infrastructure.Packs.Platform;

namespace Archoera.Tests;

public sealed class PlatformPackClientTests
{
    [Fact]
    public async Task Registry_exposes_all_providers_without_network()
    {
        using var http = new HttpClient(new FakeHttpMessageHandler());
        var service = new PlatformPackModuleService(
            new PlatformPackService(http: http));
        using var document = System.Text.Json.JsonDocument.Parse(
            await service.HandleAsync(
                "pack.platform.providers",
                "{}",
                CancellationToken.None));

        var items = document.RootElement
            .GetProperty("data")
            .GetProperty("items");
        Assert.Equal(3, items.GetArrayLength());
        Assert.Equal(
            ["ftb", "technic", "atlauncher"],
            items.EnumerateArray().Select(item => item.GetProperty("id").GetString()));
    }

    [Fact]
    public async Task Technic_maps_solder_mods_with_md5()
    {
        var handler = new FakeHttpMessageHandler();
        handler.EnqueueJson(
            """
            {"modpacks":[{"id":"1","name":"Tekkit","slug":"tekkit","url":"https://technic.example/tekkit","iconUrl":"https://cdn.example/icon.png"}]}
            """);
        using var http = new HttpClient(handler);
        var client = new TechnicPackClient(http, new Uri("https://technic.test/"));
        var (items, total) = await client.SearchAsync("tekkit", 0, 20, CancellationToken.None);
        Assert.Equal(1, total);
        Assert.Equal("tekkit", Assert.Single(items).Id);

        handler.EnqueueJson(
            """
            {"name":"Tekkit","displayName":"Tekkit","description":"desc","minecraft":"1.20.1","solder":"https://solder.test/"}
            """);
        handler.EnqueueJson(
            """
            {"minecraft":"1.20.1","forge":"47.2.0","mods":[
              {"name":"Example","url":"https://cdn.example/example.jar","filesize":123,"md5":"cccccccccccccccccccccccccccccccc"}
            ]}
            """);

        var index = await client.BuildIndexAsync("tekkit", "1.0.0", CancellationToken.None);

        Assert.Equal("1.20.1", index.MinecraftVersion);
        Assert.Equal("forge", index.Loader.Kind);
        Assert.Equal("47.2.0", index.Loader.Version);
        var file = Assert.Single(index.Files);
        Assert.Equal("mods/example.jar", file.Path);
        Assert.Equal("cccccccccccccccccccccccccccccccc", file.Md5);
    }

    [Fact]
    public async Task Technic_resolves_a_direct_zip_without_a_manifest()
    {
        var handler = new FakeHttpMessageHandler();
        handler.EnqueueJson(
            """
            {"name":"rlcraft","displayName":"RLCraft","description":"desc",
             "minecraft":"1.12.2","version":"2.9.3",
             "url":"https://cdn.example/rlcraft.zip","solder":null}
            """);
        handler.Enqueue(() => new HttpResponseMessage(System.Net.HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(new byte[194]),
        });
        using var http = new HttpClient(handler);
        var client = new TechnicPackClient(http, new Uri("https://technic.test/"));

        var archive = await client.ResolveArchiveAsync(
            "shivaxi-rlcraft",
            "2.9.3",
            CancellationToken.None);

        Assert.NotNull(archive);
        Assert.Equal("https://cdn.example/rlcraft.zip", archive!.Url);
        Assert.Equal("2.9.3", archive.VersionId);
        Assert.Equal("RLCraft", archive.Name);
        Assert.Equal("1.12.2", archive.GameVersion);
        Assert.Equal(194, archive.Size);
        Assert.Equal("HEAD", handler.Requests[1].Method);
    }

    [Fact]
    public async Task Technic_real_direct_zip_exposes_a_size()
    {
        if (Environment.GetEnvironmentVariable("ARCHOERACX_NETWORK_TESTS") != "1")
        {
            return;
        }

        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("ArchoeraCX/0.1.0+1 test");
        var client = new TechnicPackClient(http);
        var archive = await client.ResolveArchiveAsync(
            "shivaxi-rlcraft",
            "2.9.3",
            CancellationToken.None);
        Assert.NotNull(archive);
        Assert.StartsWith("https://", archive!.Url);
        Assert.Equal("1.12.2", archive.GameVersion);
        Assert.True(archive.Size > 1_000_000);
    }

    [Fact]
    public async Task Technic_uses_the_manifest_path_for_solder_packs()
    {
        var handler = new FakeHttpMessageHandler();
        handler.EnqueueJson(
            """
            {"displayName":"Tekkit","minecraft":"1.20.1","solder":"https://solder.test/"}
            """);
        using var http = new HttpClient(handler);
        var client = new TechnicPackClient(http, new Uri("https://technic.test/"));

        var archive = await client.ResolveArchiveAsync(
            "tekkit",
            "1.0.0",
            CancellationToken.None);

        Assert.Null(archive);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task Atlauncher_maps_graphql_config_json()
    {
        var handler = new FakeHttpMessageHandler();
        handler.EnqueueJson(
            """
            {"data":{"packVersion":{
              "version":"v1.0.0","minecraftVersion":"1.20.1",
              "pack":{"name":"ATL Pack","description":"desc"},
              "json":{
                "minecraft":"1.20.1",
                "loader":{"type":"FABRIC","metadata":{"version":"0.15.0"}},
                "mods":[{"name":"Example","file":"example.jar","url":"https://cdn.example/example.jar",
                         "md5":"dddddddddddddddddddddddddddddddd","filesize":123,
                         "client":true,"server":false,"type":"MODS","optional":false}]
              }}}}
            """);
        using var http = new HttpClient(handler);
        var client = new AtLauncherPackClient(http, new Uri("https://atl.test/graphql"));

        var index = await client.BuildIndexAsync("597", "v1.0.0", CancellationToken.None);

        Assert.Equal("ATL Pack", index.Name);
        Assert.Equal("1.20.1", index.MinecraftVersion);
        Assert.Equal("fabric", index.Loader.Kind);
        var file = Assert.Single(index.Files);
        Assert.Equal("mods/example.jar", file.Path);
        Assert.True(file.ServerUnsupported);
        Assert.Equal("dddddddddddddddddddddddddddddddd", file.Md5);
    }
}
