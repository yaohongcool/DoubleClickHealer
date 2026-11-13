using System.ComponentModel;
using System.Runtime.InteropServices;
using DoubleClickHealer.Core.Interop;

namespace DoubleClickHealer.Core.Engine;

/// <summary>
/// 单线程高精度延迟执行器。空闲时无限等待，不占 CPU。
/// </summary>
internal sealed class ReleaseTimer : IDisposable
{
    private readonly Func<long> _nextDue;
    private readonly Action _onDue;
    private readonly AutoResetEvent _wake = new(false);
    private readonly Thread _thread;
    private volatile bool _disposed;
    private nint _timerHandle;

    public ReleaseTimer(Func<long> nextDue, Action onDue)
    {
        _nextDue = nextDue;
        _onDue = onDue;

        _timerHandle = Native.CreateWaitableTimerExW(
            0, null, Native.CREATE_WAITABLE_TIMER_HIGH_RESOLUTION, Native.TIMER_ALL_ACCESS);

        if (_timerHandle == 0)
            _timerHandle = Native.CreateWaitableTimerExW(0, null, 0, Native.TIMER_ALL_ACCESS);

        if (_timerHandle == 0)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "创建高精度定时器失败");

        _thread = new Thread(Run) { IsBackground = true, Name = "DCH.ReleaseTimer" };
        _thread.Start();
    }

    public void Nudge() => _wake.Set();

    private void Run()
    {
        nint wakeHandle = _wake.SafeWaitHandle.DangerousGetHandle();
        var handles = new[] { _timerHandle, wakeHandle };

        while (!_disposed)
        {
            long due = _nextDue();
            if (due == 0)
            {
                _wake.WaitOne();
                continue;
            }

            double remaining = Clock.TicksToMs(due - Clock.Now);
            if (remaining <= 0)
            {
                _onDue();
                continue;
            }

            long relative = -(long)Math.Ceiling(remaining) * 10_000L;
            if (relative > -10_000L) relative = -10_000L;

            if (!Native.SetWaitableTimer(_timerHandle, ref relative, 0, 0, 0, false))
            {
                Thread.Sleep((int)Math.Ceiling(remaining));
                _onDue();
                continue;
            }

            uint result = Native.WaitForMultipleObjects(2, handles, false, Native.INFINITE);
            if (result == Native.WAIT_OBJECT_0)
                _onDue();
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _wake.Set();
        _thread.Join(1000);

        if (_timerHandle != 0)
        {
            Native.CloseHandle(_timerHandle);
            _timerHandle = 0;
        }

        _wake.Dispose();
    }
}
