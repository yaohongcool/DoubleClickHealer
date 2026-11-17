using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using iNKORE.UI.WPF.Modern;

namespace DoubleClickHealer.App.Services;

internal static class AppTheme
{
    private const int DwmwaUseImmersiveDarkMode = 20;

    public static bool IsDark => ThemeManager.Current.ActualApplicationTheme == ApplicationTheme.Dark;

    public static void ApplySolidWindow(Window window)
    {
        bool dark = IsDark;

        window.Resources["SolidBackgroundFillColorBaseBrush"] = new SolidColorBrush(
            dark ? Color.FromRgb(0x20, 0x20, 0x20) : Color.FromRgb(0xF3, 0xF3, 0xF3));
        window.Resources["TextFillColorPrimaryBrush"] = new SolidColorBrush(
            dark ? Colors.White : Color.FromRgb(0x1A, 0x1A, 0x1A));
        window.Resources["TextFillColorSecondaryBrush"] = new SolidColorBrush(
            dark ? Color.FromRgb(0xB0, 0xB0, 0xB0) : Color.FromRgb(0x5D, 0x5D, 0x5D));
        window.Resources["TextFillColorTertiaryBrush"] = new SolidColorBrush(
            dark ? Color.FromRgb(0x8A, 0x8A, 0x8A) : Color.FromRgb(0x8A, 0x8A, 0x8A));
        window.Resources["AccentTextFillColorPrimaryBrush"] = new SolidColorBrush(
            dark ? Color.FromRgb(0x60, 0xCD, 0xFF) : Color.FromRgb(0x00, 0x67, 0xC0));

        window.Resources["ButtonBackground"] = new SolidColorBrush(
            dark ? Color.FromRgb(0x2D, 0x2D, 0x2D) : Color.FromRgb(0xFF, 0xFF, 0xFF));
        window.Resources["ButtonBackgroundPointerOver"] = new SolidColorBrush(
            dark ? Color.FromRgb(0x38, 0x38, 0x38) : Color.FromRgb(0xF5, 0xF5, 0xF5));
        window.Resources["ButtonBackgroundPressed"] = new SolidColorBrush(
            dark ? Color.FromRgb(0x27, 0x27, 0x27) : Color.FromRgb(0xEF, 0xEF, 0xEF));
        window.Resources["ButtonBorderBrush"] = new SolidColorBrush(
            dark ? Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF) : Color.FromRgb(0xE5, 0xE5, 0xE5));
        window.Resources["ButtonBorderBrushPointerOver"] = new SolidColorBrush(
            dark ? Color.FromArgb(0x60, 0xFF, 0xFF, 0xFF) : Color.FromRgb(0xD6, 0xD6, 0xD6));
        window.Resources["ButtonBorderBrushPressed"] = new SolidColorBrush(
            dark ? Color.FromArgb(0x20, 0xFF, 0xFF, 0xFF) : Color.FromRgb(0xDA, 0xDA, 0xDA));
        window.Resources["ButtonForeground"] = new SolidColorBrush(
            dark ? Colors.White : Color.FromRgb(0x1A, 0x1A, 0x1A));
        window.Resources["ButtonForegroundPointerOver"] = new SolidColorBrush(
            dark ? Colors.White : Color.FromRgb(0x1A, 0x1A, 0x1A));
        window.Resources["ButtonForegroundPressed"] = new SolidColorBrush(
            dark ? Color.FromRgb(0xB0, 0xB0, 0xB0) : Color.FromRgb(0x5D, 0x5D, 0x5D));
    }

    public static void ApplyTitleBar(Window window)
    {
        nint handle = new WindowInteropHelper(window).Handle;
        if (handle == 0) return;

        int dark = IsDark ? 1 : 0;
        DwmSetWindowAttribute(handle, DwmwaUseImmersiveDarkMode, ref dark, sizeof(int));
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(nint hwnd, int attribute, ref int value, int size);
}
