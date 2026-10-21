using System.Text;
using System.Text.Json;
using Archoera.Application.Mojang;
using Archoera.Domain.Mojang;

namespace Archoera.Tests;

/// <summary>
/// Synthetic version-merge coverage. No test touches the network or a real
/// installation: every layer is an in-memory JSON string (docs/08 01 §8).
/// </summary>
public sealed class VersionMergeTests
{
    private static readonly VersionMergeService Service = new();

    private const string VanillaJson = """
        {
          "id": "1.20.1",
          "type": "release",
          "mainClass": "net.minecraft.client.main.Main",
          "assets": "5",
          "assetIndex": {
            "id": "5",
            "url": "https://piston-meta.mojang.com/v1/packages/index/5.json",
            "sha1": "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
            "size": 500,
            "totalSize": 2000
          },
          "downloads": {
            "client": {
              "url": "https://piston-data.mojang.com/v1/objects/client/client.jar",
              "sha1": "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb",
              "size": 1000
            }
          },
          "javaVersion": { "component": "java-runtime-gamma", "majorVersion": 17 },
          "logging": {
            "client": {
              "argument": "-Dlog4j.configurationFile=${path}",
              "file": {
                "id": "client-1.12.xml",
                "url": "https://piston-data.mojang.com/v1/objects/log/client-1.12.xml",
                "sha1": "cccccccccccccccccccccccccccccccccccccccc",
                "size": 42,
                "type": "log4j2-xml"
              }
            }
          },
          "minimumLauncherVersion": 21,
          "complianceLevel": 1,
          "releaseTime": "2024-08-08T12:00:00Z",
          "time": "2024-08-08T12:00:00Z",
          "customField": { "from": "root" },
          "rootOnly": "kept",
          "libraries": [
            {
              "name": "com.google.guava:guava:32.1.2-jre",
              "downloads": {
                "artifact": {
                  "url": "https://libraries.minecraft.net/com/google/guava/guava/32.1.2-jre/guava-32.1.2-jre.jar",
                  "sha1": "1111111111111111111111111111111111111111",
                  "size": 10,
                  "path": "com/google/guava/guava/32.1.2-jre/guava-32.1.2-jre.jar"
                }
              }
            },
            {
              "name": "org.ow2.asm:asm:9.6",
              "url": "https://maven.fabricmc.net/"
            },
            {
              "name": "org.lwjgl:lwjgl:3.3.3:natives-windows",
              "rules": [
                { "action": "allow", "os": { "name": "windows" } }
              ]
            }
          ],
          "arguments": {
            "jvm": [
              "-Xmx2G",
              {
                "rules": [{ "action": "allow", "os": { "name": "osx" } }],
                "value": ["-XstartOnFirstThread"]
              }
            ],
            "game": ["--username", "${auth_player_name}"]
          }
        }
        """;

    private const string FabricJson = """
        {
          "id": "fabric-loader-0.15.11-1.20.1",
          "inheritsFrom": "1.20.1",
          "mainClass": "net.fabricmc.loader.impl.launch.knot.KnotClient",
          "arguments": {
            "jvm": ["-DFabricMcEmu= net.minecraft.client.main.Main "],
            "game": ["--fabric"]
          },
          "libraries": [
            {
              "name": "com.google.guava:guava:31.1-jre",
              "downloads": {
                "artifact": {
                  "url": "https://libraries.minecraft.net/com/google/guava/guava/31.1-jre/guava-31.1-jre.jar",
                  "sha1": "2222222222222222222222222222222222222222",
                  "size": 11
                }
              }
            },
            {
              "name": "net.fabricmc:intermediary:1.20.1",
              "url": "https://maven.fabricmc.net/"
            },
            {
              "name": "net.fabricmc:fabric-loader:0.15.11",
              "url": "https://maven.fabricmc.net/"
            }
          ],
          "minimumLauncherVersion": 17,
          "releaseTime": "2026-10-20T09:54:00+0000",
          "time": "2026-10-20T09:54:00+0000",
          "customField": { "from": "child" }
        }
        """;

