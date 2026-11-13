using System.Text;
using DoubleClickHealer.Core.Interop;

namespace DoubleClickHealer.Core;

public static class KeyNames
{
    public static string Get(InputKind kind, int code) =>
        kind == InputKind.Mouse ? Mouse(code) : Keyboard(code);

    public static string Mouse(int code) => code switch
    {
        MouseButtons.Left => "鼠标左键",
        MouseButtons.Right => "鼠标右键",
        MouseButtons.Middle => "鼠标中键",
        MouseButtons.X1 => "鼠标侧键 1",
        MouseButtons.X2 => "鼠标侧键 2",
        _ => $"鼠标 {code}",
    };

    public static string Keyboard(int virtualKey) => virtualKey switch
    {
        VirtualKeys.LShift => "左 Shift",
        VirtualKeys.RShift => "右 Shift",
        VirtualKeys.LControl => "左 Ctrl",
        VirtualKeys.RControl => "右 Ctrl",
        VirtualKeys.LMenu => "左 Alt",
        VirtualKeys.RMenu => "右 Alt",
        0x20 => "空格",
        0x0D => "回车",
        0x1B => "Esc",
        0x09 => "Tab",
        0x08 => "退格",
        0x14 => "CapsLock",
        0x25 => "←",
        0x26 => "↑",
        0x27 => "→",
        0x28 => "↓",
        >= 0x30 and <= 0x39 => ((char)virtualKey).ToString(),
        >= 0x41 and <= 0x5A => ((char)virtualKey).ToString(),
        >= 0x60 and <= 0x69 => $"小键盘 {virtualKey - 0x60}",
        >= 0x70 and <= 0x87 => $"F{virtualKey - 0x6F}",
        _ => SystemName(virtualKey),
    };

    private static string SystemName(int virtualKey)
    {
        uint scanCode = Native.MapVirtualKeyW((uint)virtualKey, 0);
        if (scanCode != 0)
        {
            var buffer = new StringBuilder(64);
            int length = Native.GetKeyNameTextW((int)(scanCode << 16), buffer, buffer.Capacity);
            if (length > 0) return buffer.ToString(0, length);
        }

        return $"VK_{virtualKey:X2}";
    }
}
