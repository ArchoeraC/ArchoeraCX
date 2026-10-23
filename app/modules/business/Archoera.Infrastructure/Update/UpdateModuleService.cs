using System.Globalization;
using System.Net;
using System.Text.Json;
using Archoera.Application.Modules;
using Archoera.Infrastructure.Modules;

namespace Archoera.Infrastructure.Update;

/// <summary>
/// Registered handler for <c>update.check</c>: compares this build with the
/// newest published GitHub release. Read-only; nothing is downloaded and no
/// installer is started here.
/// </summary>
public sealed class UpdateModuleService : IModuleMethodHandler
{
    /// <summary>Must follow app/pubspec.yaml version.</summary>
    private const string LauncherVersion = "0.0.1+1";

    private const string RepositoryUrl =
        "https://github.com/BetaStudio2/ArchoeraCX";

    private const string LatestReleaseUrl =
        "https://api.github.com/repos/BetaStudio2/ArchoeraCX/releases/latest";

    /// <summary>Used when the repository has no non-prerelease yet (HTTP 404).</summary>
    private const string ReleasesListUrl =
        "https://api.github.com/repos/BetaStudio2/ArchoeraCX/releases?per_page=1";

    private const string ReleasesPageUrl =
        "https://github.com/BetaStudio2/ArchoeraCX/releases";

    private const string UserAgent = "ArchoeraCX/" + LauncherVersion + " (" + RepositoryUrl + ")";

    private const string GitHubAccept = "application/vnd.github+json";

    private static readonly string[] SupportedMethods = ["update.check"];

    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(10);

    /// <summary>Release bodies can be very long; the UI only shows a summary.</summary>
    private const int MaxNotesLength = 4000;

    private static readonly HttpClient SharedHttp = CreateHttp();

    private readonly HttpClient _http;

    /// <summary>The registry builds the default instance; tests inject a fake transport.</summary>
    public UpdateModuleService(HttpClient? http = null) => _http = http ?? SharedHttp;

    public IReadOnlyCollection<string> Methods => SupportedMethods;

    public async Task<byte[]> HandleAsync(
        string method,
        string? paramsJson,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(method, "update.check", StringComparison.Ordinal))
        {
            return ModuleJson.Error("E_UNSUPPORTED", $"unsupported update method '{method}'");
        }

        // The frozen contract takes no parameters; unknown keys are ignored.
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(RequestTimeout);

