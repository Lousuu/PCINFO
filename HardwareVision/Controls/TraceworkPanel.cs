using System.Windows;
using System.Windows.Controls;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using VerticalAlignment = System.Windows.VerticalAlignment;

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

    public static readonly DependencyProperty BadgeMinWidthProperty = DependencyProperty.Register(
        nameof(BadgeMinWidth),
        typeof(double),
        typeof(TraceworkPanel),
        new PropertyMetadata(0d));

    public static readonly DependencyProperty BadgeHorizontalAlignmentProperty = DependencyProperty.Register(
        nameof(BadgeHorizontalAlignment),
        typeof(HorizontalAlignment),
        typeof(TraceworkPanel),
        new PropertyMetadata(HorizontalAlignment.Right));

    public static readonly DependencyProperty BadgeVerticalAlignmentProperty = DependencyProperty.Register(
        nameof(BadgeVerticalAlignment),
        typeof(VerticalAlignment),
        typeof(TraceworkPanel),
        new PropertyMetadata(VerticalAlignment.Center));

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

    public double BadgeMinWidth
    {
        get => (double)GetValue(BadgeMinWidthProperty);
        set => SetValue(BadgeMinWidthProperty, value);
    }

    public HorizontalAlignment BadgeHorizontalAlignment
    {
        get => (HorizontalAlignment)GetValue(BadgeHorizontalAlignmentProperty);
        set => SetValue(BadgeHorizontalAlignmentProperty, value);
    }

    public VerticalAlignment BadgeVerticalAlignment
    {
        get => (VerticalAlignment)GetValue(BadgeVerticalAlignmentProperty);
        set => SetValue(BadgeVerticalAlignmentProperty, value);
    }

    public TraceworkPanelVariant PanelVariant
    {
        get => (TraceworkPanelVariant)GetValue(PanelVariantProperty);
        set => SetValue(PanelVariantProperty, value);
    }
}
