using System.Diagnostics;
using System.Text;
using System.Threading.Channels;
using DoubleClickHealer.Core;
using DoubleClickHealer.Core.Config;
using DoubleClickHealer.Core.Engine;
using DoubleClickHealer.Core.Input;

Console.OutputEncoding = Encoding.UTF8;

long origin = Stopwatch.GetTimestamp();

int? holdOverride = null;
string? configPath = null;

for (int i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--hold" when i + 1 < args.Length && int.TryParse(args[i + 1], out int parsed):
            holdOverride = Math.Clamp(parsed, 0, 200);
            i++;
            break;

        case "--config" when i + 1 < args.Length:
            configPath = args[i + 1];
            i++;
            break;
    }
}

using var store = new ConfigStore(configPath);
store.EnsureFile();

var channel = Channel.CreateUnbounded<HookTrace>(new UnboundedChannelOptions { SingleReader = true });
var printer = Task.Run(async () =>
{
    await foreach (var trace in channel.Reader.ReadAllAsync())
    {
        string target = KeyNames.Get(trace.Kind, trace.Code);

        string verdict = trace.Injected
            ? "→ INJECT"
            : trace.Verdict switch
            {
                InputVerdict.PassThrough => "pass",
                InputVerdict.Swallow => "SWALLOW",
                InputVerdict.EmitUpThenPass => "up+pass",
                _ => "?",
            };

        string tag = trace.IsTest ? "[T]" : "   ";
        double ms = (trace.Timestamp - origin) * 1000.0 / Stopwatch.Frequency;
        Console.WriteLine($"{ms,9:F1}ms {tag} {target,-12} {(trace.IsDown ? "down" : "up  ")}  {verdict}");
    }
});

using var engine = new HealerEngine(SelectRules(store.Current));
engine.OnInjected = (kind, code, down) =>
    channel.Writer.TryWrite(new HookTrace(kind, code, down, InputVerdict.Swallow, Stopwatch.GetTimestamp(), true));
engine.Start();

using var hook = new InputHook(engine) { OnTrace = t => channel.Writer.TryWrite(t) };
hook.Start();

store.Changed += config =>
{
    engine.UpdateRules(SelectRules(config));
    PrintRules(config);
};
store.StartWatching();

PrintRules(store.Current);

using var exit = new ManualResetEventSlim(false);
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    exit.Set();
};
exit.Wait();

hook.Stop();
channel.Writer.TryComplete();
await printer;

Console.WriteLine("已停止。");
return;

List<KeyRule> SelectRules(AppConfig config)
{
    if (!config.Enabled) return [];

    return holdOverride is int hold
        ? config.Targets
            .Where(r => r.Enabled)
            .Select(r => r.Kind == InputKind.Mouse ? r with { HoldMs = hold } : r)
            .ToList()
        : config.Targets.Where(r => r.Enabled).ToList();
}

void PrintRules(AppConfig config)
{
    Console.WriteLine();
    Console.WriteLine($"总开关: {(config.Enabled ? "开" : "关")}    配置文件: {store.Path}");

    if (holdOverride is int hold)
        Console.WriteLine($"命令行覆盖: 鼠标扣留 {hold}ms");

    foreach (var rule in config.Targets)
    {
        string mode = rule.HoldMs > 0 ? $"深度（扣留 {rule.HoldMs}ms）" : "轻量";
        Console.WriteLine($"  {(rule.Enabled ? "●" : "○")} {KeyNames.Get(rule.Kind, rule.Code),-12} {mode}");
    }

    Console.WriteLine();
}
