using Archoera.Application.Modules;
using Archoera.Infrastructure.Accounts;
using Archoera.Infrastructure.Instances;
using Archoera.Infrastructure.Java;
using Archoera.Infrastructure.Launch;
using Archoera.Infrastructure.Loaders;
using Archoera.Infrastructure.Mojang;
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
    public static ModuleMethodRegistry CreateDefault(string? dataRoot = null)
    {
        var policy = new DownloadPolicy(dataRoot);
        var handlers = new List<IModuleMethodHandler>
        {
            new JavaModuleService(
                settings: dataRoot is null ? null : new JsonAppSettingsStore(dataRoot)),
            new VersionModuleService(policy: policy),
            new LaunchModuleService(dataRoot: dataRoot),
            new LoaderVersionService(policy: policy),
            new LoaderModuleService(policy: policy),
            new ResourceModuleService(policy: policy),
            new DownloadPolicyModuleService(policy),
            new AccountModuleService(
                accounts: new JsonAccountStore(dataRoot),
                credentials: new VaultCredentialStore(dataRoot),
                dataRoot: dataRoot),
            new SystemModuleService(),
            new SettingsModuleService(
                dataRoot is null ? null : new JsonAppSettingsStore(dataRoot)),
            new InstanceModuleService(
                dataRoot is null
                    ? null
                    : new SqliteInstanceRepository(
                        dataRoot,
                        new JsonAppSettingsStore(dataRoot)),
                dataRoot is null ? null : new JsonAppSettingsStore(dataRoot)),
        };

        return new ModuleMethodRegistry(handlers);

    }
}
