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
/// The same shape as <c>Igi1HumanAi.GroundQuery</c>, declared again here so the solver depends on nothing
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

    /// <summary>Gets the solid world triangles around a box, terrain included, for mesh-accurate contacts.</summary>
    /// <remarks>A dropped weapon's rigid body collides against these; without them it falls back to rays.</remarks>
    public CollisionTriangleGather? Triangles { get; init; }

    /// <summary>Gets a ray cast against everything solid, terrain included.</summary>
    /// <remarks>
    /// <see cref="Cast"/> meets placed objects only. The original's pickup probe (<c>0x46C980</c>) traces
    /// fifty metres down through terrain and objects alike, which is what this answers.
    /// </remarks>
    public WorldRayCast? Trace { get; init; }

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

    /// <summary>Builds a floor with one upright wall in it, for tests.</summary>
    /// <param name="floor">The floor's Z, in engine units.</param>
    /// <param name="wallY">The wall's Y; it faces towards negative Y and is unbounded.</param>
    /// <returns>The world.</returns>
    public static RagdollWorld Room(double floor, double wallY) =>
        new(
            cast: (Vector3d origin, Vector3d end, out WorldRayHit hit) =>
            {
                hit = default;

                double from = origin.Y - wallY;
                double to = end.Y - wallY;
                if (from == to || (from < 0.0 && to < 0.0) || (from > 0.0 && to > 0.0))
                {
                    return false;
                }

                double at = from / (from - to);
                hit = new WorldRayHit(
                    origin + ((end - origin) * at),
                    new Vector3f(0f, -1f, 0f),
                    (end - origin).Length * at);
                return true;
            },
            ground: (double x, double y, double z, out double groundZ) =>
            {
                _ = x;
                _ = y;
                _ = z;
                groundZ = floor;
                return true;
            });

    /// <summary>How far above a point the ground search starts, in engine units.</summary>
    /// <remarks>
    /// <para>
    /// It has to be above the point at all, because <b>the port's ground query only ever looks
    /// downwards</b> and reports nothing when the surface is above where the search began. Start it at
    /// the bone and a body that has got even slightly under a floor can never find that floor again, so
    /// it drops out of the world — and a bone moving fast enough to end a substep below the surface is
    /// exactly how it gets under there.
    /// </para>
    /// <para>
    /// <b>But it must be only just above, and that is not a tuning preference.</b> The query returns the
    /// <em>highest surface below where it starts</em>. Begin the search two metres up and, in any room,
    /// what it finds is the <b>ceiling</b> — which it then reports as the ground, whereupon the contact
    /// dutifully pushes the bone up to it and the corpse flies into the roof. Outdoors there is nothing
    /// overhead so the same code behaves perfectly, which is precisely how a bug like this survives being
    /// tested in a field.
    /// </para>
    /// <para>
    /// Forty centimetres: further than a bone can sink in a tick at any speed it falls, and well under
    /// the height of anything a body could be standing beneath.
    /// </para>
    /// </remarks>
    public const double GroundSearchAboveInUnits = 1638.0;

    /// <summary>Looks for a surface a short way from a point.</summary>
    /// <param name="from">Where to look from, in engine units.</param>
    /// <param name="direction">Which way to look; need not be unit length.</param>
    /// <param name="reach">How far to look, in engine units.</param>
    /// <param name="point">Receives the surface point.</param>
    /// <param name="normal">Receives its outward normal, unit length.</param>
    /// <returns><see langword="false"/> when nothing is that way.</returns>
    /// <remarks>
    /// The ground query answers only downwards, so everything vertical goes through that and everything
    /// else goes through here — the same ray cast the player's own wall sweep uses, so a corpse meets the
    /// walls a living body does.
    /// </remarks>
    public bool TrySurface(Vector3d from, Vector3d direction, double reach, out Vector3d point, out Vector3d normal)
    {
        point = default;
        normal = default;

        double length = direction.Length;
        if (Cast is not { } cast || length <= 0.0 || reach <= 0.0)
        {
            return false;
        }

        Vector3d unit = direction / length;
        if (!cast(from, from + (unit * reach), out WorldRayHit hit))
        {
            return false;
        }

        point = hit.Position;
        normal = new Vector3d(hit.Normal.X, hit.Normal.Y, hit.Normal.Z);

        double size = normal.Length;
        if (size <= 0.0)
        {
            return false;
        }

        normal /= size;

        // A surface facing the same way the ray travelled is one the ray hit from behind -- the far side
        // of a wall the bone is already inside. Its normal points away from the bone, and pushing along
        // it would drive the bone further in, so it is flipped to face the bone.
        if (Vector3d.Dot(normal, unit) > 0.0)
        {
            normal = -normal;
        }

        return true;
    }

    /// <summary>Finds the ground height near a point, whether it is above or below.</summary>
    /// <param name="point">The point, in engine units.</param>
    /// <param name="groundZ">Receives the ground height.</param>
    /// <returns><see langword="true"/> when a surface was found.</returns>
    public bool TryGround(Vector3d point, out double groundZ)
    {
        groundZ = 0.0;
        return Ground is { } ground
            && ground(point.X, point.Y, point.Z + GroundSearchAboveInUnits, out groundZ);
    }
}
