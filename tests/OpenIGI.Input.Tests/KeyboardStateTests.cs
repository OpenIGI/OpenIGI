using Xunit;

namespace OpenIGI.Input.Tests;

public sealed class KeyboardStateTests
{
    [Fact]
    public void Update_TakesTheShadowBeforeApplyingTheNewState()
    {
        KeyboardState keyboard = new();

        keyboard.Update(Down(KeyCode.W));
        Assert.True(keyboard.IsDown(KeyCode.W));
        Assert.True(keyboard.WasPressed(KeyCode.W));

        // Second frame with the key still held: down, but no longer a press.
        keyboard.Update(Down(KeyCode.W));
        Assert.True(keyboard.IsDown(KeyCode.W));
        Assert.False(keyboard.WasPressed(KeyCode.W));

        keyboard.Update([]);
        Assert.False(keyboard.IsDown(KeyCode.W));
        Assert.True(keyboard.WasReleased(KeyCode.W));
    }

    [Fact]
    public void Release_ReportsAFallingEdgeRatherThanStickingTheKeyDown()
    {
        // The original zeroes its whole key array when the device cannot be read — losing focus, for
        // instance — and it does so after taking the shadow, so the frame sees a clean release.
        KeyboardState keyboard = new();
        keyboard.Update(Down(KeyCode.Space));

        keyboard.Release();

        Assert.False(keyboard.IsDown(KeyCode.Space));
        Assert.True(keyboard.WasReleased(KeyCode.Space));
    }

    [Fact]
    public void EventDrivenHostsUseBeginFrameAndSetKey()
    {
        KeyboardState keyboard = new();

        keyboard.BeginFrame();
        keyboard.SetKey(KeyCode.Escape, down: true);
        Assert.True(keyboard.WasPressed(KeyCode.Escape));

        keyboard.BeginFrame();
        Assert.True(keyboard.IsDown(KeyCode.Escape));
        Assert.False(keyboard.WasPressed(KeyCode.Escape));

        keyboard.BeginFrame();
        keyboard.SetKey(KeyCode.Escape, down: false);
        Assert.True(keyboard.WasReleased(KeyCode.Escape));
    }

    [Fact]
    public void DownStateUsesTheOriginalsHighBitEncoding()
    {
        KeyboardState keyboard = new();
        keyboard.Update(Down(KeyCode.F9));

        Assert.Equal(KeyboardState.DownValue, keyboard.Current[(byte)KeyCode.F9]);
        Assert.Equal(0x80, KeyboardState.DownValue);
        Assert.Equal(256, KeyboardState.KeyCount);
    }

    [Fact]
    public void ScanCodesMatchTheDirectInputNumbering()
    {
        // These indices are part of the data format: retail configs store them as raw numbers.
        Assert.Equal(0x01, (byte)KeyCode.Escape);
        Assert.Equal(0x1C, (byte)KeyCode.Return);
        Assert.Equal(0x1D, (byte)KeyCode.LeftControl);
        Assert.Equal(0x2A, (byte)KeyCode.LeftShift);
        Assert.Equal(0x39, (byte)KeyCode.Space);
        Assert.Equal(0x43, (byte)KeyCode.F9);
        Assert.Equal(0xC8, (byte)KeyCode.Up);
        Assert.Equal(0xD0, (byte)KeyCode.Down);
    }

    [Fact]
    public void Update_RejectsAnOversizedState()
    {
        KeyboardState keyboard = new();
        Assert.Throws<ArgumentException>(() => keyboard.Update(new byte[257]));
    }

    [Fact]
    public void Update_ClearsKeysTheNewStateDoesNotMention()
    {
        KeyboardState keyboard = new();
        keyboard.Update(Down(KeyCode.A));
        keyboard.Update(Down(KeyCode.B));

        Assert.False(keyboard.IsDown(KeyCode.A));
        Assert.True(keyboard.IsDown(KeyCode.B));
    }

    private static byte[] Down(params KeyCode[] keys)
    {
        byte[] state = new byte[KeyboardState.KeyCount];
        foreach (KeyCode key in keys)
        {
            state[(byte)key] = KeyboardState.DownValue;
        }

        return state;
    }
}