    [Fact]
    public async Task MergeAsync_flattens_vanilla_and_fabric_like_profile()
    {
        var effective = await Service.MergeAsync(
            "fabric-loader-0.15.11-1.20.1",
            Lookup(Vanilla(), Fabric()));

        Assert.Equal("fabric-loader-0.15.11-1.20.1", effective.Id);
        Assert.Equal(["1.20.1", "fabric-loader-0.15.11-1.20.1"], effective.Chain);
        Assert.Null(effective.Version.InheritsFrom);

        // Leaf scalar overrides; the vanilla scalars are inherited.
        Assert.Equal("net.fabricmc.loader.impl.launch.knot.KnotClient", effective.Version.MainClass);
        Assert.Equal("5", effective.Version.Assets);
        Assert.Equal("5", effective.Version.AssetIndex?.Id);
        Assert.Equal(17, effective.Version.JavaVersion?.MajorVersion);
        Assert.Equal("client-1.12.xml", effective.Version.Logging?.Client?.Id);
        Assert.NotNull(effective.Version.Downloads.Client);

        // minimumLauncherVersion is the maximum across the chain.
        Assert.Equal(21, effective.Version.MinimumLauncherVersion);
        Assert.Equal(1, effective.Version.ComplianceLevel);

        // Root position is kept, leaf version wins.
        Assert.Equal(5, effective.Version.Libraries.Count);
        Assert.Equal("com.google.guava:guava:31.1-jre", effective.Version.Libraries[0].Name);
        Assert.Equal("org.ow2.asm:asm:9.6", effective.Version.Libraries[1].Name);
        Assert.Equal("org.lwjgl:lwjgl:3.3.3:natives-windows", effective.Version.Libraries[2].Name);
        Assert.Equal("net.fabricmc:intermediary:1.20.1", effective.Version.Libraries[3].Name);
        Assert.Equal("net.fabricmc:fabric-loader:0.15.11", effective.Version.Libraries[4].Name);

        var warning = Assert.Single(effective.Warnings);
        Assert.Equal(VersionMergeWarningCode.LibraryOverride, warning.Code);
        Assert.Equal("com.google.guava:guava::jar", warning.LibraryKey);
        Assert.Equal("32.1.2-jre", warning.OldValue);
        Assert.Equal("31.1-jre", warning.NewValue);
        Assert.Equal("fabric-loader-0.15.11-1.20.1", warning.LayerId);

        // Parent arguments come first; rules and placeholders stay verbatim.
        var jvm = effective.Version.Arguments!.Jvm;
        Assert.Equal(3, jvm.Count);
        Assert.Equal("-Xmx2G", Assert.Single(jvm[0].Values));
        Assert.Single(jvm[1].Rules);
        Assert.Equal("-XstartOnFirstThread", Assert.Single(jvm[1].Values));
        Assert.Equal(
            "-DFabricMcEmu= net.minecraft.client.main.Main ",
            Assert.Single(jvm[2].Values));

        var game = effective.Version.Arguments.Game;
        Assert.Equal(3, game.Count);
        Assert.Equal("--username", Assert.Single(game[0].Values));
        Assert.Equal("${auth_player_name}", Assert.Single(game[1].Values));
        Assert.Equal("--fabric", Assert.Single(game[2].Values));

        using var document = JsonDocument.Parse(effective.Json);
        var root = document.RootElement;
        Assert.False(root.TryGetProperty("inheritsFrom", out _));
        Assert.Equal("child", root.GetProperty("customField").GetProperty("from").GetString());
        Assert.Equal("kept", root.GetProperty("rootOnly").GetString());
        Assert.Equal(
            "2026-10-20T09:54:00+0000",
            root.GetProperty("releaseTime").GetString());

        var json = await Service.MergeJsonAsync(
            "fabric-loader-0.15.11-1.20.1",
            Lookup(Vanilla(), Fabric()));
        Assert.Equal(effective.Json, json);
    }

