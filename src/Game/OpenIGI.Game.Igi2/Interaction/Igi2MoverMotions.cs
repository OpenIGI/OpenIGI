using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi2.World;
using OpenIGI.Game.World;

namespace OpenIGI.Game.Igi2.Interaction;

/// <summary>Where the level's moving objects are drawn as they move, doors and lifts, and everything that hangs off them.</summary>
/// <remarks>
/// The scene was built with each one where the level placed it, so each placement of its tree keeps where it sat against it
/// then, and is drawn there against it as it stands now. Between ticks it is drawn part way from where the tick started it
/// (<c>tLastPosition</c>, <c>tLastOrientation</c>) to where it left it.
/// </remarks>
public sealed class Igi2MoverMotions : IPlacementMotion
{
    private readonly Dictionary<int, IIgi2Mover> _movers = [];
    private readonly Dictionary<int, IIgi2PartMover> _parts = [];

    /// <summary>Initializes a new instance of the <see cref="Igi2MoverMotions"/> class.</summary>
    /// <param name="movers">The level's moving objects.</param>
    /// <param name="parts">
    /// The level's objects that move parts of themselves, or <see langword="null"/> for none; a part one does not move moves with it,
    /// when it moves.
    /// </param>
    public Igi2MoverMotions(IEnumerable<IIgi2Mover> movers, IEnumerable<IIgi2PartMover>? parts = null)
    {
        ArgumentNullException.ThrowIfNull(movers);
        foreach (IIgi2Mover mover in movers)
        {
            _movers[mover.Id] = mover;
        }

        foreach (IIgi2PartMover part in parts ?? [])
        {
            _parts[part.Id] = part;
        }
    }

    /// <inheritdoc/>
    public bool TryGetTransform(WorldPlacement placement, float tickFraction, out Vector3d positionMetres, out Matrix3f orientation)
    {
        ArgumentNullException.ThrowIfNull(placement);
        if (placement.LevelId >= 0 && _parts.TryGetValue(placement.LevelId, out IIgi2PartMover? part))
        {
            if (part.TryMovePart(placement, out positionMetres, out orientation))
            {
                // A part is posed where its object stands at the tick; it is drawn on the object as the object is drawn, between
                // ticks, or a wheel or a gun trails the body it is on.
                if (_movers.TryGetValue(placement.LevelId, out IIgi2Mover? carrier) && tickFraction < 1f)
                {
                    Matrix3f standing = Blend(carrier.CurrentOrientation, carrier.CurrentOrientation, 1f);
                    Vector3d local = standing.Transform(WorldScene.ToUnits(positionMetres) - carrier.CurrentPosition);
                    Matrix3f localOrientation = standing.Transposed() * orientation;
                    double f = Math.Clamp(tickFraction, 0f, 1f);
                    Vector3d from = carrier.LastPosition, to = carrier.CurrentPosition;
                    Vector3d between = new(
                        from.X + ((to.X - from.X) * f), from.Y + ((to.Y - from.Y) * f), from.Z + ((to.Z - from.Z) * f));
                    Matrix3f blended = Blend(carrier.LastOrientation, carrier.CurrentOrientation, (float)f);
                    orientation = blended * localOrientation;
                    positionMetres = WorldScene.ToMetres(blended.Transposed().Transform(local) + between);
                }

                return true;
            }

            if (!_movers.ContainsKey(placement.LevelId))
            {
                return false;
            }
        }

        if (placement.LevelId < 0 || !_movers.TryGetValue(placement.LevelId, out IIgi2Mover? mover))
        {
            positionMetres = default;
            orientation = default;
            return false;
        }

        Igi2Pose built = mover.Built;
        double t = Math.Clamp(tickFraction, 0f, 1f);
        Vector3d last = mover.LastPosition, now = mover.CurrentPosition;
        Vector3d at = new(last.X + ((now.X - last.X) * t), last.Y + ((now.Y - last.Y) * t), last.Z + ((now.Z - last.Z) * t));
        Matrix3f turned = Blend(mover.LastOrientation, mover.CurrentOrientation, (float)t);

        // The placement against the mover as built, then that against the mover as it stands. A frame turns a vector as
        // its transpose's Transform does.
        Matrix3f start = built.ToMatrix();
        Vector3d offset = start.Transform(WorldScene.ToUnits(placement.Position) - built.Position);
        orientation = turned * (start.Transposed() * placement.Orientation);
        positionMetres = WorldScene.ToMetres(turned.Transposed().Transform(offset) + at);
        return true;
    }

    private static Matrix3f Blend(IReadOnlyList<float> from, IReadOnlyList<float> to, float t)
    {
        float Mix(int i) => from[i] + ((to[i] - from[i]) * t);
        return new Matrix3f(Mix(0), Mix(1), Mix(2), Mix(3), Mix(4), Mix(5), Mix(6), Mix(7), Mix(8));
    }
}
