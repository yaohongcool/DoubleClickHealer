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
    private MainViewModel? _viewModel;
    private bool _exiting;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DispatcherUnhandledException += OnUnhandledException;

        _singleInstance = new SingleInstance();
        if (!_singleInstance.IsFirstInstance)
        {
            SingleInstance.SignalExisting();
            Shutdown();
            return;
        }

        bool firstRun = !File.Exists(ConfigStore.DefaultPath);

        _store = new ConfigStore();
        _store.EnsureFile();
        _store.StartWatching();

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
        try
        {
            _hook.Start();
        }
        catch (Exception)
        {
            hookFailed = true;
        }

        _viewModel = new MainViewModel(_store, _engine);
        _window = new MainWindow(_viewModel);

        _singleInstance.Listen(() => Dispatcher.BeginInvoke(ShowMainWindow));

        _tray = new TrayService(
            show: ShowMainWindow,
            exit: ExitApplication,
            isEnabled: () => _store.Current.Enabled,
            setEnabled: enabled => _viewModel.Enabled = enabled);

        _store.Changed += config => Dispatcher.BeginInvoke(() =>
        {
            _viewModel.LoadFromConfig(config);
            _engine.UpdateRules(SelectRules(config));
        });

        if (hookFailed)
            _viewModel.ReportStatus("全局钩子安装失败，修复未生效");

        bool startHidden = !firstRun && (e.Args.Contains("--minimized") || _store.Current.StartMinimized);
        if (!startHidden) ShowMainWindow();
    }

    private static List<KeyRule> SelectRules(AppConfig config) =>
        config.Enabled ? config.Targets.Where(rule => rule.Enabled).ToList() : [];

    private void ShowMainWindow()
    {
        if (_window == null) return;

        _window.Show();
        _window.WindowState = WindowState.Normal;
        _window.Activate();
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
        _window?.Close();

        Shutdown();
    }

    private void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        e.Handled = true;
        MessageBox.Show(e.Exception.Message, "双击修复器", MessageBoxButton.OK, MessageBoxImage.Warning);
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
