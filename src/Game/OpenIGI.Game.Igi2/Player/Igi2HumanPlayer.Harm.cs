using OpenIGI.Core.Numerics;

namespace OpenIGI.Game.Igi2.Player;

/// <content>Flinching from a hit, and dying.</content>
public sealed partial class Igi2HumanPlayer
{
    /// <summary>The clip a crouched flinch plays.</summary>
    public const int CrouchHitAnimation = 34;

    /// <summary>The clip a flinch lying flat plays.</summary>
    public const int FlatHitAnimation = 81;

    /// <summary>The clip a death in the water plays.</summary>
    public const int SwimDeathAnimation = 22;

    /// <summary>The clip a death lying flat plays.</summary>
    public const int FlatDeathAnimation = 82;

    /// <summary>The clip a death in mid-air plays.</summary>
    public const int AirDeathAnimation = 106;

    private const float HitStep = 14.845724f;

    // One over 4800, the float HumanPlayer_InitStateHit scales a flinch's length by.
    private static readonly float HitTimeScale = BitConverter.Int32BitsToSingle(0x395A740E);

    /// <summary>
    /// A standing flinch's three clips by the zone hit, <c>DamagePartData_t.aeShortHitState</c> in the
    /// single-player table at <c>igi2 0x63D830</c>. The three are the same clip for every zone, but the
    /// draw that picks one is still made.
    /// </summary>
    private static readonly int[][] ShortHitAnimations =
    [
        [58, 58, 58], [58, 58, 58], [58, 58, 58],
        [61, 61, 61], [61, 61, 61], [61, 61, 61],
        [62, 62, 62], [62, 62, 62], [62, 62, 62],
        [63, 63, 63], [63, 63, 63], [63, 63, 63],
        [64, 64, 64], [64, 64, 64], [64, 64, 64], [64, 64, 64], [64, 64, 64],
    ];

    // The death clips HumanPlayer_InitStateDie builds on its stack: eight standing, six kneeling.
    private static readonly int[] StandDeathAnimations = [92, 93, 94, 95, 96, 97, 98, 99];
    private static readonly int[] KneelDeathAnimations = [100, 101, 102, 103, 104, 105];

    /// <summary>Gets or sets the game's run random stream, <c>Game.tRunRandomState</c>, which flinches and deaths draw from.</summary>
    /// <remarks>Shared with everything else that draws from it; the session supplies the one stream.</remarks>
    public IgiRandom Random { get; set; } = new();

    /// <summary>Gets or sets the pain sound playing, <c>Human_s.ptHitSoundQTaskRef</c>; death cuts it off.</summary>
    public IIgi2SoundInstance? HitSound { get; set; }

    /// <summary>Gets or sets the body's whole speed last tick, <c>Human_s.tTotalSpeed</c>; a death carries it on.</summary>
    public Vector3f TotalSpeed { get; set; }

    /// <summary>Gets a value indicating whether the death clip has played out, <c>Human_s.isDeathAnimationDone</c>.</summary>
    public bool IsDeathAnimationDone { get; private set; }

    /// <summary>Gets or sets a value indicating whether a dead body has come to rest, <c>Human_s.isDeathPositionStabilized</c>.</summary>
    public bool IsDeathPositionStabilized { get; set; }

    /// <summary>Gets a value indicating whether the upper body turns with the aim, <c>Human_s.isRotateUpperBody</c>.</summary>
    public bool IsRotateUpperBody { get; private set; } = true;

    /// <summary>Gets a value indicating whether the action machine is locked, <c>HumanPlayer_t.isLockAction</c>.</summary>
    public bool IsLockAction { get; private set; }

    /// <summary>Gets a value indicating whether the body's orientation is frozen, <c>Human_s.isLockOrientation</c>.</summary>
    public bool IsLockOrientation { get; private set; }

    /// <summary>Gets the camera mode, <c>Human_s.eHumanCameraMode</c>; 5 is the death camera.</summary>
    public int CameraMode { get; private set; }

