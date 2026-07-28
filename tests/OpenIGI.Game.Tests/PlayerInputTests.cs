using OpenIGI.Game.Player;
using Xunit;

namespace OpenIGI.Game.Tests;

/// <summary>
/// Pins the shape of the player's input frame and the edge rule the update runs on.
/// </summary>
public sealed class PlayerInputTests
{
    [Fact]
    public void AFrameIsTheOriginalsHundredAndSixtyBytes()
    {
        Assert.Equal(40, PlayerInputFrame.SlotCount);
        Assert.Equal(160, PlayerInputFrame.SizeInBytes);
        Assert.Equal(160, System.Runtime.InteropServices.Marshal.SizeOf<PlayerInputFrame>());
    }

    [Fact]
    public void AnActionSlotCarriesItsMaskedBitNotOne()
    {
        // The sampler stores `mask & bit`, so slot 12 holds 0x8000 rather than 1 and slot 31 holds
        // 0x40000000. Anything comparing a slot to 1 reads them as released.
        PlayerInputFrame frame = default;
        frame[(int)PlayerInputSlot.Action12] = 0x8000;
        frame[(int)PlayerInputSlot.Action31] = 0x40000000;

        Assert.True(frame.IsSet(PlayerInputSlot.Action12));
        Assert.True(frame.IsSet(PlayerInputSlot.Action31));
        Assert.NotEqual(1f, frame[(int)PlayerInputSlot.Action12]);
        Assert.False(frame.IsSet(PlayerInputSlot.Action13));
    }

    [Fact]
    public void SensitivityClampsLowFirstSoNanBecomesTheFloor()
    {
        Assert.Equal(0.05f, PlayerInputFrame.ClampSensitivity(0f));
        Assert.Equal(0.05f, PlayerInputFrame.ClampSensitivity(0.01f));
        Assert.Equal(0.5f, PlayerInputFrame.ClampSensitivity(0.5f));
        Assert.Equal(1.0f, PlayerInputFrame.ClampSensitivity(4f));

        // The original's compare-and-branch pair fails both tests for a NaN and falls into the low arm.
        Assert.Equal(0.05f, PlayerInputFrame.ClampSensitivity(float.NaN));
    }

    [Fact]
    public void TheEdgeIsAgainstThePreviousFrameNotAgainstZero()
    {
        PlayerInputState state = new();
        PlayerInputFrame pressed = default;
        pressed[(int)PlayerInputSlot.Action20] = 0x40000;

        Assert.True(state.Accept(pressed));
        Assert.True(state.IsRisingEdge(PlayerInputSlot.Action20));

        // Held for a second tick: still set, but no longer an edge.
        Assert.True(state.Accept(pressed));
        Assert.True(state.IsSet(PlayerInputSlot.Action20));
        Assert.False(state.IsRisingEdge(PlayerInputSlot.Action20));

        Assert.True(state.Accept(default));
        Assert.True(state.IsFallingEdge(PlayerInputSlot.Action20));
    }

    [Fact]
    public void FreezingHoldsBothFramesRatherThanClearingThem()
    {
        PlayerInputState state = new();
        PlayerInputFrame pressed = default;
        pressed[(int)PlayerInputSlot.Action20] = 0x40000;
        state.Accept(pressed);

        state.IsFrozen = true;
        Assert.False(state.Accept(default));

        // The action is still held and no edge fired, because neither frame moved.
        Assert.True(state.IsSet(PlayerInputSlot.Action20));
        Assert.True(state.IsRisingEdge(PlayerInputSlot.Action20));
    }

    [Fact]
    public void FromSlotsRoundTripsAndRejectsTheWrongLength()
    {
        float[] slots = [.. Enumerable.Range(0, PlayerInputFrame.SlotCount).Select(i => (float)i)];
        PlayerInputFrame frame = PlayerInputFrame.FromSlots(slots);

        Assert.Equal(slots, frame.AsSpan().ToArray());
        Assert.Equal(frame, PlayerInputFrame.FromSlots(slots));
        Assert.Throws<ArgumentException>(() => PlayerInputFrame.FromSlots(new float[39]));
    }
}
