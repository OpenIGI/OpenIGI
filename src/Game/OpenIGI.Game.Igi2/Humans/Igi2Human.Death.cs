using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi2.Animation;
using OpenIGI.Game.Igi2.Collision;
using OpenIGI.Game.Physics;

namespace OpenIGI.Game.Igi2.Humans;

/// <summary>
/// A dead body coming to rest: it falls, is pushed out of what its limbs sink into, slides to a stop and lies along the
/// ground (<c>Human_HandleDeathCollision</c>, <c>Human_CheckBoneCollision</c>,
/// <c>Human_InterpolateZAxisToAveragedGroundNormal</c>).
/// </summary>
public abstract partial class Igi2Human
{
    /// <summary>The gravity a dead body falls by each tick, <c>44.600887</c>.</summary>
    private const float DeathGravity = 44.600887298583984f;

    /// <summary>Below this the body counts as on the ground: half a metre.</summary>
    private const double DeathGroundReach = 2048.0;

    /// <summary>The ticks on the ground after which a body whose death clip has ended lies still.</summary>
    private const int DeathStabilizationTicks = 150;

    /// <summary>A sideways speed below which a dead body stops sliding.</summary>
    private const float DeathSlideStop = 27.30666732788086f;

    /// <summary>The friction a dead body on the ground slides with.</summary>
    private const float DeathFriction = 0.699999988079071f;

    /// <summary>How far above the lower back the limb traces start: 0.2 metres.</summary>
    private const double BoneTraceLift = 819.2000122070312;

    /// <summary>The groups the limbs and the ground normal are traced against: the level's solids and its trees.</summary>
    private const uint DeathTraceGroups = 0x98000000 | Igi2CollisionWorld.ForestGroup;

    /// <summary>The bones a dead body's limbs are traced to from its lower back, in the original's order.</summary>
    private static readonly string[] DeathTraceBones =
    [
        "lower left leg", "left toe end", "lower right leg", "right toe end", "head end",
        "upper left arm", "lower left arm", "lower left fingers", "upper right arm", "lower right arm", "lower right fingers",
    ];

    private readonly WorldLineTrace _boneTrace = new(DeathTraceBones.Length + 3)
    {
        Mesh = 1,
        Includes = static owner => !Igi2ObjectCollision.PassesThrough(owner),
    };

    private readonly WorldLineTrace _groundNormalTrace = new(1) { Mesh = 1 };

    /// <summary>Gets or sets the ticks the dead body has spent near the ground, <c>nDeathStablizationTimer</c>.</summary>
    public int DeathStabilizationTimer { get; set; }

    /// <summary>Gets or sets how far the dead body's limbs push it out of what they sink into this tick, <c>tBoneCollisionData.tDeltaMovement</c>.</summary>
    public Vector3f BoneCollisionDelta { get; set; }

    /// <summary>Gets or sets the orientation the dead body lies in, <c>tDeadOrientation</c>: its orientation as of its last move.</summary>
    public Matrix3f DeadOrientation { get; set; } = Matrix3f.Identity;

    /// <summary>
    /// Eases the averaged ground normal a thirtieth of the way toward the ground found 0.1 m ahead of the body and 0.1 m up its
    /// own up axis, three metres down: <c>Human_InterpolateZAxisToAveragedGroundNormal</c>.
    /// </summary>
    public void InterpolateZAxisToAveragedGroundNormal()
    {
        // sin(0) and cos(0) by 409.6: the probe sits a tenth of a metre along the body's forward and up axes.
        Fpu side = Fpu.Sin(0f) * 409.6000061035156f;
        Fpu ahead = Fpu.Cos(0f) * 409.6000061035156f;
        Matrix3f o = Orientation;
        Fpu ox = ((ahead * o.M12) + (side * o.M11)) + ((Fpu)o.M13 * 409.6000061035156f);
        Fpu oy = ((ahead * o.M22) + (side * o.M21)) + ((Fpu)o.M23 * 409.6000061035156f);
        Fpu oz = ((ahead * o.M32) + (side * o.M31)) + ((Fpu)o.M33 * 409.6000061035156f);
        _groundNormalTrace.Starts[0] = new Vector3d((ox + Position.X).Value, (oy + Position.Y).Value, (oz + Position.Z).Value);
        WorldIntersection hit = _groundNormalTrace.Intersections[0];
        hit.IsHit = false;
        hit.HitObject = null;
        Surroundings.Collision.NextZ(_groundNormalTrace, [-12288.0], 0x98000000);
        if (!hit.IsHit)
        {
            return;
        }

        Vector3f n = Normalized(hit.Normal);
        Vector3f was = AveragedGroundNormal;
        Fpu share = (Fpu)1.0 / 1.0;
        AveragedGroundNormal = new Vector3f(
            (((((Fpu)n.X * share) - was.X) * 0.03333333507180214f) + was.X).F,
            (((((Fpu)n.Y * share) - was.Y) * 0.03333333507180214f) + was.Y).F,
            (((((Fpu)n.Z * share) - was.Z) * 0.03333333507180214f) + was.Z).F);
    }

