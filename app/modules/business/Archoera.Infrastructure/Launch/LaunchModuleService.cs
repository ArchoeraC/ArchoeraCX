using System.Diagnostics;
using System.Text.Json;
using Archoera.Application.Accounts;
using Archoera.Application.Instances;
using Archoera.Application.Java;
using Archoera.Application.Modules;
using Archoera.Application.Mojang;
using Archoera.Application.Stats;
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
using Archoera.Infrastructure.Stats;

namespace Archoera.Infrastructure.Launch;

/// <summary>
/// Launch orchestration through the C# business module. Offline accounts use
/// the local UUID; Microsoft accounts resolve their stored Minecraft session
/// (refreshing it when needed) and never expose tokens to the caller.
/// </summary>
public sealed class LaunchModuleService : IModuleMethodHandler
{
    private static readonly string[] SupportedMethods =
        ["launch.start", "launch.prepare", "launch.memory", "launch.wait"];

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
    private readonly AuthlibInjectorClient _authlibClient = new();
    private readonly AuthlibInjectorManager _injector;
    private readonly JavaScanner _java = new();
    private readonly JavaRuntimeSelectionService _javaSelection;
    private readonly PackInstallService? _instanceRepair;
    private readonly DownloadPolicy _downloadPolicy;
    private readonly OnlineSessionResolver _sessionResolver;
    private readonly IPlaySessionStore _playSessions;

    public LaunchModuleService(
        IInstanceRepository? instances = null,
        JsonAppSettingsStore? settings = null,
        string? dataRoot = null,
        JsonAccountStore? accounts = null,
        IAccountCredentialStore? credentials = null,
        MicrosoftAuthClient? auth = null,
        PackInstallService? instanceRepair = null,
        DownloadPolicy? downloadPolicy = null,
        IPlaySessionStore? playSessions = null)
    {
        _settings = settings ?? new JsonAppSettingsStore(dataRoot);
        _instances = instances ?? new SqliteInstanceRepository(dataRoot, _settings);
        _accounts = accounts ?? new JsonAccountStore(dataRoot);
        _credentials = credentials ?? _accounts;
        _auth = auth ?? new MicrosoftAuthClient(Http, new MicrosoftAuthOptions());
        _premiumGate = new MicrosoftAccountGate(_accounts, _credentials, _auth);
        _instanceRepair = instanceRepair;
        _javaSelection = new JavaRuntimeSelectionService(_java, _settings);
        _sessionResolver = new OnlineSessionResolver(_auth, _credentials, _accounts);
        _injector = new AuthlibInjectorManager(dataRoot ?? _settings.DataRoot);
        _downloadPolicy = downloadPolicy ??
                          new DownloadPolicy(dataRoot ?? _settings.DataRoot);
        _playSessions = playSessions ?? new SqlitePlaySessionStore(dataRoot);
    }

    public IReadOnlyCollection<string> Methods => SupportedMethods;

    private static bool IsForgeInstance(GameInstanceRecord instance)
        => instance.Loader.Equals("forge", StringComparison.OrdinalIgnoreCase) ||
           instance.Loader.Equals("neoforge", StringComparison.OrdinalIgnoreCase);

    private sealed record AuthlibLaunchSession(
        AccountRecord Account,
        string AccessToken,
        AuthlibLaunchArguments Arguments);

