using System.Drawing;
using System.Windows.Forms;

namespace DoubleClickHealer.App.Services;

public sealed class TrayService : IDisposable
{
    private readonly NotifyIcon _icon;
    private readonly ToolStripMenuItem _toggleItem;

    public TrayService(Action show, Action exit, Func<bool> isEnabled, Action<bool> setEnabled)
    {
        _icon = new NotifyIcon
        {
            Icon = LoadIcon(),
            Visible = true,
            Text = "双击修复器",
        };

        var menu = new ContextMenuStrip();

        var showItem = new ToolStripMenuItem("显示主界面");
        showItem.Click += (_, _) => show();

        var toggleItem = new ToolStripMenuItem("暂停修复");
        toggleItem.Click += (_, _) => setEnabled(!isEnabled());
        _toggleItem = toggleItem;

        var exitItem = new ToolStripMenuItem("退出");
        exitItem.Click += (_, _) => exit();

        menu.Opening += (_, _) => toggleItem.Text = isEnabled() ? "暂停修复" : "启用修复";

        menu.Items.Add(showItem);
        menu.Items.Add(toggleItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(exitItem);

        _icon.ContextMenuStrip = menu;
        _icon.DoubleClick += (_, _) => show();
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
        _icon.Visible = false;
        _icon.Dispose();
    }
}
