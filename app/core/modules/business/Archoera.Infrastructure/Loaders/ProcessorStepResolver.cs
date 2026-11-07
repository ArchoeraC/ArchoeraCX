using Archoera.Domain.Loaders;

namespace Archoera.Infrastructure.Loaders;

/// <summary>
/// Expands a manifest-relative processor step into an execution step with
/// absolute paths. Kept separate so manifests never contain machine paths.
/// </summary>
internal static class ProcessorStepResolver
{
    public static ProcessorStep Resolve(
        ProcessorStep step,
        string rootFull,
        string librariesFull,
        string installerFull,
        string minecraftJarFull)
    {
        var classPath = new string[step.ClassPath.Count];
        for (var index = 0; index < classPath.Length; index++)
        {
            classPath[index] = LoaderPaths.Combine(librariesFull, step.ClassPath[index]);
        }

        var arguments = new string[step.Arguments.Count];
        for (var index = 0; index < arguments.Length; index++)
        {
            arguments[index] = LoaderPaths.ExpandToken(
                step.Arguments[index],
                rootFull,
                librariesFull,
                installerFull,
                minecraftJarFull);
        }

        var outputs = new LoaderArtifact[step.Outputs.Count];
        for (var index = 0; index < outputs.Length; index++)
        {
            var output = step.Outputs[index];
            var baseFull = output.Location == LoaderArtifactLocation.Libraries
                ? librariesFull
                : rootFull;

            var fullPath = LoaderPaths.Combine(baseFull, output.RelativePath);
            outputs[index] = output with
            {
                Location = LoaderArtifactLocation.Root,
                RelativePath = fullPath,
            };
        }

        return step with
        {
            JarRelativePath = LoaderPaths.Combine(librariesFull, step.JarRelativePath),
            ClassPath = classPath,
            Arguments = arguments,
            Outputs = outputs,
        };
    }
}
