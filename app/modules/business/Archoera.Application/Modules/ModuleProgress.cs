namespace Archoera.Application.Modules;

/// <summary>
/// Ambient progress sink for module methods. The native export adapter pushes a
/// reporter around one module call; handlers may call <see cref="Report"/> at
/// any await point without threading a callback through every interface.
/// </summary>
public static class ModuleProgress
{
    private static readonly AsyncLocal<Action<long, long, long, string?>?> Current = new();

    public static IDisposable Begin(Action<long, long, long, string?> reporter)
    {
        ArgumentNullException.ThrowIfNull(reporter);
        var previous = Current.Value;
        Current.Value = reporter;
        return new Scope(previous);
    }

    public static void Report(
        long done,
        long total,
        long bytesPerSecond = 0,
        string? message = null)
    {
        Current.Value?.Invoke(done, total, bytesPerSecond, message);
    }

    private sealed class Scope(Action<long, long, long, string?>? previous) : IDisposable
    {
        public void Dispose() => Current.Value = previous;
    }
}
