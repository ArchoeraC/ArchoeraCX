using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Archoera.Application.Accounts;
using Archoera.Application.Instances;
using Archoera.Application.Java;
using Archoera.Application.Modules;
using Archoera.Application.Mojang;
using Archoera.Domain.Accounts;
using Archoera.Domain.Instances;
using Archoera.Domain.Java;
using Archoera.Domain.Mojang;
using Archoera.Domain.Settings;
using Archoera.Infrastructure.Accounts;
using Archoera.Infrastructure.Instances;
using Archoera.Infrastructure.Java;
using Archoera.Infrastructure.Modules;
using Archoera.Infrastructure.Packs;
using Archoera.Infrastructure.Settings;

namespace Archoera.Infrastructure.Launch;

/// <summary>
/// Launch orchestration through the C# business module. Offline accounts use
/// the local UUID; Microsoft accounts resolve their stored Minecraft session
/// (refreshing it when needed) and never expose tokens to the caller.
/// </summary>
public sealed class LaunchModuleService : IModuleMethodHandler
{
    private static readonly string[] SupportedMethods = ["launch.start", "launch.prepare"];

    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromSeconds(60),
    };

    private readonly IInstanceRepository _instances;
    private readonly JsonAppSettingsStore _settings;
    private readonly JsonAccountStore _accounts;
    private readonly IAccountCredentialStore _credentials;
    private readonly MicrosoftAuthClient _auth;
    private readonly MicrosoftAccountGate _premiumGate;
    private readonly JavaScanner _java = new();
    private readonly JavaRuntimeSelectionService _javaSelection;
    private readonly PackInstallService? _instanceRepair;
    private readonly DownloadPolicy _downloadPolicy;

    public LaunchModuleService(
        IInstanceRepository? instances = null,
        JsonAppSettingsStore? settings = null,
        string? dataRoot = null,
        JsonAccountStore? accounts = null,
        IAccountCredentialStore? credentials = null,
        MicrosoftAuthClient? auth = null,
        PackInstallService? instanceRepair = null,
        DownloadPolicy? downloadPolicy = null)
    {
        _settings = settings ?? new JsonAppSettingsStore(dataRoot);
        _instances = instances ?? new SqliteInstanceRepository(dataRoot, _settings);
        _accounts = accounts ?? new JsonAccountStore(dataRoot);
        _credentials = credentials ?? _accounts;
        _auth = auth ?? new MicrosoftAuthClient(Http, new MicrosoftAuthOptions());
        _premiumGate = new MicrosoftAccountGate(_accounts, _credentials, _auth);
        _instanceRepair = instanceRepair;
        _javaSelection = new JavaRuntimeSelectionService(_java, _settings);
        _downloadPolicy = downloadPolicy ??
                          new DownloadPolicy(dataRoot ?? _settings.DataRoot);
    }

    public IReadOnlyCollection<string> Methods => SupportedMethods;

    private static bool IsForgeInstance(GameInstanceRecord instance)
        => instance.Loader.Equals("forge", StringComparison.OrdinalIgnoreCase) ||
           instance.Loader.Equals("neoforge", StringComparison.OrdinalIgnoreCase);

    private async Task RepairLaunchAsync(
        GameInstanceRecord instance,
        string? javaPath,
        CancellationToken cancellationToken)
    {
        try
        {
            await _downloadPolicy
                .EnsureEnabledAsync(cancellationToken)
                .ConfigureAwait(false);
            var repair = _instanceRepair ?? new PackInstallService(settings: _settings);
            if (IsForgeInstance(instance))
            {
                if (string.IsNullOrWhiteSpace(javaPath))
                {
                    throw new LaunchException(
                        "JAVA_MISSING",
                        "a Java runtime is required to repair this loader");
                }

                await repair
                    .RepairForgeInstanceAsync(instance, javaPath, cancellationToken)
                    .ConfigureAwait(false);
            }
            else
            {
                await repair
                    .RepairSimpleInstanceAsync(instance, cancellationToken)
                    .ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (LaunchException)
        {
            throw;
        }
        catch (DownloadPolicyException exception)
        {
            throw new LaunchException(
                "AUTO_REPAIR_BLOCKED",
                "launch files are missing and downloads are disabled: " +
                exception.Message);
        }
        catch (Exception exception)
        {
            throw new LaunchException(
                "AUTO_REPAIR_FAILED",
                "launch files are missing and automatic repair failed: " +
                exception.Message);
        }
    }

    private async Task<byte[]> PrepareAsync(
        string? paramsJson,
        CancellationToken cancellationToken)
    {
        var instanceId = ReadRequired(paramsJson, "instanceId");
        var mode = (ReadOptional(paramsJson, "mode") ?? "offline").ToLowerInvariant();
        if (mode is not ("offline" or "online"))
        {
            return ModuleJson.Error("E_INVALID", "mode must be 'offline' or 'online'");
        }

        var instance = (await _instances.ListAsync(cancellationToken).ConfigureAwait(false))
            .FirstOrDefault(item => string.Equals(item.Id, instanceId, StringComparison.Ordinal))
            ?? throw new LaunchException("INSTANCE_NOT_FOUND", $"instance not found: {instanceId}");
        var account = await _accounts.GetActiveAsync(cancellationToken).ConfigureAwait(false);
        var requirePremium = ReadBool(paramsJson, "requirePremium");
        var ready = false;
        string? reason = null;

        if (mode == "online")
        {
            var onlineAccount = account is { Kind: AccountKind.Microsoft } &&
                                !string.IsNullOrWhiteSpace(account.Uuid);
            if (account is null)
            {
                reason = "ACCOUNT_REQUIRED";
            }
            else if (!onlineAccount)
            {
                reason = "MICROSOFT_ACCOUNT_REQUIRED";
            }
            else if (requirePremium &&
                     !await _premiumGate.HasValidAccountAsync(cancellationToken)
                         .ConfigureAwait(false))
            {
                reason = "PREMIUM_ACCOUNT_REQUIRED";
            }
            else if (string.IsNullOrWhiteSpace(_auth.Options.ClientId))
            {
                reason = "MICROSOFT_CLIENT_ID_MISSING";
            }
            else if (!await HasOnlineCredentialAsync(account.Id, cancellationToken)
                         .ConfigureAwait(false))
            {
                reason = "AUTH_REQUIRED";
            }
            else
            {
                ready = true;
            }
        }
        else
        {
            if (account is null)
            {
                reason = "ACCOUNT_REQUIRED";
            }
            else if (account.Kind != AccountKind.Offline)
            {
                reason = "OFFLINE_ACCOUNT_REQUIRED";
            }
            else if (!await _premiumGate.HasValidAccountAsync(cancellationToken)
                         .ConfigureAwait(false))
            {
                reason = "PREMIUM_ACCOUNT_REQUIRED";
            }
            else
            {
                ready = true;
            }
        }

        var accountReady = ready;
        var filesReady = true;
        string? fileIssue = null;
        var autoRepairAvailable = false;
        try
        {
            var paths = LaunchPaths.Resolve(instance.RootPath);
            var version = LoadVersion(instance, paths);
            var check = LaunchPlanBuilder.CheckFiles(version, paths);
            filesReady = check.Ok;
            fileIssue = check.Code;
            autoRepairAvailable = !filesReady ||
                                  !string.IsNullOrWhiteSpace(version.InheritsFrom);
        }
        catch (LaunchException exception)
        {
            filesReady = false;
            fileIssue = exception.Code;
            autoRepairAvailable = true;
        }

        if (accountReady && !filesReady)
        {
            reason = fileIssue;
        }

        return ModuleJson.Envelope(writer =>
        {
            writer.WriteString("instanceId", instance.Id);
            writer.WriteString("mode", mode);
            writer.WriteBoolean("ready", accountReady && filesReady);
            writer.WriteString("reason", reason);
            writer.WriteBoolean("requiresEntitlement", !accountReady);
            writer.WriteBoolean("requiresCertificates", !accountReady);
            writer.WriteBoolean("filesReady", filesReady);
            writer.WriteString("fileIssue", fileIssue);
            writer.WriteBoolean("autoRepairAvailable", autoRepairAvailable);
            writer.WriteString(
                "accountKind",
                account?.Kind.ToString().ToLowerInvariant());
        });
    }

    /// <summary>
    /// Read-only readiness probe. A credential is usable when it still has a
    /// Minecraft access token and either is not near expiry or can be refreshed.
    /// </summary>
    private async Task<bool> HasOnlineCredentialAsync(
        string accountId,
        CancellationToken cancellationToken)
    {
        AccountCredential? credential;
        try
        {
            credential = await _credentials
                .GetCredentialAsync(accountId, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (CredentialVaultException)
        {
            // Prepare is advisory; launch.start reports the vault error.
            return false;
        }

        if (credential is null || string.IsNullOrWhiteSpace(credential.MinecraftAccessToken))
        {
            return false;
        }

        var expiresAt = credential.MinecraftAccessTokenExpiresAt;
        return credential.HasRefreshToken ||
               (expiresAt is not null &&
                expiresAt > DateTimeOffset.UtcNow.AddMinutes(2));
    }

    public async Task<byte[]> HandleAsync(
        string method,
        string? paramsJson,
        CancellationToken cancellationToken)
    {
        if (string.Equals(method, "launch.prepare", StringComparison.Ordinal))
        {
            return await PrepareAsync(paramsJson, cancellationToken).ConfigureAwait(false);
        }

        if (!string.Equals(method, "launch.start", StringComparison.Ordinal))
        {
            return ModuleJson.Error("E_UNSUPPORTED", $"unsupported launch method '{method}'");
        }

        try
        {
            var instanceId = ReadRequired(paramsJson, "instanceId");
            var mode = (ReadOptional(paramsJson, "mode") ?? "offline").ToLowerInvariant();
            if (mode is not ("offline" or "online"))
            {
                throw new LaunchException("E_INVALID", "mode must be 'offline' or 'online'");
            }

            var requirePremium = ReadBool(paramsJson, "requirePremium");
            if (requirePremium &&
                !await _premiumGate.HasValidAccountAsync(cancellationToken).ConfigureAwait(false))
            {
                throw new LaunchException(
                    "PREMIUM_ACCOUNT_REQUIRED",
                    "Sign in with a verified Microsoft account before launching.");
            }

            var instance = (await _instances.ListAsync(cancellationToken).ConfigureAwait(false))
                .FirstOrDefault(item => string.Equals(item.Id, instanceId, StringComparison.Ordinal))
                ?? throw new LaunchException("INSTANCE_NOT_FOUND", $"instance not found: {instanceId}");
            var settings = await _settings.LoadAsync(cancellationToken).ConfigureAwait(false);
            var account = await _accounts.GetActiveAsync(cancellationToken).ConfigureAwait(false)
                ?? throw new LaunchException("ACCOUNT_REQUIRED", "add an account before launching");
            if (mode == "online" && requirePremium &&
                (account.Kind != AccountKind.Microsoft ||
                 string.IsNullOrWhiteSpace(account.Uuid)))
            {
                throw new LaunchException(
                    "PREMIUM_ACCOUNT_REQUIRED",
                    "The active account must be a Microsoft account that owns Minecraft.");
            }

            var requireMicrosoft = ReadBool(paramsJson, "requireMicrosoft");
            if (requireMicrosoft && account.Kind != AccountKind.Microsoft)
            {
                throw new LaunchException(
                    "MICROSOFT_ACCOUNT_ONLY",
                    "Only a verified Microsoft account may launch in this region.");
            }

            var launchAccount = account;
            var accessToken = "0";
            var clientId = string.Empty;
            var userType = "legacy";
            if (mode == "online")
            {
                var session = await ResolveOnlineSessionAsync(account, cancellationToken)
                    .ConfigureAwait(false);
                launchAccount = session.Account;
                accessToken = session.AccessToken;
                clientId = _auth.Options.ClientId;
                userType = "msa";
            }
            else if (account.Kind != AccountKind.Offline)
            {
                throw new LaunchException(
                    "AUTH_REQUIRED",
                    "Offline launch requires an offline account. Sign in with Microsoft or switch to online mode.");
            }
            else if (!await _premiumGate.HasValidAccountAsync(cancellationToken)
                         .ConfigureAwait(false))
            {
                throw new LaunchException(
                    "PREMIUM_ACCOUNT_REQUIRED",
                    "A verified Microsoft account is required before offline launch.");
            }

            var paths = LaunchPaths.Resolve(instance.RootPath);
            MojangVersion version;
            try
            {
                version = LoadVersion(instance, paths);
            }
            catch (LaunchException exception) when (
                exception.Code is "VERSION_JSON_MISSING" or "VERSION_NOT_MERGED")
            {
                // The merged launch profile is missing, but the instance still
                // records its loader/game versions; rebuild it in place.
                JavaRuntimeInfo? repairJava = null;
                if (IsForgeInstance(instance))
                {
                    try
                    {
                        repairJava = await _javaSelection
                            .SelectAsync(null, instance.GameVersion, cancellationToken)
                            .ConfigureAwait(false);
                    }
                    catch (JavaSelectionException selectionError)
                    {
                        throw new LaunchException(
                            selectionError.Code,
                            selectionError.Message);
                    }
                }

                await RepairLaunchAsync(
                        instance,
                        repairJava?.Path,
                        cancellationToken)
                    .ConfigureAwait(false);
                version = LoadVersion(instance, paths);
            }

            var java = !string.IsNullOrWhiteSpace(version.InheritsFrom)
                ? await _javaSelection
                    .SelectAsync(null, instance.GameVersion, cancellationToken)
                    .ConfigureAwait(false)
                : await SelectJavaAsync(settings, version, cancellationToken)
                    .ConfigureAwait(false);

            // Forge/NeoForge instances also need generated-artifact checks.
            // Other loaders are repaired from a fresh merged/profile plan.
            var needsRepair =
                !LaunchPlanBuilder.CheckFiles(version, paths).Ok ||
                !string.IsNullOrWhiteSpace(version.InheritsFrom);
            if (IsForgeInstance(instance) && !needsRepair)
            {
                var loaderCheck = _instanceRepair is null
                    ? new PackInstallService(settings: _settings)
                    : _instanceRepair;
                needsRepair = !(await loaderCheck
                        .CheckForgeInstanceAsync(instance, cancellationToken)
                        .ConfigureAwait(false))
                    .Ok;
            }

            if (needsRepair)
            {
                await RepairLaunchAsync(instance, java.Path, cancellationToken)
                    .ConfigureAwait(false);
                version = LoadVersion(instance, paths);
                java = await SelectJavaAsync(settings, version, cancellationToken)
                    .ConfigureAwait(false);
            }

            var fileCheck = LaunchPlanBuilder.CheckFiles(version, paths);
            if (!fileCheck.Ok)
            {
                throw new LaunchException(
                    fileCheck.Code ?? "LAUNCH_FILES_MISSING",
                    fileCheck.Message ?? "launch files are missing");
            }

            var plan = LaunchPlanBuilder.Build(
                instance,
                version,
                launchAccount,
                java,
                settings,
                paths,
                accessToken: accessToken,
                clientId: clientId,
                userType: userType);
            var (process, logPath) = Start(plan);
            return ModuleJson.Envelope(writer =>
            {
                writer.WriteNumber("pid", process.Id);
                writer.WriteString("instanceId", instance.Id);
                writer.WriteString("javaPath", java.Path);
                writer.WriteString("javaVersion", java.Version);
                writer.WriteString("accountName", launchAccount.DisplayName);
                writer.WriteString("logPath", logPath);
            });
        }
        catch (LaunchException exception)
        {
            return ModuleJson.Error(exception.Code, exception.Message);
        }
        catch (CredentialVaultException exception)
        {
            return ModuleJson.Error("E_VAULT", exception.Message);
        }
        catch (ArgumentException exception)
        {
            return ModuleJson.Error("E_INVALID", exception.Message);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            return ModuleJson.Error("LAUNCH_FAILED", exception.Message);
        }
    }

    /// <summary>
    /// Resolves the launchable Microsoft session. A still-valid Minecraft token
    /// is reused; otherwise the Microsoft refresh token is exchanged for a new
    /// Minecraft session and both stores are updated. Tokens are never logged.
    /// </summary>
    private async Task<OnlineSession> ResolveOnlineSessionAsync(
        AccountRecord account,
        CancellationToken cancellationToken)
    {
        if (account.Kind != AccountKind.Microsoft ||
            string.IsNullOrWhiteSpace(account.Uuid))
        {
            throw new LaunchException(
                "MICROSOFT_ACCOUNT_REQUIRED",
                "Online launch requires an active Microsoft account with a Minecraft profile.");
        }

        var clientId = _auth.Options.ClientId;
        if (string.IsNullOrWhiteSpace(clientId))
        {
            throw new LaunchException(
                "MICROSOFT_CLIENT_ID_MISSING",
                "No Microsoft client ID is configured, so online launch is unavailable.");
        }

        var credential = await _credentials
            .GetCredentialAsync(account.Id, cancellationToken)
            .ConfigureAwait(false);
        if (credential is null ||
            string.IsNullOrWhiteSpace(credential.MinecraftAccessToken))
        {
            throw new LaunchException(
                "AUTH_REQUIRED",
                "No stored Minecraft session for this account. Sign in again.");
        }

        var expiresAt = credential.MinecraftAccessTokenExpiresAt;
        var needsRefresh = expiresAt is null ||
                           expiresAt <= DateTimeOffset.UtcNow.AddMinutes(2);
        if (!needsRefresh)
        {
            return new OnlineSession(account, credential.MinecraftAccessToken);
        }

        if (!credential.HasRefreshToken)
        {
            throw new LaunchException(
                "AUTH_REQUIRED",
                "The Minecraft session expired. Sign in again.");
        }

        MicrosoftTokenSet microsoftTokens;
        MinecraftAuthenticatedSession session;
        try
        {
            microsoftTokens = await _auth
                .RefreshMicrosoftAsync(credential.MicrosoftRefreshToken!, cancellationToken)
                .ConfigureAwait(false);
            session = await _auth
                .CompleteMinecraftLoginAsync(microsoftTokens, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (AccountAuthException exception)
        {
            throw new LaunchException(exception.Code, exception.Message);
        }

        var refreshToken = microsoftTokens.RefreshToken;
        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            throw new LaunchException(
                "AUTH_REQUIRED",
                "Microsoft returned no reusable refresh token. Sign in again.");
        }

        var now = DateTimeOffset.UtcNow;
        var updated = new AccountRecord(
            account.Id,
            AccountKind.Microsoft,
            session.Name,
            session.Uuid,
            session.Xuid,
            session.SkinUrl,
            session.CapeUrl,
            account.Active,
            account.CreatedAt,
            now,
            now);
        await _accounts.SaveAsync(updated, cancellationToken).ConfigureAwait(false);
        await _credentials.SaveCredentialAsync(
                new AccountCredential(
                    account.Id,
                    refreshToken,
                    session.AccessToken,
                    session.ExpiresAt,
                    session.CertificatesJson,
                    now),
                cancellationToken)
            .ConfigureAwait(false);

        return new OnlineSession(updated, session.AccessToken);
    }

    internal static MojangVersion LoadVersion(
        GameInstanceRecord instance,
        LaunchPaths paths)
    {
        var directoryName = Path.GetFileName(paths.InstanceRoot);
        var candidates = new List<string>
        {
            Path.Combine(paths.InstanceRoot, $"{directoryName}.json"),
            Path.Combine(paths.SharedRoot, "versions", directoryName, $"{directoryName}.json"),
            Path.Combine(paths.InstanceRoot, $"{instance.GameVersion}.json"),
            Path.Combine(
                paths.SharedRoot,
                "versions",
                instance.GameVersion,
                $"{instance.GameVersion}.json"),
        };

        // Version-isolated instances created under a versions/ directory may
        // have a custom folder name. Also accept the version id recorded by a
        // loader install (Forge/NeoForge merged profiles).
        foreach (var versionsRoot in new[]
                 {
                     Path.Combine(paths.SharedRoot, "versions"),
                     Path.Combine(paths.InstanceRoot, "versions"),
                 })
        {
            if (!Directory.Exists(versionsRoot)) continue;
            foreach (var directory in Directory.EnumerateDirectories(versionsRoot))
            {
                foreach (var jsonPath in Directory.EnumerateFiles(
                             directory,
                             "*.json",
                             SearchOption.TopDirectoryOnly))
                {
                    candidates.Add(jsonPath);
                }
            }
        }

        var matches = new List<(MojangVersion Version, int Score, DateTime Modified)>();
        foreach (var path in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!File.Exists(path)) continue;
            MojangVersion version;
            try
            {
                using var document = JsonDocument.Parse(File.ReadAllText(path));
                version = MojangJson.ParseVersion(document.RootElement);
            }
            catch (Exception exception) when (
                exception is IOException or JsonException or ArgumentException)
            {
                continue;
            }

            var isLoaderInstance = !string.Equals(
                instance.Loader,
                "vanilla",
                StringComparison.OrdinalIgnoreCase);
            var score = 0;
            if (string.Equals(version.Id, directoryName, StringComparison.OrdinalIgnoreCase) &&
                (!isLoaderInstance ||
                 version.Id.Contains(instance.Loader, StringComparison.OrdinalIgnoreCase)))
            {
                score += 100;
            }

            if (string.Equals(version.Id, instance.GameVersion, StringComparison.OrdinalIgnoreCase))
            {
                score += isLoaderInstance ? 10 : 80;
            }

            if (isLoaderInstance &&
                version.Id.Contains(instance.GameVersion, StringComparison.OrdinalIgnoreCase))
            {
                score += 20;
                if (version.Id.Contains(instance.Loader, StringComparison.OrdinalIgnoreCase))
                {
                    score += 20;
                }

                if (!string.IsNullOrWhiteSpace(instance.LoaderVersion) &&
                    version.Id.Contains(
                        instance.LoaderVersion,
                        StringComparison.OrdinalIgnoreCase))
                {
                    score += 10;
                }
            }

            if (score == 0) continue;
            matches.Add((version, score, File.GetLastWriteTimeUtc(path)));
        }

        if (matches.Count == 0)
        {
            throw new LaunchException(
                "VERSION_JSON_MISSING",
                $"no version JSON found for {instance.GameVersion} in {paths.SharedRoot}");
        }

        var selected = matches
            .OrderByDescending(item => item.Score)
            .ThenByDescending(item => item.Modified)
            .First()
            .Version;
        if (!string.IsNullOrWhiteSpace(selected.InheritsFrom))
        {
            throw new LaunchException(
                "VERSION_NOT_MERGED",
                $"version '{selected.Id}' still inherits from '{selected.InheritsFrom}'; install/merge it first");
        }

        return selected;
    }

    private async Task<JavaRuntimeInfo> SelectJavaAsync(
        AppSettings settings,
        MojangVersion version,
        CancellationToken cancellationToken)
    {
        var required = version.JavaVersion?.MajorVersion ?? InferMajor(version.Id);
        if (!settings.JavaAuto && !string.IsNullOrWhiteSpace(settings.JavaPath))
        {
            var manual = _java.Scan(
                new JavaScanRequest
                {
                    IncludeDefaults = false,
                    Executables = [settings.JavaPath],
                },
                cancellationToken);
            var match = manual.FirstOrDefault();
            if (match is null)
            {
                throw new LaunchException(
                    "JAVA_MISSING",
                    $"configured Java is not usable: {settings.JavaPath}");
            }
            if (required is not null && match.Major < required)
            {
                throw new LaunchException(
                    "JAVA_VERSION_MISMATCH",
                    $"Java {match.Major} is too old; this instance needs Java {required}");
            }
            return match;
        }

        var runtimes = _java.Scan(JavaScanRequest.Default, cancellationToken);
        if (runtimes.Count == 0)
        {
            throw new LaunchException("JAVA_MISSING", "no Java runtime found");
        }

        if (required is null) return runtimes[0];
        var selected = JavaSelector.Select(runtimes, required);
        if (selected is not null) return selected;
        throw new LaunchException(
            "JAVA_VERSION_MISMATCH",
            $"no Java {required}+ runtime found");
    }

    private static int? InferMajor(string id)
    {
        if (id.StartsWith("26.", StringComparison.Ordinal)) return 25;
        if (id.StartsWith("1.20.5", StringComparison.Ordinal) ||
            id.StartsWith("1.21", StringComparison.Ordinal)) return 21;
        if (id.StartsWith("1.18", StringComparison.Ordinal) ||
            id.StartsWith("1.19", StringComparison.Ordinal)) return 17;
        if (id.StartsWith("1.17", StringComparison.Ordinal)) return 16;
        return null;
    }

    private static (Process Process, string LogPath) Start(LaunchPlan plan)
    {
        Directory.CreateDirectory(plan.LogDirectory);
        var startInfo = new ProcessStartInfo
        {
            FileName = plan.JavaPath,
            WorkingDirectory = plan.GameDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var argument in plan.Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        foreach (var name in new[]
                 {
                     "JAVA_TOOL_OPTIONS", "_JAVA_OPTIONS", "JDK_JAVA_OPTIONS", "CLASSPATH",
                 })
        {
            startInfo.Environment.Remove(name);
        }

        var logPath = Path.Combine(
            plan.LogDirectory,
            $"launch-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}.log");
        var process = new Process { StartInfo = startInfo };
        if (!process.Start())
        {
            throw new LaunchException("LAUNCH_FAILED", "JVM did not start");
        }

        var stream = new FileStream(logPath, FileMode.Create, FileAccess.Write, FileShare.Read);
        var log = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true };
        process.OutputDataReceived += (_, args) =>
        {
            if (args.Data is not null) lock (log) log.WriteLine(args.Data);
        };
        process.ErrorDataReceived += (_, args) =>
        {
            if (args.Data is not null) lock (log) log.WriteLine(args.Data);
        };
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        process.EnableRaisingEvents = true;
        process.Exited += (_, _) =>
        {
            lock (log)
            {
                log.WriteLine($"[exited with code {process.ExitCode}]");
                log.Dispose();
            }
        };
        return (process, logPath);
    }

    private sealed record OnlineSession(AccountRecord Account, string AccessToken);

    private static bool ReadBool(string? json, string name)
    {
        if (string.IsNullOrWhiteSpace(json)) return false;
        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.TryGetProperty(name, out var element) &&
                   element.ValueKind is JsonValueKind.True or JsonValueKind.False &&
                   element.GetBoolean();
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string? ReadOptional(string? json, string name)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        using var document = JsonDocument.Parse(json);
        return document.RootElement.TryGetProperty(name, out var element) &&
               element.ValueKind == JsonValueKind.String &&
               !string.IsNullOrWhiteSpace(element.GetString())
            ? element.GetString()
            : null;
    }

    private static string ReadRequired(string? json, string name)
    {
        if (string.IsNullOrWhiteSpace(json)) throw new ArgumentException("request JSON is required");
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty(name, out var element) ||
            element.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(element.GetString()))
        {
            throw new ArgumentException($"{name} is required");
        }
        return element.GetString()!;
    }
}
