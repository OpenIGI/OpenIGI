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
        Assert.Equal(120, slot.ActivatedTick);

        detector.BeginScan();
        detector.ExpireInactive(3099);
        Assert.Contains(detector.Slots, static item => item.Occupied);

        // Re-seeing the target refreshed slot[7], but native retention uses slot[8], the original
        // allocation timestamp.  It therefore expires at 3101, not three seconds after the scan.
        detector.ExpireInactive(3101);
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
}
