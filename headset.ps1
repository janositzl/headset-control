<#
.SYNOPSIS
  Connect / disconnect a paired Bluetooth headset (same as the Connect button
  in the Windows Bluetooth panel) by toggling its audio services.
.EXAMPLE
  .\headset.ps1 status
  .\headset.ps1 off
  .\headset.ps1 on
  .\headset.ps1 toggle
#>
param(
    [Parameter(Position = 0)]
    [ValidateSet('on', 'off', 'toggle', 'status')]
    [string]$Action = 'toggle',

    [string]$Name = 'WH-CH720N'
)

Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;

public static class BtApi {
    [StructLayout(LayoutKind.Sequential)]
    public struct SYSTEMTIME {
        public ushort wYear, wMonth, wDayOfWeek, wDay, wHour, wMinute, wSecond, wMilliseconds;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct BLUETOOTH_DEVICE_INFO {
        public int dwSize;
        public ulong Address;
        public uint ulClassofDevice;
        public int fConnected;
        public int fRemembered;
        public int fAuthenticated;
        public SYSTEMTIME stLastSeen;
        public SYSTEMTIME stLastUsed;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 248)]
        public string szName;
    }

    [DllImport("bthprops.cpl", SetLastError = true)]
    public static extern uint BluetoothGetDeviceInfo(IntPtr hRadio, ref BLUETOOTH_DEVICE_INFO pbtdi);

    [DllImport("bthprops.cpl", SetLastError = true)]
    public static extern uint BluetoothSetServiceState(IntPtr hRadio, ref BLUETOOTH_DEVICE_INFO pbtdi, ref Guid pGuidService, uint dwServiceFlags);

    public static BLUETOOTH_DEVICE_INFO GetInfo(ulong address) {
        var info = new BLUETOOTH_DEVICE_INFO();
        info.dwSize = Marshal.SizeOf(typeof(BLUETOOTH_DEVICE_INFO));
        info.Address = address;
        uint r = BluetoothGetDeviceInfo(IntPtr.Zero, ref info);
        if (r != 0) throw new System.ComponentModel.Win32Exception((int)r);
        return info;
    }

    // enable = true -> connect the service, false -> disconnect it
    public static uint SetService(ulong address, Guid service, bool enable) {
        var info = GetInfo(address);
        return BluetoothSetServiceState(IntPtr.Zero, ref info, ref service, enable ? 1u : 0u);
    }
}
'@

# Audio sink (A2DP), Handsfree and Headset profiles
$services = @(
    [Guid]'0000110B-0000-1000-8000-00805F9B34FB'
    [Guid]'0000111E-0000-1000-8000-00805F9B34FB'
    [Guid]'00001108-0000-1000-8000-00805F9B34FB'
)

# MAC address comes from the paired device's instance id (BTHENUM\DEV_<MAC>\...)
$dev = Get-PnpDevice -Class Bluetooth -ErrorAction SilentlyContinue |
    Where-Object { $_.FriendlyName -eq $Name -and $_.InstanceId -like 'BTHENUM\DEV_*' } |
    Select-Object -First 1
if (-not $dev) { Write-Error "Device '$Name' not found. Is it paired?"; exit 1 }

$address = [Convert]::ToUInt64(($dev.InstanceId -replace '^BTHENUM\\DEV_([0-9A-F]{12}).*$', '$1'), 16)

function Get-Connected { [bool][BtApi]::GetInfo($address).fConnected }

function Set-Connection([bool]$connect) {
    foreach ($svc in $services) {
        $r = [BtApi]::SetService($address, $svc, $connect)
        # 0 = ok, 87 = service not supported by this device (expected for some)
        if ($r -ne 0 -and $r -ne 87) { Write-Verbose "service $svc -> error $r" }
    }
}

$connected = Get-Connected

switch ($Action) {
    'status' { "${Name}: " + $(if ($connected) { 'Connected' } else { 'Disconnected' }); exit 0 }
    'on'     { $want = $true }
    'off'    { $want = $false }
    'toggle' { $want = -not $connected }
}

Set-Connection $want
Start-Sleep -Seconds 2
"${Name}: " + $(if (Get-Connected) { 'Connected' } else { 'Disconnected' })
