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
