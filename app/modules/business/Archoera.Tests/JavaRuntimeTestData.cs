using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Archoera.Application.JavaRuntime;
using Archoera.Domain.Java;
using Archoera.Domain.Mojang;

namespace Archoera.Tests;

/// <summary>
/// In-memory Java-runtime metadata. No test touches the network or writes a
/// runtime file.
/// </summary>
internal static class JavaRuntimeTestData
{
    public const string UserAgent = "ArchoeraCX/0.0.1+1 (tests@example.com)";

    public const string CatalogUrl =
        "https://launchermeta.mojang.com/v1/products/java-runtime/"
        + "2ec0cc96c44e5a76b9c8b7c39df7210883d12871/all.json";

    public const string ManifestUrl =
        "https://launchermeta.mojang.com/v1/products/java-runtime/manifest/linux-delta.json";

    public const string JavaUrl =
        "https://launchermeta.mojang.com/v1/products/java-runtime/manifest/bin/java";

    public const string JavaLzmaUrl =
        "https://launchermeta.mojang.com/v1/products/java-runtime/manifest/bin/java.lzma";

    public const string LibJliUrl =
        "https://launchermeta.mojang.com/v1/products/java-runtime/manifest/lib/libjli.so";

    public const string Released = "2025-01-01T00:00:00+00:00";

    public static readonly string Root = Path.Combine(
        Path.GetTempPath(),
        "archoera-java-runtime-tests");

    public static string Sha1(char character) => new(character, 40);

    public static string Sha1Of(string text)
        => Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(text)))
            .ToLowerInvariant();

    public static string HappyManifestJson()
    {
        var javaSha1 = Sha1('a');
        var lzmaSha1 = Sha1('c');
        var libSha1 = Sha1('b');

        return $$"""
            {
              "files": {
                "bin/java": {
                  "type": "file",
                  "executable": true,
                  "downloads": {
                    "raw": {
                      "url": "{{JavaUrl}}",
                      "sha1": "{{javaSha1}}",
                      "size": 123456
                    },
                    "lzma": {
                      "url": "{{JavaLzmaUrl}}",
                      "sha1": "{{lzmaSha1}}",
                      "size": 5000
                    }
                  }
                },
                "lib/libjli.so": {
                  "type": "file",
                  "downloads": {
                    "raw": {
                      "url": "{{LibJliUrl}}",
                      "sha1": "{{libSha1}}",
                      "size": 4567
                    }
                  }
                },
                "lib/modules": { "type": "directory" },
                "bin/readlink": { "type": "link" }
              }
            }
            """;
    }

    public static string LzmaOnlyManifestJson()
        => $$"""
            {
              "files": {
                "bin/java": {
                  "type": "file",
                  "executable": true,
                  "downloads": {
                    "lzma": {
                      "url": "{{JavaLzmaUrl}}",
                      "sha1": "{{Sha1('c')}}",
                      "size": 5000
                    }
                  }
                }
              }
            }
            """;

    public static string CatalogJson(
        string manifestJson,
        string platform = "linux",
        string component = "java-runtime-delta",
        string versionName = "21.0.7",
        string released = Released,
        string? availability = "available")
    {
        var availabilityLine = availability is null
            ? string.Empty
            : $",{Environment.NewLine}        \"availability\": \"{availability}\"";

        return $$"""
            {
              "gamecore": {
                "java-runtime-alpha": [],
                "java-runtime-delta": []
              },
              "{{platform}}": {
                "{{component}}": [
                  {
                    "manifest": {
                      "url": "{{ManifestUrl}}",
                      "sha1": "{{Sha1Of(manifestJson)}}",
                      "size": {{Encoding.UTF8.GetByteCount(manifestJson).ToString(CultureInfo.InvariantCulture)}}
                    },
                    "version": {
                      "name": "{{versionName}}",
                      "released": "{{released}}"
                    }{{availabilityLine}}
                  }
                ]
              }
            }
            """;
    }

    public static JavaRuntimeCatalog ParseCatalog(string json)
    {
        using var document = JsonDocument.Parse(json);
        return JavaRuntimeJson.ParseCatalog(document.RootElement);
    }

    public static JavaRuntimeManifest ParseManifest(string json)
    {
        using var document = JsonDocument.Parse(json);
        return JavaRuntimeJson.ParseManifest(document.RootElement);
    }

    public static JavaRuntimeCatalog Catalog(
        string platform = "linux",
        string component = "java-runtime-delta",
        string versionName = "21.0.7")
        => new(new Dictionary<string, IReadOnlyList<JavaRuntimeComponent>>(
            StringComparer.Ordinal)
        {
            [platform] =
            [
                new JavaRuntimeComponent(
                    PlatformKey: platform,
                    Name: component,
                    Availability: "available",
                    Manifest: new JavaRuntimeDownloadRef(ManifestUrl, Sha1('e'), 100),
                    Version: new JavaRuntimeVersion(
                        versionName,
                        DateTimeOffset.Parse(Released, CultureInfo.InvariantCulture))),
            ],
        });

    public static JavaRuntimeComponent ComponentFor(
        string manifestJson,
        string? sha1 = null)
        => Component(
            sha1 ?? Sha1Of(manifestJson),
            Encoding.UTF8.GetByteCount(manifestJson));

    public static JavaRuntimeComponent Component(string? sha1, long? size)
        => new(
            PlatformKey: "linux",
            Name: "java-runtime-delta",
            Availability: "available",
            Manifest: new JavaRuntimeDownloadRef(ManifestUrl, sha1, size),
            Version: new JavaRuntimeVersion(
                "21.0.7",
                DateTimeOffset.Parse(Released, CultureInfo.InvariantCulture)));
}
