using System.Net;
using System.Text;
using System.Text.Json;
using Archoera.Application.Modules;
using Archoera.Infrastructure.Modules;

namespace Archoera.Infrastructure.Community;

/// <summary>
/// Read-only community feed for the Wiki page: official Minecraft news, Java
/// patch notes and recent Minecraft Wiki edits.
///
/// Everything is fetched here so Flutter never talks to a third party. Each
/// source is independent: one failing endpoint reports its own error while the
/// others still return items.
/// </summary>
public sealed class CommunityModuleService : IModuleMethodHandler
{
    private static readonly string[] SupportedMethods = ["wiki.feed"];

    public const string SourceNews = "mojang-news";
    public const string SourceJavaNotes = "mojang-java";
    public const string SourceWiki = "minecraft-wiki";

    private static readonly string[] AllSources = [SourceNews, SourceJavaNotes, SourceWiki];

    /// <summary>Per-source item cap, also the cache size.</summary>
    private const int MaxPerSource = 20;

    private const int DefaultPerSource = 8;

    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);

    private static readonly HttpClient SharedHttp = CreateHttp();

    private readonly HttpClient _http;
    private readonly SemaphoreSlim _cacheGate = new(1, 1);
    private FeedCache? _cache;

    /// <summary>The registry builds the default instance; tests inject a fake transport.</summary>
    public CommunityModuleService(HttpClient? http = null) => _http = http ?? SharedHttp;

    public IReadOnlyCollection<string> Methods => SupportedMethods;

    public async Task<byte[]> HandleAsync(
        string method,
        string? paramsJson,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(method, "wiki.feed", StringComparison.Ordinal))
        {
            return ModuleJson.Error("E_UNSUPPORTED", $"unsupported wiki method '{method}'");
        }

        try
        {
            var limit = Math.Clamp(ReadInt(paramsJson, "limit") ?? DefaultPerSource, 1, MaxPerSource);
            var force = ReadBool(paramsJson, "force");
            var requested = ReadSources(paramsJson);

            var cache = await GetFeedAsync(force, cancellationToken).ConfigureAwait(false);

            // Per-source cap first, then one chronological list.
            var selected = cache.Items
                .Where(item => requested.Contains(item.Source))
                .GroupBy(item => item.Source)
                .SelectMany(group => group
                    .OrderByDescending(item => item.PublishedAt)
                    .Take(limit))
                .OrderByDescending(item => item.PublishedAt)
                .ToList();

            return ModuleJson.Envelope(writer =>
            {
                writer.WriteString("fetchedAt", cache.FetchedAt.ToString("O"));
                writer.WriteBoolean("cached", cache.FromCache);

                writer.WriteStartArray("items");
                foreach (var item in selected)
                {
                    writer.WriteStartObject();
                    writer.WriteString("id", item.Id);
                    writer.WriteString("source", item.Source);
                    writer.WriteString("kind", item.Kind);
                    writer.WriteString("title", item.Title);
                    if (item.Summary is null) writer.WriteNull("summary");
                    else writer.WriteString("summary", item.Summary);
                    if (item.Url is null) writer.WriteNull("url");
                    else writer.WriteString("url", item.Url);
                    writer.WriteString("publishedAt", item.PublishedAt.ToString("O"));
                    if (item.Author is null) writer.WriteNull("author");
                    else writer.WriteString("author", item.Author);
                    writer.WriteStartArray("tags");
                    foreach (var tag in item.Tags)
                    {
                        writer.WriteStringValue(tag);
                    }

                    writer.WriteEndArray();
                    writer.WriteEndObject();
                }

                writer.WriteEndArray();

                writer.WriteStartArray("sources");
                foreach (var status in cache.Sources)
                {
                    writer.WriteStartObject();
                    writer.WriteString("id", status.Id);
                    writer.WriteBoolean("ok", status.Error is null);
                    // Count what this response actually carries, not the cache size.
                    writer.WriteNumber(
                        "count",
                        selected.Count(item => item.Source == status.Id));
                    if (status.Error is null) writer.WriteNull("error");
                    else writer.WriteString("error", status.Error);
                    writer.WriteEndObject();
                }

                writer.WriteEndArray();
            });
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (JsonException exception)
        {
            return ModuleJson.Error("E_INVALID", exception.Message);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            return ModuleJson.Error("NETWORK_ERROR", exception.Message);
        }
        catch (Exception exception)
        {
            return ModuleJson.Error("E_INTERNAL", exception.Message);
        }
    }

    private async Task<FeedCache> GetFeedAsync(bool force, CancellationToken cancellationToken)
    {
        await _cacheGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var cached = _cache;
            if (!force &&
                cached is not null &&
                DateTimeOffset.UtcNow - cached.FetchedAt < CacheTtl)
            {
                return cached with { FromCache = true };
            }

            var fresh = await FetchAsync(cancellationToken).ConfigureAwait(false);
            _cache = fresh;
            return fresh;
        }
        finally
        {
            _cacheGate.Release();
        }
    }

    private async Task<FeedCache> FetchAsync(CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));

        var tasks = new (string Id, Task<SourceResult> Task)[]
        {
            (SourceNews, FetchMojangNewsAsync(timeout.Token)),
            (SourceJavaNotes, FetchJavaNotesAsync(timeout.Token)),
            (SourceWiki, FetchWikiChangesAsync(timeout.Token)),
        };

        var results = await Task.WhenAll(tasks.Select(entry => entry.Task))
            .ConfigureAwait(false);

        var items = new List<FeedItem>();
        var sources = new List<SourceStatus>(results.Length);
        for (var index = 0; index < results.Length; index++)
        {
            var result = results[index];
            var id = tasks[index].Id;
            items.AddRange(result.Items);
            sources.Add(new SourceStatus(id, result.Items.Count, result.Error));
        }

        return new FeedCache(
            DateTimeOffset.UtcNow,
            false,
            items,
            sources);
    }

    private async Task<SourceResult> FetchMojangNewsAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var document = await GetJsonAsync(
                "https://launchercontent.mojang.com/v2/news.json",
                cancellationToken).ConfigureAwait(false);
            if (!document.RootElement.TryGetProperty("entries", out var entries) ||
                entries.ValueKind != JsonValueKind.Array)
            {
                return SourceResult.Failed("unexpected news payload");
            }

            var items = new List<FeedItem>();
            foreach (var entry in entries.EnumerateArray())
            {
                var title = GetString(entry, "title")?.Trim();
                if (string.IsNullOrWhiteSpace(title)) continue;
                var date = GetDate(entry, "date") ?? DateTimeOffset.MinValue;
                var tags = new List<string>();
                if (entry.TryGetProperty("newsType", out var newsType) &&
                    newsType.ValueKind == JsonValueKind.Array)
                {
                    foreach (var tag in newsType.EnumerateArray())
                    {
                        var text = tag.GetString();
                        if (!string.IsNullOrWhiteSpace(text)) tags.Add(text);
                    }
                }

                var category = GetString(entry, "category");
                if (!string.IsNullOrWhiteSpace(category) && !tags.Contains(category))
                {
                    tags.Add(category);
                }

                items.Add(new FeedItem(
                    Id: $"news:{GetString(entry, "id") ?? title}",
                    Source: SourceNews,
                    Kind: "news",
                    Title: title,
                    Summary: CleanText(GetString(entry, "text")),
                    Url: CleanLink(GetString(entry, "readMoreLink")),
                    PublishedAt: date,
                    Author: null,
                    Tags: tags.Take(3).ToList()));
            }

            return SourceResult.Ok(items
                .OrderByDescending(item => item.PublishedAt)
                .Take(MaxPerSource)
                .ToList());
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return SourceResult.Failed("timed out");
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException)
        {
            return SourceResult.Failed(Describe(exception));
        }
    }

    private async Task<SourceResult> FetchJavaNotesAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var document = await GetJsonAsync(
                "https://launchercontent.mojang.com/v2/javaPatchNotes.json",
                cancellationToken).ConfigureAwait(false);
            if (!document.RootElement.TryGetProperty("entries", out var entries) ||
                entries.ValueKind != JsonValueKind.Array)
            {
                return SourceResult.Failed("unexpected patch notes payload");
            }

            var items = new List<FeedItem>();
            foreach (var entry in entries.EnumerateArray())
            {
                var title = GetString(entry, "title")?.Trim();
                var version = GetString(entry, "version");
                if (string.IsNullOrWhiteSpace(title)) continue;
                var type = GetString(entry, "type") ?? "release";
                items.Add(new FeedItem(
                    Id: $"java:{GetString(entry, "id") ?? version ?? title}",
                    Source: SourceJavaNotes,
                    Kind: type,
                    Title: title,
                    Summary: CleanText(GetString(entry, "shortText")),
                    Url: WikiVersionUrl(title, type),
                    PublishedAt: GetDate(entry, "date") ?? DateTimeOffset.MinValue,
                    Author: null,
                    Tags: string.IsNullOrWhiteSpace(version) ? [] : [version]));
            }

            return SourceResult.Ok(items
                .OrderByDescending(item => item.PublishedAt)
                .Take(MaxPerSource)
                .ToList());
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return SourceResult.Failed("timed out");
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException)
        {
            return SourceResult.Failed(Describe(exception));
        }
    }

    private async Task<SourceResult> FetchWikiChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            const string url =
                "https://minecraft.wiki/api.php?action=query&list=recentchanges" +
                "&rcprop=title%7Ctimestamp%7Cuser%7Ccomment%7Cids%7Csizes" +
                "&rcshow=!minor%7C!bot&rcnamespace=0&rclimit=30" +
                "&format=json&formatversion=2";
            using var document = await GetJsonAsync(url, cancellationToken).ConfigureAwait(false);
            if (!document.RootElement.TryGetProperty("query", out var query) ||
                !query.TryGetProperty("recentchanges", out var changes) ||
                changes.ValueKind != JsonValueKind.Array)
            {
                return SourceResult.Failed("unexpected recent changes payload");
            }

            var items = new List<FeedItem>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var change in changes.EnumerateArray())
            {
                var title = GetString(change, "title")?.Trim();
                if (string.IsNullOrWhiteSpace(title)) continue;
                // Repeated edits of the same page would flood the feed.
                if (!seen.Add(title)) continue;

                var comment = CleanText(GetString(change, "comment"));
                var delta = SizeDelta(change);
                var tags = new List<string>();
                var changeType = GetString(change, "type");
                if (!string.IsNullOrWhiteSpace(changeType)) tags.Add(changeType);
                if (delta is not null) tags.Add(delta >= 0 ? $"+{delta} B" : $"{delta} B");

                items.Add(new FeedItem(
                    Id: $"wiki:{GetString(change, "revid") ?? title}",
                    Source: SourceWiki,
                    Kind: changeType ?? "edit",
                    Title: title,
                    Summary: string.IsNullOrWhiteSpace(comment) ? null : comment,
                    Url: WikiPageUrl(title),
                    PublishedAt: GetDate(change, "timestamp") ?? DateTimeOffset.MinValue,
                    Author: GetString(change, "user"),
                    Tags: tags));
            }

            return SourceResult.Ok(items.Take(MaxPerSource).ToList());
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return SourceResult.Failed("timed out");
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException)
        {
            return SourceResult.Failed(Describe(exception));
        }
    }

    private async Task<JsonDocument> GetJsonAsync(string url, CancellationToken cancellationToken)
    {
        using var response = await _http
            .GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"{(int)response.StatusCode} {response.ReasonPhrase}",
                null,
                response.StatusCode);
        }

        await using var stream = await response
            .Content
            .ReadAsStreamAsync(cancellationToken)
            .ConfigureAwait(false);
        return await JsonDocument
            .ParseAsync(stream, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Java patch notes have no article link in the feed payload, so they point
    /// at the matching Minecraft Wiki version page.
    /// </summary>
    private static string? WikiVersionUrl(string title, string type)
    {
        if (type is not ("release" or "snapshot")) return null;
        var name = title.Trim();
        if (name.StartsWith("Minecraft ", StringComparison.OrdinalIgnoreCase))
        {
            name = name["Minecraft ".Length..];
        }

        return WikiPageUrl($"Java Edition {name}");
    }

    /// <summary>
    /// Builds a readable article URL: spaces become underscores and only
    /// characters that would break the path are percent-encoded, so links keep
    /// the wiki's own punctuation (apostrophes, parentheses).
    /// </summary>
    private static string WikiPageUrl(string title)
    {
        const string allowed = "-_.~!$&'()*+,;=:@/";
        var slug = title.Trim().Replace(' ', '_');
        var builder = new StringBuilder("https://minecraft.wiki/w/");
        foreach (var character in slug)
        {
            if (char.IsAsciiLetterOrDigit(character) || allowed.Contains(character))
            {
                builder.Append(character);
                continue;
            }

            foreach (var value in Encoding.UTF8.GetBytes(character.ToString()))
            {
                builder.Append('%').Append(value.ToString("X2"));
            }
        }

        return builder.ToString();
    }

    /// <summary>Strips launcher tracking parameters from an external link.</summary>
    private static string? CleanLink(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return null;
        if (!uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) &&
            !uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var kept = uri
            .Query
            .TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Where(part => !part.StartsWith("OCID=", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var builder = new UriBuilder(uri) { Query = string.Join('&', kept) };
        return builder.Uri.ToString();
    }

    /// <summary>Collapses whitespace and trims a feed body for a list row.</summary>
    private static string? CleanText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var collapsed = string.Join(
            ' ',
            text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return collapsed.Length <= 400 ? collapsed : collapsed[..400] + "…";
    }

    private static long? SizeDelta(JsonElement change)
    {
        if (!change.TryGetProperty("oldlen", out var oldLen) ||
            !change.TryGetProperty("newlen", out var newLen) ||
            oldLen.ValueKind != JsonValueKind.Number ||
            newLen.ValueKind != JsonValueKind.Number)
        {
            return null;
        }

        return newLen.GetInt64() - oldLen.GetInt64();
    }

    private static string? GetString(JsonElement parent, string name)
        => parent.TryGetProperty(name, out var element) &&
           element.ValueKind == JsonValueKind.String
            ? element.GetString()
            : null;

    private static DateTimeOffset? GetDate(JsonElement parent, string name)
    {
        var text = GetString(parent, name);
        if (string.IsNullOrWhiteSpace(text)) return null;
        return DateTimeOffset.TryParse(
            text,
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AssumeUniversal |
            System.Globalization.DateTimeStyles.AdjustToUniversal,
            out var value)
            ? value
            : null;
    }

    private static string Describe(Exception exception)
        => exception is HttpRequestException { StatusCode: { } status }
            ? $"HTTP {(int)status}"
            : exception.Message;

    private static HashSet<string> ReadSources(string? json)
    {
        var requested = new HashSet<string>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(json)) return [.. AllSources];
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("sources", out var sources) ||
            sources.ValueKind != JsonValueKind.Array)
        {
            return [.. AllSources];
        }

        foreach (var item in sources.EnumerateArray())
        {
            var value = item.GetString();
            if (value is not null && AllSources.Contains(value)) requested.Add(value);
        }

        return requested.Count == 0 ? [.. AllSources] : requested;
    }

    private static int? ReadInt(string? json, string name)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        using var document = JsonDocument.Parse(json);
        return document.RootElement.TryGetProperty(name, out var element) &&
               element.ValueKind == JsonValueKind.Number &&
               element.TryGetInt32(out var value)
            ? value
            : null;
    }

    private static bool ReadBool(string? json, string name)
    {
        if (string.IsNullOrWhiteSpace(json)) return false;
        using var document = JsonDocument.Parse(json);
        return document.RootElement.TryGetProperty(name, out var element) &&
               element.ValueKind is JsonValueKind.True or JsonValueKind.False &&
               element.GetBoolean();
    }

    private static HttpClient CreateHttp()
    {
        var client = new HttpClient(new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            AllowAutoRedirect = true,
        })
        {
            Timeout = TimeSpan.FromSeconds(25),
        };
        // minecraft.wiki asks API clients to identify themselves.
        client.DefaultRequestHeaders.UserAgent.ParseAdd(
            "ArchoeraCX/0.1.0 (+https://github.com/BetaStudio2/ArchoeraCX)");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        return client;
    }

    private sealed record FeedItem(
        string Id,
        string Source,
        string Kind,
        string Title,
        string? Summary,
        string? Url,
        DateTimeOffset PublishedAt,
        string? Author,
        IReadOnlyList<string> Tags);

    private sealed record SourceStatus(string Id, int Count, string? Error);

    private sealed record FeedCache(
        DateTimeOffset FetchedAt,
        bool FromCache,
        IReadOnlyList<FeedItem> Items,
        IReadOnlyList<SourceStatus> Sources);

    private sealed record SourceResult(IReadOnlyList<FeedItem> Items, string? Error)
    {
        public static SourceResult Ok(IReadOnlyList<FeedItem> items) => new(items, null);

        public static SourceResult Failed(string error) => new([], error);
    }
}