    [Fact]
    public async Task MergeAsync_preserves_forge_empty_url_library_without_download_or_duplication()
    {
        const string path =
            "net/minecraftforge/forge/1.20.1-47.2.0/forge-1.20.1-47.2.0-client.jar";

        const string parent = """
            {
              "id": "1.20.1",
              "type": "release",
              "mainClass": "net.minecraft.client.main.Main",
              "libraries": [{ "name": "org.ow2.asm:asm:9.6" }]
            }
            """;

        const string forge = """
            {
              "id": "1.20.1-forge-47.2.0",
              "inheritsFrom": "1.20.1",
              "mainClass": "cpw.mods.bootstraplauncher.BootstrapLauncher",
              "libraries": [
                {
                  "name": "net.minecraftforge:forge:1.20.1-47.2.0:client",
                  "downloads": {
                    "artifact": {
                      "path": "net/minecraftforge/forge/1.20.1-47.2.0/forge-1.20.1-47.2.0-client.jar",
                      "url": "",
                      "sha1": "dddddddddddddddddddddddddddddddddddddddd",
                      "size": 123
                    }
                  },
                  "mmc": { "client": true }
                },
                {
                  "name": "net.minecraftforge:forge:1.20.1-47.2.0:client",
                  "downloads": {
                    "artifact": {
                      "path": "net/minecraftforge/forge/1.20.1-47.2.0/forge-1.20.1-47.2.0-client.jar",
                      "url": "",
                      "sha1": "dddddddddddddddddddddddddddddddddddddddd",
                      "size": 123
                    }
                  },
                  "mmc": { "client": true }
                },
                {
                  "name": "cpw.mods:modlauncher:10.1.1",
                  "url": "https://maven.minecraftforge.net/"
                }
              ],
              "arguments": {
                "jvm": ["-p", "${library_directory}", "--add-modules", "ALL-MODULE-PATH"]
              }
            }
            """;

        var effective = await Service.MergeAsync(
            "1.20.1-forge-47.2.0",
            Lookup(new VersionLayer("1.20.1", parent, "test"), new VersionLayer("1.20.1-forge-47.2.0", forge, "test")));

        Assert.Equal(
            "cpw.mods.bootstraplauncher.BootstrapLauncher",
            effective.Version.MainClass);

        var forges = effective.Version.Libraries
            .Where(library => library.Name == "net.minecraftforge:forge:1.20.1-47.2.0:client")
            .ToArray();
        var preserved = Assert.Single(forges);
        Assert.Equal(string.Empty, preserved.Downloads?.Artifact?.Url);
        Assert.Equal(path, preserved.Downloads?.Artifact?.Path);

        using var document = JsonDocument.Parse(effective.Json);
        var library = document.RootElement
            .GetProperty("libraries")
            .EnumerateArray()
            .Single(item => item.GetProperty("name").GetString()!.Contains(":forge:", StringComparison.Ordinal));
        Assert.Equal(string.Empty, library.GetProperty("downloads").GetProperty("artifact").GetProperty("url").GetString());
        Assert.Equal(path, library.GetProperty("downloads").GetProperty("artifact").GetProperty("path").GetString());
        Assert.True(library.GetProperty("mmc").GetProperty("client").GetBoolean());
        Assert.Empty(effective.Warnings);
    }

    [Fact]
    public void Merge_converts_legacy_minecraft_arguments_with_quotes_and_backslashes()
    {
        const string legacy = """
            {
              "id": "1.7.10",
              "type": "release",
              "minecraftArguments": "--username ${auth_player_name} --gameDir \"${game_directory}\" --path \"C:\\Games\\Minecraft\" --demo --empty \"\"",
              "libraries": []
            }
            """;

        var effective = Service.Merge([new VersionLayer("1.7.10", legacy, "test")]);

        var tokens = effective.Version.Arguments!.Game
            .SelectMany(argument => argument.Values)
            .ToArray();
        Assert.Equal(
            [
                "--username",
                "${auth_player_name}",
                "--gameDir",
                "${game_directory}",
                "--path",
                "C:\\Games\\Minecraft",
                "--demo",
                "--empty",
                string.Empty,
            ],
            tokens);

        var warning = Assert.Single(effective.Warnings);
        Assert.Equal(VersionMergeWarningCode.LegacyArgumentsConverted, warning.Code);
        Assert.Equal("1.7.10", warning.LayerId);
    }

