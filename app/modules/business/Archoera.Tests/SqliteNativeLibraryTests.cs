using Archoera.Infrastructure.Storage;

namespace Archoera.Tests;

/// <summary>
/// Only the pure probe/resolve logic is covered here. Loading the native
/// library itself is exercised by the repository integration tests and the
/// DevHost <c>--check</c> run.
/// </summary>
public sealed class SqliteNativeLibraryTests
{
    [Fact]
    public void Explicit_override_wins_over_the_bundled_candidate()
    {
        var directory = TempDirectory();
        try
        {
            var bundled = Path.Combine(directory, "lib");
            Directory.CreateDirectory(bundled);
            File.WriteAllText(
                Path.Combine(bundled, SqliteNativeLibrary.FileName),
                string.Empty);
            var overridePath = Path.Combine(directory, "custom-sqlite.so");
            File.WriteAllText(overridePath, string.Empty);

            var resolved = SqliteNativeLibrary.Resolve(
                overridePath,
                [bundled],
                SqliteNativeLibrary.FileName);

            Assert.Equal(Path.GetFullPath(overridePath), resolved);
        }
        finally
        {
            Delete(directory);
        }
    }

    [Fact]
    public void Missing_override_is_actionable_and_does_not_fall_through()
    {
        var directory = TempDirectory();
        try
        {
            var bundled = Path.Combine(directory, "lib");
            Directory.CreateDirectory(bundled);
            File.WriteAllText(
                Path.Combine(bundled, SqliteNativeLibrary.FileName),
                string.Empty);
            var missingOverride = Path.Combine(directory, "gone", "libe_sqlite3.so");

            var error = Assert.Throws<InvalidOperationException>(() =>
                SqliteNativeLibrary.Resolve(
                    missingOverride,
                    [bundled],
                    SqliteNativeLibrary.FileName));

            Assert.Contains(
                SqliteNativeLibrary.OverrideVariable,
                error.Message,
                StringComparison.Ordinal);
            Assert.Contains("build-cs.sh", error.Message, StringComparison.Ordinal);
        }
        finally
        {
            Delete(directory);
        }
    }

    [Fact]
    public void Base_lib_directory_is_probed_and_found()
    {
        var directory = TempDirectory();
        try
        {
            var lib = Path.Combine(directory, "lib");
            Directory.CreateDirectory(lib);
            var expected = Path.Combine(lib, SqliteNativeLibrary.FileName);
            File.WriteAllText(expected, string.Empty);

            var candidates = SqliteNativeLibrary.CandidatePaths(
                directory,
                processDirectory: null,
                cwd: null);
            var resolved = SqliteNativeLibrary.Resolve(
                overridePath: null,
                candidates,
                SqliteNativeLibrary.FileName);

            Assert.Equal(Path.GetFullPath(expected), resolved);
        }
        finally
        {
            Delete(directory);
        }
    }

    [Fact]
    public void Missing_everywhere_returns_null_without_throwing()
    {
        var directory = TempDirectory();
        try
        {
            var resolved = SqliteNativeLibrary.Resolve(
                overridePath: null,
                SqliteNativeLibrary.CandidatePaths(
                    directory,
                    processDirectory: null,
                    cwd: null),
                SqliteNativeLibrary.FileName);

            Assert.Null(resolved);
        }
        finally
        {
            Delete(directory);
        }
    }

    [Fact]
    public void Missing_payload_message_names_the_file_and_the_fix()
    {
        var candidates = SqliteNativeLibrary.CandidatePaths(
            "/opt/archoera/lib",
            processDirectory: null,
            cwd: null);

        var error = SqliteNativeLibrary.Missing(SqliteNativeLibrary.FileName, candidates);

        Assert.Contains(SqliteNativeLibrary.FileName, error.Message, StringComparison.Ordinal);
        Assert.Contains("build-cs.sh", error.Message, StringComparison.Ordinal);
        Assert.Contains("/opt/archoera/lib", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Candidates_stay_in_fixed_directories_and_are_deduplicated()
    {
        var baseDirectory = "/opt/archoera/lib";
        var candidates = SqliteNativeLibrary.CandidatePaths(
            baseDirectory,
            processDirectory: baseDirectory,
            cwd: "/work/archoera");

        Assert.Equal(baseDirectory, candidates[0]);
        Assert.Contains(Path.Combine(baseDirectory, "lib"), candidates);
        Assert.Contains(Path.Combine("/work/archoera", "build", "native"), candidates);
        Assert.Contains(
            Path.Combine("/work/archoera", "app", "build", "native"),
            candidates);
        Assert.Equal(
            candidates.Count,
            candidates.Distinct(StringComparer.Ordinal).Count());
        Assert.DoesNotContain(candidates, candidate =>
            candidate.Split(
                ['/', '\\'],
                StringSplitOptions.RemoveEmptyEntries).Contains(".."));
    }

    private static string TempDirectory()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            "archoera-sqlite-native",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void Delete(string path)
    {
        try
        {
            if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
        }
        catch
        {
            // best effort
        }
    }
}
