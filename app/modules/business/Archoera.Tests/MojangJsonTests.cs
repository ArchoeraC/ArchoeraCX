using System.Text.Json;
using Archoera.Application.Mojang;
using Archoera.Domain.Mojang;

namespace Archoera.Tests;

public sealed class MojangJsonTests
{
    [Fact]
    public void ParseVersion_reads_libraries_rules_natives_arguments_and_logging()
    {
        const string json = """
            {
              "id": "1.12.2",
              "type": "release",
              "mainClass": "net.minecraft.client.main.Main",
              "assets": "1.12",
              "assetIndex": {
                "id": "1.12",
                "url": "https://piston-meta.mojang.com/v1/packages/index/1.12.json",
                "sha1": "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
                "size": 100,
                "totalSize": 200
              },
              "downloads": {
                "client": {
                  "url": "https://piston-data.mojang.com/v1/objects/client/client.jar",
                  "sha1": "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb",
                  "size": 300
                }
              },
              "javaVersion": { "component": "jre-legacy", "majorVersion": 8 },
              "logging": {
                "client": {
                  "argument": "-Dlog4j.configurationFile=${path}",
                  "file": {
                    "id": "client-1.12.xml",
                    "url": "https://piston-data.mojang.com/v1/objects/log/client-1.12.xml",
                    "sha1": "cccccccccccccccccccccccccccccccccccccccc",
                    "size": 50,
                    "type": "log4j2-xml"
                  }
                }
              },
              "libraries": [
                {
                  "name": "org.example:common:1.0",
                  "downloads": {
                    "artifact": {
                      "url": "https://libraries.minecraft.net/org/example/common/1.0/common-1.0.jar",
                      "sha1": "dddddddddddddddddddddddddddddddddddddddd",
                      "size": 10,
                      "path": "org/example/common/1.0/common-1.0.jar"
                    }
                  }
                },
                {
                  "name": "org.example:natives:1.0",
                  "natives": { "linux": "natives-linux-${arch}" },
                  "extract": { "exclude": ["META-INF/"] },
                  "rules": [
                    { "action": "allow", "os": { "name": "linux", "arch": "x86_64" } }
                  ],
                  "downloads": {
                    "classifiers": {
                      "natives-linux-64": {
                        "url": "https://libraries.minecraft.net/org/example/natives/1.0/natives-1.0-natives-linux-64.jar",
                        "sha1": "eeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee",
                        "size": 20,
                        "path": "org/example/natives/1.0/natives-1.0-natives-linux-64.jar"
                      }
                    }
                  }
                }
              ],
              "arguments": {
                "game": [
                  "--username",
                  {
                    "rules": [{ "action": "allow", "features": { "is_demo_user": true } }],
                    "value": ["--demo"]
                  }
                ],
                "jvm": ["-Xmx2G"]
              }
            }
            """;

        using var document = JsonDocument.Parse(json);
        var version = MojangJson.ParseVersion(document.RootElement);

        Assert.Equal("1.12.2", version.Id);
        Assert.Equal("1.12", version.AssetIndex?.Id);
        Assert.Equal(8, version.JavaVersion?.MajorVersion);
        Assert.Equal("client-1.12.xml", version.Logging?.Client?.Id);
        Assert.Equal(2, version.Libraries.Count);

        var natives = version.Libraries[1];
        Assert.Equal(1, natives.Rules?.Count);
        Assert.Equal("META-INF/", Assert.Single(natives.Extract!.Exclude));
        Assert.Equal("natives-linux-${arch}", natives.Natives!["linux"]);
        Assert.True(MojangRuleEvaluator.IsAllowed(natives.Rules, MojangPlatform.LinuxX64));

        var classifier = natives.Downloads!.ClassifierMap["natives-linux-64"];
        Assert.Equal(20, classifier.Size);

        Assert.Equal(2, version.Arguments!.Game.Count);
        Assert.Equal("--username", version.Arguments.Game[0].Values[0]);
        Assert.Equal("--demo", version.Arguments.Game[1].Values[0]);
        Assert.Single(version.Arguments.Game[1].Rules);
        Assert.Equal("-Xmx2G", Assert.Single(version.Arguments.Jvm).Values[0]);
    }

    [Fact]
    public void ParseVersion_keeps_inherits_from_for_the_caller_to_reject()
    {
        const string json = """
            {
              "id": "fabric-loader-0.15.11-1.20.1",
              "inheritsFrom": "1.20.1",
              "downloads": {}
            }
            """;

        using var document = JsonDocument.Parse(json);
        var version = MojangJson.ParseVersion(document.RootElement);

        Assert.Equal("1.20.1", version.InheritsFrom);
        Assert.Null(version.Downloads.Client);
        Assert.Empty(version.Libraries);
    }

    [Fact]
    public void ParseAssetIndex_reads_virtual_flags_and_objects()
    {
        const string json = """
            {
              "objects": {
                "icons/icon_16x16.png": {
                  "hash": "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
                  "size": 143
                }
              },
              "virtual": true,
              "map_to_resources": true
            }
            """;

        using var document = JsonDocument.Parse(json);
        var index = MojangJson.ParseAssetIndex(document.RootElement, "legacy");

        Assert.Equal("legacy", index.Id);
        Assert.True(index.Virtual);
        Assert.True(index.MapToResources);
        Assert.Equal(143, index.Objects["icons/icon_16x16.png"].Size);
    }

    [Fact]
    public void ParseAssetIndex_rejects_a_missing_objects_map()
    {
        using var document = JsonDocument.Parse("{}");

        var exception = Assert.Throws<MojangException>(
            () => MojangJson.ParseAssetIndex(document.RootElement, "5"));

        Assert.Equal(MojangErrorCode.InvalidResponse, exception.Code);
    }
}
