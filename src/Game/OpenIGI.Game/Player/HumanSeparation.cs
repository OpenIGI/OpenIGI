using OpenIGI.Core.Numerics;

namespace OpenIGI.Game.Player;

/// <summary>
/// Human-versus-human collision — the third and simplest of the three passes the movement integrator
/// runs.
/// </summary>
/// <remarks>
/// <para>
/// From <c>0x462BA0</c>. It is not a push-out: for every other human close enough, if the move would
/// end up <em>nearer</em> than it started, the move is <b>rejected outright</b> and the human stays
/// where it was. Bodies therefore block each other without ever being displaced, which is why two
/// guards in a doorway jam rather than squeeze past.
/// </para>
/// <para>
/// The test is a cylinder, not a sphere: vertical separation is compared against
/// <see cref="HeightInUnits"/> and the <em>horizontal</em> distance against <see cref="RadiusInUnits"/>.
/// </para>
/// </remarks>
public static class HumanSeparation
{
    /// <summary>The half-height of the cylinder, in engine units — 1.8 m.</summary>
    public const double HeightInUnits = 7372.8;

    /// <summary>The radius of the cylinder, in engine units — 0.55 m.</summary>
    public const double RadiusInUnits = 2252.8;

    /// <summary>Resolves a move against one other human.</summary>
    /// <param name="start">Where the mover is now.</param>
    /// <param name="end">Where it wants to be.</param>
    /// <param name="other">The other human's position.</param>
    /// <returns>
    /// <paramref name="end"/> when the move is allowed, or <paramref name="start"/> when the other
    /// human blocks it.
    /// </returns>
    /// <remarks>
    /// Only the horizontal distance decides "closer": the original recomputes the 2D length at the
    /// start position and compares it against the one at the end.
    /// </remarks>
    public static Vector3d Resolve(Vector3d start, Vector3d end, Vector3d other)
    {
        double verticalGap = Math.Abs(end.Z - other.Z);
        if (verticalGap >= HeightInUnits)
        {
            return end;
        }

        double endDistance = Horizontal(end, other);
        if (endDistance >= RadiusInUnits)
        {
            return end;
        }

        // Inside the cylinder. The move is only refused when it closes the gap, so a human already
        // overlapping can still walk out — without that, two bodies that spawn touching would lock.
        return Horizontal(start, other) > endDistance ? start : end;
    }

    /// <summary>Resolves a move against every other human.</summary>
    /// <param name="start">Where the mover is now.</param>
    /// <param name="end">Where it wants to be.</param>
    /// <param name="others">The other humans' positions; the mover's own must not be included.</param>
    /// <returns>The permitted end position.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="others"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// The original walks the whole list and lets each blocker overwrite the end position in turn, so
    /// one refusal is enough — later humans are tested against the already-refused position.
    /// </remarks>
    public static Vector3d Resolve(Vector3d start, Vector3d end, IEnumerable<Vector3d> others)
    {
        ArgumentNullException.ThrowIfNull(others);
        Vector3d result = end;
        foreach (Vector3d other in others)
        {
            result = Resolve(start, result, other);
        }

        return result;
    }

    private static double Horizontal(Vector3d from, Vector3d to)
    {
        double dx = from.X - to.X;
        double dy = from.Y - to.Y;
        return Math.Sqrt((dx * dx) + (dy * dy));
    }
}
