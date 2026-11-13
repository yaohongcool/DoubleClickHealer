using System.Diagnostics;
using DoubleClickHealer.Core;
using DoubleClickHealer.Core.Engine;
using Xunit;

namespace DoubleClickHealer.Core.Tests;

public class DebounceStateTests
{
    private static long T(double ms) => (long)Math.Round(ms * Stopwatch.Frequency / 1000.0);

    private static DebounceState Mouse(int holdMs = 40) =>
        new(KeyRule.Mouse(MouseButtons.Left, holdMs));

    private static DebounceState Keyboard(int holdMs = 0) =>
        new(KeyRule.Keyboard(0x41, holdMs));

    [Fact]
    public void CleanClick_ReleasesUpAfterHold()
    {
        var state = Mouse(40);

        Assert.Equal(InputVerdict.PassThrough, state.OnDown(T(0)).Verdict);

        var up = state.OnUp(T(60));
        Assert.Equal(InputVerdict.Swallow, up.Verdict);
        Assert.True(state.HasPendingRelease);
        Assert.False(state.TryTakeRelease(T(99)));
        Assert.True(state.TryTakeRelease(T(100)));
        Assert.False(state.HasPendingRelease);
    }

    [Fact]
    public void Chatter_IsMergedIntoSinglePress()
    {
        var state = Mouse(40);

        Assert.Equal(InputVerdict.PassThrough, state.OnDown(T(0)).Verdict);
        Assert.Equal(InputVerdict.Swallow, state.OnUp(T(5)).Verdict);
        Assert.Equal(InputVerdict.Swallow, state.OnDown(T(8)).Verdict);
        Assert.False(state.HasPendingRelease);

        var up = state.OnUp(T(300));
        Assert.Equal(InputVerdict.Swallow, up.Verdict);
        Assert.True(state.TryTakeRelease(T(340)));
    }

    [Fact]
    public void RealDoubleClick_IsPreserved()
    {
        var state = Mouse(40);

        Assert.Equal(InputVerdict.PassThrough, state.OnDown(T(0)).Verdict);
        Assert.Equal(InputVerdict.Swallow, state.OnUp(T(60)).Verdict);
        Assert.True(state.TryTakeRelease(T(100)));

        Assert.Equal(InputVerdict.PassThrough, state.OnDown(T(150)).Verdict);
        Assert.Equal(InputVerdict.Swallow, state.OnUp(T(210)).Verdict);
        Assert.True(state.TryTakeRelease(T(250)));
    }

    [Fact]
    public void FastDoubleClick_EmitsPendingUpThenPasses()
    {
        var state = Mouse(40);

        Assert.Equal(InputVerdict.PassThrough, state.OnDown(T(0)).Verdict);
        Assert.Equal(InputVerdict.Swallow, state.OnUp(T(60)).Verdict);
        Assert.Equal(InputVerdict.EmitUpThenPass, state.OnDown(T(90)).Verdict);
        Assert.False(state.HasPendingRelease);
    }

    [Fact]
    public void DragGlitch_DoesNotInterruptHold()
    {
        var state = Mouse(40);

        Assert.Equal(InputVerdict.PassThrough, state.OnDown(T(0)).Verdict);
        Assert.Equal(InputVerdict.Swallow, state.OnUp(T(500)).Verdict);
        Assert.Equal(InputVerdict.Swallow, state.OnDown(T(503)).Verdict);
        Assert.Equal(InputVerdict.Swallow, state.OnUp(T(900)).Verdict);
        Assert.True(state.TryTakeRelease(T(940)));
    }

    [Fact]
    public void LightMode_DropsRepeatDownAndUp()
    {
        var state = Mouse(0);

        Assert.Equal(InputVerdict.PassThrough, state.OnDown(T(0)).Verdict);
        Assert.Equal(InputVerdict.PassThrough, state.OnUp(T(5)).Verdict);
        Assert.Equal(InputVerdict.Swallow, state.OnDown(T(8)).Verdict);
        Assert.Equal(InputVerdict.Swallow, state.OnUp(T(300)).Verdict);
    }

    [Fact]
    public void LightMode_KeepsSlowSecondClick()
    {
        var state = Mouse(0);

        Assert.Equal(InputVerdict.PassThrough, state.OnDown(T(0)).Verdict);
        Assert.Equal(InputVerdict.PassThrough, state.OnUp(T(5)).Verdict);
        Assert.Equal(InputVerdict.PassThrough, state.OnDown(T(105)).Verdict);
    }

    [Fact]
    public void KeyboardAutoRepeat_PassesThrough()
    {
        var state = Keyboard();

        Assert.Equal(InputVerdict.PassThrough, state.OnDown(T(0)).Verdict);
        Assert.Equal(InputVerdict.PassThrough, state.OnDown(T(33)).Verdict);
        Assert.Equal(InputVerdict.PassThrough, state.OnDown(T(66)).Verdict);
    }

    [Fact]
    public void KeyboardChatter_IsSwallowed()
    {
        var state = Keyboard();

        Assert.Equal(InputVerdict.PassThrough, state.OnDown(T(0)).Verdict);
        Assert.Equal(InputVerdict.PassThrough, state.OnUp(T(5)).Verdict);
        Assert.Equal(InputVerdict.Swallow, state.OnDown(T(10)).Verdict);
        Assert.Equal(InputVerdict.Swallow, state.OnUp(T(200)).Verdict);
    }
}
