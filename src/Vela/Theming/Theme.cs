using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using Vela.Core;
using Vela.Interop;

namespace Vela.Theming;

public enum BackdropKind { None, Blur, Acrylic }

/// <summary>
/// A dock style. Colours follow the Apple-style system palette the reference app uses
/// (#0A84FF blue, #FF453A red, #30D158 green, #FF9F0A orange, #8E8E93 grey).
/// To add a style: add a Palette to <see cref="Palettes.All"/>; everything else picks it up.
/// </summary>
public sealed record Palette(
    string Id, string Name, bool IsLight, BackdropKind Backdrop,
    Color Bar, Color BarBorderTop, Color BarBorderBottom, Color Sheen,
    Color Tile, Color TileHover, Color TileBorder,
    Color Text, Color TextSecondary, Color TextTertiary,
    Color Track, Color Control, Color ControlHover,
    Color Flyout, Color FlyoutBorder, Color Divider,
    Color Red, Color Green, Color Orange, Color Yellow, Color Purple);

public static class Palettes
{
    static Color C(uint argb) => Color.FromArgb((byte)(argb >> 24), (byte)(argb >> 16), (byte)(argb >> 8), (byte)argb);

    static readonly Color DarkRed = C(0xFFFF453A), DarkGreen = C(0xFF30D158), DarkOrange = C(0xFFFF9F0A), DarkYellow = C(0xFFFFD60A), DarkPurple = C(0xFFBF5AF2);
    static readonly Color LightRed = C(0xFFFF3B30), LightGreen = C(0xFF34C759), LightOrange = C(0xFFFF9500), LightYellow = C(0xFFFFCC00), LightPurple = C(0xFFAF52DE);

    public static readonly Palette Black = new(
        "black", "Black", false, BackdropKind.None,
        Bar: C(0xF5050506), BarBorderTop: C(0x33FFFFFF), BarBorderBottom: C(0x14FFFFFF), Sheen: C(0x00FFFFFF),
        Tile: C(0xFF161618), TileHover: C(0xFF202023), TileBorder: C(0x1AFFFFFF),
        Text: C(0xFFFFFFFF), TextSecondary: C(0x99EBEBF5), TextTertiary: C(0x55EBEBF5),
        Track: C(0xFF2C2C2E), Control: C(0xFF2C2C2E), ControlHover: C(0xFF3A3A3C),
        Flyout: C(0xFF1C1C1E), FlyoutBorder: C(0x26FFFFFF), Divider: C(0x1FFFFFFF),
        DarkRed, DarkGreen, DarkOrange, DarkYellow, DarkPurple);

    public static readonly Palette Midnight = new(
        "midnight", "Midnight", false, BackdropKind.Blur,
        Bar: C(0xEE07070A), BarBorderTop: C(0x40FFFFFF), BarBorderBottom: C(0x10FFFFFF), Sheen: C(0x0FFFFFFF),
        Tile: C(0xFF0E0E11), TileHover: C(0xFF19191D), TileBorder: C(0x1FFFFFFF),
        Text: C(0xFFFFFFFF), TextSecondary: C(0x99EBEBF5), TextTertiary: C(0x55EBEBF5),
        Track: C(0xFF2A2A2E), Control: C(0xFF232327), ControlHover: C(0xFF303036),
        Flyout: C(0xFF101014), FlyoutBorder: C(0x2EFFFFFF), Divider: C(0x1FFFFFFF),
        DarkRed, DarkGreen, DarkOrange, DarkYellow, DarkPurple);

    public static readonly Palette White = new(
        "white", "White", true, BackdropKind.Blur,
        Bar: C(0xE6FAFAFC), BarBorderTop: C(0xFFFFFFFF), BarBorderBottom: C(0x1F000000), Sheen: C(0x00FFFFFF),
        Tile: C(0xFFF4F4F6), TileHover: C(0xFFEAEAEE), TileBorder: C(0x14000000),
        Text: C(0xFF000000), TextSecondary: C(0x993C3C43), TextTertiary: C(0x553C3C43),
        Track: C(0xFFE3E3E8), Control: C(0xFFE9E9ED), ControlHover: C(0xFFDDDDE2),
        Flyout: C(0xFFFFFFFF), FlyoutBorder: C(0x1A000000), Divider: C(0x14000000),
        LightRed, LightGreen, LightOrange, LightYellow, LightPurple);

    /// <summary>Clear glass: wallpaper shows through, frosted, with a bright rim highlight.</summary>
    public static readonly Palette LiquidGlass = new(
        "glass", "Liquid Glass", false, BackdropKind.Blur,
        Bar: C(0x38202428), BarBorderTop: C(0x73FFFFFF), BarBorderBottom: C(0x1AFFFFFF), Sheen: C(0x1CFFFFFF),
        Tile: C(0x330A0C10), TileHover: C(0x4D16181E), TileBorder: C(0x1AFFFFFF),
        Text: C(0xFFFFFFFF), TextSecondary: C(0xBFFFFFFF), TextTertiary: C(0x73FFFFFF),
        Track: C(0x40FFFFFF), Control: C(0x33FFFFFF), ControlHover: C(0x4DFFFFFF),
        Flyout: C(0xFF1E2024), FlyoutBorder: C(0x40FFFFFF), Divider: C(0x26FFFFFF),
        DarkRed, DarkGreen, DarkOrange, DarkYellow, DarkPurple);

