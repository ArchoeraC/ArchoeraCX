namespace Archoera.Infrastructure.Resources.Modrinth;

/// <summary>
/// Read-only wrapper that aborts as soon as a provider response exceeds the
/// configured byte budget. <see cref="System.Text.Json.JsonDocument.ParseAsync"/>
/// reads incrementally, so the limit also covers bodies without a
/// <c>Content-Length</c> header.
/// </summary>
internal sealed class BoundedReadStream : Stream
{
    private readonly Stream _inner;
    private readonly long _maxBytes;
    private long _read;

    public BoundedReadStream(Stream inner, long maxBytes)
    {
        ArgumentNullException.ThrowIfNull(inner);
        if (maxBytes <= 0) throw new ArgumentOutOfRangeException(nameof(maxBytes));
        _inner = inner;
        _maxBytes = maxBytes;
    }

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override int Read(byte[] buffer, int offset, int count)
        => Count(_inner.Read(buffer, offset, count));

    public override int Read(Span<byte> buffer)
        => Count(_inner.Read(buffer));

    public override async Task<int> ReadAsync(
        byte[] buffer,
        int offset,
        int count,
        CancellationToken cancellationToken)
        => Count(await _inner
            .ReadAsync(buffer.AsMemory(offset, count), cancellationToken)
            .ConfigureAwait(false));

    public override async ValueTask<int> ReadAsync(
        Memory<byte> buffer,
        CancellationToken cancellationToken = default)
        => Count(await _inner
            .ReadAsync(buffer, cancellationToken)
            .ConfigureAwait(false));

    private int Count(int read)
    {
        if (read <= 0) return read;
        _read += read;
        if (_read > _maxBytes)
        {
            throw new InvalidDataException(
                $"response exceeded {_maxBytes} bytes");
        }

        return read;
    }

    public override void Flush()
    {
    }

    public override long Seek(long offset, SeekOrigin origin)
        => throw new NotSupportedException();

    public override void SetLength(long value)
        => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count)
        => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing) _inner.Dispose();
        base.Dispose(disposing);
    }

    public override async ValueTask DisposeAsync()
    {
        await _inner.DisposeAsync().ConfigureAwait(false);
        await base.DisposeAsync().ConfigureAwait(false);
    }
}
