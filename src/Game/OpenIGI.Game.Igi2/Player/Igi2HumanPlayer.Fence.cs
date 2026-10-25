using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi2.Humans;
using OpenIGI.Game.Igi2.Interaction;
using OpenIGI.Game.Igi2.World;

namespace OpenIGI.Game.Igi2.Player;

/// <content>Fences: the shock of a live one, telling the notified objects where the player is, and climbing over a dead one.</content>
public sealed partial class Igi2HumanPlayer : IIgi2ElectricTarget
{
    /// <summary>The ticks between two shocks, <c>45</c>.</summary>
    public const int ElectricTimeout = 45;

    /// <summary>The ticks a fence climb's activation counts, <c>180</c>.</summary>
    public const int FenceClimbTicks = 180;

    /// <summary>The sound a shock makes on a living player.</summary>
    public const string ElectricSparkSound = "elfence_spark1";

    /// <summary>
    /// Gets or sets the ticks before a fence may shock the player again, <c>nElectricDamageTimeOut</c>; a shock sets it to 45 and each
    /// tick counts it down.
    /// </summary>
    public int ElectricDamageTimeout { get; set; }

    /// <summary>Gets a value indicating whether the player is about to climb a fence, <c>isInitClimbFence</c>.</summary>
    public bool IsInitClimbFence { get; private set; }

    /// <summary>Gets a value indicating whether the player is climbing a fence, <c>isClimbFence</c>.</summary>
    public bool IsClimbFence { get; private set; }

    /// <summary>Gets a value indicating whether the player is about to get off a fence, <c>isDeInitClimbFence</c>.</summary>
    public bool IsDeInitClimbFence { get; private set; }

    /// <summary>
    /// A shock from a live fence, <c>HumanPlayer_ElectricDamageHandler</c>: while active and out of the timeout, the chest takes the
    /// fence's damage scale times its multiplier, the timeout starts, the view shakes and flashes red, and a living player sparks.
    /// </summary>
    public void ElectricDamage()
    {
        if (!IsActive || ElectricDamageTimeout != 0)
        {
            return;
        }

        Damage.LastPart = Igi2DamagePart.Chest;
        ElectricDamageTimeout = ElectricTimeout;
        Damage.Add(Igi2DamagePart.Chest, ((Fpu)General.ElectricFenceDamageScale * Damage.Multiplier(Igi2DamagePart.Chest)).F);
        View.ShakeFactor = 1f;
        View.ShakeRange = 204.8f;
        View.ShakeAngle = 0.013089969754219055f;
        View.ShakeTime = 0.02222222276031971f;
        for (int i = 0; i < _damageFlash.Length; i++)
        {
            _damageFlash[i] = (((Fpu)Random.NextDouble() + 1.0) * 0.4).F;
        }

        if (Damage.Damage < Damage.MaxDamage)
        {
            Surroundings.StartSound(ElectricSparkSound);
        }
    }