    /// <summary>
    /// Moves a dead body: it counts its ticks near the ground, and until it lies still it falls, is pushed out by its limbs,
    /// loses the part of its speed that drove it into them, stops sliding when slow and slides with friction on the ground
    /// (<c>Human_HandleDeathCollision</c>).
    /// </summary>
    /// <param name="start">Where the move started.</param>
    /// <param name="to">Where it ends; the fall and the limbs' push are applied to it.</param>
    public void HandleDeathCollision(Vector3d start, ref Vector3d to)
    {
        BoneCollisionDelta = default;
        if (!IsDead)
        {
            return;
        }

        IsObjectCollision = false;
        int timer = DeathStabilizationTimer;
        bool settled = timer > DeathStabilizationTicks && IsDeathAnimationDone;
        IsDeathPositionStabilized = settled;
        if (DistanceToGround < DeathGroundReach)
        {
            DeathStabilizationTimer = timer + 1;
        }

        if (settled)
        {
            Speed = default;
            BoneCollisionDelta = default;
            return;
        }

        Vector3f saved = Speed;
        float savedZ = ((Fpu)saved.Z - DeathGravity).F;
        Fpu fallen = (Fpu)to.Z - start.Z;
        Fpu falling = !(fallen.Value >= 0.0) ? fallen : 0f;
        Speed = new Vector3f(((Fpu)to.X - start.X).F, ((Fpu)to.Y - start.Y).F, (falling - DeathGravity).F);
        CheckBoneCollision();

        Vector3f speed = Speed, delta = BoneCollisionDelta;
        Fpu toZ = (Fpu)speed.Z + delta.Z;
        Fpu toY = (Fpu)speed.Y + delta.Y;
        to = new Vector3d((((Fpu)speed.X + delta.X) + start.X).Value, (toY + start.Y).Value, (toZ + start.Z).Value);

        float sx = speed.X, sy = speed.Y, sz = savedZ;
        if (delta.X == 0f && delta.Y == 0f && delta.Z == 0f)
        {
            sx = saved.X;
            sy = saved.Y;
        }
        else
        {
            Vector3f push = Normalized(delta);
            Fpu into = (((Fpu)push.X * sx) + ((Fpu)push.Y * sy)) + ((Fpu)push.Z * savedZ);
            if (into.Value < 0.0)
            {
                float pushZ = (into * push.Z).F;
                sx = ((Fpu)sx - ((Fpu)push.X * into)).F;
                sy = ((Fpu)sy - (into * push.Y)).F;
                sz = ((Fpu)savedZ - pushZ).F;
            }
        }

        if (Math.Abs(sx) < DeathSlideStop && Math.Abs(sy) < DeathSlideStop)
        {
            sx = 0f;
            sy = 0f;
        }

        if (DistanceToGround < DeathGroundReach)
        {
            sx = ((Fpu)sx * DeathFriction).F;
            sy = ((Fpu)sy * DeathFriction).F;
        }

        Speed = new Vector3f(sx, sy, sz);
        BoneCollisionDelta = default;
    }

