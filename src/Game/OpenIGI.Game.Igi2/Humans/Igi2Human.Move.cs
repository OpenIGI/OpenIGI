using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi2.Collision;
using OpenIGI.Game.Igi2.World;

namespace OpenIGI.Game.Igi2.Humans;

/// <summary>The move: the speeds a body is integrated by, and the platform it rides.</summary>
public abstract partial class Igi2Human
{
    /// <summary>Above this ground normal's height a slope is walked on; below it the body slides, the float <c>0x3F248DE3</c>.</summary>
    private const float SlideSlope = 0.64279f;

    /// <summary>The sliding speed's scale, <c>0x450E3000</c>.</summary>
    private const float SlideSpeed = 2275f;

    /// <summary>The steepest a slide may pull down, <c>0xC3CCCCCD</c>.</summary>
    private const float SlideFloor = -409.6f;

    /// <summary>A quarter turn, <c>0x3FC90FDB</c>, scaling a normal's height into the angle whose sine slows the step.</summary>
    private const float QuarterTurn = 1.5707964f;

    /// <summary>The share of the way to the ideal slide a tick closes, <c>0x3DCCCCCD</c>.</summary>
    private const float SlideEase = 0.1f;

    /// <summary>What is left of an impact after a tick, <c>0x3F666666</c>.</summary>
    private const float ImpactDecay = 0.9f;

    /// <summary>
    /// A step along the ground, <c>Human_MoveOnGround</c> (<c>igi2 0x503570</c>): a speed forward, turned by a heading off the
    /// body's and then by the body, taken as the animation's step.
    /// </summary>
    /// <param name="speed">The step forward.</param>
    /// <param name="gamma">The heading off the body's the step is taken at.</param>
    protected void StepOnGround(float speed, float gamma)
    {
        float[] r = Igi2Pose.Rotation(0f, 0f, gamma);
        Fpu a = (((Fpu)r[2] + r[0]) * 0f) + ((Fpu)r[1] * speed);
        Fpu b = (((Fpu)r[5] + r[3]) * 0f) + ((Fpu)r[4] * speed);
        Fpu c = (((Fpu)r[8] + r[6]) * 0f) + ((Fpu)r[7] * speed);
        Matrix3f o = Orientation;
        float x = ((c * o.M13) + (b * o.M12) + (a * o.M11)).F;
        float y = ((c * o.M23) + (b * o.M22) + (a * o.M21)).F;
        float z = ((c * o.M33) + (b * o.M32) + (a * o.M31)).F;
        DeltaTranslation = new Vector3f(x, y, z);
    }

    /// <summary>Gets or sets the step the animation takes this tick, <c>Human_s.tDeltaTranslation</c>.</summary>
    public Vector3f DeltaTranslation { get; set; }

    /// <summary>Gets or sets a value indicating whether the animation's step is ignored, <c>isDisableDeltaTranslation</c>.</summary>
    public bool IsDisableDeltaTranslation { get; set; }

    /// <summary>Gets or sets the speed a slope slides the body at, <c>tSlidingSpeed</c>.</summary>
    public Vector3f SlidingSpeed { get; set; }

    /// <summary>Gets or sets a value indicating whether the body passes through everything, <c>isDisableCollision</c>.</summary>
    public bool IsDisableCollision { get; set; }

    /// <summary>Gets or sets a value indicating whether walls stop the body, <c>isObjectCollision</c>.</summary>
    public bool IsObjectCollision { get; set; }

    /// <summary>Gets or sets a value indicating whether the body stands on the ground, <c>isGroundCollision</c>.</summary>
    /// <remarks>On for every human (<c>Human_CreateHandler</c>, <c>igi2 0x4FDE9C</c>).</remarks>
    public bool IsGroundCollision { get; set; } = true;

    /// <summary>Gets or sets a value indicating whether the body bumps other humans, <c>isHumanCollision</c>.</summary>
    public bool IsHumanCollision { get; set; }

    /// <summary>Gets the change of speed over the last move, <c>tAcceleration</c>.</summary>
    public Vector3f Acceleration { get; private set; }

    /// <summary>Gets how far the body moved in the world over the last move, <c>tWorldSpeed</c>.</summary>
    public Vector3f WorldSpeed { get; private set; }

    /// <summary>Gets or sets where the body stood after the move before, <c>tWorldPrevPos</c>.</summary>
    public Vector3d WorldPreviousPosition { get; set; }

    /// <summary>Gets or sets the body's turn about the vertical, <c>vGamma</c>, in radians.</summary>
    public float Gamma { get; set; }

    /// <summary>Gets or sets the turn the body is heading for, <c>vIdealGamma</c>.</summary>
    public float IdealGamma { get; set; }