    [Fact]
    public void Merge_ignores_legacy_arguments_when_the_same_layer_has_structured_game_arguments()
    {
        const string layer = """
            {
              "id": "modern",
              "minecraftArguments": "--legacy --ignored",
              "arguments": { "game": ["--structured"] }
            }
            """;

        var effective = Service.Merge([new VersionLayer("modern", layer, "test")]);

        var tokens = effective.Version.Arguments!.Game
            .SelectMany(argument => argument.Values)
            .ToArray();
        Assert.Equal(["--structured"], tokens);

        var warning = Assert.Single(effective.Warnings);
        Assert.Equal(VersionMergeWarningCode.LegacyArgumentsIgnored, warning.Code);
    }

    [Fact]
    public void Merge_converts_legacy_parent_arguments_and_appends_structured_child_arguments()
    {
        const string parent = """
            {
              "id": "1.7.10",
              "minecraftArguments": "--username ${auth_player_name} --demo"
            }
            """;

        const string child = """
            {
              "id": "child",
              "inheritsFrom": "1.7.10",
              "arguments": { "game": ["--child"] }
            }
            """;

        var effective = Service.Merge([
            new VersionLayer("1.7.10", parent, "test"),
            new VersionLayer("child", child, "test"),
        ]);

        var tokens = effective.Version.Arguments!.Game
            .SelectMany(argument => argument.Values)
            .ToArray();
        Assert.Equal(["--username", "${auth_player_name}", "--demo", "--child"], tokens);
    }

    [Fact]
    public void Merge_keeps_classifier_and_extension_keys_distinct_and_override_positions()
    {
        const string root = """
            {
              "id": "root",
              "libraries": [
                { "name": "org.lwjgl:lwjgl:3.3.3" },
                { "name": "org.lwjgl:lwjgl:3.3.3:natives-windows" },
                { "name": "org.lwjgl:lwjgl:3.3.3:natives-linux" },
                { "name": "org.ow2.asm:asm:9.6@zip" }
              ]
            }
            """;

        const string child = """
            {
              "id": "child",
              "inheritsFrom": "root",
              "libraries": [
                { "name": "org.lwjgl:lwjgl:3.3.4" },
                { "name": "org.lwjgl:lwjgl:3.3.4:natives-windows" },
                { "name": "org.ow2.asm:asm:9.5" }
              ]
            }
            """;

        var effective = Service.Merge([
            new VersionLayer("root", root, "test"),
            new VersionLayer("child", child, "test"),
        ]);

        Assert.Equal(5, effective.Version.Libraries.Count);
        Assert.Equal("org.lwjgl:lwjgl:3.3.4", effective.Version.Libraries[0].Name);
        Assert.Equal("org.lwjgl:lwjgl:3.3.4:natives-windows", effective.Version.Libraries[1].Name);
        Assert.Equal("org.lwjgl:lwjgl:3.3.3:natives-linux", effective.Version.Libraries[2].Name);
        Assert.Equal("org.ow2.asm:asm:9.6@zip", effective.Version.Libraries[3].Name);
        Assert.Equal("org.ow2.asm:asm:9.5", effective.Version.Libraries[4].Name);

        Assert.Equal(2, effective.Warnings.Count);
        Assert.All(effective.Warnings, warning =>
            Assert.Equal(VersionMergeWarningCode.LibraryOverride, warning.Code));
    }

