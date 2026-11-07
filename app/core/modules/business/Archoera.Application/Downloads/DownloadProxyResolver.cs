using System.Net;
using Archoera.Domain.Downloads;

namespace Archoera.Application.Downloads;

/// <summary>
/// Resolves the system/env proxy once so every plan carries the same routing
/// as C#'s own HTTP calls (docs/10 §9.5). Rust never guesses a proxy.
/// </summary>
public static class DownloadProxyResolver
{
    private static readonly Lazy<DownloadProxyOptions?> Cached = new(ResolveUncached);

    private static readonly Uri Probe =
        new("https://piston-meta.mojang.com/mc/game/version_manifest_v2.json");

    /// <summary>Process-wide proxy, or null when connections should go direct.</summary>
    public static DownloadProxyOptions? Current => Cached.Value;

    /// <summary>Test seam: resolve without touching the cache.</summary>
    public static DownloadProxyOptions? Resolve() => ResolveUncached();

    /// <summary>
    /// Manual proxy from settings (`http(s)://` or `socks5(h)://host:port`).
    /// Returns null for empty/invalid input so the system proxy still applies.
    /// </summary>
    public static DownloadProxyOptions? FromSettings(string? manual)
    {
        if (string.IsNullOrWhiteSpace(manual)) return null;
        var value = manual.Trim();
        if (!value.Contains("://", StringComparison.Ordinal))
        {
            value = "http://" + value;
        }

        return Uri.TryCreate(value, UriKind.Absolute, out var uri) && IsSupported(uri)
            ? new DownloadProxyOptions(Normalize(uri), ReadNoProxy())
            : null;
    }

    private static DownloadProxyOptions? ResolveUncached()
    {
        // Env vars first, then the desktop's own configuration (KDE/GNOME/PAC),
        // then .NET's platform proxy. Browsers use the desktop settings, so a
        // launcher that ignores them looks throttled on the same link.
        var proxy = ReadEnvironmentProxy() ?? ReadDesktopProxy() ?? ReadSystemProxy();
        if (proxy is null) return null;
        return new DownloadProxyOptions(proxy, ReadNoProxy());
    }

    /// <summary>
    /// Linux desktops keep proxies in KDE's kioslaverc, GNOME's gsettings or a
    /// PAC URL. Nothing here is fatal: any failure falls through to the next
    /// source.
    /// </summary>
    private static string? ReadDesktopProxy()
    {
        if (!OperatingSystem.IsLinux()) return null;
        return ReadKdeProxy() ?? ReadGnomeProxy() ?? ReadPacProxy();
    }

