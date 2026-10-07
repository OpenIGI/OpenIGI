using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi2.Collision;

namespace OpenIGI.Game.Igi2.Humans;

/// <summary>The walls around a human: the sweep that keeps a move out of them and the ceiling that limits a posture.</summary>
public abstract partial class Igi2Human
{
    /// <summary>The signal bit raised while the body touches a wall, <c>bSignal &amp; 4</c>.</summary>
    protected const uint WallSignal = 0x4;

    /// <summary>The query groups the sweep reaches: forests, rigid objects and spline segments.</summary>
    private const uint SweepGroups = Igi2CollisionWorld.ForestGroup | Igi2RigidObject.RigidGroup | Igi2CollisionWorld.SplineGroup;

    /// <summary>The ring's reach, 0.1 metres.</summary>
    private const float RingReach = 409.6f;

    /// <summary>How far the ceiling line starts above the feet and how far it reaches.</summary>
    private const double CeilingStart = 409.6000061035156;

    private const double CeilingReach = 10240.0;

    /// <summary>Normals this close to each other are one wall to the corner test, the cosine <c>0x3F666666</c>.</summary>
    private const float SameWall = 0.9f;

    /// <summary>Opposing walls nearer each other than half a metre are a gap the body cannot pass.</summary>
    private const float NarrowGap = 2048f;

    /// <summary>The ring of directions the sweep looks along, 0.1 metres out on a square, in the body's frame.</summary>
    private static readonly float[] Ring =
    [
        -409.6f, 409.6f, 0f, 0f, 409.6f, 0f, 409.6f, 409.6f, 0f, 409.6f, 0f, 0f,
        409.6f, -409.6f, 0f, 0f, -409.6f, 0f, -409.6f, -409.6f, 0f, -409.6f, 0f, 0f,
    ];

    /// <summary>The ring a body lying on a vehicle uses: half a metre ahead and behind, 0.2 metres to the sides.</summary>
    private static readonly float[] VehicleRing =
    [
        -819.2f, 2048f, 0f, 0f, 2048f, 0f, 819.2f, 2048f, 0f, 819.2f, 0f, 0f,
        819.2f, -2048f, 0f, 0f, -2048f, 0f, -819.2f, -2048f, 0f, -819.2f, 0f, 0f,
    ];

    /// <summary>The three heights the ring is cast at, by posture: knee, waist and head.</summary>
    private static readonly float[][] RingHeights =
    [
        [1638.4f, 4096f, 6963.2f],
        [1638.4f, 3276.8f, 4915.2f],
        [1024f, 1433.6f, 1638.4f],
    ];

    /// <summary>The types whose objects a body walks through: <c>GameFunctions_ObjectCollisionEvaluateDiscardHandler</c> and its kin.</summary>
    /// <remarks>
    /// <c>Wire</c> (<c>Wire_Open</c>, <c>igi2 0x514EF0</c>) and the three pickups (<c>GunPickup</c> <c>0x46BC60</c>,
    /// <c>AmmoPickup</c> <c>0x5276C0</c>, <c>GenericPickup</c> <c>0x4F7380</c>) answer the sweep's evaluate event with no.
    /// </remarks>
    private static readonly HashSet<string> SweepThrough = new(StringComparer.Ordinal)
    {
        "Wire", "GunPickup", "AmmoPickup", "GenericPickup",
    };

    /// <summary>The types that derive from <c>Vehicle</c>: <c>Car</c>, <c>Plane</c>, <c>Train</c>, <c>Heli</c> and <c>Missile</c> open on it.</summary>
    private static readonly HashSet<string> VehicleTypes = new(StringComparer.Ordinal)
    {
        "Vehicle", "Car", "Plane", "Train", "Heli", "Missile",
    };

    private readonly Igi2LineTrace _sweep = new(MaxSweepLines) { Mesh = FootingMesh, Includes = static owner => !IsSweptThrough(owner) };

    /// <summary>Three heights of eight ring lines and a probe, and the ceiling line.</summary>
    private const int MaxSweepLines = (3 * 9) + 1;

