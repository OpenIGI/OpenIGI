using OpenIGI.Core.Numerics;

namespace OpenIGI.Game.Igi2.Player;

/// <content>Jumping, falling, and the landing both end in.</content>
public sealed partial class Igi2HumanPlayer
{
    /// <summary>The speed gravity takes off the vertical each tick in the air.</summary>
    /// <remarks>The float <c>0x42A97BBF</c> in <c>HumanPlayer_InAirControl</c> (<c>igi2 0x49FCC0</c>).</remarks>
    public const float GravityPerTick = 84.741692f;

    /// <summary>The ground tolerance once a jump is coming down, and after it ends.</summary>
    public const float LandingGroundTolerance = 819.20001f;

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
        Speed = new Vector3f(carried.X, carried.Y, (float)(General.UpwardJumpSpeed + (double)carried.Z));
        Signal &= ~Igi2PlayerSignal.Grounded;
        Surroundings.LeaveGround();
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
            Go(Igi2PlayerState.Swim);
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
            AimPrecision(Igi2PrecisionType.CrouchJumping, static p => p.JumpingModifier + (double)p.CrouchWalking + p.Running);
        }
        else
        {
            AimPrecision(Igi2PrecisionType.Jumping, static p => p.JumpingModifier + (double)p.Running);
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
            Go(Igi2PlayerState.Swim);
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
        else if (Surroundings.GroundLandingSound is { } sound)
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
        double vertical = Speed.Z - (double)GravityPerTick;

        float control = General.InAirControlSpeed;
        float side = 0f;
        if (Channels.IsHeld(Igi2HumanChannel.MoveLeft))
        {
            side = -control;
        }

        if (Channels.IsHeld(Igi2HumanChannel.MoveRight))
        {
            side = (float)(control + (double)side);
        }

        double forward = 0.0;
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
        float pushX = (float)((forward * o.M12) + (side * (double)o.M11) + (o.M13 * 0.0));
        float pushY = (float)((forward * o.M22) + (side * (double)o.M21) + (o.M23 * 0.0));
        double pushZ = (forward * o.M32) + (side * (double)o.M31) + (o.M33 * 0.0);

        float x = (float)(pushX + (double)Speed.X);
        float y = (float)(pushY + (double)Speed.Y);
        vertical += pushZ;
        Speed = new Vector3f(x, y, (float)vertical);

        if (!Has(Igi2PlayerSignal.Grounded))
        {
            // The protection is latched for the whole fall from whatever it is while airborne.
            _fallDamageProtected = IsProtectedFromFallDamage;
            return;
        }

        Land(x, y, vertical);
    }

    /// <summary>The landing: a view kick by impact speed, damage over the safe speed, and a noise the AI hears.</summary>
    private void Land(float x, float y, double vertical)
    {
        // A landing on something moving is measured against how far it moved this tick.
        Vector3d ground = Surroundings.GroundMotion();

        double impact;
        if (_fallDamageProtected)
        {
            impact = Math.Abs(vertical);
        }
        else
        {
            float dx = (float)(x - ground.X);
            float dy = (float)(y - ground.Y);
            double dz = vertical - ground.Z;
            impact = Math.Sqrt((dx * (double)dx) + (dy * (double)dy) + (dz * dz));
        }

        // Units per tick to metres per second.
        double metresPerSecond = impact * (1.0 / Igi2HumanPlayerGeneral.UnitsPerMetre) * Igi2HumanPlayerGeneral.TicksPerSecond;

        double kick = 0.125 * metresPerSecond;
        kick = kick < 0.0 ? 0.0 : kick > 1.0 ? 1.0 : kick;
        View.ImpactOffset = (float)(kick * -1024.0);
        View.ImpactAngle = 0f;

        double excess = metresPerSecond - SafeLandingSpeed;
        if (excess > 0.0)
        {
            Damage.LastPart = Igi2DamagePart.Chest;
            float amount = (float)(excess * LandingDamagePerMetrePerSecond * Damage.MaxDamage);
            if (!IsDead)
            {
                Damage.Add(Igi2DamagePart.Chest, amount);
            }

            if (amount != 0f)
            {
                Surroundings.PlaySound(
                    Damage.MaxDamage * (double)0.1f > amount ? "player_fall_1"
                    : Damage.MaxDamage * (double)0.2f > amount ? "player_fall_2"
                    : "player_fall_3");
            }
        }

        Surroundings.SendLandingEvent();
    }

    /// <summary>Death, once the damage reaches the limit: the weapon's sound slot is stopped and the dead bit raised.</summary>
    /// <remarks><c>Human_UpdateDie</c> (<c>igi2 0x504010</c>), single-player path.</remarks>
    private void UpdateDie()
    {
        if (!Damage.IsFatal || IsDead)
        {
            return;
        }

        Weapon.StopSoundSlot();
        IsDead = true;
        Signal |= Igi2PlayerSignal.Dead;
    }
}
