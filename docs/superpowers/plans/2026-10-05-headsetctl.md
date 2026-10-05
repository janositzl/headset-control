# HeadsetCtl Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A .NET tray app (click + Ctrl+Alt+H) and a CLI that connect/disconnect a chosen paired Bluetooth audio device exactly like the Windows panel's Connect button.

**Architecture:** One class library (`HeadsetCtl.Core`) holds all Bluetooth logic behind an `IBluetoothApi` interface (real P/Invoke implementation `NativeBt`, fake in tests). A console exe (`headset`) and a WinForms tray exe sit on top. Both share `%APPDATA%\HeadsetCtl\settings.json`.

**Tech Stack:** .NET 9 (`net9.0` / `net9.0-windows`), WinForms, P/Invoke into `bthprops.cpl`, xUnit.

**Spec:** `docs/superpowers/specs/2026-10-05-headsetctl-design.md` (proven reference implementation: `headset.ps1`)

## Global Constraints

- Target framework `net9.0` (Core, Cli, Tests) and `net9.0-windows` (Tray). .NET 8 is NOT installed (SDKs 9 and 10 are).
- No third-party runtime dependencies (no 32feet.NET). Only xUnit for tests.
- Connect = `BluetoothSetServiceState(..., flag 1)`, disconnect = flag 0, on service GUIDs A2DP sink `0000110B-0000-1000-8000-00805F9B34FB`, Handsfree `0000111E-0000-1000-8000-00805F9B34FB`, Headset `00001108-0000-1000-8000-00805F9B34FB`. Never use Enable/Disable-PnpDevice.
- Win32 error 87 from `SetServiceState` = service unsupported = OK (ignore).
- State = `BLUETOOTH_DEVICE_INFO.fConnected`. No admin rights required.
- Audio device filter = class-of-device major class `0x04` (`(cod >> 8) & 0x1F`).
- Default device name `WH-CH720N`; default hotkey `Ctrl+Alt+H`; settings file `%APPDATA%\HeadsetCtl\settings.json`; selected device stored by MAC (`AA:BB:CC:DD:EE:FF`).
- CLI exit codes: 0 ok, 1 error, 2 device not found. CLI exe name `headset`; tray exe name `HeadsetCtl`.
- Tray polls every 5 s; single instance via named mutex; no exception may escape the UI thread.
- No installer, no settings UI, one active device at a time (YAGNI).

## Review Focus

1. MAC input in odd formats (lowercase, `-` separators, no separators, wrong length, mixed separators, non-hex) must parse or be rejected cleanly - tests in Task 1.
2. Two paired devices with the same name: selecting by name must fail with a message listing their MACs, never silently pick one - tests in Task 2.
3. A corrupt, empty or half-filled `settings.json` (e.g. `"Hotkey": null`) must fall back to defaults, never crash either exe - tests in Task 4.
4. Invalid or unsupported hotkey strings (`H`, `Ctrl+Alt`, `Ctrl+H+J`, `Ctrl+Alt+Foo`) must be rejected, with the tray falling back to Ctrl+Alt+H - tests in Task 5, tray fallback in Task 8.
5. Bluetooth off / no radio / no paired devices / unnamed device: `list` must print a friendly message or `(unnamed)`, not crash - tests in Task 7.

---

### Task 1: Solution scaffold and `MacAddress`

**Files:**
- Create: `SonyHP.slnx` (or `SonyHP.sln` on older SDKs), `.gitignore`
- Create: `src/HeadsetCtl.Core/HeadsetCtl.Core.csproj`, `src/HeadsetCtl.Core/MacAddress.cs`
- Create: `src/HeadsetCtl.Cli/HeadsetCtl.Cli.csproj`, `src/HeadsetCtl.Tray/HeadsetCtl.Tray.csproj`, `tests/HeadsetCtl.Tests/HeadsetCtl.Tests.csproj`
- Test: `tests/HeadsetCtl.Tests/MacAddressTests.cs`

**Interfaces:**
- Produces: `static class MacAddress { string Format(ulong address); bool TryParse(string? text, out ulong address); }` in namespace `HeadsetCtl.Core`. `Format(0x00A41C06640C) == "00:A4:1C:06:64:0C"`.

- [ ] **Step 1: Scaffold the solution**

Run from `C:\Work\SonyHP`:
```powershell
git init
dotnet new gitignore
dotnet new sln -n SonyHP
dotnet new classlib -n HeadsetCtl.Core -o src/HeadsetCtl.Core -f net9.0
dotnet new console  -n HeadsetCtl.Cli  -o src/HeadsetCtl.Cli  -f net9.0
dotnet new winforms -n HeadsetCtl.Tray -o src/HeadsetCtl.Tray -f net9.0
dotnet new xunit    -n HeadsetCtl.Tests -o tests/HeadsetCtl.Tests -f net9.0
dotnet sln add src/HeadsetCtl.Core src/HeadsetCtl.Cli src/HeadsetCtl.Tray tests/HeadsetCtl.Tests
dotnet add src/HeadsetCtl.Cli reference src/HeadsetCtl.Core
dotnet add src/HeadsetCtl.Tray reference src/HeadsetCtl.Core
dotnet add tests/HeadsetCtl.Tests reference src/HeadsetCtl.Core src/HeadsetCtl.Cli
Remove-Item src/HeadsetCtl.Core/Class1.cs, src/HeadsetCtl.Tray/Form1.cs, src/HeadsetCtl.Tray/Form1.Designer.cs, src/HeadsetCtl.Tray/Form1.resx -ErrorAction SilentlyContinue
```
In `src/HeadsetCtl.Cli/HeadsetCtl.Cli.csproj` add `<AssemblyName>headset</AssemblyName>` inside the first `<PropertyGroup>`. In `src/HeadsetCtl.Tray/HeadsetCtl.Tray.csproj` add `<AssemblyName>HeadsetCtl</AssemblyName>`.
Delete any template test file in `tests/HeadsetCtl.Tests` (e.g. `UnitTest1.cs`).
Expected: `dotnet build` may fail only because Tray has no entry point yet; proceed (Task 8 adds it). To keep the build green meanwhile, create `src/HeadsetCtl.Tray/Program.cs` containing `internal static class Program { [STAThread] static void Main() { } }`.

- [ ] **Step 2: Write the failing test**

`tests/HeadsetCtl.Tests/MacAddressTests.cs`:
```csharp
using HeadsetCtl.Core;

namespace HeadsetCtl.Tests;

public class MacAddressTests
{
    [Fact]
    public void Format_UsesUppercaseColonSeparatedPairs() =>
        Assert.Equal("00:A4:1C:06:64:0C", MacAddress.Format(0x00A41C06640C));

    [Theory]
    [InlineData("00:A4:1C:06:64:0C")]
    [InlineData("00-a4-1c-06-64-0c")]
    [InlineData("00A41C06640C")]
    [InlineData("00a41c06640c")]
    [InlineData("  00:a4:1c:06:64:0c  ")]
    public void TryParse_AcceptsCommonFormats(string text)
    {
        Assert.True(MacAddress.TryParse(text, out var address));
        Assert.Equal(0x00A41C06640CUL, address);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("00:A4:1C:06:64")]          // too short
    [InlineData("00:A4:1C:06:64:0C:11")]    // too long
    [InlineData("ZZ:A4:1C:06:64:0C")]       // not hex
    [InlineData("00:A4-1C:06:64:0C")]       // mixed separators
    [InlineData("00A41C06640")]             // 11 digits
    [InlineData("+0A41C06640C")]            // sign is not a hex digit
    [InlineData("WH-CH720N")]               // a device name
    public void TryParse_RejectsInvalidInput(string? text)
    {
        Assert.False(MacAddress.TryParse(text, out var address));
        Assert.Equal(0UL, address);
    }
}
```

- [ ] **Step 3: Run test to verify it fails**

Run: `dotnet test`
Expected: build FAIL, `MacAddress` does not exist.

- [ ] **Step 4: Write minimal implementation**