    // State 54 -- InitStateHit 0x4B2090, RunStateHit 0x4B21F0.
    private void EnterHit()
    {
        // The draw is made whatever the posture; only a standing flinch uses it.
        uint draw = Random.NextRaw();
        switch (Pose)
        {
            case Igi2HumanPose.Standing:
                Animation.PlayState(ShortHitAnimations[(int)Damage.LastPart][draw % 3], 0, StateBlendSeconds);
                break;
            case Igi2HumanPose.Crouching:
                Animation.PlayState(CrouchHitAnimation, 0, StateBlendSeconds);
                break;
            case Igi2HumanPose.LyingFlat:
                Animation.PlayState(FlatHitAnimation, 0, StateBlendSeconds);
                break;
        }

        // A flinch plays at its own pace: two-fifteenths of its length per tick.
        Animation.MainTimeStep = (int)(Animation.MainTotalTime * (double)HitTimeScale * AnimationTicksPerFrame * 4.0);
        CurrentUpperBody = 0;
        RequestedUpperBody = 0;
        Animation.ClearUpperBody(0.4f);
        AimPrecision(Igi2PrecisionType.Standing, static p => p.Standing);
        Locomotion.Current = (_, _) => RunHit();
    }

    private void RunHit()
    {
        MoveOnGround(0f, HitStep, 0f, HitStep);

        if (Has(Igi2PlayerSignal.Dead))
        {
            Go(Igi2PlayerState.Die);
        }
        else if (Has(Igi2PlayerSignal.AnimationEnded))
        {
            Go(StillInPose());
        }
    }

    /// <summary>The still state of the posture: standing, crouching or lying flat.</summary>
    private int StillInPose() => Pose switch
    {
        Igi2HumanPose.Crouching => Igi2PlayerState.Crouch,
        Igi2HumanPose.LyingFlat => Igi2PlayerState.Flat,
        _ => Igi2PlayerState.Stand,
    };

    // State 55 -- InitStateDie 0x4B2320, RunStateDie 0x4B2550, RunStateDie2 0x4B25E0.
    private void EnterDie()
    {
        Weapon.ClearOverlay();
        Animation.PlayState(DeathAnimation(), 0, StateBlendSeconds);

        Pose = Igi2HumanPose.Standing;
        CurrentUpperBody = 0;
        RequestedUpperBody = 0;
        Animation.ClearUpperBody(0.4f);
        AimPrecision(Igi2PrecisionType.Standing, static p => p.Standing);

        IsRotateUpperBody = false;
        IsLockAction = true;
        CameraMode = 5;
        View.IsLockOrientation = true;
        IsLockOrientation = true;
        Weapon.ReleaseTriggers();
        PlayDeathSound();

        Speed = TotalSpeed;
        Locomotion.Current = (_, _) => RunDie();
    }

    /// <summary>The death clip: by how the player died if in water or the air, otherwise drawn for the posture.</summary>
    private int DeathAnimation()
    {
        int previous = Locomotion.PreviousState;
        if (previous is Igi2PlayerState.SwimForward or Igi2PlayerState.TreadWater)
        {
            return SwimDeathAnimation;
        }

        if (previous is Igi2PlayerState.Fall or Igi2PlayerState.Jump)
        {
            return AirDeathAnimation;
        }

        return Pose switch
        {
            Igi2HumanPose.Crouching => KneelDeathAnimations[Random.NextBounded((uint)KneelDeathAnimations.Length)],
            Igi2HumanPose.LyingFlat => FlatDeathAnimation,
            _ => StandDeathAnimations[Random.NextBounded((uint)StandDeathAnimations.Length)],
        };
    }

    /// <summary>Cuts off the pain sound and plays one of three death cries (<c>Human_PlayDeathSound</c>, <c>igi2 0x503280</c>).</summary>
    private void PlayDeathSound()
    {
        HitSound?.Delete();
        HitSound = null;
        uint draw = Random.NextBounded(3);
        Surroundings.PlaySound($"player_death_{draw}");
    }

    private void RunDie()
    {
        NoteDeathAnimation();

        if (Locomotion.PreviousState is Igi2PlayerState.SwimForward or Igi2PlayerState.TreadWater)
        {
            // A body in water just plays out its clip.
            if (!Has(Igi2PlayerSignal.AnimationEnded))
            {
                return;
            }
        }
        else
        {
            // A body anywhere else falls until it lands or comes to rest.
            ControlInAir();
            if (!Has(Igi2PlayerSignal.Grounded) && !IsDeathPositionStabilized)
            {
                return;
            }

            Speed = default;
            ImpactSpeed = default;
        }

        Weapon.ClearAllWeapons();
        Locomotion.Current = (_, _) => NoteDeathAnimation();
    }

    private void NoteDeathAnimation()
    {
        if (Has(Igi2PlayerSignal.AnimationEnded))
        {
            IsDeathAnimationDone = true;
        }
    }
}
