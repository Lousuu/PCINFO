using HardwareVision.Models;

namespace HardwareVision.Services;

internal static class GameProcessScorer
{
    private const int LikelyGameThreshold = 60;
    private const int HighConfidenceThreshold = 75;
    private const int MinimumScoreLead = 18;
    private static readonly TimeSpan RecentForegroundLifetime = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan RecentProcessLifetime = TimeSpan.FromHours(6);

    private static readonly string[] SoftPenaltyTokens =
    [
        "launcher",
        "bootstrap",
        "updater",
        "patcher",
        "helper",
        "service",
        "crash",
        "reporter",
        "webhelper",
        "overlay"
    ];

    private static readonly HashSet<string> StrongNonGameProcessNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "explorer",
        "dwm",
        "searchhost",
        "startmenuexperiencehost",
        "shellexperiencehost",
        "applicationframehost",
        "textinputhost",
        "taskmgr",
        "cmd",
        "powershell",
        "pwsh",
        "windowsterminal",
        "devenv",
        "rider64",
        "code",
        "chrome",
        "msedge",
        "firefox",
        "steamwebhelper",
        "epicwebhelper",
        "discord",
        "chatgpt",
        "codex"
    };

    private static readonly HashSet<string> CoreSystemProcessNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "idle",
        "system",
        "registry",
        "smss",
        "csrss",
        "wininit",
        "services",
        "lsass",
        "svchost"
    };

    public static IReadOnlyList<GameProcessDetectionResult> ScoreAndSort(
        IEnumerable<GameProcessInfo> processes,
        ForegroundProcessSnapshot? foreground,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(processes);

        return processes
            .Select(process => Score(process, foreground, now))
            .OrderBy(result => GetSortGroup(result))
            .ThenByDescending(result => result.Score)
            .ThenBy(result => result.Process.ProcessName, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(result => result.Process.ProcessId)
            .ToArray();
    }

    public static GameProcessDetectionDecision ChooseHighConfidence(
        IReadOnlyList<GameProcessDetectionResult> results)
    {
        ArgumentNullException.ThrowIfNull(results);

        GameProcessDetectionResult[] likely = results
            .Where(result => result.Process.IsRunning && result.IsLikelyGame)
            .OrderByDescending(result => result.Score)
            .ThenBy(result => result.Process.ProcessName, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(result => result.Process.ProcessId)
            .ToArray();
        if (likely.Length == 0)
        {
            return new GameProcessDetectionDecision();
        }

        GameProcessDetectionResult first = likely[0];
        int lead = likely.Length == 1 ? int.MaxValue : first.Score - likely[1].Score;
        bool ambiguous = lead < MinimumScoreLead;
        bool highConfidence = first.IsRecentForeground || first.Score >= HighConfidenceThreshold;

        return new GameProcessDetectionDecision
        {
            Selection = highConfidence && !ambiguous ? first : null,
            HasLikelyCandidates = true,
            IsAmbiguous = ambiguous
        };
    }

    public static bool IsCoreSystemProcess(string processName)
    {
        return CoreSystemProcessNames.Contains(NormalizeProcessName(processName));
    }

    private static GameProcessDetectionResult Score(
        GameProcessInfo process,
        ForegroundProcessSnapshot? foreground,
        DateTimeOffset now)
    {
        int score = 0;
        List<string> reasons = new();
        string processName = NormalizeProcessName(process.ProcessName);
        string combinedText = $"{processName} {process.FilePath}";
        bool isStronglyNonGame = StrongNonGameProcessNames.Contains(processName);
        bool isRecentForeground = IsRecentForeground(process, foreground, now);

        if (process.IsRunning)
        {
            score += 5;
        }
        else
        {
            score -= 200;
        }

        if (isRecentForeground)
        {
            score += 100;
            reasons.Add("最近前台程序");
        }

        if (!string.IsNullOrWhiteSpace(process.WindowTitle))
        {
            score += 25;
            reasons.Add("具有主窗口标题");
        }

        if (process.HasVisibleMainWindow)
        {
            score += 15;
            if (!reasons.Contains("具有主窗口标题", StringComparer.Ordinal))
            {
                reasons.Add("具有可见主窗口");
            }
        }

        if (processName.Contains("win64-shipping", StringComparison.OrdinalIgnoreCase)
            || processName.Contains("win32-shipping", StringComparison.OrdinalIgnoreCase))
        {
            score += 30;
            reasons.Add("Shipping 游戏进程");
        }

        if (IsGameInstallationPath(process.FilePath))
        {
            score += 25;
            reasons.Add("位于游戏目录");
        }

        if (process.StartTimeUtc is DateTimeOffset startTime
            && startTime <= now.AddSeconds(2)
            && now - startTime <= RecentProcessLifetime)
        {
            score += 10;
            reasons.Add("近期启动");
        }

        if (SoftPenaltyTokens.Any(token => combinedText.Contains(token, StringComparison.OrdinalIgnoreCase)))
        {
            score -= 35;
            reasons.Add("包含启动器或辅助程序特征（已降权）");
        }

        if (isStronglyNonGame)
        {
            score -= 140;
            reasons.Add("常见非游戏程序（已降权）");
        }

        bool isLikelyGame = process.IsRunning && !isStronglyNonGame && score >= LikelyGameThreshold;
        string reason = reasons.Count == 0 ? "无明显游戏特征" : string.Join("、", reasons);
        GameProcessInfo scoredProcess = CopyWithDetection(process, score, isLikelyGame, reason, isRecentForeground);

        return new GameProcessDetectionResult
        {
            Process = scoredProcess,
            Score = score,
            IsLikelyGame = isLikelyGame,
            IsRecentForeground = isRecentForeground,
            IsStronglyNonGame = isStronglyNonGame,
            Reason = reason
        };
    }

    private static bool IsRecentForeground(
        GameProcessInfo process,
        ForegroundProcessSnapshot? foreground,
        DateTimeOffset now)
    {
        if (foreground is null
            || !process.IsRunning
            || process.ProcessId != foreground.ProcessId
            || foreground.ObservedAtUtc > now.AddSeconds(5)
            || now - foreground.ObservedAtUtc > RecentForegroundLifetime)
        {
            return false;
        }

        return process.StartTimeUtc is not DateTimeOffset startTime
            || startTime <= foreground.ObservedAtUtc.AddSeconds(2);
    }

    private static bool IsGameInstallationPath(string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return false;
        }

        string path = filePath.Replace('/', '\\');
        return path.Contains("\\steamapps\\common\\", StringComparison.OrdinalIgnoreCase)
            || path.Contains("\\epic games\\", StringComparison.OrdinalIgnoreCase)
            || path.Contains("\\xboxgames\\", StringComparison.OrdinalIgnoreCase)
            || path.Contains("\\gog games\\", StringComparison.OrdinalIgnoreCase)
            || path.Contains("\\games\\", StringComparison.OrdinalIgnoreCase);
    }

    private static int GetSortGroup(GameProcessDetectionResult result)
    {
        if (result.IsRecentForeground)
        {
            return 0;
        }

        if (result.IsLikelyGame)
        {
            return 1;
        }

        if (result.Process.HasVisibleMainWindow || !string.IsNullOrWhiteSpace(result.Process.WindowTitle))
        {
            return 2;
        }

        return 3;
    }

    private static string NormalizeProcessName(string processName)
    {
        string value = processName.Trim();
        return value.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? value[..^4]
            : value;
    }

    private static GameProcessInfo CopyWithDetection(
        GameProcessInfo process,
        int score,
        bool isLikelyGame,
        string reason,
        bool isRecentForeground)
    {
        return new GameProcessInfo
        {
            ProcessId = process.ProcessId,
            ProcessName = process.ProcessName,
            DisplayName = process.DisplayName,
            WindowTitle = process.WindowTitle,
            FilePath = process.FilePath,
            StartTimeUtc = process.StartTimeUtc,
            IsRunning = process.IsRunning,
            HasVisibleMainWindow = process.HasVisibleMainWindow,
            DetectionScore = score,
            IsLikelyGame = isLikelyGame,
            DetectionReason = reason,
            IsRecentForeground = isRecentForeground
        };
    }
}

