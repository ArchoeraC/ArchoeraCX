using System.Net;
using System.Text;
using System.Text.Json;
using Archoera.Infrastructure.Community;

namespace Archoera.Tests;

/// <summary>
/// Offline coverage for the Wiki feed: routing is by URL fragment because the
/// three sources are fetched concurrently.
/// </summary>
public sealed class CommunityModuleTests
{
    private const string NewsUrl = "launchercontent.mojang.com/v2/news.json";
    private const string NotesUrl = "launchercontent.mojang.com/v2/javaPatchNotes.json";
    private const string WikiUrl = "minecraft.wiki/api.php";

    [Fact]
    public async Task Merges_sources_and_normalizes_items()
    {
        var handler = new RoutingHandler()
            .Route(NewsUrl, NewsPayload)
            .Route(NotesUrl, NotesPayload)
            .Route(WikiUrl, WikiPayload);
        var service = new CommunityModuleService(new HttpClient(handler));

        using var document = JsonDocument.Parse(
            await service.HandleAsync("wiki.feed", "{}", CancellationToken.None));

        Assert.True(document.RootElement.GetProperty("ok").GetBoolean());
        var data = document.RootElement.GetProperty("data");
        var sources = data.GetProperty("sources").EnumerateArray().ToList();
        Assert.Equal(3, sources.Count);
        Assert.All(sources, source => Assert.True(source.GetProperty("ok").GetBoolean()));

        var items = data.GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(7, items.Count);

        var news = items.Single(item => item.GetProperty("id").GetString() == "news:n1");
        // Launcher tracking parameters are stripped from external links.
        Assert.Equal(
            "https://minecraft.net/article/a",
            news.GetProperty("url").GetString());
        Assert.Equal("Hello world", news.GetProperty("summary").GetString());
        Assert.Equal("news", news.GetProperty("kind").GetString());

        var notes = items.Single(item => item.GetProperty("id").GetString() == "java:26-4-snapshot-5");
        Assert.Equal("snapshot", notes.GetProperty("kind").GetString());
        Assert.Equal(
            "https://minecraft.wiki/w/Java_Edition_26.4_Snapshot_5",
            notes.GetProperty("url").GetString());

        // Duplicate wiki edits collapse to the newest revision of a page.
        var wiki = items.Where(item => item.GetProperty("source").GetString() == "minecraft-wiki")
            .ToList();
        Assert.Equal(2, wiki.Count);
        var domino = wiki.Single(item => item.GetProperty("title").GetString() == "Domino's");
        Assert.Equal("/* Videos */", domino.GetProperty("summary").GetString());
        Assert.Equal("Yaya", domino.GetProperty("author").GetString());
        Assert.Equal(
            "https://minecraft.wiki/w/Domino's",
            domino.GetProperty("url").GetString());
        var golem = wiki.Single(item => item.GetProperty("title").GetString() == "Copper Golem");
        Assert.Equal(
            "https://minecraft.wiki/w/Copper_Golem",
            golem.GetProperty("url").GetString());
        Assert.Contains("+20 B", domino.GetProperty("tags").EnumerateArray().Select(tag => tag.GetString()));
    }

    [Fact]
    public async Task Limits_items_per_source()
    {
        var handler = new RoutingHandler()
            .Route(NewsUrl, NewsPayload)
            .Route(NotesUrl, NotesPayload)
            .Route(WikiUrl, WikiPayload);
        var service = new CommunityModuleService(new HttpClient(handler));

        using var document = JsonDocument.Parse(
            await service.HandleAsync("wiki.feed", "{\"limit\":1}", CancellationToken.None));

        var items = document.RootElement.GetProperty("data").GetProperty("items")
            .EnumerateArray()
            .ToList();
        Assert.Equal(3, items.Count);
        Assert.Equal(
            new[] { "minecraft-wiki", "mojang-java", "mojang-news" },
            items.Select(item => item.GetProperty("source").GetString()!)
                .Distinct()
                .OrderBy(value => value)
                .ToArray());
    }

    [Fact]
    public async Task One_failing_source_keeps_the_others()
    {
        var handler = new RoutingHandler()
            .Route(NewsUrl, () => new HttpResponseMessage(HttpStatusCode.InternalServerError))
            .Route(NotesUrl, NotesPayload)
            .Route(WikiUrl, WikiPayload);
        var service = new CommunityModuleService(new HttpClient(handler));

        using var document = JsonDocument.Parse(
            await service.HandleAsync("wiki.feed", "{}", CancellationToken.None));

        Assert.True(document.RootElement.GetProperty("ok").GetBoolean());
        var data = document.RootElement.GetProperty("data");
        var sources = data.GetProperty("sources").EnumerateArray()
            .ToDictionary(
                source => source.GetProperty("id").GetString()!,
                source => source);
        Assert.False(sources["mojang-news"].GetProperty("ok").GetBoolean());
        Assert.Equal("HTTP 500", sources["mojang-news"].GetProperty("error").GetString());
        Assert.True(sources["minecraft-wiki"].GetProperty("ok").GetBoolean());
        Assert.DoesNotContain(
            data.GetProperty("items").EnumerateArray(),
            item => item.GetProperty("source").GetString() == "mojang-news");
    }