`src/HeadsetCtl.Core/MacAddress.cs`:
```csharp
using System.Globalization;

namespace HeadsetCtl.Core;

public static class MacAddress
{
    public static string Format(ulong address)
    {
        var bytes = new string[6];
        for (int i = 0; i < 6; i++)
            bytes[i] = ((address >> (8 * (5 - i))) & 0xFF).ToString("X2", CultureInfo.InvariantCulture);
        return string.Join(':', bytes);
    }

    public static bool TryParse(string? text, out ulong address)
    {
        address = 0;
        if (string.IsNullOrWhiteSpace(text)) return false;

        var s = text.Trim();
        string hex = s;
        if (s.Length == 17 && (s[2] == ':' || s[2] == '-'))
        {
            var parts = s.Split(s[2]);
            if (parts.Length != 6 || parts.Any(p => p.Length != 2)) return false;
            hex = string.Concat(parts);
        }

        if (hex.Length != 12) return false;
        return ulong.TryParse(hex, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out address);
    }
}
```

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test`
Expected: PASS (all MacAddress tests).

- [ ] **Step 6: Commit**
```bash
git add -A
git commit -m "chore: scaffold solution and add MacAddress"
```

---

### Task 2: Device model, `IBluetoothApi`, `DeviceLocator`

**Files:**
- Create: `src/HeadsetCtl.Core/BtDevice.cs`, `src/HeadsetCtl.Core/BluetoothExceptions.cs`, `src/HeadsetCtl.Core/IBluetoothApi.cs`, `src/HeadsetCtl.Core/DeviceLocator.cs`
- Create: `tests/HeadsetCtl.Tests/FakeBluetoothApi.cs`
- Test: `tests/HeadsetCtl.Tests/DeviceLocatorTests.cs`

**Interfaces:**
- Consumes: `MacAddress` (Task 1).
- Produces (namespace `HeadsetCtl.Core`):
  - `record BtDevice(string Name, ulong Address, bool Connected, uint ClassOfDevice)` with `bool IsAudio`, `string Mac`.
  - `class BluetoothException : Exception`; `sealed class DeviceNotFoundException : BluetoothException`; `sealed class AmbiguousDeviceException : BluetoothException`.
  - `interface IBluetoothApi { BtDevice? GetDevice(ulong address); uint SetServiceState(ulong address, Guid service, bool enable); IReadOnlyList<BtDevice> EnumeratePaired(); }` (`GetDevice` returns null when the device is not known; `SetServiceState` returns the Win32 error code, 0 = ok).
  - `sealed class DeviceLocator(IBluetoothApi api)`: `const string DefaultDeviceName = "WH-CH720N"`, `IReadOnlyList<BtDevice> ListPaired(bool includeAll = false)`, `BtDevice Resolve(string nameOrMac)`, `BtDevice ResolveSelected(string? deviceMac)`.
- Test helpers (namespace `HeadsetCtl.Tests`): `FakeBluetoothApi` (`List<BtDevice> Devices`, `Dictionary<Guid,uint> ServiceResults`, `List<(ulong Address, Guid Service, bool Enable)> Calls`, `bool ApplyStateChange = true`) and `static class Fake { BtDevice Audio(string name, ulong address, bool connected = false); BtDevice Phone(string name, ulong address, bool connected = false); }`.

- [ ] **Step 1: Write the test helpers and failing tests**

`tests/HeadsetCtl.Tests/FakeBluetoothApi.cs`:
```csharp
using HeadsetCtl.Core;

namespace HeadsetCtl.Tests;

public static class Fake
{
    public static BtDevice Audio(string name, ulong address, bool connected = false) =>
        new(name, address, connected, 0x240418);   // major class 0x04 = Audio/Video (headphones)

    public static BtDevice Phone(string name, ulong address, bool connected = false) =>
        new(name, address, connected, 0x5A020C);   // major class 0x02 = Phone
}

public sealed class FakeBluetoothApi : IBluetoothApi
{
    public List<BtDevice> Devices { get; } = new();
    public Dictionary<Guid, uint> ServiceResults { get; } = new();
    public List<(ulong Address, Guid Service, bool Enable)> Calls { get; } = new();
    public bool ApplyStateChange { get; set; } = true;

    public BtDevice? GetDevice(ulong address) => Devices.FirstOrDefault(d => d.Address == address);

    public uint SetServiceState(ulong address, Guid service, bool enable)
    {
        Calls.Add((address, service, enable));
        var code = ServiceResults.TryGetValue(service, out var c) ? c : 0u;
        if (code == 0 && ApplyStateChange)
        {
            var i = Devices.FindIndex(d => d.Address == address);
            if (i >= 0) Devices[i] = Devices[i] with { Connected = enable };
        }
        return code;
    }

    public IReadOnlyList<BtDevice> EnumeratePaired() => Devices.ToList();
}
```

`tests/HeadsetCtl.Tests/DeviceLocatorTests.cs`:
```csharp
using HeadsetCtl.Core;

namespace HeadsetCtl.Tests;

public class DeviceLocatorTests
{
    private static readonly BtDevice Headset = Fake.Audio("WH-CH720N", 0x00A41C06640C);
    private static readonly BtDevice Other = Fake.Audio("Other-BT", 0xAABBCC112233, connected: true);
    private static readonly BtDevice Phone = Fake.Phone("Pixel", 0x112233445566);

    private static DeviceLocator Locator(params BtDevice[] devices)
    {
        var api = new FakeBluetoothApi();
        api.Devices.AddRange(devices);
        return new DeviceLocator(api);
    }

    [Fact]
    public void BtDevice_IsAudio_UsesMajorDeviceClass()
    {
        Assert.True(Headset.IsAudio);
        Assert.False(Phone.IsAudio);
        Assert.Equal("00:A4:1C:06:64:0C", Headset.Mac);
    }

    [Fact]
    public void ListPaired_ReturnsOnlyAudioDevicesSortedByName()
    {
        var list = Locator(Phone, Headset, Other).ListPaired();
        Assert.Equal(new[] { "Other-BT", "WH-CH720N" }, list.Select(d => d.Name));
    }

    [Fact]
    public void ListPaired_All_IncludesNonAudio() =>
        Assert.Equal(3, Locator(Phone, Headset, Other).ListPaired(includeAll: true).Count);

    [Fact]
    public void ListPaired_NoDevices_ReturnsEmpty() =>
        Assert.Empty(Locator().ListPaired());

    [Fact]
    public void Resolve_ByName_IsCaseInsensitive() =>
        Assert.Equal(Headset, Locator(Headset, Other).Resolve("wh-ch720n"));

    [Fact]
    public void Resolve_ByMac_AcceptsAnyFormat() =>
        Assert.Equal(Headset, Locator(Headset, Other).Resolve("00-a4-1c-06-64-0c"));

    [Fact]
    public void Resolve_UnknownName_Throws() =>
        Assert.Throws<DeviceNotFoundException>(() => Locator(Headset).Resolve("Nope"));

    [Fact]
    public void Resolve_UnknownMac_Throws() =>
        Assert.Throws<DeviceNotFoundException>(() => Locator(Headset).Resolve("11:22:33:44:55:66"));

    [Fact]
    public void Resolve_DuplicateName_ThrowsAmbiguousListingMacs()
    {
        var twin = Fake.Audio("WH-CH720N", 0x0000000000AA);
        var ex = Assert.Throws<AmbiguousDeviceException>(() => Locator(Headset, twin).Resolve("WH-CH720N"));
        Assert.Contains("00:A4:1C:06:64:0C", ex.Message);
        Assert.Contains("00:00:00:00:00:AA", ex.Message);
    }

    [Fact]
    public void Resolve_DuplicateName_CanStillBeChosenByMac() =>
        Assert.Equal(0xAAUL, Locator(Headset, Fake.Audio("WH-CH720N", 0xAA)).Resolve("00:00:00:00:00:AA").Address);

    [Fact]
    public void ResolveSelected_WithoutSavedMac_UsesDefaultName() =>
        Assert.Equal(Headset, Locator(Other, Headset).ResolveSelected(null));

    [Fact]
    public void ResolveSelected_WithSavedMac_UsesIt() =>
        Assert.Equal(Other, Locator(Other, Headset).ResolveSelected("AA:BB:CC:11:22:33"));
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test`
Expected: build FAIL, `BtDevice`, `DeviceLocator` etc. do not exist.

- [ ] **Step 3: Write minimal implementation**

`src/HeadsetCtl.Core/BtDevice.cs`:
```csharp
namespace HeadsetCtl.Core;

public sealed record BtDevice(string Name, ulong Address, bool Connected, uint ClassOfDevice)
{
    private const uint MajorClassAudioVideo = 0x04;

    public bool IsAudio => ((ClassOfDevice >> 8) & 0x1F) == MajorClassAudioVideo;
    public string Mac => MacAddress.Format(Address);
}
```

`src/HeadsetCtl.Core/BluetoothExceptions.cs`:
```csharp
namespace HeadsetCtl.Core;

public class BluetoothException : Exception
{
    public BluetoothException(string message) : base(message) { }
    public BluetoothException(string message, Exception inner) : base(message, inner) { }
}

public sealed class DeviceNotFoundException(string message) : BluetoothException(message);

public sealed class AmbiguousDeviceException(string message) : BluetoothException(message);
```

`src/HeadsetCtl.Core/IBluetoothApi.cs`:
```csharp
namespace HeadsetCtl.Core;

public interface IBluetoothApi
{
    /// <summary>Returns the device, or null if Windows does not know it.</summary>
    BtDevice? GetDevice(ulong address);

