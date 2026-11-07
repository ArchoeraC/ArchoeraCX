namespace Archoera.Infrastructure.Launch;

internal sealed class LaunchException : Exception
{
    public LaunchException(
        string code,
        string message,
        int? requiredJavaMajor = null) : base(message)
    {
        Code = code;
        RequiredJavaMajor = requiredJavaMajor;
    }

    public string Code { get; }

    /// <summary>
    /// Major Java version the instance needs when <see cref="Code"/> is
    /// JAVA_MISSING or JAVA_VERSION_MISMATCH. The UI uses it to jump straight
    /// to the matching runtime installer instead of asking the user to guess.
    /// </summary>
    public int? RequiredJavaMajor { get; }
}
