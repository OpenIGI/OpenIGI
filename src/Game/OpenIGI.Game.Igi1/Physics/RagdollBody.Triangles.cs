using OpenIGI.Core.Numerics;
using OpenIGI.Game.Physics;
using OpenIGI.Game.World;

namespace OpenIGI.Game.Igi1.Physics;

/// <content>
/// The world's real surfaces: every solid triangle near the body, gathered once a tick, with each capsule
/// pushed out of them every substep.
/// </content>
/// <remarks>
/// <para>
/// <b>What a corpse lies on when it is not lying on flat ground.</b> The ground probe answers one height
/// under each capsule end and the wall rays one surface each way, and between them they cannot describe a
/// crate's edge, a step, or the corner a body slumps into: a body falling across a staircase sank its
/// middle into the steps, and one that went down beside a crate lay half inside it. The triangles are the
/// level's own collision geometry — the same query a dropped gun's rigid body runs — so a corpse now meets
/// the shape that is actually there.
/// </para>
/// <para>
/// Gathered once a tick over everything the body can reach, and measured against every substep: the
/// surfaces do not move within a tick even though the body's distance to them does, the same split the
/// ground probe makes. The probe and the rays stay — the triangle gather can be absent, and the rays still
/// catch a body travelling fast enough to cross a thin wall in one tick.
/// </para>
/// </remarks>
public sealed partial class RagdollBody
{
    /// <summary>How many triangle contacts a substep may keep for its velocity pass, beyond the probes'.</summary>
    /// <remarks>A body lying across a step can touch a dozen; the cap only bounds the array.</remarks>
    public const int TriangleContacts = 64;

    private readonly List<CollisionTriangle> _nearby = [];
    private readonly List<Vector3d> _nearbyLow = [];
    private readonly List<Vector3d> _nearbyHigh = [];

    private static Vector3d Lower(Vector3d a, Vector3d b) =>
        new(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Min(a.Z, b.Z));

    private static Vector3d Upper(Vector3d a, Vector3d b) =>
        new(Math.Max(a.X, b.X), Math.Max(a.Y, b.Y), Math.Max(a.Z, b.Z));

    // Everything the body can touch this tick: its capsules' bounds, widened by how far each will travel.
    private void GatherTriangles(RagdollWorld world)
    {
        _nearby.Clear();
        _nearbyLow.Clear();
        _nearbyHigh.Clear();
        if (world.Triangles is not { } gather)
        {
            return;
        }

        Vector3d low = new(double.MaxValue, double.MaxValue, double.MaxValue);
        Vector3d high = new(double.MinValue, double.MinValue, double.MinValue);
        for (int body = 0; body < _position.Length; body++)
        {
            CapsuleOf(body, out Vector3d from, out Vector3d to, out double radius);
            double reach = radius + _velocity[body].Length + PenetrationSlop;
            low = Lower(low, Lower(from, to) - new Vector3d(reach, reach, reach));
            high = Upper(high, Upper(from, to) + new Vector3d(reach, reach, reach));
        }

        gather(low, high, _nearby);
        foreach (CollisionTriangle triangle in _nearby)
        {
            _nearbyLow.Add(Lower(triangle.A, Lower(triangle.B, triangle.C)));
            _nearbyHigh.Add(Upper(triangle.A, Upper(triangle.B, triangle.C)));
        }
    }

    /// <summary>Pushes one capsule out of every triangle it has sunk into.</summary>
    /// <remarks>
    /// <para>
    /// The push is along the line between the nearest two points, which is the face's normal over a face
    /// and the way out over an edge or a corner — so a body draped over a crate's edge rests on the edge
    /// rather than being shoved along one face of it. The winding is never read: level meshes and terrain
    /// wind opposite ways, so which side is out comes from where the capsule is.
    /// </para>
    /// <para>
    /// A capsule whose axis passes right through a face has no nearest pair to go by. It is pushed back
    /// out the side its middle was on at the start of the tick, which is the side it came from.
    /// </para>
    /// <para>
    /// Applied at the point of contact, as the ground is, and as a repair: like every push out of the
    /// world, it is not speed the body had (see <see cref="Recover"/>).
    /// </para>
    /// </remarks>
    private void SolveTriangles(int body, double budget, bool record)
    {
        if (_nearby.Count == 0)
        {
            return;
        }

        double radius = _radius[body];
        for (int i = 0; i < _nearby.Count; i++)
        {
            Vector3d along = Turned(body, _aim[body]) * _halfLength[body];
            Vector3d from = _position[body] - along;
            Vector3d to = _position[body] + along;
            Vector3d reach = new(radius, radius, radius);
            Vector3d low = Lower(from, to) - reach;
            Vector3d high = Upper(from, to) + reach;
            if (_nearbyHigh[i].X < low.X || _nearbyLow[i].X > high.X
                || _nearbyHigh[i].Y < low.Y || _nearbyLow[i].Y > high.Y
                || _nearbyHigh[i].Z < low.Z || _nearbyLow[i].Z > high.Z)
            {
                continue;
            }

            CollisionTriangle triangle = _nearby[i];
            Vector3d normal;
            double depth;
            Vector3d touching;
            if (triangle.NearestToSegment(from, to, out Vector3d onSegment, out Vector3d onTriangle))
            {
                Vector3d face = Vector3d.Cross(triangle.B - triangle.A, triangle.C - triangle.A);
                double size = face.Length;
                if (size <= 1e-9)
                {
                    continue;
                }

                normal = face / size;
                if (Vector3d.Dot(_tickStart[body] - triangle.A, normal) < 0.0)
                {
                    normal = -normal;
                }

                // As deep as the end that has gone furthest through, plus the radius that should be clear.
                double under = Math.Min(Vector3d.Dot(from - triangle.A, normal), Vector3d.Dot(to - triangle.A, normal));
                depth = radius - under - PenetrationSlop;
                touching = onSegment;
            }
            else
            {
                Vector3d gap = onSegment - onTriangle;
                double distance = gap.Length;
                if (distance <= 1e-9 || distance >= radius)
                {
                    continue;
                }

                normal = gap / distance;
                depth = radius - distance - PenetrationSlop;
                touching = onTriangle;
            }

            if (depth <= 0.0)
            {
                continue;
            }

            Vector3d arm = touching - _position[body];
            Remember(record, body, arm, normal);
            ApplyPositional(-1, body, Vector3d.Zero, arm, normal, Math.Min(depth, budget), repair: true);
        }
    }
}
