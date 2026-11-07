using System.IO.Compression;
using Archoera.Application.Loaders;
using Archoera.Domain.Loaders;

namespace Archoera.Infrastructure.Loaders;

/// <summary>
/// High-level producer for Forge/NeoForge installer downloads. It resolves the
/// official installer URL, reads the Maven <c>.sha1</c>/size, asks the caller for
/// the installer bytes (already downloaded or opened from disk) and delegates
/// plan construction to <see cref="ForgeDownloadPlanBuilder"/>.
/// </summary>
/// <remarks>
/// The service never writes files and never downloads the installer itself: when
/// <c>openInstaller</c> is null it emits an installer-only plan plus an
/// <c>INSTALLER_METADATA_UNAVAILABLE</c> warning. That keeps the "download the
/// installer, then read it" two-step flow explicit.
/// </remarks>
public sealed class ForgeLoaderPlanService
{
    private readonly MavenSidecarClient _sidecar;

    /// <summary>Creates the service over an injected HTTP transport.</summary>
    public ForgeLoaderPlanService(HttpClient httpClient, string? userAgent = null)
        : this(new MavenSidecarClient(httpClient, userAgent))
    {
    }

    /// <summary>Creates the service over an existing sidecar client.</summary>
    public ForgeLoaderPlanService(MavenSidecarClient sidecar)
    {
        ArgumentNullException.ThrowIfNull(sidecar);
        _sidecar = sidecar;
    }

    /// <summary>
    /// Builds the plan from official installer metadata plus a caller-supplied
    /// installer reader.
    /// </summary>
    /// <param name="root">Absolute instance root.</param>
    /// <param name="kind">Forge or NeoForge.</param>
    /// <param name="gameVersion">Minecraft version.</param>
    /// <param name="loaderVersion">Loader version.</param>
    /// <param name="openInstaller">
    /// Opens the installer bytes for zip parsing. The service disposes the
    /// returned stream. Pass null to plan only the installer jar.
    /// </param>
    /// <param name="installerMirrors">Optional byte-identical installer mirrors.</param>
    /// <param name="mavenBase">Optional mirror root replacing the official Maven host.</param>
    /// <param name="options">Producer knobs; defaults are safe.</param>
    /// <param name="cancellationToken">Cancels metadata requests and parsing.</param>
    /// <exception cref="ForgeDownloadException">
    /// Sidecar/HTTP codes plus <c>INVALID_REQUEST</c>, <c>MISSING_HASH</c>,
    /// <c>MISSING_SIZE</c>, <c>DUPLICATE_DEST</c> or <c>INSTALLER_INVALID</c>.
    /// </exception>
    public async Task<ForgeDownloadPlanResult> BuildAsync(
        string root,
        LoaderKind kind,
        string gameVersion,
        string loaderVersion,
        Func<CancellationToken, Task<Stream>>? openInstaller = null,
        IReadOnlyList<string>? installerMirrors = null,
        Uri? mavenBase = null,
        ForgeDownloadPlanOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var installerUri = ForgeMavenUrls.Installer(
            kind,
            gameVersion,
            loaderVersion,
            mavenBase);

        var metadata = await _sidecar
            .GetMetadataAsync(installerUri, cancellationToken)
            .ConfigureAwait(false);

        var descriptor = new ForgeInstallerDescriptor(
            installerUri.AbsoluteUri,
            metadata.Sha1,
            metadata.Size,
            installerMirrors);

        if (openInstaller is null)
        {
            return ForgeDownloadPlanBuilder.Build(
                root,
                kind,
                gameVersion,
                loaderVersion,
                descriptor,
                archive: null,
                options,
                cancellationToken);
        }

        var stream = await openInstaller(cancellationToken).ConfigureAwait(false);
        if (stream is null)
        {
            throw new ForgeDownloadException(
                ForgeDownloadErrorCode.InstallerMissing,
                $"installer reader returned no stream for {installerUri}");
        }

        try
        {
            using var archive = await OpenArchiveAsync(stream, cancellationToken)
                .ConfigureAwait(false);

            return ForgeDownloadPlanBuilder.Build(
                root,
                kind,
                gameVersion,
                loaderVersion,
                descriptor,
                archive,
                options,
                cancellationToken);
        }
        finally
        {
            await stream.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Offline convenience: plans from a local installer file. The descriptor
    /// hash/size are computed from the file itself; callers that need to verify
    /// against the published Maven sidecar should use <see cref="BuildAsync"/>.
    /// </summary>
    /// <exception cref="ForgeDownloadException">
    /// <c>INSTALLER_MISSING</c>, <c>INSTALLER_INVALID</c> or plan metadata codes.
    /// </exception>
    public async Task<ForgeDownloadPlanResult> BuildFromInstallerPathAsync(
        string root,
        LoaderKind kind,
        string gameVersion,
        string loaderVersion,
        string installerPath,
        Uri? mavenBase = null,
        ForgeDownloadPlanOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(installerPath);

        var installerFull = Path.GetFullPath(installerPath);
        if (!File.Exists(installerFull))
        {
            throw new ForgeDownloadException(
                ForgeDownloadErrorCode.InstallerMissing,
                $"installer jar not found: {installerFull}");
        }

        var installerUri = ForgeMavenUrls.Installer(
            kind,
            gameVersion,
            loaderVersion,
            mavenBase);
        var sha1 = await LoaderPaths
            .Sha1FileAsync(installerFull, cancellationToken)
            .ConfigureAwait(false);
        var size = new FileInfo(installerFull).Length;

        var descriptor = new ForgeInstallerDescriptor(
            installerUri.AbsoluteUri,
            sha1,
            size);

        await using var stream = new FileStream(
            installerFull,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 64 * 1024,
            useAsync: true);

        using var archive = await OpenArchiveAsync(stream, cancellationToken)
            .ConfigureAwait(false);

        return ForgeDownloadPlanBuilder.Build(
            root,
            kind,
            gameVersion,
            loaderVersion,
            descriptor,
            archive,
            options,
            cancellationToken);
    }

    private static async Task<ZipArchive> OpenArchiveAsync(
        Stream stream,
        CancellationToken cancellationToken)
    {
        if (!stream.CanRead)
        {
            throw new ForgeDownloadException(
                ForgeDownloadErrorCode.InstallerInvalid,
                "installer stream is not readable");
        }

        MemoryStream? copy = null;
        Stream source = stream;

        if (!stream.CanSeek)
        {
            copy = new MemoryStream();
            await stream.CopyToAsync(copy, cancellationToken).ConfigureAwait(false);
            copy.Position = 0;
            source = copy;
        }

        try
        {
            return new ZipArchive(source, ZipArchiveMode.Read, leaveOpen: false);
        }
        catch (Exception exception) when (exception is InvalidDataException
                                          or IOException
                                          or NotSupportedException)
        {
            if (copy is not null)
            {
                await copy.DisposeAsync().ConfigureAwait(false);
            }

            throw new ForgeDownloadException(
                ForgeDownloadErrorCode.InstallerInvalid,
                $"installer is not a readable zip/jar: {exception.Message}",
                innerException: exception);
        }
    }
}
