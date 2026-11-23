using System.IO;
using System.Windows;
using System.Windows.Threading;
using DoubleClickHealer.App.Services;
using DoubleClickHealer.App.ViewModels;
using DoubleClickHealer.Core;
using DoubleClickHealer.Core.Config;
using DoubleClickHealer.Core.Engine;
using DoubleClickHealer.Core.Input;

namespace DoubleClickHealer.App;

public partial class App : Application
{
    private SingleInstance? _singleInstance;
    private ConfigStore? _store;
    private HealerEngine? _engine;
    private InputHook? _hook;
    private TrayService? _tray;
    private MainWindow? _window;
    private AboutWindow? _about;
    private MainViewModel? _viewModel;
    private bool _exiting;
    private DateTime _lastErrorShownAt;
    private string? _lastErrorMessage;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DispatcherUnhandledException += OnUnhandledException;

        _singleInstance = new SingleInstance();
        if (!_singleInstance.IsFirstInstance)
        {
            // 开机时的第二个启动项（Run 键和计划任务同时存在）不应把主窗口叫出来。
            if (!e.Args.Contains("--minimized"))
                SingleInstance.SignalExisting();

            Shutdown();
            return;
        }

        bool firstRun = !File.Exists(ConfigStore.DefaultPath);

        _store = new ConfigStore();
        if (_store.LastError != null)
        {
            AppLog.Write(
                _store.RecoveredFromCorruptFile
                    ? "配置文件损坏，已备份并恢复默认配置: " + _store.LastError
                    : "配置读取失败，暂时使用默认配置: " + _store.LastError);
        }

        _store.EnsureFile();

        // 读取失败时不要用默认配置覆盖原文件，保留用户数据等待恢复。
        if (_store.LastError == null)
            _store.Save(_store.Current); // 迁移后的配置立即落盘

        _store.StartWatching();

        // 旧版本可能残留提权任务，导致登录时 Run 键和计划任务同时拉起应用。
        // 增强模式关闭时清理，避免双启动和意外的提权运行。
        bool elevatedMode = _store.Current.ElevatedMode;
        _ = Task.Run(() =>
        {
            try
            {
                if (!elevatedMode && ElevationService.IsTaskInstalled() && !ElevationService.Disable())
                    AppLog.Write("清理残留提权任务失败，可能需要管理员权限");
            }
            catch (Exception ex)
            {
                AppLog.Write("清理残留提权任务失败", ex);
            }
        });

        LocalizationService.Initialize(_store.Current.Language);

        try
        {
            StartupService.SetEnabled(_store.Current.RunAtStartup);
        }
        catch (Exception)
        {
            // 无权限写入 Run 项时忽略，界面里仍可手动调整。
        }

        _engine = new HealerEngine(SelectRules(_store.Current));
        _engine.Start();

        _hook = new InputHook(_engine);

        bool hookFailed = false;
        Exception? hookError = null;
        try
        {
            _hook.Start();
        }
        catch (Exception ex)
        {
            hookFailed = true;
            hookError = ex;
        }

        _viewModel = new MainViewModel(_store, _engine);
        _window = new MainWindow(_viewModel);
        _window.Closed += (_, _) => _window = null;

        _singleInstance.Listen(() => Dispatcher.BeginInvoke(ShowMainWindow));

        _tray = new TrayService(
            show: ShowMainWindow,
            exit: ExitApplication,
            about: ShowAbout,
            store: _store);

        _store.Changed += config => Dispatcher.BeginInvoke(() =>
        {
            if (_exiting) return;
            _viewModel?.LoadFromConfig(config);
            _engine?.UpdateRules(SelectRules(config));
        });

        if (hookFailed)
        {
            AppLog.Write("安装全局钩子失败", hookError);
            _viewModel.ReportStatus(LocalizationService.Get("StrHookFailed"));
        }
        else if (!_hook.MouseHookActive || !_hook.KeyboardHookActive)
        {
            AppLog.Write($"部分输入钩子安装失败（鼠标: {_hook.MouseHookActive}，键盘: {_hook.KeyboardHookActive}）");
            _viewModel.ReportStatus(LocalizationService.Get("StrHookPartialFailed"));
        }
        else if (_store.RecoveredFromCorruptFile)
        {
            _viewModel.ReportStatus(LocalizationService.Get("StrConfigRecovered"));
        }
        else if (_store.LastError != null)
        {
            _viewModel.ReportStatus(LocalizationService.Get("StrConfigLoadFailed"));
        }

        // 由开机自启拉起时始终隐藏窗口，与是否首次运行无关。
        bool launchedAtStartup = e.Args.Contains("--minimized");
        bool startHidden = launchedAtStartup || (!firstRun && _store.Current.StartMinimized);
        if (!startHidden) ShowMainWindow();
    }

    private static List<KeyRule> SelectRules(AppConfig config) =>
        config.Enabled ? config.Targets.Where(rule => rule.Enabled).ToList() : [];

    private void ShowMainWindow()
    {
        if (_exiting || _viewModel == null) return;

        if (_window == null)
        {
            _window = new MainWindow(_viewModel);
            _window.Closed += (_, _) => _window = null;
        }

        _window.Show();
        _window.WindowState = WindowState.Normal;
        _window.Activate();
    }

    private void ShowAbout()
    {
        if (_about != null)
        {
            _about.Activate();
            return;
        }

        var about = new AboutWindow();
        _about = about;
        about.Closed += (_, _) => _about = null;
        about.ShowDialog();
    }

    private void ExitApplication()
    {
        if (_exiting) return;
        _exiting = true;

        _viewModel?.Dispose();
        _hook?.Dispose();
        _engine?.Dispose();
        _store?.Dispose();
        _tray?.Dispose();
        _singleInstance?.Dispose();

        Shutdown();
    }

    private void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        e.Handled = true;

        AppLog.Write("未处理的界面线程异常", e.Exception);

        // 同一异常连续发生时避免弹出无数个对话框。
        string message = e.Exception.Message;
        if (message == _lastErrorMessage && (DateTime.UtcNow - _lastErrorShownAt).TotalSeconds < 10)
            return;

        _lastErrorMessage = message;
        _lastErrorShownAt = DateTime.UtcNow;

        MessageBox.Show(message, LocalizationService.Get("StrAppName"),
            MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _viewModel?.Dispose();
        _hook?.Dispose();
        _engine?.Dispose();
        _store?.Dispose();
        _tray?.Dispose();
        _singleInstance?.Dispose();
        base.OnExit(e);
    }
}
