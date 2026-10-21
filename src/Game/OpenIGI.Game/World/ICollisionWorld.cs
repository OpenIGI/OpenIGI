using OpenIGI.Game.Player;
using OpenIGI.Core.Numerics;

namespace OpenIGI.Game.World;

/// <summary>A level's walls and ground, as anything that moves through it or looks across it asks of them.</summary>
/// <remarks>
/// <para>
/// Each game keeps its own collision -- Project I.G.I.'s placed objects and terrain octree, I.G.I. 2's object tree and height
/// grid -- and answers its own retail queries exactly. This is the part of them that is the same question in both: is there
/// a wall between two points, and where is the ground under one. A camera kept out of walls, a body set down, or another
/// game's soldier brought into a level asks this, and works in either game.
/// </para>
/// <para>
/// Not a replacement for the retail queries: a game's own code keeps calling them, in their own precision and order.
/// </para>
/// </remarks>
public interface ICollisionWorld
{
    /// <summary>Gets the ground a body stands on, or <see langword="null"/> when the level has none yet.</summary>
    IGroundHeightField? Ground { get; }

    /// <summary>Casts a ray at the level's walls and ground and reports the nearest surface it meets.</summary>
    /// <param name="origin">Where the ray starts, in world units.</param>
    /// <param name="target">Where it stops.</param>
    /// <param name="hit">Receives the nearest surface.</param>
    /// <returns><see langword="true"/> when the ray met something before its end.</returns>
    bool Cast(Vector3d origin, Vector3d target, out WorldRayHit hit);
}

/// <summary>
/// A level's collision built from placed objects and a terrain: the nearer of the two answering, as the rays, the bodies, the
/// impact effects and the cameras ask of it.
/// </summary>
public sealed class LevelCollision : ICollisionWorld
{
    private readonly WorldRayCast? _cast;
    private WorldRayCast? _daylight;

    /// <summary>Initializes a new instance of the <see cref="LevelCollision"/> class.</summary>
    /// <param name="solids">The placed objects, or <see langword="null"/> for none.</param>
    /// <param name="terrain">The terrain, or <see langword="null"/> for a level with none to trace.</param>
    /// <param name="ground">The ground a body stands on, or <see langword="null"/> for none yet.</param>
    /// <param name="terrainMaterialRow">
    /// The game-material row the level's terrain is made of, which an impact on open ground marks as; <see langword="null"/>
    /// when it is not known.
    /// </param>
    public LevelCollision(ObjectCollisionIndex? solids, TerrainData? terrain, IGroundHeightField? ground, int? terrainMaterialRow = null)
    {
        Solids = solids;
        Terrain = terrain;
        Ground = ground;
        TerrainMaterialRow = terrainMaterialRow;
        _cast = (solids, terrain) switch
        {
            ({ } placed, { } land) => WorldTrace.AsRayCast(WorldTrace.Nearest(placed.Tracer(), TerrainRayTrace.Caster(land))),
            ({ } placed, null) => placed.Caster(),
            (null, { } land) => WorldTrace.AsRayCast(TerrainRayTrace.Caster(land)),
            _ => null,
        };
        Trace = solids is { } traced
            ? terrain is { } ground2 ? WorldTrace.Nearest(traced.Tracer(), TerrainRayTrace.Caster(ground2)) : traced.Tracer()
            : null;
        EffectWalls = Trace is { } effectTrace ? EffectCast(effectTrace, terrainMaterialRow) : null;
        DropletWalls = Trace is { } dropletTrace
            ? (Vector3d origin, Vector3d end, out WorldRayHit hit) =>
            {
                if (!dropletTrace(new WorldTraceRequest(origin, end, WorldTraceMask.Terrain), out WorldTraceHit rich))
                {
                    hit = default;
                    return false;
                }

                hit = rich.RayHit with { OnTerrain = true };
                return true;
            }
            : null;
        Walls = _cast is { } walls
            ? (Vector3d from, Vector3d to, out Vector3d hit) =>
            {
                bool blocked = walls(from, to, out WorldRayHit wall);
                hit = blocked ? wall.Position : default;
                return blocked;
            }
            : null;
    }

    /// <summary>Gets a collision with nothing in it.</summary>
    public static LevelCollision Empty { get; } = new(null, null, null);

    /// <summary>Gets the game-material row the level's terrain is made of, when it is known.</summary>
    public int? TerrainMaterialRow { get; }

    /// <summary>Gets the trace of the placed objects and the terrain together, who and what was struck included.</summary>
    public WorldTraceCast? Trace { get; }

