namespace DoubleClickHealer.Core.Config;

public sealed record AppConfig
{
    public int Version { get; init; } = 3;

    /// <summary>软件总开关。</summary>
    public bool Enabled { get; init; } = true;

    public bool RunAtStartup { get; init; } = true;
    public bool StartMinimized { get; init; } = true;
    public bool MinimizeToTray { get; init; } = true;

    /// <summary>以管理员权限运行（由任务计划实现，需重启生效）。</summary>
    public bool ElevatedMode { get; init; }

    /// <summary>界面主题：跟随系统 / 浅色 / 深色。</summary>
    public AppThemeMode Theme { get; init; } = AppThemeMode.System;

    /// <summary>界面语言：简体中文 / English。</summary>
    public AppLanguage Language { get; init; } = AppLanguage.Chinese;

    public List<KeyRule> Targets { get; init; } = DefaultTargets();

    public static List<KeyRule> DefaultTargets() =>
    [
        KeyRule.Mouse(MouseButtons.Left, 60),
        KeyRule.Mouse(MouseButtons.Right, 60),
    ];

    /// <summary>
    /// 逐版本迁移纯默认配置；用户自定义过的配置只升级版本号，不覆盖。
    /// v1: 左/右/中键 40ms
    /// v2: 左/右键 80ms
    /// v3: 左/右键 60ms
    /// </summary>
    public static AppConfig Migrate(AppConfig config)
    {
        // JSON 里的 "targets": null 或 [null] 会在运行时产生 null，必须先归一化，
        // 否则后续 Count/Where 会抛 NullReferenceException。
        List<KeyRule> targets = config.Targets is null
            ? DefaultTargets()
            : config.Targets
                .Where(rule => rule is not null)
                .Select(NormalizeRule)
                .ToList();

        config = config with { Targets = targets };

        if (config.Version >= 3) return config;

        var migrated = config;

        if (migrated.Version < 2)
        {
            bool isV1Default = migrated.Targets.Count == 3
                && migrated.Targets.All(rule => rule.Kind == InputKind.Mouse && rule.HoldMs == 40)
                && migrated.Targets
                    .Select(rule => rule.Code)
                    .OrderBy(code => code)
                    .SequenceEqual([MouseButtons.Left, MouseButtons.Right, MouseButtons.Middle]);

            migrated = migrated with { Version = 2 };
            if (isV1Default)
                migrated = migrated with { Targets = DefaultTargets(), RunAtStartup = true };
        }

        if (migrated.Version < 3)
        {
            bool isV2Default = migrated.Targets.Count == 2
                && migrated.Targets.All(rule => rule.Kind == InputKind.Mouse && rule.HoldMs == 80)
                && migrated.Targets
                    .Select(rule => rule.Code)
                    .OrderBy(code => code)
                    .SequenceEqual([MouseButtons.Left, MouseButtons.Right]);

            migrated = migrated with { Version = 3 };
            if (isV2Default)
                migrated = migrated with { Targets = DefaultTargets() };
        }

        return migrated;
    }

    private static KeyRule NormalizeRule(KeyRule rule) => rule with
    {
        HoldMs = Math.Clamp(rule.HoldMs, 0, 120),
        ChatterGapMs = Math.Clamp(rule.ChatterGapMs, 0, 500),
        ChatterPressMs = Math.Clamp(rule.ChatterPressMs, 0, 500),
        ChatterGapLongMs = Math.Clamp(rule.ChatterGapLongMs, 0, 1000),
    };
}
