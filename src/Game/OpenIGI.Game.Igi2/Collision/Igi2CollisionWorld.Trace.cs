using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;
using OpenIGI.Game.Physics;
using OpenIGI.Game.World;

namespace OpenIGI.Game.Igi2.Collision;

public sealed partial class Igi2CollisionWorld
{
    // How many times a trace steps past a human or an ignored owner it is not asked to meet before it gives up and reports clear.
    private const int TraceSkipLimit = 16;

    // How far past a skipped contact the next pass starts, in world units.
    private const double TraceSkipUnits = 1.0;

    /// <summary>
    /// Casts one line through the world as the other game's world trace asks it (<see cref="WorldTraceCast"/>), and reports the
    /// nearest contact it is asked to meet.
    /// </summary>
    /// <param name="request">The line, the kinds of collision it meets, and the owners it passes through.</param>
    /// <param name="hit">Receives the nearest contact; its material is this level's own row, which the other game maps to its own.</param>
    /// <returns><see langword="true"/> when the line met something before its end.</returns>
    /// <remarks>
    /// <para>
    /// The objects, spline segments and stands of trees answer as <see cref="WorldTraceHitKind.StaticObject"/>: the world keeps
    /// no record of which of them move, and a static object is the one that blocks wherever it is met. The ground answers as
    /// <see cref="WorldTraceHitKind.Terrain"/>, and a human -- a boned body, or anything else in <see cref="HumanGroup"/> -- as
    /// <see cref="WorldTraceHitKind.Human"/>, only when the request asks for humans.
    /// </para>
    /// <para>
    /// A contact the request does not want -- a human when it asks for none, or an owner it ignores -- is passed through: the line
    /// is cast again from just beyond it, as many as <see cref="TraceSkipLimit"/> times.
    /// </para>
    /// </remarks>
    public bool Trace(in WorldTraceRequest request, out WorldTraceHit hit)
    {
        hit = default;
        bool objects = (request.Mask & (WorldTraceMask.StaticObjects | WorldTraceMask.DynamicObjects)) != 0;
        bool ground = (request.Mask & WorldTraceMask.Terrain) != 0;
        bool humans = (request.Mask & WorldTraceMask.Humans) != 0;
        uint groups = (objects || humans ? EveryTypeGroup : 0u) | (ground ? TerrainGroup : 0u);
        double length = request.Length;
        if (groups == 0 || length <= 0.0)
        {
            return false;
        }

        Vector3d along = (request.End - request.Origin) * (1.0 / length);
        double travelled = 0.0;
        WorldLineTrace trace = new(1) { TracePortals = request.TracePortals };
        for (int pass = 0; pass <= TraceSkipLimit; pass++)
        {
            trace.Starts[0] = request.Origin + (along * travelled);
            trace.Ends[0] = request.End;
            TraceLines(trace, groups);
            WorldIntersection met = trace.Intersections[0];
            if (!met.IsHit)
            {
                return false;
            }

            object? owner = met.HitObject?.Owner;
            WorldTraceHitKind kind = KindOf(met.HitObject);
            bool wanted = kind switch
            {
                WorldTraceHitKind.Human => humans,
                WorldTraceHitKind.Terrain => ground,
                _ => objects,
            };
            if (wanted && owner is not null && (ReferenceEquals(owner, request.IgnoredOwner) || ReferenceEquals(owner, request.IgnoredSubject)))
            {
                wanted = false;
            }

            double distance = travelled + met.Distance;
            if (wanted)
            {
                hit = new WorldTraceHit(
                    met.Position,
                    met.Normal,
                    distance,
                    Math.Clamp(distance / length, 0.0, 1.0),
                    met.EnterMaterial ?? met.LeaveMaterial ?? 0,
                    kind,
                    owner as GameTask,
                    owner?.ToString() ?? string.Empty,
                    IsExit: met.EnterMaterial is null && met.LeaveMaterial is not null);
                return true;
            }

            travelled = distance + TraceSkipUnits;
            if (travelled >= length)
            {
                return false;
            }
        }

        return false;
    }

    /// <inheritdoc/>
    /// <remarks>Every type's group and the ground: what <see cref="Cast"/> traces.</remarks>
    void IHostCollisionWorld.TraceLines(WorldLineTrace trace) => TraceLines(trace, EveryTypeGroup | TerrainGroup);

    /// <inheritdoc/>
    bool IHostCollisionWorld.LineOfSight(WorldLineOfSight sight) => LineOfSight(sight, EveryTypeGroup | TerrainGroup);

    private WorldTraceHitKind KindOf(IWorldCollider? collider) => collider switch
    {
        null => WorldTraceHitKind.None,
        _ when ReferenceEquals(collider, _terrain) => WorldTraceHitKind.Terrain,
        Igi2BoneCollider => WorldTraceHitKind.Human,
        IWorldLineCollider other when (other.Group & HumanGroup) != 0 => WorldTraceHitKind.Human,
        _ => WorldTraceHitKind.StaticObject,
    };
}
