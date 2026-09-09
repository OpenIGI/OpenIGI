using OpenIGI.Desktop;
using OpenIGI.Host.Lighting;
using Xunit;

namespace OpenIGI.Desktop.Tests;

/// <summary>
/// The gate a freshly loaded level waits behind, held to what a player would see if it were wrong: a level
/// revealed before its lighting exists, a bar that goes backwards, or a loading screen that never ends.
/// </summary>
public sealed class MissionWarmUpGateTests
{
    private const float Frame = 1f / 60f;

    [Fact]
    public void ItHoldsUntilTheLightingIsReadyAndThenForTheSettleFrames()
    {
        var gate = new MissionWarmUpGate(settleFrames: 5);
        gate.Begin();
        Assert.True(gate.IsHolding);

        // Frames drawn while the lighting is still coming do not count towards the settle.
        for (int i = 0; i < 50; i++)
        {
            gate.Update(lightingReady: false, fieldPending: true, convergeProgress: 0f, Frame);
            Assert.True(gate.IsHolding);
        }

        // Once it is ready, exactly the settle frames are drawn and then the gate opens.
        for (int i = 0; i < 4; i++)
        {
            gate.Update(lightingReady: true, fieldPending: false, convergeProgress: 1f, Frame);
            Assert.True(gate.IsHolding);
        }

        gate.Update(lightingReady: true, fieldPending: false, convergeProgress: 1f, Frame);
        Assert.False(gate.IsHolding);
        Assert.False(gate.IsActive);
        Assert.False(gate.GaveUp);
    }

    [Fact]
    public void ALevelWithNothingToWaitForOpensAfterItsSettleFramesAlone()
    {
        var gate = new MissionWarmUpGate(settleFrames: 2);
        gate.Begin();

        gate.Update(lightingReady: true, fieldPending: false, convergeProgress: 1f, Frame);
        Assert.True(gate.IsHolding);
        gate.Update(lightingReady: true, fieldPending: false, convergeProgress: 1f, Frame);
        Assert.False(gate.IsHolding);
    }

    [Fact]
    public void ZeroSettleFramesOpensTheMomentTheLightingIsReady()
    {
        var gate = new MissionWarmUpGate(settleFrames: 0);
        gate.Begin();

        gate.Update(lightingReady: false, fieldPending: false, convergeProgress: 0.5f, Frame);
        Assert.True(gate.IsHolding);

        gate.Update(lightingReady: true, fieldPending: false, convergeProgress: 1f, Frame);
        Assert.False(gate.IsHolding);
    }

    [Fact]
    public void TheBarNeverGoesBackwards()
    {
        var gate = new MissionWarmUpGate(settleFrames: 10);
        gate.Begin();

        float last = 0f;
        gate.ReportLoad(0.3f);
        Assert.True(gate.Progress >= last);
        last = gate.Progress;

        gate.ReportLoad(0.1f); // a stage reported out of order must not pull the bar back
        Assert.Equal(last, gate.Progress);

        // The bake has no progress of its own; the bar eases towards the end of its share and no further.
        for (int i = 0; i < 60 * 8; i++)
        {
            gate.Update(lightingReady: false, fieldPending: true, convergeProgress: 0f, Frame);
            Assert.True(gate.Progress >= last);
            last = gate.Progress;
        }

        // The convergence, then the settle, each move it on.
        for (int i = 0; i < 10; i++)
        {
            gate.Update(lightingReady: false, fieldPending: false, convergeProgress: i / 10f, Frame);
            Assert.True(gate.Progress >= last);
            last = gate.Progress;
        }

        while (gate.IsHolding)
        {
            gate.Update(lightingReady: true, fieldPending: false, convergeProgress: 1f, Frame);
            Assert.True(gate.Progress >= last);
            last = gate.Progress;
        }

        Assert.Equal(1f, gate.Progress);
    }

    [Fact]
    public void ItGivesUpRatherThanHoldingForever()
    {
        // A bake that has hung must not hold a loading screen indefinitely; an honest cross-over later is
        // better than a level that never appears.
        var gate = new MissionWarmUpGate(settleFrames: 3);
        gate.Begin();

        int frames = 0;
        while (gate.IsHolding && frames < 60 * 60)
        {
            gate.Update(lightingReady: false, fieldPending: true, convergeProgress: 0f, Frame);
            frames++;
        }

        Assert.False(gate.IsHolding);
        Assert.True(gate.GaveUp);

        // Just past the patience plus the settle, not a minute later.
        Assert.InRange(frames, (int)(MissionWarmUpGate.PatienceSeconds * 60) + 1, (int)(MissionWarmUpGate.PatienceSeconds * 60) + 10);
    }

    [Fact]
    public void CancellingStopsTheHoldAtOnce()
    {
        var gate = new MissionWarmUpGate(settleFrames: 30);
        gate.Begin();
        gate.Update(lightingReady: false, fieldPending: true, convergeProgress: 0f, Frame);
        Assert.True(gate.IsHolding);

        gate.Cancel();
        Assert.False(gate.IsHolding);
        Assert.False(gate.IsActive);
        Assert.Equal(1f, gate.Progress);
    }

    [Fact]
    public void TheCaptionSaysWhatItIsWaitingOn()
    {
        var gate = new MissionWarmUpGate(settleFrames: 2);
        gate.Begin();
        Assert.Equal("Loading the level", gate.Stage);

        gate.Update(lightingReady: false, fieldPending: true, convergeProgress: 0f, Frame);
        Assert.Equal("Tracing the level's distance field", gate.Stage);

        gate.Update(lightingReady: false, fieldPending: false, convergeProgress: 0.2f, Frame);
        Assert.Equal("Lighting the level", gate.Stage);

        gate.Update(lightingReady: true, fieldPending: false, convergeProgress: 1f, Frame);
        Assert.Equal("Settling", gate.Stage);
    }
}
