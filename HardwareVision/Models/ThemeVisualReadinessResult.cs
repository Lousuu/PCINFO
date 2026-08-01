namespace HardwareVision.Models;

public sealed record ThemeVisualReadinessResult(
    bool IsReady,
    string FailureReason,
    int RenderPassCount)
{
    public static ThemeVisualReadinessResult Ready(int renderPassCount) =>
        new(true, string.Empty, renderPassCount);

    public static ThemeVisualReadinessResult Failed(
        string failureReason,
        int renderPassCount) =>
        new(
            false,
            string.IsNullOrWhiteSpace(failureReason)
                ? "Theme visual state validation failed."
                : failureReason.Trim(),
            renderPassCount);
}
