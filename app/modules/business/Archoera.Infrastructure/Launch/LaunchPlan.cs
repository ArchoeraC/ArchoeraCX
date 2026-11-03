namespace Archoera.Infrastructure.Launch;

/// <summary>authlib-injector pieces injected into the game JVM.</summary>
internal sealed record AuthlibLaunchArguments(
    string JarPath,
    string Server,
    string PrefetchedMetadata);

internal sealed record LaunchPlan(
    string JavaPath,
    string GameDirectory,
    string LogDirectory,
    IReadOnlyList<string> Wrapper,
    IReadOnlyList<string> Arguments,
    IReadOnlyDictionary<string, string> Environment);
