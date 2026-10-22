using System.Text;

namespace Archoera.Infrastructure.Launch;

/// <summary>
/// Writes one launch log and stops appending once <see cref="_maxBytes"/> is
/// reached, leaving a marker in the file. The process output is still drained
/// by the caller, so a chatty JVM cannot block on a full pipe.
/// </summary>
internal sealed class LaunchLogWriter : IDisposable
{
    private readonly object _gate = new();
    private readonly StreamWriter _writer;
    private readonly long _maxBytes;
    private bool _truncated;
    private bool _closed;

    public LaunchLogWriter(string path, long maxBytes = LaunchLogFiles.MaxFileBytes)
    {
        var stream = new FileStream(
            path,
            FileMode.Create,
            FileAccess.Write,
            FileShare.Read);
        _writer = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true };
        _maxBytes = maxBytes;
    }

    public bool Truncated
    {
        get
        {
            lock (_gate) return _truncated;
        }
    }

    public void WriteLine(string line)
    {
        lock (_gate)
        {
            if (_closed || _truncated) return;
            if (_writer.BaseStream.Length >= _maxBytes)
            {
                _truncated = true;
                WriteUnlocked(
                    $"[archoera] log truncated after {FormatLimit(_maxBytes)}; " +
                    "further output is dropped");
                return;
            }

            WriteUnlocked(line);
        }
    }

    /// <summary>Appends the exit marker and closes the file.</summary>
    public void Complete(int exitCode)
    {
        lock (_gate)
        {
            if (_closed) return;
            WriteUnlocked($"[exited with code {exitCode}]");
            _closed = true;
            _writer.Dispose();
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_closed) return;
            _closed = true;
            _writer.Dispose();
        }
    }

    private void WriteUnlocked(string line)
    {
        try
        {
            _writer.WriteLine(line);
        }
        catch (IOException)
        {
            // The viewer keeps the file open with FileShare.Delete; a failed
            // write must not take the game process down.
        }
        catch (ObjectDisposedException)
        {
            // The process exited while output was still arriving.
        }
    }

    private static string FormatLimit(long bytes)
    {
        if (bytes >= 1024 * 1024 && bytes % (1024 * 1024) == 0)
        {
            return $"{bytes / (1024 * 1024)} MiB";
        }

        if (bytes >= 1024 && bytes % 1024 == 0)
        {
            return $"{bytes / 1024} KiB";
        }

        return $"{bytes} bytes";
    }
}
