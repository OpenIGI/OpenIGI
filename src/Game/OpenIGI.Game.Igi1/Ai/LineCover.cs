using OpenIGI.Core.Numerics;
using OpenIGI.Game.World;

namespace OpenIGI.Game.Igi1.Ai;

/// <summary>What stands between two points of the level, at its heaviest: for a mod's own sense of sound or sight. <b>Not retail.</b></summary>
public enum LineCover
{
    /// <summary>Nothing, or only what the sight test sees through (glass, fences).</summary>
    Open,

    /// <summary>Only moving objects: a door leaf, a gate.</summary>
    MovingObject,

    /// <summary>The level's terrain, or a building's wall, floor or roof.</summary>
    Solid,
}

/// <summary>Asks the level what stands between two points. <b>Not retail.</b></summary>
public static class LineCoverQuery
{
    // As the sight test: how far past a see-through hit the next cast starts, and how many it steps past at most.
    private const double SkipEpsilonUnits = 64d;
    private const int SkipLimit = 8;

    /// <summary>The heaviest cover along the line from one point to another.</summary>
    /// <param name="trace">The level's world trace (MissionLevelBuild.WorldTrace).</param>
    /// <param name="from">From, in world units.</param>
    /// <param name="to">To, in world units.</param>
    /// <returns>What stands between: open, a moving object, or something solid.</returns>
    /// <remarks>Steps past glass, fences and moving objects, as the sight test steps past glass, to find anything solid behind them.</remarks>
    public static LineCover Between(WorldTraceCast trace, Vector3d from, Vector3d to)
    {
        ArgumentNullException.ThrowIfNull(trace);

        Vector3d segment = new(to.X - from.X, to.Y - from.Y, to.Z - from.Z);
        double length = Math.Sqrt((segment.X * segment.X) + (segment.Y * segment.Y) + (segment.Z * segment.Z));
        LineCover cover = LineCover.Open;
        Vector3d origin = from;
        double travelled = 0d;
        for (int step = 0; step <= SkipLimit && length > 0d; step++)
        {
            if (!trace(new WorldTraceRequest(origin, to, WorldTraceMask.World), out WorldTraceHit hit))
            {
                return cover;
            }

            if (hit.Kind is WorldTraceHitKind.Terrain or WorldTraceHitKind.StaticObject && !GameMaterial.IsSightTransparent(hit.Material))
            {
                return LineCover.Solid;
            }

            if (hit.Kind is WorldTraceHitKind.DynamicObject)
            {
                cover = LineCover.MovingObject;
            }

            double advance = hit.Distance + SkipEpsilonUnits;
            if (advance >= length - travelled)
            {
                return cover;
            }

            travelled += advance;
            double fraction = travelled / length;
            origin = new(from.X + (segment.X * fraction), from.Y + (segment.Y * fraction), from.Z + (segment.Z * fraction));
        }

        return cover;
    }
}
