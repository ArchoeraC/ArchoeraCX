using System.IO.Compression;
using Archoera.Infrastructure.Loaders.Remap;

namespace Archoera.Tests;

/// <summary>
/// Reads Mojang/ART jars only when a local oracle directory is configured
/// (<c>ARCHOERACX_ART_ORACLE</c>). Nothing is downloaded or committed.
/// </summary>
public sealed class NativeArtOracleTests
{
    [Fact]
    public void Reference_jars_parse_when_oracle_is_configured()
    {
        var directory = Environment.GetEnvironmentVariable("ARCHOERACX_ART_ORACLE");
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            return;
        }

        var parsed = 0;
        foreach (var name in new[] { "client.jar", "MC_OFF.jar" })
        {
            var path = Path.Combine(directory, name);
            if (!File.Exists(path))
            {
                continue;
            }

            using var archive = ZipFile.OpenRead(path);
            foreach (var entry in archive.Entries)
            {
                if (!entry.FullName.EndsWith(".class", StringComparison.Ordinal))
                {
                    continue;
                }

                using var stream = entry.Open();
                using var memory = new MemoryStream();
                stream.CopyTo(memory);
                var model = ClassFileReader.Read(memory.ToArray());
                Assert.False(string.IsNullOrWhiteSpace(model.ClassName));
                parsed++;
            }
        }

        Assert.True(parsed > 8000, $"expected a full Minecraft jar, parsed {parsed} classes");
    }

    [Fact]
    public void Reference_jars_round_trip_when_oracle_is_configured()
    {
        var directory = Environment.GetEnvironmentVariable("ARCHOERACX_ART_ORACLE");
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            return;
        }

        var compared = 0;
        foreach (var name in new[] { "client.jar", "MC_OFF.jar" })
        {
            var path = Path.Combine(directory, name);
            if (!File.Exists(path))
            {
                continue;
            }

            using var archive = ZipFile.OpenRead(path);
            foreach (var entry in archive.Entries)
            {
                if (!entry.FullName.EndsWith(".class", StringComparison.Ordinal))
                {
                    continue;
                }

                using var stream = entry.Open();
                using var memory = new MemoryStream();
                stream.CopyTo(memory);
                var original = memory.ToArray();
                var model = ClassFileReader.Read(original);
                var rewritten = ClassFileWriter.WriteUnchanged(model);
                var mismatch = FirstDifference(original, rewritten);
                Assert.True(
                    mismatch < 0,
                    $"{name}/{entry.FullName} did not round-trip at byte {mismatch} " +
                    $"(original {original.Length}, rewritten {rewritten.Length})");
                compared++;
            }
        }

        Assert.True(compared > 8000, $"expected a full Minecraft jar, compared {compared} classes");
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
