using System.IO.Compression;
using Archoera.Infrastructure.Loaders.Remap;

namespace Archoera.Tests;

/// <summary>
/// Byte-level oracle for the ART 0.1.22 rename pass. The fixture is generated
/// locally from official client files and is never committed.
/// </summary>
public sealed class NativeArtRemapOracleTests
{
    [Fact]
    public void Rename_pass_matches_art_0122_for_tick_priority()
    {
        var directory = Environment.GetEnvironmentVariable("ARCHOERACX_ART_ORACLE_1201");
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            return;
        }

        var mapping = TsrgMapping.ParseFile(Path.Combine(directory, "MERGED.tsrg"));
        var hierarchy = ClassHierarchy.FromJar(Path.Combine(directory, "MC_SLIM.jar"));
        const string target = "net/minecraft/world/ticks/TickPriority";

        var sourceName = mapping.Classes
            .FirstOrDefault(pair => pair.Value == target)
            .Key;
        Assert.False(string.IsNullOrEmpty(sourceName));

        using var sourceArchive = ZipFile.OpenRead(Path.Combine(directory, "MC_SLIM.jar"));
        using var referenceArchive = ZipFile.OpenRead(Path.Combine(directory, "RENAME_ONLY.jar"));

        var sourceBytes = ReadEntry(sourceArchive, sourceName + ".class");
        var referenceBytes = ReadEntry(referenceArchive, target + ".class");

        var model = ClassFileReader.Read(sourceBytes);
        var actual = new ClassFileRemapper(model, mapping, ArtProfile.Forge0122, hierarchy, ArtFixers.None).Rewrite();

