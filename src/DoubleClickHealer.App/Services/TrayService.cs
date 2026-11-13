using System.Drawing;
using Forms = System.Windows.Forms;

namespace DoubleClickHealer.App.Services;

/// <summary>
/// 托盘图标与弹出菜单。左键单击显示主界面，右键弹出 Win11 风格菜单。
/// </summary>
public sealed class TrayService : IDisposable
{
    private readonly Forms.NotifyIcon _icon;
    private readonly Action _show;
    private readonly Action _exit;
    private readonly Func<bool> _isEnabled;
    private readonly Action<bool> _setEnabled;

    private TrayMenuWindow? _menu;

    public TrayService(Action show, Action exit, Func<bool> isEnabled, Action<bool> setEnabled)
    {
        _show = show;
        _exit = exit;
        _isEnabled = isEnabled;
        _setEnabled = setEnabled;

        _icon = new Forms.NotifyIcon
        {
            Icon = LoadIcon(),
            Visible = true,
            Text = "双击修复器",
        };

        _icon.MouseClick += (_, e) =>
        {
            if (e.Button == Forms.MouseButtons.Left) _show();
            else if (e.Button == Forms.MouseButtons.Right) ShowMenu();
        };
    }

    private void ShowMenu()
    {
        if (_menu != null)
        {
            _menu.Close();
            _menu = null;
            return;
        }

        var items = new List<(string, Action?)>
        {
            ("显示主界面", _show),
            (_isEnabled() ? "暂停修复" : "启用修复", () => _setEnabled(!_isEnabled())),
            (string.Empty, null),
            ("退出", _exit),
        };

        _menu = new TrayMenuWindow(items);
        _menu.Closed += (_, _) => _menu = null;

        Point cursor = Forms.Cursor.Position;
        _menu.ShowAt(cursor.X, cursor.Y);
    }

    private static Icon LoadIcon()
    {
        try
        {
            string? executable = Environment.ProcessPath;
            if (executable != null)
                return Icon.ExtractAssociatedIcon(executable) ?? SystemIcons.Application;
        }
        catch (Exception)
        {
        }

        return SystemIcons.Application;
    }

    public void Dispose()
    {
        _menu?.Close();
        _menu = null;
        _icon.Visible = false;
        _icon.Dispose();
    }
}