    /// <summary>Gets or sets a value indicating whether the body was just popped out of something and skips one platform step, <c>isPopped</c>.</summary>
    public bool IsPopped { get; set; }

    /// <summary>Gets how far the platform underfoot moved last tick, <c>tGroundSpeed</c>.</summary>
    public Vector3f GroundSpeed { get; private set; }

    /// <summary>Gets where the body stands relative to the platform, <c>tGroundRelativePos</c>.</summary>
    public Vector3d GroundRelativePosition { get; private set; }

    private readonly float[] _groundInverseOriginal = [1f, 0f, 0f, 0f, 1f, 0f, 0f, 0f, 1f];
    private readonly float[] _groundRelativeOrientation = [1f, 0f, 0f, 0f, 1f, 0f, 0f, 0f, 1f];

    /// <summary>
    /// One move of the body: <c>Human_Move</c> (<c>igi2 0x5015F0</c>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// The platform underfoot carries the start first. The speed is then the body's own, plus whatever a conveyor
    /// pushes. On ground the animation's step is slowed by the slope (the sine of the normal's height as a quarter
    /// turn), and a slope steeper than about fifty degrees sets an ideal slide that the sliding speed eases a tenth of
    /// the way toward each tick. The step, the slide and a decaying impact are added, and what the body carries into
    /// the air is that sum plus the platform's own speed.
    /// </para>
    /// <para>
    /// The target is checked against the walls, then the ground, then other humans. The body then stands there; its
    /// acceleration and its speed through the world are measured, and its place on the platform is noted again. The
    /// sum's first axis stays on the FPU through every addition, while the other two are stored at each step, as the
    /// original does.
    /// </para>
    /// </remarks>
    public void Move()
    {
        uint signal = Locomotion.Flags;
        Vector3f own = Speed;
        Vector3d start = Position;
        bool forcePosition = false;
        bool forceOrientation = false;
        UpdatePlatformMovement(ref start, updatePosition: true, updateOrientation: true);

        Fpu x = own.X;
        float y = own.Y, z = own.Z;
        if (GroundCollider is { } ground && Surroundings.PushOnConveyor(ground, GroundIntersection) is { } push)
        {
            x = (Fpu)push.X + own.X;
            y = ((Fpu)push.Y + y).F;
            z = ((Fpu)push.Z + z).F;
            IsProtectedFromFallDamage = push.X != 0f || push.Y != 0f || push.Z != 0f;
        }

        IdealSlidingSpeed = default;
        if (GroundIntersection.IsHit && (signal & GroundedSignal) != 0)
        {
            Vector3f normal = GroundIntersection.Normal;
            Fpu slow = Fpu.Sin((Fpu)normal.Z * QuarterTurn);
            Vector3f delta = DeltaTranslation;
            DeltaTranslation = new Vector3f((slow * delta.X).F, (slow * delta.Y).F, (slow * delta.Z).F);
            if (normal.Z < SlideSlope)
            {
                Fpu down = (((Fpu)normal.Z * normal.Z) - 1f) * SlideSpeed;
                Fpu clamped = down.Value < SlideFloor ? SlideFloor : !(0.0 < down.Value) ? down : 0f;
                IdealSlidingSpeed = new Vector3f(
                    (((Fpu)normal.Z * normal.X) * SlideSpeed).F,
                    (((Fpu)normal.Z * normal.Y) * SlideSpeed).F,
                    clamped.F);
            }
        }

        Vector3f ideal = IdealSlidingSpeed, sliding = SlidingSpeed;
        Fpu slideX = (((Fpu)ideal.X - sliding.X) * SlideEase) + sliding.X;
        float slideY = ((((Fpu)ideal.Y - sliding.Y) * SlideEase) + sliding.Y).F;
        float slideZ = ((((Fpu)ideal.Z - sliding.Z) * SlideEase) + sliding.Z).F;
        SlidingSpeed = new Vector3f(slideX.F, slideY, slideZ);

        if (!IsDisableDeltaTranslation)
        {
            Vector3f delta = DeltaTranslation;
            x += delta.X;
            y = ((Fpu)y + delta.Y).F;
            z = ((Fpu)z + delta.Z).F;
            if (x.Value != 0.0 || y != 0f || z != 0f)
            {
                forcePosition = true;
                forceOrientation = true;
            }

            DeltaTranslation = default;
        }

        y = ((Fpu)slideY + y).F;
        z = ((Fpu)slideZ + z).F;
        Vector3f impact = ImpactSpeed;
        Fpu impactX = (Fpu)impact.X * ImpactDecay;
        float impactY = ((Fpu)impact.Y * ImpactDecay).F, impactZ = ((Fpu)impact.Z * ImpactDecay).F;
        ImpactSpeed = new Vector3f(impactX.F, impactY, impactZ);

        // X sums impact and slide first, then the move (Human_Move: v15 + v14 + vX); Y and Z add the slide in earlier.
        Vector3f speed = new(((impactX + slideX) + x).F, ((Fpu)impactY + y).F, ((Fpu)impactZ + z).F);
        AccumulatedSpeed = speed;
        if (IsGroundRigidObject)
        {
            Vector3f carried = GroundSpeed;
            AccumulatedSpeed = new Vector3f(
                ((Fpu)carried.X + speed.X).F,
                ((Fpu)carried.Y + speed.Y).F,
                ((Fpu)carried.Z + speed.Z).F);
        }

        Vector3d to = new(((Fpu)speed.X + start.X).Value, ((Fpu)speed.Y + start.Y).Value, ((Fpu)speed.Z + start.Z).Value);
        Locomotion.Flags = signal & ~LowRoofSignal;
        if (!IsDisableCollision)
        {
            if (IsObjectCollision)
            {
                ObjectCollision(start, ref to, out speed);
            }

            if (IsGroundCollision)
            {
                GroundCollision(start, ref to, GroundTolerance);
            }

            if (IsHumanCollision)
            {
                HumanCollision(start, ref to, out speed);
            }
        }

        if (IsDead)
        {
            if (GroundIntersection.HitObject is { } under)
            {
                SetGround(under);
            }

            // Until it lies still a body tilts onto the ground, keeps its orientation as the dead one, and falls and slides.
            if (!IsDeathPositionStabilized)
            {
                InterpolateZAxisToAveragedGroundNormal();
                DeadOrientation = Orientation;
                HandleDeathCollision(start, ref to);
                forcePosition = true;
                forceOrientation = true;
            }
        }
        else
        {
            BoneCollisionDelta = default;
        }

        Position = to;
        Vector3f total = TotalSpeed;
        Acceleration = new Vector3f(((Fpu)speed.X - total.X).F, ((Fpu)speed.Y - total.Y).F, ((Fpu)speed.Z - total.Z).F);
        TotalSpeed = speed;
        UpdatePlatformPosition(forcePosition, forceOrientation);
        Vector3d now = Position, before = WorldPreviousPosition;
        WorldSpeed = new Vector3f(((Fpu)now.X - before.X).F, ((Fpu)now.Y - before.Y).F, ((Fpu)now.Z - before.Z).F);
        WorldPreviousPosition = now;
    }

