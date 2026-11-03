using System.IO.Compression;
using System.Text.Json;
using Archoera.Domain.Loaders;

namespace Archoera.Infrastructure.Loaders.Native;

/// <summary>
/// Reproduces <c>installertools --task MCP_DATA</c> for single-entry keys: the
/// key resolves through <c>config.json</c>'s <c>data</c> object and the named
/// zip entry is copied byte for byte.
/// </summary>
internal sealed class McpDataProcessorHandler : INativeProcessorHandler
{
    private static readonly string[] MainClasses =
    [
        "net.minecraftforge.installertools.ConsoleTool",
        "net.neoforged.installertools.ConsoleTool",
    ];

    public bool CanRun(ProcessorStep step) => TryCreate(step, out _);

    public void Run(ProcessorStep step, string workingDir, CancellationToken cancellationToken)
    {
        if (!TryCreate(step, out var request))
        {
            throw new InvalidOperationException("processor is not a supported MCP_DATA invocation");
        }

        var inputPath = NativeProcessorPaths.Resolve(request.InputPath, workingDir);
        var outputPath = NativeProcessorPaths.Resolve(request.OutputPath, workingDir);
        using var archive = ZipFile.OpenRead(inputPath);
        var configEntry = archive.GetEntry("config.json")
            ?? throw new InvalidDataException("config.json is missing from the MCP data zip");
        using var configStream = configEntry.Open();
        using var config = JsonDocument.Parse(configStream);
        if (!config.RootElement.TryGetProperty("data", out var data))
        {
            throw new InvalidDataException("config.json has no data object");
        }

        var node = data;
        var parts = request.Key.Split('.');
        for (var index = 0; index < parts.Length - 1; index++)
        {
            if (!node.TryGetProperty(parts[index], out node) ||
                node.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidDataException($"config.json has no '{request.Key}' entry");
            }
        }

        if (!node.TryGetProperty(parts[^1], out var value) ||
            value.ValueKind != JsonValueKind.String)
        {
            throw new InvalidDataException($"config.json has no '{request.Key}' entry");
        }

        var entryName = value.GetString()!;
        if (entryName.EndsWith("/", StringComparison.Ordinal))
        {
            ExtractDirectory(archive, entryName, outputPath, cancellationToken);
            return;
        }

        var entry = archive.GetEntry(entryName)
            ?? throw new InvalidDataException($"MCP data entry '{entryName}' is missing");
        var directory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var temporary = outputPath + ".native-" + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var input = entry.Open())
            using (var output = new FileStream(
                       temporary,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None))
            {
                cancellationToken.ThrowIfCancellationRequested();
                input.CopyTo(output);
            }

            File.Move(temporary, outputPath, overwrite: true);
        }
        catch
        {
            NativeProcessorPaths.TryDelete(temporary);
            throw;
        }
    }

    private static void ExtractDirectory(
        ZipArchive archive,
        string prefix,
        string outputPath,
        CancellationToken cancellationToken)
    {
        // installertools deletes the destination first, so a stale directory
        // never leaks old files into the processor output.
        if (Directory.Exists(outputPath))
        {
            Directory.Delete(outputPath, recursive: true);
        }
        else if (File.Exists(outputPath))
        {
            File.Delete(outputPath);
        }

        Directory.CreateDirectory(outputPath);
        var root = Path.GetFullPath(outputPath) + Path.DirectorySeparatorChar;
        foreach (var entry in archive.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (entry.FullName.EndsWith("/", StringComparison.Ordinal) ||
                !entry.FullName.StartsWith(prefix, StringComparison.Ordinal))
            {
                continue;
            }

            var relative = entry.FullName[prefix.Length..];
            var destination = Path.GetFullPath(Path.Combine(outputPath, relative));
            if (!destination.StartsWith(root, StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    $"MCP data entry '{entry.FullName}' escapes the output directory");
            }

            var directory = Path.GetDirectoryName(destination);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            using var input = entry.Open();
            using var output = new FileStream(
                destination,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None);
            input.CopyTo(output);
        }
    }

    private static bool TryCreate(ProcessorStep step, out McpDataRequest request)
    {
        request = null!;
        if (!MainClasses.Contains(step.MainClass, StringComparer.Ordinal))
        {
            return false;
        }

        string? task = null;
        string? input = null;
        string? output = null;
        string? key = null;
        for (var index = 0; index < step.Arguments.Count; index++)
        {
            switch (step.Arguments[index])
            {
                case "--task":
                    if (!TryNext(step.Arguments, ref index, out task))
                    {
                        return false;
                    }

                    break;
                case "--input":
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
                case "--key":
                    if (!TryNext(step.Arguments, ref index, out key))
                    {
                        return false;
                    }

                    break;
                default:
                    return false;
            }
        }

        if (!string.Equals(task, "MCP_DATA", StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(input) ||
            string.IsNullOrWhiteSpace(output) ||
            string.IsNullOrWhiteSpace(key))
        {
            return false;
        }

        request = new McpDataRequest(input, output, key);
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

    private sealed record McpDataRequest(
        string InputPath,
        string OutputPath,
        string Key);
}
