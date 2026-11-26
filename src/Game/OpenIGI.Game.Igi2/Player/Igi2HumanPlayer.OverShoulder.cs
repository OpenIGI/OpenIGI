using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi2.Collision;
using OpenIGI.Game.Physics;
using OpenIGI.Game.Player;

namespace OpenIGI.Game.Igi2.Player;

/// <content>
/// The player watched over the shoulder (<see cref="ITitleOverShoulder"/>): its weapon held on the body, and its rounds turned onto
/// what the crosshair is over. <b>Not retail</b>: the remaster's third-person view. Nothing here is saved, and with no ray the
/// fire frame is the original's to the bit.
/// </content>
public sealed partial class Igi2HumanPlayer
{
    /// <summary>How far ahead the crosshair's ray looks for what it is over, in world units: three hundred metres.</summary>
    public const double AimConvergenceReachUnits = 300.0 * 4096.0;

    private readonly WorldLineTrace _convergenceTrace = new(1);
    private Func<object, bool>? _notSelf;

    /// <summary>
    /// Gets or sets a value indicating whether the player's body is drawn in its stead: the weapon is then the one in the body's
    /// hand, as for a human seen from behind (<see cref="Humans.Igi2Human.IsThirdPerson"/>, which is the original's and is left alone).
    /// </summary>
    public bool IsOverShoulder { get; set; }

    /// <summary>
    /// Gets or sets the crosshair's ray over the shoulder, as an offset from the view's eye and a direction, or <see langword="null"/>
    /// in first person, which leaves the rounds on the view's own forward.
    /// </summary>
    public (Vector3d EyeOffset, Vector3f Direction)? AimConvergence { get; set; }

    /// <summary>Gets or sets what the crosshair's ray is traced through, or <see langword="null"/> to converge on nothing.</summary>
    internal Igi2CollisionWorld? AimWorld { get; set; }

    /// <summary>
    /// Turns a fire frame onto what the crosshair's ray meets first -- a wall, the ground, a body other than the player's own -- as a
    /// round would meet it; a ray meeting nothing keeps its far end, where the turn is no turn at all.
    /// </summary>
    /// <param name="o">The view's orientation, row by row, whose columns are its right, forward and up axes; turned in place.</param>
    private void ConvergeOnCrosshair(float[] o)
    {
        if (AimConvergence is not { } aim || AimWorld is not { } world)
        {
            return;
        }

        Vector3d eye = View.Position;
        Vector3d origin = eye + aim.EyeOffset;
        Vector3f d = aim.Direction;
        Vector3d end = new(
            origin.X + (d.X * AimConvergenceReachUnits),
            origin.Y + (d.Y * AimConvergenceReachUnits),
            origin.Z + (d.Z * AimConvergenceReachUnits));

        _convergenceTrace.Starts[0] = origin;
        _convergenceTrace.Ends[0] = end;
        _convergenceTrace.Includes = _notSelf ??= owner => !ReferenceEquals(owner, this);
        WorldIntersection hit = _convergenceTrace.Intersections[0];
        hit.IsHit = false;
        hit.HitObject = null;
        world.TraceLines(_convergenceTrace, Igi2CollisionWorld.EveryTypeGroup | Igi2CollisionWorld.HumanGroup | Igi2CollisionWorld.TerrainGroup);
        Vector3d target = hit.IsHit ? hit.Position : end;

        Vector3f right = new(o[0], o[3], o[6]);
        Vector3f forward = new(o[1], o[4], o[7]);
        Vector3f up = new(o[2], o[5], o[8]);

        // The shared turn rebuilds its up as right across forward; the view's own may be the other way round, and keeps its side.
        float handedness = Vector3f.Dot(Vector3f.Cross(right, forward), up) < 0f ? -1f : 1f;
        if (!AimBasis.Converge(eye, target, ref forward, ref right, ref up))
        {
            return;
        }

        up *= handedness;
        (o[0], o[3], o[6]) = (right.X, right.Y, right.Z);
        (o[1], o[4], o[7]) = (forward.X, forward.Y, forward.Z);
        (o[2], o[5], o[8]) = (up.X, up.Y, up.Z);
    }
}
