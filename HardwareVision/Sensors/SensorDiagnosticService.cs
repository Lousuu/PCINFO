using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using HardwareVision.Utilities;
using LibreHardwareMonitor.Hardware;

namespace HardwareVision.Sensors;

public sealed class SensorDiagnosticService
{
	private readonly SemaphoreSlim diagnosticLock = new SemaphoreSlim(1, 1);

	public string LogsDirectory { get; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "HardwareVision", "logs");


	public string DiagnosticFilePath => Path.Combine(LogsDirectory, "sensor-diagnostic.txt");

	public string OfficialComparisonFilePath => Path.Combine(LogsDirectory, "librehardwaremonitor-official-comparison.txt");

	public async Task<string> ExportAsync(CancellationToken cancellationToken = default(CancellationToken))
	{
		await diagnosticLock.WaitAsync(cancellationToken);
		try
		{
			return await Task.Run(() => ExportCore(cancellationToken), cancellationToken);
		}
		finally
		{
			diagnosticLock.Release();
		}
	}

	public async Task<string> ExportOfficialComparisonAsync(CancellationToken cancellationToken = default(CancellationToken))
	{
		await diagnosticLock.WaitAsync(cancellationToken);
		try
		{
			return await Task.Run(() => ExportOfficialComparisonCore(cancellationToken), cancellationToken);
		}
		finally
		{
			diagnosticLock.Release();
		}
	}

