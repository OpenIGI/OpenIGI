namespace OpenIGI.Game.Player;

/// <summary>
/// What the player's <b>third-person body</b> is made of — the rig <c>Human+104</c> that the first-person
/// view never shows but the death camera pulls back to reveal.
/// </summary>
/// <remarks>
/// <para>
/// The player is not only the arms. Its constructor (<c>0x410370</c>) builds a full body BoneObject at
/// <c>Human+104</c> and plays animation <b>2</b> on it (<c>sub_489EA0(player, 2, 0, 0.0)</c> forwards to
/// <c>sub_4D61D0(Human+104, 0, 2, 0, 0.0)</c>), exactly as a standing soldier does. Unlike a soldier the
/// <c>HumanPlayer</c> task declares no <c>Bone Heirachy</c> — the skeleton is not a level-data decision —
/// so the body uses <b>skeleton 0</b> (<c>anims/000.iff</c>), and its mesh is the ordinary <c>Model</c>
/// the level's <c>HumanPlayer</c> declares (<c>spawn.Model</c>), resolved by name in the chained
/// <c>Human</c> handler like any other character's.
/// </para>
/// <para>
/// So the body is drawable with the same machinery a soldier's is — a <c>SoldierView</c> over the
/// <c>000.iff</c> set, standing in clip 2 — and the death state (<c>0x40E560</c>) plays a fall clip from
/// the player's hit-zone table (201/202/215/112/204) on it, which are ids in this same set.
/// </para>
/// </remarks>
public static class HumanPlayerBody
{
    /// <summary>The skeleton the player's third-person body uses — <c>anims/000.iff</c>.</summary>
    /// <remarks>The <c>HumanPlayer</c> task carries no skeleton parameter; the constructor uses this one.</remarks>
    public const int Skeleton = 0;

    /// <summary>The clip the body stands in — the literal <c>2</c> the constructor plays on <c>Human+104</c>.</summary>
    public const int StandAnimation = 2;

    /// <summary>
    /// How far the death camera sits from the body, in engine units — <c>FindStartPosition</c>'s ideal.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>HumanCamera_DeathCameraInit</c> (<c>0x4d51d0</c>) writes <c>+292 = 10240</c> and
    /// <c>FindStartPosition</c> (<c>0x4d36a0</c>) takes its ideal ray length as <c>+292 + 2048 = 12288</c>
    /// units — three metres — casting a fan of elevation and azimuth guesses to find the first that clears
    /// the world. The full search is the refinement; the distance it aims for is this.
    /// </para>
    /// <para>
    /// Read from I.G.I. 2's named camera (the sequel shares this engine); I.G.I. 1's own death camera is
    /// the same machinery, and the distance is the value to reproduce.
    /// </para>
    /// </remarks>
    public const double DeathCameraDistanceInUnits = 12288.0;

    /// <summary>The death camera's horizontal field of view, in radians — <c>tan⁻¹(1)·2 = 90°</c>.</summary>
    /// <remarks><c>DeathCameraInit</c> sets the half-tangent to <c>tan(45°) = 1</c>, the same as the game view.</remarks>
    public const float DeathCameraFieldOfView = 1.5707964f;

    /// <summary>How far the death camera is pulled in from what it aims for, in engine units — half a metre.</summary>
    /// <remarks>
    /// <c>FindStartPosition</c> stores <c>maxDistance − 2048</c> as the camera distance, so the eye always
    /// sits half a metre short of the ideal or of whatever wall it found — never flush against geometry.
    /// </remarks>
    public const double DeathCameraPullInInUnits = 2048.0;

    /// <summary>The elevation angles the death camera tries, in radians, <b>in preference order</b>.</summary>
    /// <remarks>
    /// <para>
    /// The eight floats of <c>HumanCamera_avAlphaGuesses</c>, read from I.G.I. 2's data (this camera is the
    /// shared engine's; I.G.I. 1's own binary does not carry the table, so its death camera is a different,
    /// simpler mechanism — <c>human+1264</c> mode 5 — whose vantage logic is not yet traced). The search
    /// takes the <b>first</b> angle whose ray clears the world, so the order is the preference: a little
    /// above the body first (~33°, ~11°, level), then higher (~56°, ~78°), then below (~−22°, −44°, −67°).
    /// </para>
    /// </remarks>
    public static ReadOnlySpan<float> DeathCameraElevationGuesses =>
    [
        0.5825035f,   // 0x3F151EF6 — ~33.4 degrees, the first choice
        0.19418449f,  // 0x3E46D3F2 — ~11.1
        0f,           // level with the body
        0.9708392f,   // 0x3F7888EF — ~55.6
        1.3591747f,   // 0x3FADF974 — ~77.9
        -0.38836897f, // 0xBEC6D3F2 — ~-22.2
        -0.7767379f,  // 0xBF46D3F2 — ~-44.5
        -1.1650069f,  // 0xBF951EF6 — ~-66.7
    ];

    /// <summary>Where the death camera's azimuth sweep starts, in radians — <c>−π/2</c>.</summary>
    /// <remarks><c>FindStartPosition</c> starts <c>vGamma</c> here and steps it, relative to the body's frame.</remarks>
    public const float DeathCameraAzimuthStart = -1.5707964f;

    /// <summary>How much the azimuth sweep steps each try, in radians — <c>π/4</c>.</summary>
    public const float DeathCameraAzimuthStep = 0.78539819f;

    /// <summary>How many azimuths the death camera tries per elevation — eight, a full turn less one step.</summary>
    /// <remarks>The <c>a1 != 0 ? 1 : 8</c> count; the death camera passes <c>0</c>, so it sweeps all eight.</remarks>
    public const int DeathCameraAzimuthCount = 8;

    /// <summary>The distance the death camera rests at, in engine units — <c>2.5 m</c>, <c>DeathCameraInit</c>'s <c>+292</c>.</summary>
    /// <remarks>
    /// The orbit eases the distance back out to this whenever the way is clear; the search's ideal ray is
    /// this plus the half-metre pull-in, so a clear vantage lands exactly here.
    /// </remarks>
    public const double DeathCameraRestingDistanceInUnits =
        DeathCameraDistanceInUnits - DeathCameraPullInInUnits;

    /// <summary>The most the orbit turns in one frame, in radians — <c>6°</c>, the clamp in <c>0x4d5220</c>.</summary>
    public const float DeathCameraOrbitStepLimit = 0.10471976f;

    /// <summary>The lowest the orbit's elevation may fall, in radians — <c>−71.2°</c> (<c>0x4d5220</c>).</summary>
    public const float DeathCameraElevationMin = -1.2426745f;

    /// <summary>The highest the orbit's elevation may rise, in radians — <c>89.0°</c> (<c>0x4d5220</c>).</summary>
    public const float DeathCameraElevationMax = 1.5533431f;

    /// <summary>How fast the orbit eases its distance back toward the resting one, in units a frame.</summary>
    /// <remarks>The <c>136.53334</c> step <c>0x4d5220</c> adds (or subtracts) when the way is clear.</remarks>
    public const double DeathCameraDistanceEaseInUnits = 136.53334;
}
