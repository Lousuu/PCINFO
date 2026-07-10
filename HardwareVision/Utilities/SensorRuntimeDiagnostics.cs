using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security;
using System.Security.Cryptography;
using System.Security.Principal;
using LibreHardwareMonitor.Hardware;

namespace HardwareVision.Utilities;

public static class SensorRuntimeDiagnostics
{
	public const string OfficialReadableButIntegratedValueMissingMessage = "LibreHardwareMonitor 官方程序可读取，但当前集成库未返回该值。请检查库版本、驱动文件、权限和运行架构。";

	public static string? FindOfficialLibreHardwareMonitorDirectory()
	{
		string folderPath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
		string[] source = new string[4]
		{
			Environment.GetEnvironmentVariable("HARDWAREVISION_LHM_OFFICIAL_DIR") ?? string.Empty,
			Path.Combine(folderPath, "Downloads", "LibreHardwareMonitor"),
			Path.Combine(folderPath, "Desktop", "LibreHardwareMonitor"),
			Path.Combine(AppContext.BaseDirectory, "LibreHardwareMonitor")
		};
		return source.Where((string candidate) => !string.IsNullOrWhiteSpace(candidate)).FirstOrDefault((string candidate) => File.Exists(Path.Combine(candidate, "LibreHardwareMonitor.exe")) || File.Exists(Path.Combine(candidate, "LibreHardwareMonitorLib.dll")));
	}

	public static string GetLibreHardwareMonitorVersion()
	{
		return typeof(Computer).Assembly.GetName().Version?.ToString() ?? "--";
	}

	public static string GetLibreHardwareMonitorLocation()
	{
		string looseAssemblyPath = Path.Combine(AppContext.BaseDirectory, "LibreHardwareMonitorLib.dll");
		if (File.Exists(looseAssemblyPath))
		{
			return looseAssemblyPath;
		}

		return Environment.ProcessPath ?? AppContext.BaseDirectory;
	}

	public static string GetLibreHardwareMonitorFullName()
	{
		return typeof(Computer).Assembly.FullName ?? "--";
	}

	public static bool IsAdministrator()
	{
		try
		{
			using WindowsIdentity ntIdentity = WindowsIdentity.GetCurrent();
			WindowsPrincipal windowsPrincipal = new WindowsPrincipal(ntIdentity);
			return windowsPrincipal.IsInRole(WindowsBuiltInRole.Administrator);
		}
		catch (Exception ex) when (((ex is SecurityException || ex is UnauthorizedAccessException || ex is InvalidOperationException) ? 1 : 0) != 0)
		{
			AppLogger.LogError("Failed to detect process elevation state.", ex, "process-elevation:" + ex.GetType().FullName, TimeSpan.FromMinutes(10.0));
			return false;
		}
	}

	public static string GetProcessArchitecture()
	{
		return RuntimeInformation.ProcessArchitecture.ToString();
	}

	public static string GetAssemblyDescription(string? assemblyPath)
	{
		if (string.IsNullOrWhiteSpace(assemblyPath) || !File.Exists(assemblyPath))
		{
			return "--";
		}
		try
		{
			AssemblyName assemblyName = AssemblyName.GetAssemblyName(assemblyPath);
			FileVersionInfo versionInfo = FileVersionInfo.GetVersionInfo(assemblyPath);
			return string.Join(" | ", assemblyName.FullName, "FileVersion=" + ValueOrFallback(versionInfo.FileVersion), "ProductVersion=" + ValueOrFallback(versionInfo.ProductVersion), "SHA256=" + ComputeSha256(assemblyPath));
		}
		catch (Exception ex) when (((ex is ArgumentException || ex is BadImageFormatException || ex is FileLoadException || ex is FileNotFoundException || ex is IOException || ex is SecurityException || ex is UnauthorizedAccessException) ? 1 : 0) != 0)
		{
			return $"{Path.GetFileName(assemblyPath)}: {ex.GetType().Name}: {ex.Message}";
		}
	}

	public static IReadOnlyList<string> GetDriverOrNativeFiles(string? directory)
	{
		if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
		{
			return Array.Empty<string>();
		}
		try
		{
			return Directory.EnumerateFiles(directory, "*", SearchOption.TopDirectoryOnly).Where(IsDriverOrNativeFile).OrderBy<string, string>(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
				.ToArray();
		}
		catch (Exception ex) when (((ex is IOException || ex is UnauthorizedAccessException || ex is SecurityException) ? 1 : 0) != 0)
		{
			AppLogger.LogError("Failed to enumerate native or driver files in " + directory + ".", ex, "native-file-enumerate:" + directory + ":" + ex.GetType().FullName, TimeSpan.FromMinutes(10.0));
			return Array.Empty<string>();
		}
	}

	public static string FormatBoolean(bool value)
	{
		return value ? "True" : "False";
	}

	public static string ComputeSha256(string filePath)
	{
		using FileStream source = File.OpenRead(filePath);
		byte[] inArray = SHA256.HashData(source);
		return Convert.ToHexString(inArray);
	}

	private static bool IsDriverOrNativeFile(string filePath)
	{
		string fileName = Path.GetFileName(filePath);
		string extension = Path.GetExtension(filePath);
		return extension.Equals(".sys", StringComparison.OrdinalIgnoreCase) || fileName.Contains("WinRing", StringComparison.OrdinalIgnoreCase) || fileName.Contains("LibreHardwareMonitor.sys", StringComparison.OrdinalIgnoreCase) || fileName.Contains("OpenHardwareMonitor", StringComparison.OrdinalIgnoreCase) || fileName.Contains("MonoPosixHelper", StringComparison.OrdinalIgnoreCase) || fileName.Contains("InpOut", StringComparison.OrdinalIgnoreCase);
	}

	private static string ValueOrFallback(string? value)
	{
		return string.IsNullOrWhiteSpace(value) ? "--" : value.Trim();
	}
}
