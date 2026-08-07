using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Security;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using HardwareVision.Utilities;

namespace HardwareVision.Services;

public sealed class StartupTaskService : IStartupService
{
	private sealed record SchTasksResult(int ExitCode, string Output, string Error);

	private const string TaskName = "HardwareVision";

	private readonly StartupService registryFallback = new StartupService();
	private readonly SemaphoreSlim operationGate = new(1, 1);

	public string StatusMessage { get; private set; } = "管理员权限开机自启服务已准备。";


	public bool IsAdministratorStartupAvailable { get; private set; } = true;


	public bool IsUsingFallbackStartup { get; private set; }

	public bool IsEnabled()
	{
		return ResolveEnabledState(
			RunSchTasks("/Query", "/TN", TaskName));
	}

	public void Enable()
	{
		string? executablePath = GetExecutablePath();
		if (string.IsNullOrWhiteSpace(executablePath))
		{
			StatusMessage = "无法定位 HardwareVision.exe，不能创建管理员开机自启任务。";
			AppLogger.LogError(StatusMessage, null, "startup-task-missing-exe", TimeSpan.FromMinutes(10.0));
			EnableFallback();
			return;
		}
		ApplyEnableResult(RunSchTasks(
			"/Create",
			"/TN",
			TaskName,
			"/TR",
			QuoteForTaskAction(executablePath),
			"/SC",
			"ONLOGON",
			"/RL",
			"HIGHEST",
			"/F"));
	}

