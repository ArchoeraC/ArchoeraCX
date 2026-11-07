using System.Net;
using System.Net.Sockets;
using System.Text;
using Archoera.Application.Accounts;

namespace Archoera.Infrastructure.Accounts;

/// <summary>
/// Minimal loopback HTTP listener for the Microsoft OAuth 2.0 Authorization
/// Code redirect. It binds to 127.0.0.1 only, accepts connections until it sees
/// <c>GET /callback</c>, and never logs query values, codes, or state.
///
/// BCL only (TcpListener), so it stays NativeAOT-compatible.
/// </summary>
public sealed class LoopbackOAuthListener : IDisposable
{
    private const string CallbackPath = "/callback";
    private const string SuccessHtml =
        "<!doctype html><html><head><meta charset=\"utf-8\"><title>ArchoeraCX</title></head>" +
        "<body><h1>Sign-in complete</h1><p>You can close this window and return to ArchoeraCX.</p></body></html>";
    private const string NotFoundHtml =
        "<!doctype html><html><head><meta charset=\"utf-8\"><title>ArchoeraCX</title></head>" +
        "<body><h1>Not found</h1></body></html>";

    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(5);

    private readonly TcpListener _listener;
    private bool _disposed;

    /// <summary>Starts a listener. Port 0 asks the OS for a free ephemeral port.</summary>
    public LoopbackOAuthListener(int port = 0)
    {
        if (port is < 0 or > 65535)
        {
            throw new ArgumentOutOfRangeException(nameof(port));
        }

        _listener = new TcpListener(IPAddress.Loopback, port);
        _listener.Start();
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        RedirectUri = $"http://localhost:{Port}{CallbackPath}";
    }

    /// <summary>Creates and starts a listener on a loopback port.</summary>
    public static LoopbackOAuthListener Start(int port = 0) => new(port);

    public int Port { get; }

    public string RedirectUri { get; }

    /// <summary>
    /// Waits for the browser redirect. Non-callback paths are answered with 404
    /// and ignored; malformed/slow connections are dropped after a short read
    /// timeout. Cancellation or disposal unblocks the pending accept.
    /// </summary>
    public async Task<LoopbackOAuthCallback> WaitForCallbackAsync(
        CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            TcpClient client;
            try
            {
                client = await _listener
                    .AcceptTcpClientAsync(cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (ObjectDisposedException) when (_disposed)
            {
                throw new OperationCanceledException(
                    "The loopback listener was disposed.",
                    cancellationToken);
            }
            catch (SocketException) when (_disposed)
            {
                throw new OperationCanceledException(
                    "The loopback listener was disposed.",
                    cancellationToken);
            }

            using (client)
            {
                client.NoDelay = true;
                var callback = await TryHandleAsync(client, cancellationToken)
                    .ConfigureAwait(false);
                if (callback is not null)
                {
                    return callback;
                }
            }
        }
    }

    private static async Task<LoopbackOAuthCallback?> TryHandleAsync(
        TcpClient client,
        CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(RequestTimeout);

        try
        {
            var stream = client.GetStream();
            using var reader = new StreamReader(
                stream,
                Encoding.ASCII,
                detectEncodingFromByteOrderMarks: false,
                bufferSize: 1024,
                leaveOpen: true);

            var requestLine = await reader
                .ReadLineAsync(timeout.Token)
                .ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(requestLine))
            {
                return null;
            }

            // Drain headers (bounded) so the response is not sent mid-request.
            for (var i = 0; i < 64; i++)
            {
                var line = await reader
                    .ReadLineAsync(timeout.Token)
                    .ConfigureAwait(false);
                if (string.IsNullOrEmpty(line))
                {
                    break;
                }
            }

            var parts = requestLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2 ||
                !string.Equals(parts[0], "GET", StringComparison.OrdinalIgnoreCase))
            {
                await WriteResponseAsync(
                        stream,
                        405,
                        "Method Not Allowed",
                        NotFoundHtml,
                        timeout.Token)
                    .ConfigureAwait(false);
                return null;
            }

            var target = parts[1];
            string path;
            string query;
            if (Uri.TryCreate(target, UriKind.Absolute, out var absolute) &&
                absolute.Scheme is "http" or "https")
            {
                path = absolute.AbsolutePath;
                query = absolute.Query.TrimStart('?');
            }
            else
            {
                var queryStart = target.IndexOf('?');
                path = queryStart < 0 ? target : target[..queryStart];
                query = queryStart < 0 ? string.Empty : target[(queryStart + 1)..];
            }

            if (!string.Equals(path, CallbackPath, StringComparison.Ordinal))
            {
                await WriteResponseAsync(
                        stream,
                        404,
                        "Not Found",
                        NotFoundHtml,
                        timeout.Token)
                    .ConfigureAwait(false);
                return null;
            }

            var values = ParseQuery(query);
            var callback = new LoopbackOAuthCallback(
                values.GetValueOrDefault("code"),
                values.GetValueOrDefault("state"),
                values.GetValueOrDefault("error"),
                values.GetValueOrDefault("error_description"));

            await WriteResponseAsync(stream, 200, "OK", SuccessHtml, timeout.Token)
                .ConfigureAwait(false);
            return callback;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Per-connection read timeout: drop it and keep waiting.
            return null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (SocketException)
        {
            return null;
        }
        catch (ObjectDisposedException) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
    }

    private static Dictionary<string, string> ParseQuery(string query)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = pair.IndexOf('=');
            var name = separator < 0 ? pair : pair[..separator];
            var value = separator < 0 ? string.Empty : pair[(separator + 1)..];
            name = Decode(name);
            if (name.Length == 0)
            {
                continue;
            }

            values.TryAdd(name, Decode(value));
        }

        return values;
    }

    private static string Decode(string value)
    {
        try
        {
            return Uri.UnescapeDataString(value.Replace('+', ' '));
        }
        catch (UriFormatException)
        {
            return string.Empty;
        }
    }

    private static async Task WriteResponseAsync(
        NetworkStream stream,
        int status,
        string reason,
        string body,
        CancellationToken cancellationToken)
    {
        var bodyBytes = Encoding.UTF8.GetBytes(body);
        var header =
            $"HTTP/1.1 {status} {reason}\r\n" +
            "Content-Type: text/html; charset=utf-8\r\n" +
            $"Content-Length: {bodyBytes.Length}\r\n" +
            "Cache-Control: no-store\r\n" +
            "Connection: close\r\n\r\n";

        await stream
            .WriteAsync(Encoding.ASCII.GetBytes(header), cancellationToken)
            .ConfigureAwait(false);
        await stream.WriteAsync(bodyBytes, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        try
        {
            _listener.Stop();
        }
        catch (SocketException)
        {
            // best effort
        }
        catch (ObjectDisposedException)
        {
            // best effort
        }
    }
}
