using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using Archoera.Application.Loaders;
using Archoera.Domain.Loaders;
using Archoera.Infrastructure.Loaders;

namespace Archoera.DevHost;

/// <summary>
/// Synthetic Forge 1.20.1-style installer used by the DevHost self-test and by
/// the xUnit suite. It contains no Minecraft or Forge code: only hashes and
/// tiny placeholder files, so tests never need Java or the network.
/// </summary>
public sealed class SyntheticLoaderFixture
{
    public const string InstallerEntryClientLzma = "data/client.lzma";

    private readonly Dictionary<string, byte[]> _generatedByKey;
    private readonly Dictionary<int, byte[]> _generatedByProcessor;

    private SyntheticLoaderFixture(
        string baseDirectory,
        bool includePatchedOutput,
        bool includePatchedLibrary)
    {
        BaseDirectory = baseDirectory;
        Root = Path.Combine(baseDirectory, "instance");
        Libraries = Path.Combine(Root, "libraries");
        InstallerPath = Path.Combine(Root, "installer.jar");
        FakeJavaPath = Path.Combine(baseDirectory, "fake-java");

        Directory.CreateDirectory(Libraries);
        File.WriteAllText(FakeJavaPath, "not a real JVM; the fake processor runner ignores it");

        MappingsRelative = "net/minecraft/client/1.20.1/client-1.20.1-mappings.tsrg";
        PatchedRelative = "net/minecraftforge/forge/1.20.1-47.2.0/forge-1.20.1-47.2.0-client.jar";
        BinpatchRelative = "net/minecraftforge/forge/1.20.1-47.2.0/forge-1.20.1-47.2.0-clientdata.lzma";
        ShimRelative = "net/minecraftforge/forge/1.20.1-47.2.0/forge-1.20.1-47.2.0-shim.jar";

        var mappings = Encoding.UTF8.GetBytes("synthetic mappings for processor 0\n");
        var patched = Encoding.UTF8.GetBytes("synthetic patched client jar for processor 1\n");
        var clientLzma = Encoding.UTF8.GetBytes("synthetic lzma binpatch");
        var shim = Encoding.UTF8.GetBytes("synthetic shim jar");

        _generatedByKey = new Dictionary<string, byte[]>(StringComparer.Ordinal)
        {
            ["MAPPINGS"] = mappings,
            ["MC_SRG"] = Encoding.UTF8.GetBytes("synthetic srg input\n"),
            ["PATCHED"] = patched,
        };
        _generatedByProcessor = new Dictionary<int, byte[]>
        {
            [0] = mappings,
            [1] = patched,
        };

        var mappingsSha = Sha1(mappings);
        var patchedSha = Sha1(patched);
        var patchedSize = patched.Length;

        // Newer installers do not always declare PATCHED in processors[].outputs
        // and do not always list it in version.json.libraries. Keep both knobs so
        // tests can exercise the "unlisted launch artifact" case.
        var patchedOutputsJson = includePatchedOutput
            ? $"{{\"{{PATCHED}}\": \"{patchedSha}\"}}"
            : "{}";
        var patchedLibraryJson = includePatchedLibrary
            ? $$"""
                ,
                {
                  "name": "net.minecraftforge:forge:1.20.1-47.2.0:client",
                  "downloads": {
                    "artifact": {
                      "path": "{{PatchedRelative}}",
                      "url": "",
                      "sha1": "{{patchedSha}}",
                      "size": {{patchedSize}}
                    }
                  }
                }
                """
            : string.Empty;

        WriteProcessorJar(
            Path.Combine(Libraries, "com/example/installertools/1.0/installertools-1.0.jar"),
            "com.example.Installertools");
        WriteProcessorJar(
            Path.Combine(Libraries, "com/example/binarypatcher/1.0/binarypatcher-1.0.jar"),
            "com.example.BinaryPatcher");

        // Inputs normally supplied by the download stage. They are not part of
        // the manifest because repair must never download them.
        var srgPath = Path.Combine(Libraries, "net/minecraft/client/1.20.1/client-1.20.1-srg.jar");
        Directory.CreateDirectory(Path.GetDirectoryName(srgPath)!);
        File.WriteAllText(srgPath, "synthetic srg input");

        var profile = $$"""
            {
              "_comment_": ["synthetic fixture; no Forge code"],
              "spec": 1,
              "profile": "forge",
              "version": "1.20.1-forge-47.2.0",
              "minecraft": "1.20.1",
              "json": "/version.json",
              "data": {
                "MAPPINGS": { "client": "[net.minecraft:client:1.20.1:mappings@tsrg]" },
                "MC_SRG": { "client": "[net.minecraft:client:1.20.1:srg]" },
                "MC_UNPACKED": { "client": "[net.minecraft:client:1.20.1:unpacked]" },
                "BINPATCH": { "client": "/data/client.lzma" },
                "PATCHED": { "client": "[net.minecraftforge:forge:1.20.1-47.2.0:client]" },
                "PATCHED_SHA": { "client": "{{patchedSha}}" }
              },
              "libraries": [
                { "name": "com.example:installertools:1.0", "url": "https://example.invalid/installertools.jar" },
                { "name": "com.example:binarypatcher:1.0", "url": "https://example.invalid/binarypatcher.jar" }
              ],
              "processors": [
                {
                  "sides": ["client"],
                  "jar": "com.example:installertools:1.0",
                  "classpath": ["com.example:installertools:1.0"],
                  "args": ["--task", "MCP_DATA", "--input", "{MC_SRG}", "--output", "{MAPPINGS}"],
                  "outputs": { "{MAPPINGS}": "{{mappingsSha}}" }
                },
                {
                  "sides": ["client"],
                  "jar": "com.example:binarypatcher:1.0",
                  "classpath": ["com.example:binarypatcher:1.0"],
                  "args": ["--input", "{MC_SRG}", "--output", "{PATCHED}", "--patch", "{BINPATCH}"],
                  "outputs": {{patchedOutputsJson}}
                },
                {
                  "sides": ["server"],
                  "jar": "com.example:installertools:1.0",
                  "classpath": ["com.example:installertools:1.0"],
                  "args": ["--task", "SERVER_ONLY"],
                  "outputs": { "[net.minecraft:server:1.20.1:server]": "0000000000000000000000000000000000000000" }
                }
              ]
            }
            """;

        var version = $$"""
            {
              "id": "1.20.1-forge-47.2.0",
              "inheritsFrom": "1.20.1",
              "mainClass": "com.example.SyntheticMain",
              "libraries": [
                {
                  "name": "org.ow2.asm:asm:9.5",
                  "downloads": {
                    "artifact": {
                      "path": "org/ow2/asm/asm/9.5/asm-9.5.jar",
                      "url": "https://maven.example.invalid/asm-9.5.jar",
                      "sha1": "0000000000000000000000000000000000000000",
                      "size": 1
                    }
                  }
                }{{patchedLibraryJson}}
              ]
            }
            """;

        using var file = File.Create(InstallerPath);
        using var archive = new ZipArchive(file, ZipArchiveMode.Create);
        AddEntry(archive, "install_profile.json", Encoding.UTF8.GetBytes(profile));
        AddEntry(archive, "version.json", Encoding.UTF8.GetBytes(version));
        AddEntry(archive, InstallerEntryClientLzma, clientLzma);
        AddEntry(archive, "maven/" + ShimRelative, shim);
    }