    /// <summary>Light frosted glass with dark text (iOS control-centre look).</summary>
    public static readonly Palette FrostGlass = new(
        "frost", "Frost Glass", true, BackdropKind.Acrylic,
        Bar: C(0x8CF2F3F6), BarBorderTop: C(0xCCFFFFFF), BarBorderBottom: C(0x33FFFFFF), Sheen: C(0x33FFFFFF),
        Tile: C(0x80FFFFFF), TileHover: C(0xB3FFFFFF), TileBorder: C(0x40FFFFFF),
        Text: C(0xFF000000), TextSecondary: C(0xA63C3C43), TextTertiary: C(0x663C3C43),
        Track: C(0x33000000), Control: C(0x66FFFFFF), ControlHover: C(0x99FFFFFF),
        Flyout: C(0xFFF4F5F8), FlyoutBorder: C(0x80FFFFFF), Divider: C(0x1A000000),
        LightRed, LightGreen, LightOrange, LightYellow, LightPurple);

    public static readonly IReadOnlyList<Palette> All = new[] { Black, White, LiquidGlass, FrostGlass, Midnight };

    public static Palette Get(string id) => All.FirstOrDefault(p => p.Id == id) ?? LiquidGlass;

    public static readonly (string Name, string Hex)[] Accents =
    {
        ("Blue", "#0A84FF"), ("Purple", "#BF5AF2"), ("Pink", "#FF375F"), ("Red", "#FF453A"),
        ("Orange", "#FF9F0A"), ("Green", "#30D158"), ("Teal", "#64D2FF"), ("Graphite", "#8E8E93"),
    };
}

/// <summary>Pushes the active palette into Application resources as brushes (DynamicResource keys).</summary>
public static class ThemeManager
{
    public static Palette Current { get; private set; } = Palettes.LiquidGlass;
    public static Color Accent { get; private set; } = Color.FromRgb(0x0A, 0x84, 0xFF);
    public static event Action? Changed;

    /// <summary>Whether the bar shows the blurred background (style default or the Glass setting).</summary>
    public static bool GlassOn { get; private set; }
    /// <summary>Liquid Glass extras on: specular rim on the bar and tiles, stronger top glow.</summary>
    public static bool Liquid { get; private set; }

    static Color Blend(Color c, Color under)
    {
        double a = c.A / 255.0;
        return Color.FromRgb((byte)(c.R * a + under.R * (1 - a)), (byte)(c.G * a + under.G * (1 - a)), (byte)(c.B * a + under.B * (1 - a)));
    }

    /// <summary>Specular edge: bright along the top-left, fading, a faint light again at the bottom.</summary>
    static Brush Rim(byte top, byte mid, byte bottom)
    {
        var g = new LinearGradientBrush { StartPoint = new Point(0.2, 0), EndPoint = new Point(0.8, 1) };
        g.GradientStops.Add(new GradientStop(Color.FromArgb(top, 255, 255, 255), 0));
        g.GradientStops.Add(new GradientStop(Color.FromArgb(mid, 255, 255, 255), 0.45));
        g.GradientStops.Add(new GradientStop(Color.FromArgb(bottom, 255, 255, 255), 1));
        g.Freeze();
        return g;
    }

    /// <summary>Size multiplier for S / M / L.</summary>
    public static double SizeScale => Settings.Current.Size switch { "S" => 0.86, "L" => 1.16, _ => 1.0 };

