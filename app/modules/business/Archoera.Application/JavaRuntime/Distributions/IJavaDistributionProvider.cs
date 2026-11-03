namespace Archoera.Application.JavaRuntime;

/// <summary>
/// Metadata + archive resolver for one JDK vendor.
///
/// Catalogue responses never carry a download URL into the UI. The UI sends
/// the provider-native <c>versionId</c> back on install, and the provider
/// resolves the current URL/hash again so a stale or tampered list item can
/// never redirect the downloader.
/// </summary>
public interface IJavaDistributionProvider
{
    /// <summary>Stable id used by <c>java.distributions.*</c>.</summary>
    string Id { get; }

    /// <summary>Product name shown in the picker.</summary>
    string DisplayName { get; }

    /// <summary>Compact name used by provider buttons.</summary>
    string ShortName { get; }

    /// <summary>Publisher shown as attribution.</summary>
    string Vendor { get; }

    /// <summary>Public project page.</summary>
    string Homepage { get; }

    /// <summary>Whether the vendor publishes historical builds.</summary>
    bool ListsAllVersions { get; }

    /// <summary>Feature release preselected for Minecraft.</summary>
    int RecommendedMajor { get; }

    /// <summary>Feature releases that have a build for <paramref name="platform"/>.</summary>
    Task<IReadOnlyList<int>> GetMajorsAsync(
        JavaDistributionPlatform platform,
        CancellationToken cancellationToken);

    /// <summary>Builds for one feature release, newest first.</summary>
    Task<IReadOnlyList<JavaDistributionRelease>> GetReleasesAsync(
        int major,
        JavaDistributionPlatform platform,
        CancellationToken cancellationToken);

    /// <summary>
    /// Resolves a provider-native id to an exact archive. Implementations must
    /// verify the id against their own catalogue and return HTTPS + SHA-256.
    /// </summary>
    Task<JavaDistributionArchive> ResolveAsync(
        string versionId,
        JavaDistributionPlatform platform,
        CancellationToken cancellationToken);
}
