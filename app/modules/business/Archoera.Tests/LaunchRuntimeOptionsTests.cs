using Archoera.Domain.Accounts;
using Archoera.Domain.Instances;
using Archoera.Domain.Java;
using Archoera.Domain.Mojang;
using Archoera.Domain.Settings;
using Archoera.Infrastructure.Launch;

namespace Archoera.Tests;

/// <summary>
/// Covers how wrapper, environment and auto-join reach the actual child
/// process: token order, environment precedence, hostile-variable stripping
/// and the Quick Play versus classic server switch.
/// </summary>
public sealed class LaunchRuntimeOptionsTests
{
    [Fact]
    public void Wrapper_is_tokenized_and_comes_before_java()
    {
        var root = CreateTempDirectory();
        try
        {
            var (instance, version, paths) = CreateFixture(root, ClassicArguments());
            var settings = CreateSettings(root) with
            {
                WrapperCommand = "prime-run --keep \"my wrapper\"",
            };

            var plan = LaunchPlanBuilder.Build(
                instance,
                version,
                CreateAccount(),
                CreateJava(),
                settings,
                paths);

            Assert.Equal(["prime-run", "--keep", "my wrapper"], plan.Wrapper);

            var startInfo = LaunchProcessStarter.CreateStartInfo(plan);
            Assert.Equal("prime-run", startInfo.FileName);
            var arguments = startInfo.ArgumentList.ToArray();
            Assert.Equal("--keep", arguments[0]);
            Assert.Equal("my wrapper", arguments[1]);
            Assert.Equal(plan.JavaPath, arguments[2]);
            Assert.Equal(plan.Arguments[0], arguments[3]);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void An_empty_instance_wrapper_disables_the_global_wrapper()
    {
        var root = CreateTempDirectory();
        try
        {
            var (instance, version, paths) = CreateFixture(root, ClassicArguments());
            var global = CreateSettings(root) with { WrapperCommand = "prime-run" };
            var effective = new InstanceOverrides(WrapperCommand: string.Empty)
                .ApplyTo(global);

            var plan = LaunchPlanBuilder.Build(
                instance,
                version,
                CreateAccount(),
                CreateJava(),
                effective,
                paths);

            Assert.Empty(plan.Wrapper);
            var startInfo = LaunchProcessStarter.CreateStartInfo(plan);
            Assert.Equal(plan.JavaPath, startInfo.FileName);
            Assert.DoesNotContain(plan.JavaPath, startInfo.ArgumentList);
            Assert.Equal(plan.Arguments[0], startInfo.ArgumentList[0]);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Jvm_and_game_arguments_use_the_quote_aware_tokenizer()
    {
        var root = CreateTempDirectory();
        try
        {
            var (instance, version, paths) = CreateFixture(root, ClassicArguments());
            var settings = CreateSettings(root) with
            {
                JvmArgs = "-Dname=\"My Game\" \"-Dempty=\"",
                GameArgs = "--title 'My World'",
            };

            var plan = LaunchPlanBuilder.Build(
                instance,
                version,
                CreateAccount(),
                CreateJava(),
                settings,
                paths);

            Assert.Contains("-Dname=My Game", plan.Arguments);
            Assert.Contains("-Dempty=", plan.Arguments);
            AssertArgument(plan, "--title", "My World");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Malformed_user_arguments_fail_with_launch_args_invalid()
    {
        var root = CreateTempDirectory();
        try
        {
            var (instance, version, paths) = CreateFixture(root, ClassicArguments());
            foreach (var settings in new[]
                     {
                         CreateSettings(root) with { WrapperCommand = "\"unclosed" },
                         CreateSettings(root) with { GameArgs = "'unclosed" },
                     })
            {
                var exception = Assert.Throws<LaunchException>(
                    () => LaunchPlanBuilder.Build(
                        instance,
                        version,
                        CreateAccount(),
                        CreateJava(),
                        settings,
                        paths));
                Assert.Equal("LAUNCH_ARGS_INVALID", exception.Code);
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Environment_precedence_is_optimization_then_global_then_instance()
    {
        var root = CreateTempDirectory();
        try
        {
            var (instance, version, paths) = CreateFixture(root, ClassicArguments());
            var global = CreateSettings(root) with
            {
                Environment = new Dictionary<string, string>
                {
                    ["A"] = "global",
                    ["B"] = "global",
                    ["JAVA_TOOL_OPTIONS"] = "-Xmx1G",
                },
            };
            var overrides = new InstanceOverrides(
                Environment: new Dictionary<string, string>
                {
                    ["B"] = "instance",
                    ["C"] = "instance",
                });
            var effective = overrides.ApplyTo(global);

            var optimization = new LaunchOptimizationPlan(
                [],
                new Dictionary<string, string>
                {
                    ["A"] = "optimization",
                    ["D"] = "optimization",
                },
                []);

            var plan = LaunchPlanBuilder.Build(
                instance,
                version,
                CreateAccount(),
                CreateJava(),
                effective,
                paths,
                optimizationPlan: optimization);

            Assert.Equal("global", plan.Environment["A"]);
            Assert.Equal("instance", plan.Environment["B"]);
            Assert.Equal("instance", plan.Environment["C"]);
            Assert.Equal("optimization", plan.Environment["D"]);
            Assert.DoesNotContain("JAVA_TOOL_OPTIONS", plan.Environment.Keys);
            Assert.DoesNotContain("_JAVA_OPTIONS", plan.Environment.Keys);
            Assert.DoesNotContain("JDK_JAVA_OPTIONS", plan.Environment.Keys);
            Assert.DoesNotContain("CLASSPATH", plan.Environment.Keys);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void An_empty_instance_environment_clears_the_global_map_before_launch()
    {
        var root = CreateTempDirectory();
        try
        {
            var (instance, version, paths) = CreateFixture(root, ClassicArguments());
            var global = CreateSettings(root) with
            {
                Environment = new Dictionary<string, string> { ["A"] = "global" },
            };
            var effective = new InstanceOverrides(
                Environment: new Dictionary<string, string>()).ApplyTo(global);

            var plan = LaunchPlanBuilder.Build(
                instance,
                version,
                CreateAccount(),
                CreateJava(),
                effective,
                paths);

            Assert.Empty(plan.Environment);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Server_join_uses_quick_play_when_the_version_supports_it()
    {
        var root = CreateTempDirectory();
        try
        {
            var (instance, version, paths) = CreateFixture(root, QuickPlayArguments());
            instance = WithJoin(instance, "mc.example.org:25566");

            var plan = LaunchPlanBuilder.Build(
                instance,
                version,
                CreateAccount(),
                CreateJava(),
                CreateSettings(root),
                paths);

            AssertArgumentLast(plan, "--quickPlayMultiplayer", "mc.example.org:25566");
            Assert.DoesNotContain("--server", plan.Arguments);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Server_join_falls_back_to_server_and_port_on_older_versions()
    {
        var root = CreateTempDirectory();
        try
        {
            var (instance, version, paths) = CreateFixture(root, ClassicArguments());
            instance = WithJoin(instance, "mc.example.org");

            var plan = LaunchPlanBuilder.Build(
                instance,
                version,
                CreateAccount(),
                CreateJava(),
                CreateSettings(root),
                paths);

            AssertArgument(plan, "--server", "mc.example.org");
            AssertArgument(plan, "--port", "25565");
            Assert.DoesNotContain("--quickPlayMultiplayer", plan.Arguments);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Server_join_keeps_a_custom_port_and_brackets_ipv6_for_quick_play()
    {
        var root = CreateTempDirectory();
        try
        {
            var (instance, version, paths) = CreateFixture(root, QuickPlayArguments());
            instance = WithJoin(instance, "[2001:db8::1]:25567");

            var plan = LaunchPlanBuilder.Build(
                instance,
                version,
                CreateAccount(),
                CreateJava(),
                CreateSettings(root),
                paths);

            AssertArgumentLast(plan, "--quickPlayMultiplayer", "[2001:db8::1]:25567");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void World_join_is_added_only_when_quick_play_single_player_is_supported()
    {
        var root = CreateTempDirectory();
        try
        {
            var (instance, version, paths) = CreateFixture(root, QuickPlayArguments());
            instance = WithJoin(instance, world: "My World");
            var withSupport = LaunchPlanBuilder.Build(
                instance,
                version,
                CreateAccount(),
                CreateJava(),
                CreateSettings(root),
                paths);
            AssertArgumentLast(withSupport, "--quickPlaySingleplayer", "My World");

            var (oldInstance, oldVersion, oldPaths) = CreateFixture(root, ClassicArguments());
            oldInstance = WithJoin(oldInstance, world: "My World");
            var withoutSupport = LaunchPlanBuilder.Build(
                oldInstance,
                oldVersion,
                CreateAccount(),
                CreateJava(),
                CreateSettings(root),
                oldPaths);
            Assert.DoesNotContain("--quickPlaySingleplayer", withoutSupport.Arguments);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Join_is_ignored_when_the_override_is_disabled()
    {
        var root = CreateTempDirectory();
        try
        {
            var (instance, version, paths) = CreateFixture(root, QuickPlayArguments());
            instance = instance with
            {
                Overrides = new InstanceOverrides(
                    JoinServerOnLaunch: false,
                    JoinServerAddress: "mc.example.org",
                    JoinWorldName: "My World"),
            };

            var plan = LaunchPlanBuilder.Build(
                instance,
                version,
                CreateAccount(),
                CreateJava(),
                CreateSettings(root),
                paths);

            Assert.DoesNotContain("mc.example.org", plan.Arguments);
            Assert.DoesNotContain("My World", plan.Arguments);
            Assert.DoesNotContain("--server", plan.Arguments);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Join_without_a_target_fails_at_launch()
    {
        var root = CreateTempDirectory();
        try
        {
            var (instance, version, paths) = CreateFixture(root, QuickPlayArguments());
            instance = instance with
            {
                Overrides = new InstanceOverrides(JoinServerOnLaunch: true),
            };

            var exception = Assert.Throws<LaunchException>(
                () => LaunchPlanBuilder.Build(
                    instance,
                    version,
                    CreateAccount(),
                    CreateJava(),
                    CreateSettings(root),
                    paths));
            Assert.Equal("LAUNCH_ARGS_INVALID", exception.Code);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Hostile_environment_is_stripped_from_the_final_child_environment()
    {
        var plan = new LaunchPlan(
            JavaPath: "/jdk/bin/java",
            GameDirectory: Path.GetTempPath(),
            LogDirectory: Path.GetTempPath(),
            Wrapper: [],
            Arguments: ["-Xmx1G", "Main"],
            Environment: new Dictionary<string, string>
            {
                ["JAVA_TOOL_OPTIONS"] = "-Xmx1G",
                ["_JAVA_OPTIONS"] = "-Xmx1G",
                ["JDK_JAVA_OPTIONS"] = "-Xmx1G",
                ["CLASSPATH"] = "/tmp/a.jar",
                ["SAFE"] = "1",
            });

        var startInfo = LaunchProcessStarter.CreateStartInfo(plan);

        Assert.False(startInfo.Environment.ContainsKey("JAVA_TOOL_OPTIONS"));
        Assert.False(startInfo.Environment.ContainsKey("_JAVA_OPTIONS"));
        Assert.False(startInfo.Environment.ContainsKey("JDK_JAVA_OPTIONS"));
        Assert.False(startInfo.Environment.ContainsKey("CLASSPATH"));
        Assert.Equal("1", startInfo.Environment["SAFE"]);
    }

    [Fact]
    public void Maximized_uses_the_configured_window_size_and_fullscreen_still_wins()
    {
        var root = CreateTempDirectory();
        try
        {
            var (instance, version, paths) = CreateFixture(root, WindowArguments());
            var maximized = LaunchPlanBuilder.Build(
                instance,
                version,
                CreateAccount(),
                CreateJava(),
                CreateSettings(root) with
                {
                    Maximized = true,
                    WindowWidth = 2560,
                    WindowHeight = 1440,
                },
                paths);

            // Vanilla has no maximized flag; the UI stores the display size,
            // which is what the game receives as resolution.
            Assert.DoesNotContain("--fullscreen", maximized.Arguments);
            AssertArgument(maximized, "--width", "2560");
            AssertArgument(maximized, "--height", "1440");

            // A hand-edited document with both modes must behave as fullscreen.
            var both = LaunchPlanBuilder.Build(
                instance,
                version,
                CreateAccount(),
                CreateJava(),
                CreateSettings(root) with
                {
                    Maximized = true,
                    Fullscreen = true,
                    WindowWidth = 1920,
                    WindowHeight = 1080,
                },
                paths);

            Assert.Contains("--fullscreen", both.Arguments);
            AssertArgument(both, "--width", "1920");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Linux_presets_compose_the_wrapper_and_user_environment_wins()
    {
        if (!OperatingSystem.IsLinux()) return;

        var root = CreateTempDirectory();
        try
        {
            var (instance, version, paths) = CreateFixture(root, ClassicArguments());
            var settings = CreateSettings(root) with
            {
                WrapperCommand = "mangohud",
                EnableGameMode = true,
                EnableMangoHud = true,
                UseZink = true,
                Environment = new Dictionary<string, string>
                {
                    ["MANGOHUD"] = "0",
                    ["MESA_LOADER_DRIVER_OVERRIDE"] = "user-loader",
                    ["GALLIUM_DRIVER"] = "user-gallium",
                },
            };

            var plan = LaunchPlanBuilder.Build(
                instance,
                version,
                CreateAccount(),
                CreateJava(),
                settings,
                paths);

            Assert.Equal(["gamemoderun", "mangohud"], plan.Wrapper);
            Assert.Equal("0", plan.Environment["MANGOHUD"]);
            Assert.Equal("user-loader", plan.Environment["MESA_LOADER_DRIVER_OVERRIDE"]);
            Assert.Equal("user-gallium", plan.Environment["GALLIUM_DRIVER"]);

            var startInfo = LaunchProcessStarter.CreateStartInfo(plan);
            Assert.Equal("gamemoderun", startInfo.FileName);
            Assert.Equal("mangohud", startInfo.ArgumentList[0]);
            Assert.Equal(plan.JavaPath, startInfo.ArgumentList[1]);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Native_overrides_are_inserted_before_user_jvm_arguments()
    {
        if (!OperatingSystem.IsLinux()) return;

        var root = CreateTempDirectory();
        try
        {
            var glfw = Path.Combine(root, "libglfw.so");
            var sdl = Path.Combine(root, "libSDL2.so");
            File.WriteAllText(glfw, "native");
            File.WriteAllText(sdl, "native");
            var userGlfw = "-Dorg.lwjgl.glfw.libname=/user/libglfw.so";
            var (instance, version, paths) = CreateFixture(root, ClassicArguments());

            var plan = LaunchPlanBuilder.Build(
                instance,
                version,
                CreateAccount(),
                CreateJava(),
                CreateSettings(root) with
                {
                    DeveloperMode = true,
                    NativeGlfwPath = glfw,
                    // Missing on purpose: it must be skipped without failing.
                    NativeOpenALPath = Path.Combine(root, "missing-openal.so"),
                    NativeSdlPath = sdl,
                    JvmArgs = userGlfw,
                },
                paths);

            var arguments = plan.Arguments.ToList();
            var nativeGlfwIndex = arguments.IndexOf($"-Dorg.lwjgl.glfw.libname={glfw}");
            var userGlfwIndex = arguments.IndexOf(userGlfw);
            Assert.True(nativeGlfwIndex >= 0);
            Assert.True(userGlfwIndex > nativeGlfwIndex);
            Assert.Contains($"-Dorg.lwjgl.sdl.libname={sdl}", arguments);
            Assert.DoesNotContain(
                arguments,
                argument => argument.StartsWith(
                    "-Dorg.lwjgl.openal.libname=",
                    StringComparison.Ordinal));

            // Without developer mode the saved paths are ignored entirely.
            var disabled = LaunchPlanBuilder.Build(
                instance,
                version,
                CreateAccount(),
                CreateJava(),
                CreateSettings(root) with
                {
                    DeveloperMode = false,
                    NativeGlfwPath = glfw,
                    NativeSdlPath = sdl,
                },
                paths);
            Assert.DoesNotContain(
                disabled.Arguments,
                argument => argument.StartsWith(
                    "-Dorg.lwjgl.",
                    StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static MojangArguments WindowArguments()
        => new(
            Game:
            [
                new MojangArgument(
                    [],
                    [
                        "--width", "${resolution_width}",
                        "--height", "${resolution_height}",
                    ]),
            ],
            Jvm: []);

    private static void AssertArgument(LaunchPlan plan, string name, string expected)
    {
        var arguments = plan.Arguments.ToList();
        var index = arguments.IndexOf(name);
        Assert.True(index >= 0, $"missing argument {name}");
        Assert.Equal(expected, arguments[index + 1]);
    }

    /// <summary>
    /// Quick Play flags also appear in the version profile with an empty
    /// substituted value; the auto-join pair is appended after it, so the
    /// last occurrence is the effective one.
    /// </summary>
    private static void AssertArgumentLast(LaunchPlan plan, string name, string expected)
    {
        var arguments = plan.Arguments.ToList();
        var index = arguments.LastIndexOf(name);
        Assert.True(index >= 0, $"missing argument {name}");
        Assert.True(index + 1 < arguments.Count, $"missing value for {name}");
        Assert.Equal(expected, arguments[index + 1]);
    }

    private static GameInstanceRecord WithJoin(
        GameInstanceRecord instance,
        string? address = null,
        string? world = null)
        => instance with
        {
            Overrides = new InstanceOverrides(
                JoinServerOnLaunch: true,
                JoinServerAddress: address,
                JoinWorldName: world),
        };

    private static MojangArguments ClassicArguments()
        => new(
            Game:
            [
                new MojangArgument(
                    [],
                    [
                        "--username", "${auth_player_name}",
                        "--version", "${version_name}",
                    ]),
            ],
            Jvm: []);

    private static MojangArguments QuickPlayArguments()
        => new(
            Game:
            [
                new MojangArgument(
                    [],
                    [
                        "--username", "${auth_player_name}",
                        "--quickPlayMultiplayer", "${quickPlayMultiplayer}",
                        "--quickPlaySingleplayer", "${quickPlaySingleplayer}",
                    ]),
            ],
            Jvm: []);

    private static (
        GameInstanceRecord Instance,
        MojangVersion Version,
        LaunchPaths Paths) CreateFixture(
        string gameRoot,
        MojangArguments arguments)
    {
        var instanceRoot = Path.Combine(gameRoot, "instances", Guid.NewGuid().ToString("N"));
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
        File.WriteAllText(Path.Combine(instanceRoot, "1.21.1.jar"), "jar");
        return (instance, version, paths);
    }

    private static AccountRecord CreateAccount()
        => new(
            "account",
            AccountKind.Offline,
            "Player",
            "00000000-0000-0000-0000-000000000001",
            null,
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
            "archoera-launch-runtime-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