    /// <summary>
    /// Whether the body, in its current posture, fits where it stands: the wall sweep run on the spot against a copy
    /// of the signal word, as the stand states probe a lie-down (<c>HumanPlayer_RunStateStand</c>, <c>igi2 0x4A2F70</c>).
    /// </summary>
    /// <returns><see langword="false"/> when the sweep touches a wall.</returns>
    /// <remarks>
    /// The sweep's signal is a local copy in the original, so only its wall bit is read and the body's own signal word
    /// is left as it was; what the sweep allows the posture is the body's, as it is after any sweep.
    /// </remarks>
    public bool FitsWhereItStands()
    {
        uint flags = Locomotion.Flags;
        Vector3d spot = Position;
        ObjectCollision(Position, ref spot, out _);
        bool touches = (Locomotion.Flags & WallSignal) != 0;
        Locomotion.Flags = flags;
        return !touches;
    }

    /// <summary>
    /// Keeps a move out of the walls: <c>Human_ObjectCollision</c> (<c>igi2 0x4FBA60</c>).
    /// </summary>
    /// <param name="from">Where the move starts.</param>
    /// <param name="to">Where it would end; moved out of the walls, and kept from rising under a ceiling.</param>
    /// <param name="speed">Receives the move as it now is.</param>
    /// <remarks>
    /// <para>
    /// The sweep casts a ring of eight lines, a tenth of a metre out and lengthened by the move, at three heights for
    /// the posture. When the body moves it adds a tenth of a metre along the move at each height, and it casts one line
    /// straight up for the ceiling. Every line is in a frame turned with the body about the vertical only.
    /// </para>
    /// <para>
    /// The ceiling is read first. It takes postures away (a crouch can't stand under 1.8 metres) and keeps the body
    /// from rising, and a ceiling close above makes the move slide along it and along the ground. Then every wall line
    /// that struck, from the highest down, pushes the body back out of any wall nearer than the ring, and slides the
    /// move along the wall: first along the wall's horizontal direction, then along its whole normal. Walls above the
    /// ceiling are ignored.
    /// </para>
    /// <para>
    /// The horizontal slides are remembered. Two walls that are not one wall and do not face each other are a corner,
    /// and two that face each other closer than half a metre are a gap: either stops the move dead, which is why a
    /// body cannot squeeze into a corner. The pushes are averaged, and the move's height is never changed except by
    /// the ceiling.
    /// </para>
    /// </remarks>
    public void ObjectCollision(Vector3d from, ref Vector3d to, out Vector3f speed)
    {
        speed = default;
        Locomotion.Flags &= ~WallSignal;
        AllowedStance = Igi2AllowedStance.All;

        float dx = ((Fpu)to.X - from.X).F;
        float dy = ((Fpu)to.Y - from.Y).F;
        Fpu dy2 = (Fpu)dy * dy;
        Fpu dx2 = (Fpu)dx * dx;
        float moveLength = Fpu.Sqrt(dx2 + dy2).F;

        if (Pose is < Igi2HumanPose.Standing or > Igi2HumanPose.LyingFlat)
        {
            return;
        }

        float[] frame = SweepFrame();
        float[] ring = Pose == Igi2HumanPose.LyingFlat && GroundCollider?.Owner is Igi2CollisionOwner owner && VehicleTypes.Contains(owner.TypeName)
            ? (float[])VehicleRing.Clone()
            : (float[])Ring.Clone();
        Span<float> stretch = stackalloc float[8];
        for (int j = 0; j < 8; j++)
        {
            float x = ring[3 * j], y = ring[(3 * j) + 1], z = ring[(3 * j) + 2];
            Fpu reach = Fpu.Sqrt(((Fpu)y * y) + ((Fpu)x * x) + ((Fpu)z * z));
            stretch[j] = (((Fpu)moveLength / reach) + 1f).F;
            ring[3 * j] = (((Fpu)frame[0] * x) + ((Fpu)frame[2] * z) + ((Fpu)frame[1] * y)).F;
            ring[(3 * j) + 1] = (((Fpu)frame[3] * x) + ((Fpu)frame[5] * z) + ((Fpu)frame[4] * y)).F;
            ring[(3 * j) + 2] = (((Fpu)frame[6] * x) + ((Fpu)frame[8] * z) + ((Fpu)frame[7] * y)).F;
        }

        Fpu moveRegister = Fpu.Sqrt(dy2 + dx2);
        Span<Vector3d> near = stackalloc Vector3d[MaxSweepLines];
        int n = 0;
        foreach (float height in RingHeights[(int)Pose])
        {
            float hx = (((Fpu)frame[0] * 0f) + ((Fpu)frame[1] * 0f) + ((Fpu)frame[2] * height)).F;
            float hy = (((Fpu)frame[3] * 0f) + ((Fpu)frame[4] * 0f) + ((Fpu)frame[5] * height)).F;
            float hz = (((Fpu)frame[6] * 0f) + ((Fpu)frame[7] * 0f) + ((Fpu)frame[8] * height)).F;
            Fpu bx = (Fpu)hx + from.X, by = (Fpu)hy + from.Y, bz = (Fpu)hz + from.Z;
            for (int j = 0; j < 8; j++, n++)
            {
                float x = ring[3 * j], y = ring[(3 * j) + 1], z = ring[(3 * j) + 2];
                float s = stretch[j];
                _sweep.Starts[n] = new Vector3d(bx.Value, by.Value, bz.Value);
                near[n] = new Vector3d(((Fpu)x + bx).Value, ((Fpu)y + by).Value, ((Fpu)z + bz).Value);
                _sweep.Ends[n] = new Vector3d(
                    (((Fpu)s * x) + bx).Value,
                    (((Fpu)s * y) + by).Value,
                    ((Fpu)((Fpu)s * z).F + bz).Value);
            }

            if (dx != 0f || dy != 0f)
            {
                Fpu inverse = (Fpu)1f / moveRegister;
                float px = (((Fpu)dx * inverse) * RingReach).F;
                float py = (((Fpu)dy * inverse) * RingReach).F;
                float pz = (((Fpu)0f * inverse) * RingReach).F;
                _sweep.Starts[n] = new Vector3d(bx.Value, by.Value, bz.Value);
                _sweep.Ends[n] = new Vector3d(((Fpu)px + bx).Value, ((Fpu)py + by).Value, ((Fpu)pz + bz).Value);
                near[n] = _sweep.Ends[n];
                n++;
            }
        }

        // The ceiling line, straight up from a tenth of a metre above the feet.
        Vector3d ceilingStart = new(from.X, from.Y, ((Fpu)from.Z + CeilingStart).Value);
        Fpu ceilingEnd = (Fpu)ceilingStart.Z + CeilingReach;
        double top = (ceilingEnd + CeilingStart).Value;
        _sweep.Starts[n] = ceilingStart;
        _sweep.Ends[n] = new Vector3d(from.X, from.Y, ceilingEnd.Value);
        near[n] = ceilingStart;
        n++;

        // The collision body's lift raises every start and far end, but not the near ends.
        for (int i = 0; i < n; i++)
        {
            Vector3d start = _sweep.Starts[i], end = _sweep.Ends[i];
            _sweep.Starts[i] = new Vector3d(start.X, start.Y, ((Fpu)CollisionZOffset + start.Z).Value);
            _sweep.Ends[i] = new Vector3d(end.X, end.Y, ((Fpu)CollisionZOffset + end.Z).Value);
        }

        _sweep.Count = n;
        Surroundings.Collision.TraceLines(_sweep, SweepGroups);

        Sweep move = new(dx, dy);
        Span<int> walls = stackalloc int[MaxSweepLines];
        Span<float> wallNormals = stackalloc float[3 * MaxSweepLines];
        int wallCount = 0;
        for (int k = n - 1; k >= 0; k--)
        {
            Igi2Intersection hit = _sweep.Intersections[k];
            if (!hit.IsHit || !(_sweep.Starts[k].Z < top))
            {
                continue;
            }

            Vector3f raw = hit.Normal;
            float length = Fpu.Sqrt(((Fpu)raw.X * raw.X) + ((Fpu)raw.Y * raw.Y) + ((Fpu)raw.Z * raw.Z)).F;
            if (length == 0f)
            {
                continue;
            }

            Fpu inverse = (Fpu)1f / length;
            float nx = ((Fpu)raw.X * inverse).F, ny = ((Fpu)raw.Y * inverse).F, nz = (inverse * raw.Z).F;
            bool ceilingLine = k == n - 1;
            if (ceilingLine && nz < 0f)
            {
                Fpu below = ((Fpu)hit.Position.Z - _sweep.Starts[k].Z) + CeilingStart;
                top = hit.Position.Z;
                LimitPostureUnderCeiling(below);
                if (to.Z > from.Z)
                {
                    to = new Vector3d(to.X, to.Y, from.Z);
                }

                if (below.Value < ((Fpu)GroundCheckZOffset + 1228.8f).Value)
                {
                    move.SlideHorizontally(raw.X, raw.Y, 0.01f);
                    if (GroundIntersection.IsHit)
                    {
                        move.SlideHorizontally(GroundIntersection.Normal.X, GroundIntersection.Normal.Y, 0.01f);
                    }
                }

                continue;
            }

            if (!ceilingLine)
            {
                move.PushOut(hit.Position, near[k], _sweep.Starts[k], nx, ny, nz);
            }

            Locomotion.Flags |= WallSignal;
            if (move.SlideAlongWall(nx, ny))
            {
                walls[wallCount] = k;
                wallNormals[3 * wallCount] = nx;
                wallNormals[(3 * wallCount) + 1] = ny;
                wallNormals[(3 * wallCount) + 2] = nz;
                wallCount++;
            }

            move.Slide(nx, ny, nz);
        }

        float my = move.Y.F;
        Fpu mx = move.X;
        if (wallCount > 1 && IsCornered(walls[..wallCount], wallNormals))
        {
            mx = 0f;
            my = 0f;
        }

        move.AveragePush();
        double x1 = ((mx + move.PushX) + from.X).Value;
        double y1 = (((Fpu)my + move.PushY) + from.Y).Value;
        to = new Vector3d(x1, y1, to.Z);
        speed = new Vector3f(((Fpu)to.X - from.X).F, ((Fpu)to.Y - from.Y).F, ((Fpu)to.Z - from.Z).F);
    }

