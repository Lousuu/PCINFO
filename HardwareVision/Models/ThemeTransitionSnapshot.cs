namespace HardwareVision.Models;

public sealed record ThemeTransitionSnapshot(
    long Version,
    ThemeTransitionPhase Phase,
    AppTheme SourceTheme,
    AppTheme TargetTheme,
    ThemeTransitionPlan Plan,
    bool IsActive,
    bool IsInteractionBlocked,
    bool WasThemeCommitted,
    ThemeTransitionStatus? TerminalStatus,
    string? FailureMessage)
{
    public static ThemeTransitionSnapshot Idle(AppTheme currentTheme) => new(
        Version: 0L,
        Phase: ThemeTransitionPhase.Idle,
        SourceTheme: currentTheme,
        TargetTheme: currentTheme,
        Plan: ThemeTransitionPlan.Immediate(MotionLevel.Off),
        IsActive: false,
        IsInteractionBlocked: false,
        WasThemeCommitted: true,
        TerminalStatus: null,
        FailureMessage: null);

    public string SourceCode => ToThemeCode(SourceTheme);

    public string TargetCode => ToThemeCode(TargetTheme);

    public string PhaseCode => Phase switch
    {
        ThemeTransitionPhase.Trace => "TRACE",
        ThemeTransitionPhase.Latch => "LATCH",
        ThemeTransitionPhase.Splice => "SPLICE",
        ThemeTransitionPhase.Failed => "FAULT",
        _ => "IDLE"
    };

    public string StatusText
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(FailureMessage))
            {
                return FailureMessage!;
            }

            return Phase switch
            {
                ThemeTransitionPhase.Trace => "Tracing theme rails",
                ThemeTransitionPhase.Latch => "Latching palette core",
                ThemeTransitionPhase.Splice => "Splicing interface signal",
                ThemeTransitionPhase.Failed => "Theme rewire failed",
                _ => "Theme system idle"
            };
        }
    }

    private static string ToThemeCode(AppTheme theme) => theme switch
    {
        AppTheme.Tracework => "TRACEWORK",
        _ => "CLASSIC"
    };
}
