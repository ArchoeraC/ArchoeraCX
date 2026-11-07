namespace Archoera.Domain.Mojang;

/// <summary>One object in a Mojang asset index.</summary>
/// <param name="Hash">Lower-case SHA-1, used as the content address.</param>
/// <param name="Size">Object size in bytes; null means malformed metadata for the plan builder.</param>
public sealed record MojangAssetObject(string Hash, long? Size);

/// <summary>
/// <c>assets/indexes/&lt;id&gt;.json</c>. <see cref="Virtual"/> and
/// <see cref="MapToResources"/> only affect the later C# materialization step;
/// download destinations are always content-addressed.
/// </summary>
/// <param name="Id">Index id from the version JSON.</param>
/// <param name="Objects">Logical asset path to content hash/size.</param>
/// <param name="Virtual">Legacy virtual assets flag.</param>
/// <param name="MapToResources">Legacy resources mapping flag.</param>
public sealed record MojangAssetIndex(
    string Id,
    IReadOnlyDictionary<string, MojangAssetObject> Objects,
    bool Virtual = false,
    bool MapToResources = false);
