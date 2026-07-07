using System;
using System.Windows;

namespace HardwareVision.Services;

public interface ITrayService : IDisposable
{
	event EventHandler? OpenRequested;

	event EventHandler? RefreshHardwareInfoRequested;

	event EventHandler? SettingsRequested;

	event EventHandler? ExitRequested;

	void Initialize(Window mainWindow);

	void ShowMainWindow();

	void HideToTray();
}
