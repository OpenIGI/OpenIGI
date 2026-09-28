using OpenIGI.Game.Player;
using Xunit;

namespace OpenIGI.Game.Tests;

public sealed class EnemyDetectorOverlayTests
{
    [Fact]
    public void NativeTableReusesExistingTargetsAndRetainsUnseenTargetsForThreeSeconds()
    {
        EnemyDetectorOverlay detector = new();
        detector.Upsert(7, 10f, 1, 100f, 100f, 0, 0, 0, 100, 20f, 40f);

        detector.BeginScan();
        detector.Upsert(7, 9f, 1, 110f, 100f, 0, 0, 0, 120, 20f, 40f);

        EnemyDetectorOverlay.TargetSlot slot = Assert.Single(detector.Slots, static item => item.Occupied);
        Assert.True(slot.Active);
        Assert.Equal(7, slot.TargetId);
        Assert.Equal(100, slot.AllocatedTick);
        Assert.Equal(120, slot.LastSeenTick);

        detector.BeginScan();
        detector.ExpireInactive(3120);
        Assert.Contains(detector.Slots, static item => item.Occupied);

        // Re-seeing the target refreshed slot[7], which is the field FUN_00471B60 compares for
        // retention. Slot[8] stays at its allocation time and drives only the marker pulse.
        detector.ExpireInactive(3121);
        Assert.DoesNotContain(detector.Slots, static item => item.Occupied);
    }

    [Fact]
    public void NativeAnimationPulseDoesNotResizeProjectedTargetGeometry()
    {
        EnemyDetectorOverlay detector = new();
        detector.Upsert(3, 20f, 1, 320f, 240f, 0, 0, 0, 1000, 10f, 20f);

        EnemyOverlayBox initial = Assert.Single(detector.OverlayBoxes(1000));
        EnemyOverlayBox settled = Assert.Single(detector.OverlayBoxes(1250));

        Assert.Equal(10f, initial.Right - initial.Left, 3);
        Assert.Equal(10f, settled.Right - settled.Left, 3);
        Assert.Equal(20f, initial.Bottom - initial.Top, 3);
        Assert.Equal(20f, settled.Bottom - settled.Top, 3);
        Assert.Equal(20f, settled.DistanceMetres, 3);
    }

    [Fact]
    public void RepeatedDetectorScansDoNotRestartTheNativeEntryPulse()
    {
        EnemyDetectorOverlay detector = new();
        detector.Upsert(3, 20f, 1, 320f, 240f, 0, 0, 0, 100, 10f, 20f);

        detector.BeginScan();
        detector.Upsert(3, 19f, 1, 321f, 240f, 0, 0, 0, 350, 10f, 20f);

        EnemyDetectorOverlay.TargetSlot slot = Assert.Single(detector.Slots, static item => item.Occupied);
        EnemyOverlayBox settled = Assert.Single(detector.OverlayBoxes(350));
        Assert.Equal(100, slot.AllocatedTick);
        Assert.Equal(350, slot.LastSeenTick);
        Assert.Equal(1f, settled.PulseScale, 3);
        Assert.Equal(1.5707f, settled.RotationRadians, 3);
    }

    [Fact]
    public void NativeAnimationPulseCarriesTheRotationAndContractionUsedByTheRectangleStrip()
    {
        EnemyDetectorOverlay detector = new();
        detector.Upsert(3, 20f, 1, 320f, 240f, 0, 0, 0, 1000, 10f, 20f);

        EnemyOverlayBox initial = Assert.Single(detector.OverlayBoxes(1000));
        EnemyOverlayBox settled = Assert.Single(detector.OverlayBoxes(1250));

        Assert.Equal(0f, initial.RotationRadians, 3);
        Assert.Equal(11f, initial.PulseScale, 3);
        Assert.Equal(1.5707f, settled.RotationRadians, 3);
        Assert.Equal(1f, settled.PulseScale, 3);
    }

    [Fact]
    public void NativeStyleTargetRectanglesAreClippedToTheViewport()
    {
        EnemyOverlayBox target = new(7, -40f, 120f, 140f, 520f, 18f, 1f);

        Assert.True(target.TryClipToViewport(1280, 480, out EnemyOverlayBox clipped));
        Assert.Equal(0f, clipped.Left);
        Assert.Equal(120f, clipped.Top);
        Assert.Equal(140f, clipped.Right);
        Assert.Equal(480f, clipped.Bottom);
        Assert.Equal(target.DistanceMetres, clipped.DistanceMetres);
    }

    [Fact]
    public void FullyOffscreenTargetRectangleIsNotDrawable()
    {
        EnemyOverlayBox target = new(7, -120f, 20f, -5f, 90f, 18f, 1f);

        Assert.False(target.TryClipToViewport(1280, 480, out _));
    }

    [Fact]
    public void RetailRectangleAtTheScreenEdgeIsDrawnWholeAroundItsTrueCentre()
    {
        // FUN_004B5860 submits the strip unclipped; only a rectangle wholly off screen is skipped.
        EnemyOverlayBox target = new(7, -40f, 120f, 140f, 520f, 18f, 1f);

        Assert.True(target.IntersectsViewport(1280, 480));
        Assert.False(new EnemyOverlayBox(7, -120f, 20f, -5f, 90f, 18f, 1f).IntersectsViewport(1280, 480));
    }

    [Fact]
    public void RetailRectangleCountsItsEntryPulseWhenTestingTheViewport()
    {
        // Ten pixels wide and five off the left edge: settled it is off screen, but at the entry
        // pulse's 11x scale the strip reaches back onto it.
        EnemyOverlayBox settled = new(7, -15f, 100f, -5f, 110f, 18f, 1f);
        EnemyOverlayBox pulsing = settled with { PulseScale = 11f };

        Assert.False(settled.IntersectsViewport(640, 480));
        Assert.True(pulsing.IntersectsViewport(640, 480));
    }
}
