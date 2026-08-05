namespace OpenIGI.Game.Weapons;

/// <summary>
/// Where the in-game HUD puts everything, as <c>StatusScreen</c> and the weapon's own HUD handler
/// compute it.
/// </summary>
/// <remarks>
/// <para>
/// The HUD is a task of its own — <c>StatusScreen</c>, 1264 bytes, registered at <c>0x486900</c> with
/// the resource prefix <c>STATUSSCREEN:</c> bound to <c>LOCAL:screens/game/status/</c>. Its render
/// (<c>0x486F30</c>) draws its children and then sends one message to the <b>weapon in hand</b>, which
/// the <c>Gun</c> class answers with <c>0x479D20</c>. So every part of the HUD that depends on what you
/// are carrying is drawn by the weapon, not by the screen.
/// </para>
/// <para>
/// <b>Everything is a fraction of the current resolution</b>, so the HUD scales with the window rather
/// than being authored at 640×480 and centred the way the menus are. The two fractions below are the
/// whole of the layout: an anchor at the right and one near the bottom, with every element measured
/// back from them.
/// </para>
/// </remarks>
public static class StatusScreenLayout
{
    /// <summary>The resource prefix <c>0x486900</c> binds for the HUD's own archive.</summary>
    public const string ResourcePrefix = "STATUSSCREEN:";

    /// <summary>Where that prefix points.</summary>
    public const string ResourceRoot = "LOCAL:screens/game/status/";

    /// <summary>The archive every HUD sprite and the HUD font come out of.</summary>
    public const string Archive = ResourceRoot + "status.res";

    /// <summary>The crosshair's sprite.</summary>
    /// <remarks>Nine pixels square, one frame.</remarks>
    public const string CrosshairSprite = "cross.spr";

    /// <summary>The magazine gauge's sprite.</summary>
    /// <remarks>
    /// <b>Sixty-one frames</b>, and that is what settles how it is drawn: the seventh argument of the
    /// sprite draw is a <i>frame index</i>, not a crop, so the magazine empties by picking a frame.
    /// </remarks>
    public const string MagazineSprite = "clip20.spr";

    /// <summary>The action sprite displayed for activate type 10 (ladder).</summary>
    public const string LadderActionSprite = "a_ladder.spr";

    /// <summary>The action sprite displayed for a wire or zipline.</summary>
    public const string WireActionSprite = "a_wire.spr";

    /// <summary>The action sprite displayed for a climbable fence.</summary>
    public const string FenceActionSprite = "a_fence.spr";

    /// <summary>The action sprite displayed for a door.</summary>
    public const string DoorActionSprite = "a_door.spr";

    /// <summary>The action sprite displayed for a wall switch.</summary>
    public const string SwitchActionSprite = "action_pressbutton.spr";

    /// <summary>The vertical action-icon anchor, as a fraction of the framebuffer height.</summary>
    public const float ActionIconYFraction = 0.875f;

    /// <summary>The amount <c>StatusScreen+1256</c> advances per 30 Hz tick.</summary>
    public const float ActionPulseStep = 0.06666667f;

    /// <summary>How many frames the magazine gauge has minus one — its empty frame.</summary>
    /// <remarks>
    /// Sixty. The original computes <c>60 - 60 × roundsInClip / roundsPerClip</c>, so a full magazine
    /// is frame 0 and an empty one frame 60.
    /// </remarks>
    public const int MagazineFrames = 60;

    /// <summary>Where the right-hand edge of the weapon readout sits, as a fraction of the width.</summary>
    /// <remarks><c>0.984375</c> — ten pixels in from the right at 640 wide.</remarks>
    public const float RightMarginFraction = 0.984375f;

    /// <summary>Where the weapon readout's baseline sits, as a fraction of the height.</summary>
    /// <remarks><c>0.89583331</c> — fifty pixels up from the bottom at 480 tall.</remarks>
    public const float BaselineFraction = 0.89583331f;

    /// <summary>The gap between the baseline and the weapon's name.</summary>
    public const float NameGap = 2f;

