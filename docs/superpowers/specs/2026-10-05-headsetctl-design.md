# HeadsetCtl - Design

Date: 2026-10-05

## Goal

Connect and disconnect a paired Bluetooth headset (default: Sony WH-CH720N) with the same effect as the
"Connect" button in the Windows Bluetooth panel, from a tray app (click or Ctrl+Alt+H) and from a CLI.
The user can pick which paired audio device is controlled.

## Background (proven by `headset.ps1`)

- "On/off" means connect/disconnect an already-paired device; the PC cannot power the headset.
- Enabling/disabling the PnP device does NOT do this. `BluetoothSetServiceState` (bthprops.cpl) does:
  flag 1 = connect, 0 = disconnect, for the A2DP sink `0000110B-...`, Handsfree `0000111E-...` and
  Headset `00001108-...` service GUIDs (base `-0000-1000-8000-00805F9B34FB`).
- Connection state is `BLUETOOTH_DEVICE_INFO.fConnected` from `BluetoothGetDeviceInfo`.
- No admin rights needed. Error 87 from `SetServiceState` = service not supported by the device; treat as OK.

## Constraints and assumptions

- Windows 11, .NET `net9.0` (SDKs 9 and 10 installed; 8 is not).
- Own P/Invoke, no 32feet.NET dependency.
- Device must already be paired with Windows.
- No installer, no settings UI (YAGNI).

## Layout

```
SonyHP.sln
src/HeadsetCtl.Core/     class library: Bluetooth logic, settings
src/HeadsetCtl.Cli/      console exe: headset ...
src/HeadsetCtl.Tray/     WinForms tray exe
tests/HeadsetCtl.Tests/  xUnit
```

## Core library

- `IBluetoothApi`: `GetDeviceInfo(mac)`, `SetServiceState(mac, guid, enable)`, `EnumeratePairedDevices()`.
  `NativeBt` is the real implementation (P/Invoke: `BLUETOOTH_DEVICE_INFO`, `BluetoothGetDeviceInfo`,
  `BluetoothSetServiceState`, `BluetoothFindFirstDevice`/`FindNextDevice`/`FindDeviceClose`).
- `BtDevice` record: `Name`, `Mac` (ulong, formatted `AA:BB:CC:DD:EE:FF`), `Connected`, `IsAudio`.
- `DeviceLocator`: `ListPairedDevices(includeAll)`; audio filter = class-of-device major class Audio/Video.
  `Resolve(nameOrMac)` accepts a name (case-insensitive) or a MAC (`:`/`-`/none separators).
- `HeadsetController(IBluetoothApi)`: `GetState(mac)`, `Connect(mac)`, `Disconnect(mac)`, `Toggle(mac)`.
  Applies the three service GUIDs; ignores error 87; polls up to 3 s for the target state; other errors raise
  `BluetoothException` only if the target state was not reached (some services may fail while others succeed).
  Connect/Disconnect/Toggle are async (`Task.Run`) because the calls block 1-2 s.
- `Settings` (`%APPDATA%\HeadsetCtl\settings.json`, so the CLI and tray exes share one file): `DeviceMac` (string, default none -> falls back to a paired
  device named `WH-CH720N`), `Hotkey` (default `Ctrl+Alt+H`). Missing/invalid file -> defaults.
  Selected device is stored by MAC so same-named devices cannot be confused.

## CLI (`headset`)

```
headset list [--all]            table of paired devices: NAME, MAC, STATE
headset select <name-or-mac>    save default device to settings.json
headset status|on|off|toggle [--device <name-or-mac>]
```

- `list` shows audio devices only unless `--all`.
- `status` prints name, MAC and state.
- Exit codes: 0 ok, 1 error, 2 device not found.

## Tray app

- NotifyIcon; icon + tooltip reflect Connected / Disconnected / Warning (device missing or Bluetooth off).
- Left-click toggles. Right-click menu: Connect, Disconnect, Device submenu (paired audio devices, current
  ticked; choosing one saves its MAC to `settings.json`), Start with Windows (HKCU Run key), Exit.
- Global hotkey Ctrl+Alt+H via `RegisterHotKey`; on failure (already taken) show a balloon notification.
- Polls state every 5 s so the icon follows changes made with the headset's own button.
- Named mutex = single instance.
- Saved device no longer paired -> Warning icon + balloon asking the user to pick a device.
- Tray and CLI share `settings.json`.

## Error handling

CLI prints a message and returns a non-zero exit code. Tray shows balloon notifications and the Warning icon.
No exceptions escape the UI thread; connect/disconnect failures are reported, not crashed on.

## Testing

Unit tests (xUnit) against a fake `IBluetoothApi`: toggle logic, error-87 handling, other errors raise,
name/MAC resolution and MAC parsing/formatting, audio filter, settings defaults and round-trip, list formatting.
Real-hardware check (connect/disconnect/list/select, hotkey) is manual.

## Out of scope

Installer, multiple simultaneous devices, settings UI, non-audio device control.
