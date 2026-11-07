namespace Archoera.Application.Modules;

/// <summary>
/// One registrable method family inside the native business module. A handler
/// owns an exact, stable set of method names; the module export layer never
/// hard-codes those names itself.
/// </summary>
public interface IModuleMethodHandler
{
    /// <summary>Exact public method names owned by this handler.</summary>
    IReadOnlyCollection<string> Methods { get; }

    /// <summary>
    /// Handle one method and return an already-serialized UTF-8 JSON envelope.
    /// Throwing <see cref="OperationCanceledException"/> maps to a cancelled
    /// module callback; any other exception is converted to E_INTERNAL by the
    /// export boundary.
    /// </summary>
    Task<byte[]> HandleAsync(
        string method,
        string? paramsJson,
        CancellationToken cancellationToken);
}
