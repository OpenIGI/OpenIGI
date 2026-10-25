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
}
