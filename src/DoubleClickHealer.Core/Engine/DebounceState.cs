namespace DoubleClickHealer.Core.Engine;

public enum InputVerdict
{
    /// <summary>原始事件放行。</summary>
    PassThrough,

    /// <summary>原始事件吞掉。</summary>
    Swallow,

    /// <summary>先补发被扣留的 UP，再放行本次 DOWN（判定为真实快速双击）。</summary>
    EmitUpThenPass,
}

public readonly record struct StateOutcome(InputVerdict Verdict, long ReleaseDue);

/// <summary>
/// 单个按键的防抖状态机。时间戳由调用方以 Stopwatch tick 传入，便于单元测试。
/// </summary>
public sealed class DebounceState
{
    private readonly bool _isKeyboard;
    private readonly bool _holdEnabled;
    private readonly long _gapTicks;
    private readonly long _pressTicks;
    private readonly long _gapLongTicks;
    private readonly long _holdTicks;

    private bool _isDown;
    private bool _hasUp;
    private bool _pendingUp;
    private bool _suppressUp;
    private long _downTs;
    private long _upTs;
    private long _lastPressTicks;
    private long _releaseDue;

    public DebounceState(KeyRule rule)
    {
        _isKeyboard = rule.Kind == InputKind.Keyboard;
        _holdEnabled = rule.HoldMs > 0;
        _holdTicks = Clock.MsToTicks(rule.HoldMs);
        _gapTicks = Clock.MsToTicks(rule.ChatterGapMs);
        _pressTicks = Clock.MsToTicks(rule.ChatterPressMs);
        _gapLongTicks = Clock.MsToTicks(rule.ChatterGapLongMs);
    }

    public bool HasPendingRelease => _pendingUp;

    public long ReleaseDue => _releaseDue;

    public StateOutcome OnDown(long now)
    {
        if (_pendingUp)
        {
            if (IsChatter(now))
            {
                // 抖动碎片：撤销扣留的 UP，系统侧保持按住。
                _pendingUp = false;
                _releaseDue = 0;
                _isDown = true;
                _downTs = now;
                return new StateOutcome(InputVerdict.Swallow, 0);
            }

            // 真实快速双击：先补发 UP，再放行这次 DOWN。
            _pendingUp = false;
            _releaseDue = 0;
            _isDown = true;
            _downTs = now;
            return new StateOutcome(InputVerdict.EmitUpThenPass, 0);
        }

        if (_isDown)
        {
            // 键盘自动重复应当放行；鼠标的重复 DOWN 属于异常。
            return new StateOutcome(_isKeyboard ? InputVerdict.PassThrough : InputVerdict.Swallow, 0);
        }

        if (!_holdEnabled && IsChatter(now))
        {
            _suppressUp = true;
            return new StateOutcome(InputVerdict.Swallow, 0);
        }

        _isDown = true;
        _downTs = now;
        return new StateOutcome(InputVerdict.PassThrough, 0);
    }

    public StateOutcome OnUp(long now)
    {
        if (!_isDown)
        {
            if (_suppressUp) _suppressUp = false;
            return new StateOutcome(InputVerdict.Swallow, 0);
        }

        _isDown = false;
        _lastPressTicks = now - _downTs;
        _upTs = now;
        _hasUp = true;

        if (!_holdEnabled)
            return new StateOutcome(InputVerdict.PassThrough, 0);

        _pendingUp = true;
        _releaseDue = now + _holdTicks;
        return new StateOutcome(InputVerdict.Swallow, _releaseDue);
    }

    /// <summary>到期则原子地取走待释放状态，调用方随后注入 UP。</summary>
    public bool TryTakeRelease(long now)
    {
        if (!_pendingUp || now < _releaseDue) return false;
        _pendingUp = false;
        _releaseDue = 0;
        return true;
    }

    private bool IsChatter(long now)
    {
        if (!_hasUp) return false;

        long gap = now - _upTs;
        if (gap < _gapTicks) return true;

        // 上一次按压本身就是碎片，那么间隔可以放宽。
        return _lastPressTicks > 0 && _lastPressTicks < _pressTicks && gap < _gapLongTicks;
    }
}
