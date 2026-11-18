using System.Drawing;
using DoubleClickHealer.Core.Config;
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
    private readonly ConfigStore _store;

    private TrayMenuWindow? _menu;
    private bool _disposed;

    public TrayService(Action show, Action exit, Action about, ConfigStore store)
    {
        _show = show;
        _exit = exit;
        _about = about;
        _store = store;

        _icon = new Forms.NotifyIcon
        {
            Icon = LoadIcon(),
            Visible = true,
            Text = LocalizationService.Get("StrAppName"),
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

        var items = new List<TrayMenuItem>
        {
            new()
            {
                Text = LocalizationService.Get("StrSettingsTitle"),
                Action = _show,
            },
            new()
            {
                Text = LocalizationService.Get("StrLanguage"),
                Children = new List<TrayMenuItem>
                {
                    new()
                    {
                        Text = LanguageLabel(AppLanguage.Chinese, "StrLanguageChinese"),
                        CloseOnInvoke = false,
                        Action = () => SetLanguage(AppLanguage.Chinese),
                    },
                    new()
                    {
                        Text = LanguageLabel(AppLanguage.English, "StrLanguageEnglish"),
                        CloseOnInvoke = false,
                        Action = () => SetLanguage(AppLanguage.English),
                    },
                },
            },
            new() { Text = string.Empty },
            new()
            {
                Text = LocalizationService.Get("StrAboutTitle"),
                Action = _about,
            },
            new()
            {
                Text = LocalizationService.Get("StrExit"),
                Action = _exit,
            },
        };

        _menu = new TrayMenuWindow(items);
        _menu.Closed += (_, _) => _menu = null;

        Point cursor = Forms.Cursor.Position;
        _menu.ShowAt(cursor.X, cursor.Y);
    }

    private string LanguageLabel(AppLanguage language, string resourceKey)
    {
        string label = LocalizationService.Get(resourceKey);
        return _store.Current.Language == language ? "• " + label : label;
    }

    private void SetLanguage(AppLanguage language)
    {
        if (_store.Current.Language == language) return;

        _store.Save(_store.Current with { Language = language });
        _menu?.Close();
        _menu = null;

        var dialog = new UpdateDialog(
            LocalizationService.Get("StrLanguage"),
            LocalizationService.Get("StrRestartMessage"),
            LocalizationService.Get("StrOk"))
        {
            WindowStartupLocation = System.Windows.WindowStartupLocation.CenterScreen,
            Topmost = true,
        };
        dialog.ShowDialog();
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