    private static string? ReadKdeProxy()
    {
        try
        {
            var configHome = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
            if (string.IsNullOrWhiteSpace(configHome))
            {
                var home = Environment.GetEnvironmentVariable("HOME");
                if (string.IsNullOrWhiteSpace(home)) return null;
                configHome = Path.Combine(home, ".config");
            }

            var path = Path.Combine(configHome, "kioslaverc");
            if (!File.Exists(path)) return null;

            string? pac = null;
            string? http = null;
            string? https = null;
            var inSection = false;
            foreach (var raw in File.ReadLines(path))
            {
                var line = raw.Trim();
                if (line.StartsWith('['))
                {
                    inSection = line == "[Proxy Settings]";
                    continue;
                }
                if (!inSection) continue;
                var split = line.IndexOf('=');
                if (split <= 0) continue;
                var key = line[..split].Trim();
                var value = line[(split + 1)..].Trim();
                switch (key)
                {
                    case "Proxy Config Script":
                        pac = value;
                        break;
                    case "httpProxy":
                        http = value;
                        break;
                    case "httpsProxy":
                        https = value;
                        break;
                }
            }

            // Clash/Verge-style setups keep a PAC even when ProxyType is unset,
            // and browsers use it; prefer it so downloads match the browser.
            if (!string.IsNullOrWhiteSpace(pac))
            {
                var fromPac = FetchPacProxy(pac);
                if (fromPac is not null) return fromPac;
            }

            return Normalize(https) ?? Normalize(http);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static string? ReadGnomeProxy()
    {
        try
        {
            var mode = RunGsettings("get org.gnome.system.proxy mode");
            if (mode is null || !mode.Contains("manual", StringComparison.Ordinal))
            {
                return null;
            }

            var host = RunGsettings("get org.gnome.system.proxy.https host")
                ?? RunGsettings("get org.gnome.system.proxy.http host");
            var port = RunGsettings("get org.gnome.system.proxy.https port")
                ?? RunGsettings("get org.gnome.system.proxy.http port");
            host = host?.Trim('\'', '"');
            port = port?.Trim('\'', '"');
            if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(port))
            {
                return null;
            }

            return Normalize($"http://{host}:{port}");
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException
                or System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }

    private static string? RunGsettings(string arguments)
        => RunCommand("gsettings", arguments, TimeSpan.FromSeconds(2));

    /// <summary>
    /// Runs a helper command with a hard deadline. Reading stdout before
    /// waiting used to make the 2 s timeout meaningless: a gsettings process
    /// stuck on D-Bus/dconf kept the read blocked forever, and the first
    /// download then stayed at 0/0 before the Rust executor even started.
    /// </summary>
    internal static string? RunCommand(string fileName, string arguments, TimeSpan timeout)
    {
        try
        {
            using var process = System.Diagnostics.Process.Start(
                new System.Diagnostics.ProcessStartInfo
                {
                    FileName = fileName,
                    Arguments = arguments,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                });
            if (process is null) return null;

            // Drain both pipes concurrently: a helper that exceeds the pipe
            // buffer must not look like a timeout.
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            var milliseconds = (int)Math.Clamp(
                timeout.TotalMilliseconds,
                1,
                int.MaxValue);
            if (!process.WaitForExit(milliseconds))
            {
                TryKill(process);
                return null;
            }

            var output = stdout.GetAwaiter().GetResult().Trim();
            _ = stderr.GetAwaiter().GetResult();
            return process.ExitCode == 0 && output.Length > 0 ? output : null;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException
                or System.ComponentModel.Win32Exception or AggregateException
                or ObjectDisposedException)
        {
            return null;
        }
    }

    private static void TryKill(System.Diagnostics.Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or System.ComponentModel.Win32Exception
                or NotSupportedException)
        {
            // Best effort: the process may have exited between the wait and
            // the kill, or the platform may not support tree termination.
        }
    }

    private static string? ReadPacProxy()
    {
        var pac = Environment.GetEnvironmentVariable("AUTO_PROXY_URL")
            ?? Environment.GetEnvironmentVariable("PAC_URL");
        return string.IsNullOrWhiteSpace(pac) ? null : FetchPacProxy(pac);
    }

    /// <summary>
    /// Minimal PAC support: fetch the script and take the first `PROXY` (or
    /// `SOCKS5`) endpoint. Complex scripts fall through to the system proxy.
    /// </summary>
    private static string? FetchPacProxy(string pacUrl)
    {
        if (!Uri.TryCreate(pacUrl.Trim(), UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return null;
        }

        try
        {
            // The PAC itself is normally on loopback: never route it through a proxy.
            using var handler = new HttpClientHandler { UseProxy = false };
            using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(3) };
            var script = http.GetStringAsync(uri).GetAwaiter().GetResult();
            return ParsePac(script);
        }
        catch (Exception exception) when (
            exception is HttpRequestException or TaskCanceledException or IOException)
        {
            return null;
        }
    }

    /// <summary>First usable endpoint from a PAC script body.</summary>
    public static string? ParsePac(string script)
    {
        // Match "PROXY host:port" / "SOCKS5 host:port" / "SOCKS host:port".
        var match = System.Text.RegularExpressions.Regex.Match(
            script,
            @"(PROXY|SOCKS5|SOCKS)\s+([A-Za-z0-9._-]+):(\d{1,5})\b",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (!match.Success) return null;
        var scheme = match.Groups[1].Value.Equals("PROXY", StringComparison.OrdinalIgnoreCase)
            ? "http"
            : "socks5h";
        return Normalize($"{scheme}://{match.Groups[2].Value}:{match.Groups[3].Value}");
    }

    private static string? Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var text = value.Trim();
        if (text.StartsWith("DIRECT", StringComparison.OrdinalIgnoreCase)) return null;
        if (!text.Contains("://", StringComparison.Ordinal))
        {
            text = "http://" + text;
        }

        return Uri.TryCreate(text, UriKind.Absolute, out var uri) && IsSupported(uri)
            ? Normalize(uri)
            : null;
    }

    private static string Normalize(Uri uri) => uri.AbsoluteUri;

    private static string? ReadEnvironmentProxy()
    {
        foreach (var name in new[]
                 {
                     "HTTPS_PROXY", "https_proxy", "ALL_PROXY", "all_proxy",
                 })
        {
            var value = Environment.GetEnvironmentVariable(name)?.Trim();
            if (string.IsNullOrEmpty(value)) continue;
            if (!value.Contains("://", StringComparison.Ordinal))
            {
                value = "http://" + value;
            }

            return Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
                   IsSupported(uri)
                ? Normalize(uri)
                : null;
        }

        return null;
    }

    private static string? ReadSystemProxy()
    {
        try
        {
            var candidate = HttpClient.DefaultProxy.GetProxy(Probe);
            if (candidate is null) return null;
            // .NET returns the probe URI itself when no proxy is configured.
            if (Uri.Compare(
                    candidate,
                    Probe,
                    UriComponents.SchemeAndServer,
                    UriFormat.Unescaped,
                    StringComparison.OrdinalIgnoreCase) == 0)
            {
                return null;
            }

            return IsSupported(candidate) ? Normalize(candidate) : null;
        }
        catch (PlatformNotSupportedException)
        {
            return null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private static IReadOnlyList<string> ReadNoProxy()
    {
        foreach (var name in new[] { "NO_PROXY", "no_proxy" })
        {
            var value = Environment.GetEnvironmentVariable(name);
            if (string.IsNullOrWhiteSpace(value)) continue;
            return SplitNoProxy(value);
        }

        return OperatingSystem.IsLinux() ? ReadKdeNoProxy() : [];
    }

    private static IReadOnlyList<string> SplitNoProxy(string value) => value
        .Split([',', ';'], StringSplitOptions.RemoveEmptyEntries |
                          StringSplitOptions.TrimEntries)
        .Where(entry => entry.Length is > 0 and <= 255)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();

    /// <summary>KDE stores bypass hosts in kioslaverc; browsers honor it too.</summary>
    private static IReadOnlyList<string> ReadKdeNoProxy()
    {
        try
        {
            var configHome = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
            if (string.IsNullOrWhiteSpace(configHome))
            {
                var home = Environment.GetEnvironmentVariable("HOME");
                if (string.IsNullOrWhiteSpace(home)) return [];
                configHome = Path.Combine(home, ".config");
            }

            var path = Path.Combine(configHome, "kioslaverc");
            if (!File.Exists(path)) return [];

            var inSection = false;
            foreach (var raw in File.ReadLines(path))
            {
                var line = raw.Trim();
                if (line.StartsWith('['))
                {
                    inSection = line == "[Proxy Settings]";
                    continue;
                }
                if (!inSection || !line.StartsWith("NoProxyFor=", StringComparison.Ordinal))
                {
                    continue;
                }

                return SplitNoProxy(line["NoProxyFor=".Length..]);
            }
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            // Ignore: bypass hosts are an optimization, not a requirement.
        }

        return [];
    }

    private static bool IsSupported(Uri uri)
        => (uri.Scheme is "http" or "https" or "socks5" or "socks5h" or "socks4" or "socks4a") &&
           !string.IsNullOrEmpty(uri.Host);
}