    /// <summary>The extra gap between the name and the weapon's icon.</summary>
    public const float IconGap = 2f;

    /// <summary>How far below the baseline the ammunition count sits.</summary>
    public const float AmmoDrop = 4f;

    /// <summary>How far in from the right margin the ammunition count sits.</summary>
    public const float AmmoInset = 4f;

    /// <summary>The gap between the ammunition count and the magazine gauge.</summary>
    public const float MagazineGap = 8f;

    /// <summary>The gap added to the name's width when the underline is sized.</summary>
    public const float UnderlineNamePadding = 3f;

    /// <summary>The underline's colour.</summary>
    /// <remarks>Green at <c>0.9</c>, drawn at half alpha.</remarks>
    public static (float R, float G, float B, float A) UnderlineColour => (0f, 0.89999998f, 0f, 0.5f);

    // ---------------------------------------------------------------------------------------------
    // The status-message column, recovered from StatusMessageScreen's draw slot at 0x486020. The RE
    // notes record this family of text as placed in fractions of the framebuffer rather than authored at
    // 640x480, and these follow that: the constants below are the literal multipliers in the original.
    // ---------------------------------------------------------------------------------------------

    /// <summary>Where the message column's left edge sits, as a fraction of the width.</summary>
    /// <remarks><c>0.015625</c> is one sixty-fourth — ten pixels in from the left at 640 wide.</remarks>
    public const float MessageLeftFraction = 0.015625f;

    /// <summary>Where the first message sits, as a fraction of the height.</summary>
    /// <remarks><c>0.020833334</c> is one forty-eighth — ten pixels down from the top at 480 tall.</remarks>
    public const float MessageTopFraction = 0.020833334f;

    /// <summary>Where a cutscene message's subtitle sits, as a fraction of the height.</summary>
    /// <remarks>
    /// <c>0.86458331</c> — 415 pixels down at 480 tall, low on the screen and clear of the weapon
    /// readout. A cutscene message takes this line instead of a place in the column, and it does not
    /// stack: the original draws every one of them at the same height.
    /// </remarks>
    public const float SubtitleYFraction = 0.86458331f;

    /// <summary>How far apart stacked messages sit, in scale-1 pixels.</summary>
    /// <remarks>Advanced per <em>shown</em> message, so a finished one closes the gap it leaves.</remarks>
    public const float MessageStep = 42f;

    /// <summary>Half the width of the panel behind a message, in scale-1 pixels.</summary>
    public const float MessagePanelHalfWidth = 117.5f;

    /// <summary>The height of the panel behind a message with no icon, in scale-1 pixels.</summary>
    /// <remarks>With an icon the panel takes the icon's height instead.</remarks>
    public const float MessagePanelHeight = 37f;

    /// <summary>The gap between the panel's left edge and the text, in scale-1 pixels.</summary>
    public const float MessageTextInset = 3f;

    /// <summary>The gap between the panel's top edge and the text, in scale-1 pixels.</summary>
    public const float MessageTextDrop = 2f;

    /// <summary>How many ticks the panel spends opening before it settles.</summary>
    /// <remarks>
    /// Nine. Over them the panel fades from <see cref="MessagePanelPeakAlpha"/> to
    /// <see cref="MessagePanelRestAlpha"/> and the category icon is held back entirely, which is what
    /// makes a message arrive rather than appear.
    /// </remarks>
    public const int MessageOpenTicks = 9;

    /// <summary>The panel's alpha on the tick a message arrives.</summary>
    public const float MessagePanelPeakAlpha = 0.75f;

    /// <summary>The panel's alpha once it has settled.</summary>
    public const float MessagePanelRestAlpha = 0.15f;

    /// <summary>The message text's colour.</summary>
    /// <remarks>
    /// The original writes <c>(0, 120, 0)</c> into the text object's colour fields, on the same 0-255
    /// scale where <c>128</c> is unmodulated — so this is a green a little under full strength rather
    /// than a dim one.
    /// </remarks>
    public static (float R, float G, float B) MessageColour => (0f, 120f, 0f);

