using AirPodsLink.Core;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;

namespace AirPodsLink.App;

/// <summary>How much the shown charge can be trusted.</summary>
internal enum BatteryFreshness
{
    /// <summary>No believable reading yet: drawn as a grey "--".</summary>
    Unknown,

    /// <summary>Confirmed moments ago: drawn in the level colour.</summary>
    Live,

    /// <summary>
    /// Last confirmed value, no longer current (worn pods withhold their charge,
    /// or the AirPods went quiet): drawn in grey so it is not read as live.
    /// </summary>
    Remembered
}

/// <summary>
/// Draws the taskbar icon: the lowest earbud charge as a plain number, styled
/// like the system clock so it sits with the other notification-area glyphs.
/// </summary>
/// <remarks>
/// Windows 11 tray glyphs are monochrome: white on a dark taskbar, near black
/// on a light one. The icon follows the taskbar theme and only departs from it
/// to flag a low charge in red, or a reading that is no longer current in grey.
/// </remarks>
internal sealed class BatteryTrayIcon : IDisposable
{
    private const int LowThreshold = 20;
    private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    private readonly NotifyIcon _target;
    private (int? Percent, bool Charging, BatteryFreshness Freshness, bool LightTaskbar)? _rendered;
    private Icon? _owned;

    public BatteryTrayIcon(NotifyIcon target) => _target = target;

    public void Update(int? percent, bool charging, BatteryFreshness freshness)
    {
        if (percent is null) freshness = BatteryFreshness.Unknown;
        var next = (percent, charging, freshness, TaskbarIsLight());
        if (_rendered == next)
        {
            return;
        }

        _rendered = next;
        var previous = _owned;
        _owned = Render(next.percent, next.charging, next.freshness, next.Item4);
        _target.Icon = _owned;
        previous?.Dispose();
    }

    private static Icon Render(int? percent, bool charging, BatteryFreshness freshness, bool lightTaskbar)
    {
        var size = Math.Max(SystemInformation.SmallIconSize.Width, 16);
        using var bitmap = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            // ClearType needs an opaque background; tray icons are transparent.
            graphics.TextRenderingHint = TextRenderingHint.AntiAlias;

            var palette = Palette.For(lightTaskbar);
            var live = freshness == BatteryFreshness.Live;
            // A remembered charging flag says nothing about now.
            var showCharging = charging && live;
            var ink = !live || percent is null ? palette.Muted
                : percent <= LowThreshold ? palette.Low
                : palette.Normal;

            var text = percent is { } shown ? BatteryTracker.FormatPercent(shown) : "--";
            var body = DrawBatteryBody(graphics, size, percent, live, ink);
            DrawFitted(graphics, text, ink, body);
            if (showCharging) DrawBolt(graphics, body, ink);
        }

