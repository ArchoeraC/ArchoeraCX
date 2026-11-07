using Archoera.Domain.Java;

namespace Archoera.Infrastructure.Java;

/// <summary>Chooses one runtime for a required Java feature release.</summary>
internal static class JavaSelector
{
    public static JavaRuntimeInfo? Select(
        IReadOnlyList<JavaRuntimeInfo> runtimes,
        int? requiredMajor)
    {
        if (runtimes.Count == 0) return null;
        if (requiredMajor is null) return runtimes[0];

        var exact = runtimes.FirstOrDefault(runtime => runtime.Major == requiredMajor);
        if (exact is not null) return exact;

        return runtimes
            .Where(runtime => runtime.Major > requiredMajor)
            .OrderBy(runtime => runtime.Major)
            .FirstOrDefault();
    }
}
