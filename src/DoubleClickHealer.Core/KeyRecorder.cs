using System.ComponentModel;
using System.Runtime.InteropServices;
using DoubleClickHealer.Core.Interop;

namespace DoubleClickHealer.Core;

/// <summary>
/// 临时钩子，用于“按下要修复的按键”录制。捕获到第一个按键后自动停止。
/// </summary>
public sealed class KeyRecorder : IDisposable
{
    private const int VkEscape = 0x1B;

    private readonly ManualResetEventSlim _ready = new(false);

    private Thread? _thread;
    private uint _threadId;
    private nint _mouseHook;
    private nint _keyboardHook;
    private Native.HookProc? _mouseProc;
    private Native.HookProc? _keyboardProc;
    private Action<InputKind, int>? _onCaptured;
    private Action? _onCanceled;
    private volatile bool _running;
    private bool _disposed;

    public bool IsRecording => _thread != null;

    /// <summary>开始录制。回调在钩子线程触发，调用方需自行切回 UI 线程。</summary>
    public void Start(Action<InputKind, int> onCaptured, Action? onCanceled = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_thread != null) return;

        _onCaptured = onCaptured;
        _onCanceled = onCanceled;
        _running = true;
        _ready.Reset();

        _thread = new Thread(ThreadMain) { IsBackground = true, Name = "DCH.KeyRecorder" };
        _thread.Start();
        _ready.Wait();
    }

    public void Stop()
    {
        var thread = _thread;
        if (thread == null) return;

        _running = false;
        Native.PostThreadMessageW(_threadId, Native.WM_QUIT, 0, 0);
        thread.Join(1000);
        _thread = null;
        _onCaptured = null;
        _onCanceled = null;
    }

    private void ThreadMain()
    {
        _threadId = Native.GetCurrentThreadId();
        _mouseProc = MouseProc;
        _keyboardProc = KeyboardProc;

        nint module = Native.GetModuleHandleW(null);
        _mouseHook = Native.SetWindowsHookExW(Native.WH_MOUSE_LL, _mouseProc, module, 0);
        _keyboardHook = Native.SetWindowsHookExW(Native.WH_KEYBOARD_LL, _keyboardProc, module, 0);

        _ready.Set();

        if (_mouseHook == 0 && _keyboardHook == 0)
            return;

        while (_running && Native.GetMessageW(out var msg, 0, 0, 0) > 0)
        {
            Native.TranslateMessage(ref msg);
            Native.DispatchMessageW(ref msg);
        }

        UninstallHooks();
    }

    private void UninstallHooks()
    {
        if (_mouseHook != 0)
        {
            Native.UnhookWindowsHookEx(_mouseHook);
            _mouseHook = 0;
        }

        if (_keyboardHook != 0)
        {
            Native.UnhookWindowsHookEx(_keyboardHook);
            _keyboardHook = 0;
        }
    }

    private nint MouseProc(int nCode, nint wParam, nint lParam)
    {
        if (nCode < 0)
            return Native.CallNextHookEx(0, nCode, wParam, lParam);

        unsafe
        {
            var data = (Native.MSLLHOOKSTRUCT*)lParam;
            if ((data->flags & Native.LLMHF_INJECTED) != 0)
                return Native.CallNextHookEx(0, nCode, wParam, lParam);

            if (!TryMapMouse((int)wParam, data, out int button))
                return Native.CallNextHookEx(0, nCode, wParam, lParam);

            Capture(InputKind.Mouse, button);
            return 1;
        }
    }

    private nint KeyboardProc(int nCode, nint wParam, nint lParam)
    {
        if (nCode < 0)
            return Native.CallNextHookEx(0, nCode, wParam, lParam);

        unsafe
        {
            var data = (Native.KBDLLHOOKSTRUCT*)lParam;
            if ((data->flags & Native.LLKHF_INJECTED) != 0)
                return Native.CallNextHookEx(0, nCode, wParam, lParam);

            int message = (int)wParam;
            if (message is not (Native.WM_KEYDOWN or Native.WM_SYSKEYDOWN))
                return Native.CallNextHookEx(0, nCode, wParam, lParam);

            int virtualKey = VirtualKeys.Normalize(
                (int)data->vkCode,
                (int)data->scanCode,
                (data->flags & Native.LLKHF_EXTENDED) != 0);

            if (virtualKey == VkEscape)
            {
                Cancel();
                return 1;
            }

            Capture(InputKind.Keyboard, virtualKey);
            return 1;
        }
    }

    private void Capture(InputKind kind, int code)
    {
        var callback = _onCaptured;
        _onCaptured = null;
        _onCanceled = null;
        _running = false;
        Native.PostThreadMessageW(_threadId, Native.WM_QUIT, 0, 0);
        callback?.Invoke(kind, code);
    }

    private void Cancel()
    {
        var callback = _onCanceled;
        _onCaptured = null;
        _onCanceled = null;
        _running = false;
        Native.PostThreadMessageW(_threadId, Native.WM_QUIT, 0, 0);
        callback?.Invoke();
    }

    private static unsafe bool TryMapMouse(int message, Native.MSLLHOOKSTRUCT* data, out int button)
    {
        switch (message)
        {
            case Native.WM_LBUTTONDOWN:
                button = MouseButtons.Left;
                return true;

            case Native.WM_RBUTTONDOWN:
                button = MouseButtons.Right;
                return true;

            case Native.WM_MBUTTONDOWN:
                button = MouseButtons.Middle;
                return true;

            case Native.WM_XBUTTONDOWN:
                button = ((data->mouseData >> 16) & 0xFFFF) == 1 ? MouseButtons.X1 : MouseButtons.X2;
                return true;

            default:
                button = 0;
                return false;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
        _ready.Dispose();
    }
}
