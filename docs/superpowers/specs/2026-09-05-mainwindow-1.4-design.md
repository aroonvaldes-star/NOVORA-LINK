# NOVORA-LINK 1.4 MainWindow Design

## Goal

Replace the previous MainWindow visual surface with the approved minimal matte-gray NOVORA 1.4 interface while preserving the existing device, monitor, LinkEngine, VisionEngine, settings, Wi-Fi ADB, refresh, performance and window-control behavior.

## Visual constraints

- Matte light-gray base: `#ECEFF2`.
- NOVORA blue: `#00AEEF`.
- No drop shadows or glow effects.
- Title bar contains only Settings, Minimize and Close.
- No bottom status/version bar. The lower boundary is LinkEngine + VisionEngine Start/Stop.
- Monitor selector contains only the monitor selection UI; no resolution summary below it.
- LinkEngine button visually displays only `LinkEngine`.
- Battery shows one percentage inside the battery graphic.
- Main sections remain RED, VIDEO and PERFORMANCE.

## Functional mapping

- Device ComboBox -> `Devices` / `Device` / `Device_SelectionChanged`.
- Wi-Fi button -> `WifiAdb_Click`.
- Refresh button -> `RefreshDevices_Click`.
- Monitor ComboBox -> `Monitors` / `SelectedMonitor` / `Monitor_SelectionChanged`.
- RED panel -> existing LinkEngine status controls.
- VIDEO panel -> bitrate, FPS and resolution bindings from `MainViewModel`.
- PERFORMANCE panel -> `DeviceMetricsService`, displayed independently as CPU, RAM, battery and temperature.
- Performance status card -> `DeviceFastPerformanceButton_Click`.
- LinkEngine button -> `LinkEngineTest_Click`.
- Main action -> `MainActionButton_Click` -> VisionEngine Start/Stop.

## VisionEngine renderer behavior

When stopped, the center VIDEO card displays configuration. On Start, `HostRendererVE` is created dynamically and inserted into `VideoRendererContainer`; the settings card is hidden and the actual VisionEngine renderer occupies the same center card. On Stop or failure, the renderer surface is hidden and the settings card returns.

## Scope

The design intentionally does not add a new executable, does not restore scrcpy as the Play backend, and does not change LinkEngine lifecycle behavior.
