using Archoera.Application.Loaders;
using Archoera.Application.Modules;
using Archoera.Domain.Loaders;
using Archoera.Infrastructure.Modules;

namespace Archoera.Infrastructure.Loaders;

/// <summary>Registered handler for <c>loader.verify</c> / <c>loader.repair</c>.</summary>
public sealed class LoaderModuleService : IModuleMethodHandler
{
    private static readonly string[] SupportedMethods = ["loader.verify", "loader.repair"];

    private readonly IForgeArtifactPlanner _planner;
    private readonly ILoaderArtifactVerifier _verifier;
    private readonly IForgeRepairService _repairService;

    public LoaderModuleService(
        IForgeArtifactPlanner? planner = null,
        ILoaderArtifactVerifier? verifier = null,
        IForgeRepairService? repairService = null)
    {
        _planner = planner ?? new ForgeArtifactPlanner();
        _verifier = verifier ?? new LoaderArtifactVerifier();
        _repairService = repairService ?? new ForgeRepairService(
            new ForgeProcessorRunner(),
            _verifier);
    }

    public IReadOnlyCollection<string> Methods => SupportedMethods;

    public async Task<byte[]> HandleAsync(
        string method,
        string? paramsJson,
        CancellationToken cancellationToken)
    {
        if (!LoaderRequestJson.TryParse(paramsJson, out var request, out _, out _))
        {
            return ModuleJson.Error("E_INVALID", "loader params are not a valid request");
        }

        try
        {
            var librariesPath = Path.Combine(request!.Root, request.LibrariesPath);
            var manifest = await _planner
                .ParseAsync(
                    request.InstallerPath,
                    request.Root,
                    librariesPath,
                    request.Kind,
                    cancellationToken)
                .ConfigureAwait(false);

            var verification = _verifier.Verify(manifest, request.Root);
            if (string.Equals(method, "loader.verify", StringComparison.Ordinal))
            {
                return LoaderManifestJson.BuildEnvelope(manifest, verification);
            }

            var result = await _repairService
                .RepairAsync(
                    manifest,
                    request.Root,
                    request.JavaPath ?? string.Empty,
                    cancellationToken)
                .ConfigureAwait(false);

            return LoaderManifestJson.BuildEnvelope(manifest, result.Verification, result);
        }
        catch (LoaderManifestException exception)
        {
            return LoaderManifestJson.BuildError(exception.Code, exception.Message);
        }
    }
}
