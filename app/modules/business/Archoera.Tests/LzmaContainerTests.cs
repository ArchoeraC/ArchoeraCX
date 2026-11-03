using System.IO.Compression;
using Archoera.Infrastructure.Loaders.Native;

namespace Archoera.Tests;

public sealed class LzmaContainerTests
{
    [Theory]
    [InlineData("ARCHOERACX_LZMA_PATCH_1201")]
    [InlineData("ARCHOERACX_LZMA_PATCH_1211")]
    public void Decompresses_a_real_binarypatcher_payload(string variable)
    {
        var path = Environment.GetEnvironmentVariable(variable);
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return;
        }

        var payload = LzmaContainer.Decompress(File.ReadAllBytes(path));

        using var archive = new ZipArchive(new MemoryStream(payload), ZipArchiveMode.Read);
        var patches = archive.Entries
            .Where(entry => entry.FullName.EndsWith(".binpatch", StringComparison.Ordinal))
            .ToList();
        Assert.NotEmpty(patches);
    }
}
