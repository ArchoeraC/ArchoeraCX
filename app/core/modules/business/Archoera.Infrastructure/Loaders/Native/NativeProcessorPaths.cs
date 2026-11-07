namespace Archoera.Infrastructure.Loaders.Native;

/// <summary>Small path and stream helpers shared by the native processors.</summary>
internal static class NativeProcessorPaths
{
    public static string Resolve(string path, string workingDir)
        => Path.IsPathRooted(path)
            ? Path.GetFullPath(path)
            : Path.GetFullPath(Path.Combine(workingDir, path));

    public static byte[] ReadAll(Stream stream)
    {
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }

    public static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Best effort; the original exception is more useful.
        }
    }
}
