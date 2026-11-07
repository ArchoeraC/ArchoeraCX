using Archoera.Application.Modules;

namespace Archoera.Infrastructure.Launch;

/// <summary>
/// Stable stage numbers and tokens for the <c>launch.start</c> task. The UI
/// localizes the numbers and the error envelope reuses the tokens, so neither
/// side has to parse the human-readable messages produced by lower layers.
/// </summary>
internal static class LaunchProgress
{
    public const int Total = 8;

    public const int Instance = 1;
    public const int Account = 2;
    public const int Version = 3;
    public const int Java = 4;
    public const int Files = 5;
    public const int Repair = 6;
    public const int Plan = 7;
    public const int Process = 8;

    public const string StageInstance = "instance";
    public const string StageAccount = "account";
    public const string StageVersion = "version";
    public const string StageJava = "java";
    public const string StageFiles = "files";
    public const string StageRepair = "repair";
    public const string StagePlan = "plan";
    public const string StageProcess = "process";

    public static void Report(int stage) => ModuleProgress.Report(stage, Total);
}
