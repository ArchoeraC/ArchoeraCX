namespace Archoera.Application.Loaders;

/// <summary>
/// Artifact verification depth. <c>Quick</c> checks existence/size and hashes
/// only generated launch artifacts; <c>Full</c> hashes every hashed artifact.
/// </summary>
public enum LoaderVerifyMode
{
    Quick,
    Full,
}

public static class LoaderVerifyModeExtensions
{
    public static string ToWire(this LoaderVerifyMode mode)
        => mode == LoaderVerifyMode.Quick ? "quick" : "full";

    public static bool TryParse(string? value, out LoaderVerifyMode mode)
    {
        if (string.Equals(value, "quick", StringComparison.OrdinalIgnoreCase))
        {
            mode = LoaderVerifyMode.Quick;
            return true;
        }

        if (string.IsNullOrWhiteSpace(value) ||
            string.Equals(value, "full", StringComparison.OrdinalIgnoreCase))
        {
            mode = LoaderVerifyMode.Full;
            return true;
        }

        mode = LoaderVerifyMode.Full;
        return false;
    }
}