internal static class GameProcessSelectionPolicy
{
    public static bool CanAutoSelect(
        GameCaptureState captureState,
        bool hasValidUserSelection,
        bool hasSearchText)
    {
        return !IsCaptureTargetLocked(captureState)
            && !hasValidUserSelection
            && !hasSearchText;
    }

    public static bool IsCaptureTargetLocked(GameCaptureState captureState)
    {
        return captureState is GameCaptureState.Preparing
            or GameCaptureState.WaitingForFirstFrame
            or GameCaptureState.Capturing
            or GameCaptureState.Stopping;
    }

    public static bool IsSameProcess(GameProcessInfo expected, GameProcessInfo candidate)
    {
        if (expected.ProcessId != candidate.ProcessId
            || !string.Equals(expected.ProcessName, candidate.ProcessName, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (expected.StartTimeUtc is DateTimeOffset expectedStart
            && candidate.StartTimeUtc is DateTimeOffset candidateStart
            && Math.Abs((expectedStart - candidateStart).TotalSeconds) > 2)
        {
            return false;
        }

        return string.IsNullOrWhiteSpace(expected.FilePath)
            || string.IsNullOrWhiteSpace(candidate.FilePath)
            || string.Equals(expected.FilePath, candidate.FilePath, StringComparison.OrdinalIgnoreCase);
    }
}
