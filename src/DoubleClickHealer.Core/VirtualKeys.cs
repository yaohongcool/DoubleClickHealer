namespace DoubleClickHealer.Core;

public static class VirtualKeys
{
    public const int Shift = 0x10;
    public const int Control = 0x11;
    public const int Menu = 0x12;

    public const int LShift = 0xA0;
    public const int RShift = 0xA1;
    public const int LControl = 0xA2;
    public const int RControl = 0xA3;
    public const int LMenu = 0xA4;
    public const int RMenu = 0xA5;

    /// <summary>
    /// 低级钩子会把左右修饰键合并成 VK_SHIFT / VK_CONTROL / VK_MENU，
    /// 这里按扫描码和扩展位还原成独立的左/右键值。
    /// </summary>
    public static int Normalize(int virtualKey, int scanCode, bool extended) => virtualKey switch
    {
        Shift => scanCode == 0x36 ? RShift : LShift,
        Control => extended ? RControl : LControl,
        Menu => extended ? RMenu : LMenu,
        _ => virtualKey,
    };

    /// <summary>注入时把左/右修饰键还原成系统可接受的 VK_SHIFT / VK_CONTROL / VK_MENU。</summary>
    public static (int VirtualKey, bool Extended) Denormalize(int virtualKey) => virtualKey switch
    {
        LShift => (Shift, false),
        RShift => (Shift, false),
        LControl => (Control, false),
        RControl => (Control, true),
        LMenu => (Menu, false),
        RMenu => (Menu, true),
        _ => (virtualKey, false),
    };
}
