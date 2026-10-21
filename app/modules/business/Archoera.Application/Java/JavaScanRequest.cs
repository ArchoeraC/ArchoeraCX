namespace Archoera.Application.Java;

/// <summary>
/// Optional filters for a Java scan. Supplied by the caller through the
/// <c>java.scan</c> params object; every field is optional on the wire.
/// </summary>
/// <remarks>
/// <para><see cref="Roots"/> are directories searched recursively (bounded) for
/// <c>bin/java</c> and <c>jre/bin/java</c>. <see cref="Executables"/> are direct
/// candidate paths.</para>
/// <para>When <see cref="IncludeDefaults"/> is <see langword="false"/> only the
/// manual entries are considered, which lets the UI verify a single user
/// selection without paying for a full machine scan.</para>
/// </remarks>
public sealed record JavaScanRequest
{
    /// <summary>Directory roots to search. Empty means no manual roots.</summary>
    public IReadOnlyList<string> Roots { get; init; } = [];

    /// <summary>Direct Java executable paths. Empty means no manual executables.</summary>
    public IReadOnlyList<string> Executables { get; init; } = [];

    /// <summary>Whether environment/platform discovery runs before manual entries.</summary>
    public bool IncludeDefaults { get; init; } = true;

    /// <summary>A request that performs the regular machine-wide scan.</summary>
    public static JavaScanRequest Default { get; } = new();
}
