using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi2.World;
using OpenIGI.Game.World;

namespace OpenIGI.Game.Igi2.Interaction;

/// <summary>Where the level's doors are drawn as they swing and slide, and everything that hangs off them.</summary>
/// <remarks>
/// The scene was built with each door shut at its start, so each placement of a door's tree keeps where it sat against the door
/// then, and is drawn there against the door as it stands now. Between ticks the door is drawn part way from where the tick
/// started it (<c>tLastPosition</c>, <c>tLastOrientation</c>) to where it left it.
/// </remarks>
public sealed class Igi2DoorMotions : IPlacementMotion
{
    private readonly Dictionary<int, (Igi2DoorTask Door, Igi2Pose Built)> _doors = [];

    /// <summary>Initializes a new instance of the <see cref="Igi2DoorMotions"/> class.</summary>
    /// <param name="doors">The level's doors.</param>
    public Igi2DoorMotions(IEnumerable<Igi2DoorTask> doors)
    {
        ArgumentNullException.ThrowIfNull(doors);
        foreach (Igi2DoorTask door in doors)
        {
            _doors[door.Id] = (door, Igi2Pose.FromAngles(door.Position, door.Orientation));
        }
    }

    /// <inheritdoc/>
    public bool TryGetTransform(WorldPlacement placement, float tickFraction, out Vector3d positionMetres, out Matrix3f orientation)
    {
        ArgumentNullException.ThrowIfNull(placement);
        if (placement.LevelId < 0 || !_doors.TryGetValue(placement.LevelId, out (Igi2DoorTask Door, Igi2Pose Built) entry))
        {
            positionMetres = default;
            orientation = default;
            return false;
        }

        (Igi2DoorTask door, Igi2Pose built) = entry;
        double t = Math.Clamp(tickFraction, 0f, 1f);
        Vector3d last = door.LastPosition, now = door.CurrentPosition;
        Vector3d at = new(last.X + ((now.X - last.X) * t), last.Y + ((now.Y - last.Y) * t), last.Z + ((now.Z - last.Z) * t));
        Matrix3f turned = Blend(door.LastOrientation, door.CurrentOrientation, (float)t);

        // The placement against the door as built, then that against the door as it stands. A frame turns a vector as
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