    /// <summary>
    /// Gets on or off the fence being activated, <c>HumanPlayer_ClimbFence</c>: run after both machines each tick while the player is
    /// getting on or is on one.
    /// </summary>
    /// <remarks>
    /// Getting on: onto the fence's line at the player's place along it, facing over it from whichever side the player is on, stopped
    /// and locked to it with the view looking ahead and the collision off, for the camera's third mode. On it, the climb is heard. Getting
    /// off puts everything back, the standing clip, and a dead player falls.
    /// </remarks>
    private void ClimbFence()
    {
        if (IsInitClimbFence)
        {
            if (ActivatingTarget is not IIgi2Fence fence)
            {
                IsInitClimbFence = false;
                return;
            }

            IReadOnlyList<float> m = fence.Frame;
            Fpu dx = (Fpu)Position.X - fence.Position.X, dy = (Fpu)Position.Y - fence.Position.Y, dz = (Fpu)Position.Z - fence.Position.Z;
            Fpu along = (((Fpu)m[3] * dy) + ((Fpu)m[6] * dz)) + ((Fpu)m[0] * dx);
            float turn = ((((Fpu)m[4] * dy) + ((Fpu)m[7] * dz)) + ((Fpu)m[1] * dx)).Value >= 0.0 ? HalfTurn : 0f;
            PopPosition(new Vector3d(
                (((((Fpu)m[2] + m[1]) * 0.0) + ((Fpu)m[0] * along)) + fence.Position.X).Value,
                (((((Fpu)m[5] + m[4]) * 0.0) + ((Fpu)m[3] * along)) + fence.Position.Y).Value,
                (((((Fpu)m[8] + m[7]) * 0.0) + ((Fpu)m[6] * along)) + fence.Position.Z).Value));
            GroundMovementSpeed = default;
            PopOrientation(((Fpu)Igi2Pose.Gamma(m[0], m[3]) + turn).F);
            Speed = default;
            ImpactSpeed = default;
            SlidingSpeed = default;
            IsInitClimbFence = false;
            IsClimbFence = true;
            IsActivating = true;
            IsLockOrientation = true;
            View.LookAhead();
            View.IsLockOrientation = true;
            IsDisableCollision = true;
            SnapFromGround();
            IsDisableDeltaTranslationScale = true;
            CameraMode = 3;
            IsLockAction = true;
            Go(Igi2PlayerState.ClimbFence);
            return;
        }

        if (IsDeInitClimbFence)
        {
            IsDeInitClimbFence = false;
            IsClimbFence = false;
            EndActivate();
            IsLockOrientation = false;
            View.IsLockOrientation = false;
            CameraMode = 1;
            IsDisableCollision = false;
            IsDisableDeltaTranslationScale = false;
            IsLockAction = false;
            Animation.PlayState(Igi2PlayerState.Stand, 0, 0f);
            if (Has(Igi2PlayerSignal.Dead))
            {
                Go(Igi2PlayerState.Die);
            }

            return;
        }

        Surroundings.SendAiEvent(new Ai.Igi2AiEvent(Ai.Igi2AiEvent.Fence, Team, 1090, 20480f, Position, default, this, this));
    }

    // State 49 -- HumanPlayer_InitStateClimbFence: the posture kept and standing, the clip begun, the activate timer counting to 180,
    // and tba_fenceclimb.
    private void EnterClimbFence()
    {
        StanceBeforeClimb = Pose;
        Pose = Igi2HumanPose.Standing;
        BeginLadderState(fromEnd: false);
        TimedActionTime = FenceClimbTicks;
        TimedActionTimer = 0;
        Surroundings.StartSound("tba_fenceclimb");
        Locomotion.Current = (_, _) => RunClimbFence();
    }

    // HumanPlayer_RunStateClimbFence: the timer counts; at the clip's end, 1.5 m on and 2 m up, in the old posture, falling.
    private void RunClimbFence()
    {
        MoveOnGround(0f, GroundAcceleration, 0f, GroundAcceleration);
        if (TimedActionTimer < TimedActionTime)
        {
            TimedActionTimer++;
        }

        Igi2PlayerSignal signal = Signal;
        if ((signal & Igi2PlayerSignal.AnimationEnded) != 0)
        {
            IsDeInitClimbFence = true;
            Matrix3f o = Orientation;
            PopPosition(new Vector3d(
                (((((Fpu)o.M13 * 8192.0) + ((Fpu)o.M12 * 6144.0)) + ((Fpu)o.M11 * 0.0)) + Position.X).Value,
                (((((Fpu)o.M23 * 8192.0) + ((Fpu)o.M22 * 6144.0)) + ((Fpu)o.M21 * 0.0)) + Position.Y).Value,
                (((((Fpu)o.M33 * 8192.0) + ((Fpu)o.M32 * 6144.0)) + ((Fpu)o.M31 * 0.0)) + Position.Z).Value));
            GroundMovementSpeed = default;
            Pose = StanceBeforeClimb;
            Go(Igi2PlayerState.Fall);
        }
        else if ((signal & Igi2PlayerSignal.Dead) != 0)
        {
            IsDeInitClimbFence = true;
        }
    }
}