	private string ExportCore(CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();
		Directory.CreateDirectory(LogsDirectory);
		StringBuilder stringBuilder = new StringBuilder();
		stringBuilder.AppendLine("HardwareVision Sensor Diagnostic");
		stringBuilder.Append("Generated: ");
		stringBuilder.AppendLine(DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture));
		stringBuilder.AppendLine("Provider: LibreHardwareMonitor");
		stringBuilder.AppendLine();
		Computer computer = CreateComputer();
		try
		{
			AppendRuntimeInfo(stringBuilder, computer);
			stringBuilder.AppendLine();
			computer.Open();
			stringBuilder.AppendLine("Computer.Open: OK");
			UpdateVisitor.WarmUp(computer, cancellationToken);
			stringBuilder.AppendLine("Warmup: OK");
			stringBuilder.Append("Hardware.Count: ");
			stringBuilder.AppendLine(computer.Hardware.Count.ToString(CultureInfo.InvariantCulture));
			stringBuilder.AppendLine();
			foreach (IHardware item in computer.Hardware)
			{
				AppendHardware(stringBuilder, item, 0, cancellationToken);
			}
		}
		catch (Exception ex) when (!(ex is OperationCanceledException))
		{
			stringBuilder.AppendLine("Computer.Open or hardware traversal failed.");
			stringBuilder.AppendLine(FormatException(ex));
			AppLogger.LogError("Sensor diagnostic export failed while opening or traversing LibreHardwareMonitor.", ex, "sensor-diagnostic:" + ex.GetType().FullName, TimeSpan.FromMinutes(5.0));
		}
		finally
		{
			TryClose(computer);
		}
		File.WriteAllText(DiagnosticFilePath, stringBuilder.ToString(), Encoding.UTF8);
		AppLogger.LogKeyEvent("Sensor diagnostic exported: " + DiagnosticFilePath);
		return DiagnosticFilePath;
	}

	private string ExportOfficialComparisonCore(CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();
		Directory.CreateDirectory(LogsDirectory);
		StringBuilder stringBuilder = new StringBuilder();
		stringBuilder.AppendLine("HardwareVision LibreHardwareMonitor Official Comparison");
		stringBuilder.Append("Generated: ");
		stringBuilder.AppendLine(DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture));
		stringBuilder.AppendLine();
		stringBuilder.AppendLine("Current Process");
		stringBuilder.Append("  IsAdministrator: ");
		stringBuilder.AppendLine(SensorRuntimeDiagnostics.FormatBoolean(SensorRuntimeDiagnostics.IsAdministrator()));
		stringBuilder.Append("  ProcessArchitecture: ");
		stringBuilder.AppendLine(SensorRuntimeDiagnostics.GetProcessArchitecture());
		stringBuilder.Append("  Environment.Is64BitProcess: ");
		stringBuilder.AppendLine(SensorRuntimeDiagnostics.FormatBoolean(Environment.Is64BitProcess));
		stringBuilder.Append("  AppContext.BaseDirectory: ");
		stringBuilder.AppendLine(AppContext.BaseDirectory);
		stringBuilder.AppendLine();
		stringBuilder.AppendLine("Loaded LibreHardwareMonitorLib");
		stringBuilder.Append("  Assembly.GetAssembly(typeof(Computer))?.GetName().Version: ");
		stringBuilder.AppendLine(typeof(Computer).Assembly.GetName().Version?.ToString() ?? "--");
		stringBuilder.Append("  typeof(Computer).Assembly.Location: ");
		stringBuilder.AppendLine(SensorRuntimeDiagnostics.GetLibreHardwareMonitorLocation());
		stringBuilder.Append("  typeof(Computer).Assembly.FullName: ");
		stringBuilder.AppendLine(SensorRuntimeDiagnostics.GetLibreHardwareMonitorFullName());
		stringBuilder.Append("  LoadedAssemblyDescription: ");
		stringBuilder.AppendLine(SensorRuntimeDiagnostics.GetAssemblyDescription(SensorRuntimeDiagnostics.GetLibreHardwareMonitorLocation()));
		stringBuilder.AppendLine();
		string text = SensorRuntimeDiagnostics.FindOfficialLibreHardwareMonitorDirectory();
		string text2 = ((text == null) ? null : Path.Combine(text, "LibreHardwareMonitorLib.dll"));
		stringBuilder.AppendLine("Official LibreHardwareMonitor Directory");
		stringBuilder.Append("  Directory: ");
		stringBuilder.AppendLine(text ?? "--");
		stringBuilder.Append("  OfficialDll: ");
		stringBuilder.AppendLine(text2 ?? "--");
		stringBuilder.Append("  OfficialDllDescription: ");
		stringBuilder.AppendLine(SensorRuntimeDiagnostics.GetAssemblyDescription(text2));
		AppendFileList(stringBuilder, "  OfficialDriverOrNativeFiles", SensorRuntimeDiagnostics.GetDriverOrNativeFiles(text));
		stringBuilder.AppendLine();
		stringBuilder.AppendLine("HardwareVision Output Directory");
		string text3 = Path.Combine(AppContext.BaseDirectory, "LibreHardwareMonitorLib.dll");
		string text4 = Path.Combine(AppContext.BaseDirectory, "runtimes", "win-x64", "lib", "net8.0", "LibreHardwareMonitorLib.dll");
		stringBuilder.Append("  RootDll: ");
		stringBuilder.AppendLine(text3);
		stringBuilder.Append("  RootDllDescription: ");
		stringBuilder.AppendLine(SensorRuntimeDiagnostics.GetAssemblyDescription(text3));
		stringBuilder.Append("  RuntimeWinX64Dll: ");
		stringBuilder.AppendLine(text4);
		stringBuilder.Append("  RuntimeWinX64DllDescription: ");
		stringBuilder.AppendLine(SensorRuntimeDiagnostics.GetAssemblyDescription(text4));
		AppendFileList(stringBuilder, "  OutputDriverOrNativeFiles", SensorRuntimeDiagnostics.GetDriverOrNativeFiles(AppContext.BaseDirectory));
		stringBuilder.AppendLine();
		Computer computer = CreateComputer();
		List<ISensor> list = new List<ISensor>();
		try
		{
			stringBuilder.AppendLine("Initialization");
			AppendRuntimeInfo(stringBuilder, computer);
			stringBuilder.AppendLine();
			computer.Open();
			stringBuilder.AppendLine("Computer.Open: OK");
			UpdateVisitor.WarmUp(computer, cancellationToken);
			stringBuilder.AppendLine("UpdateVisitorWarmup: OK");
			foreach (IHardware item in computer.Hardware)
			{
				CollectCpuTemperatureSensors(item, list, cancellationToken);
			}
		}
		catch (Exception ex) when (!(ex is OperationCanceledException))
		{
			stringBuilder.AppendLine("Computer.Open or CPU temperature traversal failed.");
			stringBuilder.AppendLine(FormatException(ex));
			AppLogger.LogError("Official comparison diagnostic failed while reading CPU temperature sensors.", ex, "official-comparison:" + ex.GetType().FullName, TimeSpan.FromMinutes(5.0));
		}
		finally
		{
			TryClose(computer);
		}
		stringBuilder.AppendLine();
		stringBuilder.AppendLine("CPU Temperature Sensors");
		stringBuilder.Append("  Count: ");
		stringBuilder.AppendLine(list.Count.ToString(CultureInfo.InvariantCulture));
		stringBuilder.Append("  NullValueCount: ");
		stringBuilder.AppendLine(list.Count((ISensor sensor) => !sensor.Value.HasValue).ToString(CultureInfo.InvariantCulture));
		stringBuilder.Append("  AnyDetectedButNull: ");
		stringBuilder.AppendLine(SensorRuntimeDiagnostics.FormatBoolean(list.Count > 0 && list.Any((ISensor sensor) => !sensor.Value.HasValue)));
		foreach (ISensor item2 in list)
		{
			stringBuilder.AppendLine("  Sensor");
			stringBuilder.Append("    Identifier: ");
			stringBuilder.AppendLine(ValueOrFallback(item2.Identifier.ToString()));
			stringBuilder.Append("    Name: ");
			stringBuilder.AppendLine(ValueOrFallback(item2.Name));
			stringBuilder.Append("    Value: ");
			stringBuilder.AppendLine(FormatNullable(item2.Value));
		}
		stringBuilder.AppendLine();
		stringBuilder.AppendLine("Interpretation");
		stringBuilder.Append("  Message: ");
		stringBuilder.AppendLine("LibreHardwareMonitor 官方程序可读取，但当前集成库未返回该值。请检查库版本、驱动文件、权限和运行架构。");
		File.WriteAllText(OfficialComparisonFilePath, stringBuilder.ToString(), Encoding.UTF8);
		AppLogger.LogKeyEvent("Official LibreHardwareMonitor comparison diagnostic exported: " + OfficialComparisonFilePath);
		return OfficialComparisonFilePath;
	}

	private static Computer CreateComputer()
	{
		return new Computer
		{
			IsCpuEnabled = true,
			IsGpuEnabled = true,
			IsMemoryEnabled = true,
			IsMotherboardEnabled = true,
			IsControllerEnabled = true,
			IsStorageEnabled = true,
			IsNetworkEnabled = true,
			IsBatteryEnabled = true,
			IsPowerMonitorEnabled = true
		};
	}

	private static void AppendRuntimeInfo(StringBuilder builder, Computer computer)
	{
		Version version = typeof(Computer).Assembly.GetName().Version;
		builder.Append("IsCpuEnabled: ");
		builder.AppendLine(computer.IsCpuEnabled.ToString(CultureInfo.InvariantCulture));
		builder.Append("IsGpuEnabled: ");
		builder.AppendLine(computer.IsGpuEnabled.ToString(CultureInfo.InvariantCulture));
		builder.Append("IsMemoryEnabled: ");
		builder.AppendLine(computer.IsMemoryEnabled.ToString(CultureInfo.InvariantCulture));
		builder.Append("IsMotherboardEnabled: ");
		builder.AppendLine(computer.IsMotherboardEnabled.ToString(CultureInfo.InvariantCulture));
		builder.Append("IsControllerEnabled: ");
		builder.AppendLine(computer.IsControllerEnabled.ToString(CultureInfo.InvariantCulture));
		builder.Append("IsStorageEnabled: ");
		builder.AppendLine(computer.IsStorageEnabled.ToString(CultureInfo.InvariantCulture));
		builder.Append("IsNetworkEnabled: ");
		builder.AppendLine(computer.IsNetworkEnabled.ToString(CultureInfo.InvariantCulture));
		builder.Append("IsBatteryEnabled: ");
		builder.AppendLine(computer.IsBatteryEnabled.ToString(CultureInfo.InvariantCulture));
		builder.Append("IsPowerMonitorEnabled: ");
		builder.AppendLine(computer.IsPowerMonitorEnabled.ToString(CultureInfo.InvariantCulture));
		builder.Append("UpdateVisitorUsed: ");
		builder.AppendLine(bool.TrueString);
		builder.Append("WarmupRefreshCount: ");
		builder.AppendLine(3.ToString(CultureInfo.InvariantCulture));
		builder.Append("WarmupDelayMilliseconds: ");
		builder.AppendLine(300.ToString(CultureInfo.InvariantCulture));
		builder.Append("ProcessElevated: ");
		builder.AppendLine(SensorRuntimeDiagnostics.IsAdministrator().ToString(CultureInfo.InvariantCulture));
		builder.Append("LibreHardwareMonitorLibVersion: ");
		builder.AppendLine(version?.ToString() ?? "--");
		builder.Append("LibreHardwareMonitorLibLocation: ");
		builder.AppendLine(SensorRuntimeDiagnostics.GetLibreHardwareMonitorLocation());
		builder.Append("LibreHardwareMonitorLibFullName: ");
		builder.AppendLine(SensorRuntimeDiagnostics.GetLibreHardwareMonitorFullName());
		builder.Append("ProcessArchitecture: ");
		builder.AppendLine(SensorRuntimeDiagnostics.GetProcessArchitecture());
		builder.Append("Environment.Is64BitProcess: ");
		builder.AppendLine(Environment.Is64BitProcess.ToString(CultureInfo.InvariantCulture));
	}

	private static void AppendHardware(StringBuilder builder, IHardware hardware, int depth, CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();
		string value = new string(' ', depth * 2);
		IList<ISensor> sensors = hardware.Sensors;
		IList<IHardware> subHardware = hardware.SubHardware;
		builder.Append(value);
		builder.AppendLine("Hardware");
		builder.Append(value);
		builder.Append("  HardwareType: ");
		builder.AppendLine(hardware.HardwareType.ToString());
		builder.Append(value);
		builder.Append("  Name: ");
		builder.AppendLine(ValueOrFallback(hardware.Name));
		builder.Append(value);
		builder.Append("  Identifier: ");
		builder.AppendLine(ValueOrFallback(hardware.Identifier.ToString()));
		builder.Append(value);
		builder.Append("  Sensors.Count: ");
		builder.AppendLine(sensors.Count.ToString(CultureInfo.InvariantCulture));
		builder.Append(value);
		builder.AppendLine("  Update: UpdateVisitor");
		foreach (ISensor item in sensors)
		{
			AppendSensor(builder, item, depth + 1);
		}
		foreach (IHardware item2 in subHardware)
		{
			AppendHardware(builder, item2, depth + 1, cancellationToken);
		}
		builder.AppendLine();
	}

	private static void AppendSensor(StringBuilder builder, ISensor sensor, int depth)
	{
		string value = new string(' ', depth * 2);
		builder.Append(value);
		builder.AppendLine("Sensor");
		builder.Append(value);
		builder.Append("  SensorType: ");
		builder.AppendLine(sensor.SensorType.ToString());
		builder.Append(value);
		builder.Append("  Name: ");
		builder.AppendLine(ValueOrFallback(sensor.Name));
		builder.Append(value);
		builder.Append("  Identifier: ");
		builder.AppendLine(ValueOrFallback(sensor.Identifier.ToString()));
		builder.Append(value);
		builder.Append("  Value: ");
		builder.AppendLine(FormatNullable(sensor.Value));
		builder.Append(value);
		builder.Append("  Min: ");
		builder.AppendLine(FormatNullable(sensor.Min));
		builder.Append(value);
		builder.Append("  Max: ");
		builder.AppendLine(FormatNullable(sensor.Max));
		builder.Append(value);
		builder.Append("  Status: ");
		builder.AppendLine(sensor.Value.HasValue ? "Reported" : "NotReported");
	}

	private static string FormatNullable(float? value)
	{
		return value.HasValue ? value.Value.ToString("0.###", CultureInfo.InvariantCulture) : "--";
	}

	private static string ValueOrFallback(string? value)
	{
		return string.IsNullOrWhiteSpace(value) ? "--" : value.Trim();
	}

	private static string FormatException(Exception exception)
	{
		return exception.GetType().FullName + ": " + exception.Message;
	}

	private static void CollectCpuTemperatureSensors(IHardware hardware, ICollection<ISensor> sensors, CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();
		if (hardware.HardwareType == HardwareType.Cpu)
		{
			foreach (ISensor item in hardware.Sensors.Where((ISensor sensor) => sensor.SensorType == SensorType.Temperature))
			{
				sensors.Add(item);
			}
		}
		IHardware[] subHardware = hardware.SubHardware;
		foreach (IHardware hardware2 in subHardware)
		{
			CollectCpuTemperatureSensors(hardware2, sensors, cancellationToken);
		}
	}

	private static void AppendFileList(StringBuilder builder, string title, IReadOnlyList<string> files)
	{
		builder.Append(title);
		builder.Append(".Count: ");
		builder.AppendLine(files.Count.ToString(CultureInfo.InvariantCulture));
		foreach (string file in files)
		{
			builder.Append("    ");
			builder.Append(Path.GetFileName(file));
			builder.Append(" | ");
			builder.AppendLine(file);
		}
	}

	private static void TryClose(Computer computer)
	{
		try
		{
			computer.Close();
		}
		catch (Exception ex) when (!(ex is OperationCanceledException))
		{
			AppLogger.LogError("Sensor diagnostic computer close failed.", ex, "sensor-diagnostic-close:" + ex.GetType().FullName, TimeSpan.FromMinutes(5.0));
		}
	}
}
