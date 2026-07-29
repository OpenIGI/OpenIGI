using OpenIGI.Game.Weapons;
using Xunit;

namespace OpenIGI.Game.Tests;

/// <summary>
/// The HUD's arithmetic, as <c>0x479D20</c> computes it.
/// </summary>
/// <remarks>
/// The layout is two fractions of the current resolution and everything measured back from them, so
/// these are worth pinning: a HUD authored at a fixed size and centred — the way the <i>menus</i> work
/// — would look right at 640×480 and wrong everywhere else.
/// </remarks>
public sealed class StatusScreenLayoutTests
{
    [Fact]
    public void TheAnchorsAreTenPixelsInAndFiftyUpAtTheAuthoredSize()
    {
        // 630/640 and 430/480. The fractions are what the original stores; these are the pixels they
        // mean at the size the artwork was drawn for.
        Assert.Equal(630f, StatusScreenLayout.RightMargin(640), 3);
        Assert.Equal(430f, StatusScreenLayout.Baseline(480), 3);
    }

    [Fact]
    public void TheAnchorsScaleWithTheWindow()
    {
        // Twice the size, twice the offset -- the HUD is not authored at a fixed size and centred.
        Assert.Equal(1260f, StatusScreenLayout.RightMargin(1280), 3);
        Assert.Equal(860f, StatusScreenLayout.Baseline(960), 3);
    }

    [Fact]
    public void TheCrosshairIsCentredWithATruncatedHalfSprite()
    {
        // The half-SPRITE is truncated to a whole pixel and the half-SCREEN is not, so a 9-pixel
        // crosshair on a 640x480 buffer sits at 316, 236 -- not 315.5, 235.5.
        (float x, float y) = StatusScreenLayout.Crosshair(640, 480, 9f, 9f);
        Assert.Equal(316f, x, 3);
        Assert.Equal(236f, y, 3);
    }

    [Fact]
    public void AnOddBufferPutsTheCrosshairHalfAPixelPastCentre()
    {
        // Not a rounding bug to tidy: it is what the original's mixed truncation does.
        (float x, _) = StatusScreenLayout.Crosshair(641, 480, 9f, 9f);
        Assert.Equal(316.5f, x, 3);
    }

    [Fact]
    public void TheMagazineGaugeEmptiesFromFrameZeroToSixty()
    {
        Assert.Equal(0, StatusScreenLayout.MagazineFrame(17, 17));
        Assert.Equal(StatusScreenLayout.MagazineFrames, StatusScreenLayout.MagazineFrame(0, 17));

        // An INTEGER divide, so the gauge steps rather than sliding: eight of seventeen is 60 - 28.
        Assert.Equal(60 - (60 * 8 / 17), StatusScreenLayout.MagazineFrame(8, 17));
    }

    [Fact]
    public void TheMagazineGaugeStaysInsideItsFrameRange()
    {
        // A weapon with no magazine -- the knife's Rounds/Clip is -1 -- must not index out of the
        // sprite, and it never reaches this draw anyway because it counts no ammunition.
        Assert.InRange(StatusScreenLayout.MagazineFrame(-1, -1), 0, StatusScreenLayout.MagazineFrames);
        Assert.InRange(StatusScreenLayout.MagazineFrame(99, 17), 0, StatusScreenLayout.MagazineFrames);
    }

    [Fact]
    public void TheAmmoCountIsLeftAlignedInFourColumns()
    {
        // "%-4d", so the readout does not jitter as the count changes width.
        Assert.Equal("7   ", StatusScreenLayout.AmmoText(7));
        Assert.Equal("120 ", StatusScreenLayout.AmmoText(120));
        Assert.Equal("1200", StatusScreenLayout.AmmoText(1200));
    }
}
