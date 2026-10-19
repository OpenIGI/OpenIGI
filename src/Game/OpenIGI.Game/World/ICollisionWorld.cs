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

/// <summary>A Project I.G.I. level's collision: its placed objects and its terrain, the nearer of the two answering.</summary>
public sealed class LevelCollision : ICollisionWorld
{
    private readonly WorldRayCast? _cast;

    /// <summary>Initializes a new instance of the <see cref="LevelCollision"/> class.</summary>
    /// <param name="solids">The placed objects, or <see langword="null"/> for none.</param>
    /// <param name="terrain">The terrain, or <see langword="null"/> for a level with none to trace.</param>
    /// <param name="ground">The ground a body stands on, or <see langword="null"/> for none yet.</param>
    public LevelCollision(ObjectCollisionIndex? solids, TerrainData? terrain, IGroundHeightField? ground)
    {
        Solids = solids;
        Terrain = terrain;
        Ground = ground;
        _cast = (solids, terrain) switch
        {
            ({ } placed, { } land) => WorldTrace.AsRayCast(WorldTrace.Nearest(placed.Tracer(), TerrainRayTrace.Caster(land))),
            ({ } placed, null) => placed.Caster(),
            (null, { } land) => WorldTrace.AsRayCast(TerrainRayTrace.Caster(land)),
            _ => null,
        };
    }

    /// <summary>Gets a collision with nothing in it.</summary>
    public static LevelCollision Empty { get; } = new(null, null, null);

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
}
