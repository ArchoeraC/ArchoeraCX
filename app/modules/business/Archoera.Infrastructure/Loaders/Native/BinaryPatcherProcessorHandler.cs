using System.IO.Compression;
using Archoera.Domain.Loaders;

namespace Archoera.Infrastructure.Loaders.Native;

/// <summary>
/// Applies Forge/NeoForge's binary patches: decompress the <c>.lzma</c> patch
/// set, verify every source class with Adler-32, apply the GDIFF delta and write
/// the patched-only jar the installer expects.
/// </summary>
internal sealed class BinaryPatcherProcessorHandler : INativeProcessorHandler
{
    private static readonly string[] MainClasses =
    [
        "net.minecraftforge.binarypatcher.ConsoleTool",
        "net.neoforged.binarypatcher.ConsoleTool",
    ];

    public bool CanRun(ProcessorStep step) => TryCreate(step, out _);
    public void Run(ProcessorStep step, string workingDir, CancellationToken cancellationToken)
    {
        if (!TryCreate(step, out var request))
        {
            throw new InvalidOperationException("processor is not a supported binarypatcher invocation");
        }

        Execute(request, workingDir, cancellationToken);
    }

    private static void Execute(
        PatchRequest request,
        string workingDir,
        CancellationToken cancellationToken)
    {
        var inputPath = NativeProcessorPaths.Resolve(request.InputPath, workingDir);
        var outputPath = NativeProcessorPaths.Resolve(request.OutputPath, workingDir);
        var patches = LoadPatches(request, workingDir, cancellationToken);
        var order = patches.Keys.ToList();
        if (request.SortedCreatedEntries)
        {
            order.Sort(StringComparer.Ordinal);
        }
        else
        {
            order = [.. JavaHashMap.Order(order)];
        }

        var outputDirectory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(outputDirectory))
        {
            Directory.CreateDirectory(outputDirectory);
        }

