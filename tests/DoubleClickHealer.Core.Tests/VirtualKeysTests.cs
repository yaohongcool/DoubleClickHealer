using DoubleClickHealer.Core;
using Xunit;

namespace DoubleClickHealer.Core.Tests;

public class VirtualKeysTests
{
    [Theory]
    [InlineData(VirtualKeys.Shift, 0x2A, false, VirtualKeys.LShift)]
    [InlineData(VirtualKeys.Shift, 0x36, false, VirtualKeys.RShift)]
    [InlineData(VirtualKeys.Control, 0x1D, false, VirtualKeys.LControl)]
    [InlineData(VirtualKeys.Control, 0x1D, true, VirtualKeys.RControl)]
    [InlineData(VirtualKeys.Menu, 0x38, false, VirtualKeys.LMenu)]
    [InlineData(VirtualKeys.Menu, 0x38, true, VirtualKeys.RMenu)]
    [InlineData(0x41, 0x1E, false, 0x41)]
    public void Normalize_SplitsModifiers(int vk, int scan, bool extended, int expected)
    {
        Assert.Equal(expected, VirtualKeys.Normalize(vk, scan, extended));
    }

    [Theory]
    [InlineData(VirtualKeys.LShift, VirtualKeys.Shift, false)]
    [InlineData(VirtualKeys.RShift, VirtualKeys.Shift, false)]
    [InlineData(VirtualKeys.LControl, VirtualKeys.Control, false)]
    [InlineData(VirtualKeys.RControl, VirtualKeys.Control, true)]
    [InlineData(VirtualKeys.LMenu, VirtualKeys.Menu, false)]
    [InlineData(VirtualKeys.RMenu, VirtualKeys.Menu, true)]
    [InlineData(0x41, 0x41, false)]
    public void Denormalize_RestoresSystemKeys(int vk, int expectedVk, bool expectedExtended)
    {
        var (key, extended) = VirtualKeys.Denormalize(vk);

        Assert.Equal(expectedVk, key);
        Assert.Equal(expectedExtended, extended);
    }

    [Fact]
    public void NormalizeAndDenormalize_RoundTripsModifiers()
    {
        int[] modifiers =
        [
            VirtualKeys.LShift, VirtualKeys.RShift,
            VirtualKeys.LControl, VirtualKeys.RControl,
            VirtualKeys.LMenu, VirtualKeys.RMenu,
        ];

        foreach (int modifier in modifiers)
        {
            var (systemKey, extended) = VirtualKeys.Denormalize(modifier);
            int scan = modifier == VirtualKeys.RShift ? 0x36 : 0x2A;
            Assert.Equal(modifier, VirtualKeys.Normalize(systemKey, scan, extended));
        }
    }
}
