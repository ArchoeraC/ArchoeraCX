using System.Net;
using System.Text;
using Archoera.Application.JavaRuntime;
using Archoera.Domain.Downloads;
using Archoera.Infrastructure.Java.Distributions;

namespace Archoera.Tests;

public sealed partial class JavaDistributionTests
{
    [Fact]
    public async Task Adoptium_lists_majors_and_resolves_an_archive()
    {
        var handler = new FakeHttpMessageHandler();
        handler.EnqueueJson("""{"available_releases":[8,17,21,25],"most_recent_lts":25}""");
        handler.EnqueueJson($"[{AdoptiumReleaseJson()}]");
        handler.EnqueueJson(AdoptiumReleaseJson());

        var provider = new AdoptiumJavaDistributionProvider(
            new HttpClient(handler),
            new Uri("https://adoptium.test/"));
        var platform = new JavaDistributionPlatform("linux", "x64", "tar.gz");

        var majors = await provider.GetMajorsAsync(platform, CancellationToken.None);
        Assert.Equal([8, 17, 21, 25], majors);

        var releases = await provider.GetReleasesAsync(21, platform, CancellationToken.None);
        var release = Assert.Single(releases);
        Assert.Equal("jdk-21.0.13+9", release.VersionId);
        Assert.Equal("21.0.13+9", release.Version);
        Assert.True(release.Recommended);

        var archive = await provider.ResolveAsync(release.VersionId, platform, CancellationToken.None);
        Assert.Equal(21, archive.Major);
        Assert.Equal(207858352, archive.Size);
        Assert.StartsWith("https://", archive.Url);
        Assert.Equal(64, archive.Hash.Length);
        Assert.Equal("tar.gz", archive.ArchiveType);
    }

    [Fact]
    public async Task Zulu_resolves_the_uuid_to_a_checksummed_archive()
    {
        var handler = new FakeHttpMessageHandler();
        handler.EnqueueJson(
            """
            [{"name":"zulu21.54.109-ca-jdk21.0.13.0.3-linux_x64.tar.gz",
              "java_version":[21,0,13,0,3],
              "package_uuid":"3a7c000f-2eb1-4e4c-b2d0-b9680efe2b95"}]
            """);
        handler.EnqueueJson(
            """
            {"name":"zulu21.54.109-ca-jdk21.0.13.0.3-linux_x64.tar.gz",
             "java_version":[21,0,13,0,3],
             "download_url":"https://cdn.azul.com/zulu.tar.gz",
             "sha256_hash":"abe544103f1c4f0cc4b8997c0cac847eb1e842382eef57ce361c982fb0a3de11",
             "size":213609000,
             "archive_type":"tar.gz"}
            """);

        var provider = new ZuluJavaDistributionProvider(
            new HttpClient(handler),
            new Uri("https://zulu.test/"));
        var platform = new JavaDistributionPlatform("linux", "x64", "tar.gz");
        var releases = await provider.GetReleasesAsync(21, platform, CancellationToken.None);
        var release = Assert.Single(releases);
        Assert.Equal("21.0.13.0.3", release.Version);

        var archive = await provider.ResolveAsync(release.VersionId, platform, CancellationToken.None);
        Assert.Equal(213609000, archive.Size);
        Assert.Equal("https://cdn.azul.com/zulu.tar.gz", archive.Url);
    }

