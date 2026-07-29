namespace OpenIGI.Game.Weapons;

/// <summary>
/// One surface a round met, and the mark it leaves.
/// </summary>
/// <remarks>
/// <para>
/// The bullet resolution records up to <see cref="GunShotBallistics.MaximumRecordedImpacts"/> of these
/// as it works through a round's range, then spawns a decal for each once the round has stopped.
/// </para>
/// <para>
/// <b>The bullet does not choose the mark.</b> <c>0x4083C0</c> is a thin dispatcher: it packages the
/// impact and sends it to a message slot on the <i>material's</i> own task type, so each surface
/// decides what a hit on it looks like. A round through glass and into brick leaves two different marks
/// without the shot knowing anything about either.
/// </para>
/// </remarks>
/// <param name="MaterialTypeId">
/// The struck surface's task type, which selects the handler. Taken from the hit object's own record —
/// a different field for a human than for scenery.
/// </param>
/// <param name="Surface">The object that was struck.</param>
/// <param name="IsEntry">
/// Whether this is an entry mark rather than an exit one; see <see cref="ClassifyEntry"/>.
/// </param>
public readonly record struct BulletImpact(int MaterialTypeId, int Surface, bool IsEntry)
{
    /// <summary>
    /// Decides whether an impact reads as an entry or an exit.
    /// </summary>
    /// <param name="index">Which impact this is, in the order they were recorded.</param>
    /// <param name="impactCount">How many the round recorded in total.</param>
    /// <param name="hasDeflected">Whether the round ricocheted at any point.</param>
    /// <param name="normalDotTravel">
    /// The surface normal dotted with the round's travel direction, for the <i>last</i> impact only.
    /// </param>
    /// <returns><see langword="true"/> for an entry mark.</returns>
    /// <remarks>
    /// <para>
    /// Three cases, in the original's order:
    /// </para>
    /// <list type="number">
    /// <item><description>A round that <b>deflected</b> leaves entry marks throughout — a ricochet never
    /// produces an exit wound, however many surfaces it went on to meet.</description></item>
    /// <item><description>Every impact <b>but the last</b> is an entry, because the round demonstrably
    /// carried on past it.</description></item>
    /// <item><description>The <b>last</b> impact is decided by the sign of the dot product: a normal
    /// facing the same way as the travel means the round was leaving, so it is an exit.</description></item>
    /// </list>
    /// <para>
    /// The sign test is <c>&gt; 0</c>, so a normal exactly perpendicular to the travel counts as an
    /// entry.
    /// </para>
    /// </remarks>
    public static bool ClassifyEntry(int index, int impactCount, bool hasDeflected, float normalDotTravel)
    {
        if (hasDeflected)
        {
            return true;
        }

        if (index < impactCount - 1)
        {
            return true;
        }

        return normalDotTravel <= 0f;
    }
}
