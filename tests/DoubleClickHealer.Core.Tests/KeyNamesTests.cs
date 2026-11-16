using DoubleClickHealer.Core;
using Xunit;

namespace DoubleClickHealer.Core.Tests;

public class KeyNamesTests
{
    [Theory]
    [InlineData(0x41, "A")]
    [InlineData(0x5A, "Z")]
    [InlineData(0x30, "0")]
    [InlineData(0x70, "F1")]
    public void Keyboard_LettersAndDigits_UseSystemNames(int virtualKey, string expected)
    {
        Assert.Equal(expected, KeyNames.Keyboard(virtualKey));
    }

    [Fact]
    public void Keyboard_Modifiers_UseEnglishNames()
    {
        Assert.Equal("Left Shift", KeyNames.Keyboard(VirtualKeys.LShift));
        Assert.Equal("Right Shift", KeyNames.Keyboard(VirtualKeys.RShift));
        Assert.Equal("Left Ctrl", KeyNames.Keyboard(VirtualKeys.LControl));
        Assert.Equal("Right Alt", KeyNames.Keyboard(VirtualKeys.RMenu));
    }

    [Theory]
    [InlineData(0x25)]
    [InlineData(0x26)]
    [InlineData(0x27)]
    [InlineData(0x28)]
    [InlineData(0x2D)]
    [InlineData(0x2E)]
    [InlineData(0x24)]
    [InlineData(0x23)]
    public void Keyboard_ExtendedKeys_AreNotNumpadNames(int virtualKey)
    {
        string name = KeyNames.Keyboard(virtualKey);

        Assert.DoesNotContain("Num", name, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("VK_", name);
    }

    [Fact]
    public void Keyboard_AllNamesAreAscii()
    {
        int[] keys =
        [
            0x08, 0x09, 0x0D, 0x1B, 0x20,
            0x21, 0x22, 0x23, 0x24, 0x25, 0x26, 0x27, 0x28,
            0x2C, 0x2D, 0x2E, 0x60, 0x69, 0x90, 0x91,
            VirtualKeys.LShift, VirtualKeys.RShift,
            VirtualKeys.LControl, VirtualKeys.RControl,
            VirtualKeys.LMenu, VirtualKeys.RMenu,
        ];

        foreach (int key in keys)
        {
            Assert.All(KeyNames.Keyboard(key), c => Assert.True(c < 128));
        }
    }

    [Fact]
    public void Mouse_UsesEnglishNames()
    {
        Assert.Equal("Mouse Left", KeyNames.Mouse(MouseButtons.Left));
        Assert.Equal("Mouse Right", KeyNames.Mouse(MouseButtons.Right));
        Assert.Equal("Mouse Middle", KeyNames.Mouse(MouseButtons.Middle));
    }
}
