namespace Archoera.Domain.Instances;

/// <summary>
/// Outcome of removing an instance from the registry. When the caller also asks
/// for the local files to be deleted, <paramref name="FilesDeleted"/> reports
/// whether the instance directory is gone and <paramref name="FilesSkippedReason"/>
/// explains why it was kept.
/// </summary>
public sealed record InstanceDeleteResult(
    bool Deleted,
    bool FilesDeleted,
    string? FilesSkippedReason = null);

/// <summary>Stable reason codes for a kept instance directory.</summary>
public static class InstanceDeleteSkipReason
{
    /// <summary>The instance directory no longer exists on disk.</summary>
    public const string Missing = "missing";

    /// <summary>The path is a filesystem root or otherwise unsafe to remove.</summary>
    public const string UnsafePath = "unsafe_path";

    /// <summary>The instance directory is a symbolic link.</summary>
    public const string Symlink = "symlink";

    /// <summary>No <c>.archoera/instance.json</c> marker, so ArchoeraCX does not own it.</summary>
    public const string NotManaged = "not_managed";

    /// <summary>The directory is a configured instance root shared by other instances.</summary>
    public const string SharedRoot = "shared_root";

    /// <summary>The directory contains another registered instance.</summary>
    public const string NestedInstance = "nested_instance";

    /// <summary>The filesystem refused the delete.</summary>
    public const string Failed = "failed";
}
