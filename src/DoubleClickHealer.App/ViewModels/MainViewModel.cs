using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Threading;
using DoubleClickHealer.App.Services;
using DoubleClickHealer.Core;
using DoubleClickHealer.Core.Config;
using DoubleClickHealer.Core.Engine;

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
    private bool _isRecording;
    private string _statusText = "就绪";

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
                StatusText = "无法修改开机自启项";
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

            bool succeeded = value ? ElevationService.Enable() : ElevationService.Disable();

            if (!succeeded)
            {
                _elevatedMode = !value;
                OnPropertyChanged(nameof(ElevatedMode));
                StatusText = value ? "增强模式启用失败，需要管理员确认" : "增强模式关闭失败";
                return;
            }

            StatusText = value ? "增强模式已启用，重启软件后生效" : "增强模式已关闭，重启软件后生效";
            ScheduleSave();
        }
    }

    public string AddButtonText => _isRecording ? "按下要添加的按键…（Esc 取消）" : "＋ 添加按键";

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public void ReportStatus(string text) => StatusText = text;

    public void LoadFromConfig(AppConfig config)
    {
        if (_isRecording)
        {
            _recorder.Stop();
            SetRecording(false);
        }

        _enabled = config.Enabled;
        _runAtStartup = config.RunAtStartup;
        _elevatedMode = config.ElevatedMode;
        OnPropertyChanged(nameof(Enabled));
        OnPropertyChanged(nameof(RunAtStartup));
        OnPropertyChanged(nameof(ElevatedMode));

        Rules.Clear();
        foreach (var rule in config.Targets)
        {
            var viewModel = new KeyRuleViewModel(rule);
            viewModel.Changed += ScheduleSave;
            Rules.Add(viewModel);
        }

        StatusText = Rules.Count == 0
            ? "还没有添加按键"
            : $"已监控 {Rules.Count(r => r.Enabled)} 个按键";
    }

    private void ToggleRecording()
    {
        if (_isRecording)
        {
            _recorder.Stop();
            SetRecording(false);
            StatusText = "已取消";
            return;
        }

        if (_recorder.IsRecording)
            _recorder.Stop();

        SetRecording(true);
        StatusText = "请按下要添加的按键，Esc 取消";

        _recorder.Start(
            onCaptured: (kind, code) => Application.Current.Dispatcher.BeginInvoke(() => OnCaptured(kind, code)),
            onCanceled: () => Application.Current.Dispatcher.BeginInvoke(() =>
            {
                SetRecording(false);
                StatusText = "已取消";
            }));
    }

    private void OnCaptured(InputKind kind, int code)
    {
        _recorder.Stop();
        SetRecording(false);

        if (Rules.Any(r => r.Kind == kind && r.Code == code))
        {
            StatusText = $"{KeyNames.Get(kind, code)} 已在列表中";
            return;
        }

        KeyRule rule = kind == InputKind.Mouse
            ? KeyRule.Mouse(code, 80)
            : KeyRule.Keyboard(code, 80);

        var viewModel = new KeyRuleViewModel(rule);
        viewModel.Changed += ScheduleSave;
        Rules.Add(viewModel);

        StatusText = $"已添加 {viewModel.Name}";
        Persist();
    }

    private void RemoveRule(KeyRuleViewModel? rule)
    {
        if (rule == null) return;

        rule.Changed -= ScheduleSave;
        Rules.Remove(rule);
        StatusText = $"已移除 {rule.Name}";
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
            Targets = Rules.Select(rule => rule.ToModel()).ToList(),
        };

        _store.Save(config);
        _engine.UpdateRules(_enabled ? config.Targets : []);
    }

    public void Dispose()
    {
        _saveTimer.Stop();
        _recorder.Dispose();
    }
}
