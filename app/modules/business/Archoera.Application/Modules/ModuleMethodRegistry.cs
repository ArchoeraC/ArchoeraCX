namespace Archoera.Application.Modules;

/// <summary>
/// Deterministic registry for business-module methods. Registration is eager
/// and duplicate method names are rejected at startup, so two handlers can
/// never silently compete for the same public method.
/// </summary>
public sealed class ModuleMethodRegistry
{
    private readonly Dictionary<string, IModuleMethodHandler> _handlers;

    public ModuleMethodRegistry(IEnumerable<IModuleMethodHandler> handlers)
    {
        ArgumentNullException.ThrowIfNull(handlers);

        _handlers = new Dictionary<string, IModuleMethodHandler>(StringComparer.Ordinal);
        foreach (var handler in handlers)
        {
            ArgumentNullException.ThrowIfNull(handler);
            foreach (var method in handler.Methods)
            {
                if (string.IsNullOrWhiteSpace(method) || method.Contains('*', StringComparison.Ordinal))
                {
                    throw new ArgumentException($"invalid method name '{method}'");
                }

                if (!_handlers.TryAdd(method, handler))
                {
                    throw new InvalidOperationException(
                        $"method '{method}' is registered by more than one handler");
                }
            }
        }

        Methods = _handlers.Keys.Order(StringComparer.Ordinal).ToArray();
    }

    /// <summary>All public methods, sorted for diagnostics and tests.</summary>
    public IReadOnlyList<string> Methods { get; }

    public bool Supports(string? method)
        => method is not null && _handlers.ContainsKey(method);

    public bool TryGetHandler(string? method, out IModuleMethodHandler handler)
    {
        if (method is not null && _handlers.TryGetValue(method, out var found))
        {
            handler = found;
            return true;
        }

        handler = null!;
        return false;
    }

    public Task<byte[]> HandleAsync(
        string method,
        string? paramsJson,
        CancellationToken cancellationToken)
        => TryGetHandler(method, out var handler)
            ? handler.HandleAsync(method, paramsJson, cancellationToken)
            : throw new NotSupportedException($"unsupported module method '{method}'");
}
