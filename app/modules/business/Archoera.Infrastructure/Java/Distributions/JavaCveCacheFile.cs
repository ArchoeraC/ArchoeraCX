using System.Text.Json;

namespace Archoera.Infrastructure.Java.Distributions;

/// <summary>
/// Shared on-disk cache format for CVE feeds. Both the NVD client and the
/// CIRCL fallback store the same reduced records, so a mirror switch never
/// changes the matching code.
/// </summary>
internal static class JavaCveCacheFile
{
    private const int CacheSchema = 2;

    public static IReadOnlyList<JavaNvdVulnerability>? Read(
        string path,
        string product,
        TimeSpan ttl,
        DateTimeOffset now)
    {
        try
        {
            if (!File.Exists(path)) return null;
            using var document = JsonDocument.Parse(File.ReadAllBytes(path));
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("schema", out var schema) ||
                !schema.TryGetInt32(out var schemaNumber) ||
                schemaNumber != CacheSchema ||
                !root.TryGetProperty("product", out var productValue) ||
                productValue.ValueKind != JsonValueKind.String ||
                productValue.GetString() != product ||
                !root.TryGetProperty("fetchedAt", out var fetchedAt) ||
                fetchedAt.ValueKind != JsonValueKind.String ||
                !DateTimeOffset.TryParse(fetchedAt.GetString(), out var fetched) ||
                fetched.Add(ttl) <= now ||
                !root.TryGetProperty("vulnerabilities", out var list) ||
                list.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            var results = new List<JavaNvdVulnerability>();
            foreach (var item in list.EnumerateArray())
            {
                var parsed = ReadVulnerability(item);
                if (parsed is not null) results.Add(parsed);
            }

            return results;
        }
        catch (Exception exception) when (
            exception is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public static void Write(
        string path,
        string product,
        IReadOnlyList<JavaNvdVulnerability> vulnerabilities,
        DateTimeOffset now)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var temporary = path + ".tmp";
            using (var stream = File.Create(temporary))
            using (var writer = new Utf8JsonWriter(stream))
            {
                writer.WriteStartObject();
                writer.WriteNumber("schema", CacheSchema);
                writer.WriteString("product", product);
                writer.WriteString("fetchedAt", now);
                writer.WriteStartArray("vulnerabilities");
                foreach (var vulnerability in vulnerabilities)
                {
                    writer.WriteStartObject();
                    writer.WriteString("id", vulnerability.Id);
                    writer.WriteString("summary", vulnerability.Summary);
                    writer.WriteString("published", vulnerability.Published);
                    writer.WriteString("severity", vulnerability.Severity);
                    if (vulnerability.Score is { } score)
                    {
                        writer.WriteNumber("score", score);
                    }
                    else
                    {
                        writer.WriteNull("score");
                    }

                    writer.WriteString("url", vulnerability.Url);
                    writer.WriteStartArray("matches");
                    foreach (var match in vulnerability.Matches)
                    {
                        writer.WriteStartObject();
                        writer.WriteString("criteria", match.Criteria);
                        writer.WriteBoolean("vulnerable", match.Vulnerable);
                        writer.WriteString("startIncluding", match.VersionStartIncluding);
                        writer.WriteString("startExcluding", match.VersionStartExcluding);
                        writer.WriteString("endIncluding", match.VersionEndIncluding);
                        writer.WriteString("endExcluding", match.VersionEndExcluding);
                        writer.WriteEndObject();
                    }

                    writer.WriteEndArray();
                    writer.WriteEndObject();
                }

                writer.WriteEndArray();
                writer.WriteEndObject();
            }

            File.Move(temporary, path, overwrite: true);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            // Caching is an optimisation; the fetched feed is still usable.
        }
    }

    private static JavaNvdVulnerability? ReadVulnerability(JsonElement item)
    {
        if (item.ValueKind != JsonValueKind.Object) return null;
        var id = Text(item, "id");
        if (string.IsNullOrWhiteSpace(id)) return null;
        var score = Number(item, "score");
        var severity = JavaCveSeverity.Normalize(Text(item, "severity"), score);
        var matches = new List<JavaCpeMatch>();
        if (item.TryGetProperty("matches", out var list) &&
            list.ValueKind == JsonValueKind.Array)
        {
            foreach (var match in list.EnumerateArray())
            {
                var parsed = JavaCpeMatch.Parse(
                    Text(match, "criteria"),
                    match.TryGetProperty("vulnerable", out var vulnerable) &&
                    vulnerable.ValueKind == JsonValueKind.True,
                    Text(match, "startIncluding"),
                    Text(match, "startExcluding"),
                    Text(match, "endIncluding"),
                    Text(match, "endExcluding"));
                if (parsed is not null) matches.Add(parsed);
            }
        }

        return new JavaNvdVulnerability(
            id,
            Text(item, "summary"),
            Text(item, "published"),
            severity,
            score,
            Text(item, "url"),
            matches);
    }

    private static string? Text(JsonElement element, string name)
        => element.ValueKind == JsonValueKind.Object &&
           element.TryGetProperty(name, out var value) &&
           value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static double? Number(JsonElement element, string name)
        => element.ValueKind == JsonValueKind.Object &&
           element.TryGetProperty(name, out var value) &&
           value.ValueKind == JsonValueKind.Number &&
           value.TryGetDouble(out var number)
            ? number
            : null;
}
