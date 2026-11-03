using System.Runtime.InteropServices;
using Ionic.Zlib;

namespace Archoera.Infrastructure.Loaders.Native;

/// <summary>
/// Raw deflate with the same parameters as Java's <c>new Deflater()</c>
/// (zlib level 6, 32 KiB window, memLevel 8, default strategy). The installer
/// jars are hash-verified, so the compressed bytes must match zlib exactly;
/// .NET's zlib-ng based <c>DeflateStream</c> and the managed zlib ports
/// (zlib 1.2.x) do not.
/// </summary>
/// <remarks>
/// On Linux the system zlib (1.3.x) is used through P/Invoke, which reproduces
/// the installer bytes. Other platforms fall back to the managed port: the jars
/// remain valid but are not byte-identical to the official installer output.
/// </remarks>
internal static class ZipDeflate
{
    private const int ZOk = 0;
    private const int ZStreamEnd = 1;
    private const int ZFinish = 4;
    private const int ZDefaultStrategy = 0;

    private static readonly Lazy<bool> NativeAvailable = new(ProbeNative);

    public static byte[] Compress(ReadOnlySpan<byte> data)
    {
        if (NativeAvailable.Value)
        {
            try
            {
                return CompressNative(data);
            }
            catch (Exception exception) when (
                exception is DllNotFoundException or EntryPointNotFoundException or
                    BadImageFormatException)
            {
                // Fall through to the managed implementation.
            }
        }

        return CompressManaged(data);
    }

    private static bool ProbeNative()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return false;
        }

        try
        {
            return Zlib.Version() != IntPtr.Zero;
        }
        catch (Exception exception) when (
            exception is DllNotFoundException or EntryPointNotFoundException or
                BadImageFormatException)
        {
            return false;
        }
    }

    private static byte[] CompressNative(ReadOnlySpan<byte> data)
    {
        var input = data.ToArray();
        var output = new byte[Math.Max(64, input.Length + (input.Length / 8) + 1024)];
        var stream = new ZStream();
        var result = Zlib.Init2(
            ref stream,
            6,
            8,
            -15,
            8,
            ZDefaultStrategy,
            Zlib.Version(),
            Marshal.SizeOf<ZStream>());
        if (result != ZOk)
        {
            throw new InvalidOperationException($"zlib deflateInit2 failed with {result}");
        }

        var inputHandle = GCHandle.Alloc(input, GCHandleType.Pinned);
        try
        {
            var written = 0;
            stream.next_in = inputHandle.AddrOfPinnedObject();
            stream.avail_in = (uint)input.Length;
            while (true)
            {
                var outputHandle = GCHandle.Alloc(output, GCHandleType.Pinned);
                try
                {
                    stream.next_out = IntPtr.Add(outputHandle.AddrOfPinnedObject(), written);
                    stream.avail_out = (uint)(output.Length - written);
                    result = Zlib.Deflate(ref stream, ZFinish);
                    written = output.Length - (int)stream.avail_out;
                }
                finally
                {
                    outputHandle.Free();
                }

                if (result == ZStreamEnd)
                {
                    break;
                }

                if (result != ZOk)
                {
                    throw new InvalidOperationException($"zlib deflate failed with {result}");
                }

                // The output buffer was too small; grow it and continue.
                Array.Resize(ref output, output.Length * 2);
            }

            return output.AsSpan(0, written).ToArray();
        }
        finally
        {
            inputHandle.Free();
            Zlib.End(ref stream);
        }
    }

    internal static byte[] CompressManaged(ReadOnlySpan<byte> data)
    {
        if (data.Length == 0)
        {
            // zlib emits an empty final block; the managed stream emits nothing.
            return [0x03, 0x00];
        }

        using var output = new MemoryStream();
        using (var deflater = new DeflateStream(
                   output,
                   CompressionMode.Compress,
                   CompressionLevel.Level6,
                   leaveOpen: true))
        {
            var bytes = data.ToArray();
            deflater.Write(bytes, 0, bytes.Length);
        }

        return output.ToArray();
    }

    /// <summary>
    /// zlib's <c>z_stream</c> on LP64 platforms (Linux/macOS 64-bit). The
    /// Windows <c>uLong</c> is 32-bit, so the native path is Linux-only for now.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct ZStream
    {
        public IntPtr next_in;
        public uint avail_in;
        public ulong total_in;
        public IntPtr next_out;
        public uint avail_out;
        public ulong total_out;
        public IntPtr msg;
        public IntPtr state;
        public IntPtr zalloc;
        public IntPtr zfree;
        public IntPtr opaque;
        public int data_type;
        public ulong adler;
        public ulong reserved;
    }

    private static class Zlib
    {
        [DllImport("libz.so.1", EntryPoint = "zlibVersion")]
        public static extern IntPtr Version();

        [DllImport("libz.so.1", EntryPoint = "deflateInit2_")]
        public static extern int Init2(
            ref ZStream stream,
            int level,
            int method,
            int windowBits,
            int memLevel,
            int strategy,
            IntPtr version,
            int streamSize);

        [DllImport("libz.so.1", EntryPoint = "deflate")]
        public static extern int Deflate(ref ZStream stream, int flush);

        [DllImport("libz.so.1", EntryPoint = "deflateEnd")]
        public static extern int End(ref ZStream stream);
    }
}
