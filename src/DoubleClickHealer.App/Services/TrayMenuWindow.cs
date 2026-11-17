using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shell;
using System.Windows.Threading;
using iNKORE.UI.WPF.Modern;

namespace DoubleClickHealer.App.Services;

/// <summary>
/// Win11 风格的托盘弹出菜单。使用独立无边框窗口而不是 ContextMenu，
/// 因为托盘点击时应用通常没有焦点，ContextMenu 会立即关闭。
/// </summary>
public sealed class TrayMenuWindow : Window
{
    private const int DwmwaWindowCornerPreference = 33;
    private const int DwmwaBorderColor = 34;
    private const int DwmwaSystemBackdropType = 38;
    private const int DwmcpRound = 2;
    private const int DwmColorNone = unchecked((int)0xFFFFFFFE);
    private const int DwmsbtTransientWindow = 3;
    private const int Windows11AcrylicBuild = 22621;

    private readonly DispatcherTimer _autoClose;
    private readonly bool _useBackdrop;
    private DateTime _shownAt;
    private bool _actionInvoked;

    public TrayMenuWindow(IReadOnlyList<(string Text, Action? Action)> items)
    {
        _useBackdrop = Environment.OSVersion.Version.Build >= Windows11AcrylicBuild;

        WindowStyle = WindowStyle.None;
        AllowsTransparency = !_useBackdrop;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        Topmost = true;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.Manual;

        ElementTheme theme = ThemeManager.Current.ActualApplicationTheme == ApplicationTheme.Dark
            ? ElementTheme.Dark
            : ElementTheme.Light;
        ThemeManager.SetRequestedTheme(this, theme);

        bool dark = theme == ElementTheme.Dark;
        Resources["TextFillColorPrimaryBrush"] =
            new SolidColorBrush(dark ? Colors.White : Color.FromRgb(0x1A, 0x1A, 0x1A));
        Resources["TextFillColorDisabledBrush"] =
            new SolidColorBrush(dark ? Color.FromRgb(0x9A, 0x9A, 0x9A) : Color.FromRgb(0x8A, 0x8A, 0x8A));
        Resources["SubtleFillColorSecondaryBrush"] =
            new SolidColorBrush(dark ? Color.FromArgb(0x1E, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x12, 0x00, 0x00, 0x00));
        Resources["SubtleFillColorTertiaryBrush"] =
            new SolidColorBrush(dark ? Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x20, 0x00, 0x00, 0x00));
        Resources["DividerStrokeColorDefaultBrush"] =
            new SolidColorBrush(dark ? Color.FromArgb(0x28, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x28, 0x00, 0x00, 0x00));
        Resources["CardStrokeColorDefaultBrush"] =
            new SolidColorBrush(dark ? Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x30, 0x00, 0x00, 0x00));
        Resources["AcrylicBackgroundFillColorDefaultBrush"] =
            new SolidColorBrush(dark ? Color.FromArgb(0xE6, 0x2B, 0x2B, 0x2B) : Color.FromArgb(0xE6, 0xF9, 0xF9, 0xF9));

        if (_useBackdrop)
        {
            // 非分层窗口才能拿到 DWM 亚克力；不启用玻璃框，避免系统绘制白色边框。
            WindowChrome.SetWindowChrome(this, new WindowChrome
            {
                CaptionHeight = 0,
                ResizeBorderThickness = new Thickness(0),
                GlassFrameThickness = new Thickness(0),
                CornerRadius = new CornerRadius(0),
                UseAeroCaptionButtons = false,
            });
        }

        _autoClose = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _autoClose.Tick += (_, _) =>
        {
            _autoClose.Stop();
            Close();
        };

        var panel = new StackPanel { Margin = new Thickness(4) };

        foreach (var (text, action) in items)
        {
            if (action == null)
            {
                var separator = new Border { Height = 1, Margin = new Thickness(6, 4, 6, 4) };
                separator.SetResourceReference(Border.BackgroundProperty, "DividerStrokeColorDefaultBrush");
                panel.Children.Add(separator);
                continue;
            }

            var button = new Button
            {
                Content = text,
                Style = (Style)FindResource("TrayMenuItemStyle"),
            };

            Action handler = action;
            button.Click += (_, _) =>
            {
                _actionInvoked = true;
                Close();
                handler();
            };

            panel.Children.Add(button);
        }

        var card = new Border
        {
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Child = panel,
        };
        card.SetResourceReference(Border.BackgroundProperty, "AcrylicBackgroundFillColorDefaultBrush");
        card.SetResourceReference(Border.BorderBrushProperty, "CardStrokeColorDefaultBrush");

        if (!_useBackdrop)
            card.Effect = (Effect)FindResource("MenuShadow");

        Content = card;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        if (!_useBackdrop) return;

        nint handle = new WindowInteropHelper(this).Handle;
        if (handle == 0) return;

        int corner = DwmcpRound;
        DwmSetWindowAttribute(handle, DwmwaWindowCornerPreference, ref corner, sizeof(int));

        int backdrop = DwmsbtTransientWindow;
        DwmSetWindowAttribute(handle, DwmwaSystemBackdropType, ref backdrop, sizeof(int));

        int borderColor = DwmColorNone;
        DwmSetWindowAttribute(handle, DwmwaBorderColor, ref borderColor, sizeof(int));
    }

    public void ShowAt(int physicalX, int physicalY)
    {
        DpiScale dpi = VisualTreeHelper.GetDpi(this);
        Left = physicalX / dpi.DpiScaleX;
        Top = physicalY / dpi.DpiScaleY;

        _shownAt = DateTime.UtcNow;
        Show();
        Activate();

        nint handle = new WindowInteropHelper(this).Handle;
        if (handle != 0) SetForegroundWindow(handle);

        _autoClose.Start();
    }

    protected override void OnDeactivated(EventArgs e)
    {
        base.OnDeactivated(e);

        // 激活过程中的瞬时失焦不应关闭菜单。
        if ((DateTime.UtcNow - _shownAt).TotalMilliseconds < 200) return;

        // 失焦和菜单项点击可能同时发生，延后一轮再判断，避免抢先关闭。
        Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
        {
            if (!_actionInvoked) Close();
        });
    }

    protected override void OnClosed(EventArgs e)
    {
        _autoClose.Stop();
        base.OnClosed(e);
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(nint hwnd, int attribute, ref int value, int size);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(nint hWnd);
}
