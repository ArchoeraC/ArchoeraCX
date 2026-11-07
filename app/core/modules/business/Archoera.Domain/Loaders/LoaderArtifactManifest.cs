namespace Archoera.Domain.Loaders;

/// <summary>
/// Relocatable description of every file a Forge/NeoForge install must
/// produce or extract, plus the processor chain that produces generated files.
/// </summary>
/// <remarks>
/// No path in this model is absolute. <see cref="InstallerPath"/> and
/// <see cref="VersionJsonPath"/> are relative to the instance root;
/// <see cref="LibrariesPath"/> is also relative to the root and locates the
/// libraries directory used by artifacts whose location is
/// <see cref="LoaderArtifactLocation.Libraries"/>.
/// </remarks>
public sealed record LoaderArtifactManifest
{
    /// <summary>Current on-disk schema version.</summary>
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    public required LoaderId LoaderId { get; init; }

    /// <summary>Installer jar path relative to the instance root.</summary>
    public required string InstallerPath { get; init; }

    /// <summary>
    /// True when the installer lives outside the instance root. In that case
    /// <see cref="InstallerPath"/> is only the file name and repair refuses to
    /// proceed until the installer is placed under the root.
    /// </summary>
    public bool InstallerOutsideRoot { get; init; }

    /// <summary>Merged version JSON target, relative to the instance root.</summary>
    public required string VersionJsonPath { get; init; }

    /// <summary>Version id used for the version JSON directory and launch entry.</summary>
    public required string VersionId { get; init; }

    /// <summary>Libraries directory, relative to the instance root.</summary>
    public string LibrariesPath { get; init; } = "libraries";

    public required IReadOnlyList<LoaderArtifact> Artifacts { get; init; }

    /// <summary>Number of client-side processors, equal to <see cref="Processors"/>.Count.</summary>
    public int ProcessorCount { get; init; }

    public IReadOnlyList<ProcessorStep> Processors { get; init; } = [];

    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}
