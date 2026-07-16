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

public sealed class MemoryModuleViewModel : ObservableObject
{
    public string ModuleName { get; init; } = "--";

    public string SlotName { get; init; } = "--";

    public ObservableCollection<DetailMetricViewModel> Metrics { get; } = new();
}
