using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi2.Humans;

namespace OpenIGI.Game.Igi2.Player;

/// <content>Jumping, falling, and the landing both end in.</content>
public sealed partial class Igi2HumanPlayer
{
    /// <summary>The speed gravity takes off the vertical each tick in the air.</summary>
    /// <remarks>The float <c>0x42A97BBF</c> in <c>HumanPlayer_InAirControl</c> (<c>igi2 0x49FCC0</c>).</remarks>
    public const float GravityPerTick = 84.741692f;

    /// <summary>The landing speed, in metres per second, above which a landing hurts.</summary>
    public const double SafeLandingSpeed = 16.5;

    // The fraction of the maximum damage each m/s over the safe speed costs, a float in the original.
    private const float LandingDamagePerMetrePerSecond = 0.07f;

    private bool _fallDamageProtected;

    // State 36 -- InitStateJump 0x4ABA60, RunStateJump 0x4ABCB0, ExitStateJump 0x4ABC70.
    private void EnterJump()
    {
        // There is no jumping off a lift: the jump ends before it starts.
        if (Surroundings.IsOnElevator)
        {
            Go(Igi2PlayerState.Stand);
            return;
        }

        Animation.PlayState(Igi2PlayerState.Jump, 0, StateBlendSeconds);
        AimJumpPrecision(Pose == Igi2HumanPose.Crouching);
        Surroundings.PlaySound("jump_1");

        Vector3f carried = AccumulatedSpeed;
        Speed = new Vector3f(carried.X, carried.Y, ((Fpu)General.UpwardJumpSpeed + carried.Z).F);
        Signal &= ~Igi2PlayerSignal.Grounded;
        LeaveGround();
        GroundTolerance = 0f;
        Locomotion.Current = (_, _) => RunJump();
    }

    private void RunJump()
    {
        ControlInAir();
        if (Speed.Z <= 0f)
        {
            GroundTolerance = LandingGroundTolerance;
        }

        AimJumpPrecision(Has(Igi2PlayerSignal.Crouch));

        if (Has(Igi2PlayerSignal.Swimming))
        {
            Surroundings.PlaySound("water_land_02");
            Go(Igi2PlayerState.TreadWater);
            return;
        }

        if (!Has(Igi2PlayerSignal.Grounded))
        {
            return;
        }

        PlayLandingSound();
        if (Damage.IsFatal)
        {
            UpdateDie();
            Go(Igi2PlayerState.Die);
            return;
        }

        Go(LandingGait());
    }

    private void ExitJump()
    {
        Speed = default;
        ImpactSpeed = default;
        GroundTolerance = LandingGroundTolerance;
    }

    /// <summary>The gait a jump lands into: the held direction in the posture and pace it left with, or still.</summary>
    private int LandingGait()
    {
        (int forward, int backward, int right, int left, int still) =
            Pose == Igi2HumanPose.Crouching
                ? (Igi2PlayerState.CrouchWalkForward, Igi2PlayerState.CrouchWalkBackward, Igi2PlayerState.CrouchWalkRight, Igi2PlayerState.CrouchWalkLeft, Igi2PlayerState.Crouch)
                : Has(Igi2PlayerSignal.Walking)
                    ? (Igi2PlayerState.WalkForward, Igi2PlayerState.WalkBackward, Igi2PlayerState.WalkRight, Igi2PlayerState.WalkLeft, Igi2PlayerState.Stand)
                    : (Igi2PlayerState.RunForward, Igi2PlayerState.RunBackward, Igi2PlayerState.RunRight, Igi2PlayerState.RunLeft, Igi2PlayerState.Stand);

        return Has(Igi2PlayerSignal.Forward) ? forward
            : Has(Igi2PlayerSignal.Backward) ? backward
            : Has(Igi2PlayerSignal.Right) ? right
            : Has(Igi2PlayerSignal.Left) ? left
            : still;
    }

    /// <summary>A jump aims the running precision plus the jumping modifier, and the crouched walk's too when crouched.</summary>
    private void AimJumpPrecision(bool crouched)
    {
        if (crouched)
        {
            AimPrecision(Igi2PrecisionType.CrouchJumping, static p => (Fpu)p.JumpingModifier + p.CrouchWalking + p.Running);
        }
        else
        {
            AimPrecision(Igi2PrecisionType.Jumping, static p => (Fpu)p.JumpingModifier + p.Running);
        }
    }

    // State 37 -- InitStateFall 0x4AC1F0, RunStateFall 0x4AC2E0.
    private void EnterFall()
    {
        Animation.PlayState(Igi2PlayerState.Fall, 0, 0f);
        Speed = AccumulatedSpeed;
        AimPrecision(Igi2PrecisionType.Running, static p => p.Running);
        Locomotion.Current = (_, _) => RunFall();
    }

    private void RunFall()
    {
        ControlInAir();

        if (Has(Igi2PlayerSignal.Swimming))
        {
            Surroundings.PlaySound("water_land_02");
            Go(Igi2PlayerState.TreadWater);
            return;
        }

        if (!Has(Igi2PlayerSignal.Grounded))
        {
            return;
        }

        PlayLandingSound();
        Speed = default;
        ImpactSpeed = default;

        if (Damage.IsFatal)
        {
            UpdateDie();
            Go(Igi2PlayerState.Die);
            return;
        }

        Go(Pose switch
        {
            Igi2HumanPose.Crouching => Igi2PlayerState.Crouch,
            Igi2HumanPose.LyingFlat => Igi2PlayerState.Flat,
            _ => Igi2PlayerState.Stand,
        });
    }