    /// <summary>How wide the message text may run before it wraps, in scale-1 pixels.</summary>
    /// <remarks>
    /// 229, which is the 235-wide panel less the three-pixel inset at each side. The original re-lays the
    /// text at this width on <em>every</em> revealed character, so a word that will not fit moves to the
    /// next line the moment its last letter is typed rather than when the line is finished.
    /// </remarks>
    public const float MessageWrapWidth = 229f;

    /// <summary>How wide a cutscene subtitle may run, as a fraction of the framebuffer width.</summary>
    /// <remarks><c>0.96875</c> — 620 pixels at 640 wide, so a subtitle uses nearly the whole screen.</remarks>
    public const float SubtitleWrapFraction = 0.96875f;

    /// <summary>How far the panel's height opens per tick over the first two ticks.</summary>
    public const float MessageHeightOpenStep = 0.5f;

    /// <summary>How far the panel's width opens per tick over the seven ticks after that.</summary>
    public const float MessageWidthOpenStep = 0.14285715f;

    /// <summary>How much of its full height the panel has opened to.</summary>
    /// <param name="displayFrame">How many ticks the message has been on screen.</param>
    /// <returns>A factor from 0 to 1.</returns>
    /// <remarks>
    /// The panel opens in two movements, and in this order: it reaches full height in two ticks while it
    /// is still a vertical line of no width, then unfurls sideways over the next seven. That is why the
    /// first couple of ticks show nothing at all, and why what follows reads as a panel being drawn open
    /// rather than one being faded in.
    /// </remarks>
    public static float MessageHeightScale(int displayFrame) =>
        Math.Clamp(displayFrame * MessageHeightOpenStep, 0f, 1f);

    /// <summary>How much of its full width the panel has opened to.</summary>
    /// <param name="displayFrame">How many ticks the message has been on screen.</param>
    /// <returns>A factor from 0 to 1.</returns>
    public static float MessageWidthScale(int displayFrame) =>
        Math.Clamp((displayFrame - 2) * MessageWidthOpenStep, 0f, 1f);

    /// <summary>The panel's alpha for a message that has been up a given number of ticks.</summary>
    /// <param name="displayFrame">How many ticks the message has been on screen.</param>
    /// <returns>The alpha, from <see cref="MessagePanelPeakAlpha"/> down to <see cref="MessagePanelRestAlpha"/>.</returns>
    /// <remarks>
    /// The original computes this in integer arithmetic — <c>(9 - frame) / 9</c> on <c>int</c>s — so it is
    /// 1 only on the very first tick and 0 from the second onwards, which makes the opening a single
    /// bright flash rather than a nine-tick ramp. Reproduced exactly, because a smooth fade here is a
    /// different effect that happens to be built from the same numbers.
    /// </remarks>
    public static float MessagePanelAlpha(int displayFrame)
    {
        int ramp = Math.Max(0, (MessageOpenTicks - displayFrame) / MessageOpenTicks);
        return (ramp * (MessagePanelPeakAlpha - MessagePanelRestAlpha)) + MessagePanelRestAlpha;
    }

    /// <summary>The left edge of the message column, in pixels.</summary>
    /// <param name="screenWidth">The back buffer's width.</param>
    /// <returns>Where a message starts.</returns>
    public static float MessageLeft(int screenWidth) => screenWidth * MessageLeftFraction;

    /// <summary>The top of the message column, in pixels.</summary>
    /// <param name="screenHeight">The back buffer's height.</param>
    /// <returns>Where the first message sits.</returns>
    public static float MessageTop(int screenHeight) => screenHeight * MessageTopFraction;

    /// <summary>The subtitle line, in pixels.</summary>
    /// <param name="screenHeight">The back buffer's height.</param>
    /// <returns>Where a cutscene message sits.</returns>
    public static float SubtitleY(int screenHeight) => screenHeight * SubtitleYFraction;