        var entries = new List<JarEntry>();
        var temporary = outputPath + ".native-" + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var input = ZipFile.OpenRead(inputPath))
            {
                var done = new HashSet<string>(StringComparer.Ordinal);
                foreach (var entry in input.Entries)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (entry.FullName.EndsWith("/", StringComparison.Ordinal))
                    {
                        if (request.KeepData)
                        {
                            entries.Add(new JarEntry(entry.FullName, []));
                        }

                        continue;
                    }

                    if (!entry.FullName.EndsWith(".class", StringComparison.Ordinal))
                    {
                        if (request.KeepData)
                        {
                            entries.Add(new JarEntry(entry.FullName, ReadEntry(entry)));
                        }

                        continue;
                    }

                    var key = entry.FullName[..^6];
                    if (!patches.TryGetValue(key, out var list))
                    {
                        if (request.IncludeUnpatched)
                        {
                            entries.Add(new JarEntry(entry.FullName, ReadEntry(entry)));
                        }

                        continue;
                    }

                    done.Add(key);
                    var data = ReadEntry(entry);
                    foreach (var patch in list)
                    {
                        data = ApplyOne(data, patch);
                    }

                    if (data.Length > 0)
                    {
                        entries.Add(new JarEntry(entry.FullName, data));
                    }
                }

                foreach (var key in order)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (done.Contains(key))
                    {
                        continue;
                    }

                    var data = Array.Empty<byte>();
                    foreach (var patch in patches[key])
                    {
                        data = ApplyOne(data, patch);
                    }

                    if (data.Length > 0)
                    {
                        entries.Add(new JarEntry(key + ".class", data));
                    }
                }
            }

            using (var stream = new FileStream(
                       temporary,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None))
            {
                JarWriter.Write(stream, entries);
            }

            File.Move(temporary, outputPath, overwrite: true);
        }
        catch
        {
            NativeProcessorPaths.TryDelete(temporary);
            throw;
        }
    }

    private static Dictionary<string, List<BinaryPatch>> LoadPatches(
        PatchRequest request,
        string workingDir,
        CancellationToken cancellationToken)
    {
        var patches = new Dictionary<string, List<BinaryPatch>>(StringComparer.Ordinal);
        for (var index = 0; index < request.Applies.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var prefix = request.Prefixes is null ? null : request.Prefixes[index];
            var path = NativeProcessorPaths.Resolve(request.Applies[index], workingDir);
            var payload = LzmaContainer.Decompress(File.ReadAllBytes(path));
            using var archive = new ZipArchive(new MemoryStream(payload), ZipArchiveMode.Read);
            foreach (var entry in archive.Entries)
            {
                if (!entry.FullName.EndsWith(".binpatch", StringComparison.Ordinal))
                {
                    continue;
                }

                if (prefix is not null &&
                    !entry.FullName.StartsWith(prefix + "/", StringComparison.Ordinal))
                {
                    continue;
                }

                var patch = BinaryPatch.Parse(ReadEntry(entry));
                if (!patches.TryGetValue(patch.ObfuscatedName, out var list))
                {
                    list = [];
                    patches[patch.ObfuscatedName] = list;
                }

                list.Add(patch);
            }
        }

        return patches;
    }

    private static byte[] ApplyOne(byte[] data, BinaryPatch patch)
    {
        if (patch.Exists && data.Length == 0)
        {
            throw new InvalidDataException(
                $"patch expected {patch.MappedName} to exist but received empty data");
        }

        if (!patch.Exists && data.Length > 0)
        {
            throw new InvalidDataException(
                $"patch expected {patch.MappedName} not to exist but received {data.Length} bytes");
        }

        var checksum = Adler32(data);
        if (checksum != patch.Checksum)
        {
            throw new InvalidDataException(
                $"patch expected {patch.MappedName} to have checksum {patch.Checksum:x8} " +
                $"but it was {checksum:x8}");
        }

        return patch.Delta.Length == 0 ? [] : GDiff.Apply(data, patch.Delta);
    }

    private static uint Adler32(byte[] data)
    {
        if (data.Length == 0)
        {
            return 0;
        }

        const uint Modulus = 65521;
        uint a = 1;
        uint b = 0;
        foreach (var value in data)
        {
            a = (a + value) % Modulus;
            b = (b + a) % Modulus;
        }

        return (b << 16) | a;
    }

    private static byte[] ReadEntry(ZipArchiveEntry entry)
    {
        using var stream = entry.Open();
        return NativeProcessorPaths.ReadAll(stream);
    }

    private static bool TryCreate(ProcessorStep step, out PatchRequest request)
    {
        request = null!;
        if (!MainClasses.Contains(step.MainClass, StringComparer.Ordinal))
        {
            return false;
        }

        string? input = null;
        string? output = null;
        var applies = new List<string>();
        var prefixes = new List<string>();
        var keepData = false;
        var includeUnpatched = false;

        for (var index = 0; index < step.Arguments.Count; index++)
        {
            switch (step.Arguments[index])
            {
                case "--clean":
                    if (!TryNext(step.Arguments, ref index, out input))
                    {
                        return false;
                    }

                    break;
                case "--output":
                    if (!TryNext(step.Arguments, ref index, out output))
                    {
                        return false;
                    }

                    break;
                case "--apply":
                    if (!TryNext(step.Arguments, ref index, out var apply))
                    {
                        return false;
                    }

                    applies.Add(apply!);
                    break;
                case "--prefix":
                    if (!TryNext(step.Arguments, ref index, out var prefix))
                    {
                        return false;
                    }

                    prefixes.Add(prefix!);
                    break;
                case "--data":
                    keepData = true;
                    break;
                case "--unpatched":
                    includeUnpatched = true;
                    break;
                default:
                    // --pack200/--legacy and the generator options need Java.
                    return false;
            }
        }

        if (string.IsNullOrWhiteSpace(input) ||
            string.IsNullOrWhiteSpace(output) ||
            applies.Count == 0)
        {
            return false;
        }

        if (prefixes.Count > 0 && prefixes.Count != applies.Count)
        {
            return false;
        }

        IReadOnlyList<string?>? resolvedPrefixes = null;
        if (prefixes.Count > 0)
        {
            var values = new string?[prefixes.Count];
            for (var index = 0; index < prefixes.Count; index++)
            {
                values[index] = prefixes[index] == "NULL" ? null : prefixes[index];
            }

            resolvedPrefixes = values;
        }

        request = new PatchRequest(
            input,
            output,
            applies,
            resolvedPrefixes,
            keepData,
            includeUnpatched,
            SortedCreatedEntries: SortsCreatedEntries(step));
        return true;
    }

    /// <summary>
    /// binarypatcher 1.2.0 and newer append created classes in name order;
    /// 1.1.x still walks a Java <c>HashMap</c>. Forge 1.21.1 declares
    /// <c>PATCHED_SHA</c> for its patched client jar, so the difference shows up
    /// as a hash mismatch rather than a missing class.
    /// </summary>
    /// <remarks>
    /// Verified against the official tools: 1.1.1 (Forge 1.20.1) uses hash
    /// order, 1.2.0 (Forge 1.21.1) and 2.1.2 (NeoForge 21.1.248) sort. The
    /// version comes from the jar coordinate; an unknown layout falls back to
    /// the NeoForge toolchain, which sorts.
    /// </remarks>
    private static bool SortsCreatedEntries(ProcessorStep step)
    {
        var parts = step.JarRelativePath
            .Replace('\\', '/')
            .Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 4 && Version.TryParse(parts[^2], out var version))
        {
            return version >= new Version(1, 2, 0);
        }

        return step.MainClass.StartsWith("net.neoforged", StringComparison.Ordinal);
    }

    private static bool TryNext(
        IReadOnlyList<string> arguments,
        ref int index,
        out string? value)
    {
        value = null;
        if (index + 1 >= arguments.Count)
        {
            return false;
        }

        value = arguments[++index];
        return !string.IsNullOrWhiteSpace(value);
    }

    private sealed record PatchRequest(
        string InputPath,
        string OutputPath,
        IReadOnlyList<string> Applies,
        IReadOnlyList<string?>? Prefixes,
        bool KeepData,
        bool IncludeUnpatched,
        bool SortedCreatedEntries);
}
