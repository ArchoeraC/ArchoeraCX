using Archoera.Application.Java;
using Archoera.Application.JavaRuntime;
using Archoera.Domain.Java;
using Archoera.Infrastructure.Settings;

namespace Archoera.Infrastructure.Java;

/// <summary>Stable Java selection failure code.</summary>
public sealed class JavaSelectionException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

/// <summary>
/// Resolves one usable Java runtime using the saved manual selection or the
/// regular machine scan. Shared by <c>java.select</c> and the pack installer so
/// both paths agree on version rules.
/// </summary>
public sealed class JavaRuntimeSelectionService
{
    private readonly IJavaScanner _scanner;
    private readonly JsonAppSettingsStore _settings;

    public JavaRuntimeSelectionService(
        IJavaScanner? scanner = null,
        JsonAppSettingsStore? settings = null)
    {
        _scanner = scanner ?? new JavaScanner();
        _settings = settings ?? new JsonAppSettingsStore();
    }

    /// <summary>
    /// Selects an exact or newer runtime. <paramref name="requiredMajor"/> wins
    /// over the coarse game-version inference.
    /// </summary>
    public async Task<JavaRuntimeInfo> SelectAsync(
        int? requiredMajor,
        string? gameVersion,
        CancellationToken cancellationToken = default,
        string? forcedJavaPath = null)
    {
        requiredMajor ??= InferMajor(gameVersion);
        var settings = await _settings
            .LoadAsync(cancellationToken)
            .ConfigureAwait(false);

        // An instance-level override arrives here: it must win over the global
        // manual selection, which is why it is checked first.
        var manualPath = string.IsNullOrWhiteSpace(forcedJavaPath)
            ? (settings.JavaAuto ? null : settings.JavaPath)
            : forcedJavaPath;

        IReadOnlyList<JavaRuntimeInfo> items;
        if (!string.IsNullOrWhiteSpace(manualPath))
        {
            items = _scanner.Scan(
                new JavaScanRequest
                {
                    IncludeDefaults = false,
                    Executables = [manualPath],
                },
                cancellationToken);
            if (items.Count == 0)
            {
                throw new JavaSelectionException(
                    "JAVA_MISSING",
                    $"configured Java is not usable: {manualPath}");
            }
        }
        else
        {
            // Launcher-installed runtimes live under <dataRoot>/runtime; the
            // automatic path must consider them like any other JVM.
            items = _scanner.Scan(
                new JavaScanRequest
                {
                    Roots = [ManagedJavaRuntimes.Root(settings.DataRoot)],
                },
                cancellationToken);
        }

        var selected = JavaSelector.Select(items, requiredMajor);
        if (selected is null)
        {
            throw new JavaSelectionException(
                requiredMajor is null ? "JAVA_MISSING" : "JAVA_VERSION_MISMATCH",
                requiredMajor is null
                    ? "no Java runtime found"
                    : $"no Java {requiredMajor}+ runtime found");
        }

        return selected;
    }

    /// <summary>Coarse Java requirement when the merged version lacks javaVersion.</summary>
    public static int? InferMajor(string? id)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;
        if (id.StartsWith("26.", StringComparison.Ordinal)) return 25;
        if (id.StartsWith("1.20.5", StringComparison.Ordinal) ||
            id.StartsWith("1.20.6", StringComparison.Ordinal) ||
            id.StartsWith("1.21", StringComparison.Ordinal)) return 21;
        if (id.StartsWith("1.18", StringComparison.Ordinal) ||
            id.StartsWith("1.19", StringComparison.Ordinal) ||
            id.StartsWith("1.20", StringComparison.Ordinal)) return 17;
        if (id.StartsWith("1.17", StringComparison.Ordinal)) return 16;
        if (id.StartsWith("1.16", StringComparison.Ordinal) ||
            id.StartsWith("1.15", StringComparison.Ordinal) ||
            id.StartsWith("1.14", StringComparison.Ordinal) ||
            id.StartsWith("1.13", StringComparison.Ordinal) ||
            id.StartsWith("1.12", StringComparison.Ordinal)) return 8;
        return null;
    }
}
