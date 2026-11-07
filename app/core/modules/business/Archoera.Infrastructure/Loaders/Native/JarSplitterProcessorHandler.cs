using System.IO.Compression;
using Archoera.Domain.Loaders;
using Archoera.Infrastructure.Loaders.Remap;

namespace Archoera.Infrastructure.Loaders.Native;

/// <summary>
/// Splits a client jar into the slim jar (classes present in the installer
/// mapping) and the extra jar (everything else). The profile hashes both
/// outputs, so <see cref="JarWriter"/> reproduces the installer's ZIP layout.
/// </summary>
internal sealed class JarSplitterProcessorHandler : INativeProcessorHandler
{
    private static readonly string[] MainClasses =
    [
        "net.minecraftforge.jarsplitter.ConsoleTool",
        "net.neoforged.jarsplitter.ConsoleTool",
    ];

    public bool CanRun(ProcessorStep step) => TryCreate(step, out _);

    public void Run(ProcessorStep step, string workingDir, CancellationToken cancellationToken)
    {
        if (!TryCreate(step, out var request))
        {
            throw new InvalidOperationException("processor is not a supported jarsplitter invocation");
        }

        Execute(request, workingDir, cancellationToken);
    }

    private static void Execute(
        SplitRequest request,
        string workingDir,
        CancellationToken cancellationToken)
    {
        var inputPath = NativeProcessorPaths.Resolve(request.InputPath, workingDir);
        var whitelist = new HashSet<string>(StringComparer.Ordinal);
        foreach (var mappingPath in request.Mappings)
        {
            var mapping = TsrgMapping.ParseFile(
                NativeProcessorPaths.Resolve(mappingPath, workingDir));
            foreach (var name in mapping.Classes.Keys)
            {
                whitelist.Add(name);
            }
        }

        var slim = new List<JarEntry>();
        var extra = new List<JarEntry>();
        var data = request.DataPath is null ? null : new List<JarEntry>();
        using (var input = ZipFile.OpenRead(inputPath))
        {
            foreach (var entry in input.Entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var bytes = ReadEntry(entry);
                if (entry.FullName.EndsWith(".class", StringComparison.Ordinal))
                {
                    var key = entry.FullName[..^6];
                    if (whitelist.Count == 0 || whitelist.Contains(key))
                    {
                        slim.Add(new JarEntry(entry.FullName, bytes));
                    }
                    else
                    {
                        extra.Add(new JarEntry(entry.FullName, bytes));
                    }
                }
                else if (data is not null)
                {
                    data.Add(new JarEntry(entry.FullName, bytes));
                }
                else
                {
                    extra.Add(new JarEntry(entry.FullName, bytes));
                }
            }
        }

        Write(request.SlimPath, slim, workingDir);
        if (request.DataPath is not null)
        {
            Write(request.DataPath, data!, workingDir);
        }

        if (request.ExtraPath is not null)
        {
            Write(request.ExtraPath, extra, workingDir);
        }
    }

    private static void Write(string path, List<JarEntry> entries, string workingDir)
    {
        var outputPath = NativeProcessorPaths.Resolve(path, workingDir);
        var directory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var temporary = outputPath + ".native-" + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
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

    private static byte[] ReadEntry(ZipArchiveEntry entry)
    {
        using var stream = entry.Open();
        return NativeProcessorPaths.ReadAll(stream);
    }

    private static bool TryCreate(ProcessorStep step, out SplitRequest request)
    {
        request = null!;
        if (!MainClasses.Contains(step.MainClass, StringComparer.Ordinal))
        {
            return false;
        }

        string? input = null;
        string? slim = null;
        string? data = null;
        string? extra = null;
        var mappings = new List<string>();

        for (var index = 0; index < step.Arguments.Count; index++)
        {
            switch (step.Arguments[index])
            {
                case "--input":
                    if (!TryNext(step.Arguments, ref index, out input))
                    {
                        return false;
                    }

                    break;
                case "--slim":
                    if (!TryNext(step.Arguments, ref index, out slim))
                    {
                        return false;
                    }

                    break;
                case "--data":
                    if (!TryNext(step.Arguments, ref index, out data))
                    {
                        return false;
                    }

                    break;
                case "--extra":
                    if (!TryNext(step.Arguments, ref index, out extra))
                    {
                        return false;
                    }

                    break;
                case "--srg":
                    if (!TryNext(step.Arguments, ref index, out var mapping))
                    {
                        return false;
                    }

                    mappings.Add(mapping!);
                    break;
                default:
                    return false;
            }
        }

        if (string.IsNullOrWhiteSpace(input) || string.IsNullOrWhiteSpace(slim))
        {
            return false;
        }

        if (data is null && extra is null)
        {
            return false;
        }

        if (extra is not null && mappings.Count == 0)
        {
            return false;
        }

        request = new SplitRequest(input, slim, data, extra, mappings);
        return true;
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

    private sealed record SplitRequest(
        string InputPath,
        string SlimPath,
        string? DataPath,
        string? ExtraPath,
        IReadOnlyList<string> Mappings);
}
