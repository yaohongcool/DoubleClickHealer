using System.Diagnostics;
using DoubleClickHealer.Core;
using DoubleClickHealer.Core.Input;

long origin = Stopwatch.GetTimestamp();
string sequence = args.Length > 0 ? args[0] : "chatter";
int delay = int.TryParse(Environment.GetEnvironmentVariable("DCH_DELAY"), out int d) ? d : 2500;

double Ms(long t) => (t - origin) * 1000.0 / Stopwatch.Frequency;
double Delta(long ticks) => ticks * 1000.0 / Stopwatch.Frequency;

Console.WriteLine($"模拟序列: {sequence}，等待 {delay}ms 让 PoC 就绪…");
Thread.Sleep(delay);

switch (sequence)
{
    case "clean":
        Click(60);
        break;

    case "chatter":
        Click(5);
        Thread.Sleep(3);
        Click(292);
        break;

    case "double":
        Click(60);
        Thread.Sleep(90);
        Click(60);
        break;

    case "fastdouble":
        Click(60);
        PreciseSleep(25);
        Click(60);
        break;

    case "keyboard":
        InputInjector.SendTestKey(0x41, up: false);
        Thread.Sleep(5);
        InputInjector.SendTestKey(0x41, up: true);
        Thread.Sleep(3);
        InputInjector.SendTestKey(0x41, up: false);
        Thread.Sleep(292);
        InputInjector.SendTestKey(0x41, up: true);
        break;

    default:
        Console.WriteLine($"未知序列: {sequence}");
        return;
}

Thread.Sleep(300);
Console.WriteLine("模拟完成。");
return;

void Click(int holdMs)
{
    long a = Stopwatch.GetTimestamp();
    InputInjector.SendTestMouse(MouseButtons.Left, up: false);
    long b = Stopwatch.GetTimestamp();
    Thread.Sleep(holdMs);
    long c = Stopwatch.GetTimestamp();
    InputInjector.SendTestMouse(MouseButtons.Left, up: true);
    long e = Stopwatch.GetTimestamp();

    Console.WriteLine(
        $"  down@{Ms(a):F1} send={Delta(b - a):F1}ms | sleep={Delta(c - b):F1}ms | up@{Ms(c):F1} send={Delta(e - c):F1}ms");
}

void PreciseSleep(int ms)
{
    long target = Stopwatch.GetTimestamp() + ms * Stopwatch.Frequency / 1000;
    while (Stopwatch.GetTimestamp() < target)
        Thread.SpinWait(64);
}
