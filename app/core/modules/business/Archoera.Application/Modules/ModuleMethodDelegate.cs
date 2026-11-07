namespace Archoera.Application.Modules;

/// <summary>
/// One dispatch entry inside a module handler. Handlers build their
/// <c>method → delegate</c> table once (exact method name, ordinal comparer)
/// and <see cref="IModuleMethodHandler.HandleAsync"/> resolves a call by an
/// O(1) dictionary lookup instead of walking a string switch.
/// Plain delegates only: no reflection, so NativeAOT stays static.
/// </summary>
public delegate Task<byte[]> ModuleMethodDelegate(
    string? paramsJson,
    CancellationToken cancellationToken);
