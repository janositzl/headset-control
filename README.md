# HeadsetCtl

Connect / disconnect a paired Bluetooth headset (default: Sony WH-CH720N) exactly like the
**Connect** button in the Windows Bluetooth panel. The headset must already be paired.

## CLI (`dist/cli/headset.exe`)

    headset list [--all]               paired audio devices with MAC and state
    headset select <name-or-mac>       choose the device the other commands use
    headset status|on|off|toggle [--device <name-or-mac>]

Exit codes: 0 ok, 1 error, 2 device not found.

## Tray app (`dist/tray/HeadsetCtl.exe`)

- Left-click the icon or press **Ctrl+Alt+H** to toggle (green = connected, grey = disconnected, orange = problem).
- Right-click: Connect, Disconnect, Device (pick the headset), Start with Windows, Exit.

## Settings

`%APPDATA%\HeadsetCtl\settings.json`, shared by both programs:

    { "DeviceMac": "00:A4:1C:06:64:0C", "Hotkey": "Ctrl+Alt+H" }

Requires the .NET 9 (desktop) runtime.
