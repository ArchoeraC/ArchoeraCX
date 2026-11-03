namespace Archoera.Infrastructure.Packs.Platform;

/// <summary>
/// Registry of platform pack providers. Adding a platform is a registration,
/// not a new switch arm in every service or UI panel.
/// </summary>
internal sealed class PlatformPackRegistry
{
    private readonly Dictionary<string, IPlatformPackProvider> _providers;

    public PlatformPackRegistry(IEnumerable<IPlatformPackProvider> providers)
    {
        _providers = new Dictionary<string, IPlatformPackProvider>(StringComparer.OrdinalIgnoreCase);
        foreach (var provider in providers)
        {
            if (!_providers.TryAdd(provider.Id, provider))
            {
                throw new ArgumentException($"duplicate platform pack provider '{provider.Id}'");
            }
        }
    }

    /// <summary>Registration order is the UI order.</summary>
    public IReadOnlyList<IPlatformPackProvider> Providers
        => _providers.Values.ToArray();

    public bool TryGet(string? id, out IPlatformPackProvider provider)
    {
        provider = null!;
        return !string.IsNullOrWhiteSpace(id) &&
               _providers.TryGetValue(id.Trim(), out provider!);
    }

    public IPlatformPackProvider Get(string? id)
        => TryGet(id, out var provider)
            ? provider
            : throw new PlatformPackException(
                "PLATFORM_UNSUPPORTED",
                $"unsupported pack platform '{id}'");
}
