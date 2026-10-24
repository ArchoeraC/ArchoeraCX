namespace Archoera.Infrastructure.Launch;

internal sealed record LaunchPlan(
    string JavaPath,
    string GameDirectory,
    string LogDirectory,
    IReadOnlyList<string> Wrapper,
    IReadOnlyList<string> Arguments,
    IReadOnlyDictionary<string, string> Environment);
