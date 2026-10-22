using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Archoera.Application.Downloads;
using Archoera.Application.Java;
using Archoera.Application.Loaders;
using Archoera.Application.Resources;
using Archoera.Domain.Downloads;
using Archoera.Domain.Loaders;
using Archoera.Domain.Resources;
using Archoera.Infrastructure.Java;
using Archoera.Infrastructure.Loaders;
using Archoera.Infrastructure.Resources.Modrinth;

namespace Archoera.DevHost;

internal static class Program
{
    private static int Main(string[] args)
    {
        if (args.Length == 0)
        {
            return Usage();
        }

        try
        {
            return args[0] switch
            {
                "--scan" or "--check" => RunJavaMode(args),
                "--verify-loader" or "--repair-loader" => RunLoaderMode(args),
                "--modrinth-plan" => RunModrinthPlan(args),
                "--self-test-loader" => LoaderSelfTest.Run(Console.Out, Console.Error) ? 0 : 1,
                _ => Usage($"unknown mode: {args[0]}"),
            };
        }
        catch (LoaderManifestException exception)
        {
            WriteJson(LoaderManifestJson.BuildError(exception.Code, exception.Message));
            Console.Error.WriteLine($"loader error [{exception.Code}]: {exception.Message}");
            return 1;
        }
        catch (Exception exception)
        {
            WriteJson(LoaderManifestJson.BuildError("E_INTERNAL", exception.Message));
            Console.Error.WriteLine($"error: {exception.Message}");
            return 1;
        }
    }

    private static int RunJavaMode(string[] args)
    {
        var mode = args[0];
        var roots = new List<string>();
        var executables = new List<string>();
        var includeDefaults = true;

        for (var index = 1; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--root":
                    if (++index >= args.Length)
                    {
                        return Usage("--root requires a directory");
                    }

                    roots.Add(args[index]);
                    break;

                case "--java":
                    if (++index >= args.Length)
                    {
                        return Usage("--java requires an executable path");
                    }

                    executables.Add(args[index]);
                    break;

                case "--no-defaults":
                    includeDefaults = false;
                    break;

                default:
                    return Usage($"unknown option: {args[index]}");
            }
        }

        var request = new JavaScanRequest
        {
            Roots = roots,
            Executables = executables,
            IncludeDefaults = includeDefaults,
        };

