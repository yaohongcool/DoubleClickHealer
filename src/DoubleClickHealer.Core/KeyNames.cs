using System.Text;
using DoubleClickHealer.Core.Interop;

namespace DoubleClickHealer.Core;

public static class KeyNames
{
    /// <summary>
    /// 这些键与小键盘共用扫描码，必须带扩展位才能被 GetKeyNameTextW 正确识别。
    /// 部分系统上 MAPVK_VK_TO_VSC_EX 不会返回 0xE0 前缀，因此显式列出。
    /// </summary>
    private static readonly HashSet<int> ExtendedKeys =
    [
        0x21, 0x22, 0x23, 0x24, 0x25, 0x26, 0x27, 0x28,
        0x2C, 0x2D, 0x2E, 0x5B, 0x5C, 0x5D, 0x6F, 0x90,
        0xAD, 0xAE, 0xAF, 0xB0, 0xB1, 0xB2, 0xB3,
    ];

    /// <summary>
    /// 固定英文名称。系统 API 在非英文语言下会返回本地化名称，这些键统一走英文表。
    /// </summary>
    private static readonly Dictionary<int, string> EnglishNames = new()
    {
        [0x08] = "Backspace",
        [0x09] = "Tab",
        [0x0C] = "Clear",
        [0x0D] = "Enter",
        [0x13] = "Pause",
        [0x14] = "Caps Lock",
        [0x1B] = "Esc",
        [0x20] = "Space",
        [0x21] = "Page Up",
        [0x22] = "Page Down",
        [0x23] = "End",
        [0x24] = "Home",
        [0x25] = "Left",
        [0x26] = "Up",
        [0x27] = "Right",
        [0x28] = "Down",
        [0x2C] = "Print Screen",
        [0x2D] = "Insert",
        [0x2E] = "Delete",
        [0x5B] = "Left Win",
        [0x5C] = "Right Win",
        [0x5D] = "Menu",
        [0x60] = "Numpad 0",
        [0x61] = "Numpad 1",
        [0x62] = "Numpad 2",
        [0x63] = "Numpad 3",
        [0x64] = "Numpad 4",
        [0x65] = "Numpad 5",
        [0x66] = "Numpad 6",
        [0x67] = "Numpad 7",
        [0x68] = "Numpad 8",
        [0x69] = "Numpad 9",
        [0x6A] = "Numpad *",
        [0x6B] = "Numpad +",
        [0x6D] = "Numpad -",
        [0x6E] = "Numpad .",
        [0x6F] = "Numpad /",
        [0x90] = "Num Lock",
        [0x91] = "Scroll Lock",
        [0xA0] = "Left Shift",
        [0xA1] = "Right Shift",
        [0xA2] = "Left Ctrl",
        [0xA3] = "Right Ctrl",
        [0xA4] = "Left Alt",
        [0xA5] = "Right Alt",
        [0xAD] = "Mute",
        [0xAE] = "Volume Down",
        [0xAF] = "Volume Up",
        [0xB0] = "Next Track",
        [0xB1] = "Prev Track",
        [0xB2] = "Stop",
        [0xB3] = "Play/Pause",
        [0xBA] = ";",
        [0xBB] = "=",
        [0xBC] = ",",
        [0xBD] = "-",
        [0xBE] = ".",
        [0xBF] = "/",
        [0xC0] = "`",
        [0xDB] = "[",
        [0xDC] = "\\",
        [0xDD] = "]",
        [0xDE] = "'",
    };

    public static string Get(InputKind kind, int code) =>
        kind == InputKind.Mouse ? Mouse(code) : Keyboard(code);

    public static string Mouse(int code) => code switch
    {
        MouseButtons.Left => "Mouse Left",
        MouseButtons.Right => "Mouse Right",
        MouseButtons.Middle => "Mouse Middle",
        MouseButtons.X1 => "Mouse Side 1",
        MouseButtons.X2 => "Mouse Side 2",
        _ => $"Mouse {code}",
    };

    public static string Keyboard(int virtualKey)
    {
        if (EnglishNames.TryGetValue(virtualKey, out string? name)) return name;

        string apiName = SystemName(virtualKey);
        return IsAscii(apiName) ? apiName : $"VK_{virtualKey:X2}";
    }

    private static string SystemName(int virtualKey)
    {
        // MAPVK_VK_TO_VSC_EX：高字节为 0xE0 表示扩展键（方向键、小键盘导航键等）。
        uint scan = Native.MapVirtualKeyW((uint)virtualKey, 4);
        if (scan == 0) return $"VK_{virtualKey:X2}";

        bool extended = (scan >> 8) == 0xE0 || ExtendedKeys.Contains(virtualKey);
        int scanCode = (int)(scan & 0xFF);
        int lParam = (scanCode << 16) | (extended ? 1 << 24 : 0);

        var buffer = new StringBuilder(64);
        int length = Native.GetKeyNameTextW(lParam, buffer, buffer.Capacity);
        return length > 0 ? buffer.ToString(0, length) : $"VK_{virtualKey:X2}";
    }

    private static bool IsAscii(string value)
    {
        foreach (char c in value)
        {
            if (c > 127) return false;
        }

        return true;
    }
}
