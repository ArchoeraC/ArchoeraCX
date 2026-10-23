namespace Archoera.Application;

/// <summary>
/// Launcher version shared by every consumer-facing string: the module ABI
/// table, JVM launcher substitution, HTTP user agents and update checks.
/// <c>app/pubspec.yaml</c> is the single source of truth for this value
/// (the <c>version:</c> field); keep this constant in sync with it.
/// </summary>
public static class ArchoeraBuildInfo
{
    public const string Version = "0.0.1+1";
}