    /// <summary>Whether two of the walls slid along make a corner or a gap the body cannot pass.</summary>
    private bool IsCornered(ReadOnlySpan<int> walls, ReadOnlySpan<float> normals)
    {
        Vector3d first = _sweep.Intersections[walls[0]].Position;
        for (int i = 1; i < walls.Length; i++)
        {
            Fpu cosine = ((Fpu)normals[2] * normals[(3 * i) + 2]) + ((Fpu)normals[0] * normals[3 * i]) + ((Fpu)normals[1] * normals[(3 * i) + 1]);
            if (!(cosine.Value < SameWall))
            {
                continue;
            }

            if (!(cosine.Value < 0.0))
            {
                return true;
            }

            Vector3d other = _sweep.Intersections[walls[i]].Position;
            float gx = ((Fpu)first.X - other.X).F, gy = ((Fpu)first.Y - other.Y).F;
            Fpu gz = (Fpu)first.Z - other.Z;
            Fpu gap = Fpu.Sqrt(((Fpu)gx * gx) + ((Fpu)gy * gy) + (gz * gz));
            if (gap.Value < NarrowGap)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>What a ceiling this far above the feet leaves of the postures.</summary>
    private void LimitPostureUnderCeiling(Fpu below)
    {
        double d = below.Value;
        if (Pose == Igi2HumanPose.LyingFlat)
        {
            AllowedStance &= d < 6144f
                ? ~(Igi2AllowedStance.Stand | Igi2AllowedStance.Crouch | Igi2AllowedStance.Jump)
                : d < 7372.7998f ? ~(Igi2AllowedStance.Stand | Igi2AllowedStance.Jump) : ~Igi2AllowedStance.Jump;
            return;
        }

        if (d < 7372.7998f)
        {
            AllowedStance &= ~Igi2AllowedStance.Stand;
        }

        if (d < 9420.7998f)
        {
            AllowedStance &= ~Igi2AllowedStance.Jump;
        }
    }

    /// <summary>The sweep's frame: the body turned about the vertical only, from its forward axis.</summary>
    private float[] SweepFrame()
    {
        Matrix3f o = Orientation;
        float a = o.M12, b = o.M22;
        Fpu length = Fpu.Sqrt(((Fpu)a * a) + ((Fpu)b * b));
        if (!(length.Value > 0.0))
        {
            return [o.M11, o.M12, o.M13, o.M21, o.M22, o.M23, o.M31, o.M32, o.M33];
        }

        Fpu inverse = (Fpu)1f / length;
        Fpu forwardX = (Fpu)a * inverse, forwardY = inverse * b, forwardZ = inverse * 0f;
        Fpu z0 = forwardZ * 0f;
        float c0 = (forwardY - z0).F;
        float c1 = (-(forwardX - z0)).F;
        Fpu c2 = (forwardX * 0f) - (forwardY * 0f);
        float c2f = c2.F;
        Fpu side = Fpu.Sqrt(((Fpu)c0 * c0) + ((Fpu)c1 * c1) + (c2 * c2));
        float m0 = c0, m3 = c1, m6 = c2f;
        if (side.Value != 0.0)
        {
            Fpu inverseSide = (Fpu)1f / side;
            m0 = ((Fpu)c0 * inverseSide).F;
            m3 = ((Fpu)c1 * inverseSide).F;
            m6 = (inverseSide * c2f).F;
        }

        return [m0, forwardX.F, 0f, m3, forwardY.F, 0f, m6, forwardZ.F, 1f];
    }

    private static bool IsSweptThrough(object owner) =>
        owner is Igi2CollisionOwner { Depth: 0 } placed && SweepThrough.Contains(placed.TypeName);

    /// <summary>The move as the sweep reshapes it: what is left of it on the FPU, and the pushes out of walls.</summary>
    private sealed class Sweep(float dx, float dy)
    {
        public Fpu X { get; private set; } = dx;

        public Fpu Y { get; private set; } = dy;

        public Fpu Z { get; private set; } = 0f;

        public float PushX { get; private set; }

        public float PushY { get; private set; }

        public int Pushes { get; private set; }

        /// <summary>Out of a wall nearer than the ring's near end, by the depth along its normal, sideways only.</summary>
        public void PushOut(Vector3d hit, Vector3d near, Vector3d start, float nx, float ny, float nz)
        {
            float ex = ((Fpu)hit.X - near.X).F, ey = ((Fpu)hit.Y - near.Y).F, ez = ((Fpu)hit.Z - near.Z).F;
            Fpu ax = (Fpu)near.X - start.X, ay = (Fpu)near.Y - start.Y, az = (Fpu)near.Z - start.Z;
            if (!((((Fpu)ez * az) + (ay * ey) + (ax * ex)).Value < 0.0))
            {
                return;
            }

            float depth = (((Fpu)ez * nz) + ((Fpu)ey * ny) + ((Fpu)ex * nx)).F;
            Fpu px = (Fpu)depth * nx, py = (Fpu)depth * ny;
            if (!(depth > 0f))
            {
                px = -px;
                py = -py;
            }

            PushX = (px + PushX).F;
            PushY = (py + PushY).F;
            Pushes++;
        }

        /// <summary>Slides along a wall's horizontal direction; true when it took something off the move.</summary>
        public bool SlideAlongWall(float nx, float ny)
        {
            Fpu across = Fpu.Sqrt(((Fpu)nx * nx) + ((Fpu)ny * ny));
            return across.Value != 0.0 && SlideOn(nx, ny, across);
        }

        /// <summary>Slides along a surface's horizontal direction when there is enough of one.</summary>
        public void SlideHorizontally(float nx, float ny, float threshold)
        {
            Fpu across = Fpu.Sqrt(((Fpu)nx * nx) + ((Fpu)ny * ny));
            if (across.Value > threshold)
            {
                SlideOn(nx, ny, across);
            }
        }

        /// <summary>Takes off the move what heads into a surface, along its whole normal.</summary>
        public void Slide(float nx, float ny, float nz)
        {
            Fpu into = ((Fpu)nz * Z) + ((Fpu)ny * Y) + ((Fpu)nx * X);
            if (into.Value < 0.0)
            {
                X -= (Fpu)nx * into;
                Y -= ((Fpu)ny * into).F;
                Z -= ((Fpu)nz * into).F;
            }
        }

        /// <summary>The pushes averaged over the walls that pushed.</summary>
        public void AveragePush()
        {
            if (Pushes == 0)
            {
                return;
            }

            Fpu inverse = (Fpu)1f / (double)Pushes;
            PushX = ((Fpu)PushX * inverse).F;
            PushY = (inverse * PushY).F;
        }

        private bool SlideOn(float nx, float ny, Fpu across)
        {
            Fpu inverse = (Fpu)1f / across;
            float ux = ((Fpu)nx * inverse).F, uy = ((Fpu)ny * inverse).F;
            Fpu uz = inverse * 0f;
            Fpu into = (uz * Z) + ((Fpu)uy * Y) + ((Fpu)ux * X);
            if (!(into.Value < 0.0))
            {
                return false;
            }

            float intoF = into.F;
            X -= (Fpu)ux * intoF;
            Y -= ((Fpu)uy * intoF).F;
            Z -= (uz * intoF).F;
            return true;
        }
    }
}
