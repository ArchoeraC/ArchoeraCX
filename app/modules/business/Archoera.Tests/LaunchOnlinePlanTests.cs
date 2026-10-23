using Archoera.Domain.Accounts;
using Archoera.Domain.Instances;
using Archoera.Domain.Java;
using Archoera.Domain.Mojang;
using Archoera.Domain.Settings;
using Archoera.Infrastructure.Launch;

namespace Archoera.Tests;

/// <summary>
/// Online launch credentials must reach the argument list; the defaults keep
/// the offline plan byte-for-byte identical.
/// </summary>
public sealed class LaunchOnlinePlanTests
{
    [Fact]
    public void Online_credentials_fill_the_substitutions()
    {
        var root = CreateTempDirectory();
        try
        {
            var (instance, version, paths) = CreateFixture(
                root,
                new MojangArguments(
                    Game:
                    [
                        new MojangArgument(
                            [],
                            [
                                "--accessToken", "${auth_access_token}",
                                "--clientId", "${clientid}",
                                "--userType", "${user_type}",
                            ]),
                    ],
                    Jvm: []));

            var plan = LaunchPlanBuilder.Build(
                instance,
                version,
                CreateAccount(),
                CreateJava(),
                CreateSettings(root),
                paths,
                accessToken: "token-online",
                clientId: "client-online",
                userType: "msa");

            AssertArgument(plan, "--accessToken", "token-online");
            AssertArgument(plan, "--clientId", "client-online");
            AssertArgument(plan, "--userType", "msa");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Online_credentials_fill_the_legacy_fallback_arguments()
    {
        var root = CreateTempDirectory();
        try
        {
            var (instance, version, paths) = CreateFixture(root, arguments: null);

            var plan = LaunchPlanBuilder.Build(
                instance,
                version,
                CreateAccount(),
                CreateJava(),
                CreateSettings(root),
                paths,
                accessToken: "token-fallback",
                clientId: "client-fallback",
                userType: "msa");

            AssertArgument(plan, "--accessToken", "token-fallback");
            AssertArgument(plan, "--userType", "msa");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Offline_defaults_stay_unchanged()
    {
        var root = CreateTempDirectory();
        try
        {
            var (instance, version, paths) = CreateFixture(root, arguments: null);

            var plan = LaunchPlanBuilder.Build(
                instance,
                version,
                CreateAccount(),
                CreateJava(),
                CreateSettings(root),
                paths);

            AssertArgument(plan, "--accessToken", "0");
            AssertArgument(plan, "--userType", "legacy");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Window_size_and_fullscreen_reach_the_game_arguments()
    {
        var root = CreateTempDirectory();
        try
        {
            var (instance, version, paths) = CreateFixture(
                root,
                new MojangArguments(
                    Game:
                    [
                        new MojangArgument(
                            [],
                            [
                                "--width", "${resolution_width}",
                                "--height", "${resolution_height}",
                            ]),
                    ],
                    Jvm: []));

            var settings = CreateSettings(root) with
            {
                WindowWidth = 1600,
                WindowHeight = 900,
                Fullscreen = true,
            };

            var plan = LaunchPlanBuilder.Build(
                instance,
                version,
                CreateAccount(),
                CreateJava(),
                settings,
                paths);

            AssertArgument(plan, "--width", "1600");
            AssertArgument(plan, "--height", "900");
            Assert.Contains("--fullscreen", plan.Arguments);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Instance_overrides_win_over_global_settings()
    {
        var root = CreateTempDirectory();
        try
        {
            var (instance, version, paths) = CreateFixture(root, arguments: null);
            var global = CreateSettings(root) with
            {
                MemoryMaxMb = 2048,
                GameArgs = "--global",
            };
            var effective = new InstanceOverrides(
                MemoryMaxMb: 6144,
                GameArgs: "--instance",
                Fullscreen: true).ApplyTo(global);

            var plan = LaunchPlanBuilder.Build(
                instance,
                version,
                CreateAccount(),
                CreateJava(),
                effective,
                paths);

            Assert.Contains("-Xmx6144M", plan.Arguments);
            Assert.Contains("--instance", plan.Arguments);
            Assert.Contains("--fullscreen", plan.Arguments);
            Assert.DoesNotContain("--global", plan.Arguments);
            // The global document is untouched.
            Assert.Equal(2048, global.MemoryMaxMb);
            Assert.Equal("--global", global.GameArgs);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Missing_instance_overrides_fall_back_to_global_settings()
    {
        var global = new AppSettings(
            InstancesRoot: Path.GetTempPath(),
            DataRoot: Path.GetTempPath(),
            SettingsPath: string.Empty,
            MemoryMinMb: 1024,
            MemoryMaxMb: 4096,
            GcMode: "g1gc",
            GameArgs: "--global",
            WindowWidth: 1600,
            WindowHeight: 900,
            Fullscreen: false);

        var effective = new InstanceOverrides().ApplyTo(global);

        Assert.Equal(1024, effective.MemoryMinMb);
        Assert.Equal(4096, effective.MemoryMaxMb);
        Assert.Equal("g1gc", effective.GcMode);
        Assert.Equal("--global", effective.GameArgs);
        Assert.Equal(1600, effective.WindowWidth);
        Assert.Equal(900, effective.WindowHeight);
        Assert.False(effective.Fullscreen);
    }

    [Fact]
    public void Instance_java_override_forces_manual_selection()
    {
        var java = OperatingSystem.IsWindows()
            ? @"C:\Java\bin\java.exe"
            : "/opt/java/bin/java";
        var effective = new InstanceOverrides(JavaPath: java).ApplyTo(
            new AppSettings(
                InstancesRoot: Path.GetTempPath(),
                DataRoot: Path.GetTempPath(),
                SettingsPath: string.Empty,
                JavaAuto: true));

        Assert.False(effective.JavaAuto);
        Assert.Equal(java, effective.JavaPath);
    }

    private static void AssertArgument(LaunchPlan plan, string name, string expected)
    {
        var arguments = plan.Arguments.ToList();
        var index = arguments.IndexOf(name);
        Assert.True(index >= 0, $"missing argument {name}");
        Assert.Equal(expected, arguments[index + 1]);
    }

    private static (
        GameInstanceRecord Instance,
        MojangVersion Version,
        LaunchPaths Paths) CreateFixture(
        string gameRoot,
        MojangArguments? arguments)
    {
        var instanceRoot = Path.Combine(gameRoot, "instances", "island");
        Directory.CreateDirectory(instanceRoot);
        var instance = new GameInstanceRecord(
            "id",
            "island",
            "1.21.1",
            "vanilla",
            string.Empty,
            instanceRoot,
            0,
            false,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            null);
        var version = new MojangVersion
        {
            Id = "1.21.1",
            Type = "release",
            MainClass = "net.minecraft.client.main.Main",
            Assets = "5",
            Arguments = arguments,
        };

        var paths = LaunchPaths.Resolve(instanceRoot);
        // No libraries are declared, so the client jar is the only file the
        // builder needs on disk; its contents are never opened.
        File.WriteAllText(Path.Combine(instanceRoot, "1.21.1.jar"), "jar");
        return (instance, version, paths);
    }

    private static AccountRecord CreateAccount()
        => new(
            "account",
            AccountKind.Microsoft,
            "Player",
            "00000000-0000-0000-0000-000000000001",
            "xuid-1",
            null,
            null,
            true,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            null);

    private static JavaRuntimeInfo CreateJava()
        => new("java", "21.0.4", 21, "Test", "x86_64", "TEST");

    private static AppSettings CreateSettings(string gameRoot)
        => new(gameRoot, gameRoot, Path.Combine(gameRoot, "settings.json"));

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            "archoera-launch-online-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
