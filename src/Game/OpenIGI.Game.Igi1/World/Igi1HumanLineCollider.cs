using OpenIGI.Core.Numerics;
using OpenIGI.Game.Physics;
using OpenIGI.Game.Player;

namespace OpenIGI.Game.Igi1.World;

/// <summary>A Project I.G.I. human as a world's lines and sight meet it: its hit test, answering the world's queries itself.</summary>
/// <remarks>
/// <para>
/// The world's line trace visits the humans along it (<c>World_TraceLines</c>, <c>0x4CC2A0</c>), and Project I.G.I.'s human
/// answers with its own handler (<c>0x45F500</c>): the evaluate first, then its body. This is that answer for a world that
/// is not the human's own -- another game's, whose rounds and sight have to meet this game's player -- given the body as
/// its hit test (<see cref="HumanHitTest"/>) casts against it, not its posed mesh.
/// </para>
/// <para>
/// The world says which query group the human is in and which of its materials a human is, so the answer reads as the
/// world's own humans do: a line going into the body enters that material, one coming out of it leaves it.
/// </para>
/// </remarks>
/// <param name="human">The human.</param>
/// <param name="group">The query group the world keeps its humans in.</param>
/// <param name="material">The world's material row for a human.</param>
public sealed class Igi1HumanLineCollider(HumanTask human, uint group, int material) : IWorldLineCollider
{
    private readonly HumanTask _human = human ?? throw new ArgumentNullException(nameof(human));

    /// <inheritdoc/>
    public object Owner => _human;

    /// <inheritdoc/>
    public object? Handler => _human;

    /// <inheritdoc/>
    public uint Group => group;

    /// <inheritdoc/>
    public void NextZ(WorldLineTrace trace)
    {
    }

    /// <inheritdoc/>
    public void TraceLines(WorldLineTrace trace)
    {
        ArgumentNullException.ThrowIfNull(trace);
        if (Stands() is not { } feet)
        {
            return;
        }

        for (int i = 0; i < trace.Count; i++)
        {
            Vector3d start = trace.Starts[i];
            Vector3d end = trace.Ends[i];
            Vector3d line = end - start;
            double length = line.Length;
            bool leaving = Inside(start, feet);
            HumanHit met;
            if (!leaving)
            {
                if (!HumanHitTest.TryCast(start, end, feet, Facing, pose: null, out met))
                {
                    continue;
                }
            }
            else if (Inside(end, feet) || !HumanHitTest.TryCast(end, start, feet, Facing, pose: null, out met))
            {
                // A line that never leaves the body crosses none of its faces.
                continue;
            }
            else
            {
                // Where the line comes out is where the same line run backwards goes in.
                met = met with { Distance = length - met.Distance };
            }

            WorldIntersection hit = trace.Intersections[i];
            if (hit.IsHit && !(met.Distance < hit.Distance))
            {
                continue;
            }

            Vector3f back = length > 0.0 ? new Vector3f((float)(-line.X / length), (float)(-line.Y / length), (float)(-line.Z / length)) : default;
            hit.IsHit = true;
            hit.HitObject = this;
            hit.Position = met.Position;
            hit.LocalPosition = new Vector3f((float)(met.Position.X - feet.X), (float)(met.Position.Y - feet.Y), (float)(met.Position.Z - feet.Z));
            // A face looks out of the body: back along the line where it goes in, along it where it comes out.
            Vector3f normal = leaving ? new Vector3f(-back.X, -back.Y, -back.Z) : back;
            hit.Normal = normal;
            hit.LocalNormal = normal;
            hit.Distance = met.Distance;
            hit.EnterMaterial = leaving ? null : material;
            hit.LeaveMaterial = leaving ? material : null;
            hit.IsDoubleFace = false;
            hit.Sector = met.Bone;
            hit.Light = 1f;
        }
    }

    /// <inheritdoc/>
    public void LineOfSight(WorldLineOfSight sight)
    {
        ArgumentNullException.ThrowIfNull(sight);
        if (Stands() is { } feet && HumanHitTest.TryCast(sight.Start, sight.End, feet, Facing, pose: null, out _))
        {
            sight.IsHit = true;
            sight.Transparency = 0f;
            sight.Material = material;
        }
    }

    // Whether a point is within the stand-in's body, the cylinder HumanHitTest casts against.
    private static bool Inside(Vector3d point, Vector3d feet)
    {
        double x = point.X - feet.X;
        double y = point.Y - feet.Y;
        return (x * x) + (y * y) < HumanHitTest.BodyRadiusInUnits * HumanHitTest.BodyRadiusInUnits
            && point.Z > feet.Z
            && point.Z < feet.Z + HumanHitTest.BodyHeightInUnits;
    }

    // The way the stand-in faces: it is round, so only its zones turn with it.
    private static Vector3f Facing => new(0f, 1f, 0f);

    // Where the human stands, while it lives in the level.
    private Vector3d? Stands() => _human is { IsDead: false, Body: { IsActive: true } body } ? body.Feet : null;
}
