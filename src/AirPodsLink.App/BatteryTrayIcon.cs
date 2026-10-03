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
            var textBox = new RectangleF(0, 0, showCharging ? size * 0.75f : size, size);
            DrawFitted(graphics, text, ink, textBox);

            if (showCharging)
            {
                DrawBolt(graphics, size * 0.875f, size / 2f, size * 0.42f, palette.Normal);
            }
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
        for (var emSize = box.Height * 0.625f; emSize >= 5; emSize -= 0.5f)
        {
            using var font = new Font(FontFamilyName.Value, emSize, FontStyle.Bold, GraphicsUnit.Pixel);
            var measured = graphics.MeasureString(text, font, PointF.Empty, format);
            if (measured.Width > box.Width - 1 || measured.Height > box.Height)
            {
                continue;
            }

            graphics.DrawString(text, font, brush, box, format);
            return;
        }
    }

    private static void DrawBolt(Graphics graphics, float centerX, float centerY, float height, Color color)
    {
        var u = height / 10f;
        PointF[] bolt =
        [
            new(centerX + 1 * u, centerY - 5 * u), new(centerX - 3 * u, centerY + 0.6f * u),
            new(centerX - 0.2f * u, centerY + 0.6f * u), new(centerX - 1 * u, centerY + 5 * u),
            new(centerX + 3 * u, centerY - 0.6f * u), new(centerX + 0.2f * u, centerY - 0.6f * u)
        ];
        using var brush = new SolidBrush(color);
        graphics.FillPolygon(brush, bolt);
    }

    /// <summary>Windows 11's own UI face, falling back on older systems.</summary>
    private static readonly Lazy<string> FontFamilyName = new(() =>
        new InstalledFontCollection().Families.Any(family => family.Name == "Segoe UI Variable Text")
            ? "Segoe UI Variable Text"
            : "Segoe UI");

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