    /// <summary>
    /// Traces the dead body's limbs, from 0.2 m above its lower back to each limb's bone as it will be after this tick's speed, and
    /// sums how far out of what they sink into each would be pushed (<c>Human_CheckBoneCollision</c>).
    /// </summary>
    /// <remarks>
    /// Fourteen lines: the eleven limbs; one 0.35 m straight down from the lower back and one three metres down (traced, but its
    /// push not counted); and one fifteen metres up from the feet. Only surfaces facing no more than sixty degrees down count.
    /// Each counted line pushes back along the surface's normal by how far its end lies beyond it; the legs, toes and the short
    /// line below the back hold the body up by the most any of them asks. With more than one push, the sideways push is their
    /// mean.
    /// </remarks>
    public void CheckBoneCollision()
    {
        Vector3d back = BonePosition(BoneIndex("lower body"));
        Vector3d from = new(back.X, back.Y, ((Fpu)back.Z + BoneTraceLift).Value);
        int limbs = DeathTraceBones.Length;
        for (int i = 0; i < limbs + 2; i++)
        {
            _boneTrace.Starts[i] = from;
        }

        for (int i = 0; i < limbs; i++)
        {
            _boneTrace.Ends[i] = BonePosition(BoneIndex(DeathTraceBones[i]));
        }

        _boneTrace.Ends[limbs] = new Vector3d(from.X, from.Y, ((Fpu)from.Z - 1433.5999755859375).Value);
        _boneTrace.Ends[limbs + 1] = new Vector3d(from.X, from.Y, ((Fpu)from.Z - 12288.0).Value);
        _boneTrace.Starts[limbs + 2] = Position;
        _boneTrace.Ends[limbs + 2] = new Vector3d(Position.X, Position.Y, ((Fpu)Position.Z + 61440.0).Value);

        Vector3f speed = Speed;
        for (int i = 0; i < _boneTrace.Ends.Length; i++)
        {
            Vector3d end = _boneTrace.Ends[i];
            _boneTrace.Ends[i] = new Vector3d(((Fpu)speed.X + end.X).Value, ((Fpu)speed.Y + end.Y).Value, ((Fpu)speed.Z + end.Z).Value);
            WorldIntersection reset = _boneTrace.Intersections[i];
            reset.IsHit = false;
            reset.HitObject = null;
        }

        Surroundings.Collision.TraceLines(_boneTrace, DeathTraceGroups);

        float dx = 0f, dy = 0f, up = 0f;
        int pushes = 0;
        for (int i = 0; i < _boneTrace.Ends.Length; i++)
        {
            WorldIntersection hit = _boneTrace.Intersections[i];
            if (!hit.IsHit || !(hit.Normal.Z > -0.5f))
            {
                continue;
            }

            Vector3d end = _boneTrace.Ends[i];
            Fpu ex = (Fpu)end.X - hit.Position.X, ey = (Fpu)end.Y - hit.Position.Y;
            float ez = ((Fpu)end.Z - hit.Position.Z).F;
            Vector3f n = hit.Normal;
            if (n.X != 0f || n.Y != 0f || n.Z != 0f)
            {
                // This normal's length sums Z first: (z*z + x*x) + y*y.
                Fpu inverse = (Fpu)1.0 / Fpu.Sqrt((((Fpu)n.Z * n.Z) + ((Fpu)n.X * n.X)) + ((Fpu)n.Y * n.Y));
                n = new Vector3f(((Fpu)n.X * inverse).F, ((Fpu)n.Y * inverse).F, ((Fpu)n.Z * inverse).F);
                hit.Normal = n;
            }

            float beyond = ((((Fpu)ez * n.Z) + (ex * n.X)) + (ey * n.Y)).F;
            if (i == limbs + 1 || !(beyond < 0f))
            {
                continue;
            }

            dx = ((Fpu)dx - ((Fpu)beyond * n.X)).F;
            dy = ((Fpu)dy - ((Fpu)beyond * n.Y)).F;
            if (i == limbs || i < 4)
            {
                float lift = (-((Fpu)beyond * n.Z)).F;
                up = lift <= up ? up : lift;
            }

            pushes++;
        }

        if (pushes > 1)
        {
            Fpu mean = (Fpu)1.0 / pushes;
            dx = (mean * dx).F;
            dy = (mean * dy).F;
        }

        BoneCollisionDelta = new Vector3f(dx, dy, up);
    }

    /// <summary>A vector scaled to unit length by the reciprocal of its length, as the original normalises.</summary>
    private static Vector3f Normalized(Vector3f v)
    {
        Fpu inverse = (Fpu)1.0 / Fpu.Sqrt((((Fpu)v.X * v.X) + ((Fpu)v.Y * v.Y)) + ((Fpu)v.Z * v.Z));
        return new Vector3f(((Fpu)v.X * inverse).F, ((Fpu)v.Y * inverse).F, ((Fpu)v.Z * inverse).F);
    }

    private int BoneIndex(string bone) => Model is { } model ? Igi2BoneBranches.IndexOf(model.BoneNames, bone) : -1;
}