    [Fact]
    public async Task Caches_until_forced()
    {
        var handler = new RoutingHandler()
            .Route(NewsUrl, NewsPayload)
            .Route(NotesUrl, NotesPayload)
            .Route(WikiUrl, WikiPayload);
        var service = new CommunityModuleService(new HttpClient(handler));

        using (var first = JsonDocument.Parse(
                   await service.HandleAsync("wiki.feed", "{}", CancellationToken.None)))
        {
            Assert.False(first.RootElement.GetProperty("data").GetProperty("cached").GetBoolean());
        }

        var requestsAfterFirst = handler.Requests.Count;
        Assert.Equal(3, requestsAfterFirst);

        using (var second = JsonDocument.Parse(
                   await service.HandleAsync("wiki.feed", "{}", CancellationToken.None)))
        {
            Assert.True(second.RootElement.GetProperty("data").GetProperty("cached").GetBoolean());
        }

        Assert.Equal(requestsAfterFirst, handler.Requests.Count);

        using (var forced = JsonDocument.Parse(
                   await service.HandleAsync("wiki.feed", "{\"force\":true}", CancellationToken.None)))
        {
            Assert.False(forced.RootElement.GetProperty("data").GetProperty("cached").GetBoolean());
        }

        Assert.Equal(requestsAfterFirst * 2, handler.Requests.Count);
    }

    [Fact]
    public async Task Rejects_unknown_method()
    {
        var service = new CommunityModuleService(new HttpClient(new RoutingHandler()));
        using var document = JsonDocument.Parse(
            await service.HandleAsync("wiki.other", "{}", CancellationToken.None));
        Assert.False(document.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal(
            "E_UNSUPPORTED",
            document.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    private static HttpResponseMessage Json(string body)
        => new(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };

    private static HttpResponseMessage NewsPayload() => Json(
        """
        {
          "version": 1,
          "entries": [
            {
              "id": "n1",
              "title": "Enter the Marketplace",
              "tag": "news",
              "category": "Minecraft: Java Edition",
              "date": "2026-10-20",
              "text": "  Hello \n world ",
              "readMoreLink": "https://minecraft.net/article/a?OCID=Launcher",
              "newsType": ["News page", "Java"]
            },
            {
              "id": "n2",
              "title": "Older note",
              "date": "2026-10-19",
              "readMoreLink": "",
              "text": ""
            }
          ]
        }
        """);

    private static HttpResponseMessage NotesPayload() => Json(
        """
        {
          "version": 1,
          "entries": [
            {
              "id": "26-4-snapshot-5",
              "title": "Minecraft 26.4 Snapshot 5",
              "version": "26.4-snapshot-5",
              "type": "snapshot",
              "date": "2026-10-20T12:15:06.000Z",
              "shortText": "Zuri is throwing Ice Balls"
            },
            {
              "id": "1-21-8",
              "title": "Minecraft 1.21.8",
              "version": "1.21.8",
              "type": "release",
              "date": "2026-10-01T00:00:00.000Z",
              "shortText": "The Copper Age"
            },
            {
              "id": "1-21-9-pre2",
              "title": "Minecraft 1.21.9 Pre-Release 2",
              "version": "1.21.9-pre2",
              "type": "pre-release",
              "date": "2026-09-01T00:00:00.000Z",
              "shortText": "Pre-release"
            }
          ]
        }
        """);

    private static HttpResponseMessage WikiPayload() => Json(
        """
        {
          "batchcomplete": true,
          "query": {
            "recentchanges": [
              {
                "type": "edit",
                "ns": 0,
                "title": "Domino's",
                "revid": 2,
                "oldlen": 100,
                "newlen": 120,
                "timestamp": "2026-10-22T05:02:05Z",
                "user": "Yaya",
                "comment": "/* Videos */"
              },
              {
                "type": "edit",
                "ns": 0,
                "title": "Domino's",
                "revid": 1,
                "oldlen": 90,
                "newlen": 100,
                "timestamp": "2026-10-22T05:01:04Z",
                "user": "Yaya",
                "comment": "older revision"
              },
              {
                "type": "new",
                "ns": 0,
                "title": "Copper Golem",
                "revid": 3,
                "oldlen": 0,
                "newlen": 500,
                "timestamp": "2026-10-22T05:03:56Z",
                "user": "Templero316",
                "comment": ""
              }
            ]
          }
        }
        """);

    private sealed class RoutingHandler : HttpMessageHandler
    {
        private readonly Dictionary<string, Func<HttpResponseMessage>> _routes = [];

        public List<Uri> Requests { get; } = [];

        public RoutingHandler Route(string fragment, Func<HttpResponseMessage> response)
        {
            _routes[fragment] = response;
            return this;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var uri = request.RequestUri ?? new Uri("about:blank");
            Requests.Add(uri);
            foreach (var (fragment, response) in _routes)
            {
                if (uri.AbsoluteUri.Contains(fragment, StringComparison.Ordinal))
                {
                    return Task.FromResult(response());
                }
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }
}
