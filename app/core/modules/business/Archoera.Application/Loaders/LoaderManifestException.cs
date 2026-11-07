using Archoera.Domain.Loaders;

namespace Archoera.Application.Loaders;

/// <summary>
/// Raised when an installer profile cannot be turned into a relocatable
/// manifest. The <see cref="Code"/> is stable and safe for the UI/log layer.
/// </summary>
public sealed class LoaderManifestException : Exception
{
    public LoaderManifestException(string code, string message)
        : base(message)
    {
        Code = code;
    }

    public LoaderManifestException(string code, string message, Exception innerException)
        : base(message, innerException)
    {
        Code = code;
    }

    /// <summary>Stable error code, for example <c>E_UNSUPPORTED_SPEC</c>.</summary>
    public string Code { get; }
}
