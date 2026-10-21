namespace Archoera.Domain.Resources;

/// <summary>
/// Compatibility target for a resource. Version-level fields are the only
/// trusted source; provider project-level lists are historical unions and must
/// not be used for candidate selection (docs/08 §2.5, §3.2).
/// </summary>
/// <param name="GameVersion">
/// Exact Minecraft version, for example <c>1.21.1</c>. <c>1.21</c> does not
/// match <c>1.21.1</c>.
/// </param>
/// <param name="Loader">
/// Instance mod loader for <see cref="ResourceKind.Mod"/>:
/// <c>forge</c>, <c>neoforge</c>, <c>fabric</c> or <c>quilt</c>.
/// </param>
/// <param name="ShaderLoader">
/// Shader loader for <see cref="ResourceKind.Shader"/>: <c>iris</c> or
/// <c>optifine</c>. Never the instance mod loader.
/// </param>
/// <param name="IncludeSnapshots">
/// Snapshots/pre-releases/release candidates are excluded by default.
/// </param>
public sealed record ResourceTarget(
    string GameVersion,
    string? Loader = null,
    string? ShaderLoader = null,
    bool IncludeSnapshots = false)
{
    /// <summary>Supported instance mod loaders.</summary>
    public static IReadOnlyList<string> ModLoaders { get; } =
        ["forge", "neoforge", "fabric", "quilt"];

    /// <summary>Supported shader loaders.</summary>
    public static IReadOnlyList<string> ShaderLoaders { get; } =
        ["iris", "optifine"];

    /// <summary>Lower-case loader name, or <see langword="null"/>.</summary>
    public string? NormalizedLoader => Normalize(Loader);

    /// <summary>Lower-case shader loader name, or <see langword="null"/>.</summary>
    public string? NormalizedShaderLoader => Normalize(ShaderLoader);

    /// <summary>Target for a mod on an instance loader.</summary>
    public static ResourceTarget ForMod(string gameVersion, string loader)
        => new(gameVersion, loader);

    /// <summary>Target for a shader pack.</summary>
    public static ResourceTarget ForShader(string gameVersion, string shaderLoader)
        => new(gameVersion, ShaderLoader: shaderLoader);

    /// <summary>Target for a resource pack (<c>loaders=["minecraft"]</c>).</summary>
    public static ResourceTarget ForResourcePack(string gameVersion)
        => new(gameVersion);

    /// <summary>
    /// True when the target carries everything the kind needs: mods need a
    /// known loader, shaders need a known shader loader.
    /// </summary>
    public bool IsCompleteFor(ResourceKind kind) => kind switch
    {
        ResourceKind.Mod => NormalizedLoader is { } loader && ModLoaders.Contains(loader),
        ResourceKind.Shader => NormalizedShaderLoader is { } shader && ShaderLoaders.Contains(shader),
        ResourceKind.ResourcePack => true,
        _ => false,
    };

    private static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToLowerInvariant();
}
