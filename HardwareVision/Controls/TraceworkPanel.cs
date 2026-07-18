using System.Windows;
using System.Windows.Controls;

namespace HardwareVision.Controls;

public enum TraceworkPanelVariant
{
    Default,
    Primary,
    Muted
}

public sealed class TraceworkPanel : HeaderedContentControl
{
    public static readonly DependencyProperty CodeProperty = DependencyProperty.Register(
        nameof(Code),
        typeof(string),
        typeof(TraceworkPanel),
        new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(
        nameof(Title),
        typeof(string),
        typeof(TraceworkPanel),
        new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty SubtitleProperty = DependencyProperty.Register(
        nameof(Subtitle),
        typeof(string),
        typeof(TraceworkPanel),
        new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty HeaderNoteProperty = DependencyProperty.Register(
        nameof(HeaderNote),
        typeof(string),
        typeof(TraceworkPanel),
        new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty BadgeTextProperty = DependencyProperty.Register(
        nameof(BadgeText),
        typeof(string),
        typeof(TraceworkPanel),
        new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty PanelVariantProperty = DependencyProperty.Register(
        nameof(PanelVariant),
        typeof(TraceworkPanelVariant),
        typeof(TraceworkPanel),
        new PropertyMetadata(TraceworkPanelVariant.Default));

    public string Code
    {
        get => (string)GetValue(CodeProperty);
        set => SetValue(CodeProperty, value);
    }

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string Subtitle
    {
        get => (string)GetValue(SubtitleProperty);
        set => SetValue(SubtitleProperty, value);
    }

    public string HeaderNote
    {
        get => (string)GetValue(HeaderNoteProperty);
        set => SetValue(HeaderNoteProperty, value);
    }

    public string BadgeText
    {
        get => (string)GetValue(BadgeTextProperty);
        set => SetValue(BadgeTextProperty, value);
    }

    public TraceworkPanelVariant PanelVariant
    {
        get => (TraceworkPanelVariant)GetValue(PanelVariantProperty);
        set => SetValue(PanelVariantProperty, value);
    }
}
