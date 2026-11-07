using Archoera.Domain.Loaders;

namespace Archoera.Application.Loaders;

/// <summary>
/// Checks a manifest against a directory tree without writing anything.
/// </summary>
public interface ILoaderArtifactVerifier
{
    /// <summary>
    /// Verifies existence, containment, optional size and optional SHA-1 for
    /// every artifact. Paths escaping the root/libraries base are reported in
    /// <see cref="LoaderVerification.Unsafe"/> and never opened.
    /// </summary>
    LoaderVerification Verify(
        LoaderArtifactManifest manifest,
        string root,
        LoaderVerifyMode mode = LoaderVerifyMode.Full);
}
