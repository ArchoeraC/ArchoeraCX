namespace Archoera.Domain.Mojang;

/// <summary>Download entries of a library (<c>artifact</c> + <c>classifiers</c>).</summary>
public sealed record MojangLibraryDownloads(
    MojangDownload? Artifact,
    IReadOnlyDictionary<string, MojangDownload>? Classifiers = null)
{
    /// <summary>Empty classifier map when the library has none.</summary>
    public IReadOnlyDictionary<string, MojangDownload> ClassifierMap
        => Classifiers ?? EmptyClassifiers;

    private static readonly IReadOnlyDictionary<string, MojangDownload> EmptyClassifiers =
        new Dictionary<string, MojangDownload>(StringComparer.OrdinalIgnoreCase);
}

/// <summary><c>extract</c> block of a legacy native library.</summary>
/// <param name="Exclude">Archive entries the C# extraction step must skip.</param>
public sealed record MojangExtract(IReadOnlyList<string> Exclude);

/// <summary>
/// One <c>libraries[]</c> entry. Rules and natives are evaluated by the plan
/// builder; extraction is explicitly a C# post-download step.
/// </summary>
/// <param name="Name">Maven coordinate, for example <c>org.lwjgl:lwjgl:3.3.3</c>.</param>
/// <param name="Downloads">Artifact/classifier downloads; null for local-only entries.</param>
/// <param name="Natives">Legacy OS-to-classifier map, for example <c>linux -&gt; natives-linux</c>.</param>
/// <param name="Rules">Eligibility rules; null/empty means allow.</param>
/// <param name="Extract">Native extraction hints.</param>
public sealed record MojangLibrary(
    string Name,
    MojangLibraryDownloads? Downloads = null,
    IReadOnlyDictionary<string, string>? Natives = null,
    IReadOnlyList<MojangRule>? Rules = null,
    MojangExtract? Extract = null);
