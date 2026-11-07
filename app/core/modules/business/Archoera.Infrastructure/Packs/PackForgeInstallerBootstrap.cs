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
    /// <summary>
    /// Suffix of the digest file written next to a verified installer. The
    /// sidecar is the only local proof that an installer still matches the
    /// bytes the launcher accepted, so a tampered installer can never be
    /// parsed or executed just because it still starts with a zip header.
    /// </summary>
    internal const string SidecarSuffix = ".sha1";

    internal async Task<string> EnsureForgeInstallerAsync(
        string instanceRoot,
        LoaderKind kind,
        string gameVersion,
        string loaderVersion,
        CancellationToken cancellationToken)
    {
        loaderVersion = LoaderVersionText.Normalize(
            kind.ToString(),
            gameVersion,
            loaderVersion);
        var relative = ForgeMavenUrls.DefaultInstallerRelativePath(
            kind,
            gameVersion,
            loaderVersion);
        var destination = Path.Combine(
            instanceRoot,
            relative.Replace('/', Path.DirectorySeparatorChar));
        var parent = Path.GetDirectoryName(destination);
        if (parent is not null) Directory.CreateDirectory(parent);

        // A matching sidecar is a complete local proof (written only after a
        // verified download), so a network failure below can still reuse it.
        // A mismatching sidecar is never trusted. A legacy installer without a
        // sidecar only passes via the published descriptor.
        var installerUri = ForgeMavenUrls.Installer(kind, gameVersion, loaderVersion);
        string? expectedSha1 = null;
        try
        {
            // Resolve the published digest before trusting an existing local
            // installer: a file that merely looks like a zip may have been
            // replaced after it was downloaded.
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

            if (IsZip(destination))
            {
                if (expectedSha1 is not null)
                {
                    var actual = await LoaderPaths
                        .Sha1FileAsync(destination, cancellationToken)
                        .ConfigureAwait(false);
                    if (string.Equals(actual, expectedSha1, StringComparison.OrdinalIgnoreCase))
                    {
                        await WriteSidecarAsync(destination, actual, cancellationToken)
                            .ConfigureAwait(false);
                        return destination;
                    }

                    // Stale or tampered: delete it before downloading again so
                    // it can never be picked up by the fallback path either.
                    DeleteInstaller(destination);
                }
                else
                {
                    var localState = await CheckSidecarAsync(destination, cancellationToken)
                        .ConfigureAwait(false);
                    if (localState == InstallerSidecarState.Match) return destination;
                    if (localState == InstallerSidecarState.Mismatch)
                    {
                        DeleteInstaller(destination);
                    }
                }
            }

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
                var actual = await LoaderPaths
                    .Sha1FileAsync(destination, cancellationToken)
                    .ConfigureAwait(false);
                if (expectedSha1 is null ||
                    string.Equals(actual, expectedSha1, StringComparison.OrdinalIgnoreCase))
                {
                    await WriteSidecarAsync(destination, actual, cancellationToken)
                        .ConfigureAwait(false);
                    return destination;
                }

                DeleteInstaller(destination);
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

        // Descriptor resolution can fail while an installer is still on disk.
        // Only our sidecar may vouch for it; a legacy installer without one is
        // replaced by the download below (and left untouched if that fails).
        if (IsZip(destination))
        {
            var state = await CheckSidecarAsync(destination, cancellationToken)
                .ConfigureAwait(false);
            if (state == InstallerSidecarState.Match) return destination;
            if (state == InstallerSidecarState.Mismatch) DeleteInstaller(destination);
        }

        var fallbackSha1 = await DownloadInstallerFallbackAsync(
                http,
                installerUri,
                destination,
                expectedSha1,
                cancellationToken)
            .ConfigureAwait(false);
        await WriteSidecarAsync(destination, fallbackSha1, cancellationToken)
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

    internal enum InstallerSidecarState
    {
        Absent,
        Match,
        Mismatch,
    }

    internal static string SidecarPath(string installerPath)
        => installerPath + SidecarSuffix;

    /// <summary>
    /// Compares a local installer with its <c>.sha1</c> sidecar. A missing
    /// sidecar means "legacy / another launcher" and is reported as
    /// <see cref="InstallerSidecarState.Absent"/>; an unreadable or malformed
    /// sidecar is a mismatch, never a pass.
    /// </summary>
    internal static async Task<InstallerSidecarState> CheckSidecarAsync(
        string installerPath,
        CancellationToken cancellationToken)
    {
        var sidecar = SidecarPath(installerPath);
        if (!File.Exists(sidecar)) return InstallerSidecarState.Absent;

        string? expected;
        try
        {
            expected = LoaderPaths.NormalizeSha1(
                await File.ReadAllTextAsync(sidecar, cancellationToken)
                    .ConfigureAwait(false));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            return InstallerSidecarState.Mismatch;
        }

        if (expected is null) return InstallerSidecarState.Mismatch;

        try
        {
            var actual = await LoaderPaths
                .Sha1FileAsync(installerPath, cancellationToken)
                .ConfigureAwait(false);
            return string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase)
                ? InstallerSidecarState.Match
                : InstallerSidecarState.Mismatch;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            return InstallerSidecarState.Mismatch;
        }
    }

    /// <summary>
    /// Synchronous local-trust check used by the offline repair scan. A missing
    /// sidecar keeps the historical "installer from another launcher" behavior;
    /// a sidecar that mismatches rejects the file (the tampering signal).
    /// </summary>
    internal static bool IsTrustedLocalInstaller(string path)
    {
        if (!IsZip(path)) return false;

        var sidecar = SidecarPath(path);
        if (!File.Exists(sidecar)) return true;

        try
        {
            var expected = LoaderPaths.NormalizeSha1(File.ReadAllText(sidecar));
            if (expected is null) return false;
            return string.Equals(
                LoaderPaths.Sha1File(path),
                expected,
                StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>
    /// Persists the digest of the bytes that were actually accepted. Normal
    /// file permissions (no secrets); a failure to write is not fatal because
    /// the in-memory verification already happened.
    /// </summary>
    private static async Task WriteSidecarAsync(
        string installerPath,
        string sha1,
        CancellationToken cancellationToken)
    {
        var normalized = LoaderPaths.NormalizeSha1(sha1);
        if (normalized is null) return;

        var sidecar = SidecarPath(installerPath);
        var temporary = sidecar + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllTextAsync(
                    temporary,
                    normalized + "\n",
                    cancellationToken)
                .ConfigureAwait(false);
            File.Move(temporary, sidecar, overwrite: true);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            // The installer is still verified; the next run re-derives the
            // digest from the published descriptor.
        }
        finally
        {
            TryDeleteFile(temporary);
        }
    }

    private static void DeleteInstaller(string path)
    {
        TryDeleteFile(path);
        // Keep a mismatching sidecar when the installer could not be removed:
        // the sidecar then keeps rejecting the file on later scans. Removing
        // it first would turn the leftover installer back into "legacy".
        if (!File.Exists(path)) TryDeleteFile(SidecarPath(path));
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
