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

public sealed class NetworkAdapterItemViewModel : ObservableObject
{
    public NetworkAdapterDevice Device { get; init; } = new();

    public string Name => ViewModelHelpers.FirstAvailable(Device.Name, Device.Description, "--")!;

    public string Subtitle => ViewModelHelpers.FirstAvailable(Device.IPv4Addresses.FirstOrDefault(), Device.Description, "--")!;

    public string DisplayType => ViewModelHelpers.ResolveNetworkType(Device);

    public string StatusText => Device.IsUp ? "已连接" : "未连接";

    public ObservableCollection<DetailMetricViewModel> Metrics { get; } = new();
}