        return CreateIcon(bitmap);
    }

    private static void DrawFitted(Graphics graphics, string text, Color color, RectangleF box)
    {
        var format = new StringFormat(StringFormat.GenericTypographic)
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center,
            FormatFlags = StringFormatFlags.NoWrap | StringFormatFlags.NoClip
        };
        using var brush = new SolidBrush(color);
        for (var emSize = box.Height * 1.15f; emSize >= 5; emSize -= 0.5f)
        {
            using var font = new Font(FontFamilyName.Value, emSize, FontStyle.Regular, GraphicsUnit.Pixel);
            var measured = graphics.MeasureString(text, font, PointF.Empty, format);
            // MeasureString reports the full line height (ascent plus descent); digits
            // only use the upper part, so allow the line to overshoot the box.
            if (measured.Width > box.Width - 1 || measured.Height > box.Height * 1.3f)
            {
                continue;
            }

            graphics.DrawString(text, font, brush, box, format);
            return;
        }
    }

    /// <summary>
    /// Outline of a horizontal battery with a faint fill showing the level. The
    /// figure is drawn inside it, so one glyph carries both the symbol and the
    /// number. Returns the inner area available for the text.
    /// </summary>
    private static RectangleF DrawBatteryBody(Graphics graphics, int size, int? percent, bool live, Color ink)
    {
        var stroke = Math.Max(1f, size / 16f);
        var nubWidth = Math.Max(1.5f, size / 16f);
        var width = size - nubWidth - stroke;
        var height = size * 0.70f;
        var body = new RectangleF(stroke / 2, (size - height) / 2, width, height);
        var radius = size / 7f;

        using var path = RoundedRectangle(body, radius);
        if (percent is { } level)
        {
            var fillWidth = Math.Max(0f, (body.Width - stroke) * Math.Clamp(level, 0, 100) / 100f);
            using var fill = new SolidBrush(Color.FromArgb(live ? 70 : 45, ink));
            var state = graphics.Save();
            graphics.SetClip(path);
            graphics.FillRectangle(fill, body.X + stroke / 2, body.Y, fillWidth, body.Height);
            graphics.Restore(state);
        }

        using var pen = new Pen(ink, stroke);
        graphics.DrawPath(pen, path);
        using var nub = new SolidBrush(ink);
        graphics.FillRectangle(nub, body.Right + stroke / 2, size / 2f - height * 0.2f, nubWidth, height * 0.4f);
        return RectangleF.Inflate(body, -stroke, -stroke / 2);
    }

    private static System.Drawing.Drawing2D.GraphicsPath RoundedRectangle(RectangleF area, float radius)
    {
        var d = radius * 2;
        var path = new System.Drawing.Drawing2D.GraphicsPath();
        path.AddArc(area.X, area.Y, d, d, 180, 90);
        path.AddArc(area.Right - d, area.Y, d, d, 270, 90);
        path.AddArc(area.Right - d, area.Bottom - d, d, d, 0, 90);
        path.AddArc(area.X, area.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    /// <summary>
    /// Lightning bolt over the battery's top-right corner, with a transparent
    /// halo so it stays legible against the figure.
    /// </summary>
    private static void DrawBolt(Graphics graphics, RectangleF body, Color ink)
    {
        var u = body.Height / 11f;
        var cx = body.Right - 1.2f * u;
        var cy = body.Top + 0.5f * u;
        PointF[] bolt =
        [
            new(cx + 1 * u, cy - 5 * u), new(cx - 3 * u, cy + 0.6f * u), new(cx - 0.2f * u, cy + 0.6f * u),
            new(cx - 1 * u, cy + 5 * u), new(cx + 3 * u, cy - 0.6f * u), new(cx + 0.2f * u, cy - 0.6f * u)
        ];
        var mode = graphics.CompositingMode;
        graphics.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy;
        using (var halo = new Pen(Color.Transparent, u * 1.6f) { LineJoin = System.Drawing.Drawing2D.LineJoin.Round })
            graphics.DrawPolygon(halo, bolt);
        graphics.CompositingMode = mode;
        using var brush = new SolidBrush(ink);
        graphics.FillPolygon(brush, bolt);
    }

    /// <summary>Windows 11's own UI face at its small optical size, falling back on older systems.</summary>
    private static readonly Lazy<string> FontFamilyName = new(() =>
        new InstalledFontCollection().Families.Select(family => family.Name)
            .FirstOrDefault(name => name is "Segoe UI Variable Small" or "Segoe UI Variable Text")
            ?? "Segoe UI");

    private static bool TaskbarIsLight()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(PersonalizeKey);
            return key?.GetValue("SystemUsesLightTheme") is int value && value == 1;
        }
        catch
        {
            return false;   // the dark taskbar is the Windows 11 default
        }
    }

    private sealed record Palette(Color Normal, Color Low, Color Muted)
    {
        private static readonly Palette Dark = new(
            Color.FromArgb(255, 255, 255), Color.FromArgb(255, 112, 112), Color.FromArgb(140, 140, 140));

        private static readonly Palette Light = new(
            Color.FromArgb(26, 26, 26), Color.FromArgb(196, 43, 28), Color.FromArgb(110, 110, 110));

        public static Palette For(bool lightTaskbar) => lightTaskbar ? Light : Dark;
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
