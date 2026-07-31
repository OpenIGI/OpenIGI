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
/// the player's hit-zone table (201/202/215/112/204) on it, which are ids in this same set. See
/// the original.
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
}
