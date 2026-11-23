using System.ComponentModel;
using System.Runtime.InteropServices;
using DoubleClickHealer.Core.Engine;
using DoubleClickHealer.Core.Interop;

namespace DoubleClickHealer.Core.Input;

public readonly record struct HookTrace(
    InputKind Kind,
    int Code,
    bool IsDown,
    InputVerdict Verdict,
    long Timestamp,
    bool Injected = false,
    bool IsTest = false);

/// <summary>
/// 专用线程上安装 WH_MOUSE_LL / WH_KEYBOARD_LL 并跑消息循环。
/// 回调里只做时间戳判定，绝不阻塞、绝不调用 SendInput。
/// </summary>
public sealed class InputHook : IDisposable
{
    private const int WmReinstall = 0x8001;
    private static readonly TimeSpan WatchdogInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan SystemIdleThreshold = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan HookSilenceThreshold = TimeSpan.FromSeconds(5);

    private readonly HealerEngine _engine;
    private readonly Native.HookProc _mouseProc;
    private readonly Native.HookProc _keyboardProc;
    private readonly ManualResetEventSlim _ready = new(false);
    private readonly ManualResetEventSlim _stopSignal = new(false);

    private Thread? _thread;
    private Thread? _watchdog;
    private uint _threadId;
    private nint _mouseHook;
    private nint _keyboardHook;
    private volatile bool _mouseHookActive;
    private volatile bool _keyboardHookActive;
    private long _lastActivityTicks;
    private volatile bool _running;
    private Exception? _startupError;
    private bool _disposed;

    public InputHook(HealerEngine engine)
    {
        _engine = engine;
        _mouseProc = MouseProc;
        _keyboardProc = KeyboardProc;
    }

    public Action<HookTrace>? OnTrace { get; set; }

    /// <summary>鼠标钩子是否安装成功。与键盘钩子独立，允许只修复其中一种输入。</summary>
    public bool MouseHookActive => _mouseHookActive;