    public static void Apply()
    {
        var s = Settings.Current;
        Current = Palettes.Get(s.Theme);
        try { Accent = (Color)ColorConverter.ConvertFromString(s.Accent); } catch { }

        var r = Application.Current.Resources;
        var p = Current;
        void B(string key, Color c) { var b = new SolidColorBrush(c); b.Freeze(); r[key] = b; }

        // Glass effect: "auto" follows the style; off / frosted / liquid override it.
        GlassOn = s.GlassEffect switch { "off" => false, "frosted" or "liquid" => true, _ => p.Backdrop != BackdropKind.None };
        Liquid = s.GlassEffect == "liquid";
        var bar = p.Bar;
        if (GlassOn && bar.A > 0xC0) bar.A = p.IsLight ? (byte)0xB0 : (byte)0xA8;   // let the glass show through solid styles
        if (!GlassOn && bar.A < 0xF0) bar = Blend(bar, p.IsLight ? Colors.White : Color.FromRgb(0x10, 0x10, 0x12));
        B("Bar.Background", bar);
        var border = new LinearGradientBrush(p.BarBorderTop, p.BarBorderBottom, 90);
        border.Freeze();
        r["Bar.Border"] = border;
        var sheenColor = Liquid ? Color.FromArgb(Math.Max(p.Sheen.A, (byte)0x2E), 255, 255, 255) : p.Sheen;
        var sheen = new LinearGradientBrush(
            new GradientStopCollection { new(sheenColor, 0), new(Color.FromArgb(0, sheenColor.R, sheenColor.G, sheenColor.B), Liquid ? 0.6 : 0.55) }, 90);
        sheen.Freeze();
        r["Bar.Sheen"] = sheen;
        r["Bar.Rim"] = Liquid ? Rim(0x9C, 0x14, 0x40) : Brushes.Transparent;

        var tile = TileOverride(s.WidgetStyle) ?? (p.Tile, p.TileHover, p.TileBorder);
        if (s.GlassTiles && GlassOn)
            tile = p.IsLight
                ? (Color.FromArgb(0x52, 255, 255, 255), Color.FromArgb(0x7A, 255, 255, 255), Color.FromArgb(0x8C, 255, 255, 255))
                : (Color.FromArgb(0x1C, 255, 255, 255), Color.FromArgb(0x2C, 255, 255, 255), Color.FromArgb(0x2E, 255, 255, 255));
        B("Tile.Background", tile.Item1);
        B("Tile.Hover", tile.Item2);
        if (Liquid) r["Tile.Border"] = Rim(0x66, 0x0A, 0x26); else B("Tile.Border", tile.Item3);

        B("Text.Primary", p.Text);
        B("Text.Secondary", p.TextSecondary);
        B("Text.Tertiary", p.TextTertiary);
        B("Fill.Track", p.Track);
        B("Fill.Control", p.Control);
        B("Fill.ControlHover", p.ControlHover);
        B("Flyout.Background", p.Flyout);
        B("Flyout.Border", p.FlyoutBorder);
        B("Divider", p.Divider);
        B("Sys.Red", p.Red);
        B("Sys.Green", p.Green);
        B("Sys.Orange", p.Orange);
        B("Sys.Yellow", p.Yellow);
        B("Sys.Purple", p.Purple);
        B("Sys.Blue", Accent);
        B("Accent", Accent);
        B("Accent.Soft", Color.FromArgb(0x2E, Accent.R, Accent.G, Accent.B));
        B("Accent.Border", Color.FromArgb(0xCC, Accent.R, Accent.G, Accent.B));
        B("OnAccent", Colors.White);
        // One physical pixel, so borders stay crisp hairlines at 150–200 % scaling instead of 2 px rules.
        double dpi = 1;
        try { Native.GetPrimaryMonitor(out var hmon); dpi = Math.Max(1, Native.GetMonitorScale(hmon)); } catch { }
        r["Hairline"] = new Thickness(1 / dpi);
        B("Transparent", Colors.Transparent);
        r["Accent.Color"] = Accent;
        r["Theme.IsLight"] = p.IsLight;

        Changed?.Invoke();
    }

    static (Color, Color, Color)? TileOverride(string style) => style switch
    {
        "black" => (Color.FromRgb(0x16, 0x16, 0x18), Color.FromRgb(0x20, 0x20, 0x23), Color.FromArgb(0x1A, 255, 255, 255)),
        "white" => (Color.FromRgb(0xF4, 0xF4, 0xF6), Color.FromRgb(0xEA, 0xEA, 0xEE), Color.FromArgb(0x14, 0, 0, 0)),
        "glass" => (Color.FromArgb(0x33, 0x0A, 0x0C, 0x10), Color.FromArgb(0x4D, 0x16, 0x18, 0x1E), Color.FromArgb(0x1A, 255, 255, 255)),
        _ => null,
    };
}

/// <summary>DWM backdrop for a window: blur / acrylic via the accent policy (works on Win10 and Win11).</summary>
public static class Backdrop
{
    const int ACCENT_DISABLED = 0, ACCENT_ENABLE_BLURBEHIND = 3, ACCENT_ENABLE_ACRYLICBLURBEHIND = 4;

    public static void Apply(IntPtr hwnd, BackdropKind kind, Color tint)
    {
        if (hwnd == IntPtr.Zero) return;
        var accent = new Native.AccentPolicy
        {
            AccentState = kind switch
            {
                BackdropKind.Blur => ACCENT_ENABLE_BLURBEHIND,
                BackdropKind.Acrylic => ACCENT_ENABLE_ACRYLICBLURBEHIND,
                _ => ACCENT_DISABLED,
            },
            AccentFlags = 2,
            // AABBGGRR. Acrylic needs a non-zero alpha or it renders opaque on some builds.
            GradientColor = kind == BackdropKind.Acrylic
                ? (uint)((0x01 << 24) | (tint.B << 16) | (tint.G << 8) | tint.R)
                : 0u,
        };
        int size = Marshal.SizeOf(accent);
        var ptr = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(accent, ptr, false);
            var data = new Native.WindowCompositionAttribData { Attribute = 19 /*WCA_ACCENT_POLICY*/, Data = ptr, SizeOfData = size };
            Native.SetWindowCompositionAttribute(hwnd, ref data);
        }
        finally { Marshal.FreeHGlobal(ptr); }
    }
}
