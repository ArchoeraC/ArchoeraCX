using System.Collections.Concurrent;
using System.IO.Compression;
using System.Security.Cryptography;
using Archoera.Application.Loaders;
using Archoera.Application.Modules;
using Archoera.Domain.Loaders;

namespace Archoera.Infrastructure.Loaders;

/// <summary>
/// Extract-only + processor repair. It never downloads, never writes outside
/// the root/libraries tree, and skips already-valid processor outputs.
/// </summary>
public sealed class ForgeRepairService : IForgeRepairService
{
    private static readonly TimeSpan DefaultProcessorTimeout = TimeSpan.FromMinutes(10);
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> LoaderLocks = new(StringComparer.Ordinal);

    private readonly IProcessorRunner _runner;
    private readonly ILoaderArtifactVerifier _verifier;
    private readonly TimeSpan _processorTimeout;

    public ForgeRepairService(
        IProcessorRunner runner,
        ILoaderArtifactVerifier verifier,
        TimeSpan? processorTimeout = null)
    {
        _runner = runner ?? throw new ArgumentNullException(nameof(runner));
        _verifier = verifier ?? throw new ArgumentNullException(nameof(verifier));
        _processorTimeout = processorTimeout is { } timeout && timeout > TimeSpan.Zero
            ? timeout
            : DefaultProcessorTimeout;
    }

