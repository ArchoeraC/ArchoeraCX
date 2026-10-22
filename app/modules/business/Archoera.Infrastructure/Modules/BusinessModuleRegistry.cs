using Archoera.Application.Modules;
using Archoera.Infrastructure.Accounts;
using Archoera.Infrastructure.Community;
using Archoera.Infrastructure.Instances;
using Archoera.Infrastructure.Java;
using Archoera.Infrastructure.Launch;
using Archoera.Infrastructure.Loaders;
using Archoera.Infrastructure.Mojang;
using Archoera.Infrastructure.Packs;
using Archoera.Infrastructure.Resources;
using Archoera.Infrastructure.Settings;
using Archoera.Infrastructure.SystemInfo;

namespace Archoera.Infrastructure.Modules;

/// <summary>
/// The single composition root for the C# business module. Every public
/// method family is registered here; the NativeAOT export layer only marshals
/// and dispatches through the resulting registry.
/// </summary>
public static class BusinessModuleRegistry
{
    private static readonly HttpClient AuthHttp = new()
    {
        Timeout = TimeSpan.FromSeconds(60),
    };

    public static ModuleMethodRegistry CreateDefault(string? dataRoot = null)
    {
        var region = new RegionPolicy();
        var policy = new DownloadPolicy(dataRoot, region);
        var settings = dataRoot is null ? null : new JsonAppSettingsStore(dataRoot);
        var instances = dataRoot is null
            ? null
            : new SqliteInstanceRepository(dataRoot, settings);
        var accounts = new JsonAccountStore(dataRoot);
        var credentials = new VaultCredentialStore(dataRoot);
        var auth = new MicrosoftAuthClient(AuthHttp, new MicrosoftAuthOptions());
        var handlers = new List<IModuleMethodHandler>
        {
            new JavaModuleService(
                settings: dataRoot is null ? null : new JsonAppSettingsStore(dataRoot)),
            new VersionModuleService(policy: policy),
            new LaunchModuleService(
                dataRoot: dataRoot,
                accounts: accounts,
                credentials: credentials,
                auth: auth),
            new LaunchLogModuleService(instances),
            new LoaderVersionService(policy: policy),
            new LoaderModuleService(policy: policy),
            new ResourceModuleService(policy: policy),
            new DownloadPolicyModuleService(policy),
            new RegionPolicyModuleService(region),
            new AccountModuleService(
                accounts: accounts,
                credentials: credentials,
                auth: auth,
                dataRoot: dataRoot),
            new SystemModuleService(),
            new CommunityModuleService(),
            new SettingsModuleService(
                dataRoot is null ? null : new JsonAppSettingsStore(dataRoot)),
            new InstanceModuleService(
                instances,
                settings),
            new PackModuleService(
                new PackInstallService(
                    repository: instances,
                    settings: settings),
                policy),
        };

        return new ModuleMethodRegistry(handlers);

    }
}