    /// <summary>Win32 error code; 0 = success. enable=true connects the service, false disconnects it.</summary>
    uint SetServiceState(ulong address, Guid service, bool enable);

    /// <summary>All paired (authenticated or remembered) devices. Empty when there is no radio.</summary>
    IReadOnlyList<BtDevice> EnumeratePaired();
}
```

`src/HeadsetCtl.Core/DeviceLocator.cs`:
```csharp
namespace HeadsetCtl.Core;

public sealed class DeviceLocator(IBluetoothApi api)
{
    public const string DefaultDeviceName = "WH-CH720N";

    public IReadOnlyList<BtDevice> ListPaired(bool includeAll = false) =>
        api.EnumeratePaired()
            .Where(d => includeAll || d.IsAudio)
            .OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

    public BtDevice Resolve(string nameOrMac)
    {
        var all = api.EnumeratePaired();

        if (MacAddress.TryParse(nameOrMac, out var address))
        {
            return all.FirstOrDefault(d => d.Address == address)
                ?? throw new DeviceNotFoundException($"No paired device with address {MacAddress.Format(address)}.");
        }

        var matches = all
            .Where(d => string.Equals(d.Name, nameOrMac, StringComparison.OrdinalIgnoreCase))
            .ToList();

        return matches.Count switch
        {
            0 => throw new DeviceNotFoundException($"No paired device named '{nameOrMac}'."),
            1 => matches[0],
            _ => throw new AmbiguousDeviceException(
                $"Several paired devices are named '{nameOrMac}'; use a MAC address instead ({string.Join(", ", matches.Select(m => m.Mac))})."),
        };
    }

    public BtDevice ResolveSelected(string? deviceMac) =>
        Resolve(string.IsNullOrWhiteSpace(deviceMac) ? DefaultDeviceName : deviceMac);
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test`
Expected: PASS.

- [ ] **Step 5: Commit**
```bash
git add -A
git commit -m "feat: device model, IBluetoothApi and DeviceLocator"
```

---

### Task 3: `HeadsetController`

**Files:**
- Create: `src/HeadsetCtl.Core/HeadsetController.cs`
- Test: `tests/HeadsetCtl.Tests/HeadsetControllerTests.cs`

**Interfaces:**
- Consumes: `IBluetoothApi`, `BtDevice`, `BluetoothException`, `DeviceNotFoundException`, test `FakeBluetoothApi`/`Fake` (Task 2).
- Produces:
  ```csharp
  public sealed class HeadsetController
  {
      public static readonly Guid A2dpSink, Handsfree, Headset;   // the three service GUIDs
      public static readonly Guid[] AudioServices;                // { A2dpSink, Handsfree, Headset }
      public HeadsetController(IBluetoothApi api, TimeSpan? settleTimeout = null, TimeSpan? pollInterval = null); // defaults 3 s / 200 ms
      public bool IsConnected(ulong address);                     // throws DeviceNotFoundException if unknown
      public Task<bool> SetConnectedAsync(ulong address, bool connect, CancellationToken ct = default); // returns the final observed state
      public Task<bool> ToggleAsync(ulong address, CancellationToken ct = default);                      // returns the new state
  }
  ```
  Behaviour: calls `SetServiceState` for each service (on a worker thread), ignoring 0 and 87; then polls `IsConnected` until it equals the target or the settle timeout passes. If the target is not reached AND a non-0/87 error was seen, throws `BluetoothException` naming the error code; if no error was seen it just returns the actual state.

- [ ] **Step 1: Write the failing tests**

`tests/HeadsetCtl.Tests/HeadsetControllerTests.cs`:
```csharp
using HeadsetCtl.Core;

namespace HeadsetCtl.Tests;

public class HeadsetControllerTests
{
    private const ulong Addr = 0x00A41C06640C;

    private static (HeadsetController ctl, FakeBluetoothApi api) Make(bool connected = false)
    {
        var api = new FakeBluetoothApi();
        api.Devices.Add(Fake.Audio("WH-CH720N", Addr, connected));
        return (new HeadsetController(api, TimeSpan.Zero, TimeSpan.Zero), api);
    }

    [Fact]
    public async Task Connect_EnablesAllThreeServices()
    {
        var (ctl, api) = Make();
        Assert.True(await ctl.SetConnectedAsync(Addr, true));
        Assert.Equal(HeadsetController.AudioServices, api.Calls.Select(c => c.Service));
        Assert.All(api.Calls, c => Assert.True(c.Enable));
    }

    [Fact]
    public async Task Disconnect_DisablesAllThreeServices()
    {
        var (ctl, api) = Make(connected: true);
        Assert.False(await ctl.SetConnectedAsync(Addr, false));
        Assert.Equal(3, api.Calls.Count);
        Assert.All(api.Calls, c => Assert.False(c.Enable));
    }

    [Fact]
    public void Services_AreTheExpectedGuids()
    {
        Assert.Equal(Guid.Parse("0000110B-0000-1000-8000-00805F9B34FB"), HeadsetController.A2dpSink);
        Assert.Equal(Guid.Parse("0000111E-0000-1000-8000-00805F9B34FB"), HeadsetController.Handsfree);
        Assert.Equal(Guid.Parse("00001108-0000-1000-8000-00805F9B34FB"), HeadsetController.Headset);
    }

    [Fact]
    public async Task Error87_UnsupportedService_IsIgnored()
    {
        var (ctl, api) = Make();
        api.ServiceResults[HeadsetController.Handsfree] = 87;
        api.ServiceResults[HeadsetController.Headset] = 87;
        Assert.True(await ctl.SetConnectedAsync(Addr, true));
    }

    [Fact]
    public async Task OtherError_WhenStateNotReached_Throws()
    {
        var (ctl, api) = Make();
        foreach (var g in HeadsetController.AudioServices) api.ServiceResults[g] = 5;
        var ex = await Assert.ThrowsAsync<BluetoothException>(() => ctl.SetConnectedAsync(Addr, true));
        Assert.Contains("5", ex.Message);
    }

    [Fact]
    public async Task OtherError_ButStateReached_DoesNotThrow()
    {
        var (ctl, api) = Make();
        api.ServiceResults[HeadsetController.Handsfree] = 5;
        api.ServiceResults[HeadsetController.Headset] = 5;
        Assert.True(await ctl.SetConnectedAsync(Addr, true));   // A2DP succeeded and flipped the state
    }

    [Fact]
    public async Task NoError_ButStateNotReached_ReturnsActualState()
    {
        var (ctl, api) = Make();
        api.ApplyStateChange = false;
        Assert.False(await ctl.SetConnectedAsync(Addr, true));
    }

    [Fact]
    public async Task Toggle_WhenConnected_Disconnects()
    {
        var (ctl, _) = Make(connected: true);
        Assert.False(await ctl.ToggleAsync(Addr));
    }

    [Fact]
    public async Task Toggle_WhenDisconnected_Connects()
    {
        var (ctl, _) = Make(connected: false);
        Assert.True(await ctl.ToggleAsync(Addr));
    }

