using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Security.Principal;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HardwareVision.Models;
using HardwareVision.Sensors;
using HardwareVision.Services;
using HardwareVision.Utilities;
using static HardwareVision.ViewModels.ViewModelHelpers;

namespace HardwareVision.ViewModels;

public sealed class NavigationItemViewModel : ObservableObject
{
    private bool isEnabled = true;

    public NavigationItemViewModel(string key, string title, string subtitle, object page)
    {
        Key = key;
        Title = title;
        Subtitle = subtitle;
        Page = page;
    }

    public string Key { get; }

    public string Title { get; }

    public string Subtitle { get; }

    public object Page { get; }

    public bool IsEnabled
    {
        get => isEnabled;
        set => SetProperty(ref isEnabled, value);
    }
}