        var scanner = new JavaScanner();
        return mode switch
        {
            "--scan" => Scan(scanner, request),
            "--check" => Check(scanner, request),
            _ => Usage($"unknown mode: {mode}"),
        };
    }

    private static int RunLoaderMode(string[] args)
    {
        var mode = args[0];
        var repair = mode == "--repair-loader";
        var index = 1;

        string? installer = null;
        if (index < args.Length && !args[index].StartsWith("--", StringComparison.Ordinal))
        {
            installer = args[index++];
        }

        string? loaderValue = null;
        string? gameVersion = null;
        string? loaderVersion = null;
        string? root = null;
        string? libraries = null;
        string? javaPath = null;
        var timeoutSeconds = 300;

        for (; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--installer":
                    installer = RequireValue(args, ref index, "--installer");
                    break;
                case "--loader":
                    loaderValue = RequireValue(args, ref index, "--loader");
                    break;
                case "--game":
                    gameVersion = RequireValue(args, ref index, "--game");
                    break;
                case "--loader-version":
                    loaderVersion = RequireValue(args, ref index, "--loader-version");
                    break;
                case "--root":
                    root = RequireValue(args, ref index, "--root");
                    break;
                case "--libraries":
                    libraries = RequireValue(args, ref index, "--libraries");
                    break;
                case "--java":
                    javaPath = RequireValue(args, ref index, "--java");
                    break;
                case "--timeout":
                    var timeoutText = RequireValue(args, ref index, "--timeout");
                    if (!int.TryParse(timeoutText, NumberStyles.Integer, CultureInfo.InvariantCulture, out timeoutSeconds) ||
                        timeoutSeconds <= 0)
                    {
                        return Usage("--timeout requires a positive number of seconds");
                    }

                    break;
                default:
                    return Usage($"unknown option: {args[index]}");
            }
        }

        if (string.IsNullOrWhiteSpace(installer))
        {
            return Usage("--verify-loader/--repair-loader requires an installer jar path");
        }

        if (string.IsNullOrWhiteSpace(root))
        {
            return Usage("--root <instance-dir> is required");
        }

        if (string.IsNullOrWhiteSpace(libraries))
        {
            return Usage("--libraries <libraries-dir> is required");
        }

        if (string.IsNullOrWhiteSpace(gameVersion))
        {
            return Usage("--game <minecraft-version> is required");
        }

        if (string.IsNullOrWhiteSpace(loaderValue) ||
            !Enum.TryParse<LoaderKind>(loaderValue, ignoreCase: true, out var kind))
        {
            return Usage("--loader must be forge or neoforge");
        }

        if (repair && string.IsNullOrWhiteSpace(javaPath))
        {
            return Usage("--repair-loader requires --java <path>");
        }

        if (repair)
        {
            // Repair needs the installer on disk under the instance root so the
            // manifest can reference it without absolute paths. Keep a stable,
            // content-addressed copy instead of trusting an arbitrary external
            // location on every run.
            installer = EnsureInstallerUnderRoot(installer, root);
        }

        IForgeArtifactPlanner planner = new ForgeArtifactPlanner();
        var manifest = planner
            .ParseAsync(installer, root, libraries, kind)
            .GetAwaiter()
            .GetResult();

        if (!string.IsNullOrWhiteSpace(loaderVersion) &&
            !string.Equals(manifest.LoaderId.LoaderVersion, loaderVersion, StringComparison.OrdinalIgnoreCase))
        {
            Console.Error.WriteLine(
                $"warning: planner derived loader version '{manifest.LoaderId.LoaderVersion}' from the profile, not '{loaderVersion}'");
        }

        if (!string.Equals(manifest.LoaderId.GameVersion, gameVersion, StringComparison.OrdinalIgnoreCase))
        {
            Console.Error.WriteLine(
                $"warning: planner derived game version '{manifest.LoaderId.GameVersion}' from the profile, not '{gameVersion}'");
        }

        ILoaderArtifactVerifier verifier = new LoaderArtifactVerifier();

        if (!repair)
        {
            var verification = verifier.Verify(manifest, root);
            WriteJson(LoaderManifestJson.BuildEnvelope(manifest, verification));

            if (!verification.Ok)
            {
                Console.Error.WriteLine(
                    $"verify failed: {verification.Missing.Count} missing, {verification.Mismatched.Count} mismatched, {verification.Unsafe?.Count ?? 0} unsafe");
                return 1;
            }

            Console.Error.WriteLine(
                $"verify ok: {manifest.Artifacts.Count} artifacts, {manifest.ProcessorCount} client processors");
            return 0;
        }

        IProcessorRunner runner = new ForgeProcessorRunner();
        var service = new ForgeRepairService(
            runner,
            verifier,
            TimeSpan.FromSeconds(timeoutSeconds));

        var result = service
            .RepairAsync(manifest, root, javaPath!, CancellationToken.None)
            .GetAwaiter()
            .GetResult();

        WriteJson(LoaderManifestJson.BuildEnvelope(manifest, result.Verification, result));

        if (!result.Ok)
        {
            Console.Error.WriteLine($"repair failed [{result.Status}]: {result.Details.Count} detail(s)");
            return 1;
        }

        Console.Error.WriteLine(
            $"repair ok [{result.Status}]: processors run = {result.ProcessorsRun.Count}");
        return 0;
    }

    private static int RunModrinthPlan(string[] args)
    {
        var index = 1;
        string? project = null;
        string? kindValue = "mod";
        string? gameVersion = null;
        string? loader = "fabric";
        string? shaderLoader = "iris";
        string? root = null;
        string? output = null;
        string? requestedVersion = null;
        string? providerBase = null;

        for (; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--project":
                    project = RequireValue(args, ref index, "--project");
                    break;
                case "--kind":
                    kindValue = RequireValue(args, ref index, "--kind");
                    break;
                case "--game":
                    gameVersion = RequireValue(args, ref index, "--game");
                    break;
                case "--loader":
                    loader = RequireValue(args, ref index, "--loader");
                    break;
                case "--shader-loader":
                    shaderLoader = RequireValue(args, ref index, "--shader-loader");
                    break;
                case "--root":
                    root = RequireValue(args, ref index, "--root");
                    break;
                case "--out":
                    output = RequireValue(args, ref index, "--out");
                    break;
                case "--version":
                    requestedVersion = RequireValue(args, ref index, "--version");
                    break;
                case "--provider-base":
                    providerBase = RequireValue(args, ref index, "--provider-base");
                    break;
                default:
                    return Usage($"unknown option: {args[index]}");
            }
        }

        if (string.IsNullOrWhiteSpace(project) ||
            string.IsNullOrWhiteSpace(gameVersion) ||
            string.IsNullOrWhiteSpace(root))
        {
            return Usage("--modrinth-plan requires --project, --game and --root");
        }

        if (!ResourceWire.TryParseKind(kindValue, out var kind))
        {
            return Usage("--kind must be mod, shader or resourcepack");
        }

        var target = kind switch
        {
            ResourceKind.Mod => ResourceTarget.ForMod(gameVersion, loader ?? "fabric"),
            ResourceKind.Shader => ResourceTarget.ForShader(gameVersion, shaderLoader ?? "iris"),
            ResourceKind.ResourcePack => ResourceTarget.ForResourcePack(gameVersion),
            _ => throw new InvalidOperationException(),
        };

        var baseAddress = providerBase is null
            ? ModrinthClientOptions.DefaultBaseAddress
            : new Uri(providerBase, UriKind.Absolute);
        using var http = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(60),
        };
        var options = new ModrinthClientOptions
        {
            BaseAddress = baseAddress,
            UserAgent = ModrinthClientOptions.CreateUserAgent(
                "0.0.1+1",
                "dev@archoera.local"),
        };
        var client = new ModrinthClient(http, options);

        ResourceVersion version;
        if (string.IsNullOrWhiteSpace(requestedVersion))
        {
            var versions = client
                .GetVersionsAsync(project, kind, target, CancellationToken.None)
                .GetAwaiter()
                .GetResult();
            if (versions.Count == 0)
            {
                Console.Error.WriteLine(
                    $"no compatible Modrinth version for {kind.ToWire()} " +
                    $"project '{project}' / Minecraft {gameVersion}");
                return 1;
            }

            version = versions.FirstOrDefault(
                          candidate => candidate.VersionType == ResourceVersionType.Release)
                      ?? versions[0];
        }
        else
        {
            version = client
                .GetVersionAsync(requestedVersion, CancellationToken.None)
                .GetAwaiter()
                .GetResult();
        }

        var rootFull = Path.GetFullPath(root);
        Directory.CreateDirectory(rootFull);
        var plan = ResourceDownloadPlanBuilder.Build(
            rootFull,
            kind,
            target,
            version);
        var outputPath = string.IsNullOrWhiteSpace(output)
            ? Path.Combine(rootFull, "modrinth-plan.json")
            : Path.GetFullPath(output);
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        var json = DownloadPlanJson.Build(plan);
        File.WriteAllBytes(outputPath, json);

        var file = plan.Files.Single();
        Console.Error.WriteLine(
            $"modrinth: {version.Name} [{version.VersionNumber}] / {file.Dest}");
        Console.Error.WriteLine(
            $"plan: {plan.PlanId} / {plan.Files.Count} file(s) / {plan.TotalBytes} bytes");
        Console.Out.WriteLine(outputPath);
        return 0;
    }

    private static string RequireValue(string[] args, ref int index, string option)
    {
        if (++index >= args.Length)
        {
            throw new LoaderManifestException("E_USAGE", $"{option} requires a value");
        }

        return args[index];
    }

    private static string EnsureInstallerUnderRoot(string installer, string root)
    {
        var installerFull = Path.GetFullPath(installer);
        var rootFull = Path.GetFullPath(root);
        var relative = Path.GetRelativePath(rootFull, installerFull);

        if (!Path.IsPathRooted(relative) &&
            !relative.Equals("..", StringComparison.Ordinal) &&
            !relative.StartsWith("../", StringComparison.Ordinal) &&
            !relative.StartsWith("..\\", StringComparison.Ordinal))
        {
            return installer;
        }

        using var stream = new FileStream(installerFull, FileMode.Open, FileAccess.Read, FileShare.Read);
        var hash = Convert.ToHexString(SHA1.HashData(stream)).ToLowerInvariant();
        var target = Path.Combine(
            rootFull,
            ".archoera",
            "installers",
            $"{Path.GetFileNameWithoutExtension(installerFull)}-{hash[..12]}{Path.GetExtension(installerFull)}");

        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.Copy(installerFull, target, overwrite: true);

        return target;
    }

    private static int Scan(IJavaScanner scanner, JavaScanRequest request)
    {
        var items = scanner.Scan(request);
        var envelope = JavaScanJson.BuildEnvelope(items);
        WriteJson(envelope);
        return 0;
    }

    private static int Check(IJavaScanner scanner, JavaScanRequest request)
    {
        var items = scanner.Scan(request);
        var envelope = JavaScanJson.BuildEnvelope(items);

        try
        {
            using var document = JsonDocument.Parse(envelope);
            var root = document.RootElement;

            if (!root.GetProperty("ok").GetBoolean())
            {
                return Fail("envelope is not ok");
            }

            var data = root.GetProperty("data");
            var jsonItems = data.GetProperty("items");
            if (jsonItems.ValueKind != JsonValueKind.Array)
            {
                return Fail("data.items is not an array");
            }

            if (data.GetProperty("count").GetInt32() != jsonItems.GetArrayLength())
            {
                return Fail("data.count does not match data.items");
            }

            if (root.GetProperty("meta").GetProperty("schemaVersion").GetInt32() != JavaScanJson.SchemaVersion)
            {
                return Fail("meta.schemaVersion mismatch");
            }

            Console.Out.WriteLine($"ok: {items.Count} Java runtime(s) discovered");
            return 0;
        }
        catch (Exception exception)
        {
            return Fail(exception.Message);
        }
    }

    private static int Fail(string message)
    {
        Console.Error.WriteLine($"check failed: {message}");
        return 1;
    }

    private static void WriteJson(byte[] envelope)
    {
        using var stdout = Console.OpenStandardOutput();
        stdout.Write(envelope);
        stdout.WriteByte((byte)'\n');
        stdout.Flush();
    }

    private static int Usage(string? message = null)
    {
        if (!string.IsNullOrEmpty(message))
        {
            Console.Error.WriteLine($"error: {message}");
        }

        Console.Error.WriteLine(
            "usage: Archoera.DevHost (--scan | --check) [--root <dir>]... [--java <path>]... [--no-defaults]");
        Console.Error.WriteLine(
            "       Archoera.DevHost --verify-loader <installer.jar> --loader forge|neoforge --game <ver> --root <dir> --libraries <dir> [--loader-version <ver>] [--java <path>]");
        Console.Error.WriteLine(
            "       Archoera.DevHost --repair-loader <installer.jar> --loader forge|neoforge --game <ver> --root <dir> --libraries <dir> --java <path> [--timeout <seconds>]");
        Console.Error.WriteLine(
            "       Archoera.DevHost --modrinth-plan --project <slug|id> --kind mod|shader|resourcepack --game <ver> --root <dir> [--loader fabric|forge|neoforge|quilt] [--shader-loader iris|optifine] [--version <id>] [--out <plan.json>]");
        Console.Error.WriteLine(
            "       Archoera.DevHost --self-test-loader");
        return 2;
    }
}
