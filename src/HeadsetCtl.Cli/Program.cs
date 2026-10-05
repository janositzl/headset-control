using HeadsetCtl.Cli;
using HeadsetCtl.Core;

var api = new NativeBt();
var app = new CliApp(api, new HeadsetController(api), new SettingsStore(SettingsStore.DefaultPath), Console.Out, Console.Error);
return await app.RunAsync(args);
