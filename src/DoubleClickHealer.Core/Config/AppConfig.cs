namespace DoubleClickHealer.Core.Config;

public sealed record AppConfig
{
    public int Version { get; init; } = 2;

    /// <summary>软件总开关。</summary>
    public bool Enabled { get; init; } = true;

    public bool RunAtStartup { get; init; } = true;
    public bool StartMinimized { get; init; } = true;
    public bool MinimizeToTray { get; init; } = true;

    /// <summary>以管理员权限运行（由任务计划实现，需重启生效）。</summary>
    public bool ElevatedMode { get; init; }

    public List<KeyRule> Targets { get; init; } = DefaultTargets();

    public static List<KeyRule> DefaultTargets() =>
    [
        KeyRule.Mouse(MouseButtons.Left, 80),
        KeyRule.Mouse(MouseButtons.Right, 80),
    ];

    /// <summary>
    /// v1 的默认目标是左/右/中键、40ms；升级到 v2 时把纯默认配置替换为新默认，
    /// 用户自定义过的配置只升级版本号。
    /// </summary>
    public static AppConfig Migrate(AppConfig config)
    {
        if (config.Version >= 2) return config;

        bool isLegacyDefault = config.Targets.Count == 3
            && config.Targets.All(rule => rule.Kind == InputKind.Mouse && rule.HoldMs == 40)
            && config.Targets
                .Select(rule => rule.Code)
                .OrderBy(code => code)
                .SequenceEqual([MouseButtons.Left, MouseButtons.Right, MouseButtons.Middle]);

        var migrated = config with { Version = 2 };

        return isLegacyDefault
            ? migrated with { Targets = DefaultTargets(), RunAtStartup = true }
            : migrated;
    }
}
