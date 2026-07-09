# HardwareVision v0.1.0

HardwareVision v0.1.0 is an early Windows x64 pre-release focused on compact local hardware monitoring and game performance capture.

## Highlights

- CPU, GPU, memory, disk, network, and motherboard overview pages.
- CPU and GPU load, temperature, clock, power, fan, and status readings where supported by the device and driver.
- Multi-GPU, multi-disk, and multi-network adapter selection.
- Real-time CPU and GPU trend charts.
- Game performance page with current FPS, average FPS, 1% low, 0.1% low, average frame time, CPU frame time, GPU frame time, and latency metrics when capture data is available.
- Motherboard, BIOS, chipset, memory module, disk, and advanced sensor detail views.
- 0.5 second foreground refresh interval.
- Administrator startup through the application manifest.
- Startup and tray behavior settings.
- Compact desktop UI for monitoring-focused workflows.

## Package

- Platform: Windows x64.
- Distribution: self-contained portable ZIP.
- .NET Runtime: included in the package, no separate .NET installation is required.
- Elevation: administrator rights are required for the main application.
- Digital signing: this pre-release build is not code signed. Windows SmartScreen may show a warning.

## Game Capture Notes

HardwareVision can detect and invoke a compatible `presentmon.exe` for game frame capture, but PresentMon is not bundled with this release package. If PresentMon is not installed or cannot be found, the game performance page remains available and reports the capture component as unavailable.

The current detection paths include:

- `PRESENTMON_PATH` environment variable.
- `Tools\PresentMon` or `PresentMon` under the application folder.
- WinGet package locations under the current user's local app data.
- Intel PresentMon folders under Program Files.
- Entries in the system `PATH`.

Frame capture availability depends on the game, graphics API, present mode, permissions, graphics driver behavior, and any anti-cheat or process protection used by the target application.

## Known Limitations

- Sensor availability varies by CPU, GPU, motherboard, BIOS, embedded controller, driver, and vendor implementation.
- Some notebook systems may not expose motherboard, PCH, fan, voltage, or memory temperature sensors.
- Memory SPD details such as timings, channels, and module thermals may not be available through standard Windows APIs on all systems.
- SMART and NVMe details depend on controller, driver, permission, and firmware support.
- Some GPU, fan, power, and clock values may be absent or vendor-specific.
- Game FPS, 1% low, 0.1% low, frame time, GPU time, CPU busy time, and latency metrics require a working external capture component and compatible target process.
- Anti-cheat, protected processes, fullscreen mode, overlays, and graphics API differences may restrict or prevent game capture.
- This release is a pre-release build and should be treated as beta software.
