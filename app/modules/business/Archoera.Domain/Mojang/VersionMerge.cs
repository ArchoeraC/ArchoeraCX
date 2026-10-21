using System.Text.Json;

namespace Archoera.Domain.Mojang;

/// <summary>
/// One layer of an <c>inheritsFrom</c> chain. <see cref="Json"/> is an owned
/// copy of the layer's UTF-8 JSON text, so the layer never depends on a
/// <see cref="JsonDocument"/> lifetime. <see cref="Source"/> is diagnostic only
/// (for example <c>manifest</c> or <c>fabric-profile</c>).
/// </summary>
/// <param name="Id">Version id used to resolve the layer.</param>
/// <param name="Json">Complete layer JSON, exactly as received.</param>
/// <param name="Source">Where the layer came from; never interpreted as a path.</param>
public sealed record VersionLayer(string Id, string Json, string Source = "inline")
{
    /// <summary>Copies a parsed element into an owned JSON string.</summary>
    public static VersionLayer FromJsonElement(string id, JsonElement element, string source = "inline")
        => new(id, element.GetRawText(), source);
}

/// <summary>Stable warning codes emitted by the merge pass.</summary>
public static class VersionMergeWarningCode
{
    /// <summary>A later layer replaced a library with a different version.</summary>
    public const string LibraryOverride = "LIBRARY_OVERRIDE";

    /// <summary>A library <c>name</c> could not be split into Maven coordinates.</summary>
    public const string LibraryCoordinateUnrecognized = "LIBRARY_COORDINATE_UNRECOGNIZED";

    /// <summary>Legacy <c>minecraftArguments</c> was tokenized into <c>arguments.game</c>.</summary>
    public const string LegacyArgumentsConverted = "LEGACY_ARGUMENTS_CONVERTED";

    /// <summary>Legacy <c>minecraftArguments</c> was ignored because the same layer has structured game arguments.</summary>
    public const string LegacyArgumentsIgnored = "LEGACY_ARGUMENTS_IGNORED";
}

/// <summary>Stable error codes raised by chain resolution and merging.</summary>
public static class VersionMergeErrorCode
{
    /// <summary>The caller supplied an empty id/lookup or an empty chain.</summary>
    public const string InvalidRequest = "INVALID_REQUEST";

    /// <summary><c>inheritsFrom</c> points at an id the lookup could not resolve.</summary>
    public const string ChainMissing = "CHAIN_MISSING";

    /// <summary>The chain revisits an id.</summary>
    public const string ChainCycle = "CHAIN_CYCLE";

    /// <summary>The chain has more layers than the supported depth.</summary>
    public const string ChainTooDeep = "CHAIN_TOO_DEEP";

    /// <summary>A layer's <c>inheritsFrom</c> equals its own id.</summary>
    public const string ChainSelfReference = "CHAIN_SELF_REFERENCE";

    /// <summary>The lookup returned a layer whose id differs from the requested id.</summary>
    public const string ChainIdMismatch = "CHAIN_ID_MISMATCH";

    /// <summary>A layer is not a JSON object or is malformed.</summary>
    public const string InvalidLayerJson = "INVALID_LAYER_JSON";

    /// <summary><c>libraries</c> is not an array of objects with a string <c>name</c>.</summary>
    public const string InvalidLibraries = "INVALID_LIBRARIES";

    /// <summary><c>arguments</c> has an unexpected shape.</summary>
    public const string InvalidArguments = "INVALID_ARGUMENTS";

    /// <summary>Legacy <c>minecraftArguments</c> has an unterminated quote.</summary>
    public const string InvalidLegacyArguments = "INVALID_LEGACY_ARGUMENTS";

    /// <summary>The merged bytes were rejected by <c>MojangJson.ParseVersion</c>.</summary>
    public const string InvalidMergedVersion = "INVALID_MERGED_VERSION";
}

/// <summary>Merge algorithm version; bump when merge semantics change.</summary>
public static class VersionMergeSchema
{
    /// <summary>Current merge schema (docs/08 01 §1).</summary>
    public const int Current = 1;
}

/// <summary>
/// One diagnostic produced while flattening the chain. Warnings never change
/// the launch decision; they exist so overrides and legacy conversions stay
/// visible in diagnostics.
/// </summary>
/// <param name="Code">One of <see cref="VersionMergeWarningCode"/>.</param>
/// <param name="Message">Human-readable summary; safe for logs.</param>
/// <param name="LibraryKey">Coordinate key for library warnings, otherwise null.</param>
/// <param name="OldValue">Previous value for override warnings, otherwise null.</param>
/// <param name="NewValue">Replacement value for override warnings, otherwise null.</param>
/// <param name="LayerId">Layer that produced the warning, when applicable.</param>
public sealed record MergeWarning(
    string Code,
    string Message,
    string? LibraryKey = null,
    string? OldValue = null,
    string? NewValue = null,
    string? LayerId = null);

/// <summary>
/// A structurally merged version: rules and <c>${...}</c> placeholders are
/// deliberately left unevaluated (docs/08 01 §3.4). <see cref="Json"/> is the
/// exact compact UTF-8 document that was validated; <see cref="Version"/> is
/// its parsed form and never has <c>inheritsFrom</c>.
/// </summary>
/// <param name="Id">Effective id (leaf JSON id, or the leaf layer id when absent).</param>
/// <param name="Chain">Resolved chain ids in root-to-leaf order.</param>
/// <param name="Json">Compact effective-version JSON.</param>
/// <param name="Warnings">Deterministic merge diagnostics.</param>
/// <param name="Version">Parsed effective version, safe for the plan builder.</param>
/// <param name="MergeSchema">Merge algorithm version.</param>
public sealed record EffectiveVersion(
    string Id,
    IReadOnlyList<string> Chain,
    byte[] Json,
    IReadOnlyList<MergeWarning> Warnings,
    MojangVersion Version,
    int MergeSchema = VersionMergeSchema.Current);