        Assert.Equal(referenceBytes.Length, actual.Length);
        Assert.True(
            referenceBytes.AsSpan().SequenceEqual(actual),
            $"first difference at {FirstDifference(referenceBytes, actual)}");
    }

    [Fact]
    public void Rebuild_pass_matches_art_203_for_sample_classes()
    {
        var directory = Environment.GetEnvironmentVariable("ARCHOERACX_ART_ORACLE_1211");
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            return;
        }

        var samples = new[]
        {
            "net/minecraft/world/ticks/TickPriority",
            "net/minecraft/client/Minecraft",
            "com/mojang/blaze3d/Blaze3D",
        };
        var (compared, failures) = CompareJar(directory, samples);
        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
        Assert.Equal(samples.Length, compared);
    }

    [Fact]
    public void Rebuild_pass_matches_art_203_for_all_classes()
    {
        var directory = Environment.GetEnvironmentVariable("ARCHOERACX_ART_ORACLE_1211");
        if (string.IsNullOrWhiteSpace(directory) ||
            !Directory.Exists(directory) ||
            Environment.GetEnvironmentVariable("ARCHOERACX_ART_ORACLE_1211_FULL") != "1")
        {
            return;
        }

        var (compared, failures) = CompareJar(directory, samples: null);
        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
        Assert.True(compared > 8000, $"expected a full jar, compared {compared} classes");
    }

    [Fact]
    public void Fixer_pipeline_matches_art_0122_for_all_classes()
    {
        var directory = Environment.GetEnvironmentVariable("ARCHOERACX_ART_ORACLE_1201");
        if (string.IsNullOrWhiteSpace(directory) ||
            !Directory.Exists(directory) ||
            Environment.GetEnvironmentVariable("ARCHOERACX_ART_ORACLE_1201_FULL") != "1")
        {
            return;
        }

        var failures = CompareFixerPipeline(
            directory,
            "MC_SLIM.jar",
            "MERGED.tsrg",
            "RENAME_ONLY_FIXED.jar",
            ArtProfile.Forge0122,
            ArtFixers.All);
        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public void Fixer_pipeline_matches_art_203_for_all_classes()
    {
        var directory = Environment.GetEnvironmentVariable("ARCHOERACX_ART_ORACLE_1211");
        if (string.IsNullOrWhiteSpace(directory) ||
            !Directory.Exists(directory) ||
            Environment.GetEnvironmentVariable("ARCHOERACX_ART_ORACLE_1211_FULL") != "1")
        {
            return;
        }

        var failures = CompareFixerPipeline(
            directory,
            "client.jar",
            "MAPPINGS.tsrg",
            "oracle-fixed.jar",
            ArtProfile.NeoForge203,
            ArtFixers.All);
        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    private static List<string> CompareFixerPipeline(
        string directory,
        string inputName,
        string mappingName,
        string expectedName,
        ArtProfile profile,
        ArtFixers fixers)
    {
        var mapping = TsrgMapping.ParseFile(Path.Combine(directory, mappingName));
        var hierarchy = ClassHierarchy.FromJar(Path.Combine(directory, inputName));
        var reverse = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in mapping.Classes)
        {
            reverse[pair.Value] = pair.Key;
        }

        using var sourceArchive = ZipFile.OpenRead(Path.Combine(directory, inputName));
        using var expectedArchive = ZipFile.OpenRead(Path.Combine(directory, expectedName));

        var failures = new List<string>();
        foreach (var entry in expectedArchive.Entries)
        {
            if (!entry.FullName.EndsWith(".class", StringComparison.Ordinal))
            {
                continue;
            }

            var target = entry.FullName[..^6];
            if (!reverse.TryGetValue(target, out var source))
            {
                continue;
            }

            var sourceEntry = sourceArchive.GetEntry(source + ".class");
            if (sourceEntry is null)
            {
                failures.Add($"{target}: source {source} missing");
                continue;
            }

            var expectedBytes = ReadEntry(expectedArchive, entry.FullName);
            try
            {
                var actual = ArtRemapper.Rewrite(
                    ReadEntry(sourceArchive, sourceEntry.FullName),
                    mapping,
                    hierarchy,
                    profile,
                    fixers);
                if (!expectedBytes.AsSpan().SequenceEqual(actual))
                {
                    var safe = target.Replace('/', '_');
                    File.WriteAllBytes(Path.Combine(directory, $"fail-actual-{safe}.class"), actual);
                    File.WriteAllBytes(Path.Combine(directory, $"fail-expected-{safe}.class"), expectedBytes);
                    failures.Add($"{target}: first difference at {FirstDifference(expectedBytes, actual)}");
                }
            }
            catch (Exception exception)
            {
                failures.Add($"{target}: {exception.GetType().Name}: {exception.Message}");
            }

            if (failures.Count >= 8)
            {
                break;
            }
        }

        return failures;
    }

    private static (int Compared, List<string> Failures) CompareJar(
        string directory,
        IReadOnlyList<string>? samples)
    {
        var mapping = TsrgMapping.ParseFile(Path.Combine(directory, "MAPPINGS.tsrg"));
        var hierarchy = ClassHierarchy.FromJar(Path.Combine(directory, "client.jar"));
        var reverse = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in mapping.Classes)
        {
            reverse[pair.Value] = pair.Key;
        }

        var wanted = samples is null
            ? null
            : new HashSet<string>(samples, StringComparer.Ordinal);

        using var sourceArchive = ZipFile.OpenRead(Path.Combine(directory, "client.jar"));
        using var referenceArchive = ZipFile.OpenRead(Path.Combine(directory, "oracle.jar"));

        var failures = new List<string>();
        var compared = 0;
        foreach (var referenceEntry in referenceArchive.Entries)
        {
            if (!referenceEntry.FullName.EndsWith(".class", StringComparison.Ordinal))
            {
                continue;
            }

            var target = referenceEntry.FullName[..^6];
            if (wanted is not null && !wanted.Contains(target))
            {
                continue;
            }

            if (!reverse.TryGetValue(target, out var source))
            {
                continue;
            }

            var sourceEntry = sourceArchive.GetEntry(source + ".class");
            if (sourceEntry is null)
            {
                failures.Add($"{target}: source {source} missing");
                continue;
            }

            var referenceBytes = ReadEntry(referenceArchive, referenceEntry.FullName);
            try
            {
                var model = ClassFileReader.Read(ReadEntry(sourceArchive, sourceEntry.FullName));
                var actual = new ClassFileRemapper(
                    model,
                    mapping,
                    ArtProfile.NeoForge203,
                    hierarchy,
                    ArtFixers.None).Rewrite();
                compared++;
                if (!referenceBytes.AsSpan().SequenceEqual(actual))
                {
                    var safe = target.Replace('/', '_');
                    File.WriteAllBytes(Path.Combine(directory, $"rebuild-actual-{safe}.class"), actual);
                    File.WriteAllBytes(Path.Combine(directory, $"rebuild-reference-{safe}.class"), referenceBytes);
                    failures.Add($"{target}: first difference at {FirstDifference(referenceBytes, actual)}");
                }
            }
            catch (Exception exception)
            {
                var frames = string.Join(
                    " <- ",
                    (exception.StackTrace ?? string.Empty)
                        .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                        .Take(6)
                        .Select(line => line.Trim()));
                failures.Add($"{target}: {exception.GetType().Name}: {exception.Message} @ {frames}");
            }

            if (failures.Count >= 8)
            {
                break;
            }
        }

        return (compared, failures);
    }

    [Fact]
    public void Rename_pass_matches_art_0122_for_all_classes()
    {
        var directory = Environment.GetEnvironmentVariable("ARCHOERACX_ART_ORACLE_1201");
        if (string.IsNullOrWhiteSpace(directory) ||
            !Directory.Exists(directory) ||
            Environment.GetEnvironmentVariable("ARCHOERACX_ART_ORACLE_1201_FULL") != "1")
        {
            return;
        }

        var mapping = TsrgMapping.ParseFile(Path.Combine(directory, "MERGED.tsrg"));
        var hierarchy = ClassHierarchy.FromJar(Path.Combine(directory, "MC_SLIM.jar"));
        var reverse = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in mapping.Classes)
        {
            reverse[pair.Value] = pair.Key;
        }

        using var sourceArchive = ZipFile.OpenRead(Path.Combine(directory, "MC_SLIM.jar"));
        using var referenceArchive = ZipFile.OpenRead(Path.Combine(directory, "RENAME_ONLY.jar"));

        var failures = new List<string>();
        var compared = 0;
        foreach (var referenceEntry in referenceArchive.Entries)
        {
            if (!referenceEntry.FullName.EndsWith(".class", StringComparison.Ordinal))
            {
                continue;
            }

            var target = referenceEntry.FullName[..^6];
            if (!reverse.TryGetValue(target, out var source))
            {
                continue;
            }

            var sourceEntry = sourceArchive.GetEntry(source + ".class");
            if (sourceEntry is null)
            {
                failures.Add($"{target}: source {source} missing");
                continue;
            }

            var referenceBytes = ReadEntry(referenceArchive, referenceEntry.FullName);
            try
            {
                var model = ClassFileReader.Read(ReadEntry(sourceArchive, sourceEntry.FullName));
                var actual = new ClassFileRemapper(model, mapping, ArtProfile.Forge0122, hierarchy, ArtFixers.None).Rewrite();
                compared++;
                if (!referenceBytes.AsSpan().SequenceEqual(actual))
                {
                    var safe = target.Replace('/', '_');
                    File.WriteAllBytes(Path.Combine(directory, $"art-actual-{safe}.class"), actual);
                    File.WriteAllBytes(Path.Combine(directory, $"art-reference-{safe}.class"), referenceBytes);
                    failures.Add($"{target}: first difference at {FirstDifference(referenceBytes, actual)}");
                }
            }
            catch (Exception exception)
            {
                var frames = string.Join(
                    " <- ",
                    (exception.StackTrace ?? string.Empty)
                        .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                        .Take(6)
                        .Select(line => line.Trim()));
                failures.Add($"{target}: {exception.GetType().Name}: {exception.Message} @ {frames}");
            }

            if (failures.Count >= 8)
            {
                break;
            }
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
        Assert.True(compared > 7000, $"expected a full jar, compared {compared} classes");
    }

    [Fact]
    public void Mapping_contains_inherited_lookup_example()
    {
        var directory = Environment.GetEnvironmentVariable("ARCHOERACX_ART_ORACLE_1201");
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            return;
        }

        var mapping = TsrgMapping.ParseFile(Path.Combine(directory, "MERGED.tsrg"));
        var hierarchy = ClassHierarchy.FromJar(Path.Combine(directory, "MC_SLIM.jar"));
        var ancestors = hierarchy.MethodAncestors("eie").ToArray();
        Assert.Contains("eig", ancestors);
        Assert.True(mapping.TryMapMethod("eig", "a", "(DDD)Lein;", out var mapped));
        Assert.Equal("m_5483_", mapped);
    }

    private static byte[] ReadEntry(ZipArchive archive, string name)
    {
        var entry = archive.GetEntry(name)
                    ?? throw new InvalidOperationException($"{name} is missing");
        using var stream = entry.Open();
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return memory.ToArray();
    }

    private static int FirstDifference(byte[] left, byte[] right)
    {
        var length = Math.Min(left.Length, right.Length);
        for (var index = 0; index < length; index++)
        {
            if (left[index] != right[index])
            {
                return index;
            }
        }

        return left.Length == right.Length ? -1 : length;
    }
}