    public string BaseDirectory { get; }

    public string Root { get; }

    public string Libraries { get; }

    public string InstallerPath { get; }

    public string FakeJavaPath { get; }

    public string MappingsRelative { get; }

    public string PatchedRelative { get; }

    public string BinpatchRelative { get; }

    public string ShimRelative { get; }

    public static SyntheticLoaderFixture Create(
        string baseDirectory,
        bool includePatchedOutput = true,
        bool includePatchedLibrary = true)
    {
        Directory.CreateDirectory(baseDirectory);
        return new SyntheticLoaderFixture(
            Path.GetFullPath(baseDirectory),
            includePatchedOutput,
            includePatchedLibrary);
    }

    public Task<LoaderArtifactManifest> PlanAsync(
        LoaderKind kind = LoaderKind.Forge,
        CancellationToken cancellationToken = default)
    {
        IForgeArtifactPlanner planner = new ForgeArtifactPlanner();
        return planner.ParseAsync(InstallerPath, Root, Libraries, kind, cancellationToken);
    }

    public string Resolve(LoaderArtifact artifact)
    {
        var baseDirectory = artifact.Location == LoaderArtifactLocation.Libraries
            ? Libraries
            : Root;
        return Path.Combine(baseDirectory, artifact.RelativePath.Replace('/', Path.DirectorySeparatorChar));
    }

