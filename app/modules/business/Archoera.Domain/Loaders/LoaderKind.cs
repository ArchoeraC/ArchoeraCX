namespace Archoera.Domain.Loaders;

/// <summary>
/// Mod loader families that use the Forge-style installer profile and
/// processor chain. Fabric/Quilt are intentionally absent: they need no
/// processors and no local artifact generation.
/// </summary>
public enum LoaderKind
{
    Forge,
    NeoForge,
}
