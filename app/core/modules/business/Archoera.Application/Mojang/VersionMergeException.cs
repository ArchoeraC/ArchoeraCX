using Archoera.Domain.Mojang;

namespace Archoera.Application.Mojang;

/// <summary>
/// Chain-resolution/merge failure with a stable code from
/// <see cref="VersionMergeErrorCode"/>. Messages contain version ids and layer
/// names only, never credentials or file contents.
/// </summary>
public sealed class VersionMergeException : Exception
{
    public VersionMergeException(string code, string message)
        : base(message)
    {
        Code = code;
    }

    public VersionMergeException(string code, string message, Exception innerException)
        : base(message, innerException)
    {
        Code = code;
    }

    /// <summary>One of <see cref="VersionMergeErrorCode"/>.</summary>
    public string Code { get; }
}