    /// <summary>
    /// Carries the body with the rigid object it stands on: <c>Human_UpdatePlatformMovement</c> (<c>igi2 0x502780</c>).
    /// </summary>
    /// <param name="position">The position to carry, which the platform's turn and travel since the body was placed on it move.</param>
    /// <param name="updatePosition">Whether to carry the position, and measure the platform's speed.</param>
    /// <param name="updateOrientation">Whether to turn the body with the platform.</param>
    /// <remarks>
    /// A body that was just popped out skips one step. The body's turn is read back from the platform's orientation
    /// times the body's orientation relative to it, through <c>Real32x9_GetAngles</c>; the position is the platform's
    /// plus the relative position turned by the inverse of the platform's orientation when the body was placed, times its
    /// orientation now. On a platform that does not move that gives back the same place, as nearly as the arithmetic
    /// allows; the original recomputes it every tick all the same.
    /// </remarks>
    public void UpdatePlatformMovement(ref Vector3d position, bool updatePosition, bool updateOrientation)
    {
        if (IsPopped)
        {
            IsPopped = false;
            return;
        }

        if (!IsGroundRigidObject || GroundCollider is not Igi2RigidObject platform)
        {
            return;
        }

        float[] p = platform.Orientation;
        Vector3d platformPosition = platform.Position;
        if (updateOrientation)
        {
            float[] r = _groundRelativeOrientation;
            float m0 = (((Fpu)p[2] * r[6]) + ((Fpu)p[0] * r[0]) + ((Fpu)p[1] * r[3])).F;
            float m3 = (((Fpu)p[5] * r[6]) + ((Fpu)p[3] * r[0]) + ((Fpu)p[4] * r[3])).F;
            float gamma = Igi2Pose.Gamma(m0, m3);
            Gamma = IdealGamma;
            IdealGamma = gamma;
        }

        if (!updatePosition)
        {
            return;
        }

        float[] i = _groundInverseOriginal;
        Fpu q00 = ((Fpu)p[6] * i[2]) + ((Fpu)p[0] * i[0]) + ((Fpu)p[3] * i[1]);
        Fpu q01 = ((Fpu)p[7] * i[2]) + ((Fpu)p[1] * i[0]) + ((Fpu)p[4] * i[1]);
        Fpu q02 = ((Fpu)p[8] * i[2]) + ((Fpu)p[2] * i[0]) + ((Fpu)p[5] * i[1]);
        float q10 = (((Fpu)p[6] * i[5]) + ((Fpu)p[0] * i[3]) + ((Fpu)p[3] * i[4])).F;
        float q11 = (((Fpu)p[7] * i[5]) + ((Fpu)p[1] * i[3]) + ((Fpu)p[4] * i[4])).F;
        float q12 = (((Fpu)p[8] * i[5]) + ((Fpu)p[2] * i[3]) + ((Fpu)p[5] * i[4])).F;
        float q20 = (((Fpu)p[6] * i[8]) + ((Fpu)p[0] * i[6]) + ((Fpu)p[3] * i[7])).F;
        float q21 = (((Fpu)p[7] * i[8]) + ((Fpu)p[1] * i[6]) + ((Fpu)p[4] * i[7])).F;
        float q22 = (((Fpu)p[8] * i[8]) + ((Fpu)p[2] * i[6]) + ((Fpu)p[5] * i[7])).F;
        Vector3d rel = GroundRelativePosition;
        Fpu dx = (q02 * rel.Z) + (q01 * rel.Y) + (q00 * rel.X);
        Fpu dy = ((Fpu)q12 * rel.Z) + ((Fpu)q11 * rel.Y) + ((Fpu)q10 * rel.X);
        double dz = (((Fpu)q22 * rel.Z) + ((Fpu)q21 * rel.Y) + ((Fpu)q20 * rel.X)).Value;
        position = new Vector3d(
            (dx + platformPosition.X).Value,
            (dy + platformPosition.Y).Value,
            ((Fpu)dz + platformPosition.Z).Value);

        Vector3d previous = GroundPreviousPosition;
        Fpu sx = (Fpu)platformPosition.X - previous.X, sy = (Fpu)platformPosition.Y - previous.Y;
        double sz = ((Fpu)platformPosition.Z - previous.Z).Value;
        GroundPreviousPosition = platformPosition;
        GroundSpeed = new Vector3f(sx.F, sy.F, ((Fpu)sz).F);
        IsPlatformMoving = sx.Value != 0.0 || sy.Value != 0.0 || sz != 0.0;
    }