	public void Disable()
	{
		ApplyDisableResult(RunSchTasks(
			"/Delete",
			"/TN",
			TaskName,
			"/F"));
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

	public async Task<bool> IsStartupEnabledAsync(CancellationToken cancellationToken = default(CancellationToken))
	{
		await operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
		try
		{
			SchTasksResult result = await RunSchTasksAsync(
				cancellationToken,
				"/Query",
				"/TN",
				TaskName).ConfigureAwait(false);
			return ResolveEnabledState(result);
		}
		finally
		{
			operationGate.Release();
		}
	}

	public async Task SetStartupEnabledAsync(bool isEnabled, CancellationToken cancellationToken = default(CancellationToken))
	{
		await operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
		try
		{
			if (isEnabled)
			{
				await EnableAsync(cancellationToken).ConfigureAwait(false);
			}
			else
			{
				await DisableAsync(cancellationToken).ConfigureAwait(false);
			}
		}
		finally
		{
			operationGate.Release();
		}
	}

	private bool ResolveEnabledState(SchTasksResult result)
	{
		if (result.ExitCode == 0)
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

	private async Task EnableAsync(CancellationToken cancellationToken)
	{
		string? executablePath = GetExecutablePath();
		if (string.IsNullOrWhiteSpace(executablePath))
		{
			StatusMessage = "无法定位 HardwareVision.exe，不能创建管理员开机自启任务。";
			AppLogger.LogError(StatusMessage, null, "startup-task-missing-exe", TimeSpan.FromMinutes(10.0));
			EnableFallback();
			return;
		}
		SchTasksResult result = await RunSchTasksAsync(
			cancellationToken,
			"/Create",
			"/TN",
			TaskName,
			"/TR",
			QuoteForTaskAction(executablePath),
			"/SC",
			"ONLOGON",
			"/RL",
			"HIGHEST",
			"/F").ConfigureAwait(false);
		ApplyEnableResult(result);
	}

	private async Task DisableAsync(CancellationToken cancellationToken)
	{
		SchTasksResult result = await RunSchTasksAsync(
			cancellationToken,
			"/Delete",
			"/TN",
			TaskName,
			"/F").ConfigureAwait(false);
		ApplyDisableResult(result);
	}

	private void ApplyEnableResult(SchTasksResult result)
	{
		if (result.ExitCode == 0)
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
		AppLogger.LogError($"Startup scheduled task creation failed. ExitCode={result.ExitCode}; Output={result.Output}; Error={result.Error}", null, $"startup-task-create:{result.ExitCode}", TimeSpan.FromMinutes(10.0));
		EnableFallback();
	}

	private void ApplyDisableResult(SchTasksResult result)
	{
		if (result.ExitCode != 0 && !IsTaskMissing(result))
		{
			StatusMessage = "管理员权限开机自启关闭失败，请检查日志。";
			AppLogger.LogError($"Startup scheduled task delete failed. ExitCode={result.ExitCode}; Output={result.Output}; Error={result.Error}", null, $"startup-task-delete:{result.ExitCode}", TimeSpan.FromMinutes(10.0));
		}
		else
		{
			StatusMessage = "管理员权限开机自启已关闭。";
			AppLogger.LogKeyEvent("Startup scheduled task disabled.");
		}
		registryFallback.Disable();
		IsUsingFallbackStartup = false;
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
			using Process process = new()
			{
				StartInfo = processStartInfo
			};
			StringBuilder output = new();
			StringBuilder error = new();
			process.OutputDataReceived += (_, eventArgs) =>
			{
				if (eventArgs.Data is not null)
				{
					lock (output)
					{
						output.AppendLine(eventArgs.Data);
					}
				}
			};
			process.ErrorDataReceived += (_, eventArgs) =>
			{
				if (eventArgs.Data is not null)
				{
					lock (error)
					{
						error.AppendLine(eventArgs.Data);
					}
				}
			};
			if (!process.Start())
			{
				throw new InvalidOperationException(
					"Failed to start schtasks.exe.");
			}
			process.BeginOutputReadLine();
			process.BeginErrorReadLine();
			process.WaitForExit();
			return new SchTasksResult(
				process.ExitCode,
				output.ToString().Trim(),
				error.ToString().Trim());
		}
		catch (Exception ex) when (((ex is InvalidOperationException || ex is Win32Exception || ex is IOException || ex is SecurityException || ex is UnauthorizedAccessException) ? 1 : 0) != 0)
		{
			AppLogger.LogError("Failed to execute schtasks.exe.", ex, "startup-task-schtasks:" + ex.GetType().FullName, TimeSpan.FromMinutes(10.0));
			return new SchTasksResult(-1, string.Empty, ex.Message);
		}
	}

	private static async Task<SchTasksResult> RunSchTasksAsync(
		CancellationToken cancellationToken,
		params string[] arguments)
	{
		try
		{
			ProcessStartInfo processStartInfo = new()
			{
				FileName = "schtasks.exe",
				UseShellExecute = false,
				RedirectStandardOutput = true,
				RedirectStandardError = true,
				CreateNoWindow = true
			};
			foreach (string argument in arguments)
			{
				processStartInfo.ArgumentList.Add(argument);
			}

			using Process process = new()
			{
				StartInfo = processStartInfo
			};
			if (!process.Start())
			{
				throw new InvalidOperationException(
					"Failed to start schtasks.exe.");
			}

			try
			{
				Task<string> outputTask = process.StandardOutput
					.ReadToEndAsync(cancellationToken);
				Task<string> errorTask = process.StandardError
					.ReadToEndAsync(cancellationToken);
				await process.WaitForExitAsync(cancellationToken)
					.ConfigureAwait(false);
				string[] streams = await Task.WhenAll(
					outputTask,
					errorTask).ConfigureAwait(false);
				return new SchTasksResult(
					process.ExitCode,
					streams[0].Trim(),
					streams[1].Trim());
			}
			catch (OperationCanceledException)
			{
				TryTerminate(process);
				throw;
			}
		}
		catch (OperationCanceledException)
		{
			throw;
		}
		catch (Exception exception)
			when (exception is InvalidOperationException
				or Win32Exception
				or IOException
				or SecurityException
				or UnauthorizedAccessException)
		{
			AppLogger.LogError(
				"Failed to execute schtasks.exe asynchronously.",
				exception,
				$"startup-task-schtasks-async:{exception.GetType().FullName}",
				TimeSpan.FromMinutes(10.0));
			return new SchTasksResult(-1, string.Empty, exception.Message);
		}
	}

	private static void TryTerminate(Process process)
	{
		try
		{
			if (!process.HasExited)
			{
				process.Kill(entireProcessTree: true);
			}
		}
		catch (Exception exception)
			when (exception is InvalidOperationException
				or Win32Exception
				or NotSupportedException)
		{
			AppLogger.LogError(
				"Unable to terminate the canceled schtasks.exe process.",
				exception,
				$"startup-task-cancel-kill:{exception.GetType().FullName}",
				TimeSpan.FromMinutes(10.0));
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
