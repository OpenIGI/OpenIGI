using OpenIGI.Core.Numerics;
using OpenIGI.Game.World;

namespace OpenIGI.Game.Player;

/// <summary>What a sweep did to the move it was given.</summary>
/// <param name="Destination">Where the body ends up, in world units.</param>
/// <param name="TouchedWall">Whether a wall was met — the flag <c>0x8</c> the original raises.</param>
/// <param name="Checks">How many casts it took; <b>0x461A70</b> gives up after ten.</param>
public readonly record struct HumanWallSweepResult(Vector3d Destination, bool TouchedWall, int Checks)
{
    /// <summary>Gets whether the high probe clamped the human below an obstruction.</summary>
    public bool StoppedByOverhang { get; init; }

    /// <summary>Gets whether the high-probe branch also cleared horizontal airborne motion.</summary>
    public bool CancelledHorizontalMotion { get; init; }
}

/// <summary>
/// Slides a human along the walls in its way — <c>0x461A70</c>, transcribed.
/// </summary>
/// <remarks>
/// <para>
/// Called from the move-and-collide (<c>0x4614B0</c>) with the body's start and its intended
/// destination, and it hands back the destination it could actually reach. Three rays go out at the
/// heights <see cref="HumanWallProbe"/> names, and the loop repeats — up to ten times — because sliding
/// off one wall can put you into another.
/// </para>
/// <para>
/// <b>A wall is a surface within about 5.7° of vertical</b> — <c>|normal.z| &lt; 0.1</c>. Anything
/// flatter is the ground query's business, not this one's. And a wall's normal has its <b>Z flattened
/// to zero and the remainder renormalised</b> before it is used, so a body slides horizontally along a
/// leaning wall and is never pushed up or down it. That one detail is most of what stops the original's
/// player climbing scenery.
/// </para>
/// <para>
/// <b>Which rays are live depends on whether you are standing.</b> On the ground the ankle-height ray
/// is skipped and only the middle and high ones are cast, so you walk over kerbs instead of into them;
/// airborne, all three are live. See <see cref="HumanWallProbe.ActiveProbes"/>.
/// </para>
/// <para>
/// The same cast also handles the two airborne cases that are not walls: the low probe snaps a falling
/// human to an almost perfectly upward-facing object surface, and the high probe keeps a jump below an
/// overhang. Those use the raw hit from their respective probes; neither is a substitute for the normal
/// wall slide.
/// </para>
/// </remarks>
public static class HumanWallSweep
{
    /// <summary>How far a body is held off a wall it slid against, in world units.</summary>
    /// <remarks>
    /// <c>10.24</c> — two and a half millimetres. It is added to the ray's length so a wall is seen
    /// slightly before it is reached, and again along the normal after a slide so the body ends up
    /// clear of the surface rather than exactly on it.
    /// </remarks>
    public const double SkinInUnits = HumanWallProbe.SkinInUnits;

