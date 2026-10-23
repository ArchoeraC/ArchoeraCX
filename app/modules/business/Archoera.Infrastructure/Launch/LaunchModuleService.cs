using System.Diagnostics;
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
            // First try to rebuild the profile from files already on disk:
            // imported instances usually still have a sibling loader profile.
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
            // Per-instance overrides win over the global document. The global
            // object stays untouched so every instance keeps its own effective
            // memory/args/window values.
            if (instance.Overrides is { } overrides)
            {
                settings = overrides.ApplyTo(settings);
            }
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
            var version = await LoadOrRepairVersionAsync(instance, paths, cancellationToken)
                .ConfigureAwait(false);

            var instanceJavaPath = settings.JavaAuto ? null : settings.JavaPath;
            var java = !string.IsNullOrWhiteSpace(version.InheritsFrom)
                ? await _javaSelection
                    .SelectAsync(
                        null,
                        instance.GameVersion,
                        cancellationToken,
                        instanceJavaPath)
                    .ConfigureAwait(false)
                : await SelectJavaAsync(settings, version, cancellationToken)
                    .ConfigureAwait(false);

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
            var (process, logPath) = Start(
                instance.Id,
                plan,
                settings.LogRetentionFiles,
                settings.LogRetentionMegabytes);
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
        var isLoaderInstance = !string.Equals(
            instance.Loader,
            "vanilla",
            StringComparison.OrdinalIgnoreCase);

        // The instance's own profile wins. Every launcher (Mojang, Fabric,
        // Quilt, Forge, NeoForge) writes exactly one profile JSON per version
        // directory and names it after that directory, so the instance folder
        // is authoritative even when its id carries no game version or loader
        // name (e.g. a modpack called "AutumnTown").
        //
        // The one exception: a loader instance whose folder is named after the
        // vanilla version (for example versions/1.21.1) can own the plain
        // vanilla profile while its loader profile lives in a sibling
        // directory. That base profile must not be launched on its own.
        foreach (var own in new[]
                 {
                     Path.Combine(paths.InstanceRoot, $"{directoryName}.json"),
                     Path.Combine(
                         paths.SharedRoot,
                         "versions",
                         directoryName,
                         $"{directoryName}.json"),
                 })
        {
            var parsed = TryReadVersion(own);
            if (parsed is null) continue;
            if (isLoaderInstance && IsVanillaBase(parsed, instance)) continue;
            EnsureMerged(parsed);
            return parsed;
        }

        // Loader profiles are named by the installer, not by the instance
        // folder: Forge writes "<game>-forge-<version>", NeoForge writes
        // "neoforge-<version>" (no game version), Fabric writes
        // "fabric-loader-<version>-<game>". Ask for those ids directly before
        // falling back to the heuristic scan below.
        if (isLoaderInstance)
        {
            var installRoot = LaunchPaths.ResolveInstallRoot(paths.InstanceRoot);
            foreach (var id in LoaderProfileIds(instance))
            {
                foreach (var baseRoot in new[]
                         {
                             installRoot,
                             paths.SharedRoot,
                             paths.InstanceRoot,
                         })
                {
                    var parsed = TryReadVersion(
                        Path.Combine(baseRoot, "versions", id, $"{id}.json"));
                    if (parsed is null) continue;
                    if (IsVanillaBase(parsed, instance)) continue;
                    EnsureMerged(parsed);
                    return parsed;
                }
            }
        }

        // Fallback for instances created from a plain version id (the profile
        // keeps the game/loader id rather than the folder name).
        var candidates = new List<string>
        {
            Path.Combine(paths.InstanceRoot, $"{instance.GameVersion}.json"),
            Path.Combine(
                paths.SharedRoot,
                "versions",
                instance.GameVersion,
                $"{instance.GameVersion}.json"),
        };

        // Also accept a base profile found under versions/. Only ids related to
        // this instance's game version are considered: picking another
        // instance's profile would launch the wrong game.
        foreach (var versionsRoot in new[]
                 {
                     Path.Combine(paths.SharedRoot, "versions"),
                     Path.Combine(paths.InstanceRoot, "versions"),
                 })
        {
            if (!Directory.Exists(versionsRoot)) continue;
            foreach (var directory in Directory.EnumerateDirectories(versionsRoot))
            {
                var name = Path.GetFileName(directory);
                if (string.IsNullOrWhiteSpace(name)) continue;
                // Do not borrow a real instance's merged profile just because
                // its custom name contains this game and loader.
                if (LaunchPaths.LooksLikeInstanceDirectory(directory)) continue;
                candidates.Add(Path.Combine(directory, $"{name}.json"));
            }
        }

        var matches = new List<(MojangVersion Version, int Score, DateTime Modified)>();
        foreach (var path in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var version = TryReadVersion(path);
            if (version is null) continue;

            var score = 0;
            if (string.Equals(version.Id, instance.GameVersion, StringComparison.OrdinalIgnoreCase))
            {
                // A vanilla base profile: usable for vanilla instances and as a
                // last resort for a loader instance without a merged profile.
                score += isLoaderInstance ? 10 : 80;
            }
            else if (isLoaderInstance &&
                     version.Id.Contains(instance.Loader, StringComparison.OrdinalIgnoreCase))
            {
                // Standard loader profile ids: "1.21.1-forge-47.2.0",
                // "fabric-loader-0.16.14-1.21.1", and NeoForge's
                // "neoforge-21.1.264" (which omits the game version).
                var hasGame = version.Id.Contains(
                    instance.GameVersion,
                    StringComparison.OrdinalIgnoreCase);
                var hasLoaderVersion =
                    !string.IsNullOrWhiteSpace(instance.LoaderVersion) &&
                    version.Id.Contains(
                        instance.LoaderVersion,
                        StringComparison.OrdinalIgnoreCase);
                if (!hasLoaderVersion &&
                    MentionsLoaderVersion(version.Id, instance.Loader))
                {
                    // "neoforge-21.1.248" when the instance wants 21.1.264: a
                    // profile for another loader build must not be used. Ids
                    // such as "1.21.1-neoforge" carry no version and remain
                    // acceptable as a last resort.
                    continue;
                }

                if (hasGame)
                {
                    score += version.Id.StartsWith(
                        instance.GameVersion + "-",
                        StringComparison.OrdinalIgnoreCase)
                        ? 60
                        : 30;
                }
                else
                {
                    // No game version in the id (NeoForge style): still a valid
                    // loader profile, but behind ids that name both.
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

        // A loader instance must never launch the plain game: the vanilla base
        // profile has no loader libraries, so the game would start unmodded.
        // Report the missing install so launch can repair it (or tell the user
        // to install the loader) instead of silently booting vanilla.
        if (isLoaderInstance && IsVanillaBase(selected, instance))
        {
            throw new LaunchException(
                "LOADER_NOT_INSTALLED",
                $"{instance.Loader} {instance.LoaderVersion} is not installed for instance {instance.Name}");
        }

        EnsureMerged(selected);
        return selected;
    }


    /// <summary>
    /// True when the id names a concrete loader build, for example
    /// <c>neoforge-21.1.248</c> or <c>1.21.8-forge-58.1.26</c>.
    /// </summary>
    private static bool MentionsLoaderVersion(string id, string loader)
    {
        var marker = loader + "-";
        var index = id.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (index < 0) return false;
        var rest = index + marker.Length;
        return rest < id.Length && char.IsAsciiDigit(id[rest]);
    }

    /// <summary>
    /// Version ids the loader installer uses for this instance, most specific
    /// first. NeoForge omits the game version; legacy NeoForge (1.20.1) used
    /// the Forge coordinates.
    /// </summary>
    internal static IReadOnlyList<string> LoaderProfileIds(GameInstanceRecord instance)
    {
        var game = instance.GameVersion;
        var loader = instance.LoaderVersion;
        if (string.IsNullOrWhiteSpace(loader)) return [];

        var loaderKind = instance.Loader.Trim().ToLowerInvariant();
        return loaderKind switch
        {
            "forge" =>
            [
                $"{game}-forge-{loader}",
                $"forge-{loader}",
            ],
            "neoforge" =>
            [
                $"neoforge-{loader}",
                $"{game}-neoforge-{loader}",
                $"{game}-forge-{loader}",
            ],
            "fabric" => [$"fabric-loader-{loader}-{game}"],
            "quilt" => [$"quilt-loader-{loader}-{game}"],
            _ => [],
        };
    }

    /// <summary>
    /// Reads a version profile, returning null when the file is missing or is
    /// not a launchable profile (game data such as <c>usercache.json</c>, a
    /// loader cache, or a truncated download).
    /// </summary>
    private static MojangVersion? TryReadVersion(string path)
    {
        if (!File.Exists(path)) return null;
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            return MojangJson.ParseVersion(document.RootElement);
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or JsonException
                or ArgumentException
                or MojangException)
        {
            return null;
        }
    }

    /// <summary>
    /// True when a profile is just the vanilla game profile for this instance,
    /// i.e. it carries the game version id and none of the loader's libraries.
    /// </summary>
    private static bool IsVanillaBase(MojangVersion version, GameInstanceRecord instance)
    {
        if (!string.Equals(version.Id, instance.GameVersion, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return !version.Libraries.Any(library =>
            library.Name.Contains(instance.Loader, StringComparison.OrdinalIgnoreCase));
    }

    private static void EnsureMerged(MojangVersion version)
    {
        if (!string.IsNullOrWhiteSpace(version.InheritsFrom))
        {
            throw new LaunchException(
                "VERSION_NOT_MERGED",
                $"version '{version.Id}' still inherits from '{version.InheritsFrom}'; install/merge it first");
        }
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

    private static (Process Process, string LogPath) Start(
        string instanceId,
        LaunchPlan plan,
        int logRetentionFiles,
        int logRetentionMegabytes)
    {
        LaunchLogFiles.Prepare(
            plan.LogDirectory,
            logRetentionFiles,
            logRetentionMegabytes * 1024L * 1024L);
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

        var pid = process.Id;
        var log = new LaunchLogWriter(logPath);
        process.OutputDataReceived += (_, args) =>
        {
            if (args.Data is not null) log.WriteLine(args.Data);
        };
        process.ErrorDataReceived += (_, args) =>
        {
            if (args.Data is not null) log.WriteLine(args.Data);
        };
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        process.EnableRaisingEvents = true;
        process.Exited += (_, _) =>
        {
            // WaitForExit() also drains the asynchronous output handlers, so the
            // exit marker is the last line instead of racing the remaining
            // output.
            try
            {
                process.WaitForExit();
            }
            catch (InvalidOperationException)
            {
                // Already reaped elsewhere.
            }

            log.Complete(process.ExitCode);

            // The log viewer polls the registry to know when a follow can end.
            LaunchProcessRegistry.Unregister(instanceId, pid);
        };
        LaunchProcessRegistry.Register(instanceId, pid, logPath);
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
