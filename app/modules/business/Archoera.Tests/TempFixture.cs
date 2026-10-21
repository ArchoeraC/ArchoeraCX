namespace Archoera.Tests;

using Archoera.DevHost;

/// <summary>
/// Owns a temporary directory holding a synthetic installer. Tests never use
/// Java or the network.
/// </summary>
internal sealed class TempFixture : IDisposable
{
    public TempFixture(
        string name,
        bool includePatchedOutput = true,
        bool includePatchedLibrary = true)
    {
        DirectoryPath = Path.Combine(
            Path.GetTempPath(),
            "archoera-tests",
            $"{name}-{Guid.NewGuid():N}");
        Loader = SyntheticLoaderFixture.Create(
            DirectoryPath,
            includePatchedOutput,
            includePatchedLibrary);
    }

    public string DirectoryPath { get; }

    public SyntheticLoaderFixture Loader { get; }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(DirectoryPath))
            {
                Directory.Delete(DirectoryPath, recursive: true);
            }
        }
        catch
        {
            // Test cleanup is best effort on Windows (process handles).
        }
    }
}