    /// <summary>The right-hand anchor, in pixels.</summary>
    /// <param name="screenWidth">The back buffer's width.</param>
    /// <returns>Where the readout's right edge is.</returns>
    public static float RightMargin(int screenWidth) => screenWidth * RightMarginFraction;

    /// <summary>The readout's baseline, in pixels.</summary>
    /// <param name="screenHeight">The back buffer's height.</param>
    /// <returns>Where the readout sits vertically.</returns>
    public static float Baseline(int screenHeight) => screenHeight * BaselineFraction;

    /// <summary>Where the crosshair's top-left corner goes.</summary>
    /// <param name="screenWidth">The back buffer's width.</param>
    /// <param name="screenHeight">The back buffer's height.</param>
    /// <param name="spriteWidth">The crosshair sprite's width.</param>
    /// <param name="spriteHeight">The crosshair sprite's height.</param>
    /// <returns>The corner, in pixels.</returns>
    /// <remarks>
    /// <b>The half-sprite is truncated to a whole pixel and the half-screen is not</b>, so on an
    /// odd-sized back buffer the crosshair sits half a pixel right of and below centre. That is the
    /// original's arithmetic, spelled out rather than tidied.
    /// </remarks>
    public static (float X, float Y) Crosshair(
        int screenWidth,
        int screenHeight,
        float spriteWidth,
        float spriteHeight) =>
        ((screenWidth * 0.5f) - (int)(spriteWidth * 0.5f),
         (screenHeight * 0.5f) - (int)(spriteHeight * 0.5f));

    /// <summary>Where an interaction sprite's top-left corner is drawn.</summary>
    /// <param name="screenWidth">The back buffer width.</param>
    /// <param name="screenHeight">The back buffer height.</param>
    /// <param name="spriteWidth">The native sprite width.</param>
    /// <param name="spriteHeight">The native sprite height.</param>
    /// <returns>The original's centred, seven-eighths-height placement.</returns>
    public static (float X, float Y) ActionIcon(
        int screenWidth,
        int screenHeight,
        int spriteWidth,
        int spriteHeight) =>
        ((screenWidth * 0.5f) - (spriteWidth / 2),
         (screenHeight * ActionIconYFraction) - (spriteHeight / 2));

    /// <summary>Converts the status screen's triangular phase into action-icon opacity.</summary>
    /// <param name="phase">The status phase in the range zero through two.</param>
    /// <returns>Opacity from 0.3 at either end to 1.0 at the midpoint.</returns>
    public static float ActionIconAlpha(float phase)
    {
        float triangle = phase >= 1f ? 2f - phase : phase;
        return (triangle * 0.7f) + 0.3f;
    }

    /// <summary>Which frame of the magazine gauge a round count selects.</summary>
    /// <param name="roundsInClip">What is left in the magazine.</param>
    /// <param name="roundsPerClip">The magazine's capacity.</param>
    /// <returns>The frame, 0 when full and <see cref="MagazineFrames"/> when empty.</returns>
    /// <remarks>
    /// An <b>integer</b> divide, floored at zero. A capacity of zero or less yields the empty frame
    /// rather than dividing — the original would fault, and no weapon that reaches this draw has one.
    /// </remarks>
    public static int MagazineFrame(int roundsInClip, int roundsPerClip)
    {
        if (roundsPerClip <= 0)
        {
            return MagazineFrames;
        }

        int frame = MagazineFrames - (MagazineFrames * roundsInClip / roundsPerClip);
        return frame < 0 ? 0 : frame > MagazineFrames ? MagazineFrames : frame;
    }

    /// <summary>How the ammunition count is spelled.</summary>
    /// <param name="rounds">What the count is.</param>
    /// <returns>The text.</returns>
    /// <remarks><c>"%-4d"</c> — left-aligned in four columns, so the readout does not jitter.</remarks>
    public static string AmmoText(int rounds) => rounds.ToString(System.Globalization.CultureInfo.InvariantCulture).PadRight(4);
}