    /// <summary>Sweeps a move against the world.</summary>
    /// <param name="start">Where the body is, in world units.</param>
    /// <param name="destination">Where it means to go.</param>
    /// <param name="grounded">Whether it is standing on something.</param>
    /// <param name="crouching">Whether it is crouched, which lowers two of the three rays.</param>
    /// <param name="cast">The world ray cast.</param>
    /// <param name="footOffset">
    /// How far the body's origin sits above its feet. Zero for <see cref="PlayerBody"/>, whose position
    /// <i>is</i> its feet; the original carries a model-dependent offset here.
    /// </param>
    /// <returns>Where the body actually reaches.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="cast"/> is <see langword="null"/>.</exception>
    public static HumanWallSweepResult Sweep(
        Vector3d start,
        Vector3d destination,
        bool grounded,
        bool crouching,
        WorldRayCast cast,
        double footOffset = 0.0)
    {
        ArgumentNullException.ThrowIfNull(cast);

        (double low, double middle, double high) = HumanWallProbe.ProbeHeights(crouching);
        double[] heights = [low - footOffset, middle - footOffset, high - footOffset];
        (int first, int count) = HumanWallProbe.ActiveProbes(grounded);

        bool touchedWall = false;
        bool stoppedByOverhang = false;
        bool cancelledHorizontalMotion = false;
        int checks = 0;

        while (true)
        {
            Vector3d motion = new(
                destination.X - start.X,
                destination.Y - start.Y,
                destination.Z - start.Z);

            double length = Math.Sqrt((motion.X * motion.X) + (motion.Y * motion.Y) + (motion.Z * motion.Z));
            if (length <= 0.0)
            {
                break;
            }

            checks++;

            // The rays reach a skin further than the move does, so a wall is met before it is touched.
            double reach = length + SkinInUnits;
            Vector3d direction = new(motion.X / length, motion.Y / length, motion.Z / length);
            Vector3d extended = new(direction.X * reach, direction.Y * reach, direction.Z * reach);

            bool haveWall = false;
            double nearest = double.MaxValue;
            Vector3d wallPoint = default;
            Vector3f wallNormal = default;
            WorldRayHit lowHit = default;
            WorldRayHit highHit = default;
            bool hitLow = false;
            bool hitHigh = false;

            for (int i = first; i < first + count; i++)
            {
                Vector3d origin = new(start.X, start.Y, start.Z + heights[i]);
                Vector3d end = new(origin.X + extended.X, origin.Y + extended.Y, origin.Z + extended.Z);

                if (!cast(origin, end, out WorldRayHit probe))
                {
                    continue;
                }

                if (i == 0)
                {
                    lowHit = probe;
                    hitLow = true;
                }
                else if (i == 2)
                {
                    highHit = probe;
                    hitHigh = true;
                }

                if (!HumanWallProbe.IsWall(probe.Normal))
                {
                    continue;
                }

                if (probe.Distance >= nearest)
                {
                    continue;
                }

                nearest = probe.Distance;
                wallPoint = probe.Position;
                wallNormal = HumanWallProbe.Flatten(probe.Normal);
                haveWall = true;
            }

            // Airborne low-probe landing: 0x461A70 accepts only a near-perfect upward normal. This
            // catches a thin object floor crossed during a large fall, which a later endpoint-only
            // ground query has already passed through.
            if (!grounded
                && !haveWall
                && hitLow
                && HumanWallProbe.UnitZ(lowHit.Normal) > 0.99989998f)
            {
                destination = new Vector3d(
                    lowHit.Position.X,
                    lowHit.Position.Y,
                    lowHit.Position.Z - heights[0]);
            }

            // The high probe is the head-bump pass. A surface directly above the human places the
            // feet one skin width below it; a non-ceiling contact also takes the hit's horizontal
            // point, exactly as the retail branch does before it clears the motion accumulators.
            if (!grounded && !haveWall && hitHigh)
            {
                cancelledHorizontalMotion = HumanWallProbe.UnitZ(highHit.Normal) >= -0.99989998f;
                if (cancelledHorizontalMotion)
                {
                    destination = new Vector3d(highHit.Position.X, highHit.Position.Y, destination.Z);
                }

                destination = new Vector3d(
                    destination.X,
                    destination.Y,
                    highHit.Position.Z - heights[2] - SkinInUnits);
                stoppedByOverhang = true;
                break;
            }

            if (!haveWall)
            {
                break;
            }

            // Only a wall you are moving INTO stops you. Brushing one on the way out is not a collision,
            // and treating it as one would stick the body to every surface it ever touched.
            double approach = (motion.X * wallNormal.X) + (motion.Y * wallNormal.Y) + (motion.Z * wallNormal.Z);
            if (approach >= 0.0)
            {
                break;
            }

            // Slide already re-anchors at the hit and steps out along the normal by the skin, which is
            // the original's `*a3 = v43 * 10.24 + *a3` after its projection. Pushing again here would
            // hold the body a centimetre off every wall and let it squeeze through thin gaps.
            destination = HumanWallProbe.Slide(destination, wallPoint, wallNormal);
            touchedWall = true;

            // Ten casts and it gives up - and when it does, the body does not move at all. A corner
            // tight enough to need an eleventh is one the original refuses to enter.
            if (checks > HumanWallProbe.MaximumIterations)
            {
                return new HumanWallSweepResult(start, touchedWall, checks)
                {
                    StoppedByOverhang = stoppedByOverhang,
                    CancelledHorizontalMotion = cancelledHorizontalMotion,
                };
            }
        }

        return new HumanWallSweepResult(destination, touchedWall, checks)
        {
            StoppedByOverhang = stoppedByOverhang,
            CancelledHorizontalMotion = cancelledHorizontalMotion,
        };
    }
}
