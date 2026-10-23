using Archoera.Application;
using Archoera.Application.Downloads;
using Archoera.Application.Loaders;
using Archoera.Application.Modules;
using Archoera.Application.Packs;
using Archoera.Domain.Downloads;
using Archoera.Domain.Loaders;
using Archoera.Infrastructure.Downloads;
using Archoera.Infrastructure.Loaders;
using Archoera.Infrastructure.Modules;

namespace Archoera.Infrastructure.Packs;

/// <summary>
/// Downloads the Forge/NeoForge installer for an in-place repair. The primary
/// path goes through the Rust executor with the Maven sidecar digest; the
/// last-resort bootstrap downloads straight from the BMCLAPI mirror and
/// verifies the bytes against the sidecar SHA-1 before the installer runs.
/// </summary>
internal sealed class PackForgeInstallerBootstrap(
    PackGameInstallService gameInstall,
    IDownloadPlanRunner runner,
    HttpClient http)
{
    internal async Task<string> EnsureForgeInstallerAsync(
        string instanceRoot,
        LoaderKind kind,
        string gameVersion,
        string loaderVersion,
        CancellationToken cancellationToken)
    {
        var relative = ForgeMavenUrls.DefaultInstallerRelativePath(
            kind,
            gameVersion,
            loaderVersion);
        var destination = Path.Combine(
            instanceRoot,
            relative.Replace('/', Path.DirectorySeparatorChar));
        if (IsZip(destination)) return destination;

        var parent = Path.GetDirectoryName(destination);
        if (parent is not null) Directory.CreateDirectory(parent);

        var installerUri = ForgeMavenUrls.Installer(kind, gameVersion, loaderVersion);
        string? expectedSha1 = null;
        try
        {
            var descriptor = await gameInstall
                .ResolveForgeInstallerAsync(
                    kind,
                    gameVersion,
                    loaderVersion,
                    cancellationToken)
                .ConfigureAwait(false);
            if (descriptor.Size is not { } size)
            {
                throw new PackException(
                    PackErrorCode.InstallerFailed,
                    "installer size is missing");
            }

            // The sidecar digest is the verification source for both the primary
            // plan (the executor checks it) and the mirror bootstrap below.
            expectedSha1 = LoaderPaths.NormalizeSha1(descriptor.Sha1);

            var primary = descriptor.Url;
            IReadOnlyList<string> mirrors = [];
            if (ForgeMavenMirror.TryRewrite(
                    descriptor.Url,
                    ForgeMavenMirror.Bmclapi,
                    out var mapped,
                    out var fallback))
            {
                primary = mapped;
                mirrors = fallback;
            }

            var plan = new DownloadPlanBuilder(instanceRoot)
                .AddFile(
                    primary,
                    relative,
                    DownloadHash.Create(DownloadHashAlgorithm.Sha1, descriptor.Sha1),
                    size,
                    mirrors)
                .Build();
            var result = await runner
                .ExecuteAsync(plan, cancellationToken)
                .ConfigureAwait(false);
            if (result.Completed && IsZip(destination))
            {
                return destination;
            }
        }
        catch (ForgeDownloadException)
        {
            // Official NeoForge Maven is blocked on some networks; fall back.
        }
        catch (DownloadRunnerException exception)
            when (exception.Code != "DOWNLOADER_MISSING")
        {
            // Try the mirror bootstrap before failing the whole install.
        }

        await DownloadInstallerFallbackAsync(
                http,
                installerUri,
                destination,
                expectedSha1,
                cancellationToken)
            .ConfigureAwait(false);
        return destination;
    }

    /// <summary>
    /// Last-resort installer bootstrap: direct mirror downloads, used only when
    /// the official Maven sidecar / Rust executor path failed. Every download
    /// is hashed while it is written (see <see cref="Sha1Streams"/>); when the
    /// sidecar digest is known a mirror is rejected unless its bytes match it,
    /// and a rejected mirror sends the whole bootstrap back through the
    /// official channel. Without a published digest the SHA-1 is still computed
    /// from the received bytes and flows into the installer descriptor of the
    /// combined plan (same as the primary path), so the download chain stays
    /// verifiable end to end.
    /// </summary>
    internal static async Task<string> DownloadInstallerFallbackAsync(
        HttpClient http,
        Uri officialUri,
        string destination,
        string? expectedSha1,
        CancellationToken cancellationToken)
    {
        var mirrors = ForgeMavenMirror.InstallerMirrors(officialUri);
        if (mirrors.Count == 0)
        {
            throw new PackException(
                PackErrorCode.InstallerFailed,
                $"no mirror is available for {officialUri.Host}");
        }

        var failures = new List<string>();
        ModuleProgress.Report(0, 0, 0, "Downloading loader installer");
        foreach (var mirror in mirrors)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var sha1 = await DownloadFileAsync(http, mirror, destination, cancellationToken)
                    .ConfigureAwait(false);
                if (expectedSha1 is not null &&
                    !string.Equals(sha1, expectedSha1, StringComparison.OrdinalIgnoreCase))
                {
                    // Mirrors must serve byte-identical copies. Do not trust the
                    // remaining mirrors after one failed verification: throw the
                    // file away and take the official source.
                    TryDeleteFile(destination);
                    return await DownloadFromOfficialAsync(
                            http,
                            officialUri,
                            destination,
                            expectedSha1,
                            failures,
                            cancellationToken)
                        .ConfigureAwait(false);
                }

                return sha1;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception) when (
                exception is HttpRequestException or IOException or
                UnauthorizedAccessException or OperationCanceledException)
            {
                failures.Add($"{mirror.Host}: {exception.Message}");
            }
        }

        throw new PackException(
            PackErrorCode.InstallerFailed,
            "installer download failed: " + string.Join("; ", failures.Take(3)));
    }

    private static async Task<string> DownloadFromOfficialAsync(
        HttpClient http,
        Uri officialUri,
        string destination,
        string? expectedSha1,
        IReadOnlyList<string> failures,
        CancellationToken cancellationToken)
    {
        string sha1;
        try
        {
            sha1 = await DownloadFileAsync(http, officialUri, destination, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is HttpRequestException or IOException or
            UnauthorizedAccessException or OperationCanceledException)
        {
            throw new PackException(
                PackErrorCode.InstallerFailed,
                "official installer download failed: " + string.Join(
                    "; ",
                    failures.Take(3)).TrimEnd(';'),
                exception);
        }

        if (expectedSha1 is not null &&
            !string.Equals(sha1, expectedSha1, StringComparison.OrdinalIgnoreCase))
        {
            TryDeleteFile(destination);
            throw new PackException(
                PackErrorCode.InstallerFailed,
                "the official installer does not match its published SHA-1");
        }

        return sha1;
    }

    private static async Task<string> DownloadFileAsync(
        HttpClient http,
        Uri uri,
        string destination,
        CancellationToken cancellationToken)
    {
        const long maxBytes = 128L * 1024 * 1024;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(3));
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.UserAgent.ParseAdd(
            $"ArchoeraCX/{ArchoeraBuildInfo.Version} (https://github.com/BetaStudio2/ArchoeraCX)");
        using var response = await http
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token)
            .ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"HTTP {(int)response.StatusCode} from {uri.Host}");
        }

        if (response.Content.Headers.ContentLength is { } length && length > maxBytes)
        {
            throw new HttpRequestException(
                $"{uri.Host} returned an oversized installer ({length} bytes)");
        }

        var temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        string sha1;
        try
        {
            await using (var source = await response.Content
                             .ReadAsStreamAsync(timeout.Token)
                             .ConfigureAwait(false))
            await using (var target = new FileStream(
                             temporary,
                             FileMode.Create,
                             FileAccess.Write,
                             FileShare.None,
                             bufferSize: 128 * 1024,
                             useAsync: true))
            {
                (sha1, _) = await Sha1Streams
                    .CopyAsync(source, target, maxBytes, uri.Host, timeout.Token)
                    .ConfigureAwait(false);
            }

            if (!IsZip(temporary))
            {
                throw new HttpRequestException(
                    $"{uri.Host} did not return a jar");
            }

            File.Move(temporary, destination, overwrite: true);
        }
        finally
        {
            TryDeleteFile(temporary);
        }

        return sha1;
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            // A leftover temp file is harmless; the next attempt overwrites it.
        }
    }

    internal static bool IsZip(string path)
    {
        try
        {
            if (!File.Exists(path)) return false;
            using var stream = File.OpenRead(path);
            var first = stream.ReadByte();
            var second = stream.ReadByte();
            return first == 'P' && second == 'K';
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
