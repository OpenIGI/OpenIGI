using OpenIGI.Core.Numerics;
using OpenIGI.Game.World;

namespace OpenIGI.Game.Player;

/// <summary>The body put on the ground: a player dropped into a level, or an interaction's authored root snapped down.</summary>
public sealed partial class PlayerBody
{
    /// <summary>How far a placement search looks below its start, in world units.</summary>
    /// <remarks>
    /// Half a kilometre. Placement is <b>not</b> the runtime ground query and does not share its
    /// four-metre reach: dropping a player into a level starts from wherever the caller was looking,
    /// which may be tens of metres up. The search walks down in whole probe lengths and takes the
    /// first ground it meets, so it lands on the highest surface below the start rather than the
    /// lowest.
    /// </remarks>
    public const double PlacementSearchUnits = 500.0 * 4096.0;

    /// <summary>How far a self-driving interaction state searches when it snaps its authored root to ground.</summary>
    /// <remarks>
    /// IGI2's symbolized <c>Human_SnapToGround</c> passes exactly <c>4096000</c> to
    /// <c>Human_GroundCollision</c>; the matching IGI1 routine is <c>sub_463260</c>. Terminal, generic,
    /// cabinet, door and explosive state initializers call it immediately after popping the authored
    /// position. This is deliberately distinct from the ordinary four-metre runtime ground probe.
    /// </remarks>
    public const double InteractionSnapSearchUnits = 4096000.0;

    /// <summary>Snaps an authored interaction root to the highest terrain or placed-model surface below it.</summary>
    public bool SnapInteractionToGround(IGroundHeightField? terrain) =>
        PlaceOnGround(terrain, InteractionSnapSearchUnits);

    /// <summary>Drops the player onto the ground beneath it, for placing one at the start of a level.</summary>
    /// <param name="terrain">The level's terrain, or <see langword="null"/> for an object-only interior.</param>
    /// <returns><see langword="false"/> when there is nothing under the player to stand on.</returns>
    public bool PlaceOnGround(IGroundHeightField? terrain)
        => PlaceOnGround(terrain, PlacementSearchUnits);

    private bool PlaceOnGround(IGroundHeightField? terrain, double searchUnits)
    {
        _pendingWalkOffVelocity = null;

        double from = Position.Z + HumanGroundQuery.ProbeOriginStandingInUnits;
        double floor = from - searchUnits;

        bool found = false;
        double height = double.NegativeInfinity;

        // Terrain: walk down in whole probe lengths and take the first — the highest — ground found.
        double probe = from;
        while (terrain is not null && probe > floor)
        {
            if (terrain.TryGetGround(Position.X, Position.Y, probe, out TerrainGround ground))
            {
                height = ground.Height;
                found = true;
                break;
            }

            probe -= TerrainHeightQuery.SearchDepthUnits;
        }

        // Objects: one downward cast finds the highest floor or platform below the spawn, so a body a
        // level authors on a building floor lands on it rather than dropping through to the terrain
        // beneath — the same object half the runtime ground query gained in Settle.
        if (Walls is not null
            && Walls(
                new Vector3d(Position.X, Position.Y, from),
                new Vector3d(Position.X, Position.Y, floor),
                out WorldRayHit hit)
            && (!found || hit.Position.Z > height))
        {
            height = hit.Position.Z;
            found = true;
        }

        // Nothing below: the original's ground query (0x462390) then casts up, as far as it cast down, so a spawn the ground
        // has been raised over stands on the ground rather than under it.
        if (!found
            && terrain is not null
            && terrain.TryDrop(Position.X, Position.Y, from, HumanGroundQuery.GroundDropUnits, upward: true, out TerrainGround above))
        {
            height = above.Height;
            found = true;
        }

        if (!found)
        {
            return false;
        }

        Position = new Vector3d(Position.X, Position.Y, height);
        Velocity = default;
        IsGrounded = true;
        return true;
    }
}