    [Fact]
    public void Merge_leaf_scalars_override_ancestors_and_logging_file_is_inherited()
    {
        const string root = """
            {
              "id": "root",
              "type": "release",
              "mainClass": "root.Main",
              "assets": "5",
              "assetIndex": {
                "id": "5",
                "url": "https://example.invalid/5.json",
                "sha1": "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
                "size": 1
              },
              "downloads": {
                "client": {
                  "url": "https://example.invalid/root-client.jar",
                  "sha1": "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb",
                  "size": 2
                }
              },
              "javaVersion": { "component": "java-runtime-gamma", "majorVersion": 17 },
              "logging": {
                "client": {
                  "argument": "-Dlog4j.configurationFile=${path}",
                  "file": {
                    "id": "client-1.12.xml",
                    "url": "https://example.invalid/client-1.12.xml",
                    "sha1": "cccccccccccccccccccccccccccccccccccccccc",
                    "size": 3
                  }
                }
              },
              "minimumLauncherVersion": 21,
              "complianceLevel": 2
            }
            """;

        const string child = """
            {
              "id": "child",
              "inheritsFrom": "root",
              "mainClass": "child.Main",
              "assets": "6",
              "assetIndex": {
                "id": "6",
                "url": "https://example.invalid/6.json",
                "sha1": "dddddddddddddddddddddddddddddddddddddddd",
                "size": 4
              },
              "downloads": {
                "client": {
                  "url": "https://example.invalid/child-client.jar",
                  "sha1": "eeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee",
                  "size": 5
                }
              },
              "javaVersion": { "component": "java-runtime-delta", "majorVersion": 21 },
              "logging": { "client": { "argument": "-Dchild=true" } },
              "minimumLauncherVersion": 8,
              "complianceLevel": 1,
              "releaseTime": "2026-01-02T03:04:05Z"
            }
            """;

        var effective = Service.Merge([
            new VersionLayer("root", root, "test"),
            new VersionLayer("child", child, "test"),
        ]);

        Assert.Equal("child.Main", effective.Version.MainClass);
        Assert.Equal("6", effective.Version.Assets);
        Assert.Equal("6", effective.Version.AssetIndex?.Id);
        Assert.Equal(21, effective.Version.JavaVersion?.MajorVersion);
        Assert.Equal(21, effective.Version.MinimumLauncherVersion);
        Assert.Equal(2, effective.Version.ComplianceLevel);
        Assert.Equal("https://example.invalid/child-client.jar", effective.Version.Downloads.Client?.Url);
        Assert.Equal("-Dchild=true", effective.Version.Logging?.Argument);
        Assert.Equal("client-1.12.xml", effective.Version.Logging?.Client?.Id);
        Assert.Equal(DateTimeOffset.Parse("2026-01-02T03:04:05Z"), effective.Version.ReleaseTime);
    }

    [Fact]
    public void Merge_preserves_unknown_top_level_and_library_fields()
    {
        const string root = """
            {
              "id": "root",
              "customField": { "from": "root" },
              "rootOnly": [1, 2, 3],
              "libraries": [
                {
                  "name": "org.example:custom:1.0",
                  "mmc": { "client": true },
                  "unknownList": ["a", "b"]
                }
              ]
            }
            """;

        const string child = """
            {
              "id": "child",
              "inheritsFrom": "root",
              "customField": { "from": "child" }
            }
            """;

        var effective = Service.Merge([
            new VersionLayer("root", root, "test"),
            new VersionLayer("child", child, "test"),
        ]);

        using var document = JsonDocument.Parse(effective.Json);
        var merged = document.RootElement;
        Assert.Equal("child", merged.GetProperty("customField").GetProperty("from").GetString());
        Assert.Equal(3, merged.GetProperty("rootOnly").GetArrayLength());

        var library = Assert.Single(merged.GetProperty("libraries").EnumerateArray());
        Assert.True(library.GetProperty("mmc").GetProperty("client").GetBoolean());
        Assert.Equal(2, library.GetProperty("unknownList").GetArrayLength());
    }

