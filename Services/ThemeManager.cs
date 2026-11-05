using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace CDir.Services;

/// <summary>Swaps the DynamicResource palette at runtime and paints the native title bar.</summary>
public static class ThemeManager
{
    public static bool IsDark { get; private set; }

    private static readonly Dictionary<string, string> LightPalette = new()
    {
        ["AppBgBrush"] = "#F3F4F7",
        ["PanelBrush"] = "#FFFFFF",
        ["PanelAltBrush"] = "#FAFBFC",
        ["StrokeBrush"] = "#E1E4EA",
        ["TextBrush"] = "#1B1F24",
        ["SubTextBrush"] = "#6A7078",
        ["AccentBrush"] = "#2563EB",
        ["AccentSoftBrush"] = "#E8F0FE",
        ["HoverBrush"] = "#EDF0F5",
        ["PressedBrush"] = "#E2E6EE",
        ["HeaderBrush"] = "#F7F8FA",
        ["InputBrush"] = "#F7F8FA",
        ["SelectedBgBrush"] = "#DBE7FD",
        ["SelectedFgBrush"] = "#12233F",
        ["DangerBrush"] = "#D33B3B",
    };

    private static readonly Dictionary<string, string> DarkPalette = new()
    {
        ["AppBgBrush"] = "#14171B",
        ["PanelBrush"] = "#1B1F24",
        ["PanelAltBrush"] = "#20252B",
        ["StrokeBrush"] = "#2C3238",
        ["TextBrush"] = "#E6E9EE",
        ["SubTextBrush"] = "#9AA2AC",
        ["AccentBrush"] = "#4C8DFF",
        ["AccentSoftBrush"] = "#1E2A3D",
        ["HoverBrush"] = "#262C33",
        ["PressedBrush"] = "#2F363E",
        ["HeaderBrush"] = "#20252B",
        ["InputBrush"] = "#232930",
        ["SelectedBgBrush"] = "#22334F",
        ["SelectedFgBrush"] = "#F2F6FF",
        ["DangerBrush"] = "#FF6B6B",
    };

    public static void Apply(bool dark)
    {
        IsDark = dark;
        var app = Application.Current;
        if (app is null) return;

        foreach (var (key, hex) in dark ? DarkPalette : LightPalette)
        {
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
            brush.Freeze();
            app.Resources[key] = brush;
        }
    }

    private const int DwmUseImmersiveDarkMode = 20;
    private const int DwmUseImmersiveDarkModeBefore20H1 = 19;

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    public static void ApplyWindowChrome(Window window, bool dark)
    {
        try
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero) return;

            int value = dark ? 1 : 0;
            if (DwmSetWindowAttribute(hwnd, DwmUseImmersiveDarkMode, ref value, sizeof(int)) != 0)
                DwmSetWindowAttribute(hwnd, DwmUseImmersiveDarkModeBefore20H1, ref value, sizeof(int));
        }
        catch
        {
            // Title bar theming is cosmetic; ignore failures on older Windows builds.
        }
    }
}