        try
        {
            var release = await FetchReleaseAsync(timeout.Token).ConfigureAwait(false);

            // A tag that is not semantic (or a build that is not) simply cannot
            // prove an update; both raw strings are still reported.
            var hasUpdate = TryNormalizeVersion(release.Tag, out var latestVersion) &&
                            TryNormalizeVersion(LauncherVersion, out var currentVersion) &&
                            latestVersion.CompareTo(currentVersion) > 0;

            return ModuleJson.Envelope(writer =>
            {
                writer.WriteString("current", LauncherVersion);
                writer.WriteString("latest", release.Tag);
                writer.WriteBoolean("hasUpdate", hasUpdate);
                WriteNullable(writer, "url", release.Url);
                WriteNullable(writer, "name", release.Name);
                WriteNullable(writer, "publishedAt", release.PublishedAt);
                WriteNullable(writer, "notes", release.Notes);
            });
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return ModuleJson.Error("E_NETWORK", "GitHub release check timed out");
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException)
        {
            return ModuleJson.Error("E_NETWORK", exception.Message);
        }
        catch (JsonException exception)
        {
            return ModuleJson.Error("E_INVALID", exception.Message);
        }
    }

    /// <summary>
    /// Reads the newest release. GitHub answers 404 on
    /// <c>/releases/latest</c> while the project only publishes pre-releases,
    /// so the first entry of the release list is used instead; an empty list
    /// means "nothing published yet" and reads as up to date.
    /// </summary>
    private async Task<ReleaseInfo> FetchReleaseAsync(CancellationToken cancellationToken)
    {
        var (status, body) = await GetAsync(LatestReleaseUrl, cancellationToken)
            .ConfigureAwait(false);
        if (status == HttpStatusCode.NotFound)
        {
            var (listStatus, listBody) = await GetAsync(ReleasesListUrl, cancellationToken)
                .ConfigureAwait(false);
            if (!IsSuccess(listStatus))
            {
                throw new HttpRequestException(
                    $"GitHub release list failed with HTTP {(int)listStatus}");
            }

            using var list = JsonDocument.Parse(listBody);
            return ReleaseInfo.ParseFirst(list.RootElement)
                ?? ReleaseInfo.None(LauncherVersion, ReleasesPageUrl);
        }

        if (!IsSuccess(status))
        {
            throw new HttpRequestException(
                $"GitHub release check failed with HTTP {(int)status}");
        }

        using var document = JsonDocument.Parse(body);
        return ReleaseInfo.Parse(document.RootElement);
    }

    private async Task<(HttpStatusCode Status, string Body)> GetAsync(
        string url,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.TryAddWithoutValidation("User-Agent", UserAgent);
        request.Headers.TryAddWithoutValidation("Accept", GitHubAccept);

        using var response = await _http
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            return (response.StatusCode, string.Empty);
        }

        var body = await response.Content
            .ReadAsStringAsync(cancellationToken)
            .ConfigureAwait(false);
        return (response.StatusCode, body);
    }

    private static bool IsSuccess(HttpStatusCode status)
        => (int)status is >= 200 and <= 299;

    /// <summary>
    /// Reduces a launcher version to <c>major.minor.patch</c>: build metadata
    /// (<c>+1</c>) and pre-release suffixes (<c>-rc1</c>) are ignored, and
    /// missing trailing components default to zero.
    /// </summary>
    private static bool TryNormalizeVersion(
        string? raw,
        out (int Major, int Minor, int Patch) version)
    {
        version = default;
        if (string.IsNullOrWhiteSpace(raw)) return false;

        var value = raw.Trim();
        var build = value.IndexOf('+', StringComparison.Ordinal);
        if (build >= 0) value = value[..build];
        var pre = value.IndexOf('-', StringComparison.Ordinal);
        if (pre >= 0) value = value[..pre];

        var parts = value.Split('.');
        if (parts.Length is < 1 or > 3) return false;

        var numbers = new int[3];
        for (var index = 0; index < parts.Length; index++)
        {
            if (!int.TryParse(
                    parts[index],
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out numbers[index]))
            {
                return false;
            }
        }

        version = (numbers[0], numbers[1], numbers[2]);
        return true;
    }

    private static void WriteNullable(Utf8JsonWriter writer, string name, string? value)
    {
        if (value is null) writer.WriteNull(name);
        else writer.WriteString(name, value);
    }

    private static HttpClient CreateHttp()
    {
        var client = new HttpClient(new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            AllowAutoRedirect = true,
        })
        {
            Timeout = RequestTimeout,
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
        client.DefaultRequestHeaders.Accept.ParseAdd(GitHubAccept);
        return client;
    }

    private sealed record ReleaseInfo(
        string Tag,
        string? Name,
        string? Url,
        string? PublishedAt,
        string? Notes)
    {
        public static ReleaseInfo Parse(JsonElement root)
        {
            if (root.ValueKind != JsonValueKind.Object)
            {
                throw new JsonException("release payload must be an object");
            }

            var tag = GetString(root, "tag_name")?.Trim();
            if (string.IsNullOrEmpty(tag))
            {
                throw new JsonException("release payload has no tag_name");
            }

            // GitHub tags carry a leading 'v' by convention; the contract
            // reports the bare version.
            if (tag[0] is 'v' or 'V') tag = tag[1..];

            return new ReleaseInfo(
                Tag: tag,
                Name: GetString(root, "name"),
                Url: GetString(root, "html_url"),
                PublishedAt: GetString(root, "published_at"),
                Notes: Truncate(GetString(root, "body")));
        }

        /// <summary>First element of a <c>/releases</c> array, if any.</summary>
        public static ReleaseInfo? ParseFirst(JsonElement root)
        {
            if (root.ValueKind != JsonValueKind.Array || root.GetArrayLength() == 0)
            {
                return null;
            }

            return Parse(root[0]);
        }

        /// <summary>Synthetic "nothing published" result that reads as current.</summary>
        public static ReleaseInfo None(string current, string releasesPage)
            => new(current, null, releasesPage, null, null);

        private static string? GetString(JsonElement parent, string name)
            => parent.TryGetProperty(name, out var element) &&
               element.ValueKind == JsonValueKind.String
                ? element.GetString()
                : null;

        private static string? Truncate(string? notes)
            => notes is null || notes.Length <= MaxNotesLength
                ? notes
                : notes[..MaxNotesLength];
    }
}
