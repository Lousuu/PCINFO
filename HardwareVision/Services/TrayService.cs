using System;
using System.Drawing;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Threading;

namespace HardwareVision.Services;

public sealed class TrayService : ITrayService, IDisposable
{
	private Window? mainWindow;

	private Dispatcher? dispatcher;

	private NotifyIcon? notifyIcon;

	private ContextMenuStrip? contextMenu;

	private bool isDisposed;

	public event EventHandler? OpenRequested;

	public event EventHandler? RefreshHardwareInfoRequested;

	public event EventHandler? SettingsRequested;

	public event EventHandler? ExitRequested;

	public void Initialize(Window mainWindow)
	{
		ObjectDisposedException.ThrowIf(isDisposed, this);
		this.mainWindow = mainWindow;
		dispatcher = ((DispatcherObject)mainWindow).Dispatcher;
		contextMenu = new ContextMenuStrip();
		contextMenu.Items.Add(CreateMenuItem("打开 HardwareVision", delegate
		{
			Raise(this.OpenRequested);
		}));
		contextMenu.Items.Add(CreateMenuItem("刷新硬件信息", delegate
		{
			Raise(this.RefreshHardwareInfoRequested);
		}));
		contextMenu.Items.Add(CreateMenuItem("设置", delegate
		{
			Raise(this.SettingsRequested);
		}));
		contextMenu.Items.Add(new ToolStripSeparator());
		contextMenu.Items.Add(CreateMenuItem("退出", delegate
		{
			Raise(this.ExitRequested);
		}));
		notifyIcon = new NotifyIcon
		{
			Text = "HardwareVision",
			Icon = SystemIcons.Application,
			ContextMenuStrip = contextMenu,
			Visible = true
		};
		notifyIcon.DoubleClick += delegate
		{
			Raise(this.OpenRequested);
		};
	}

	public void ShowMainWindow()
	{
		if (mainWindow == null)
		{
			return;
		}
		RunOnDispatcher(delegate
		{
			if (mainWindow.WindowState == WindowState.Minimized)
			{
				mainWindow.WindowState = WindowState.Normal;
			}
			mainWindow.Show();
			mainWindow.Activate();
		});
	}

	public void HideToTray()
	{
		if (mainWindow != null)
		{
			RunOnDispatcher(mainWindow.Hide);
		}
	}

	public void Dispose()
	{
		if (!isDisposed)
		{
			isDisposed = true;
			if (notifyIcon != null)
			{
				notifyIcon.Visible = false;
				notifyIcon.Dispose();
				notifyIcon = null;
			}
			contextMenu?.Dispose();
			contextMenu = null;
		}
	}

	private static ToolStripMenuItem CreateMenuItem(string text, EventHandler onClick)
	{
		ToolStripMenuItem toolStripMenuItem = new ToolStripMenuItem(text);
		toolStripMenuItem.Click += onClick;
		return toolStripMenuItem;
	}

	private void Raise(EventHandler? handler)
	{
		EventHandler handler2 = handler;
		if (handler2 != null)
		{
			RunOnDispatcher(delegate
			{
				handler2(this, EventArgs.Empty);
			});
		}
	}

	private void RunOnDispatcher(Action action)
	{
		if (dispatcher == null || dispatcher.CheckAccess())
		{
			action();
		}
		else
		{
			dispatcher.BeginInvoke((Delegate)action, Array.Empty<object>());
		}
	}
}