    public async Task<LoaderRepairResult> RepairAsync(
        LoaderArtifactManifest manifest,
        string root,
        string javaPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentException.ThrowIfNullOrWhiteSpace(root);

        var gate = LoaderLocks.GetOrAdd(
            manifest.LoaderId.LockKey,
            static _ => new SemaphoreSlim(1, 1));

        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await RepairCoreAsync(manifest, root, javaPath, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<LoaderRepairResult> RepairCoreAsync(
        LoaderArtifactManifest manifest,
        string root,
        string javaPath,
        CancellationToken cancellationToken)
    {
        var rootFull = Path.GetFullPath(root);
        var details = new List<string>();
        var processorsRun = new List<int>();

        if (manifest.SchemaVersion != LoaderArtifactManifest.CurrentSchemaVersion)
        {
            details.Add($"unsupported manifest schema {manifest.SchemaVersion}");
            return Failed(manifest, rootFull, processorsRun, details);
        }

        if (!LoaderPaths.TryResolveWithin(
                rootFull,
                manifest.LibrariesPath,
                out var librariesFull,
                out _,
                out var librariesError))
        {
            details.Add($"unsafe libraries path '{manifest.LibrariesPath}': {librariesError}");
            return Failed(manifest, rootFull, processorsRun, details);
        }

        var initial = _verifier.Verify(manifest, rootFull);
        if (initial.Ok)
        {
            return new LoaderRepairResult(
                LoaderRepairStatus.AlreadyValid,
                initial,
                [],
                ["all loader artifacts are already valid"]);
        }

        if ((initial.Unsafe?.Count ?? 0) > 0)
        {
            details.Add("manifest contains unsafe artifact paths; repair will not write anything");
            return new LoaderRepairResult(
                LoaderRepairStatus.Failed,
                initial,
                [],
                details);
        }

        if (!LoaderPaths.TryResolveWithin(
                rootFull,
                manifest.InstallerPath,
                out var installerFull,
                out _,
                out var installerPathError))
        {
            details.Add($"unsafe installer path '{manifest.InstallerPath}': {installerPathError}");
            return Failed(manifest, rootFull, processorsRun, details);
        }

        if (!File.Exists(installerFull))
        {
            details.Add(manifest.InstallerOutsideRoot
                ? $"installer '{manifest.InstallerPath}' is outside the instance root; place it under the root before repair"
                : $"installer '{manifest.InstallerPath}' is missing");
            return Failed(manifest, rootFull, processorsRun, details);
        }

        if (!string.IsNullOrWhiteSpace(manifest.LoaderId.InstallerSha1))
        {
            var actualInstallerSha1 = await LoaderPaths
                .Sha1FileAsync(installerFull, cancellationToken)
                .ConfigureAwait(false);

            if (!string.Equals(
                    actualInstallerSha1,
                    manifest.LoaderId.InstallerSha1,
                    StringComparison.OrdinalIgnoreCase))
            {
                details.Add(
                    $"installer SHA-1 mismatch: expected {manifest.LoaderId.InstallerSha1}, actual {actualInstallerSha1}");
                return Failed(manifest, rootFull, processorsRun, details);
            }
        }

        ZipArchive archive;
        try
        {
            archive = ZipFile.OpenRead(installerFull);
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException)
        {
            details.Add($"installer is not a readable zip/jar: {exception.Message}");
            return Failed(manifest, rootFull, processorsRun, details);
        }

        var changed = false;
        try
        {
            foreach (var artifact in manifest.Artifacts)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (artifact.Kind != LoaderArtifactKind.Extracted ||
                    IsArtifactValid(rootFull, librariesFull, artifact, out _))
                {
                    continue;
                }

                if (!await ExtractArtifactAsync(
                        archive,
                        rootFull,
                        librariesFull,
                        artifact,
                        cancellationToken).ConfigureAwait(false))
                {
                    details.Add($"failed to extract: {Display(artifact)}");
                    return Failed(manifest, rootFull, processorsRun, details);
                }

                changed = true;
                details.Add($"extracted: {Display(artifact)}");
            }
        }
        finally
        {
            archive.Dispose();
        }

        var afterExtract = _verifier.Verify(manifest, rootFull);
        if (afterExtract.Ok)
        {
            return new LoaderRepairResult(
                LoaderRepairStatus.Repaired,
                afterExtract,
                [],
                details);
        }

        var firstNeeded = FindFirstIncompleteProcessor(manifest, rootFull);
        if (firstNeeded < 0)
        {
            details.Add("no processor can produce the remaining missing/mismatched artifacts");
            return Failed(manifest, rootFull, processorsRun, details);
        }

        if (!File.Exists(javaPath))
        {
            details.Add(string.IsNullOrWhiteSpace(javaPath)
                ? "a Java executable is required to run loader processors"
                : "the supplied Java executable does not exist");
            return Failed(manifest, rootFull, processorsRun, details);
        }

        var minecraftJarFull = Path.GetFullPath(Path.Combine(
            rootFull,
            "versions",
            manifest.LoaderId.GameVersion,
            $"{manifest.LoaderId.GameVersion}.jar"));
        if (!File.Exists(minecraftJarFull) &&
            !string.IsNullOrWhiteSpace(manifest.VersionId))
        {
            // The merged loader profile downloads the vanilla client under its
            // own version id. Forge processors only need the same bytes, so
            // fall back to that copy instead of requiring a duplicate vanilla
            // versions/<game>/<game>.jar.
            var mergedJar = Path.GetFullPath(Path.Combine(
                rootFull,
                "versions",
                manifest.VersionId,
                $"{manifest.VersionId}.jar"));
            if (File.Exists(mergedJar))
            {
                minecraftJarFull = mergedJar;
            }
        }

        var runSteps = manifest.Processors.Skip(firstNeeded).ToArray();
        // Some installers do not list processor outputs (NeoForge 21.1 leaves
        // producerIndex=-1 for MC_SLIM/MC_EXTRA). In addition to declared
        // outputs, treat arguments following output flags as files this run will
        // create, so a later step's input is allowed to be absent up front.
        var plannedOutputs = new HashSet<string>(
            runSteps
                .SelectMany(processor => OutputsForProcessor(manifest, processor))
                .Select(output => ResolveArtifactPath(rootFull, librariesFull, output)),
            PathComparer);
        foreach (var processor in runSteps)
        {
            AddOutputArgumentPaths(
                processor,
                rootFull,
                librariesFull,
                installerFull,
                minecraftJarFull,
                plannedOutputs);
        }

        foreach (var processor in runSteps)
        {
            if (!CheckProcessorInputs(
                    processor,
                    rootFull,
                    librariesFull,
                    installerFull,
                    minecraftJarFull,
                    plannedOutputs,
                    out var inputError))
            {
                details.Add($"processor {processor.Index} input check failed: {inputError}");
                return Failed(manifest, rootFull, processorsRun, details);
            }
        }

        var totalProcessors = manifest.Processors.Count;
        foreach (var processor in runSteps)
        {
            cancellationToken.ThrowIfCancellationRequested();

            ModuleProgress.Report(
                processor.Index,
                Math.Max(1, totalProcessors),
                0,
                $"Running processor {processor.Index + 1}/{totalProcessors}");
            DeleteInvalidOutputs(manifest, rootFull, librariesFull, processor);
            var resolved = ProcessorStepResolver.Resolve(
                processor,
                rootFull,
                librariesFull,
                installerFull,
                minecraftJarFull);

            details.Add($"processor {processor.Index} started: {processor.MainClass}");

            ProcessorRunResult result;
            try
            {
                result = await _runner.RunAsync(
                        resolved,
                        javaPath,
                        rootFull,
                        _processorTimeout,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                details.Add($"processor {processor.Index} failed to start: {exception.Message}");
                return Failed(manifest, rootFull, processorsRun, details);
            }

            if (result.TimedOut)
            {
                details.Add($"processor {processor.Index} timed out");
                return Failed(manifest, rootFull, processorsRun, details);
            }

            if (result.ExitCode != 0)
            {
                details.Add($"processor {processor.Index} exited with code {result.ExitCode}");
                var errorTail = Tail(result.StandardError, 2000);
                if (!string.IsNullOrWhiteSpace(errorTail))
                {
                    details.Add($"processor {processor.Index} stderr: {errorTail}");
                }

                return Failed(manifest, rootFull, processorsRun, details);
            }

            processorsRun.Add(processor.Index);
            changed = true;
            ModuleProgress.Report(
                processor.Index + 1,
                Math.Max(1, totalProcessors),
                0,
                $"Processor {processor.Index + 1}/{totalProcessors} complete");

            var outputVerification = _verifier.Verify(
                manifest with { Artifacts = OutputsForProcessor(manifest, processor) },
                rootFull);

            if (!outputVerification.Ok)
            {
                details.Add($"processor {processor.Index} output verification failed");
                details.AddRange(outputVerification.Details);
                return new LoaderRepairResult(
                    LoaderRepairStatus.Failed,
                    outputVerification,
                    processorsRun,
                    details);
            }

            details.Add($"processor {processor.Index} outputs verified");
        }

        var final = _verifier.Verify(manifest, rootFull);
        return new LoaderRepairResult(
            final.Ok && changed ? LoaderRepairStatus.Repaired : LoaderRepairStatus.Failed,
            final,
            processorsRun,
            final.Ok ? details : [.. details, .. final.Details]);
    }

    private int FindFirstIncompleteProcessor(LoaderArtifactManifest manifest, string rootFull)
    {
        for (var position = 0; position < manifest.Processors.Count; position++)
        {
            var processor = manifest.Processors[position];
            var outputs = OutputsForProcessor(manifest, processor);
            if (outputs.Count == 0)
            {
                continue;
            }

            var verification = _verifier.Verify(
                manifest with { Artifacts = outputs },
                rootFull);
            if (!verification.Ok)
            {
                return position;
            }
        }

        return -1;
    }

    /// <summary>
    /// Some installers do not list a generated artifact in processors[].outputs
    /// (Forge 1.20.1 and NeoForge 21.1 are the important cases). The manifest
    /// still records it with producerIndex, so repair must verify/delete the
    /// same set the processor is expected to produce.
    /// </summary>
    private static IReadOnlyList<LoaderArtifact> OutputsForProcessor(
        LoaderArtifactManifest manifest,
        ProcessorStep processor)
    {
        var results = new List<LoaderArtifact>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var artifact in processor.Outputs.Concat(
                     manifest.Artifacts.Where(
                         artifact => artifact.ProducerIndex == processor.Index &&
                                     artifact.Kind != LoaderArtifactKind.Extracted)))
        {
            var identity = $"{artifact.Location}:{artifact.RelativePath}";
            if (seen.Add(identity))
            {
                results.Add(artifact);
            }
        }

        return results;
    }

    private static void AddOutputArgumentPaths(
        ProcessorStep processor,
        string rootFull,
        string librariesFull,
        string installerFull,
        string minecraftJarFull,
        HashSet<string> outputPaths)
    {
        for (var index = 0; index + 1 < processor.Arguments.Count; index++)
        {
            var flag = processor.Arguments[index];
            if (flag is not ("--output" or "--slim" or "--extra"))
            {
                continue;
            }

            var raw = processor.Arguments[index + 1];
            if (!LooksLikePathArgument(raw)) continue;
            var expanded = LoaderPaths.ExpandToken(
                raw,
                rootFull,
                librariesFull,
                installerFull,
                minecraftJarFull);
            try
            {
                outputPaths.Add(Path.GetFullPath(expanded));
            }
            catch (Exception exception) when (
                exception is ArgumentException or NotSupportedException or PathTooLongException)
            {
                // Input checking will report the unsafe path later.
            }
        }
    }

    private static bool CheckProcessorInputs(
        ProcessorStep processor,
        string rootFull,
        string librariesFull,
        string installerFull,
        string minecraftJarFull,
        HashSet<string> plannedOutputs,
        out string error)
    {
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(processor.MainClass))
        {
            error = "processor jar is missing or has no Main-Class";
            return false;
        }

        if (!LoaderPaths.TryResolveWithin(
                librariesFull,
                processor.JarRelativePath,
                out var jarPath,
                out _,
                out var jarError))
        {
            error = $"unsafe processor jar path: {jarError}";
            return false;
        }

        if (!File.Exists(jarPath))
        {
            error = $"processor jar is missing: {processor.JarRelativePath}";
            return false;
        }

        foreach (var entry in processor.ClassPath)
        {
            if (!LoaderPaths.TryResolveWithin(
                    librariesFull,
                    entry,
                    out var classPathEntry,
                    out _,
                    out var classPathError))
            {
                error = $"unsafe classpath entry: {classPathError}";
                return false;
            }

            if (!File.Exists(classPathEntry))
            {
                error = $"classpath entry is missing: {entry}";
                return false;
            }
        }

        foreach (var argument in processor.Arguments)
        {
            if (!LooksLikePathArgument(argument))
            {
                continue;
            }

            var expanded = LoaderPaths.ExpandToken(
                argument,
                rootFull,
                librariesFull,
                installerFull,
                minecraftJarFull);

            var full = Path.GetFullPath(expanded);
            if (!File.Exists(full) && !plannedOutputs.Contains(full))
            {
                error = $"processor input is missing: {argument}";
                return false;
            }
        }

        return true;
    }

