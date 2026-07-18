namespace HardwareVision.Models;

public sealed record ThemeDescriptor(
    AppTheme Theme,
    string DisplayName,
    string Description,
    IReadOnlyList<string> PreviewColors);
