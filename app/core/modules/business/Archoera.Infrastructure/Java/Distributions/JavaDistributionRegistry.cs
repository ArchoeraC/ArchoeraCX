using Archoera.Application.JavaRuntime;

namespace Archoera.Infrastructure.Java.Distributions;

/// <summary>
/// Registration order is the UI order. Adding another vendor touches only the
/// composition root, never the module facade or the page.
/// </summary>
internal sealed class JavaDistributionRegistry
{
    private readonly Dictionary<string, IJavaDistributionProvider> _providers;

    public JavaDistributionRegistry(IEnumerable<IJavaDistributionProvider> providers)
    {
        _providers = new Dictionary<string, IJavaDistributionProvider>(
            StringComparer.OrdinalIgnoreCase);
        foreach (var provider in providers)
        {
            if (!_providers.TryAdd(provider.Id, provider))
            {
                throw new ArgumentException(
                    $"duplicate Java distribution provider '{provider.Id}'");
            }
        }
    }

    public IReadOnlyList<IJavaDistributionProvider> Providers
        => _providers.Values.ToArray();

    public IJavaDistributionProvider Get(string? id)
    {
        if (!string.IsNullOrWhiteSpace(id) &&
            _providers.TryGetValue(id.Trim(), out var provider))
        {
            return provider;
        }

        throw new JavaDistributionException(
            "JAVA_PROVIDER_UNSUPPORTED",
            $"unsupported Java distribution '{id}'");
    }
}