    private static bool LooksLikePathArgument(string argument)
    {
        return argument == LoaderPaths.MinecraftJarToken ||
               argument == LoaderPaths.InstallerToken ||
               argument.StartsWith(LoaderPaths.RootToken + "/", StringComparison.Ordinal) ||
               argument.StartsWith(LoaderPaths.LibrariesToken + "/", StringComparison.Ordinal);
    }

    private async Task<bool> ExtractArtifactAsync(
        ZipArchive archive,
        string rootFull,
        string librariesFull,
        LoaderArtifact artifact,
        CancellationToken cancellationToken)
    {
        const string prefix = "installer:";
        if (!artifact.Key.StartsWith(prefix, StringComparison.Ordinal))
        {
            return false;
        }

        var entryName = artifact.Key[prefix.Length..];
        var entry = ForgeArtifactPlanner.FindEntry(archive, entryName);
        if (entry is null)
        {
            return false;
        }

        var baseFull = artifact.Location == LoaderArtifactLocation.Libraries
            ? librariesFull
            : rootFull;

        if (!LoaderPaths.TryResolveWithin(
                baseFull,
                artifact.RelativePath,
                out var target,
                out _,
                out _))
        {
            return false;
        }

        var directory = Path.GetDirectoryName(target);
        if (string.IsNullOrEmpty(directory))
        {
            return false;
        }

        try
        {
            Directory.CreateDirectory(directory);
            var temporary = Path.Combine(directory, $".{Path.GetFileName(target)}.tmp-{Guid.NewGuid():N}");
            long written = 0;
            string actualSha1;

            try
            {
                using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA1);
                await using (var source = entry.Open())
                await using (var destination = new FileStream(
                                 temporary,
                                 FileMode.CreateNew,
                                 FileAccess.Write,
                                 FileShare.None,
                                 bufferSize: 81920,
                                 useAsync: true))
                {
                    var buffer = new byte[81920];
                    int read;
                    while ((read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
                    {
                        hash.AppendData(buffer, 0, read);
                        await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken)
                            .ConfigureAwait(false);
                        written += read;
                    }
                }

                actualSha1 = LoaderPaths.Hex(hash.GetHashAndReset());
            }
            catch
            {
                TryDelete(temporary);
                throw;
            }

            if (artifact.Size is not null && artifact.Size.Value != written)
            {
                TryDelete(temporary);
                return false;
            }

            if (artifact.HasSha1 &&
                !string.Equals(actualSha1, artifact.Sha1, StringComparison.OrdinalIgnoreCase))
            {
                TryDelete(temporary);
                return false;
            }

            File.Move(temporary, target, overwrite: true);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private void DeleteInvalidOutputs(
        LoaderArtifactManifest manifest,
        string rootFull,
        string librariesFull,
        ProcessorStep processor)
    {
        foreach (var output in OutputsForProcessor(manifest, processor))
        {
            if (IsArtifactValid(rootFull, librariesFull, output, out var fullPath))
            {
                continue;
            }

            TryDelete(fullPath);
        }
    }

    private static bool IsArtifactValid(
        string rootFull,
        string librariesFull,
        LoaderArtifact artifact,
        out string fullPath)
    {
        fullPath = string.Empty;
        var baseFull = artifact.Location == LoaderArtifactLocation.Libraries
            ? librariesFull
            : rootFull;

        if (!LoaderPaths.TryResolveWithin(
                baseFull,
                artifact.RelativePath,
                out fullPath,
                out _,
                out _))
        {
            return false;
        }

        if (!File.Exists(fullPath))
        {
            return false;
        }

        var info = new FileInfo(fullPath);
        if (artifact.Size is not null && artifact.Size.Value != info.Length)
        {
            return false;
        }

        if (!artifact.HasSha1)
        {
            return true;
        }

        try
        {
            return string.Equals(
                LoaderPaths.Sha1File(fullPath),
                artifact.Sha1,
                StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static string ResolveArtifactPath(
        string rootFull,
        string librariesFull,
        LoaderArtifact artifact)
    {
        var baseFull = artifact.Location == LoaderArtifactLocation.Libraries
            ? librariesFull
            : rootFull;
        return LoaderPaths.Combine(baseFull, artifact.RelativePath);
    }

    private LoaderRepairResult Failed(
        LoaderArtifactManifest manifest,
        string rootFull,
        IReadOnlyList<int> processorsRun,
        IReadOnlyList<string> details)
    {
        var verification = _verifier.Verify(manifest, rootFull);
        return new LoaderRepairResult(
            LoaderRepairStatus.Failed,
            verification,
            processorsRun,
            details);
    }

    private static string Display(LoaderArtifact artifact)
        => $"{(artifact.Location == LoaderArtifactLocation.Root ? "root" : "libraries")}/{artifact.RelativePath}";

    private static string Tail(string value, int maximum)
    {
        if (value.Length <= maximum)
        {
            return value.Trim();
        }

        return value[^maximum..].Trim();
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // A failed delete is reported by the following verification.
        }
    }

    private static StringComparer PathComparer => OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;
}
