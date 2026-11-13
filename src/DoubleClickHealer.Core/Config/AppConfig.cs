namespace DoubleClickHealer.Core.Config;

public sealed record AppConfig
{
    public int Version { get; init; } = 1;

    /// <summary>软件总开关。</summary>
    public bool Enabled { get; init; } = true;

    public bool RunAtStartup { get; init; }
    public bool StartMinimized { get; init; } = true;
    public bool MinimizeToTray { get; init; } = true;

    /// <summary>以管理员权限运行（由任务计划实现，需重启生效）。</summary>
    public bool ElevatedMode { get; init; }

    public List<KeyRule> Targets { get; init; } = DefaultTargets();

    public static List<KeyRule> DefaultTargets() =>
    [
        KeyRule.Mouse(MouseButtons.Left, 40),
        KeyRule.Mouse(MouseButtons.Right, 40),
        KeyRule.Mouse(MouseButtons.Middle, 40),
    ];
}
