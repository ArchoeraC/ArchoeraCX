using Archoera.Domain.Loaders;

namespace Archoera.Infrastructure.Loaders;

/// <summary>
/// Decides whether an installer's client processor chain can run without a
/// JVM. A single uncovered step means the whole install needs Java, so the
/// plan reports it before the large download instead of failing afterwards.
/// </summary>
internal static class LoaderJavaCoverage
{
    /// <summary>
    /// Maps a processor jar coordinate to the main class the native handlers
    /// dispatch on.
    /// </summary>
    /// <remarks>
    /// Modern installers do not embed their processor jars, so the manifest is
    /// parsed before the plan downloads them and <see cref="ProcessorStep.MainClass"/>
    /// is empty. Repair re-reads the real jar manifest once the files exist;
    /// this table only answers the pre-download question. An unknown artifact
    /// stays empty and therefore counts as needing Java.
    /// </remarks>
    private static readonly Dictionary<string, (string Forge, string NeoForge)> MainClasses =
        new(StringComparer.Ordinal)
        {
            ["installertools"] = (
                "net.minecraftforge.installertools.ConsoleTool",
                "net.neoforged.installertools.ConsoleTool"),
            ["binarypatcher"] = (
                "net.minecraftforge.binarypatcher.ConsoleTool",
                "net.neoforged.binarypatcher.ConsoleTool"),
            ["jarsplitter"] = (
                "net.minecraftforge.jarsplitter.ConsoleTool",
                "net.neoforged.jarsplitter.ConsoleTool"),
            ["forgeautorenamingtool"] = (
                "net.minecraftforge.fart.Main",
                "net.minecraftforge.fart.Main"),
            ["autorenamingtool"] = (
                "net.minecraftforge.fart.Main",
                "net.neoforged.art.Main"),
        };

    public static (bool RequiresJava, IReadOnlyList<string> Steps) Evaluate(
        LoaderArtifactManifest manifest,
        string loaderRoot,
        string installerPath,
        string minecraftJar)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        var libraries = Path.Combine(loaderRoot, manifest.LibrariesPath);
        var native = new Native.NativeProcessorRunner();
        var steps = new List<string>();
        foreach (var processor in manifest.Processors)
        {
            var mainClass = string.IsNullOrWhiteSpace(processor.MainClass)
                ? InferMainClass(processor.JarRelativePath)
                : processor.MainClass;
            var resolved = ProcessorStepResolver.Resolve(
                processor with { MainClass = mainClass },
                loaderRoot,
                libraries,
                installerPath,
                minecraftJar);
            if (!native.CanRunWithoutJava(resolved))
            {
                steps.Add(Describe(processor, mainClass));
            }
        }

        return (steps.Count > 0, steps);
    }

    /// <summary>
    /// Best-effort main class for a processor that has not been downloaded yet,
    /// derived from its maven coordinate (<c>group/artifact/version/file.jar</c>).
    /// </summary>
    internal static string InferMainClass(string jarRelativePath)
    {
        var parts = jarRelativePath
            .Replace('\\', '/')
            .Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 4)
        {
            return string.Empty;
        }

        var artifact = parts[^3];
        var group = string.Join('.', parts[..^3]);
        if (!MainClasses.TryGetValue(artifact.ToLowerInvariant(), out var known))
        {
            return string.Empty;
        }

        // NeoForge publishes its tools under net.neoforged[.installertools];
        // Forge keeps net.minecraftforge.
        return group.StartsWith("net.neoforged", StringComparison.Ordinal)
            ? known.NeoForge
            : known.Forge;
    }

    /// <summary>
    /// Human-readable step label for the plan envelope, for example
    /// <c>1: net.neoforged.installertools.ConsoleTool --task DOWNLOAD_MOJMAPS</c>.
    /// </summary>
    private static string Describe(ProcessorStep processor, string mainClass)
    {
        var label = string.IsNullOrWhiteSpace(mainClass)
            ? Path.GetFileNameWithoutExtension(processor.JarRelativePath)
            : mainClass;
        var task = TaskArgument(processor.Arguments);
        return task is null
            ? $"{processor.Index}: {label}"
            : $"{processor.Index}: {label} --task {task}";
    }

    private static string? TaskArgument(IReadOnlyList<string> arguments)
    {
        for (var index = 0; index < arguments.Count - 1; index++)
        {
            if (string.Equals(arguments[index], "--task", StringComparison.Ordinal))
            {
                return arguments[index + 1];
            }
        }

        return null;
    }
}
