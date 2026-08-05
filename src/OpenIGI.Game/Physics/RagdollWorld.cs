using OpenIGI.Core.Numerics;
using OpenIGI.Game.World;

namespace OpenIGI.Game.Physics;

/// <summary>Finds the ground beneath a point.</summary>
/// <param name="x">World X, in engine units.</param>
/// <param name="y">World Y, in engine units.</param>
/// <param name="z">The height to search downwards from, in engine units.</param>
/// <param name="groundZ">Receives the ground height when one is found.</param>
/// <returns><see langword="true"/> when a surface was found.</returns>
/// <remarks>
/// The same shape as <c>AiSoldier.GroundQuery</c>, declared again here so the solver depends on nothing
/// in the AI. The desktop shell's <c>MakeGroundQuery</c> satisfies both.
/// </remarks>
public delegate bool RagdollGround(double x, double y, double z, out double groundZ);

/// <summary>What a ragdoll collides against.</summary>
/// <remarks>
/// <para>
/// <b>No new collision code is written for ragdolls.</b> Both members are the queries the port already
/// runs against for the player's wall sweep, the ground query and the death-fall solver, so a corpse
/// meets exactly the geometry a living body does and there is one place where that geometry is wrong.
/// </para>
/// <para>
/// Both are optional. A body given neither falls for ever, which is what the unit tests want when they
/// are measuring the constraint passes rather than the contacts.
/// </para>
/// </remarks>
public sealed class RagdollWorld
{
    /// <summary>Initializes a new instance of the <see cref="RagdollWorld"/> class.</summary>
    /// <param name="cast">Casts a ray against placed collision meshes; <see langword="null"/> for none.</param>
    /// <param name="ground">Finds the ground under a point; <see langword="null"/> for none.</param>
    public RagdollWorld(WorldRayCast? cast = null, RagdollGround? ground = null)
    {
        Cast = cast;
        Ground = ground;
    }

    /// <summary>Gets a world with no geometry at all.</summary>
    public static RagdollWorld Empty { get; } = new();

    /// <summary>Gets the ray cast against placed collision meshes.</summary>
    public WorldRayCast? Cast { get; }

    /// <summary>Gets the downward ground query, which reaches the terrain the ray cast does not.</summary>
    public RagdollGround? Ground { get; }

    /// <summary>Builds a world that is a single horizontal plane, for tests.</summary>
    /// <param name="height">The plane's Z, in engine units.</param>
    /// <returns>The world.</returns>
    public static RagdollWorld Plane(double height) =>
        new(ground: (double x, double y, double z, out double groundZ) =>
        {
            _ = x;
            _ = y;
            _ = z;
            groundZ = height;
            return true;
        });

    /// <summary>Finds the ground height under a point.</summary>
    /// <param name="point">The point, in engine units.</param>
    /// <param name="searchAbove">How far above the point to start the search, in engine units.</param>
    /// <param name="groundZ">Receives the ground height.</param>
    /// <returns><see langword="true"/> when a surface was found.</returns>
    public bool TryGround(Vector3d point, double searchAbove, out double groundZ)
    {
        groundZ = 0.0;
        return Ground is { } ground && ground(point.X, point.Y, point.Z + searchAbove, out groundZ);
    }
}
