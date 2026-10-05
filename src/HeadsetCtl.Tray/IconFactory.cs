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
