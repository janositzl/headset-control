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