    [Fact]
    public void Merge_writes_compact_json_that_round_trips_through_the_parser()
    {
        var effective = Service.Merge([Vanilla()]);
        var text = Encoding.UTF8.GetString(effective.Json);

        Assert.DoesNotContain("\n", text);
        Assert.DoesNotContain("\r", text);
        Assert.DoesNotContain(": ", text);

        using var document = JsonDocument.Parse(effective.Json);
        var parsed = MojangJson.ParseVersion(document.RootElement);

        Assert.Equal("1.20.1", parsed.Id);
        Assert.Null(parsed.InheritsFrom);
        Assert.Equal(
            ["com.google.guava:guava:32.1.2-jre", "org.ow2.asm:asm:9.6", "org.lwjgl:lwjgl:3.3.3:natives-windows"],
            parsed.Libraries.Select(library => library.Name));
    }

    [Fact]
    public async Task Resolver_accepts_eight_layers_and_rejects_nine()
    {
        var eight = BuildChain(8);
        var effective = await Service.MergeAsync("v7", Lookup(eight.ToArray()));
        Assert.Equal(Enumerable.Range(0, 8).Select(index => $"v{index}"), effective.Chain);

        var nine = BuildChain(9);
        var exception = await Assert.ThrowsAsync<VersionMergeException>(
            () => Service.MergeAsync("v8", Lookup(nine.ToArray())));
        Assert.Equal(VersionMergeErrorCode.ChainTooDeep, exception.Code);
    }

    [Fact]
    public async Task Resolver_rejects_cycles_self_references_and_missing_parents()
    {
        var cycle = new[]
        {
            new VersionLayer("a", """{"id":"a","inheritsFrom":"b"}""", "test"),
            new VersionLayer("b", """{"id":"b","inheritsFrom":"a"}""", "test"),
        };
        var cycleException = await Assert.ThrowsAsync<VersionMergeException>(
            () => Service.MergeAsync("a", Lookup(cycle)));
        Assert.Equal(VersionMergeErrorCode.ChainCycle, cycleException.Code);

        var self = new[]
        {
            new VersionLayer("a", """{"id":"a","inheritsFrom":"a"}""", "test"),
        };
        var selfException = await Assert.ThrowsAsync<VersionMergeException>(
            () => Service.MergeAsync("a", Lookup(self)));
        Assert.Equal(VersionMergeErrorCode.ChainSelfReference, selfException.Code);

        var missing = new[]
        {
            new VersionLayer("a", """{"id":"a","inheritsFrom":"gone"}""", "test"),
        };
        var missingException = await Assert.ThrowsAsync<VersionMergeException>(
            () => Service.MergeAsync("a", Lookup(missing)));
        Assert.Equal(VersionMergeErrorCode.ChainMissing, missingException.Code);
    }

    [Fact]
    public async Task Resolver_rejects_a_lookup_that_returns_the_wrong_id()
    {
        var wrong = new VersionLayer("other", """{"id":"other"}""", "test");

        var exception = await Assert.ThrowsAsync<VersionMergeException>(
            () => Service.MergeAsync(
                "a",
                (_, _) => Task.FromResult<VersionLayer?>(wrong)));
        Assert.Equal(VersionMergeErrorCode.ChainIdMismatch, exception.Code);
    }

    private static VersionLayer Vanilla()
        => new("1.20.1", VanillaJson, "test");

    private static VersionLayer Fabric()
        => new("fabric-loader-0.15.11-1.20.1", FabricJson, "test");

    private static Func<string, CancellationToken, Task<VersionLayer?>> Lookup(
        params VersionLayer[] layers)
    {
        var map = layers.ToDictionary(layer => layer.Id, StringComparer.Ordinal);
        return (id, _) => Task.FromResult(map.TryGetValue(id, out var layer) ? layer : null);
    }

    private static List<VersionLayer> BuildChain(int count)
    {
        var layers = new List<VersionLayer>(count);
        for (var index = 0; index < count; index++)
        {
            var json = index == 0
                ? $$"""{"id":"v{{index}}","type":"release"}"""
                : $$"""{"id":"v{{index}}","type":"release","inheritsFrom":"v{{index - 1}}"}""";
            layers.Add(new VersionLayer($"v{index}", json, "test"));
        }

        return layers;
    }
}
