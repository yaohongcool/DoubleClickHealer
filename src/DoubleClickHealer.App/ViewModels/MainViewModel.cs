using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Threading;
using DoubleClickHealer.App.Services;
using DoubleClickHealer.Core;
using DoubleClickHealer.Core.Config;
using DoubleClickHealer.Core.Engine;
using iNKORE.UI.WPF.Modern;
using Microsoft.Win32;

namespace DoubleClickHealer.App.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private readonly ConfigStore _store;
    private readonly HealerEngine _engine;
    private readonly DispatcherTimer _saveTimer;
    private readonly KeyRecorder _recorder = new();

    private bool _enabled;
    private bool _runAtStartup;
    private bool _elevatedMode;
    private AppThemeMode _theme = AppThemeMode.System;
    private bool _isRecording;
    private string _statusText = "";
    private int _elevationRequest;
    private bool _disposed;

    public MainViewModel(ConfigStore store, HealerEngine engine)
    {
        _store = store;
        _engine = engine;

        _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
        _saveTimer.Tick += (_, _) =>
        {
            _saveTimer.Stop();
            Persist();
        };

        AddRuleCommand = new RelayCommand(_ => ToggleRecording());
        RemoveRuleCommand = new RelayCommand(p => RemoveRule(p as KeyRuleViewModel));

        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;

        LoadFromConfig(store.Current);
    }

    public ObservableCollection<KeyRuleViewModel> Rules { get; } = [];

    public RelayCommand AddRuleCommand { get; }
    public RelayCommand RemoveRuleCommand { get; }

    public bool Enabled
    {
        get => _enabled;
        set
        {
            if (!SetProperty(ref _enabled, value)) return;
            ScheduleSave();
        }
    }

    public bool RunAtStartup
    {
        get => _runAtStartup;
        set
        {
            if (!SetProperty(ref _runAtStartup, value)) return;

            try
            {
                StartupService.SetEnabled(value);
            }
            catch (Exception)
            {
                StatusText = LocalizationService.Get("StrStartupFailed");
            }

            ScheduleSave();
        }
    }

    public bool ElevatedMode
    {
        get => _elevatedMode;
        set
        {
            if (!SetProperty(ref _elevatedMode, value)) return;
            _ = ApplyElevatedModeAsync(value);
        }
    }

    /// <summary>
    /// 提权操作会弹出 UAC 并等待用户确认，必须放到后台线程，不能阻塞 UI。
    /// </summary>
    private async Task ApplyElevatedModeAsync(bool enable)
    {
        int request = ++_elevationRequest;
        StatusText = LocalizationService.Get("StrElevationApplying");

        bool succeeded;
        try
        {
            succeeded = await Task.Run(() => enable ? ElevationService.Enable() : ElevationService.Disable());
        }
        catch (Exception)
        {
            succeeded = false;
        }

        if (request != _elevationRequest || _disposed) return;

        if (!succeeded)
        {
            _elevatedMode = !enable;
            OnPropertyChanged(nameof(ElevatedMode));
            StatusText = enable
                ? LocalizationService.Get("StrElevationEnableFailed")
                : LocalizationService.Get("StrElevationDisableFailed");
            return;
        }

        StatusText = enable
            ? LocalizationService.Get("StrElevationEnabled")
            : LocalizationService.Get("StrElevationDisabled");
        ScheduleSave();
    }

    public AppThemeMode Theme
    {
        get => _theme;
        set
        {
            if (!SetProperty(ref _theme, value)) return;

            OnPropertyChanged(nameof(ThemeIndex));
            ApplyTheme(value);
            ScheduleSave();
        }
    }

    public int ThemeIndex
    {
        get => (int)_theme;
        set
        {
            if (value < 0 || value > 2) return;
            Theme = (AppThemeMode)value;
        }
    }

    // iNKORE 在 net8 下检测不到系统主题（ColorsHelper 回退到 Light），
    // 所以「跟随系统」由项目自己读注册表 + 监听 SystemEvents 实现。
    private void ApplyTheme(AppThemeMode mode)
    {
        bool light = mode switch
        {
            AppThemeMode.Light => true,
            AppThemeMode.Dark => false,
            _ => IsSystemLight(),
        };

        ThemeManager.Current.ApplicationTheme = light ? ApplicationTheme.Light : ApplicationTheme.Dark;
    }

    private static bool IsSystemLight()
    {
        object? value = Registry.GetValue(
            @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
            "AppsUseLightTheme", 1);

        return value is not int intValue || intValue != 0;
    }

    private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (_theme != AppThemeMode.System) return;
        if (e.Category != UserPreferenceCategory.General && e.Category != UserPreferenceCategory.VisualStyle) return;

        Application? app = Application.Current;
        if (app == null || app.Dispatcher.HasShutdownStarted) return;

        app.Dispatcher.BeginInvoke(() =>
        {
            if (_theme == AppThemeMode.System)
                ApplyTheme(AppThemeMode.System);
        });
    }

    public string AddButtonText => LocalizationService.Get(
        _isRecording ? "StrAddKeyRecording" : "StrAddKey");

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public void ReportStatus(string text) => StatusText = text;

    public void LoadFromConfig(AppConfig config)
    {
        _elevationRequest++;

        if (_isRecording)
        {
            _recorder.Stop();
            SetRecording(false);
        }

        _enabled = config.Enabled;
        _runAtStartup = config.RunAtStartup;
        _elevatedMode = config.ElevatedMode;
        _theme = config.Theme;
        OnPropertyChanged(nameof(Enabled));
        OnPropertyChanged(nameof(RunAtStartup));
        OnPropertyChanged(nameof(ElevatedMode));
        OnPropertyChanged(nameof(Theme));
        OnPropertyChanged(nameof(ThemeIndex));
        ApplyTheme(config.Theme);

        Rules.Clear();
        foreach (var rule in config.Targets)
        {
            var viewModel = new KeyRuleViewModel(rule);
            viewModel.Changed += ScheduleSave;
            Rules.Add(viewModel);
        }

        StatusText = Rules.Count == 0
            ? LocalizationService.Get("StrNoKeys")
            : LocalizationService.Format("StrMonitoring", Rules.Count(r => r.Enabled));
    }

    private void ToggleRecording()
    {
        if (_isRecording)
        {
            _recorder.Stop();
            SetRecording(false);
            StatusText = LocalizationService.Get("StrCanceled");
            return;
        }

        if (_recorder.IsRecording)
            _recorder.Stop();

        SetRecording(true);
        StatusText = LocalizationService.Get("StrPressKey");

        _recorder.Start(
            onCaptured: (kind, code) => Application.Current.Dispatcher.BeginInvoke(() => OnCaptured(kind, code)),
            onCanceled: () => Application.Current.Dispatcher.BeginInvoke(() =>
            {
                SetRecording(false);
                StatusText = LocalizationService.Get("StrCanceled");
            }));
    }

    private void OnCaptured(InputKind kind, int code)
    {
        _recorder.Stop();
        SetRecording(false);

        if (Rules.Any(r => r.Kind == kind && r.Code == code))
        {
            StatusText = LocalizationService.Format("StrAlreadyInList", KeyNames.Get(kind, code));
            return;
        }

        KeyRule rule = kind == InputKind.Mouse
            ? KeyRule.Mouse(code, 60)
            : KeyRule.Keyboard(code, 0);

        var viewModel = new KeyRuleViewModel(rule);
        viewModel.Changed += ScheduleSave;
        Rules.Add(viewModel);

        StatusText = LocalizationService.Format("StrAdded", viewModel.Name);
        Persist();
    }

    private void RemoveRule(KeyRuleViewModel? rule)
    {
        if (rule == null) return;

        rule.Changed -= ScheduleSave;
        Rules.Remove(rule);
        StatusText = LocalizationService.Format("StrRemoved", rule.Name);
        Persist();
    }

    private void SetRecording(bool recording)
    {
        _isRecording = recording;
        OnPropertyChanged(nameof(AddButtonText));
    }

    private void ScheduleSave()
    {
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    private void Persist()
    {
        var config = _store.Current with
        {
            Enabled = _enabled,
            RunAtStartup = _runAtStartup,
            ElevatedMode = _elevatedMode,
            Theme = _theme,
            Targets = Rules.Select(rule => rule.ToModel()).ToList(),
        };

        bool saved = _store.Save(config);
        _engine.UpdateRules(_enabled ? config.Targets : []);

        if (!saved)
            StatusText = LocalizationService.Get("StrSaveFailed");
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
        _saveTimer.Stop();
        _recorder.Dispose();
    }
}