    /// <summary>Lands in water with a splash, otherwise with the ground material's own landing sound, if it has one.</summary>
    private void PlayLandingSound()
    {
        if (Surroundings.IsUnderWater(out float depth))
        {
            WaterDepth = depth;
            Surroundings.PlaySound("water_land_01");
        }
        else if (GroundIntersection.EnterMaterial is int material && Surroundings.LandingSound(material) is { } sound)
        {
            Surroundings.PlaySound(sound);
        }
    }

    /// <summary>One tick in the air: gravity, the held directions, and on the tick the ground comes back, the landing.</summary>
    /// <remarks>
    /// <c>HumanPlayer_InAirControl</c> (<c>igi2 0x49FCC0</c>). The arithmetic keeps the original's split
    /// between what it stores as floats and what it carries on the FPU stack: the vertical in particular is
    /// carried unrounded into the landing's impact speed.
    /// </remarks>
    private void ControlInAir()
    {
        Fpu vertical = (Fpu)Speed.Z - GravityPerTick;

        float control = General.InAirControlSpeed;
        float side = 0f;
        if (Channels.IsHeld(Igi2HumanChannel.MoveLeft))
        {
            side = -control;
        }

        if (Channels.IsHeld(Igi2HumanChannel.MoveRight))
        {
            side = ((Fpu)control + side).F;
        }

        Fpu forward = 0f;
        if (Channels.IsHeld(Igi2HumanChannel.MoveForwards))
        {
            forward = control;
        }

        if (Channels.IsHeld(Igi2HumanChannel.MoveBackwards))
        {
            forward -= control;
        }

        // The push is taken through the orientation with a zero local vertical, as the original spells it.
        Matrix3f o = Orientation;
        float pushX = ((forward * o.M12) + ((Fpu)side * o.M11) + ((Fpu)o.M13 * 0f)).F;
        float pushY = ((forward * o.M22) + ((Fpu)side * o.M21) + ((Fpu)o.M23 * 0f)).F;
        Fpu pushZ = (forward * o.M32) + ((Fpu)side * o.M31) + ((Fpu)o.M33 * 0f);

        float x = ((Fpu)pushX + Speed.X).F;
        float y = ((Fpu)pushY + Speed.Y).F;
        vertical += pushZ;
        Speed = new Vector3f(x, y, vertical.F);

        if (!Has(Igi2PlayerSignal.Grounded))
        {
            // The protection is latched for the whole fall from whatever it is while airborne.
            _fallDamageProtected = IsProtectedFromFallDamage;
            return;
        }

        Land(x, y, vertical);
    }

    /// <summary>The landing: a view kick by impact speed, damage over the safe speed, and a noise the AI hears.</summary>
    private void Land(float x, float y, Fpu vertical)
    {
        // A landing on a moving rigid object is measured against how far it moved this tick.
        Fpu groundX = 0f;
        Fpu groundY = 0f;
        Fpu groundZ = 0f;
        if (TryGetGroundObjectPosition(out Vector3d now))
        {
            groundX = (Fpu)now.X - GroundPreviousPosition.X;
            groundY = (Fpu)now.Y - GroundPreviousPosition.Y;
            groundZ = (Fpu)now.Z - GroundPreviousPosition.Z;
        }

        Fpu impact;
        if (_fallDamageProtected)
        {
            impact = Fpu.Abs(vertical);
        }
        else
        {
            float dx = ((Fpu)x - groundX).F;
            float dy = ((Fpu)y - groundY).F;
            Fpu dz = vertical - groundZ;
            impact = Fpu.Sqrt(((Fpu)dx * dx) + ((Fpu)dy * dy) + (dz * dz));
        }

        // Units per tick to metres per second.
        Fpu metresPerSecond = impact * (1f / Igi2HumanPlayerGeneral.UnitsPerMetre) * Igi2HumanPlayerGeneral.TicksPerSecond;

        Fpu kick = (Fpu)0.125f * metresPerSecond;
        if (kick.Value < 0.0)
        {
            kick = 0f;
        }
        else if (kick.Value > 1.0)
        {
            kick = 1f;
        }

        View.ImpactOffset = (kick * -1024.0).F;
        View.ImpactAngle = 0f;

        Fpu excess = metresPerSecond - SafeLandingSpeed;
        if (excess.Value > 0.0)
        {
            Damage.LastPart = Igi2DamagePart.Chest;
            float amount = (excess * LandingDamagePerMetrePerSecond * Damage.MaxDamage).F;
            if (!IsDead)
            {
                Damage.Add(Igi2DamagePart.Chest, amount);
            }

            if (amount != 0f)
            {
                Surroundings.PlaySound(
                    ((Fpu)Damage.MaxDamage * 0.1f).Value > amount ? "player_fall_1"
                    : ((Fpu)Damage.MaxDamage * 0.2f).Value > amount ? "player_fall_2"
                    : "player_fall_3");
            }
        }

        Surroundings.SendLandingEvent();
    }
}
