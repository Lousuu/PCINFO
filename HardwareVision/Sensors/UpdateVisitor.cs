using System;
using System.Threading;
using HardwareVision.Utilities;
using LibreHardwareMonitor.Hardware;

namespace HardwareVision.Sensors;

internal sealed class UpdateVisitor : IVisitor
{
	public const int WarmupRefreshCount = 3;

	public const int WarmupDelayMilliseconds = 300;

	public void VisitComputer(IComputer computer)
	{
		computer.Traverse(this);
	}

	public void VisitHardware(IHardware hardware)
	{
		try
		{
			hardware.Update();
		}
		catch (Exception ex) when (!(ex is OperationCanceledException))
		{
			AppLogger.LogError($"LibreHardwareMonitor UpdateVisitor failed for {hardware.HardwareType} / {hardware.Name}.", ex, $"lhm-update-visitor:{hardware.HardwareType}:{hardware.Name}:{ex.GetType().FullName}");
		}
		IHardware[] subHardware = hardware.SubHardware;
		foreach (IHardware hardware2 in subHardware)
		{
			try
			{
				hardware2.Accept(this);
			}
			catch (Exception ex2) when (!(ex2 is OperationCanceledException))
			{
				AppLogger.LogError($"LibreHardwareMonitor UpdateVisitor failed for subhardware {hardware2.HardwareType} / {hardware2.Name}.", ex2, $"lhm-update-visitor-sub:{hardware2.HardwareType}:{hardware2.Name}:{ex2.GetType().FullName}");
			}
		}
	}

	public void VisitSensor(ISensor sensor)
	{
	}

	public void VisitParameter(IParameter parameter)
	{
	}

	public static void Update(IComputer computer, CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();
		computer.Accept(new UpdateVisitor());
	}

	public static void WarmUp(IComputer computer, CancellationToken cancellationToken)
	{
		for (int i = 0; i < 3; i++)
		{
			Update(computer, cancellationToken);
			if (i < 2 && cancellationToken.WaitHandle.WaitOne(300))
			{
				throw new OperationCanceledException(cancellationToken);
			}
		}
	}
}
