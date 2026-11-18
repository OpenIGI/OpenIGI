using OpenIGI.Core.Numerics;

namespace OpenIGI.Game.World;

/// <summary>
/// A camera key's turn written as the three angles nearest the ones it had: the game blends each angle on its own between keys
/// (<see cref="CutsceneCameraMath.Hermite"/>), so a turn of a few degrees written as 350 turns the camera the long way round.
/// </summary>
/// <remarks>
/// A piloted camera keeps its rotation's winding when the view is written back: of every set of angles that makes the same turn -- each angle a whole turn more or less, and the second
/// Euler solution -- the one nearest the angles given.
/// </remarks>
public static class CameraAngles
{
    private const double FullTurn = 2 * Math.PI;

    // How close two turns' matrices must be for a candidate set of angles to count as the same turn.
    private const float SameTurn = 1e-4f;

    /// <summary>The angles that make a turn, nearest some angles.</summary>
    /// <param name="orientation">The turn, in the engine's storage.</param>
    /// <param name="near">The angles to stay near: the key's own, or the key before's.</param>
    /// <returns>Alpha, beta and gamma, in radians.</returns>
    public static Vector3f Closest(Matrix3f orientation, Vector3f near)
    {
        Vector3f first = orientation.ToEngineEulerAngles();
        Vector3f best = Wound(first, near);
        double bestDistance = TurnBetween(best, near);
        foreach (Vector3f other in Alternatives(first))
        {
            if (!Same(Matrix3f.FromEngineEulerAngles(other.X, other.Y, other.Z), orientation))
            {
                continue;
            }

            Vector3f wound = Wound(other, near);
            double distance = TurnBetween(wound, near);
            if (distance < bestDistance)
            {
                (best, bestDistance) = (wound, distance);
            }
        }

        return best;
    }

    /// <summary>
    /// How far a view is rolled about the way it looks, from a view looking the same way with no roll: what a key keeps of its
    /// own while a camera that cannot roll (the editor's) is flown in its place.
    /// </summary>
    /// <param name="view">The view, as a camera takes it (rows right, up, forward).</param>
    /// <param name="level">A view looking the same way, unrolled.</param>
    /// <returns>The roll, in radians.</returns>
    public static float Roll(Matrix3f view, Matrix3f level)
    {
        Vector3f forward = level.Row3;
        Vector3f across = Vector3f.Cross(forward, level.Row2);
        return MathF.Atan2(Vector3f.Dot(across, view.Row2), Vector3f.Dot(level.Row2, view.Row2));
    }

    /// <summary>An unrolled view rolled about the way it looks: <see cref="Roll"/> put back.</summary>
    /// <param name="level">The view, unrolled (rows right, up, forward).</param>
    /// <param name="roll">The roll, in radians.</param>
    /// <returns>The rolled view.</returns>
    public static Matrix3f WithRoll(Matrix3f level, float roll)
    {
        Vector3f forward = level.Row3;
        (float sin, float cos) = MathF.SinCos(roll);
        Vector3f up = (level.Row2 * cos) + (Vector3f.Cross(forward, level.Row2) * sin);
        Vector3f right = (level.Row1 * cos) + (Vector3f.Cross(forward, level.Row1) * sin);
        return Matrix3f.FromRows(right, up, forward);
    }

    // Each angle moved by whole turns to lie within half a turn of the one to stay near.
    private static Vector3f Wound(Vector3f angles, Vector3f near) =>
        new(Wind(angles.X, near.X), Wind(angles.Y, near.Y), Wind(angles.Z, near.Z));

    private static float Wind(float angle, float near) =>
        (float)(angle + (FullTurn * Math.Round((near - angle) / FullTurn)));

    // The second Euler solution of a turn, in the forms a three-axis turn takes; each is checked against the turn before use.
    private static IEnumerable<Vector3f> Alternatives(Vector3f a)
    {
        const float Half = MathF.PI;
        yield return new Vector3f(a.X + Half, Half - a.Y, a.Z + Half);
        yield return new Vector3f(a.X + Half, -Half - a.Y, a.Z + Half);
        yield return new Vector3f(a.X - Half, Half - a.Y, a.Z - Half);
    }

    // How far apart two sets of angles are: each angle's turn, summed.
    private static double TurnBetween(Vector3f a, Vector3f b) => Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y) + Math.Abs(a.Z - b.Z);

    private static bool Same(Matrix3f a, Matrix3f b) =>
        Near(a.Row1, b.Row1) && Near(a.Row2, b.Row2) && Near(a.Row3, b.Row3);

    private static bool Near(Vector3f a, Vector3f b) =>
        MathF.Abs(a.X - b.X) < SameTurn && MathF.Abs(a.Y - b.Y) < SameTurn && MathF.Abs(a.Z - b.Z) < SameTurn;
}
