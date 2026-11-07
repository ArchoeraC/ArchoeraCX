namespace Archoera.Domain.Loaders;

/// <summary>
/// Identity of an installed loader transaction.
/// </summary>
/// <param name="Kind">Forge or NeoForge.</param>
/// <param name="GameVersion">Minecraft version, for example <c>1.20.1</c>.</param>
/// <param name="LoaderVersion">Loader version without the game-version prefix, for example <c>47.2.0</c>.</param>
/// <param name="InstallerSha1">Lower-case hex SHA-1 of the installer jar.</param>
public sealed record LoaderId(
    LoaderKind Kind,
    string GameVersion,
    string LoaderVersion,
    string InstallerSha1)
{
    /// <summary>
    /// Stable key used for per-loader repair locks. Never persisted as a path.
    /// </summary>
    public string LockKey =>
        $"{Kind}:{GameVersion}:{LoaderVersion}:{InstallerSha1}";
}
