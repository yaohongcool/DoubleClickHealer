using DoubleClickHealer.Core.Input;

namespace DoubleClickHealer.Core.Engine;

public sealed class HealerEngine : IDisposable
{
    private readonly object _gate = new();
    private readonly Dictionary<(InputKind Kind, int Code), DebounceState> _states = new();
    private readonly InputInjector _injector = new();

    private ReleaseTimer? _timer;
    private bool _disposed;

    public HealerEngine(IEnumerable<KeyRule> rules)
    {
        foreach (var rule in rules)
        {
            if (!rule.Enabled) continue;
            _states[(rule.Kind, rule.Code)] = new DebounceState(rule);
        }
    }

    public bool IsRunning => _timer != null;

    /// <summary>实际补发事件后触发（kind, code, isDown），供诊断使用。</summary>
    public Action<InputKind, int, bool>? OnInjected
    {
        get => _injector.OnInjected;
        set => _injector.OnInjected = value;
    }

    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_timer != null) return;
        _timer = new ReleaseTimer(FindNextDue, ReleaseExpired);
    }

    public void Stop()
    {
        var timer = _timer;
        _timer = null;
        timer?.Dispose();
    }

    /// <summary>由钩子线程调用，只做状态判定，不阻塞。</summary>
    public InputVerdict Process(InputKind kind, int code, bool isDown)
    {
        StateOutcome outcome;
        lock (_gate)
        {
            if (!_states.TryGetValue((kind, code), out var state))
                return InputVerdict.PassThrough;

            long now = Clock.Now;
            outcome = isDown ? state.OnDown(now) : state.OnUp(now);
        }

        if (outcome.ReleaseDue != 0 || !isDown)
            _timer?.Nudge();

        return outcome.Verdict;
    }

    /// <summary>判定为真实双击：吞掉原始 DOWN，异步补发「UP + DOWN」。</summary>
    public void QueueReplay(InputKind kind, int code) => _injector.EnqueueReplay(kind, code);

    /// <summary>
    /// 热替换监控规则。被移除或重建的按键若有扣留中的 UP，会立即补发，避免系统卡在按下状态。
    /// </summary>
    public void UpdateRules(IEnumerable<KeyRule> rules)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var next = new Dictionary<(InputKind Kind, int Code), DebounceState>();
        foreach (var rule in rules)
        {
            if (rule.Enabled)
                next[(rule.Kind, rule.Code)] = new DebounceState(rule);
        }

        List<(InputKind Kind, int Code)>? stale = null;

        lock (_gate)
        {
            foreach (var (key, state) in _states)
            {
                if (state.TryTakeRelease(long.MaxValue))
                    (stale ??= new()).Add(key);
            }

            _states.Clear();
            foreach (var (key, state) in next)
                _states[key] = state;
        }

        if (stale != null)
        {
            foreach (var key in stale)
                _injector.EnqueueUp(key.Kind, key.Code);
        }

        _timer?.Nudge();
    }

    private long FindNextDue()
    {
        lock (_gate)
        {
            long earliest = 0;
            foreach (var state in _states.Values)
            {
                if (!state.HasPendingRelease) continue;
                long due = state.ReleaseDue;
                if (earliest == 0 || due < earliest) earliest = due;
            }

            return earliest;
        }
    }

    private void ReleaseExpired()
    {
        List<(InputKind Kind, int Code)>? released = null;

        lock (_gate)
        {
            long now = Clock.Now;
            foreach (var (key, state) in _states)
            {
                if (state.TryTakeRelease(now))
                    (released ??= new()).Add(key);
            }
        }

        if (released == null) return;

        foreach (var key in released)
            _injector.EnqueueUp(key.Kind, key.Code);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
        _injector.Dispose();
    }
}
