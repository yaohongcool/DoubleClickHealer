using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shell;
using System.Windows.Threading;

namespace DoubleClickHealer.App.Services;

/// <summary>
/// Win11 风格的托盘弹出菜单。使用独立无边框窗口而不是 ContextMenu，
/// 因为托盘点击时应用通常没有焦点，ContextMenu 会立即关闭。
/// </summary>
public sealed class TrayMenuWindow : Window
{
    private const int DwmwaWindowCornerPreference = 33;
    private const int DwmwaSystemBackdropType = 38;
    private const int DwmcpRound = 2;
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

        if (_useBackdrop)
        {
            // 非分层窗口才能拿到 DWM 亚克力；保留系统阴影。
            WindowChrome.SetWindowChrome(this, new WindowChrome
            {
                CaptionHeight = 0,
                ResizeBorderThickness = new Thickness(0),
                GlassFrameThickness = new Thickness(-1),
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
            Background = _useBackdrop
                ? Brushes.Transparent
                : TryFindResource("AcrylicBackgroundFillColorDefaultBrush") as Brush
                  ?? new SolidColorBrush(Color.FromArgb(0xE6, 0xF9, 0xF9, 0xF9)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Child = panel,
        };
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
