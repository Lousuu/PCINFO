using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Security;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace HardwareVision.Utilities;

public static class AppLogger
{
	private static readonly SemaphoreSlim WriteLock = new SemaphoreSlim(1, 1);

	private static readonly object ThrottleLock = new object();

	private static readonly Dictionary<string, DateTimeOffset> LastWriteByKey = new Dictionary<string, DateTimeOffset>(StringComparer.OrdinalIgnoreCase);

	private static readonly TimeSpan DefaultErrorThrottle = TimeSpan.FromMinutes(2.0);

	private static int pruneStarted;

	private static string LogsDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "HardwareVision", "logs");

	public static void LogKeyEvent(string message)
	{
		_ = WriteAsync("INFO", message, null, null, TimeSpan.Zero);
	}

	public static void LogStartupStage(string stage, Stopwatch startupClock, TimeSpan? phaseElapsed = null)
	{
		string phaseText = phaseElapsed.HasValue
			? $"; phase={phaseElapsed.Value.TotalMilliseconds:0} ms"
			: string.Empty;
		LogKeyEvent($"StartupTiming | {stage} | total={startupClock.Elapsed.TotalMilliseconds:0} ms{phaseText}; {BuildMemoryText()}");
	}

	public static void LogMemoryCheckpoint(string checkpoint)
	{
		LogKeyEvent($"MemoryCheckpoint | {checkpoint} | {BuildMemoryText()}");
	}

	public static void LogError(string message, Exception? exception = null, string? throttleKey = null, TimeSpan? throttleInterval = null)
	{
		_ = WriteAsync("ERROR", message, exception, throttleKey ?? (message + ":" + exception?.GetType().FullName), throttleInterval ?? DefaultErrorThrottle);
	}

	private static string BuildMemoryText()
	{
		try
		{
			using Process process = Process.GetCurrentProcess();
			return $"private={ToMegabytes(process.PrivateMemorySize64):0.0} MB; working={ToMegabytes(process.WorkingSet64):0.0} MB; managed={ToMegabytes(GC.GetTotalMemory(false)):0.0} MB";
		}
		catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException)
		{
			return "memory=unavailable";
		}
	}

	private static double ToMegabytes(long bytes)
	{
		return bytes / 1024d / 1024d;
	}

	private static async Task WriteAsync(string level, string message, Exception? exception, string? throttleKey, TimeSpan throttleInterval)
	{
		if (ShouldThrottle(throttleKey, throttleInterval))
		{
			return;
		}
		try
		{
			Directory.CreateDirectory(LogsDirectory);
			TryPruneOldLogs();
			await WriteLock.WaitAsync().ConfigureAwait(continueOnCapturedContext: false);
			try
			{
				string logPath = Path.Combine(LogsDirectory, $"hardwarevision-{DateTimeOffset.Now:yyyyMMdd}.log");
				string entry = BuildEntry(level, message, exception);
				await File.AppendAllTextAsync(logPath, entry, Encoding.UTF8).ConfigureAwait(continueOnCapturedContext: false);
			}
			finally
			{
				WriteLock.Release();
			}
		}
		catch (Exception ex) when (((ex is IOException || ex is UnauthorizedAccessException || ex is NotSupportedException || ex is SecurityException) ? 1 : 0) != 0)
		{
		}
	}

	private static bool ShouldThrottle(string? throttleKey, TimeSpan throttleInterval)
	{
		if (string.IsNullOrWhiteSpace(throttleKey) || throttleInterval <= TimeSpan.Zero)
		{
			return false;
		}
		DateTimeOffset now = DateTimeOffset.Now;
		lock (ThrottleLock)
		{
			if (LastWriteByKey.TryGetValue(throttleKey, out var value) && now - value < throttleInterval)
			{
				return true;
			}
			LastWriteByKey[throttleKey] = now;
			return false;
		}
	}

	private static string BuildEntry(string level, string message, Exception? exception)
	{
		StringBuilder stringBuilder = new StringBuilder();
		stringBuilder.Append(DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss.fff zzz", CultureInfo.InvariantCulture));
		stringBuilder.Append(" [");
		stringBuilder.Append(level);
		stringBuilder.Append("] ");
		stringBuilder.AppendLine(message);
		if (exception != null)
		{
			stringBuilder.Append(exception.GetType().FullName);
			stringBuilder.Append(": ");
			stringBuilder.AppendLine(exception.Message);
			stringBuilder.AppendLine(exception.StackTrace);
		}
		return stringBuilder.ToString();
	}

	private static void TryPruneOldLogs()
	{
		if (Interlocked.Exchange(ref pruneStarted, 1) != 0)
		{
			return;
		}
		try
		{
			DateTime dateTime = DateTime.Now.AddDays(-14.0);
			foreach (string item in Directory.EnumerateFiles(LogsDirectory, "hardwarevision-*.log"))
			{
				try
				{
					FileInfo fileInfo = new FileInfo(item);
					if (fileInfo.LastWriteTime < dateTime)
					{
						fileInfo.Delete();
					}
				}
				catch (Exception ex) when (((ex is IOException || ex is UnauthorizedAccessException || ex is NotSupportedException || ex is SecurityException) ? 1 : 0) != 0)
				{
				}
			}
		}
		catch (Exception ex2) when (((ex2 is IOException || ex2 is UnauthorizedAccessException || ex2 is NotSupportedException || ex2 is SecurityException) ? 1 : 0) != 0)
		{
		}
	}
}