    /// <summary>键盘钩子是否安装成功。</summary>
    public bool KeyboardHookActive => _keyboardHookActive;

    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_thread != null) return;

        _running = true;
        _ready.Reset();
        _stopSignal.Reset();
        _startupError = null;

        _thread = new Thread(ThreadMain) { IsBackground = true, Name = "DCH.InputHook" };
        _thread.Start();

        if (!_ready.Wait(TimeSpan.FromSeconds(5)))
        {
            _running = false;
            var stuck = _thread;
            _thread = null;
            if (_threadId != 0)
                Native.PostThreadMessageW(_threadId, Native.WM_QUIT, 0, 0);
            stuck?.Join(1000);
            throw new TimeoutException("启动输入钩子线程超时");
        }

        if (_startupError != null)
        {
            _running = false;
            var failed = _thread;
            _thread = null;
            failed?.Join(1000);
            throw new InvalidOperationException("安装全局钩子失败", _startupError);
        }

        _watchdog = new Thread(WatchdogMain) { IsBackground = true, Name = "DCH.HookWatchdog" };
        _watchdog.Start();
    }

    public void Stop() => StopCore();

    private bool StopCore()
    {
        var thread = _thread;
        if (thread == null) return true;

        _running = false;
        _stopSignal.Set();
        if (_threadId != 0)
            Native.PostThreadMessageW(_threadId, Native.WM_QUIT, 0, 0);

        bool threadJoined = thread.Join(5000);
        if (threadJoined)
            _thread = null;

        bool watchdogJoined = true;
        var watchdog = _watchdog;
        if (watchdog != null)
        {
            watchdogJoined = watchdog.Join(2000);
            if (watchdogJoined)
                _watchdog = null;
        }

        return threadJoined && watchdogJoined;
    }

    private void ThreadMain()
    {
        _threadId = Native.GetCurrentThreadId();
        InstallHooks();
        Volatile.Write(ref _lastActivityTicks, Clock.Now);
        _ready.Set();

        if (_mouseHook == 0 && _keyboardHook == 0)
        {
            UninstallHooks();
            return;
        }

        while (_running)
        {
            int result = Native.GetMessageW(out var msg, 0, 0, 0);
            if (result <= 0) break;

            if (msg.message == WmReinstall)
            {
                ReinstallHooks();
                continue;
            }

            Native.TranslateMessage(ref msg);
            Native.DispatchMessageW(ref msg);
        }

        UninstallHooks();
    }

    private void InstallHooks()
    {
        nint module = Native.GetModuleHandleW(null);

        _mouseHook = Native.SetWindowsHookExW(Native.WH_MOUSE_LL, _mouseProc, module, 0);
        _mouseHookActive = _mouseHook != 0;
        int mouseError = _mouseHook == 0 ? Marshal.GetLastWin32Error() : 0;

        _keyboardHook = Native.SetWindowsHookExW(Native.WH_KEYBOARD_LL, _keyboardProc, module, 0);
        _keyboardHookActive = _keyboardHook != 0;
        int keyboardError = _keyboardHook == 0 ? Marshal.GetLastWin32Error() : 0;

        // 单个钩子失败时仍可修复另一种输入，只有全部失败才视为启动错误。
        if (_mouseHook == 0 && _keyboardHook == 0)
            _startupError = new Win32Exception(mouseError != 0 ? mouseError : keyboardError);
    }

    private void ReinstallHooks()
    {
        UninstallHooks();
        _startupError = null;
        InstallHooks();
        Volatile.Write(ref _lastActivityTicks, Clock.Now);
    }

    private void UninstallHooks()
    {
        if (_mouseHook != 0)
        {
            Native.UnhookWindowsHookEx(_mouseHook);
            _mouseHook = 0;
        }

        _mouseHookActive = false;

        if (_keyboardHook != 0)
        {
            Native.UnhookWindowsHookEx(_keyboardHook);
            _keyboardHook = 0;
        }

        _keyboardHookActive = false;
    }

    /// <summary>
    /// 系统有输入但钩子长时间收不到事件，说明钩子已被系统摘除（通常是回调超时），重新安装。
    /// </summary>
    private void WatchdogMain()
    {
        while (_running)
        {
            if (_stopSignal.Wait(WatchdogInterval)) return;
            if (!_running) return;

            var info = new Native.LASTINPUTINFO
            {
                cbSize = (uint)Marshal.SizeOf<Native.LASTINPUTINFO>(),
            };

            if (!Native.GetLastInputInfo(ref info)) continue;

            uint systemIdleMs = unchecked((uint)Environment.TickCount - info.dwTime);
            if (systemIdleMs > SystemIdleThreshold.TotalMilliseconds) continue;

            long silenceMs = (long)((Clock.Now - Volatile.Read(ref _lastActivityTicks)) * 1000.0 / Clock.Frequency);
            if (silenceMs > HookSilenceThreshold.TotalMilliseconds)
                Native.PostThreadMessageW(_threadId, WmReinstall, 0, 0);
        }
    }

    private nint MouseProc(int nCode, nint wParam, nint lParam)
    {
        if (nCode < 0)
            return Native.CallNextHookEx(0, nCode, wParam, lParam);

        try
        {
            unsafe
            {
                var data = (Native.MSLLHOOKSTRUCT*)lParam;
#if DEBUG
                bool isTest = data->dwExtraInfo == InputInjector.TestMagic;
#else
                const bool isTest = false;
#endif
                // 只过滤自身注入。远程桌面、触摸板等来源也会带 INJECTED 标记，
                // 全量过滤会让这些场景下的修复功能完全失效。
                if (!isTest && data->dwExtraInfo == InputInjector.InjectionMagic)
                    return Native.CallNextHookEx(0, nCode, wParam, lParam);

                Volatile.Write(ref _lastActivityTicks, Clock.Now);

                if (!TryMapMouse((int)wParam, data, out int button, out bool isDown))
                    return Native.CallNextHookEx(0, nCode, wParam, lParam);

                var verdict = _engine.Process(InputKind.Mouse, button, isDown);
                OnTrace?.Invoke(new HookTrace(InputKind.Mouse, button, isDown, verdict, Clock.Now, IsTest: isTest));

                if (verdict == InputVerdict.EmitUpThenPass)
                {
                    // 钩子内绝不能调用 SendInput：会等待自己处理输入而死锁到超时。
                    _engine.QueueReplay(InputKind.Mouse, button);
                    return 1;
                }

                return verdict == InputVerdict.Swallow
                    ? 1
                    : Native.CallNextHookEx(0, nCode, wParam, lParam);
            }
        }
        catch (Exception)
        {
            // 低级钩子回调里抛异常会被系统摘除钩子，必须兜底放行。
            return Native.CallNextHookEx(0, nCode, wParam, lParam);
        }
    }

    private nint KeyboardProc(int nCode, nint wParam, nint lParam)
    {
        if (nCode < 0)
            return Native.CallNextHookEx(0, nCode, wParam, lParam);

        try
        {
            unsafe
            {
                var data = (Native.KBDLLHOOKSTRUCT*)lParam;
#if DEBUG
                bool isTest = data->dwExtraInfo == InputInjector.TestMagic;
#else
                const bool isTest = false;
#endif
                if (!isTest && data->dwExtraInfo == InputInjector.InjectionMagic)
                    return Native.CallNextHookEx(0, nCode, wParam, lParam);

                Volatile.Write(ref _lastActivityTicks, Clock.Now);

                int message = (int)wParam;
                bool isDown = message is Native.WM_KEYDOWN or Native.WM_SYSKEYDOWN;
                bool isUp = message is Native.WM_KEYUP or Native.WM_SYSKEYUP;
                if (!isDown && !isUp)
                    return Native.CallNextHookEx(0, nCode, wParam, lParam);

                int virtualKey = VirtualKeys.Normalize(
                    (int)data->vkCode,
                    (int)data->scanCode,
                    (data->flags & Native.LLKHF_EXTENDED) != 0);

                var verdict = _engine.Process(InputKind.Keyboard, virtualKey, isDown);
                OnTrace?.Invoke(new HookTrace(InputKind.Keyboard, virtualKey, isDown, verdict, Clock.Now, IsTest: isTest));

                if (verdict == InputVerdict.EmitUpThenPass)
                {
                    _engine.QueueReplay(InputKind.Keyboard, virtualKey);
                    return 1;
                }

                return verdict == InputVerdict.Swallow
                    ? 1
                    : Native.CallNextHookEx(0, nCode, wParam, lParam);
            }
        }
        catch (Exception)
        {
            return Native.CallNextHookEx(0, nCode, wParam, lParam);
        }
    }

    private static unsafe bool TryMapMouse(int message, Native.MSLLHOOKSTRUCT* data, out int button, out bool isDown)
    {
        isDown = message is Native.WM_LBUTTONDOWN or Native.WM_RBUTTONDOWN
            or Native.WM_MBUTTONDOWN or Native.WM_XBUTTONDOWN;

        switch (message)
        {
            case Native.WM_LBUTTONDOWN:
            case Native.WM_LBUTTONUP:
                button = MouseButtons.Left;
                return true;

            case Native.WM_RBUTTONDOWN:
            case Native.WM_RBUTTONUP:
                button = MouseButtons.Right;
                return true;

            case Native.WM_MBUTTONDOWN:
            case Native.WM_MBUTTONUP:
                button = MouseButtons.Middle;
                return true;

            case Native.WM_XBUTTONDOWN:
            case Native.WM_XBUTTONUP:
                button = ((data->mouseData >> 16) & 0xFFFF) == 1 ? MouseButtons.X1 : MouseButtons.X2;
                return true;

            default:
                button = 0;
                isDown = false;
                return false;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        // 只有线程确实退出后才释放同步对象，避免后台线程访问已释放的句柄。
        if (StopCore())
        {
            _ready.Dispose();
            _stopSignal.Dispose();
        }
    }
}