    /// <summary>
    /// Resolves the stored Yggdrasil session, refreshes it when the server
    /// rejects it, and makes sure the verified authlib-injector jar exists.
    /// Terms acceptance is checked here too: an account created before the terms
    /// gate must not download the jar silently.
    /// </summary>
    private async Task<AuthlibLaunchSession> ResolveAuthlibLaunchAsync(
        AccountRecord account,
        CancellationToken cancellationToken)
    {
        var credential = await _credentials
            .GetCredentialAsync(account.Id, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new LaunchException(
                "AUTH_REQUIRED",
                "No stored third-party session for this account. Sign in again.");
        if (!credential.HasAuthlibToken)
        {
            throw new LaunchException(
                "AUTH_REQUIRED",
                "No third-party access token is stored. Sign in again.");
        }

        if (credential.AuthlibTermsAcceptedAt is null)
        {
            throw new LaunchException(
                "AUTHLIB_TERMS_REQUIRED",
                "Accept the third-party server terms before launching this account.");
        }

        var server = credential.AuthlibServer ?? account.AuthlibServer;
        if (string.IsNullOrWhiteSpace(server))
        {
            throw new LaunchException(
                "AUTH_REQUIRED",
                "The account has no authlib server. Sign in again.");
        }

        AuthlibServerMetadata metadata;
        try
        {
            metadata = await _authlibClient
                .ResolveAsync(server, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (AccountAuthException) when (
            !string.IsNullOrWhiteSpace(credential.AuthlibMetadataJson))
        {
            // Offline launch can still use the prefetched metadata; the game
            // itself will report a session problem if the server is required.
            metadata = new AuthlibServerMetadata(
                server,
                server,
                account.DisplayName,
                null,
                credential.AuthlibMetadataJson!);
        }

        var accessToken = credential.AuthlibAccessToken!;
        var current = account;
        var valid = await _authlibClient
            .ValidateAsync(
                metadata,
                accessToken,
                credential.AuthlibClientToken ?? string.Empty,
                cancellationToken)
            .ConfigureAwait(false);
        if (!valid)
        {
            var session = await _authlibClient
                .RefreshAsync(
                    metadata,
                    accessToken,
                    credential.AuthlibClientToken ?? string.Empty,
                    account.Uuid ?? string.Empty,
                    account.DisplayName,
                    cancellationToken)
                .ConfigureAwait(false);
            accessToken = session.AccessToken;
            var now = DateTimeOffset.UtcNow;
            current = account with
            {
                DisplayName = session.Name,
                Uuid = session.Uuid,
                SkinUrl = session.SkinUrl ?? account.SkinUrl,
                AuthlibServer = metadata.ServerUrl,
                UpdatedAt = now,
                LastLoginAt = now,
            };
            await _accounts.SaveAsync(current, cancellationToken).ConfigureAwait(false);
            await _credentials.SaveCredentialAsync(
                    credential with
                    {
                        AuthlibServer = metadata.ServerUrl,
                        AuthlibAccessToken = session.AccessToken,
                        AuthlibClientToken = session.ClientToken,
                        AuthlibMetadataJson = metadata.RawJson,
                        UpdatedAt = now,
                    },
                    cancellationToken)
                .ConfigureAwait(false);
        }

        var jar = await _injector.EnsureAsync(cancellationToken).ConfigureAwait(false);
        return new AuthlibLaunchSession(
            current,
            accessToken,
            new AuthlibLaunchArguments(jar, metadata.ServerUrl, metadata.RawJson));
    }

    private async Task<MojangVersion> LoadOrRepairVersionAsync(
        GameInstanceRecord instance,
        LaunchPaths paths,
        CancellationToken cancellationToken)
    {
        var folder = Path.GetFileName(paths.InstanceRoot);
        var own = Path.Combine(paths.InstanceRoot, $"{folder}.json");
        if (LaunchProfileRecovery.NeedsRecovery(own))
        {
            // Rebuild from local profiles before LoadVersion can fall back to a
            // sibling version and before any download is considered.
            try
            {
                await LaunchProfileRecovery
                    .TryRecoverAsync(instance, paths, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception)
            {
                // Local recovery is best effort; downloads remain the fallback.
            }
        }

        try
        {
            return LoadVersion(instance, paths);
        }
        catch (LaunchException exception) when (
            exception.Code is "VERSION_JSON_MISSING" or "VERSION_NOT_MERGED"
                or "LOADER_NOT_INSTALLED")
        {
            var recovered = await TryLocalRecover(instance, paths, cancellationToken)
                .ConfigureAwait(false);
            if (recovered is not null) return recovered;

            // The merged launch profile is missing; rebuild it with downloads.
            JavaRuntimeInfo? repairJava = null;
            if (IsForgeInstance(instance))
            {
                try
                {
                    var repairSettings = await _settings
                        .LoadAsync(cancellationToken)
                        .ConfigureAwait(false);
                    if (instance.Overrides is { } repairOverrides)
                    {
                        repairSettings = repairOverrides.ApplyTo(repairSettings);
                    }
                    repairJava = await _javaSelection
                        .SelectAsync(
                            null,
                            instance.GameVersion,
                            cancellationToken,
                            repairSettings.JavaAuto ? null : repairSettings.JavaPath)
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
            return LoadVersion(instance, paths);
        }
    }

    /// <summary>
    /// First try to rebuild the profile from files already on disk: imported
    /// instances usually still have a sibling loader profile. Returns the
    /// recovered version, or null when recovery failed or the recovered
    /// profile is not usable (the caller falls back to download repair).
    /// </summary>
    private async Task<MojangVersion?> TryLocalRecover(
        GameInstanceRecord instance,
        LaunchPaths paths,
        CancellationToken cancellationToken)
    {
        bool recovered;
        try
        {
            recovered = await LaunchProfileRecovery
                .TryRecoverAsync(instance, paths, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            recovered = false;
        }

        if (recovered)
        {
            try
            {
                return LoadVersion(instance, paths);
            }
            catch (LaunchException)
            {
                // The recovered profile is not usable; download repair.
            }
        }

        return null;
    }

    private async Task<IReadOnlyList<string>> CandidateRootsAsync(
        LaunchPaths paths,
        AppSettings settings,
        CancellationToken cancellationToken)
    {
        var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            paths.InstanceRoot,
            paths.SharedRoot,
            LaunchPaths.ResolveInstallRoot(paths.InstanceRoot),
        };

        foreach (var root in settings.Roots)
        {
            if (!string.IsNullOrWhiteSpace(root)) roots.Add(root);
        }

        try
        {
            foreach (var instance in await _instances
                         .ListAsync(cancellationToken)
                         .ConfigureAwait(false))
            {
                if (string.IsNullOrWhiteSpace(instance.RootPath)) continue;
                roots.Add(instance.RootPath);
                roots.Add(LaunchPaths.ResolveInstallRoot(instance.RootPath));
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            // The current instance root and configured roots are still usable.
        }

        return roots
            .Where(Directory.Exists)
            .ToList();
    }

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
            else if (account.Kind == AccountKind.Authlib)
            {
                var credential = await _credentials
                    .GetCredentialAsync(account.Id, cancellationToken)
                    .ConfigureAwait(false);
                if (credential?.HasAuthlibToken != true)
                {
                    reason = "AUTH_REQUIRED";
                }
                else if (credential.AuthlibTermsAcceptedAt is null)
                {
                    reason = "AUTHLIB_TERMS_REQUIRED";
                }
                else if (requirePremium &&
                         !await _premiumGate.HasValidAccountAsync(cancellationToken)
                             .ConfigureAwait(false))
                {
                    reason = "PREMIUM_ACCOUNT_REQUIRED";
                }
                else
                {
                    ready = true;
                }
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
            else if (!await _sessionResolver
                         .HasUsableCredentialAsync(account.Id, cancellationToken)
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
            var loaderCheck = LaunchPlanBuilder.CheckLoaderArtifacts(instance, version, paths);
            filesReady = check.Ok && loaderCheck.Ok;
            fileIssue = check.Ok ? loaderCheck.Code : check.Code;
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
    /// Heap preview shared by the settings panel, the instance dialog and the
    /// pre-launch warning. Manual values are returned as-is together with a
    /// warning code; automatic values are derived from the version, loader,
    /// enabled mods and installed RAM.
    /// </summary>
    private async Task<byte[]> MemoryAsync(
        string? paramsJson,
        CancellationToken cancellationToken)
    {
        try
        {
            var instanceId = ReadRequired(paramsJson, "instanceId");
            var instance = (await _instances.ListAsync(cancellationToken).ConfigureAwait(false))
                .FirstOrDefault(item => string.Equals(item.Id, instanceId, StringComparison.Ordinal))
                ?? throw new LaunchException(
                    "INSTANCE_NOT_FOUND",
                    $"instance not found: {instanceId}");
            var settings = await _settings.LoadAsync(cancellationToken).ConfigureAwait(false);
            if (instance.Overrides is { } overrides)
            {
                settings = overrides.ApplyTo(settings);
            }

            var plan = LaunchMemoryPlanner.Resolve(
                settings,
                instance,
                instance.RootPath);
            return ModuleJson.Envelope(writer => WriteMemoryPlan(writer, plan));
        }
        catch (LaunchException exception)
        {
            return ModuleJson.Error(exception.Code, exception.Message);
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
            return ModuleJson.Error("E_INTERNAL", exception.Message);
        }
    }

    /// <summary>
    /// Waits for the registered JVM to exit without streaming its log. The UI
    /// calls this once after <c>launch.start</c>; on return it can analyze the
    /// artifacts. A timeout returns <c>running: true</c> so the caller follows
    /// again instead of treating a long session as an exit.
    /// </summary>
    private async Task<byte[]> WaitAsync(
        string? paramsJson,
        CancellationToken cancellationToken)
    {
        var instanceId = ReadRequired(paramsJson, "instanceId");
        var logPath = ReadOptional(paramsJson, "logPath");
        var timeoutSeconds = Math.Clamp(
            ReadInt(paramsJson, "timeoutSec") ?? 21600,
            1,
            43200);

        var waiter = LaunchProcessRegistry.TryWaitForExit(instanceId);
        int? exitCode = null;
        var running = waiter is not null;
        if (waiter is not null)
        {
            try
            {
                exitCode = await waiter
                    .WaitAsync(TimeSpan.FromSeconds(timeoutSeconds), cancellationToken)
                    .ConfigureAwait(false);
                running = false;
            }
            catch (TimeoutException)
            {
                // Still running; the caller may wait again.
            }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                // A new launch replaced this waiter.
                running = true;
            }
        }

        return ModuleJson.Envelope(writer =>
        {
            writer.WriteString("instanceId", instanceId);
            writer.WriteBoolean("running", running);
            if (exitCode is { } code)
            {
                writer.WriteNumber("exitCode", code);
            }
            else
            {
                writer.WriteNull("exitCode");
            }

            if (string.IsNullOrWhiteSpace(logPath))
            {
                writer.WriteNull("logPath");
            }
            else
            {
                writer.WriteString("logPath", logPath);
            }
        });
    }

    private static void WriteMemoryPlan(Utf8JsonWriter writer, LaunchMemoryPlan plan)
    {
        writer.WriteString("mode", plan.Mode);
        writer.WriteNumber("minMb", plan.MinMb);
        writer.WriteNumber("maxMb", plan.MaxMb);
        writer.WriteNumber("recommendedMb", plan.RecommendedMb);
        writer.WriteNumber("systemTotalMb", plan.SystemTotalMb);
        if (plan.SystemAvailableMb is { } available)
        {
            writer.WriteNumber("systemAvailableMb", available);
        }
        else
        {
            writer.WriteNull("systemAvailableMb");
        }

        writer.WriteNumber("modCount", plan.ModCount);
        if (plan.WarningCode is null)
        {
            writer.WriteNull("warningCode");
            writer.WriteNull("warning");
        }
        else
        {
            writer.WriteString("warningCode", plan.WarningCode);
            writer.WriteString("warning", plan.WarningMessage);
        }
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

        if (string.Equals(method, "launch.memory", StringComparison.Ordinal))
        {
            return await MemoryAsync(paramsJson, cancellationToken).ConfigureAwait(false);
        }

        if (string.Equals(method, "launch.wait", StringComparison.Ordinal))
        {
            return await WaitAsync(paramsJson, cancellationToken).ConfigureAwait(false);
        }

        if (!string.Equals(method, "launch.start", StringComparison.Ordinal))
        {
            return ModuleJson.Error("E_UNSUPPORTED", $"unsupported launch method '{method}'");
        }

        var stage = LaunchProgress.StageInstance;
        var instanceId = string.Empty;
        JavaRuntimeInfo? java = null;
        string? logPath = null;
        try
        {
            instanceId = ReadRequired(paramsJson, "instanceId");
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
            // Per-instance overrides win over the global document. The global
            // object stays untouched so every instance keeps its own effective
            // memory/args/window values.
            if (instance.Overrides is { } overrides)
            {
                settings = overrides.ApplyTo(settings);
            }
            // One heap decision for the whole launch: the command line, the
            // task-center label and the pre-launch warning share these numbers.
            var memoryPlan = LaunchMemoryPlanner.Resolve(
                settings,
                instance,
                instance.RootPath);

            // Pre-load runs after the instance and effective settings are known
            // but before any version or library lookup.
            await LaunchCommandRunner
                .RunPreLoadAsync(instance, settings, cancellationToken)
                .ConfigureAwait(false);

            LaunchProgress.Report(LaunchProgress.Instance);
            stage = LaunchProgress.StageAccount;

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
            AuthlibLaunchArguments? authlibLaunch = null;
            if (mode == "online")
            {
                if (account.Kind == AccountKind.Authlib)
                {
                    var session = await ResolveAuthlibLaunchAsync(account, cancellationToken)
                        .ConfigureAwait(false);
                    launchAccount = session.Account;
                    accessToken = session.AccessToken;
                    userType = "msa";
                    authlibLaunch = session.Arguments;
                }
                else
                {
                    var session = await _sessionResolver
                        .ResolveAsync(account, cancellationToken)
                        .ConfigureAwait(false);
                    launchAccount = session.Account;
                    accessToken = session.AccessToken;
                    clientId = _auth.Options.ClientId;
                    userType = "msa";
                }
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

            LaunchProgress.Report(LaunchProgress.Account);
            stage = LaunchProgress.StageVersion;

            var paths = LaunchPaths.Resolve(instance.RootPath);
            var version = await LoadOrRepairVersionAsync(instance, paths, cancellationToken)
                .ConfigureAwait(false);

            LaunchProgress.Report(LaunchProgress.Version);
            stage = LaunchProgress.StageJava;

            var instanceJavaPath = settings.JavaAuto ? null : settings.JavaPath;
            java = !string.IsNullOrWhiteSpace(version.InheritsFrom)
                ? await _javaSelection
                    .SelectAsync(
                        null,
                        instance.GameVersion,
                        cancellationToken,
                        instanceJavaPath)
                    .ConfigureAwait(false)
                : await SelectJavaAsync(settings, version, cancellationToken)
                    .ConfigureAwait(false);

            LaunchProgress.Report(LaunchProgress.Java);
            stage = LaunchProgress.StageFiles;

            // Forge/NeoForge instances also need generated-artifact checks.
            // Forge lists its generated jars in the merged profile, so
            // CheckFiles covers them; NeoForge locates them by convention.
            // Repair is only needed when something is actually missing -- an
            // instance prepared by another launcher must not require an
            // installer download just because its own downloads/ is empty.
            var filesCheck = LaunchPlanBuilder.CheckFiles(version, paths);
            if (!filesCheck.Ok)
            {
                // Missing ordinary libraries/assets may already exist in
                // another configured root or another instance's private game
                // directory. Copy verified local files before downloading.
                var roots = await CandidateRootsAsync(paths, settings, cancellationToken)
                    .ConfigureAwait(false);
                if (LaunchFileRecovery.TryRecover(instance, version, paths, roots) > 0)
                {
                    filesCheck = LaunchPlanBuilder.CheckFiles(version, paths);
                }
            }

            var loaderOk = LaunchPlanBuilder.CheckLoaderArtifacts(instance, version, paths).Ok;
            var needsRepair =
                !filesCheck.Ok ||
                !loaderOk ||
                !string.IsNullOrWhiteSpace(version.InheritsFrom);
            var repairAttempted = needsRepair;

            LaunchProgress.Report(LaunchProgress.Files);
            stage = LaunchProgress.StageRepair;

            // Prefer repairing from an installer already on disk before asking
            // the network for anything. Only NeoForge hides generated jars from
            // CheckFiles; if ordinary libraries/assets are missing, processors
            // cannot help and the download repair takes over.
            if (needsRepair && IsForgeInstance(instance) && filesCheck.Ok && !loaderOk)
            {
                var localRepair = _instanceRepair ?? new PackInstallService(settings: _settings);
                try
                {
                    if (await localRepair
                            .TryRepairForgeLocallyAsync(instance, java.Path, cancellationToken)
                            .ConfigureAwait(false))
                    {
                        version = LoadVersion(instance, paths);
                        needsRepair =
                            !LaunchPlanBuilder.CheckFiles(version, paths).Ok ||
                            !LaunchPlanBuilder.CheckLoaderArtifacts(instance, version, paths).Ok;
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception)
                {
                    // Local installer/processors are best effort; downloads
                    // remain the fallback.
                }
            }

            if (needsRepair)
            {
                await RepairLaunchAsync(instance, java.Path, cancellationToken)
                    .ConfigureAwait(false);
                version = LoadVersion(instance, paths);
                java = await SelectJavaAsync(settings, version, cancellationToken)
                    .ConfigureAwait(false);
            }

            if (repairAttempted)
            {
                LaunchProgress.Report(LaunchProgress.Repair);
            }

            stage = repairAttempted
                ? LaunchProgress.StageRepair
                : LaunchProgress.StageFiles;

            var fileCheck = LaunchPlanBuilder.CheckFiles(version, paths);
            if (!fileCheck.Ok)
            {
                throw new LaunchException(
                    fileCheck.Code ?? "LAUNCH_FILES_MISSING",
                    fileCheck.Message ?? "launch files are missing");
            }

            var loaderCheck = LaunchPlanBuilder.CheckLoaderArtifacts(instance, version, paths);
            if (!loaderCheck.Ok)
            {
                throw new LaunchException(
                    loaderCheck.Code ?? "LAUNCH_FILES_MISSING",
                    loaderCheck.Message ?? "loader files are missing");
            }

            stage = LaunchProgress.StagePlan;

            // Optimization depends on the hardware the caller probed through
            // the C++ bridge (`system.gpu`). Without it we still get the
            // heap-based JVM tuning.
            var hardware = ReadHardware(paramsJson);
            var optimizationPlan = LaunchOptimizationPlanner.Build(
                settings,
                memoryPlan,
                hardware,
                Environment.ProcessorCount);
            if (optimizationPlan.Notes.Contains("windows-gpu-preference", StringComparer.Ordinal))
            {
                WindowsGpuPreference.TryPreferHighPerformance(java.Path);
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
                userType: userType,
                memoryPlan: memoryPlan,
                optimizationPlan: optimizationPlan,
                cores: Environment.ProcessorCount,
                authlib: authlibLaunch);

            await LaunchCommandRunner
                .RunPreLaunchAsync(instance, settings, plan, cancellationToken)
                .ConfigureAwait(false);

            LaunchProgress.Report(LaunchProgress.Plan);
            stage = LaunchProgress.StageProcess;

            // The session is persisted before the JVM starts: if the launcher
            // crashes while the game runs, the launch is still counted (with
            // zero playtime until the exit callback completes the row).
            var sessionStartedAt = DateTimeOffset.UtcNow;
            string? sessionId = null;
            try
            {
                sessionId = Guid.NewGuid().ToString("N");
                await _playSessions
                    .BeginAsync(
                        new PlaySessionStart(
                            sessionId,
                            instance.Id,
                            instance.Name,
                            instance.Loader,
                            instance.GameVersion,
                            sessionStartedAt),
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception)
            {
                // Statistics are best effort; a busy database must never block
                // a launch.
                sessionId = null;
            }

            if (sessionId is { } activeId)
            {
                PlaySessionTracker.Activate(activeId);
            }

            var postExit = LaunchCommandRunner.CreatePostExitCallback(
                instance,
                settings,
                plan);
            Process process;
            string startedLogPath;
            try
            {
                (process, startedLogPath) = LaunchProcessStarter.Start(
                    instance.Id,
                    plan,
                    settings.LogRetentionFiles,
                    settings.LogRetentionMegabytes,
                    exitCode =>
                    {
                        postExit?.Invoke(exitCode);
                        if (sessionId is { } id)
                        {
                            ObservePlaySession(
                                CompletePlaySessionAsync(
                                    id,
                                    DateTimeOffset.UtcNow,
                                    exitCode));
                        }
                    });
            }
            catch
            {
                if (sessionId is { } id)
                {
                    // Await the cleanup so a failed launch cannot leave a
                    // countable session behind if the caller exits next.
                    try
                    {
                        await AbortPlaySessionAsync(id).ConfigureAwait(false);
                    }
                    catch
                    {
                        // Best effort; the next statistics query drops any
                        // stale open row anyway.
                    }
                }

                throw;
            }

            logPath = startedLogPath;

            // The process is running, so this session counts as played. The
            // write happens here (not in the UI) so hiding the launcher or
            // quitting it right after the start cannot lose the timestamp.
            await InstancePlayHistory
                .MarkPlayedAsync(_instances, instance.Id, DateTimeOffset.UtcNow)
                .ConfigureAwait(false);

            LaunchProgress.Report(LaunchProgress.Process);

            return ModuleJson.Envelope(writer =>
            {
                writer.WriteNumber("pid", process.Id);
                writer.WriteString("instanceId", instance.Id);
                writer.WriteString("javaPath", java.Path);
                writer.WriteString("javaVersion", java.Version);
                writer.WriteString("accountName", launchAccount.DisplayName);
                writer.WriteString("logPath", logPath);
                writer.WriteString("memoryMode", memoryPlan.Mode);
                writer.WriteNumber("memoryMaxMb", memoryPlan.MaxMb);
                writer.WriteNumber("memoryRecommendedMb", memoryPlan.RecommendedMb);
                writer.WriteStartArray("optimizations");
                foreach (var note in optimizationPlan.Notes)
                {
                    writer.WriteStringValue(note);
                }

                writer.WriteEndArray();
            });
        }
        catch (LaunchException exception)
        {
            return LaunchFailureDetails.Error(
                exception.Code,
                exception.Message,
                stage,
                instanceId,
                java?.Path,
                java?.Version,
                logPath,
                exception.RequiredJavaMajor);
        }
        catch (CredentialVaultException exception)
        {
            return LaunchFailureDetails.Error(
                "E_VAULT",
                exception.Message,
                stage,
                instanceId,
                java?.Path,
                java?.Version,
                logPath);
        }
        catch (ArgumentException exception)
        {
            return LaunchFailureDetails.Error(
                "E_INVALID",
                exception.Message,
                stage,
                instanceId,
                java?.Path,
                java?.Version,
                logPath);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            return LaunchFailureDetails.Error(
                "LAUNCH_FAILED",
                exception.Message,
                stage,
                instanceId,
                java?.Path,
                java?.Version,
                logPath);
        }
    }

    /// <summary>
    /// Forwarder kept for existing callers (LoaderModuleService, tests) that
    /// address version discovery through the launch service.
    /// </summary>
    internal static MojangVersion LoadVersion(
        GameInstanceRecord instance,
        LaunchPaths paths)
        => LaunchVersionDiscovery.LoadVersion(instance, paths);

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
                    $"configured Java is not usable: {settings.JavaPath}",
                    required);
            }
            if (required is not null && match.Major < required)
            {
                throw new LaunchException(
                    "JAVA_VERSION_MISMATCH",
                    $"Java {match.Major} is too old; this instance needs Java {required}",
                    required);
            }
            return match;
        }

        var runtimes = _java.Scan(JavaScanRequest.Default, cancellationToken);
        if (runtimes.Count == 0)
        {
            throw new LaunchException(
                "JAVA_MISSING",
                required is null ? "no Java runtime found" : $"no Java {required}+ runtime found",
                required);
        }

        if (required is null) return runtimes[0];
        var selected = JavaSelector.Select(runtimes, required);
        if (selected is not null) return selected;
        throw new LaunchException(
            "JAVA_VERSION_MISMATCH",
            $"no Java {required}+ runtime found",
            required);
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

    /// <summary>
    /// Optional `hardware` object forwarded from the C++ `system.gpu` probe.
    /// Missing or malformed input just means "no GPU-specific optimization".
    /// </summary>
    private static HardwareProfile? ReadHardware(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.TryGetProperty("hardware", out var element)
                ? HardwareProfile.TryParse(element)
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

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

    private static int? ReadInt(string? json, string name)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.TryGetProperty(name, out var element) &&
                   element.ValueKind == JsonValueKind.Number &&
                   element.TryGetInt32(out var value)
                ? value
                : null;
        }
        catch (JsonException)
        {
            return null;
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

    /// <summary>
    /// Fire-and-forget for the process-exit callback. The exit handler may not
    /// block the JVM reaper, and an observation failure must not surface as an
    /// unobserved task exception.
    /// </summary>
    private static void ObservePlaySession(Task task)
        => _ = task.ContinueWith(
            static completed => _ = completed.Exception,
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted |
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);

    private async Task CompletePlaySessionAsync(
        string sessionId,
        DateTimeOffset endedAt,
        int exitCode)
    {
        try
        {
            await _playSessions
                .CompleteAsync(sessionId, endedAt, exitCode, CancellationToken.None)
                .ConfigureAwait(false);
        }
        finally
        {
            PlaySessionTracker.Deactivate(sessionId);
        }
    }

    private async Task AbortPlaySessionAsync(string sessionId)
    {
        try
        {
            await _playSessions
                .AbortAsync(sessionId, CancellationToken.None)
                .ConfigureAwait(false);
        }
        finally
        {
            PlaySessionTracker.Deactivate(sessionId);
        }
    }
}
