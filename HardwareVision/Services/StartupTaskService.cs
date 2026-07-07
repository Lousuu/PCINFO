using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Security;
using System.Threading;
using System.Threading.Tasks;
using HardwareVision.Utilities;

namespace HardwareVision.Services;

public sealed class StartupTaskService : IStartupService
{
	private sealed record SchTasksResult(int ExitCode, string Output, string Error);

	private const string TaskName = "HardwareVision";

	private readonly StartupService registryFallback = new StartupService();

	public string StatusMessage { get; private set; } = "管理员权限开机自启服务已准备。";


	public bool IsAdministratorStartupAvailable { get; private set; } = true;


	public bool IsUsingFallbackStartup { get; private set; }

	public bool IsEnabled()
	{
		SchTasksResult schTasksResult = RunSchTasks("/Query", "/TN", "HardwareVision");
		if (schTasksResult.ExitCode == 0)
		{
			IsAdministratorStartupAvailable = true;
			IsUsingFallbackStartup = false;
			StatusMessage = "管理员权限开机自启已启用。";
			return true;
		}
		if (IsUsingFallbackStartup = registryFallback.IsEnabled())
		{
			IsAdministratorStartupAvailable = false;
			StatusMessage = "任务计划程序自启不可用，当前仅启用了普通权限开机自启。";
			return true;
		}
		StatusMessage = "开机自启未启用。";
		return false;
	}

	public void Enable()
	{
		string executablePath = GetExecutablePath();
		if (string.IsNullOrWhiteSpace(executablePath))
		{
			StatusMessage = "无法定位 HardwareVision.exe，不能创建管理员开机自启任务。";
			AppLogger.LogError(StatusMessage, null, "startup-task-missing-exe", TimeSpan.FromMinutes(10.0));
			EnableFallback();
			return;
		}
		SchTasksResult schTasksResult = RunSchTasks("/Create", "/TN", "HardwareVision", "/TR", QuoteForTaskAction(executablePath), "/SC", "ONLOGON", "/RL", "HIGHEST", "/F");
		if (schTasksResult.ExitCode == 0)
		{
			registryFallback.Disable();
			IsAdministratorStartupAvailable = true;
			IsUsingFallbackStartup = false;
			StatusMessage = "管理员权限开机自启已启用。";
			AppLogger.LogKeyEvent("Startup scheduled task enabled.");
			return;
		}
		IsAdministratorStartupAvailable = false;
		StatusMessage = "管理员权限开机自启创建失败，正在尝试普通权限开机自启。";
		AppLogger.LogError($"Startup scheduled task creation failed. ExitCode={schTasksResult.ExitCode}; Output={schTasksResult.Output}; Error={schTasksResult.Error}", null, $"startup-task-create:{schTasksResult.ExitCode}", TimeSpan.FromMinutes(10.0));
		EnableFallback();
	}

	public void Disable()
	{
		SchTasksResult schTasksResult = RunSchTasks("/Delete", "/TN", "HardwareVision", "/F");
		if (schTasksResult.ExitCode != 0 && !IsTaskMissing(schTasksResult))
		{
			StatusMessage = "管理员权限开机自启关闭失败，请检查日志。";
			AppLogger.LogError($"Startup scheduled task delete failed. ExitCode={schTasksResult.ExitCode}; Output={schTasksResult.Output}; Error={schTasksResult.Error}", null, $"startup-task-delete:{schTasksResult.ExitCode}", TimeSpan.FromMinutes(10.0));
		}
		else
		{
			StatusMessage = "管理员权限开机自启已关闭。";
			AppLogger.LogKeyEvent("Startup scheduled task disabled.");
		}
		registryFallback.Disable();
		IsUsingFallbackStartup = false;
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

	private void EnableFallback()
	{
		registryFallback.Enable();
		IsUsingFallbackStartup = registryFallback.IsEnabled();
		StatusMessage = (IsUsingFallbackStartup ? "管理员自启不可用；已启用普通权限开机自启。" : "管理员自启不可用；普通权限开机自启也未能启用。");
	}

	private static SchTasksResult RunSchTasks(params string[] arguments)
	{
		try
		{
			ProcessStartInfo processStartInfo = new ProcessStartInfo
			{
				FileName = "schtasks.exe",
				UseShellExecute = false,
				RedirectStandardOutput = true,
				RedirectStandardError = true,
				CreateNoWindow = true
			};
			foreach (string item in arguments)
			{
				processStartInfo.ArgumentList.Add(item);
			}
			using Process process = Process.Start(processStartInfo) ?? throw new InvalidOperationException("Failed to start schtasks.exe.");
			string text = process.StandardOutput.ReadToEnd();
			string text2 = process.StandardError.ReadToEnd();
			process.WaitForExit();
			return new SchTasksResult(process.ExitCode, text.Trim(), text2.Trim());
		}
		catch (Exception ex) when (((ex is InvalidOperationException || ex is Win32Exception || ex is IOException || ex is SecurityException || ex is UnauthorizedAccessException) ? 1 : 0) != 0)
		{
			AppLogger.LogError("Failed to execute schtasks.exe.", ex, "startup-task-schtasks:" + ex.GetType().FullName, TimeSpan.FromMinutes(10.0));
			return new SchTasksResult(-1, string.Empty, ex.Message);
		}
	}

	private static string? GetExecutablePath()
	{
		return Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName;
	}

	private static string QuoteForTaskAction(string path)
	{
		return "\"" + path + "\"";
	}

	private static bool IsTaskMissing(SchTasksResult result)
	{
		string text = string.Join(" ", result.Output, result.Error, result.ExitCode.ToString(CultureInfo.InvariantCulture));
		return text.Contains("cannot find", StringComparison.OrdinalIgnoreCase) || text.Contains("does not exist", StringComparison.OrdinalIgnoreCase) || text.Contains("找不到", StringComparison.OrdinalIgnoreCase) || text.Contains("不存在", StringComparison.OrdinalIgnoreCase);
	}
}
