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
    private readonly Action _about;

    private TrayMenuWindow? _menu;
    private bool _disposed;

    public TrayService(Action show, Action exit, Action about)
    {
        _show = show;
        _exit = exit;
        _about = about;

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
            ("设置", _show),
            (string.Empty, null),
            ("关于", _about),
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
        if (_disposed) return;
        _disposed = true;

        _menu?.Close();
        _menu = null;
        _icon.Visible = false;
        _icon.Dispose();
    }
}
