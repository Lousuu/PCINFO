namespace HardwareVision.Models;

public sealed record StartupPhasePresentation(
    int StepNumber,
    int StepCount,
    string PhaseCode,
    string ChineseLabel,
    int CompletedStepCount,
    int CurrentStepIndex,
    bool IsFailure,
    string FailureText)
{
    public string DisplayText => IsFailure
        ? FailureText
        : $"{StepNumber:00} / {StepCount:00}  {ChineseLabel}";

    public static StartupPhasePresentation? Create(
        StartupSequencePhase phase,
        string? failureMessage)
    {
        if (phase is StartupSequencePhase.Dormant or StartupSequencePhase.Complete)
        {
            return null;
        }

        (int step, string code, string label) = phase switch
        {
            StartupSequencePhase.Index => (1, "INDEX", "构建系统索引"),
            StartupSequencePhase.Route => (2, "ROUTE", "接通核心服务"),
            StartupSequencePhase.Bind => (3, "BIND", "建立硬件信号路由"),
            StartupSequencePhase.Lock => (4, "LOCK", "锁定首个遥测快照"),
            StartupSequencePhase.Reveal => (5, "REVEAL", "提交主界面"),
            _ => throw new ArgumentOutOfRangeException(nameof(phase))
        };
        bool isFailure = !string.IsNullOrWhiteSpace(failureMessage);
        return new StartupPhasePresentation(
            step,
            5,
            isFailure ? "FAILED" : code,
            label,
            step - 1,
            step - 1,
            isFailure,
            isFailure ? $"启动降级：{failureMessage}" : string.Empty);
    }
}
