using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;

namespace AirPodsLink.App;

/// <summary>
/// Draws the taskbar icon that shows the lowest earbud charge.
/// </summary>
internal sealed class BatteryTrayIcon : IDisposable
{
    private static readonly Color UnknownColor = Color.FromArgb(158, 158, 158);
    private static readonly Color HighColor = Color.FromArgb(76, 200, 108);
    private static readonly Color MediumColor = Color.FromArgb(240, 176, 40);
    private static readonly Color LowColor = Color.FromArgb(240, 84, 76);
    private static readonly Color ChargingColor = Color.FromArgb(96, 208, 255);

    private readonly NotifyIcon _target;
    private (int? Percent, bool Charging, bool Stale)? _rendered;
    private Icon? _owned;

    public BatteryTrayIcon(NotifyIcon target) => _target = target;

    public void Update(int? percent, bool charging, bool stale)
    {
        var next = (percent, charging, stale);
        if (_rendered == next)
        {
            return;
        }

        _rendered = next;
        var previous = _owned;
        _owned = Render(percent, charging, stale);
        _target.Icon = _owned;
        previous?.Dispose();
    }

    private static Icon Render(int? percent, bool charging, bool stale)
    {
        var size = Math.Max(SystemInformation.SmallIconSize.Width, 16);
        using var bitmap = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.TextRenderingHint = TextRenderingHint.AntiAlias;

            var known = percent is not null && !stale;
            var showCharging = charging && known;
            DrawFitted(
                graphics,
                known ? $"{percent!.Value}" : "--",
                known ? LevelColor(percent!.Value) : UnknownColor,
                size,
                showCharging);

            if (showCharging)
            {
                DrawChargingMark(graphics, size);
            }
        }

        return CreateIcon(bitmap);
    }

    private static Color LevelColor(int percent) => percent switch
    {
        <= 20 => LowColor,
        <= 50 => MediumColor,
        _ => HighColor
    };

    private static void DrawFitted(Graphics graphics, string text, Color color, int size, bool reserveChargingMark)
    {
        // The charging mark sits in the bottom-right corner, so the digits get a
        // narrower box when it is visible.
        var box = new RectangleF(0, 0, reserveChargingMark ? size * 0.76f : size, size);
        var format = new StringFormat(StringFormat.GenericTypographic)
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center,
            FormatFlags = StringFormatFlags.NoWrap | StringFormatFlags.NoClip
        };
        using var brush = new SolidBrush(color);

        // Bold digits stop fitting once the charge reaches three figures.
        var weight = text.Length >= 3 ? FontStyle.Regular : FontStyle.Bold;
        for (var emSize = size; emSize >= 5; emSize--)
        {
            using var font = new Font("Segoe UI", emSize, weight, GraphicsUnit.Pixel);
            var measured = graphics.MeasureString(text, font, PointF.Empty, format);
            if (measured.Width > box.Width || measured.Height > box.Height)
            {
                continue;
            }

            graphics.DrawString(text, font, brush, box, format);
            return;
        }
    }

    private static void DrawChargingMark(Graphics graphics, int size)
    {
        var diameter = Math.Max(size * 0.36f, 5f);
        using var brush = new SolidBrush(ChargingColor);
        graphics.FillEllipse(brush, size - diameter, size - diameter, diameter, diameter);
    }

    /// <summary>
    /// Creates an icon which owns its native handle. Icons constructed from a
    /// stream retain a dependency on that stream; disposing the stream made the
    /// notification-area icon intermittently disappear when Explorer refreshed it.
    /// </summary>
    private static Icon CreateIcon(Bitmap bitmap)
    {
        var handle = bitmap.GetHicon();
        try
        {
            using var borrowed = Icon.FromHandle(handle);
            return (Icon)borrowed.Clone();
        }
        finally
        {
            _ = DestroyIcon(handle);
        }
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr handle);

    public void Dispose()
    {
        _owned?.Dispose();
        _owned = null;
    }
}
