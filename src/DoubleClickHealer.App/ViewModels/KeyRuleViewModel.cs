using DoubleClickHealer.Core;

namespace DoubleClickHealer.App.ViewModels;

public sealed class KeyRuleViewModel : ObservableObject
{
    private bool _enabled;
    private int _holdMs;

    public KeyRuleViewModel(KeyRule rule)
    {
        Kind = rule.Kind;
        Code = rule.Code;
        Name = KeyNames.Get(rule.Kind, rule.Code);
        _enabled = rule.Enabled;
        _holdMs = rule.HoldMs;
    }

    public InputKind Kind { get; }
    public int Code { get; }
    public string Name { get; }

    public bool Enabled
    {
        get => _enabled;
        set
        {
            if (!SetProperty(ref _enabled, value)) return;
            OnPropertyChanged(nameof(Subtitle));
            Changed?.Invoke();
        }
    }

    public int HoldMs
    {
        get => _holdMs;
        set
        {
            int clamped = Math.Clamp(value, 0, 120);
            if (!SetProperty(ref _holdMs, clamped)) return;
            OnPropertyChanged(nameof(Subtitle));
            OnPropertyChanged(nameof(HoldText));
            Changed?.Invoke();
        }
    }

    public string Subtitle => !_enabled
        ? "已停用"
        : _holdMs == 0
            ? "轻量防抖 · 零延迟"
            : $"深度防抖 · 扣留 {_holdMs}ms";

    public string HoldText => _holdMs == 0 ? "轻量" : $"{_holdMs} ms";

    public event Action? Changed;

    public KeyRule ToModel() => new()
    {
        Kind = Kind,
        Code = Code,
        Enabled = _enabled,
        HoldMs = _holdMs,
    };
}
