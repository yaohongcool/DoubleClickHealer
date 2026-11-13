using System.Diagnostics;

namespace DoubleClickHealer.Core;

internal static class Clock
{
    public static long Now => Stopwatch.GetTimestamp();
    public static long Frequency => Stopwatch.Frequency;

    public static long MsToTicks(double ms) => (long)Math.Round(ms * Stopwatch.Frequency / 1000.0);

    public static double TicksToMs(long ticks) => ticks * 1000.0 / Stopwatch.Frequency;
}
