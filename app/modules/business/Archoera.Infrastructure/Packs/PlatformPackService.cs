using System.Collections.Concurrent;
using Archoera.Application.Packs;
using Archoera.Infrastructure.Packs.Platform;

namespace Archoera.Infrastructure.Packs;

/// <summary>
/// Platform pack providers that expose a public catalogue and direct file
/// manifests. Providers are registered once; adding another platform only
/// touches the registry, not the module facade or the UI.
/// </summary>
public sealed class PlatformPackService
{
    private static readonly TimeSpan SearchTtl = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan VersionsTtl = TimeSpan.FromMinutes(5);

    private readonly PlatformPackRegistry _registry;
    private readonly PackInstallService _installer;
    private readonly ConcurrentDictionary<string, CacheEntry> _cache =
        new(StringComparer.Ordinal);

    public PlatformPackService(
        PackInstallService? installer = null,
        HttpClient? http = null,
        Uri? ftbBase = null,
        Uri? technicBase = null,
        Uri? atLauncherBase = null)
    {
        _installer = installer ?? new PackInstallService();
        var sharedHttp = http ?? new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(60),
        };
        if (http is null)
        {
            sharedHttp.DefaultRequestHeaders.UserAgent.ParseAdd(
                "ArchoeraCX/0.0.1+1 (https://github.com/BetaStudio2/ArchoeraCX)");
        }

        _registry = new PlatformPackRegistry(
        [
            new FtbPackClient(sharedHttp, ftbBase),
            new TechnicPackClient(sharedHttp, technicBase),
            new AtLauncherPackClient(
                sharedHttp,
                atLauncherBase ?? ReadEndpoint("ARCHOERACX_ATLAUNCHER_API")),
        ]);
    }

    internal IReadOnlyList<IPlatformPackProvider> Providers => _registry.Providers;

    public async Task<(IReadOnlyList<PlatformPackSummary> Items, int Total)> SearchAsync(
        string provider,
        string query,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var source = _registry.Get(provider);
        var key = $"{source.Id}|search|{query}|{page}|{pageSize}";
        if (TryGet<(IReadOnlyList<PlatformPackSummary>, int)>(key, out var cached))
        {
            return cached;
        }

        var result = await source
            .SearchAsync(query, page, pageSize, cancellationToken)
            .ConfigureAwait(false);
        Put(key, result, SearchTtl);
        return result;
    }

    public async Task<IReadOnlyList<PlatformPackVersion>> VersionsAsync(
        string provider,
        string packId,
        CancellationToken cancellationToken)
    {
        var source = _registry.Get(provider);
        var key = $"{source.Id}|versions|{packId}";
        if (TryGet<IReadOnlyList<PlatformPackVersion>>(key, out var cached))
        {
            return cached;
        }

        var versions = await source
            .GetVersionsAsync(packId, cancellationToken)
            .ConfigureAwait(false);
        Put(key, versions, VersionsTtl);
        return versions;
    }

    public async Task<PackInstallResult> InstallAsync(
        string provider,
        string packId,
        string versionId,
        string? name,
        string? rootPath,
        CancellationToken cancellationToken)
    {
        var source = _registry.Get(provider);
        if (source is IPlatformPackArchiveProvider archiveProvider)
        {
            var archive = await archiveProvider
                .ResolveArchiveAsync(packId, versionId, cancellationToken)
                .ConfigureAwait(false);
            if (archive is not null)
            {
                return await _installer
                    .InstallDownloadedArchiveAsync(
                        archive.Url,
                        archive.Size,
                        archive.VersionId,
                        archive.Name,
                        archive.Summary,
                        archive.GameVersion,
                        source.Id,
                        packId,
                        versionId,
                        name,
                        rootPath,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        var index = await source
            .BuildIndexAsync(packId, versionId, cancellationToken)
            .ConfigureAwait(false);
        return await _installer
            .InstallIndexAsync(
                index,
                source.Id,
                packId,
                versionId,
                name,
                rootPath,
                cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Optional operator override for a self-hosted ATLauncher GraphQL mirror.
    /// The public endpoint is the default; an invalid override is ignored.
    /// </summary>
    private static Uri? ReadEndpoint(string name)
    {
        var value = Environment.GetEnvironmentVariable(name)?.Trim();
        return Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
               (uri.Scheme == Uri.UriSchemeHttps ||
                (uri.Scheme == Uri.UriSchemeHttp &&
                 (uri.Host == "localhost" || uri.Host == "127.0.0.1")))
            ? uri
            : null;
    }

    private bool TryGet<T>(string key, out T value)
    {
        if (_cache.TryGetValue(key, out var entry) && entry.Expires > DateTimeOffset.UtcNow)
        {
            value = (T)entry.Value;
            return true;
        }

        value = default!;
        return false;
    }

    private void Put<T>(string key, T value, TimeSpan ttl)
    {
        if (_cache.Count > 128)
        {
            var now = DateTimeOffset.UtcNow;
            foreach (var pair in _cache)
            {
                if (pair.Value.Expires <= now)
                {
                    _cache.TryRemove(pair.Key, out _);
                }
            }
        }

        _cache[key] = new CacheEntry(DateTimeOffset.UtcNow.Add(ttl), value!);
    }

    private sealed record CacheEntry(DateTimeOffset Expires, object Value);
}
