using System;
using System.Collections.Generic;
using System.IO;
using System.Security;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using HardwareVision.Models;
using HardwareVision.Utilities;

namespace HardwareVision.Services;

public sealed class SettingsService : ISettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly SemaphoreSlim settingsLock = new(1, 1);
    private AppSettings? currentSettings;

    public string SettingsDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "HardwareVision");

    public string SettingsFilePath => Path.Combine(SettingsDirectory, "settings.json");

    public async Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        await settingsLock.WaitAsync(cancellationToken);
        try
        {
            currentSettings = await LoadWithoutLockAsync(cancellationToken);
            return Clone(currentSettings);
        }
        finally
        {
            settingsLock.Release();
        }
    }

    public async Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);

        await settingsLock.WaitAsync(cancellationToken);
        try
        {
            currentSettings = Normalize(settings);
            await SaveCoreAsync(currentSettings, cancellationToken);
        }
        catch (Exception exception) when (IsRecoverableSettingsException(exception))
        {
            AppLogger.LogError(
                "Settings save failed. The app will keep the in-memory settings for this session.",
                exception,
                $"settings-save:{exception.GetType().FullName}",
                TimeSpan.FromMinutes(10));
            currentSettings = Clone(settings);
        }
        finally
        {
            settingsLock.Release();
        }
    }

    public async Task<AppSettings> GetSettingsAsync(CancellationToken cancellationToken = default)
    {
        if (currentSettings is not null)
        {
            return Clone(currentSettings);
        }

        return await LoadAsync(cancellationToken);
    }

    public async Task<AppSettings> UpdateAsync(Action<AppSettings> updateAction, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(updateAction);

        await settingsLock.WaitAsync(cancellationToken);
        try
        {
            currentSettings ??= await LoadWithoutLockAsync(cancellationToken);

            AppSettings updatedSettings = Clone(currentSettings);
            updateAction(updatedSettings);
            currentSettings = Normalize(updatedSettings);
            await SaveCoreAsync(currentSettings, cancellationToken);
            return Clone(currentSettings);
        }
        catch (Exception exception) when (IsRecoverableSettingsException(exception))
        {
            AppLogger.LogError(
                "Settings update failed. The app will keep the latest in-memory settings.",
                exception,
                $"settings-update:{exception.GetType().FullName}",
                TimeSpan.FromMinutes(10));
            currentSettings = Normalize(currentSettings ?? CreateDefaultSettings());
            return Clone(currentSettings);
        }
        finally
        {
            settingsLock.Release();
        }
    }

    private async Task<AppSettings> LoadWithoutLockAsync(CancellationToken cancellationToken)
    {
        try
        {
            Directory.CreateDirectory(SettingsDirectory);
        }
        catch (Exception exception) when (IsRecoverableSettingsException(exception))
        {
            AppLogger.LogError(
                "Settings directory could not be created. Defaults will be used in memory.",
                exception,
                $"settings-directory:{exception.GetType().FullName}",
                TimeSpan.FromMinutes(10));
            return CreateDefaultSettings();
        }

        if (!File.Exists(SettingsFilePath))
        {
            AppSettings defaults = CreateDefaultSettings();
            await TrySaveCoreAsync(defaults, cancellationToken, "settings-create-default");
            return defaults;
        }

        try
        {
            await using FileStream stream = new(
                SettingsFilePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite,
                bufferSize: 4096,
                useAsync: true);

            AppSettings normalized = Normalize(
                await JsonSerializer.DeserializeAsync<AppSettings>(stream, JsonOptions, cancellationToken));

            await TrySaveCoreAsync(normalized, cancellationToken, "settings-normalize-save");
            return normalized;
        }
        catch (Exception exception) when (IsRecoverableSettingsException(exception))
        {
            AppLogger.LogError(
                "Settings file could not be read. The app will back it up and use defaults.",
                exception,
                $"settings-load:{exception.GetType().FullName}",
                TimeSpan.FromMinutes(10));
            BackupCorruptedSettingsFile();

            AppSettings defaults = CreateDefaultSettings();
            await TrySaveCoreAsync(defaults, cancellationToken, "settings-save-default-after-load-failure");
            return defaults;
        }
    }

    private async Task TrySaveCoreAsync(AppSettings settings, CancellationToken cancellationToken, string throttleKey)
    {
        try
        {
            await SaveCoreAsync(settings, cancellationToken);
        }
        catch (Exception exception) when (IsRecoverableSettingsException(exception))
        {
            AppLogger.LogError(
                "Settings file could not be written. Startup will continue with in-memory settings.",
                exception,
                $"{throttleKey}:{exception.GetType().FullName}",
                TimeSpan.FromMinutes(10));
        }
    }

    private async Task SaveCoreAsync(AppSettings settings, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(SettingsDirectory);

        string temporaryPath = Path.Combine(SettingsDirectory, $"settings.{Guid.NewGuid():N}.tmp");
        try
        {
            await using (FileStream stream = new(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 4096,
                useAsync: true))
            {
                await JsonSerializer.SerializeAsync(stream, settings, JsonOptions, cancellationToken);
            }

            ReplaceSettingsFile(temporaryPath);
        }
        finally
        {
            TryDeleteFile(temporaryPath);
        }
    }

    private void ReplaceSettingsFile(string temporaryPath)
    {
        if (!File.Exists(SettingsFilePath))
        {
            File.Move(temporaryPath, SettingsFilePath);
            return;
        }

        try
        {
            File.Replace(temporaryPath, SettingsFilePath, destinationBackupFileName: null, ignoreMetadataErrors: true);
            return;
        }
        catch (Exception exception) when (IsRecoverableSettingsException(exception))
        {
            // Some file systems or security products reject File.Replace even when a direct overwrite works.
        }

        try
        {
            File.Copy(temporaryPath, SettingsFilePath, overwrite: true);
        }
        catch (Exception exception) when (IsRecoverableSettingsException(exception))
        {
            AppLogger.LogError(
                "Settings overwrite failed. Falling back to delete and move.",
                exception,
                $"settings-copy:{exception.GetType().FullName}",
                TimeSpan.FromMinutes(10));
            File.Delete(SettingsFilePath);
            File.Move(temporaryPath, SettingsFilePath);
        }
    }

    private void BackupCorruptedSettingsFile()
    {
        if (!File.Exists(SettingsFilePath))
        {
            return;
        }

        string timestamp = DateTimeOffset.Now.ToString("yyyyMMddHHmmss");
        string backupPath = Path.Combine(SettingsDirectory, $"settings.corrupt.{timestamp}.json");
        int suffix = 1;
        while (File.Exists(backupPath))
        {
            backupPath = Path.Combine(SettingsDirectory, $"settings.corrupt.{timestamp}.{suffix}.json");
            suffix++;
        }

        try
        {
            File.Move(SettingsFilePath, backupPath);
        }
        catch (Exception exception) when (IsRecoverableSettingsException(exception))
        {
            try
            {
                File.Copy(SettingsFilePath, backupPath, overwrite: true);
            }
            catch (Exception copyException) when (IsRecoverableSettingsException(copyException))
            {
                AppLogger.LogError(
                    "Settings backup failed.",
                    copyException,
                    $"settings-backup:{copyException.GetType().FullName}",
                    TimeSpan.FromMinutes(10));
            }
        }
    }

    private static AppSettings CreateDefaultSettings()
    {
        return new AppSettings
        {
            AutoStartEnabled = false,
            StartMinimizedToTray = false,
            CloseToTray = true,
            RefreshIntervalSeconds = 0.5d,
            BackgroundRefreshIntervalSeconds = 10,
            Theme = "Dark",
            LastSelectedPage = "Dashboard",
            PreferredGpuId = null,
            PreferredDiskId = null,
            PreferredNetworkAdapterId = null,
            ShowVirtualNetworkAdapters = false,
            MetricVisibility = new Dictionary<string, bool>(),
            MetricDisplayOrder = new Dictionary<string, int>()
        };
    }

    private static AppSettings Normalize(AppSettings? settings)
    {
        AppSettings normalized = settings is null ? CreateDefaultSettings() : Clone(settings);
        normalized.RefreshIntervalSeconds = NormalizeForegroundRefreshInterval(normalized.RefreshIntervalSeconds);
        normalized.BackgroundRefreshIntervalSeconds = Math.Clamp(
            normalized.BackgroundRefreshIntervalSeconds <= 0 ? 10 : normalized.BackgroundRefreshIntervalSeconds,
            5,
            120);

        if (string.IsNullOrWhiteSpace(normalized.Theme))
        {
            normalized.Theme = "Dark";
        }

        if (string.IsNullOrWhiteSpace(normalized.LastSelectedPage))
        {
            normalized.LastSelectedPage = "Dashboard";
        }

        if (string.IsNullOrWhiteSpace(normalized.PreferredGpuId))
        {
            normalized.PreferredGpuId = null;
        }

        if (string.IsNullOrWhiteSpace(normalized.PreferredDiskId))
        {
            normalized.PreferredDiskId = null;
        }

        if (string.IsNullOrWhiteSpace(normalized.PreferredNetworkAdapterId))
        {
            normalized.PreferredNetworkAdapterId = null;
        }

        normalized.MetricVisibility ??= new Dictionary<string, bool>();
        normalized.MetricDisplayOrder ??= new Dictionary<string, int>();
        return normalized;
    }

    private static double NormalizeForegroundRefreshInterval(double seconds)
    {
        double value = double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds <= 0
            ? 0.5d
            : seconds;

        value = Math.Round(value * 2d, MidpointRounding.AwayFromZero) / 2d;
        return Math.Clamp(value, 0.5d, 30d);
    }

    private static AppSettings Clone(AppSettings settings)
    {
        return new AppSettings
        {
            AutoStartEnabled = settings.AutoStartEnabled,
            StartMinimizedToTray = settings.StartMinimizedToTray,
            CloseToTray = settings.CloseToTray,
            RefreshIntervalSeconds = settings.RefreshIntervalSeconds,
            BackgroundRefreshIntervalSeconds = settings.BackgroundRefreshIntervalSeconds,
            Theme = settings.Theme,
            LastSelectedPage = settings.LastSelectedPage,
            PreferredGpuId = settings.PreferredGpuId,
            PreferredDiskId = settings.PreferredDiskId,
            PreferredNetworkAdapterId = settings.PreferredNetworkAdapterId,
            ShowVirtualNetworkAdapters = settings.ShowVirtualNetworkAdapters,
            MetricVisibility = settings.MetricVisibility is null
                ? new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, bool>(settings.MetricVisibility, StringComparer.OrdinalIgnoreCase),
            MetricDisplayOrder = settings.MetricDisplayOrder is null
                ? new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, int>(settings.MetricDisplayOrder, StringComparer.OrdinalIgnoreCase)
        };
    }

    private static bool IsRecoverableSettingsException(Exception exception)
    {
        return exception is IOException
            or UnauthorizedAccessException
            or JsonException
            or NotSupportedException
            or SecurityException;
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception exception) when (IsRecoverableSettingsException(exception))
        {
        }
    }
}
