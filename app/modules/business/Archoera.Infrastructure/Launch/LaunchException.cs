namespace Archoera.Infrastructure.Launch;

internal sealed class LaunchException : Exception
{
    public LaunchException(string code, string message) : base(message) => Code = code;
    public string Code { get; }
}
