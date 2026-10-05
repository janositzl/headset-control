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
