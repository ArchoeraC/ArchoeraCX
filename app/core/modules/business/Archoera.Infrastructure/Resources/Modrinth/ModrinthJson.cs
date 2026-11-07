using System.Globalization;
using System.Text.Json;
using Archoera.Application.Resources;
using Archoera.Domain.Resources;

namespace Archoera.Infrastructure.Resources.Modrinth;

/// <summary>
/// Maps Modrinth v2 JSON into the provider-independent DTOs. Uses
/// <see cref="JsonElement"/> only, so NativeAOT never needs reflection.
/// </summary>
internal static class ModrinthJson
{
    public static ResourceSearchResult ParseSearch(
        JsonElement root,
        ResourceSearchRequest request)
    {
        RequireObject(root, "search response");

        var total = GetInt64(root, "total_hits");
        var offset = (int)Math.Clamp(GetInt64(root, "offset"), 0, int.MaxValue);
        var limit = (int)Math.Clamp(GetInt64(root, "limit"), 0, int.MaxValue);

        var hits = new List<ResourceProject>();
        if (root.TryGetProperty("hits", out var hitsElement)
            && hitsElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var hit in hitsElement.EnumerateArray())
            {
                hits.Add(ParseSearchHit(hit, request.Kind));
            }
        }

        return new ResourceSearchResult(
            ModrinthClient.ProviderId,
            total,
            request.Page,
            limit > 0 ? limit : request.PageSize,
            hits);
    }

    public static ResourceProject ParseProject(JsonElement root)
    {
        RequireObject(root, "project response");

        var kindText = GetString(root, "project_type") ?? string.Empty;
        if (!ResourceWire.TryParseKind(kindText, out var kind))
        {
            throw Invalid($"unsupported project_type '{kindText}'");
        }

        string? license = null;
        if (root.TryGetProperty("license", out var licenseElement))
        {
            license = licenseElement.ValueKind switch
            {
                JsonValueKind.String => licenseElement.GetString(),
                JsonValueKind.Object => GetString(licenseElement, "id") ?? GetString(licenseElement, "name"),
                JsonValueKind.Null => null,
                _ => throw Invalid("'license' must be a string or object"),
            };
        }

        return new ResourceProject(
            Id: RequireString(root, "id"),
            Slug: GetString(root, "slug") ?? string.Empty,
            Name: RequireString(root, "title"),
            Summary: GetString(root, "description") ?? string.Empty,
            Kind: kind,
            Downloads: GetInt64(root, "downloads"),
            Follows: GetInt64(root, "followers"),
            UpdatedAt: GetDate(root, "updated"),
            IconUrl: GetString(root, "icon_url"),
            Categories: GetStringArray(root, "categories"),
            Loaders: GetStringArray(root, "loaders"),
            GameVersions: GetStringArray(root, "game_versions"),
            ClientSide: GetString(root, "client_side"),
            License: license,
            Gallery: ParseGallery(root),
            SourceUrl: GetString(root, "source_url"),
            IssuesUrl: GetString(root, "issues_url"),
            WikiUrl: GetString(root, "wiki_url"),
            PublishedAt: GetDate(root, "published"),
            Body: GetString(root, "body"));
    }

    public static IReadOnlyList<ResourceVersion> ParseVersions(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Array)
        {
            throw Invalid("version list must be an array");
        }

        var versions = new List<ResourceVersion>(root.GetArrayLength());
        foreach (var element in root.EnumerateArray())
        {
            versions.Add(ParseVersion(element));
        }

        return versions;
    }

    public static ResourceVersion ParseVersion(JsonElement root)
    {
        RequireObject(root, "version response");

        var files = new List<ResourceFile>();
        if (root.TryGetProperty("files", out var filesElement)
            && filesElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var file in filesElement.EnumerateArray())
            {
                files.Add(ParseFile(file));
            }
        }

        var dependencies = new List<ResourceDependency>();
        if (root.TryGetProperty("dependencies", out var dependenciesElement)
            && dependenciesElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var dependency in dependenciesElement.EnumerateArray())
            {
                dependencies.Add(ParseDependency(dependency));
            }
        }

        var versionType = ResourceVersionType.Release;
        var versionTypeText = GetString(root, "version_type");
        if (versionTypeText is not null
            && !ResourceWire.TryParseVersionType(versionTypeText, out versionType))
        {
            throw Invalid($"unsupported version_type '{versionTypeText}'");
        }

        return new ResourceVersion(
            Id: RequireString(root, "id"),
            ProjectId: RequireString(root, "project_id"),
            Name: GetString(root, "name") ?? string.Empty,
            VersionNumber: GetString(root, "version_number") ?? string.Empty,
            GameVersions: GetStringArray(root, "game_versions"),
            Loaders: GetStringArray(root, "loaders"),
            VersionType: versionType,
            PublishedAt: GetDate(root, "date_published"),
            Downloads: GetInt64(root, "downloads"),
            Files: files,
            Dependencies: dependencies);
    }

    private static IReadOnlyList<ResourceGalleryImage> ParseGallery(JsonElement root)
    {
        if (!root.TryGetProperty("gallery", out var gallery) ||
            gallery.ValueKind == JsonValueKind.Null)
        {
            return [];
        }

        if (gallery.ValueKind != JsonValueKind.Array)
        {
            throw Invalid("'gallery' must be an array");
        }

        var images = new List<ResourceGalleryImage>();
        foreach (var item in gallery.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                throw Invalid("'gallery' entries must be objects");
            }

            var url = GetString(item, "url");
            if (string.IsNullOrWhiteSpace(url) ||
                !Uri.TryCreate(url, UriKind.Absolute, out var parsed) ||
                (parsed.Scheme != Uri.UriSchemeHttps && parsed.Scheme != Uri.UriSchemeHttp))
            {
                continue;
            }

            images.Add(new ResourceGalleryImage(
                Url: url,
                Featured: item.TryGetProperty("featured", out var featured) &&
                    featured.ValueKind == JsonValueKind.True,
                Title: GetString(item, "title"),
                Description: GetString(item, "description"),
                Ordering: (int)Math.Clamp(GetInt64(item, "ordering"), 0, int.MaxValue)));
        }

        return images
            .OrderByDescending(image => image.Featured)
            .ThenBy(image => image.Ordering)
            .ToArray();
    }

    private static ResourceProject ParseSearchHit(JsonElement hit, ResourceKind fallbackKind)
    {
        RequireObject(hit, "search hit");

        var kind = fallbackKind;
        var kindText = GetString(hit, "project_type");
        if (kindText is not null && ResourceWire.TryParseKind(kindText, out var parsed))
        {
            kind = parsed;
        }

        var follows = hit.TryGetProperty("follows", out _)
            ? GetInt64(hit, "follows")
            : GetInt64(hit, "followers");

        var rawCategories = GetStringArray(hit, "categories");
        var categories = hit.TryGetProperty("display_categories", out _)
            ? GetStringArray(hit, "display_categories")
            : rawCategories;

        var loaders = GetStringArray(hit, "loaders");
        if (loaders.Count == 0)
        {
            // Modrinth's search response does not expose `loaders`; loader names
            // appear in the raw `categories` array alongside real categories.
            loaders = rawCategories
                .Where(category => IsLoaderCategory(category))
                .ToArray();
        }

        return new ResourceProject(
            Id: GetString(hit, "project_id") ?? GetString(hit, "id") ?? RequireString(hit, "slug"),
            Slug: GetString(hit, "slug") ?? string.Empty,
            Name: GetString(hit, "title") ?? string.Empty,
            Summary: GetString(hit, "description") ?? string.Empty,
            Kind: kind,
            Downloads: GetInt64(hit, "downloads"),
            Follows: follows,
            UpdatedAt: GetDate(hit, "date_modified"),
            Author: GetString(hit, "author"),
            IconUrl: GetString(hit, "icon_url"),
            Categories: categories,
            Loaders: loaders,
            GameVersions: GetStringArray(hit, "versions"),
            ClientSide: GetString(hit, "client_side"),
            License: GetString(hit, "license"));
    }

    private static bool IsLoaderCategory(string category)
        => category.ToLowerInvariant() is
            "fabric" or "forge" or "neoforge" or "quilt" or "iris" or "optifine"
            or "minecraft" or "liteloader" or "rift";

    private static ResourceFile ParseFile(JsonElement root)
    {
        RequireObject(root, "version file");

        string? sha1 = null;
        string? sha512 = null;
        if (root.TryGetProperty("hashes", out var hashes)
            && hashes.ValueKind == JsonValueKind.Object)
        {
            sha1 = GetString(hashes, "sha1");
            sha512 = GetString(hashes, "sha512");
        }

        return new ResourceFile(
            FileName: RequireString(root, "filename"),
            Url: GetString(root, "url") ?? string.Empty,
            Size: GetInt64(root, "size"),
            Sha1: sha1,
            Sha512: sha512,
            Primary: root.TryGetProperty("primary", out var primary)
                && primary.ValueKind == JsonValueKind.True);
    }

    private static ResourceDependency ParseDependency(JsonElement root)
    {
        RequireObject(root, "version dependency");

        var kindText = GetString(root, "dependency_type") ?? string.Empty;
        if (!ResourceWire.TryParseDependencyKind(kindText, out var kind))
        {
            throw Invalid($"unsupported dependency_type '{kindText}'");
        }

        return new ResourceDependency(
            Kind: kind,
            ProjectId: GetString(root, "project_id"),
            VersionId: GetString(root, "version_id"),
            FileName: GetString(root, "file_name"));
    }

    private static void RequireObject(JsonElement element, string what)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw Invalid($"{what} must be a JSON object");
        }
    }

    private static string RequireString(JsonElement parent, string name)
        => GetString(parent, name)
           ?? throw Invalid($"missing required string '{name}'");

    private static string? GetString(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var element))
        {
            return null;
        }

        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Null => null,
            _ => throw Invalid($"'{name}' must be a string"),
        };
    }

    private static long GetInt64(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var element) || element.ValueKind == JsonValueKind.Null)
        {
            return 0;
        }

        if (element.ValueKind != JsonValueKind.Number || !element.TryGetInt64(out var value))
        {
            throw Invalid($"'{name}' must be a 64-bit integer");
        }

        return value;
    }

    private static IReadOnlyList<string> GetStringArray(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var element) || element.ValueKind == JsonValueKind.Null)
        {
            return [];
        }

        if (element.ValueKind != JsonValueKind.Array)
        {
            throw Invalid($"'{name}' must be an array");
        }

        var values = new List<string>(element.GetArrayLength());
        foreach (var item in element.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String)
            {
                throw Invalid($"'{name}' entries must be strings");
            }

            values.Add(item.GetString() ?? string.Empty);
        }

        return values;
    }

    private static DateTimeOffset? GetDate(JsonElement parent, string name)
    {
        var text = GetString(parent, name);
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        return DateTimeOffset.TryParse(
            text,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out var value)
            ? value
            : null;
    }

    private static ResourceProviderException Invalid(string detail)
        => new(
            ResourceErrorCode.InvalidResponse,
            $"Modrinth response is invalid: {detail}");
}
