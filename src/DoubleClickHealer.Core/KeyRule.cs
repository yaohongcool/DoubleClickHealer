namespace DoubleClickHealer.Core;

public enum InputKind
{
    Mouse = 0,
    Keyboard = 1,
}

public static class MouseButtons
{
    public const int Left = 1;
    public const int Right = 2;
    public const int Middle = 4;
    public const int X1 = 8;
    public const int X2 = 16;
}

public sealed record KeyRule
{
    public InputKind Kind { get; init; } = InputKind.Mouse;
    public int Code { get; init; }
    public bool Enabled { get; init; } = true;

    /// <summary>扣留 UP 的时长，0 表示轻量模式（不扣留，仅丢弃重复 DOWN）。</summary>
    public int HoldMs { get; init; } = 80;

    /// <summary>两个事件间隔小于此值直接判为抖动。</summary>
    public int ChatterGapMs { get; init; } = 20;

    /// <summary>短于此值的按压视为碎片按。</summary>
    public int ChatterPressMs { get; init; } = 25;

    /// <summary>上次是碎片按时，放宽到该间隔内仍判为抖动。</summary>
    public int ChatterGapLongMs { get; init; } = 60;

    public static KeyRule Mouse(int button, int holdMs = 80) => new()
    {
        Kind = InputKind.Mouse,
        Code = button,
        HoldMs = holdMs,
    };

    public static KeyRule Keyboard(int virtualKey, int holdMs = 80) => new()
    {
        Kind = InputKind.Keyboard,
        Code = virtualKey,
        HoldMs = holdMs,
    };
}
