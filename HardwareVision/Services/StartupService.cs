using System;
using System.Diagnostics;
using System.IO;
using System.Security;
using System.Threading;
using System.Threading.Tasks;
using HardwareVision.Utilities;
using Microsoft.Win32;

namespace HardwareVision.Services;

public sealed class StartupService : IStartupService
{
	private const string RunKeyPath = "Software\\Microsoft\\Windows\\CurrentVersion\\Run";

	private const string StartupValueName = "HardwareVision";

	public string StatusMessage { get; private set; } = "普通权限开机自启服务已准备。";


	public bool IsAdministratorStartupAvailable => false;

	public bool IsUsingFallbackStartup { get; private set; }

	public bool IsEnabled()
	{
		try
		{
			using RegistryKey? registryKey = Registry.CurrentUser.OpenSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Run", writable: false);
			string? text = registryKey?.GetValue("HardwareVision") as string;
			string? executablePath = GetExecutablePath();
			if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(executablePath))
			{
				return false;
			}
			return string.Equals(NormalizePath(text), NormalizePath(executablePath), StringComparison.OrdinalIgnoreCase);
		}
		catch (Exception ex) when (IsRegistryException(ex))
		{
			AppLogger.LogError("Startup registry read failed.", ex, "startup-read:" + ex.GetType().FullName, TimeSpan.FromMinutes(10.0));
			StatusMessage = "普通开机自启状态读取失败。";
			return false;
		}
	}

	public void Enable()
	{
		try
		{
			string? executablePath = GetExecutablePath();
			if (string.IsNullOrWhiteSpace(executablePath))
			{
				return;
			}
			using RegistryKey? registryKey = Registry.CurrentUser.OpenSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Run", writable: true) ?? Registry.CurrentUser.CreateSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Run", writable: true);
			registryKey?.SetValue("HardwareVision", QuotePath(executablePath), RegistryValueKind.String);
			IsUsingFallbackStartup = true;
			StatusMessage = "任务计划程序不可用，已启用普通权限开机自启。";
		}
		catch (Exception ex) when (IsRegistryException(ex))
		{
			AppLogger.LogError("Startup registry enable failed.", ex, "startup-enable:" + ex.GetType().FullName, TimeSpan.FromMinutes(10.0));
			StatusMessage = "普通权限开机自启启用失败。";
		}
	}

	public void Disable()
	{
		try
		{
			using RegistryKey? registryKey = Registry.CurrentUser.OpenSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Run", writable: true);
			registryKey?.DeleteValue("HardwareVision", throwOnMissingValue: false);
			IsUsingFallbackStartup = false;
			StatusMessage = "普通权限开机自启已关闭。";
		}
		catch (Exception ex) when (IsRegistryException(ex))
		{
			AppLogger.LogError("Startup registry disable failed.", ex, "startup-disable:" + ex.GetType().FullName, TimeSpan.FromMinutes(10.0));
			StatusMessage = "普通权限开机自启关闭失败。";
		}
	}

	public void SetEnabled(bool enabled)
	{
		if (enabled)
		{
			Enable();
		}
		else
		{
			Disable();
		}
	}

	public Task<bool> IsStartupEnabledAsync(CancellationToken cancellationToken = default(CancellationToken))
	{
		cancellationToken.ThrowIfCancellationRequested();
		return Task.FromResult(IsEnabled());
	}

	public Task SetStartupEnabledAsync(bool isEnabled, CancellationToken cancellationToken = default(CancellationToken))
	{
		cancellationToken.ThrowIfCancellationRequested();
		SetEnabled(isEnabled);
		return Task.CompletedTask;
	}

	private static string? GetExecutablePath()
	{
		return Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName;
	}

	private static string QuotePath(string path)
	{
		return path.Contains(' ', StringComparison.Ordinal) ? ("\"" + path + "\"") : path;
	}

	private static string NormalizePath(string path)
	{
		string text = path.Trim().Trim('"');
		try
		{
			return Path.GetFullPath(text);
		}
		catch (Exception ex) when (((ex is ArgumentException || ex is NotSupportedException || ex is PathTooLongException) ? 1 : 0) != 0)
		{
			return text;
		}
	}

	private static bool IsRegistryException(Exception exception)
	{
		if (exception is SecurityException || exception is UnauthorizedAccessException || exception is IOException || exception is ObjectDisposedException)
		{
			return true;
		}
		return false;
	}
}
