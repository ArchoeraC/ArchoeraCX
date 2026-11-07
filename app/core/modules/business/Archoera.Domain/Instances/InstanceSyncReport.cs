namespace Archoera.Domain.Instances;

/// <summary>Result of reconciling configured instance roots with the index.</summary>
public sealed record InstanceSyncReport(
    IReadOnlyList<GameInstanceRecord> Items,
    IReadOnlyList<string> Adopted,
    IReadOnlyList<string> Updated,
    IReadOnlyList<InstanceSyncMissing> Missing,
    IReadOnlyList<InstanceSyncRoot> Roots);

public sealed record InstanceSyncMissing(string Id, string RootPath);

public sealed record InstanceSyncRoot(string Path, bool Exists, int InstanceCount);