    public LoaderArtifact Find(LoaderArtifactManifest manifest, string key)
        => manifest.Artifacts.Single(artifact => artifact.Key == key);

    public void WriteExtracted(LoaderArtifactManifest manifest)
    {
        using var archive = ZipFile.OpenRead(InstallerPath);
        const string prefix = "installer:";

        foreach (var artifact in manifest.Artifacts.Where(
                     artifact => artifact.Kind == LoaderArtifactKind.Extracted))
        {
            if (!artifact.Key.StartsWith(prefix, StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"unexpected extracted key '{artifact.Key}'");
            }

            var entryName = artifact.Key[prefix.Length..];
            var entry = archive.Entries.FirstOrDefault(
                candidate => string.Equals(candidate.FullName, entryName, StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidOperationException($"installer entry '{entryName}' not found");

            var target = Resolve(artifact);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            entry.ExtractToFile(target, overwrite: true);
        }
    }

    public void WriteGenerated(LoaderArtifactManifest manifest, params string[] keys)
    {
        var wanted = keys.Length == 0 ? null : new HashSet<string>(keys, StringComparer.Ordinal);

        foreach (var artifact in manifest.Artifacts.Where(
                     artifact => artifact.Kind == LoaderArtifactKind.Generated))
        {
            if (wanted is not null && !wanted.Contains(artifact.Key))
            {
                continue;
            }

            if (!_generatedByKey.TryGetValue(artifact.Key, out var bytes))
            {
                throw new InvalidOperationException($"no synthetic content for generated key '{artifact.Key}'");
            }

            var target = Resolve(artifact);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.WriteAllBytes(target, bytes);
        }
    }

    public void WriteAllValid(LoaderArtifactManifest manifest)
    {
        WriteExtracted(manifest);
        WriteGenerated(manifest);
    }

    public byte[] OutputBytesForProcessor(int index)
        => _generatedByProcessor[index];

    private static void AddEntry(ZipArchive archive, string name, byte[] bytes)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.Fastest);
        using var stream = entry.Open();
        stream.Write(bytes);
    }

    private static void WriteProcessorJar(string path, string mainClass)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var file = File.Create(path);
        using var archive = new ZipArchive(file, ZipArchiveMode.Create);
        var entry = archive.CreateEntry("META-INF/MANIFEST.MF");
        using var stream = entry.Open();
        using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        writer.Write("Manifest-Version: 1.0\r\n");
        writer.Write($"Main-Class: {mainClass}\r\n");
        writer.Write("\r\n");
        writer.Flush();
    }

    private static string Sha1(byte[] bytes)
        => Convert.ToHexString(SHA1.HashData(bytes)).ToLowerInvariant();
}

/// <summary>Processor runner that writes the expected synthetic output without Java.</summary>
public sealed class FakeProcessorRunner : IProcessorRunner
{
    private readonly SyntheticLoaderFixture _fixture;

    public FakeProcessorRunner(SyntheticLoaderFixture fixture)
    {
        _fixture = fixture;
    }

    public List<int> Indices { get; } = [];

    public int FailOnIndex { get; set; } = -1;

    public bool ThrowOnRun { get; set; }

    public Task<ProcessorRunResult> RunAsync(
        ProcessorStep step,
        string javaPath,
        string workingDir,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Indices.Add(step.Index);

        if (ThrowOnRun)
        {
            throw new InvalidOperationException("synthetic runner failure");
        }

        if (step.Index == FailOnIndex)
        {
            return Task.FromResult(new ProcessorRunResult(7, string.Empty, "synthetic failure", false));
        }

        var bytes = _fixture.OutputBytesForProcessor(step.Index);
        foreach (var output in step.Outputs)
        {
            var directory = Path.GetDirectoryName(output.RelativePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllBytes(output.RelativePath, bytes);
        }

        return Task.FromResult(new ProcessorRunResult(0, "synthetic ok", string.Empty, false));
    }
}
