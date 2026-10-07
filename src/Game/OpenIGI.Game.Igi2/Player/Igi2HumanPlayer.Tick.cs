using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi2.Collision;
using OpenIGI.Game.Igi2.Humans;

namespace OpenIGI.Game.Igi2.Player;

/// <content>The player's tick: the order the original runs everything in.</content>
public sealed partial class Igi2HumanPlayer
{
    /// <summary>The ticks a landing or a hit holds off the next jump, <c>nJumpWaitTimer</c>'s reload.</summary>
    public const int JumpWait = 15;

    /// <summary>The ground steeper than fifty degrees (a normal's Z under <c>cos 50°</c>) that cannot be walked up or down.</summary>
    public const float SteepGround = 0.64279002f;

    /// <summary>The water that is too deep to lie down in.</summary>
    public const float LieDepthLimit = 819.20001f;

    /// <summary>The water that is too deep to crouch or lie in.</summary>
    public const float CrouchDepthLimit = 3276.8f;

    /// <summary>The water the player swims in.</summary>
    public const float SwimDepth = 5734.3999f;

    /// <summary>Gets or sets the ticks before the player may jump again, <c>nJumpWaitTimer</c>.</summary>
    public int JumpWaitTimer { get; set; }

    /// <summary>Gets or sets a value indicating whether activating is locked, <c>isPlayerActivateLocked</c>: the activate key does nothing.</summary>
    public bool IsActivateLocked { get; set; }

    /// <summary>Gets or sets a value indicating whether the player swims, <c>isSwimming</c>; the swim states set it.</summary>
    public bool IsSwimming { get; set; }

    /// <summary>Gets a value indicating whether the held weapon is drawn, <c>isWeaponDisplay</c>: not while swimming.</summary>
    public bool IsWeaponDisplay { get; private set; } = true;

    /// <summary>Gets or sets a value indicating whether the animation's step is taken unscaled, <c>isDisableDeltaTranslationScale</c>.</summary>
    /// <remarks>Ladders set it: their steps are already the right length.</remarks>
    public bool IsDisableDeltaTranslationScale { get; set; }

    /// <summary>Takes the tick's channels and runs the player: <c>HumanPlayer_HumanUpdateHandler</c> (<c>igi2 0x4B9A80</c>).</summary>
    /// <param name="frame">The channel frame the input read, <see cref="Igi2HumanChannels.Count"/> values.</param>
    /// <remarks>A locked channel block keeps last tick's frame (<see cref="Igi2HumanChannels.Accept"/>).</remarks>
    public void Update(ReadOnlySpan<float> frame)
    {
        Channels.Accept(frame);
        Update();
    }

