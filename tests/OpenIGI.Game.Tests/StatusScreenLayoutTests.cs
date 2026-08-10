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
    public void HealthAndTimerUseTheirRetailAnchors()
    {
        Assert.Equal((40f, 430f), StatusScreenLayout.HealthAnchor(640, 480));
        Assert.Equal((520f, 20f), StatusScreenLayout.TimerPosition(640, 480));
    }

    [Fact]
    public void InteractionProgressUsesTheRetailNestedRectanglesAtEveryResolution()
    {
        Assert.Equal((200f, 400f, 240f, 9f), StatusScreenLayout.InteractionBackground(640, 480));

        (float x, float y, float width, float height) = StatusScreenLayout.InteractionFill(640, 480, 0.5f);
        Assert.Equal(202f, x, 3);
        Assert.Equal(402f, y, 3);
        Assert.Equal(118f, width, 3);
        Assert.Equal(5f, height, 3);

        Assert.Equal(472f, StatusScreenLayout.InteractionFill(1280, 960, 1f).Width, 3);
    }

    [Theory]
    [InlineData(0, "Time Left: 0 minutes, 0 seconds")]
    [InlineData(30, "Time Left: 0 minutes, 1 second")]
    [InlineData(1800, "Time Left: 1 minute, 0 seconds")]
    [InlineData(1830, "Time Left: 1 minute, 1 second")]
    [InlineData(3780, "Time Left: 2 minutes, 6 seconds")]
    public void MissionTimerUsesTheRetailTextAndPluralRules(int ticks, string expected)
        => Assert.Equal(expected, StatusScreenLayout.TimerText(ticks));

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
    public void RevolverAndShotgunGaugesUseTheirOwnRetailFrameArithmetic()
    {
        Assert.Equal(0, StatusScreenLayout.BarrelFrame(roundsInClip: 6, secondSubCounter: 0));
        Assert.Equal(30, StatusScreenLayout.BarrelFrame(roundsInClip: 3, secondSubCounter: 0));
        Assert.Equal(34, StatusScreenLayout.BarrelFrame(roundsInClip: 3, secondSubCounter: 4));
        Assert.Equal(0, StatusScreenLayout.ShellFrame(roundsInClip: 7));
        Assert.Equal(4, StatusScreenLayout.ShellFrame(roundsInClip: 3));
        Assert.Equal(7, StatusScreenLayout.ShellFrame(roundsInClip: 0));
    }

    [Fact]
    public void TheAmmoCountIsLeftAlignedInFourColumns()
    {
        // "%-4d", so the readout does not jitter as the count changes width.
        Assert.Equal("7   ", StatusScreenLayout.AmmoText(7));
        Assert.Equal("120 ", StatusScreenLayout.AmmoText(120));
        Assert.Equal("1200", StatusScreenLayout.AmmoText(1200));
    }

    [Fact]
    public void TheLadderIconIsCentredAtSevenEighthsHeight()
    {
        (float x, float y) = StatusScreenLayout.ActionIcon(640, 480, 31, 21);

        Assert.Equal(305f, x, 3); // integer half-width: 31 / 2 = 15
        Assert.Equal(410f, y, 3); // 480 * .875 - (21 / 2)
    }

    [Theory]
    [InlineData(0f, 0.3f)]
    [InlineData(1f, 1f)]
    [InlineData(2f, 0.3f)]
    public void TheActionIconUsesTheRetailTriangularPulse(float phase, float expected)
    {
        Assert.Equal(expected, StatusScreenLayout.ActionIconAlpha(phase), 3);
    }

    [Theory]
    [InlineData(-1f, 0f)]
    [InlineData(2f, 236f)]
    public void TheTimedInteractionFillClampsToItsFrame(float progress, float expectedWidth)
    {
        (_, _, float width, _) = StatusScreenLayout.InteractionFill(640, 480, progress);
        Assert.Equal(expectedWidth, width, 3);
    }

    [Fact]
    public void TheMessageColumnStartsTenPixelsInAndTenDownAtTheAuthoredSize()
    {
        // width/64 and height/48, from StatusMessageScreen's draw slot at 0x486020.
        Assert.Equal(10f, StatusScreenLayout.MessageLeft(640), 3);
        Assert.Equal(10f, StatusScreenLayout.MessageTop(480), 3);
    }

    [Fact]
    public void TheSubtitleSitsWhereTheOriginalPutsIt()
    {
        // 415 of 480 -- low on the screen and clear of the weapon readout at 430.
        Assert.Equal(415f, StatusScreenLayout.SubtitleY(480), 3);
        Assert.True(
            StatusScreenLayout.SubtitleY(480) < StatusScreenLayout.Baseline(480),
            "the subtitle line must stay above the weapon readout's baseline");
    }

    [Fact]
    public void TheMessagePanelFlashesForExactlyOneTick()
    {
        // The original divides two ints -- (9 - frame) / 9 -- so this is 1 on the first tick and 0 from
        // the second. It is a single-frame flash, not a nine-tick ramp, and a "tidied" smooth fade here
        // would be a different effect built from the same numbers.
        Assert.Equal(0.75f, StatusScreenLayout.MessagePanelAlpha(0), 3);
        Assert.Equal(0.15f, StatusScreenLayout.MessagePanelAlpha(1), 3);
        Assert.Equal(0.15f, StatusScreenLayout.MessagePanelAlpha(8), 3);
        Assert.Equal(0.15f, StatusScreenLayout.MessagePanelAlpha(9), 3);
    }

    [Fact]
    public void TheMessagePanelNeverGoesTransparentHoweverLongItIsUp()
    {
        // (9 - frame) is negative past the ninth tick, and without the clamp the alpha would fall
        // through zero and the panel would invert into a bright block.
        Assert.Equal(0.15f, StatusScreenLayout.MessagePanelAlpha(100), 3);
        Assert.Equal(0.15f, StatusScreenLayout.MessagePanelAlpha(10000), 3);
    }

    [Fact]
    public void TheMessagePanelOpensItsHeightFirstAndItsWidthSecond()
    {
        // Two ticks of height at half a step each, then seven of width at a seventh each: for the first
        // two ticks the panel is a full-height line of no width, so nothing is on screen at all.
        Assert.Equal(0f, StatusScreenLayout.MessageHeightScale(0), 3);
        Assert.Equal(0.5f, StatusScreenLayout.MessageHeightScale(1), 3);
        Assert.Equal(1f, StatusScreenLayout.MessageHeightScale(2), 3);

        Assert.Equal(0f, StatusScreenLayout.MessageWidthScale(0), 3);
        Assert.Equal(0f, StatusScreenLayout.MessageWidthScale(2), 3);
        Assert.Equal(1f, StatusScreenLayout.MessageWidthScale(9), 3);
    }

    [Fact]
    public void ThePanelIsFullyOpenBeforeTheFirstCharacterIsTyped()
    {
        // The reveal starts on the ninth tick and the width finishes on the ninth, so text never appears
        // over a panel that is still narrower than it is. The two are sequenced, not concurrent.
        Assert.Equal(1f, StatusScreenLayout.MessageWidthScale(StatusScreenLayout.MessageOpenTicks), 3);
        Assert.Equal(1f, StatusScreenLayout.MessageHeightScale(StatusScreenLayout.MessageOpenTicks), 3);
    }

    [Fact]
    public void TheTextWrapsInsideThePanelRatherThanOverItsEdges()
    {
        // 229 against a 235-wide panel: the three-pixel inset at each side, and the reason a message
        // never reads as though it has outgrown the box behind it.
        float panelWidth = StatusScreenLayout.MessagePanelHalfWidth * 2f;
        Assert.Equal(
            StatusScreenLayout.MessageWrapWidth,
            panelWidth - (StatusScreenLayout.MessageTextInset * 2f),
            3);
    }
}
