using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
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

    private readonly DispatcherTimer _popupCloseTimer;
    private readonly bool _useBackdrop;
    private DateTime _shownAt;
    private bool _actionInvoked;
    private Popup? _activePopup;

    public TrayMenuWindow(IReadOnlyList<TrayMenuItem> items)
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
        SizeChanged += (_, _) => EnsureOnScreen();

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

        _popupCloseTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(180) };
        _popupCloseTimer.Tick += (_, _) =>
        {
            _popupCloseTimer.Stop();
            ClosePopup();
        };

        var panel = new StackPanel { Margin = new Thickness(4) };

        foreach (TrayMenuItem item in items)
            AddMenuItem(panel, item);

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

    private void AddMenuItem(Panel parent, TrayMenuItem item)
    {
        if (item.Action == null && item.Children == null)
        {
            var separator = new Border { Height = 1, Margin = new Thickness(6, 4, 6, 4) };
            separator.SetResourceReference(Border.BackgroundProperty, "DividerStrokeColorDefaultBrush");
            parent.Children.Add(separator);
            return;
        }

        if (item.Children is { Count: > 0 })
        {
            var content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center,
            };
            content.Children.Add(new TextBlock
            {
                Text = item.Text,
                VerticalAlignment = VerticalAlignment.Center,
            });
            content.Children.Add(new TextBlock
            {
                Text = "  ›",
                Opacity = 0.7,
                VerticalAlignment = VerticalAlignment.Center,
            });

            var expander = new Button
            {
                Content = content,
                Style = (Style)FindResource("TrayMenuItemStyle"),
            };
            System.Windows.Automation.AutomationProperties.SetName(expander, item.Text);

            var popupPanel = new StackPanel { Margin = new Thickness(4) };

            foreach (TrayMenuItem child in item.Children)
            {
                var childButton = new Button
                {
                    Content = child.Text,
                    Style = (Style)FindResource("TrayMenuItemStyle"),
                };
                System.Windows.Automation.AutomationProperties.SetName(childButton, child.Text);
                childButton.Click += (_, _) => Invoke(child);
                popupPanel.Children.Add(childButton);
            }

            var popupCard = new Border
            {
                Margin = new Thickness(10),
                Background = (Brush)Resources["AcrylicBackgroundFillColorDefaultBrush"],
                BorderBrush = (Brush)Resources["CardStrokeColorDefaultBrush"],
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Effect = (Effect)FindResource("MenuShadow"),
                Child = popupPanel,
            };

            foreach (string key in new[]
                     {
                         "TextFillColorPrimaryBrush",
                         "SubtleFillColorSecondaryBrush",
                         "SubtleFillColorTertiaryBrush",
                         "TextFillColorDisabledBrush",
                     })
            {
                if (Resources.Contains(key))
                    popupCard.Resources[key] = Resources[key];
            }

            var popup = new Popup
            {
                Placement = PlacementMode.Right,
                PlacementTarget = expander,
                HorizontalOffset = -6,
                VerticalOffset = -10,
                AllowsTransparency = true,
                StaysOpen = true,
                Child = popupCard,
            };

            expander.MouseEnter += (_, _) => ShowPopup(popup);
            expander.Click += (_, _) => ShowPopup(popup);
            expander.MouseLeave += (_, _) => ScheduleClosePopup();
            popupCard.MouseEnter += (_, _) => _popupCloseTimer.Stop();
            popupCard.MouseLeave += (_, _) => ScheduleClosePopup();

            parent.Children.Add(expander);
            return;
        }

        var button = new Button
        {
            Content = item.Text,
            Style = (Style)FindResource("TrayMenuItemStyle"),
        };
        System.Windows.Automation.AutomationProperties.SetName(button, item.Text);
        button.Click += (_, _) => Invoke(item);
        parent.Children.Add(button);
    }

    private void ShowPopup(Popup popup)
    {
        _popupCloseTimer.Stop();
        if (_activePopup != null && !ReferenceEquals(_activePopup, popup))
            _activePopup.IsOpen = false;

        _activePopup = popup;
        popup.IsOpen = true;
    }

    private void ScheduleClosePopup()
    {
        _popupCloseTimer.Stop();
        _popupCloseTimer.Start();
    }

    private void ClosePopup()
    {
        _popupCloseTimer.Stop();
        if (_activePopup == null) return;

        _activePopup.IsOpen = false;
        _activePopup = null;
    }

    private void Invoke(TrayMenuItem item)
    {
        if (item.CloseOnInvoke)
        {
            _actionInvoked = true;
            ClosePopup();
            Close();
        }

        item.Action?.Invoke();
    }

    private void EnsureOnScreen()
    {
        Rect work = SystemParameters.WorkArea;

        if (ActualHeight > 0 && Top + ActualHeight > work.Bottom)
            Top = Math.Max(work.Top, work.Bottom - ActualHeight);

        if (ActualWidth > 0 && Left + ActualWidth > work.Right)
            Left = Math.Max(work.Left, work.Right - ActualWidth);

        if (Top < work.Top) Top = work.Top;
        if (Left < work.Left) Left = work.Left;
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
        UpdateLayout();
        EnsureOnScreen();
        Activate();

        nint handle = new WindowInteropHelper(this).Handle;
        if (handle != 0) SetForegroundWindow(handle);
    }

    protected override void OnDeactivated(EventArgs e)
    {
        base.OnDeactivated(e);

        // 激活过程中的瞬时失焦不应关闭菜单。
        if ((DateTime.UtcNow - _shownAt).TotalMilliseconds < 200) return;

        // 失焦和菜单项点击可能同时发生，延后一轮再判断，避免抢先关闭。
        Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
        {
            if (!_actionInvoked)
            {
                ClosePopup();
                Close();
            }
        });
    }

    protected override void OnClosed(EventArgs e)
    {
        ClosePopup();
        _popupCloseTimer.Stop();
        base.OnClosed(e);
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(nint hwnd, int attribute, ref int value, int size);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(nint hWnd);
}