    [Fact]
    public async Task Microsoft_filters_the_page_to_the_host_and_reads_the_checksum_file()
    {
        var handler = new FakeHttpMessageHandler();
        handler.Enqueue(() => Html(
            "microsoft-jdk-21.0.13.0.2-linux-x64.tar.gz " +
            "microsoft-jdk-21.0.13.0.2-windows-x64.zip " +
            "microsoft-jdk-25-linux-x64.tar.gz"));
        handler.Enqueue(() => Html(
            "microsoft-jdk-21.0.13.0.2-linux-x64.tar.gz " +
            "microsoft-jdk-21.0.13.0.2-windows-x64.zip"));
        handler.Enqueue(() => Html(
            "microsoft-jdk-21.0.13.0.2-linux-x64.tar.gz " +
            "microsoft-jdk-21.0.13.0.2-windows-x64.zip"));
        handler.Enqueue(() => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                "5bf17d27de3fe9d102981236cdea68482e3b5ef68a44ad21c6439b45d7d34da2  archive\n",
                Encoding.UTF8,
                "text/plain"),
        });
        handler.Enqueue(() =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent([]),
            };
            response.Content.Headers.ContentLength = 206928497;
            return response;
        });

        var provider = new MicrosoftJavaDistributionProvider(
            new HttpClient(handler),
            new Uri("https://microsoft.test/download"));
        var platform = new JavaDistributionPlatform("linux", "x64", "tar.gz");

        var majors = await provider.GetMajorsAsync(platform, CancellationToken.None);
        Assert.Equal([21], majors);

        var releases = await provider.GetReleasesAsync(21, platform, CancellationToken.None);
        var release = Assert.Single(releases);
        Assert.Equal("21.0.13.0.2", release.Version);

        var archive = await provider.ResolveAsync(release.VersionId, platform, CancellationToken.None);
        Assert.Equal(206928497, archive.Size);
        Assert.Equal("5bf17d27de3fe9d102981236cdea68482e3b5ef68a44ad21c6439b45d7d34da2", archive.Hash);
        Assert.Equal("tar.gz", archive.ArchiveType);
    }

    [Fact]
    public async Task GraalVm_resolves_the_release_asset_and_its_sha_file()
    {
        var handler = new FakeHttpMessageHandler();
        handler.EnqueueJson(GraalVmReleasesJson());
        handler.EnqueueJson(GraalVmReleaseJson());
        handler.Enqueue(() => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                "05ccbbe783210b6886ff7b08fcd0b061c5dce4852b05db87284fc0e24abb08e2\n",
                Encoding.UTF8,
                "text/plain"),
        });

        var provider = new GraalVmJavaDistributionProvider(
            new HttpClient(handler),
            new Uri("https://graal.test/"));
        var platform = new JavaDistributionPlatform("linux", "x64", "tar.gz");

        var majors = await provider.GetMajorsAsync(platform, CancellationToken.None);
        Assert.Equal([25], majors);

        var releases = await provider.GetReleasesAsync(25, platform, CancellationToken.None);
        var release = Assert.Single(releases);
        Assert.Equal("25.0.4.1.1", release.Version);

        var archive = await provider.ResolveAsync(release.VersionId, platform, CancellationToken.None);
        Assert.Equal(349158971, archive.Size);
        Assert.Equal("05ccbbe783210b6886ff7b08fcd0b061c5dce4852b05db87284fc0e24abb08e2", archive.Hash);
    }

    [Fact]
    public async Task Corretto_reads_the_redirect_version_size_and_sha()
    {
        var handler = new FakeHttpMessageHandler();
        handler.Enqueue(() =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent([]),
                RequestMessage = new HttpRequestMessage(
                    HttpMethod.Head,
                    new Uri(
                        "https://corretto.aws/downloads/resources/21.0.13.0.11/" +
                        "amazon-corretto-21.0.13.0.11-linux-x64.tar.gz")),
            };
            response.Content.Headers.ContentLength = 209244001;
            return response;
        });
        handler.Enqueue(() => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                "f3330bf440b8f4514b7b279b6145d4b4493dea9dda5f3a702b49c53c41bec1e4\n",
                Encoding.UTF8,
                "text/plain"),
        });

        var provider = new CorrettoJavaDistributionProvider(new HttpClient(handler));
        var platform = new JavaDistributionPlatform("linux", "x64", "tar.gz");
        var majors = await provider.GetMajorsAsync(platform, CancellationToken.None);
        Assert.Contains(21, majors);

        var release = Assert.Single(
            await provider.GetReleasesAsync(21, platform, CancellationToken.None));
        Assert.Equal("21.0.13.0.11", release.Version);
        Assert.Equal(209244001, release.Size);
        Assert.Equal(
            "amazon-corretto-21-x64-linux-jdk.tar.gz",
            release.VersionId);
    }

    [Fact]
    public async Task Liberica_lists_ga_versions_and_resolves_sha1()
    {
        var handler = new FakeHttpMessageHandler();
        handler.EnqueueJson("""[{"GA":true,"featureVersion":21}]""");
        const string releases = """
            [{"version":"21.0.13+11",
              "downloadUrl":"https://github.com/bell-sw/Liberica/releases/download/21.0.13%2B11/bellsoft-jdk21.0.13+11-linux-amd64.tar.gz",
              "size":231624041,
              "sha1":"080b896287b2b04e9a59f0bed888a376e5ea9f7a",
              "GA":true,
              "latestInFeatureVersion":true}]
            """;
        handler.EnqueueJson(releases);
        handler.EnqueueJson(releases);

        var provider = new LibericaJavaDistributionProvider(new HttpClient(handler));
        var platform = new JavaDistributionPlatform("linux", "x64", "tar.gz");
        var majors = await provider.GetMajorsAsync(platform, CancellationToken.None);
        Assert.Equal([21], majors);

        var release = Assert.Single(
            await provider.GetReleasesAsync(21, platform, CancellationToken.None));
        Assert.Equal("21.0.13+11", release.Version);

        var archive = await provider.ResolveAsync(
            release.VersionId,
            platform,
            CancellationToken.None);
        Assert.Equal(231624041, archive.Size);
        Assert.Equal(DownloadHashAlgorithm.Sha1, archive.HashAlgorithm);
        Assert.Equal(
            "080b896287b2b04e9a59f0bed888a376e5ea9f7a",
            archive.Hash);
    }

    [Fact]
    public async Task SapMachine_parses_ga_assets_and_resolves_the_sha_file()
    {
        var handler = new FakeHttpMessageHandler();
        handler.EnqueueJson(SapMachineReleasesJson());
        handler.EnqueueJson(SapMachineReleaseJson());
        handler.Enqueue(() => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                "1111111111111111111111111111111111111111111111111111111111111111\n",
                Encoding.UTF8,
                "text/plain"),
        });

        var provider = new SapMachineJavaDistributionProvider(
            new HttpClient(handler),
            new Uri("https://sap.test/"));
        var platform = new JavaDistributionPlatform("linux", "x64", "tar.gz");
        var majors = await provider.GetMajorsAsync(platform, CancellationToken.None);
        Assert.Equal([21], majors);

        var release = Assert.Single(
            await provider.GetReleasesAsync(21, platform, CancellationToken.None));
        Assert.Equal("21.0.13.0.3", release.Version);
        var archive = await provider.ResolveAsync(
            release.VersionId,
            platform,
            CancellationToken.None);
        Assert.Equal(211635320, archive.Size);
        Assert.Equal(64, archive.Hash.Length);
    }

    [Fact]
    public async Task Dragonwell_parses_standard_assets_and_resolves_the_sha_file()
    {
        var handler = new FakeHttpMessageHandler();
        handler.EnqueueJson(DragonwellReleasesJson());
        handler.EnqueueJson(DragonwellReleaseJson());
        handler.Enqueue(() => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                "2222222222222222222222222222222222222222222222222222222222222222\n",
                Encoding.UTF8,
                "text/plain"),
        });

        var provider = new DragonwellJavaDistributionProvider(new HttpClient(handler));
        var platform = new JavaDistributionPlatform("linux", "x64", "tar.gz");
        var release = Assert.Single(
            await provider.GetReleasesAsync(21, platform, CancellationToken.None));
        Assert.Equal("21.0.12.0.12.8", release.Version);
        var archive = await provider.ResolveAsync(
            release.VersionId,
            platform,
            CancellationToken.None);
        Assert.Equal(207635346, archive.Size);
        Assert.Equal(64, archive.Hash.Length);
    }


    private static HttpResponseMessage Html(string body) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(body, Encoding.UTF8, "text/html"),
    };

    private static string SapMachineReleasesJson() =>
        """
        [{"tag_name":"sapmachine-21.0.13.0.3",
          "published_at":"2026-10-20T00:00:00Z",
          "assets":[
            {"name":"sapmachine-jdk-21.0.13.0.3_linux-x64_bin.tar.gz",
             "size":211635320,
             "browser_download_url":"https://github.com/sap.tar.gz"},
            {"name":"sapmachine-jdk-21.0.13.0.3_linux-x64_bin.sha256.txt",
             "size":64,
             "browser_download_url":"https://github.com/sap.tar.gz.sha256"}
          ]}]
        """;

    private static string SapMachineReleaseJson() =>
        """
        {"tag_name":"sapmachine-21.0.13.0.3",
         "assets":[
           {"name":"sapmachine-jdk-21.0.13.0.3_linux-x64_bin.tar.gz",
            "size":211635320,
            "browser_download_url":"https://github.com/sap.tar.gz"},
           {"name":"sapmachine-jdk-21.0.13.0.3_linux-x64_bin.sha256.txt",
            "size":64,
            "browser_download_url":"https://github.com/sap.tar.gz.sha256"}
         ]}
        """;

    private static string DragonwellReleasesJson() =>
        """
        [{"tag_name":"dragonwell-standard-21.0.12.0.12+8_jdk-21.0.12-ga",
          "published_at":"2026-10-20T00:00:00Z",
          "assets":[
            {"name":"Alibaba_Dragonwell_Standard_21.0.12.0.12.8_x64_linux.tar.gz",
             "size":207635346,
             "browser_download_url":"https://github.com/dragonwell.tar.gz"},
            {"name":"Alibaba_Dragonwell_Standard_21.0.12.0.12.8_x64_linux.tar.gz.sha256.txt",
             "size":64,
             "browser_download_url":"https://github.com/dragonwell.tar.gz.sha256"}
          ]}]
        """;

    private static string DragonwellReleaseJson() =>
        """
        {"tag_name":"dragonwell-standard-21.0.12.0.12+8_jdk-21.0.12-ga",
         "assets":[
           {"name":"Alibaba_Dragonwell_Standard_21.0.12.0.12.8_x64_linux.tar.gz",
            "size":207635346,
            "browser_download_url":"https://github.com/dragonwell.tar.gz"},
           {"name":"Alibaba_Dragonwell_Standard_21.0.12.0.12.8_x64_linux.tar.gz.sha256.txt",
            "size":64,
            "browser_download_url":"https://github.com/dragonwell.tar.gz.sha256"}
         ]}
        """;

    private static string AdoptiumReleaseJson() =>
        """
        {
          "release_name":"jdk-21.0.13+9",
          "timestamp":"2026-10-22T08:58:06Z",
          "version_data":{"major":21,"openjdk_version":"21.0.13+9-LTS"},
          "binaries":[
            {"os":"linux","architecture":"x64","image_type":"jdk",
             "package":{"name":"OpenJDK21U-jdk_x64_linux_hotspot.tar.gz",
                        "link":"https://github.com/OpenJDK21U-jdk_x64_linux_hotspot.tar.gz",
                        "checksum":"d7f5d5cf865a2902b70ff549b97586a7d3f234e016f041ab447ea740c7d66d1c",
                        "size":207858352}}
          ]
        }
        """;

    private static string GraalVmReleasesJson() =>
        """
        [{"tag_name":"graal-25.4.4.1.1",
          "published_at":"2026-09-22T12:43:35Z",
          "assets":[
            {"name":"graalvm-community-jdk-25i4-25.0.4.1.1_linux-x64_bin.tar.gz",
             "size":349158971,
             "browser_download_url":"https://github.com/graalvm.tar.gz"},
            {"name":"graalvm-community-jdk-25i4-25.0.4.1.1_linux-x64_bin.tar.gz.sha256",
             "size":64,
             "browser_download_url":"https://github.com/graalvm.tar.gz.sha256"}
          ]}]
        """;

    private static string GraalVmReleaseJson() =>
        """
        {"tag_name":"graal-25.4.4.1.1",
         "assets":[
           {"name":"graalvm-community-jdk-25i4-25.0.4.1.1_linux-x64_bin.tar.gz",
            "size":349158971,
            "browser_download_url":"https://github.com/graalvm.tar.gz"},
           {"name":"graalvm-community-jdk-25i4-25.0.4.1.1_linux-x64_bin.tar.gz.sha256",
            "size":64,
            "browser_download_url":"https://github.com/graalvm.tar.gz.sha256"}
         ]}
        """;

}