    /// <summary>
    /// Notes where the body stands on its platform: <c>Human_UpdatePlatformPos</c> (<c>igi2 0x502D00</c>).
    /// </summary>
    /// <param name="forcePosition">Note the position even if the platform did not ask.</param>
    /// <param name="forceOrientation">Note the orientation even if the platform did not ask.</param>
    /// <remarks>
    /// Taken when the body first stands on a platform, and after every move the animation stepped. The relative position
    /// is the body's less the platform's, with the platform's orientation now kept transposed; the relative orientation
    /// is that transpose times the body's.
    /// </remarks>
    public void UpdatePlatformPosition(bool forcePosition, bool forceOrientation)
    {
        if (!IsUpdatePlatformPos && !forcePosition && !forceOrientation)
        {
            return;
        }

        if (GroundCollider is not Igi2RigidObject platform)
        {
            if (GroundCollider is not null)
            {
                IsGroundRigidObject = false;
            }

            return;
        }

        float[] p = platform.Orientation;
        Vector3d platformPosition = platform.Position;
        if (IsUpdatePlatformPos || forcePosition)
        {
            Vector3d here = Position;
            GroundRelativePosition = new Vector3d(
                ((Fpu)here.X - platformPosition.X).Value,
                ((Fpu)here.Y - platformPosition.Y).Value,
                ((Fpu)here.Z - platformPosition.Z).Value);
            GroundPreviousPosition = platformPosition;
            GroundSpeed = default;
            float[] inverse = [p[0], p[3], p[6], p[1], p[4], p[7], p[2], p[5], p[8]];
            inverse.CopyTo(_groundInverseOriginal, 0);
        }

        if (IsUpdatePlatformPos || forceOrientation)
        {
            Matrix3f o = Orientation;
            float[] b = [o.M11, o.M12, o.M13, o.M21, o.M22, o.M23, o.M31, o.M32, o.M33];
            float[] r = _groundRelativeOrientation;
            for (int row = 0; row < 3; row++)
            {
                for (int column = 0; column < 3; column++)
                {
                    r[(3 * row) + column] = (((Fpu)p[6 + row] * b[6 + column]) + ((Fpu)p[row] * b[column]) + ((Fpu)p[3 + row] * b[3 + column])).F;
                }
            }
        }

        IsUpdatePlatformPos = false;
        IsGroundRigidObject = true;
    }
}