    /// <summary>One tick of the player: <c>HumanPlayer_Update</c> (<c>igi2 0x4A1EC0</c>).</summary>
    /// <remarks>
    /// <para>The order is the original's, because each step reads what the one before left.</para>
    /// <list type="number">
    /// <item>Off the ground, or hit, the next jump waits fifteen ticks; it counts down once landed.</item>
    /// <item>
    /// Death is checked. A weapon-category key and the weapon cycle keys ask for a weapon change (the computer, which
    /// keeps the cycle keys for itself, is not ported). A locked activation clears the activate key.
    /// </item>
    /// <item>
    /// Water is sounded: wading stirs it; past 819.2 the player cannot lie, past 3276.8 not crouch, and past
    /// 5734.4 the swimming bit is raised. The weapon is drawn unless swimming.
    /// </item>
    /// <item>
    /// Ground steeper than fifty degrees takes the forward and back keys away, a held missile launcher forbids lying,
    /// and a computer page open blocks fire.
    /// </item>
    /// <item>
    /// The locomotion states run, then the action states; the walk toggle flips on its key, and only standing can run.
    /// </item>
    /// <item>The hit bit drops; the body turns, the view aims and, lying, is kept out of the ground.</item>
    /// <item>
    /// The animation's step is scaled; on the ground the legs' own speed is added to it. The body moves, unless
    /// waiting for the network or flying; off the ground or dead the legs add nothing.
    /// </item>
    /// <item>
    /// Lying, the body is re-noted on its platform. The main clip steps, the eyes are placed, and the view's kicks die
    /// away.
    /// </item>
    /// </list>
    /// <para>
    /// Not ported yet, and skipped where they fall: activation (<c>Human_UpdateActivate</c>), climbing, the dead body
    /// rising in water (<c>Human_PopPosition</c>), riding (<c>Human_Attach</c>), the lying body's tilt to the ground (<c>Human_InterpolateZAxisToAveragedGroundNormal</c>), firing and the
    /// weapon's status, the AI's notice, pickups and the screen flashes. The degraded-copy slowdown is inert: its
    /// factor is one.
    /// </para>
    /// </remarks>
    public void Update()
    {
        if ((Signal & Igi2PlayerSignal.Grounded) == 0 || (Signal & Igi2PlayerSignal.Hit) != 0)
        {
            AllowedStance &= ~Igi2AllowedStance.Jump;
            JumpWaitTimer = JumpWait;
        }
        else if (JumpWaitTimer != 0)
        {
            AllowedStance &= ~Igi2AllowedStance.Jump;
            JumpWaitTimer--;
        }

        UpdateDie();
        SelectWeaponFromCategory();
        SelectWeapon(forceCycle: false);
        if (IsActivateLocked)
        {
            Channels[Igi2HumanChannel.Activate] = 0f;
        }

        SoundWater();
        IsWeaponDisplay = !IsSwimming;
        if (GroundCollider is not null && GroundIntersection.Normal.Z < SteepGround)
        {
            Channels[Igi2HumanChannel.MoveForwards] = 0f;
            Channels[Igi2HumanChannel.MoveBackwards] = 0f;
        }

        if (Weapon.ActiveIsMissileLauncher)
        {
            AllowedStance &= ~Igi2AllowedStance.Lie;
        }

        if (Channels.IsHeld(Igi2HumanChannel.ComputerMap) || Channels.IsHeld(Igi2HumanChannel.ComputerLog)
            || Channels.IsHeld(Igi2HumanChannel.ComputerObjectives) || Channels.IsHeld(Igi2HumanChannel.ComputerIgiHq))
        {
            Channels[Igi2HumanChannel.Fire] = 0f;
        }

        Locomotion.Tick(this);
        Action.Tick(this);
        if (Channels.WasPressed(Igi2HumanChannel.ToggleWalkRun))
        {
            IsWalk = !IsWalk;
        }

        if (Pose >= Igi2HumanPose.Crouching)
        {
            IsRunning = false;
        }

        Signal &= ~Igi2PlayerSignal.Hit;
        UpdateBody();
        View.UpdateView(Channels);
        CheckViewCollision();
        if (!IsDisableDeltaTranslationScale)
        {
            float scale = General.DeltaTranslationScale;
            DeltaTranslation = new Vector3f(
                ((Fpu)scale * DeltaTranslation.X).F,
                ((Fpu)scale * DeltaTranslation.Y).F,
                ((Fpu)scale * DeltaTranslation.Z).F);
        }

        MoveBody();
        if (Pose == Igi2HumanPose.LyingFlat && GroundCollider?.Owner is not Igi2CollisionOwner { TypeName: TrainType })
        {
            UpdatePlatformPosition(forcePosition: false, forceOrientation: true);
        }

        Animation.StepMain();
        UpdateViewBody();
        View.DampenOffsets();
    }

    /// <summary>How deep the water is, and what it allows.</summary>
    private void SoundWater()
    {
        if (Surroundings.IsUnderWater(out float depth))
        {
            WaterDepth = depth;
            int state = Locomotion.CurrentState;
            if (state is Igi2PlayerState.TreadWater or Igi2PlayerState.StandTurn)
            {
                Surroundings.StirWater(isRunning: false);
            }
            else if (state != Igi2PlayerState.Stand)
            {
                Surroundings.StirWater(IsRunning);
            }
        }
        else
        {
            WaterDepth = 0f;
        }

        if (WaterDepth >= LieDepthLimit)
        {
            AllowedStance &= ~Igi2AllowedStance.Lie;
        }

        if (WaterDepth >= CrouchDepthLimit)
        {
            AllowedStance &= ~(Igi2AllowedStance.Crouch | Igi2AllowedStance.Lie);
        }

        Signal = WaterDepth < SwimDepth ? Signal & ~Igi2PlayerSignal.Swimming : Signal | Igi2PlayerSignal.Swimming;
    }

    /// <summary>The move: the legs' speed added to the animation's step on the ground, and <see cref="Igi2Human.Move"/>.</summary>
    private void MoveBody()
    {
        int state = Locomotion.CurrentState;
        if (state is Igi2PlayerState.NetworkWait or Igi2PlayerState.Fly)
        {
            return;
        }

        if ((Signal & Igi2PlayerSignal.Grounded) == 0 || IsDead)
        {
            GroundMovementSpeed = default;
        }
        else
        {
            Vector3f legs = GroundMovementSpeed;
            DeltaTranslation = new Vector3f(
                ((Fpu)legs.X + DeltaTranslation.X).F,
                ((Fpu)legs.Y + DeltaTranslation.Y).F,
                ((Fpu)legs.Z + DeltaTranslation.Z).F);
        }

        Move();
    }
}
