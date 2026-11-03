using System.IO.Compression;
using System.Text.RegularExpressions;
using Archoera.Domain.Loaders;
using Archoera.Infrastructure.Loaders.Remap;

namespace Archoera.Infrastructure.Loaders.Native;

/// <summary>
/// Reproduces Forge/NeoForge's AutoRenamingTool step: rename with the installer
/// mapping, run the four fixer passes, and write the mapped class names back as
/// jar entries.
/// </summary>
internal sealed class ArtProcessorHandler : INativeProcessorHandler
{
    private static readonly string[] ArtMainClasses =
    [
        "net.minecraftforge.fart.Main",
        "net.neoforged.art.Main",
    ];

    public bool CanRun(ProcessorStep step) => TryCreate(step, out _);

    public void Run(ProcessorStep step, string workingDir, CancellationToken cancellationToken)
    {
        if (!TryCreate(step, out var request))
        {
            throw new InvalidOperationException("processor is not a supported ART invocation");
        }

        Execute(request, workingDir, cancellationToken);
    }

    private static void Execute(
        ArtRequest request,
        string workingDir,
        CancellationToken cancellationToken)
    {
        var inputPath = NativeProcessorPaths.Resolve(request.InputPath, workingDir);
        var outputPath = NativeProcessorPaths.Resolve(request.OutputPath, workingDir);
        var mappingPath = NativeProcessorPaths.Resolve(request.MappingPath, workingDir);
        var mapping = TsrgMapping.ParseFile(mappingPath);
        var hierarchy = ClassHierarchy.FromJar(inputPath);
        var outputDirectory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(outputDirectory))
        {
            Directory.CreateDirectory(outputDirectory);
        }

        var temporary = outputPath + ".native-" + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            var entries = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            using (var source = ZipFile.OpenRead(inputPath))
            {
                foreach (var entry in source.Entries)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (entry.FullName.EndsWith("/", StringComparison.Ordinal))
                    {
                        // ART synthesizes directory entries from the files it
                        // writes, so input directories are not copied as-is.
                        continue;
                    }

                    var name = entry.FullName;
                    var isClass = name.EndsWith(".class", StringComparison.Ordinal);
                    if (isClass && mapping.TryMapClass(name[..^6], out var mappedName))
                    {
                        // ART writes the class under its mapped internal name.
                        name = mappedName + ".class";
                    }

                    using var input = entry.Open();
                    var data = NativeProcessorPaths.ReadAll(input);
                    if (isClass)
                    {
                        data = ArtRemapper.Rewrite(
                            data,
                            mapping,
                            hierarchy,
                            request.Profile,
                            request.Fixers);
                    }

                    entries[name] = data;
                }
            }

            // ART writes a name-sorted jar (including one entry per parent
            // directory). binarypatcher copies its input order into the
            // patched client jar, and Forge's profile hashes that file, so the
            // order is part of the install contract.
            var names = new SortedSet<string>(entries.Keys, StringComparer.Ordinal);
            foreach (var name in entries.Keys)
            {
                var slash = name.LastIndexOf('/');
                while (slash > 0)
                {
                    names.Add(name[..(slash + 1)]);
                    slash = name.LastIndexOf('/', slash - 1);
                }
            }

            using (var stream = new FileStream(
                       temporary,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None))
            using (var target = new ZipArchive(stream, ZipArchiveMode.Create))
            {
                foreach (var name in names)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (name.EndsWith("/", StringComparison.Ordinal))
                    {
                        target.CreateEntry(name);
                        continue;
                    }

                    var destination = target.CreateEntry(name, CompressionLevel.Optimal);
                    using var output = destination.Open();
                    output.Write(entries[name]);
                }
            }

            File.Move(temporary, outputPath, overwrite: true);
        }
        catch
        {
            NativeProcessorPaths.TryDelete(temporary);
            throw;
        }
    }

    private static bool TryCreate(ProcessorStep step, out ArtRequest request)
    {
        request = null!;
        if (!ArtMainClasses.Contains(step.MainClass, StringComparer.Ordinal))
        {
            return false;
        }

        if (!TryResolveProfile(step.JarRelativePath, out var profile))
        {
            return false;
        }

        string? input = null;
        string? output = null;
        string? mapping = null;
        var fixers = ArtFixers.None;

        for (var index = 0; index < step.Arguments.Count; index++)
        {
            var argument = step.Arguments[index];
            switch (argument)
            {
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
                case "--names":
                case "--map":
                    if (!TryNext(step.Arguments, ref index, out mapping))
                    {
                        return false;
                    }

                    break;
                case "--lib":
                case "--threads":
                case "--log":
                    if (!TryNext(step.Arguments, ref index, out _))
                    {
                        return false;
                    }

                    break;
                case "--ann-fix":
                    fixers |= ArtFixers.Annotations;
                    break;
                case "--ids-fix":
                    fixers |= ArtFixers.Identifiers;
                    break;
                case "--src-fix":
                    fixers |= ArtFixers.SourceFile;
                    break;
                case "--record-fix":
                    fixers |= ArtFixers.Record;
                    break;
                default:
                    // --cfg, --reverse, --strip-sigs, --unfinal-params and any
                    // future option need the Java implementation.
                    return false;
            }
        }

        if (string.IsNullOrWhiteSpace(input) ||
            string.IsNullOrWhiteSpace(output) ||
            string.IsNullOrWhiteSpace(mapping))
        {
            return false;
        }

        request = new ArtRequest(input, output, mapping, profile, fixers);
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

    private static bool TryResolveProfile(string jarPath, out ArtProfile profile)
    {
        profile = null!;
        var candidates = new List<string>();
        var fileName = Path.GetFileName(jarPath);
        var match = Regex.Match(
            fileName,
            @"-(\d+\.\d+\.\d+)(?:-|\.jar$)",
            RegexOptions.CultureInvariant);
        if (match.Success)
        {
            candidates.Add(match.Groups[1].Value);
        }

        var directory = Path.GetDirectoryName(jarPath);
        if (!string.IsNullOrEmpty(directory))
        {
            candidates.Add(Path.GetFileName(directory));
        }

        foreach (var candidate in candidates)
        {
            if (ArtProfile.TryResolve(candidate, out profile))
            {
                return true;
            }
        }

        return false;
    }

    private sealed record ArtRequest(
        string InputPath,
        string OutputPath,
        string MappingPath,
        ArtProfile Profile,
        ArtFixers Fixers);
}
