using System.Collections.Concurrent;
using System.ComponentModel;
using System.Runtime.InteropServices;
using DoubleClickHealer.Core.Interop;

namespace DoubleClickHealer.Core.Input;

/// <summary>
/// 单线程串行注入器。钩子回调和定时器只入队，绝不在回调里调用 SendInput。
/// </summary>
public sealed class InputInjector : IDisposable
{
    public const nuint InjectionMagic = 0x44434831; // "DCH1"

    /// <summary>测试签名：携带它的注入事件会被钩子当作真实输入处理，用于端到端验证。</summary>
    public const nuint TestMagic = 0x44434854; // "DCHT"

    private enum WorkKind
    {
        Up,
        Replay,
    }

    private readonly record struct Work(WorkKind Kind, InputKind Target, int Code);

    private readonly BlockingCollection<Work> _queue = new();
    private readonly Thread _worker;
    private volatile bool _disposed;

    public InputInjector()
    {
        _worker = new Thread(Run) { IsBackground = true, Name = "DCH.Injector" };
        _worker.Start();
    }

    /// <summary>实际发出事件后触发（kind, code, isDown）。</summary>
    public Action<InputKind, int, bool>? OnInjected { get; set; }

    public void EnqueueUp(InputKind kind, int code) => TryEnqueue(new Work(WorkKind.Up, kind, code));

    /// <summary>补发「UP + DOWN」一对事件，用于真实快速双击。</summary>
    public void EnqueueReplay(InputKind kind, int code) => TryEnqueue(new Work(WorkKind.Replay, kind, code));

    public static void SendTestMouse(int button, bool up) => SendMouse(button, up, TestMagic);

    public static void SendTestKey(int virtualKey, bool up) => SendKeyboard(virtualKey, up, TestMagic);

    private void TryEnqueue(Work work)
    {
        if (_disposed) return;

        try
        {
            _queue.Add(work);
        }
        catch (InvalidOperationException)
        {
            // 已完成添加，进程正在退出。
        }
    }

    private void Run()
    {
        foreach (var work in _queue.GetConsumingEnumerable())
        {
            try
            {
                if (work.Kind == WorkKind.Up)
                {
                    Send(work.Target, work.Code, up: true);
                    OnInjected?.Invoke(work.Target, work.Code, false);
                }
                else
                {
                    SendPair(work.Target, work.Code);
                    OnInjected?.Invoke(work.Target, work.Code, false);
                    OnInjected?.Invoke(work.Target, work.Code, true);
                }
            }
            catch (Win32Exception)
            {
                // 注入被系统拒绝（例如安全桌面）时跳过，不能让线程退出。
            }
        }
    }

    private static void Send(InputKind kind, int code, bool up)
    {
        var input = kind == InputKind.Mouse
            ? MakeMouse(code, up, InjectionMagic)
            : MakeKeyboard(code, up, InjectionMagic);

        Send(inputs: [input]);
    }

    private static void SendPair(InputKind kind, int code)
    {
        var inputs = kind == InputKind.Mouse
            ? new[] { MakeMouse(code, true, InjectionMagic), MakeMouse(code, false, InjectionMagic) }
            : new[] { MakeKeyboard(code, true, InjectionMagic), MakeKeyboard(code, false, InjectionMagic) };

        Send(inputs);
    }

    private static void SendMouse(int button, bool up, nuint magic) => Send([MakeMouse(button, up, magic)]);

    private static void SendKeyboard(int virtualKey, bool up, nuint magic) =>
        Send([MakeKeyboard(virtualKey, up, magic)]);

    private static void Send(Native.INPUT[] inputs)
    {
        uint sent = Native.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Native.INPUT>());
        if (sent == 0)
            throw new Win32Exception(Marshal.GetLastWin32Error());
    }

    private static Native.INPUT MakeMouse(int button, bool up, nuint magic)
    {
        uint flag = button switch
        {
            MouseButtons.Left => up ? Native.MOUSEEVENTF_LEFTUP : Native.MOUSEEVENTF_LEFTDOWN,
            MouseButtons.Right => up ? Native.MOUSEEVENTF_RIGHTUP : Native.MOUSEEVENTF_RIGHTDOWN,
            MouseButtons.Middle => up ? Native.MOUSEEVENTF_MIDDLEUP : Native.MOUSEEVENTF_MIDDLEDOWN,
            MouseButtons.X1 => up ? Native.MOUSEEVENTF_XUP : Native.MOUSEEVENTF_XDOWN,
            MouseButtons.X2 => up ? Native.MOUSEEVENTF_XUP : Native.MOUSEEVENTF_XDOWN,
            _ => throw new ArgumentOutOfRangeException(nameof(button)),
        };

        uint data = button switch
        {
            MouseButtons.X1 => 1u << 16,
            MouseButtons.X2 => 2u << 16,
            _ => 0u,
        };

        return new Native.INPUT
        {
            type = Native.INPUT_MOUSE,
            u = new Native.INPUTUNION
            {
                mi = new Native.MOUSEINPUT
                {
                    dwFlags = flag,
                    mouseData = data,
                    dwExtraInfo = magic,
                },
            },
        };
    }

    private static Native.INPUT MakeKeyboard(int virtualKey, bool up, nuint magic)
    {
        var (key, extended) = VirtualKeys.Denormalize(virtualKey);
        uint flags = up ? Native.KEYEVENTF_KEYUP : 0;
        if (extended) flags |= Native.KEYEVENTF_EXTENDEDKEY;

        return new Native.INPUT
        {
            type = Native.INPUT_KEYBOARD,
            u = new Native.INPUTUNION
            {
                ki = new Native.KEYBDINPUT
                {
                    wVk = (ushort)key,
                    dwFlags = flags,
                    dwExtraInfo = magic,
                },
            },
        };
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _queue.CompleteAdding();
        _worker.Join(1000);
        _queue.Dispose();
    }
}
