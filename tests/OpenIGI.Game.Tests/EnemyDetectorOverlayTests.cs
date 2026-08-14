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
        detector.ExpireInactive(3119);
        Assert.Contains(detector.Slots, static item => item.Occupied);

        detector.ExpireInactive(3121);
        Assert.DoesNotContain(detector.Slots, static item => item.Occupied);
    }

    [Fact]
    public void NativePulseStartsLargeAndContractsToTheAuthoredBounds()
    {
        EnemyDetectorOverlay detector = new();
        detector.Upsert(3, 20f, 1, 320f, 240f, 0, 0, 0, 1000, 10f, 20f);

        EnemyOverlayBox initial = Assert.Single(detector.OverlayBoxes(1000));
        EnemyOverlayBox settled = Assert.Single(detector.OverlayBoxes(1250));

        Assert.Equal(110f, initial.Right - initial.Left, 3);
        Assert.Equal(10f, settled.Right - settled.Left, 3);
        Assert.Equal(220f, initial.Bottom - initial.Top, 3);
        Assert.Equal(20f, settled.Bottom - settled.Top, 3);
    }
}
