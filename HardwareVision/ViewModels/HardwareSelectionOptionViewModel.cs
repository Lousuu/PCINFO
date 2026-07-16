using CommunityToolkit.Mvvm.ComponentModel;

namespace HardwareVision.ViewModels;

public sealed class HardwareSelectionOptionViewModel : ObservableObject
{
    private string id = string.Empty;
    private string displayName = "--";
    private string fullName = "--";
    private string? toolTip;

    public string Id
    {
        get => id;
        private set => SetProperty(ref id, value);
    }

    public string DisplayName
    {
        get => displayName;
        private set => SetProperty(ref displayName, value);
    }

    public string FullName
    {
        get => fullName;
        private set => SetProperty(ref fullName, value);
    }

    public string? ToolTip
    {
        get => toolTip;
        private set => SetProperty(ref toolTip, value);
    }

    public void Update(string optionId, string name, int maxDisplayLength = 28)
    {
        string normalizedName = string.IsNullOrWhiteSpace(name) ? "--" : name.Trim();
        Id = string.IsNullOrWhiteSpace(optionId) ? normalizedName : optionId.Trim();
        FullName = normalizedName;
        DisplayName = Ellipsize(normalizedName, maxDisplayLength);
        ToolTip = ViewModelHelpers.NullIfShortOrSame(DisplayName, FullName, maxDisplayLength);
    }

    public override string ToString()
    {
        return DisplayName;
    }

    private static string Ellipsize(string value, int maxLength)
    {
        if (value.Length <= maxLength)
        {
            return value;
        }

        return string.Concat(value.AsSpan(0, Math.Max(1, maxLength - 3)), "...");
    }
}