    /// <summary>
    /// Gets what blood, debris and lamps' shadows are traced against: <see cref="Trace"/> as a ray, a hit on open ground marked
    /// as the level's terrain row.
    /// </summary>
    /// <remarks>
    /// The terrain leaf reports no material row, so a hit on open ground would come back as Air and mark nothing -- leaving the
    /// ground clean of both bullet holes and blood, where retail's Ground row names <c>hole_concrete1.tex</c>. The level's own
    /// terrain row is substituted, the same row the footsteps and the player's material lookup read.
    /// </remarks>
    public WorldRayCast? EffectWalls { get; }

    /// <summary>Gets what blood droplets fall onto: the terrain alone.</summary>
    /// <remarks>
    /// The droplets' batch cast (<c>0x4FD660</c>) carries the particle class's own message, which no placed object answers:
    /// they meet the terrain alone, and pass through walls and crates.
    /// </remarks>
    public WorldRayCast? DropletWalls { get; }

    /// <summary>Gets whether a camera between two points is blocked, and where: made once, as a camera asks it every frame.</summary>
    public DeathCameraVantage.Blocked? Walls { get; }

    /// <summary>
    /// Gets daylight's line through the level, as the lighting bakes cast it: every door shut, glass and chain-link passed, and the
    /// terrain blocking wherever it is nearer than the first solid object.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A bake ray passes through glass and chain-link the way a line of sight does (<see cref="GameMaterial.IsSightTransparent"/>),
    /// so daylight reaches a room through its windows instead of stopping at the pane. It steps a hair past each see-through face
    /// and casts on until it meets something solid or leaves the level; a portal polygon (a doorway or window opening) the cast
    /// skips anyway. The terrain blocks where its own sight test says so (<see cref="TerrainSightBlock"/>); it reports material Air.
    /// </para>
    /// <para><see langword="null"/> for a level with no placed objects.</para>
    /// </remarks>
    public WorldRayCast? DaylightCast => _daylight ??= Solids is { } solids ? Daylight(solids.CasterWithDoorsShut(), Terrain) : null;

    /// <summary>Gets the placed objects.</summary>
    public ObjectCollisionIndex? Solids { get; }

    /// <summary>Gets the terrain.</summary>
    public TerrainData? Terrain { get; }

    /// <inheritdoc/>
    public IGroundHeightField? Ground { get; }

    /// <summary>Gets a value indicating whether there is nothing to cast against.</summary>
    public bool IsEmpty => _cast is null;

    /// <inheritdoc/>
    public bool Cast(Vector3d origin, Vector3d target, out WorldRayHit hit)
    {
        if (_cast is null)
        {
            hit = default;
            return false;
        }

        return _cast(origin, target, out hit);
    }

    private static WorldRayCast Daylight(WorldRayCast shut, TerrainData? terrain)
    {
        WorldRayCast throughGlass = (a, b, out blockedAt) =>
        {
            Vector3d origin = a;
            for (int pass = 0; pass < 8; pass++)
            {
                if (!shut(origin, b, out WorldRayHit hit))
                {
                    blockedAt = default;
                    return false;
                }

                if (!GameMaterial.IsSightTransparent(hit.Material))
                {
                    blockedAt = hit;
                    return true;
                }

                Vector3d toEnd = new(b.X - hit.Position.X, b.Y - hit.Position.Y, b.Z - hit.Position.Z);
                double length = Math.Sqrt((toEnd.X * toEnd.X) + (toEnd.Y * toEnd.Y) + (toEnd.Z * toEnd.Z));
                if (length <= 1e-6)
                {
                    blockedAt = default;
                    return false;
                }

                // A few units past the pane (4/4096 m, about a millimetre); thicker or layered glass is cleared by the loop,
                // one see-through face at a time.
                double scale = 4.0 / length;
                origin = new Vector3d(
                    hit.Position.X + (toEnd.X * scale),
                    hit.Position.Y + (toEnd.Y * scale),
                    hit.Position.Z + (toEnd.Z * scale));
            }

            blockedAt = default;
            return false;
        };

        return (a, b, out blockedAt) =>
        {
            bool hitObject = throughGlass(a, b, out WorldRayHit onObject);
            if (terrain is not null
                && TerrainSightBlock.TryBlock(terrain, a, b, out WorldRayHit onGround)
                && (!hitObject || onGround.Distance < onObject.Distance))
            {
                blockedAt = onGround;
                return true;
            }

            blockedAt = onObject;
            return hitObject;
        };
    }

    private static WorldRayCast EffectCast(WorldTraceCast trace, int? terrainRow) =>
        (Vector3d origin, Vector3d end, out WorldRayHit hit) =>
        {
            if (!trace(new WorldTraceRequest(origin, end, WorldTraceMask.World), out WorldTraceHit rich))
            {
                hit = default;
                return false;
            }

            hit = rich.RayHit with { OnTerrain = rich.Kind == WorldTraceHitKind.Terrain };
            if (rich.Kind == WorldTraceHitKind.Terrain && hit.Material == 0 && terrainRow is int ground)
            {
                hit = hit with { Material = ground };
            }

            return true;
        };
}