    [Fact]
    public void IsConnected_UnknownDevice_Throws()
    {
        var (ctl, _) = Make();
        Assert.Throws<DeviceNotFoundException>(() => ctl.IsConnected(0x1));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter HeadsetControllerTests`
Expected: build FAIL, `HeadsetController` does not exist.

- [ ] **Step 3: Write minimal implementation**

`src/HeadsetCtl.Core/HeadsetController.cs`:
```csharp
namespace HeadsetCtl.Core;

public sealed class HeadsetController
{
    private const uint ErrorInvalidParameter = 87;   // service not supported by this device

    public static readonly Guid A2dpSink = Guid.Parse("0000110B-0000-1000-8000-00805F9B34FB");
    public static readonly Guid Handsfree = Guid.Parse("0000111E-0000-1000-8000-00805F9B34FB");
    public static readonly Guid Headset = Guid.Parse("00001108-0000-1000-8000-00805F9B34FB");
    public static readonly Guid[] AudioServices = { A2dpSink, Handsfree, Headset };

    private readonly IBluetoothApi _api;
    private readonly TimeSpan _settleTimeout;
    private readonly TimeSpan _pollInterval;

    public HeadsetController(IBluetoothApi api, TimeSpan? settleTimeout = null, TimeSpan? pollInterval = null)
    {
        _api = api;
        _settleTimeout = settleTimeout ?? TimeSpan.FromSeconds(3);
        _pollInterval = pollInterval ?? TimeSpan.FromMilliseconds(200);
    }

    public bool IsConnected(ulong address) =>
        (_api.GetDevice(address) ?? throw new DeviceNotFoundException($"Device {MacAddress.Format(address)} is not known to Windows."))
            .Connected;

    public async Task<bool> SetConnectedAsync(ulong address, bool connect, CancellationToken ct = default)
    {
        // The service calls block for a second or two, so keep them off the caller's (UI) thread.
        uint firstError = await Task.Run(() =>
        {
            uint error = 0;
            foreach (var service in AudioServices)
            {
                var r = _api.SetServiceState(address, service, connect);
                if (r != 0 && r != ErrorInvalidParameter && error == 0) error = r;
            }
            return error;
        }, ct);

        var deadline = DateTime.UtcNow + _settleTimeout;
        while (true)
        {
            var state = IsConnected(address);
            if (state == connect) return state;
            if (DateTime.UtcNow >= deadline)
            {
                if (firstError != 0)
                    throw new BluetoothException(
                        $"Could not {(connect ? "connect" : "disconnect")} {MacAddress.Format(address)} (Windows error {firstError}).");
                return state;
            }
            await Task.Delay(_pollInterval, ct);
        }
    }

    public async Task<bool> ToggleAsync(ulong address, CancellationToken ct = default) =>
        await SetConnectedAsync(address, !IsConnected(address), ct);
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test`
Expected: PASS.

- [ ] **Step 5: Commit**
```bash
git add -A
git commit -m "feat: HeadsetController connect/disconnect/toggle"
```

---

### Task 4: `Settings` and `SettingsStore`

**Files:**
- Create: `src/HeadsetCtl.Core/Settings.cs`
- Test: `tests/HeadsetCtl.Tests/SettingsStoreTests.cs`

**Interfaces:**
- Produces:
  ```csharp
  public sealed class Settings { public const string DefaultHotkey = "Ctrl+Alt+H"; public string? DeviceMac { get; set; } public string Hotkey { get; set; } = DefaultHotkey; }
  public sealed class SettingsStore(string path) { public static string DefaultPath { get; } // %APPDATA%\HeadsetCtl\settings.json
      public Settings Load();   // never throws for missing/corrupt/empty file; returns defaults
      public void Save(Settings settings); // creates the directory }
  ```

- [ ] **Step 1: Write the failing tests**

`tests/HeadsetCtl.Tests/SettingsStoreTests.cs`:
```csharp
using HeadsetCtl.Core;

namespace HeadsetCtl.Tests;

public class SettingsStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "HeadsetCtlTests-" + Guid.NewGuid());
    private string FilePath => Path.Combine(_dir, "sub", "settings.json");

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    private void WriteRaw(string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, content);
    }

    [Fact]
    public void Load_MissingFile_ReturnsDefaults()
    {
        var s = new SettingsStore(FilePath).Load();
        Assert.Null(s.DeviceMac);
        Assert.Equal("Ctrl+Alt+H", s.Hotkey);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json at all")]
    [InlineData("{ \"Hotkey\": ")]        // truncated
    [InlineData("[1,2,3]")]               // wrong shape
    [InlineData("null")]
    public void Load_EmptyOrCorruptFile_ReturnsDefaults(string content)
    {
        WriteRaw(content);
        var s = new SettingsStore(FilePath).Load();
        Assert.Null(s.DeviceMac);
        Assert.Equal("Ctrl+Alt+H", s.Hotkey);
    }

    [Fact]
    public void Load_NullOrBlankHotkey_FallsBackToDefault()
    {
        WriteRaw("{ \"DeviceMac\": \"AA:BB:CC:11:22:33\", \"Hotkey\": null }");
        var s = new SettingsStore(FilePath).Load();
        Assert.Equal("AA:BB:CC:11:22:33", s.DeviceMac);
        Assert.Equal("Ctrl+Alt+H", s.Hotkey);
    }

    [Fact]
    public void Save_ThenLoad_RoundTrips_AndCreatesDirectory()
    {
        var store = new SettingsStore(FilePath);
        store.Save(new Settings { DeviceMac = "00:A4:1C:06:64:0C", Hotkey = "Ctrl+Shift+F9" });
        var s = store.Load();
        Assert.Equal("00:A4:1C:06:64:0C", s.DeviceMac);
        Assert.Equal("Ctrl+Shift+F9", s.Hotkey);
    }

    [Fact]
    public void DefaultPath_IsUnderAppData() =>
        Assert.EndsWith(Path.Combine("HeadsetCtl", "settings.json"), SettingsStore.DefaultPath);
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter SettingsStoreTests`
Expected: build FAIL, `Settings` does not exist.

- [ ] **Step 3: Write minimal implementation**

`src/HeadsetCtl.Core/Settings.cs`:
```csharp
using System.Text.Json;

namespace HeadsetCtl.Core;

public sealed class Settings
{
    public const string DefaultHotkey = "Ctrl+Alt+H";

    public string? DeviceMac { get; set; }
    public string Hotkey { get; set; } = DefaultHotkey;
}

public sealed class SettingsStore(string path)
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static string DefaultPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "HeadsetCtl", "settings.json");

    public Settings Load()
    {
        try
        {
            if (!File.Exists(path)) return new Settings();
            var settings = JsonSerializer.Deserialize<Settings>(File.ReadAllText(path), Options) ?? new Settings();
            if (string.IsNullOrWhiteSpace(settings.Hotkey)) settings.Hotkey = Settings.DefaultHotkey;
            return settings;
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
            return new Settings();
        }
    }

    public void Save(Settings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(settings, Options));
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test`
Expected: PASS. (If `[1,2,3]` throws a `JsonException`, it is already covered; if it throws another exception type, add it to the `when` filter.)

- [ ] **Step 5: Commit**
```bash
git add -A
git commit -m "feat: Settings and SettingsStore"
```

---

### Task 5: `HotkeyParser`

**Files:**
- Create: `src/HeadsetCtl.Core/HotkeyParser.cs`
- Test: `tests/HeadsetCtl.Tests/HotkeyParserTests.cs`

**Interfaces:**
- Produces:
  ```csharp
  public readonly record struct Hotkey(uint Modifiers, uint VirtualKey);
  public static class HotkeyParser {
      public const uint Alt = 1, Control = 2, Shift = 4, Win = 8;   // RegisterHotKey MOD_* values
      public static bool TryParse(string? text, out Hotkey hotkey);
  }
  ```
  Format: one or more distinct modifiers (`Ctrl`/`Control`, `Alt`, `Shift`, `Win`), then exactly one key last (A-Z, 0-9, F1-F24), joined by `+`, case-insensitive, whitespace around tokens ignored. `Ctrl+Alt+H` -> `Hotkey(3, 0x48)`.

- [ ] **Step 1: Write the failing tests**

`tests/HeadsetCtl.Tests/HotkeyParserTests.cs`:
```csharp
using HeadsetCtl.Core;

namespace HeadsetCtl.Tests;

public class HotkeyParserTests
{
    [Theory]
    [InlineData("Ctrl+Alt+H", HotkeyParser.Control | HotkeyParser.Alt, 0x48u)]
    [InlineData("ctrl + alt + h", HotkeyParser.Control | HotkeyParser.Alt, 0x48u)]
    [InlineData("Control+Shift+F9", HotkeyParser.Control | HotkeyParser.Shift, 0x78u)]
    [InlineData("Win+Alt+5", HotkeyParser.Win | HotkeyParser.Alt, 0x35u)]
    [InlineData("Alt+F1", HotkeyParser.Alt, 0x70u)]
    [InlineData("Ctrl+F24", HotkeyParser.Control, 0x87u)]
    public void TryParse_ValidHotkeys(string text, uint modifiers, uint vk)
    {
        Assert.True(HotkeyParser.TryParse(text, out var hk));
        Assert.Equal(new Hotkey(modifiers, vk), hk);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("H")]                 // no modifier
    [InlineData("Ctrl+Alt")]          // no key
    [InlineData("Ctrl+H+J")]          // two keys
    [InlineData("H+Ctrl")]            // key not last
    [InlineData("Ctrl+Ctrl+H")]       // duplicate modifier
    [InlineData("Ctrl+Alt+Foo")]      // unknown key
    [InlineData("Ctrl+F25")]          // out of range
    [InlineData("Ctrl+F0")]
    [InlineData("Ctrl++H")]           // empty token
    [InlineData("Ctrl+Alt+")]         // trailing separator
    public void TryParse_InvalidHotkeys(string? text)
    {
        Assert.False(HotkeyParser.TryParse(text, out _));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter HotkeyParserTests`
Expected: build FAIL, `HotkeyParser` does not exist.

- [ ] **Step 3: Write minimal implementation**

`src/HeadsetCtl.Core/HotkeyParser.cs`:
```csharp
namespace HeadsetCtl.Core;

public readonly record struct Hotkey(uint Modifiers, uint VirtualKey);

public static class HotkeyParser
{
    public const uint Alt = 1, Control = 2, Shift = 4, Win = 8;   // RegisterHotKey MOD_* values

    public static bool TryParse(string? text, out Hotkey hotkey)
    {
        hotkey = default;
        if (string.IsNullOrWhiteSpace(text)) return false;

        var tokens = text.Split('+', StringSplitOptions.TrimEntries);
        if (tokens.Length < 2 || tokens.Any(t => t.Length == 0)) return false;

        uint modifiers = 0;
        for (int i = 0; i < tokens.Length - 1; i++)
        {
            uint flag = tokens[i].ToLowerInvariant() switch
            {
                "ctrl" or "control" => Control,
                "alt" => Alt,
                "shift" => Shift,
                "win" => Win,
                _ => 0,
            };
            if (flag == 0 || (modifiers & flag) != 0) return false;
            modifiers |= flag;
        }

        if (!TryParseKey(tokens[^1], out var vk)) return false;
        hotkey = new Hotkey(modifiers, vk);
        return true;
    }

    private static bool TryParseKey(string token, out uint vk)
    {
        vk = 0;
        if (token.Length == 1)
        {
            var c = char.ToUpperInvariant(token[0]);
            if ((c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9')) { vk = c; return true; }
            return false;
        }

        if ((token[0] == 'F' || token[0] == 'f') && int.TryParse(token.AsSpan(1), out var n) && n >= 1 && n <= 24)
        {
            vk = (uint)(0x70 + n - 1);
            return true;
        }
        return false;
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test`
Expected: PASS.

- [ ] **Step 5: Commit**
```bash
git add -A
git commit -m "feat: HotkeyParser"
```

---

### Task 6: `NativeBt` (real Win32 implementation)

**Files:**
- Create: `src/HeadsetCtl.Core/NativeBt.cs`

**Interfaces:**
- Consumes: `IBluetoothApi`, `BtDevice`, `BluetoothException` (Task 2).
- Produces: `public sealed class NativeBt : IBluetoothApi` (parameterless constructor). Ported from the proven P/Invoke in `headset.ps1`. `GetDevice` returns null on Win32 error 1168 (not found) and throws `BluetoothException` on other errors. `EnumeratePaired` returns an empty list when there is no radio / no devices.
- No unit test: it needs hardware. Verified by the smoke check in Step 2 and by Task 7's manual check.

- [ ] **Step 1: Write the implementation**

`src/HeadsetCtl.Core/NativeBt.cs`:
```csharp
using System.Runtime.InteropServices;

namespace HeadsetCtl.Core;

public sealed class NativeBt : IBluetoothApi
{
    private const uint ErrorNotFound = 1168;

    [StructLayout(LayoutKind.Sequential)]
    private struct SYSTEMTIME
    {
        public ushort wYear, wMonth, wDayOfWeek, wDay, wHour, wMinute, wSecond, wMilliseconds;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct BLUETOOTH_DEVICE_INFO
    {
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

    [StructLayout(LayoutKind.Sequential)]
    private struct BLUETOOTH_DEVICE_SEARCH_PARAMS
    {
        public int dwSize;
        public int fReturnAuthenticated;
        public int fReturnRemembered;
        public int fReturnUnknown;
        public int fReturnConnected;
        public int fIssueInquiry;
        public byte cTimeoutMultiplier;
        public IntPtr hRadio;
    }

    [DllImport("bthprops.cpl", SetLastError = true)]
    private static extern uint BluetoothGetDeviceInfo(IntPtr hRadio, ref BLUETOOTH_DEVICE_INFO info);

    [DllImport("bthprops.cpl", SetLastError = true)]
    private static extern uint BluetoothSetServiceState(IntPtr hRadio, ref BLUETOOTH_DEVICE_INFO info, ref Guid service, uint flags);

    [DllImport("bthprops.cpl", SetLastError = true)]
    private static extern IntPtr BluetoothFindFirstDevice(ref BLUETOOTH_DEVICE_SEARCH_PARAMS search, ref BLUETOOTH_DEVICE_INFO info);

    [DllImport("bthprops.cpl", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool BluetoothFindNextDevice(IntPtr find, ref BLUETOOTH_DEVICE_INFO info);

    [DllImport("bthprops.cpl")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool BluetoothFindDeviceClose(IntPtr find);

    private static BLUETOOTH_DEVICE_INFO NewInfo(ulong address = 0) => new()
    {
        dwSize = Marshal.SizeOf<BLUETOOTH_DEVICE_INFO>(),
        Address = address,
    };

    private static BtDevice ToDevice(in BLUETOOTH_DEVICE_INFO info) =>
        new(info.szName ?? "", info.Address, info.fConnected != 0, info.ulClassofDevice);

    public BtDevice? GetDevice(ulong address)
    {
        var info = NewInfo(address);
        var r = BluetoothGetDeviceInfo(IntPtr.Zero, ref info);
        if (r == 0) return ToDevice(info);
        if (r == ErrorNotFound) return null;
        throw new BluetoothException($"BluetoothGetDeviceInfo failed (Windows error {r}).");
    }

    public uint SetServiceState(ulong address, Guid service, bool enable)
    {
        var info = NewInfo(address);
        var r = BluetoothGetDeviceInfo(IntPtr.Zero, ref info);
        if (r == ErrorNotFound)
            throw new DeviceNotFoundException($"Device {MacAddress.Format(address)} is not known to Windows.");
        if (r != 0)
            throw new BluetoothException($"BluetoothGetDeviceInfo failed (Windows error {r}).");
        return BluetoothSetServiceState(IntPtr.Zero, ref info, ref service, enable ? 1u : 0u);
    }

    public IReadOnlyList<BtDevice> EnumeratePaired()
    {
        var search = new BLUETOOTH_DEVICE_SEARCH_PARAMS
        {
            dwSize = Marshal.SizeOf<BLUETOOTH_DEVICE_SEARCH_PARAMS>(),
            fReturnAuthenticated = 1,
            fReturnRemembered = 1,
            fReturnConnected = 1,
            fReturnUnknown = 0,
            fIssueInquiry = 0,        // no radio scan: only what Windows already knows
            cTimeoutMultiplier = 0,
            hRadio = IntPtr.Zero,
        };
        var info = NewInfo();
        var devices = new List<BtDevice>();

        var handle = BluetoothFindFirstDevice(ref search, ref info);
        if (handle == IntPtr.Zero) return devices;   // no radio or no devices
        try
        {
            do { devices.Add(ToDevice(info)); }
            while (BluetoothFindNextDevice(handle, ref info));
        }
        finally { BluetoothFindDeviceClose(handle); }
        return devices;
    }
}
```

- [ ] **Step 2: Smoke check with a throwaway script (do not commit it)**

Temporarily put this in `src/HeadsetCtl.Cli/Program.cs` replacing the template body, run it, then restore it in Task 7:
```csharp
using HeadsetCtl.Core;
var api = new NativeBt();
foreach (var d in api.EnumeratePaired())
    Console.WriteLine($"{d.Name,-25} {d.Mac} connected={d.Connected} audio={d.IsAudio}");
```
Run: `dotnet run --project src/HeadsetCtl.Cli`
Expected: a line for `WH-CH720N 00:A4:1C:06:64:0C connected=<True/False> audio=True`. If the list is empty, check the `BLUETOOTH_DEVICE_SEARCH_PARAMS` field layout (size must be 40 on x64) before continuing.

- [ ] **Step 3: Commit (Core only)**
```bash
git add src/HeadsetCtl.Core/NativeBt.cs
git commit -m "feat: NativeBt Win32 implementation"
```

---

### Task 7: CLI (`headset`)

**Files:**
- Create: `src/HeadsetCtl.Cli/CliApp.cs`
- Modify: `src/HeadsetCtl.Cli/Program.cs` (replace the smoke-check code)
- Test: `tests/HeadsetCtl.Tests/CliAppTests.cs`

**Interfaces:**
- Consumes: `IBluetoothApi`, `HeadsetController`, `DeviceLocator`, `SettingsStore`, `Settings`, `BtDevice`, exceptions (Tasks 2-4); test helpers `Fake`, `FakeBluetoothApi`.
- Produces: `public sealed class CliApp(IBluetoothApi api, HeadsetController controller, SettingsStore store, TextWriter output, TextWriter error)` with `Task<int> RunAsync(string[] args)` and constants `Ok = 0`, `Failure = 1`, `NotFound = 2`.
- Commands: `list [--all]`, `select <name-or-mac>`, `status|on|off|toggle [--device <name-or-mac>]`.

- [ ] **Step 1: Write the failing tests**

`tests/HeadsetCtl.Tests/CliAppTests.cs`:
```csharp
using HeadsetCtl.Core;
using HeadsetCtl.Cli;

namespace HeadsetCtl.Tests;

public class CliAppTests : IDisposable
{
    private readonly string _settingsPath = Path.Combine(Path.GetTempPath(), "HeadsetCtlCli-" + Guid.NewGuid() + ".json");
    private readonly StringWriter _out = new();
    private readonly StringWriter _err = new();
    private readonly FakeBluetoothApi _api = new();

    public void Dispose() { if (File.Exists(_settingsPath)) File.Delete(_settingsPath); }

    private SettingsStore Store => new(_settingsPath);

    private CliApp App() => new(_api, new HeadsetController(_api, TimeSpan.Zero, TimeSpan.Zero), Store, _out, _err);

    private Task<int> Run(params string[] args) => App().RunAsync(args);

    private void Pair(params BtDevice[] devices) => _api.Devices.AddRange(devices);

    private static readonly BtDevice Headset = Fake.Audio("WH-CH720N", 0x00A41C06640C);
    private static readonly BtDevice Other = Fake.Audio("Other-BT", 0xAABBCC112233, connected: true);
    private static readonly BtDevice Phone = Fake.Phone("Pixel", 0x112233445566);

    [Fact]
    public async Task List_ShowsAudioDevicesWithMacAndState()
    {
        Pair(Headset, Other, Phone);
        Assert.Equal(CliApp.Ok, await Run("list"));
        var text = _out.ToString();
        Assert.Contains("NAME", text);
        Assert.Contains("MAC", text);
        Assert.Contains("00:A4:1C:06:64:0C", text);
        Assert.Contains("AA:BB:CC:11:22:33", text);
        Assert.Contains("Connected", text);
        Assert.Contains("Disconnected", text);
        Assert.DoesNotContain("Pixel", text);
    }

    [Fact]
    public async Task List_All_IncludesNonAudio()
    {
        Pair(Headset, Phone);
        Assert.Equal(CliApp.Ok, await Run("list", "--all"));
        Assert.Contains("Pixel", _out.ToString());
    }

    [Fact]
    public async Task List_NoDevices_PrintsFriendlyMessage()
    {
        Assert.Equal(CliApp.Ok, await Run("list"));
        Assert.Contains("No paired audio devices", _out.ToString());
    }

    [Fact]
    public async Task List_UnnamedDevice_ShowsPlaceholder()
    {
        Pair(Fake.Audio("", 0x1));
        Assert.Equal(CliApp.Ok, await Run("list"));
        Assert.Contains("(unnamed)", _out.ToString());
    }

    [Fact]
    public async Task Select_ByName_SavesMac()
    {
        Pair(Headset, Other);
        Assert.Equal(CliApp.Ok, await Run("select", "other-bt"));
        Assert.Equal("AA:BB:CC:11:22:33", Store.Load().DeviceMac);
        Assert.Contains("Other-BT", _out.ToString());
    }

    [Fact]
    public async Task Select_Unknown_ReturnsNotFound_AndSavesNothing()
    {
        Pair(Headset);
        Assert.Equal(CliApp.NotFound, await Run("select", "Nope"));
        Assert.Null(Store.Load().DeviceMac);
        Assert.NotEmpty(_err.ToString());
    }

    [Fact]
    public async Task Select_WithoutArgument_IsUsageError()
    {
        Assert.Equal(CliApp.Failure, await Run("select"));
        Assert.Contains("Usage", _err.ToString());
    }

    [Fact]
    public async Task Status_UsesDefaultDeviceName_AndPrintsMac()
    {
        Pair(Headset, Other);
        Assert.Equal(CliApp.Ok, await Run("status"));
        Assert.Contains("WH-CH720N (00:A4:1C:06:64:0C): Disconnected", _out.ToString());
    }

    [Fact]
    public async Task Status_UsesSelectedDevice()
    {
        Pair(Headset, Other);
        await Run("select", "Other-BT");
        _out.GetStringBuilder().Clear();
        Assert.Equal(CliApp.Ok, await Run("status"));
        Assert.Contains("Other-BT (AA:BB:CC:11:22:33): Connected", _out.ToString());
    }

    [Fact]
    public async Task Status_DeviceOverride_BeatsSelection()
    {
        Pair(Headset, Other);
        await Run("select", "Other-BT");
        _out.GetStringBuilder().Clear();
        Assert.Equal(CliApp.Ok, await Run("status", "--device", "00:A4:1C:06:64:0C"));
        Assert.Contains("WH-CH720N", _out.ToString());
    }

    [Fact]
    public async Task Status_DeviceNotPaired_ReturnsNotFound()
    {
        Assert.Equal(CliApp.NotFound, await Run("status"));
        Assert.Contains("WH-CH720N", _err.ToString());
    }

    [Fact]
    public async Task On_ConnectsDevice()
    {
        Pair(Headset);
        Assert.Equal(CliApp.Ok, await Run("on"));
        Assert.Contains("Connected", _out.ToString());
        Assert.True(_api.Devices[0].Connected);
    }

    [Fact]
    public async Task Off_DisconnectsDevice()
    {
        Pair(Fake.Audio("WH-CH720N", 0x00A41C06640C, connected: true));
        Assert.Equal(CliApp.Ok, await Run("off"));
        Assert.False(_api.Devices[0].Connected);
    }

    [Fact]
    public async Task Toggle_FlipsState()
    {
        Pair(Headset);
        Assert.Equal(CliApp.Ok, await Run("toggle"));
        Assert.True(_api.Devices[0].Connected);
        Assert.Equal(CliApp.Ok, await Run("toggle"));
        Assert.False(_api.Devices[0].Connected);
    }

    [Fact]
    public async Task On_WhenWindowsDoesNotConnect_ReturnsFailure()
    {
        Pair(Headset);
        _api.ApplyStateChange = false;
        Assert.Equal(CliApp.Failure, await Run("on"));
        Assert.NotEmpty(_err.ToString());
    }

    [Fact]
    public async Task DuplicateNames_ReturnFailureMentioningMacs()
    {
        Pair(Headset, Fake.Audio("WH-CH720N", 0xAA));
        Assert.Equal(CliApp.Failure, await Run("status"));
        Assert.Contains("00:00:00:00:00:AA", _err.ToString());
    }

    [Fact]
    public async Task NoArguments_PrintsUsage()
    {
        Assert.Equal(CliApp.Failure, await Run());
        Assert.Contains("Usage", _err.ToString());
    }

    [Fact]
    public async Task UnknownCommand_PrintsUsage()
    {
        Assert.Equal(CliApp.Failure, await Run("explode"));
        Assert.Contains("Usage", _err.ToString());
    }

    [Fact]
    public async Task DeviceOption_WithoutValue_IsUsageError()
    {
        Assert.Equal(CliApp.Failure, await Run("status", "--device"));
        Assert.Contains("Usage", _err.ToString());
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter CliAppTests`
Expected: build FAIL, `CliApp` does not exist.

- [ ] **Step 3: Write minimal implementation**

`src/HeadsetCtl.Cli/CliApp.cs`:
```csharp
using HeadsetCtl.Core;

namespace HeadsetCtl.Cli;

public sealed class CliApp(
    IBluetoothApi api, HeadsetController controller, SettingsStore store, TextWriter output, TextWriter error)
{
    public const int Ok = 0, Failure = 1, NotFound = 2;

    private const string Usage =
        "Usage:\n" +
        "  headset list [--all]\n" +
        "  headset select <name-or-mac>\n" +
        "  headset status|on|off|toggle [--device <name-or-mac>]";

    private sealed class UsageException : Exception;

    private readonly DeviceLocator _locator = new(api);

    public async Task<int> RunAsync(string[] args)
    {
        if (args.Length == 0) return UsageError();

        var command = args[0].ToLowerInvariant();
        var rest = args.Skip(1).ToList();
        try
        {
            return command switch
            {
                "list" => List(rest),
                "select" => Select(rest),
                "status" or "on" or "off" or "toggle" => await ControlAsync(command, rest),
                _ => throw new UsageException(),
            };
        }
        catch (UsageException) { return UsageError(); }
        catch (DeviceNotFoundException e) { error.WriteLine(e.Message); return NotFound; }
        catch (BluetoothException e) { error.WriteLine(e.Message); return Failure; }
    }

    private int UsageError()
    {
        error.WriteLine(Usage);
        return Failure;
    }

    private static bool TakeFlag(List<string> args, string flag) => args.Remove(flag);

    private static string? TakeValue(List<string> args, string option)
    {
        var i = args.IndexOf(option);
        if (i < 0) return null;
        if (i + 1 >= args.Count) throw new UsageException();
        var value = args[i + 1];
        args.RemoveRange(i, 2);
        return value;
    }

    private static string State(bool connected) => connected ? "Connected" : "Disconnected";

    private static string DisplayName(BtDevice d) => string.IsNullOrWhiteSpace(d.Name) ? "(unnamed)" : d.Name;

    private int List(List<string> args)
    {
        var all = TakeFlag(args, "--all");
        if (args.Count != 0) throw new UsageException();

        var devices = _locator.ListPaired(all);
        if (devices.Count == 0)
        {
            output.WriteLine(all
                ? "No paired devices found."
                : "No paired audio devices found. (Use --all to show every paired device.)");
            return Ok;
        }

        var width = Math.Max("NAME".Length, devices.Max(d => DisplayName(d).Length));
        output.WriteLine($"{"NAME".PadRight(width)}  {"MAC",-17}  STATE");
        foreach (var d in devices)
            output.WriteLine($"{DisplayName(d).PadRight(width)}  {d.Mac,-17}  {State(d.Connected)}");
        return Ok;
    }

    private int Select(List<string> args)
    {
        if (args.Count != 1) throw new UsageException();
        var device = _locator.Resolve(args[0]);
        var settings = store.Load();
        settings.DeviceMac = device.Mac;
        store.Save(settings);
        output.WriteLine($"Selected {DisplayName(device)} ({device.Mac}).");
        return Ok;
    }

    private async Task<int> ControlAsync(string command, List<string> args)
    {
        var overrideDevice = TakeValue(args, "--device");
        if (args.Count != 0) throw new UsageException();

        var device = overrideDevice is not null
            ? _locator.Resolve(overrideDevice)
            : _locator.ResolveSelected(store.Load().DeviceMac);
        var label = $"{DisplayName(device)} ({device.Mac})";

        if (command == "status")
        {
            output.WriteLine($"{label}: {State(controller.IsConnected(device.Address))}");
            return Ok;
        }

        bool? wanted = command switch { "on" => true, "off" => false, _ => null };
        var state = wanted is { } w
            ? await controller.SetConnectedAsync(device.Address, w)
            : await controller.ToggleAsync(device.Address);

        if (wanted is { } target && state != target)
        {
            error.WriteLine($"{label}: still {State(state)} - Windows did not {(target ? "connect" : "disconnect")} it. Is the device switched on and in range?");
            return Failure;
        }

        output.WriteLine($"{label}: {State(state)}");
        return Ok;
    }
}
```

`src/HeadsetCtl.Cli/Program.cs` (replace everything):
```csharp
using HeadsetCtl.Cli;
using HeadsetCtl.Core;

var api = new NativeBt();
var app = new CliApp(api, new HeadsetController(api), new SettingsStore(SettingsStore.DefaultPath), Console.Out, Console.Error);
return await app.RunAsync(args);
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test`
Expected: PASS (all tests).

- [ ] **Step 5: Manual hardware check**

With the WH-CH720N paired and switched on, run from `C:\Work\SonyHP`:
```powershell
dotnet run --project src/HeadsetCtl.Cli -- list
dotnet run --project src/HeadsetCtl.Cli -- select WH-CH720N
dotnet run --project src/HeadsetCtl.Cli -- status
dotnet run --project src/HeadsetCtl.Cli -- off
dotnet run --project src/HeadsetCtl.Cli -- on
```
Expected: `list` shows the headset with its MAC; `off` -> Disconnected and the audio stops; `on` -> Connected and the audio returns (same as the Windows panel button). Exit code `$LASTEXITCODE` is 0.

- [ ] **Step 6: Commit**
```bash
git add -A
git commit -m "feat: headset CLI (list, select, status, on, off, toggle)"
```

---

### Task 8: Tray app

**Files:**
- Create/replace: `src/HeadsetCtl.Tray/Program.cs`, `src/HeadsetCtl.Tray/TrayContext.cs`, `src/HeadsetCtl.Tray/HotkeyWindow.cs`, `src/HeadsetCtl.Tray/IconFactory.cs`, `src/HeadsetCtl.Tray/Autostart.cs`
- No automated tests (UI, hotkey and hardware). Verified manually in Step 7 and Task 9.

**Interfaces:**
- Consumes: `NativeBt`, `HeadsetController`, `DeviceLocator`, `SettingsStore`, `Settings`, `HotkeyParser`, `Hotkey`, `BtDevice` (Tasks 2-6).
- Produces: the `HeadsetCtl.exe` tray application.

- [ ] **Step 1: `IconFactory.cs`**
```csharp
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace HeadsetCtl.Tray;

internal static class IconFactory
{
    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr handle);

    /// <summary>A coloured disc with a white headphone glyph. Caller owns (disposes) the icon.</summary>
    public static Icon Create(Color color)
    {
        using var bmp = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);
            using var fill = new SolidBrush(color);
            g.FillEllipse(fill, 2, 2, 28, 28);
            using var pen = new Pen(Color.White, 3);
            g.DrawArc(pen, 8, 7, 16, 16, 180, 180);              // headband
            g.FillRectangle(Brushes.White, 7, 14, 4, 8);          // left ear cup
            g.FillRectangle(Brushes.White, 21, 14, 4, 8);         // right ear cup
        }

        var handle = bmp.GetHicon();
        try
        {
            using var temp = Icon.FromHandle(handle);
            return (Icon)temp.Clone();
        }
        finally { DestroyIcon(handle); }
    }
}
```

- [ ] **Step 2: `HotkeyWindow.cs`**
```csharp
using HeadsetCtl.Core;

namespace HeadsetCtl.Tray;

/// <summary>Message-only window that owns one global hotkey.</summary>
internal sealed class HotkeyWindow : NativeWindow, IDisposable
{
    private const int WmHotkey = 0x0312;
    private const int HotkeyId = 1;
    private const uint ModNoRepeat = 0x4000;
    private static readonly IntPtr HwndMessage = new(-3);

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint modifiers, uint vk);

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    public event Action? Pressed;

    public HotkeyWindow() => CreateHandle(new CreateParams { Parent = HwndMessage });

    /// <returns>false if another program already owns the combination.</returns>
    public bool Register(Hotkey hotkey)
    {
        Unregister();
        return RegisterHotKey(Handle, HotkeyId, hotkey.Modifiers | ModNoRepeat, hotkey.VirtualKey);
    }

    public void Unregister() => UnregisterHotKey(Handle, HotkeyId);

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WmHotkey && m.WParam == HotkeyId) Pressed?.Invoke();
        else base.WndProc(ref m);
    }

    public void Dispose()
    {
        Unregister();
        DestroyHandle();
    }
}
```

- [ ] **Step 3: `Autostart.cs`**
```csharp
using Microsoft.Win32;

namespace HeadsetCtl.Tray;

internal static class Autostart
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "HeadsetCtl";

    public static bool IsEnabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(ValueName) is string;
        }
    }

    public static void Set(bool enable)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true)
                        ?? Registry.CurrentUser.CreateSubKey(RunKey);
        if (enable) key.SetValue(ValueName, $"\"{Environment.ProcessPath}\"");
        else key.DeleteValue(ValueName, throwOnMissingValue: false);
    }
}
```

- [ ] **Step 4: `TrayContext.cs`**
```csharp
using HeadsetCtl.Core;

namespace HeadsetCtl.Tray;

internal sealed class TrayContext : ApplicationContext
{
    private enum TrayState { Connected, Disconnected, Warning }

    private readonly IBluetoothApi _api = new NativeBt();
    private readonly HeadsetController _controller;
    private readonly DeviceLocator _locator;
    private readonly SettingsStore _store = new(SettingsStore.DefaultPath);
    private readonly NotifyIcon _tray;
    private readonly HotkeyWindow _hotkeyWindow = new();
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 5000 };
    private readonly Icon _iconOn = IconFactory.Create(Color.SeaGreen);
    private readonly Icon _iconOff = IconFactory.Create(Color.Gray);
    private readonly Icon _iconWarn = IconFactory.Create(Color.DarkOrange);
    private readonly ToolStripMenuItem _deviceMenu = new("Device");
    private readonly ToolStripMenuItem _autostartItem = new("Start with Windows");
    private bool _busy;

    public TrayContext()
    {
        _controller = new HeadsetController(_api);
        _locator = new DeviceLocator(_api);

        var menu = new ContextMenuStrip();
        menu.Items.Add("Connect", null, async (_, _) => await SetAsync(true));
        menu.Items.Add("Disconnect", null, async (_, _) => await SetAsync(false));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_deviceMenu);
        _autostartItem.Click += (_, _) => SafeRun(() => Autostart.Set(!Autostart.IsEnabled));
        menu.Items.Add(_autostartItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => ExitThread());
        menu.Opening += (_, _) =>
        {
            RebuildDeviceMenu();
            _autostartItem.Checked = Autostart.IsEnabled;
        };

        _tray = new NotifyIcon { Icon = _iconOff, Text = "Headset", ContextMenuStrip = menu, Visible = true };
        _tray.MouseClick += async (_, e) => { if (e.Button == MouseButtons.Left) await ToggleAsync(); };

        _timer.Tick += async (_, _) => await RefreshAsync();
        _timer.Start();

        _hotkeyWindow.Pressed += async () => await ToggleAsync();
        RegisterHotkey();

        _ = RefreshAsync();
    }

    private BtDevice ResolveSelected() => _locator.ResolveSelected(_store.Load().DeviceMac);

    private void RegisterHotkey()
    {
        var text = _store.Load().Hotkey;
        if (!HotkeyParser.TryParse(text, out var hotkey))
        {
            Notify($"Invalid hotkey '{text}' in settings.json; using {Settings.DefaultHotkey}.");
            text = Settings.DefaultHotkey;
            HotkeyParser.TryParse(text, out hotkey);
        }
        if (!_hotkeyWindow.Register(hotkey))
            Notify($"Hotkey {text} is already in use by another program.");
    }

    private Task ToggleAsync() => PerformAsync(address => _controller.ToggleAsync(address));

    private Task SetAsync(bool connect) => PerformAsync(address => _controller.SetConnectedAsync(address, connect));

    /// <summary>Runs one user-requested action; ignores requests while another is in flight.</summary>
    private async Task PerformAsync(Func<ulong, Task<bool>> action)
    {
        if (_busy) return;
        _busy = true;
        SetText("Working...");
        try
        {
            var device = await Task.Run(ResolveSelected);
            var connected = await action(device.Address);
            Show(connected ? TrayState.Connected : TrayState.Disconnected, device);
        }
        catch (Exception e)
        {
            Show(TrayState.Warning, null, e.Message);
            Notify(e.Message);
        }
        finally { _busy = false; }
    }

    /// <summary>Background poll: updates icon only, never raises balloons.</summary>
    private async Task RefreshAsync()
    {
        if (_busy) return;
        try
        {
            var (device, connected) = await Task.Run(() =>
            {
                var d = ResolveSelected();
                return (d, _controller.IsConnected(d.Address));
            });
            if (_busy) return;
            Show(connected ? TrayState.Connected : TrayState.Disconnected, device);
        }
        catch (Exception e)
        {
            if (!_busy) Show(TrayState.Warning, null, e.Message);
        }
    }

    private void Show(TrayState state, BtDevice? device, string? message = null)
    {
        _tray.Icon = state switch
        {
            TrayState.Connected => _iconOn,
            TrayState.Disconnected => _iconOff,
            _ => _iconWarn,
        };
        SetText(state switch
        {
            TrayState.Connected => $"{device!.Name}: Connected",
            TrayState.Disconnected => $"{device!.Name}: Disconnected",
            _ => message ?? "Headset unavailable",
        });
    }

    private void SetText(string text) => _tray.Text = text.Length > 63 ? text[..63] : text;

    private void Notify(string message) => _tray.ShowBalloonTip(4000, "HeadsetCtl", message, ToolTipIcon.Warning);

    private void SafeRun(Action action)
    {
        try { action(); }
        catch (Exception e) { Notify(e.Message); }
    }

    private void RebuildDeviceMenu()
    {
        _deviceMenu.DropDownItems.Clear();

        string? selectedMac = null;
        try { selectedMac = ResolveSelected().Mac; } catch (BluetoothException) { }

        IReadOnlyList<BtDevice> devices;
        try { devices = _locator.ListPaired(); } catch (BluetoothException) { devices = []; }

        if (devices.Count == 0)
        {
            _deviceMenu.DropDownItems.Add(new ToolStripMenuItem("No paired audio devices") { Enabled = false });
            return;
        }

        foreach (var device in devices)
        {
            var mac = device.Mac;
            var name = string.IsNullOrWhiteSpace(device.Name) ? "(unnamed)" : device.Name;
            var item = new ToolStripMenuItem($"{name} ({mac})") { Checked = mac == selectedMac };
            item.Click += async (_, _) =>
            {
                SafeRun(() =>
                {
                    var settings = _store.Load();
                    settings.DeviceMac = mac;
                    _store.Save(settings);
                });
                await RefreshAsync();
            };
            _deviceMenu.DropDownItems.Add(item);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _timer.Dispose();
            _hotkeyWindow.Dispose();
            _tray.Visible = false;
            _tray.Dispose();
            _iconOn.Dispose();
            _iconOff.Dispose();
            _iconWarn.Dispose();
        }
        base.Dispose(disposing);
    }
}
```

- [ ] **Step 5: `Program.cs`** (replace the placeholder)
```csharp
namespace HeadsetCtl.Tray;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        using var mutex = new Mutex(initiallyOwned: true, @"Local\HeadsetCtl.Tray", out var isFirstInstance);
        if (!isFirstInstance) return;

        ApplicationConfiguration.Initialize();
        using var context = new TrayContext();
        Application.Run(context);
    }
}
```

- [ ] **Step 6: Build**

Run: `dotnet build`
Expected: Build succeeded, 0 errors. Fix any compile errors (the `winforms` template enables implicit usings for `System.Windows.Forms` and `System.Drawing`).

- [ ] **Step 7: Manual check**

Run: `dotnet run --project src/HeadsetCtl.Tray`
Expected: a tray icon appears (green = connected, grey = disconnected); tooltip shows `WH-CH720N: <state>`.
- Left-click the icon: the headset connects/disconnects within ~3 s and the icon colour follows.
- Press Ctrl+Alt+H: same as left-click.
- Right-click -> Device: lists paired audio devices with the current one ticked; picking another changes the tooltip.
- Start a second copy: it exits immediately (single instance).

- [ ] **Step 8: Commit**
```bash
git add -A
git commit -m "feat: tray app with hotkey, device menu and autostart"
```

---

### Task 9: End-to-end verification and publish

**Files:**
- Create: `README.md`

**Interfaces:**
- Consumes: everything above. Produces: runnable published builds and short usage docs.

- [ ] **Step 1: Run the full test suite**

Run: `dotnet test`
Expected: all tests PASS.

- [ ] **Step 2: Manual end-to-end checklist (record any failures)**

With the headset paired:
- [ ] Switch the headset off with its own button; within ~5 s the tray icon turns grey.
- [ ] Switch it on and connect with the tray (left-click); audio returns.
- [ ] Mash Ctrl+Alt+H several times quickly: no crash, no double-toggle storm (the `_busy` guard drops extra presses).
- [ ] Turn the Windows Bluetooth radio off; the icon shows the warning colour and no crash. Turn it on again; the icon recovers within ~5 s.
- [ ] Put `{ "Hotkey": "Ctrl+Alt" }` in `%APPDATA%\HeadsetCtl\settings.json`, restart the tray: a balloon says the hotkey is invalid and Ctrl+Alt+H still works.
- [ ] Put garbage in `settings.json`: both exes still start with defaults.
- [ ] Right-click -> Start with Windows ticks and unticks; confirm the entry in `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`.
- [ ] `headset select <other device>` in a terminal, then the tray's Device submenu shows that device ticked.

- [ ] **Step 3: Publish**

```powershell
dotnet publish src/HeadsetCtl.Cli  -c Release -r win-x64 --self-contained false -o dist/cli
dotnet publish src/HeadsetCtl.Tray -c Release -r win-x64 --self-contained false -o dist/tray
```
Expected: `dist/cli/headset.exe` and `dist/tray/HeadsetCtl.exe` run (they need the .NET 9 desktop runtime installed). Add `dist/` to `.gitignore`.

- [ ] **Step 4: Write `README.md`**

Short: what it does (connect/disconnect like the Windows panel), `headset list|select|status|on|off|toggle`, tray usage (click, Ctrl+Alt+H, Device menu), the `%APPDATA%\HeadsetCtl\settings.json` format (`DeviceMac`, `Hotkey`), and the note that the headset must be paired first.

- [ ] **Step 5: Commit**
```bash
git add -A
git commit -m "docs: README and publish instructions"
```
