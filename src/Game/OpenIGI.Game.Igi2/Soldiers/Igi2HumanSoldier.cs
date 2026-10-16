using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;
using OpenIGI.Game.Igi2.Animation;
using OpenIGI.Game.Igi2.Humans;
using OpenIGI.Game.Igi2.Player;
using OpenIGI.Game.Igi2.Weapons;

namespace OpenIGI.Game.Igi2.Soldiers;

/// <summary>What drives a soldier's body each tick: its <c>HumanAI</c>, which fills the channels and sends the update.</summary>
public interface IIgi2SoldierMind
{
    /// <summary>
    /// Gets a value indicating whether the mind has sent its first update (<c>HumanAI+0x1167</c>), after which a standing
    /// soldier blends into its clips.
    /// </summary>
    bool HasStarted { get; }

    /// <summary>Runs the mind one tick, as a child of the soldier's body; it sends the body its update (<see cref="Igi2HumanSoldier.Update(ReadOnlySpan{float})"/>).</summary>
    /// <param name="soldier">The body.</param>
    void Run(Igi2HumanSoldier soldier);

    /// <summary>Gets a value indicating whether hits do the soldier no harm, <c>HumanAI+0x1165</c>.</summary>
    bool IsInvulnerable => false;

    /// <summary>Gets a value indicating whether a hit to the head or groin takes no zone multiplier, <c>HumanAI+0x1166</c>.</summary>
    bool IsInstantDeathDisabled => false;

    /// <summary>
    /// Says a line at the mind's position, <c>HumanAI_PlaySoundAtPosition</c> (<c>igi2 0x464600</c>) not queued: nothing while a
    /// line it said is still timing out (<c>HumanAI+0x1154</c>), else the line, which times out in sixty ticks.
    /// </summary>
    /// <param name="soldier">The body.</param>
    /// <param name="sound">The sound's name.</param>
    void Say(Igi2HumanSoldier soldier, string sound)
    {
    }

    /// <summary>Gets where the mind's target is, <c>HumanAI_GetTargetPosition</c> (<c>igi2 0x460EF0</c>), or <see langword="null"/> for none.</summary>
    Vector3d? TargetPosition => null;

    /// <summary>Hands the mind an event meant for it alone, its <c>HumanAISendEvent</c> handler.</summary>
    /// <param name="soldier">The body.</param>
    /// <param name="aiEvent">The event.</param>
    void Perceive(Igi2HumanSoldier soldier, in Ai.Igi2AiEvent aiEvent)
    {
    }

    /// <summary>Gets how far the mind turns the head, <c>HumanAI+0x100</c>, in radians.</summary>
    float HeadTurn => 0f;

    /// <summary>
    /// Gets the grenade the mind carries, <c>HumanAI+0x74DC</c> while <c>+0x74D0</c> is set, or <see langword="null"/> for none: the
    /// one it throws, and the one a dying soldier may drop besides its own weapon.
    /// </summary>
    string? GrenadeWeaponId => null;

    /// <summary>Gets where the mind would throw its grenade, <c>HumanAI+0x74E0</c>, or <see langword="null"/> for nowhere.</summary>
    Vector3d? GrenadeTargetPosition => null;

    /// <summary>
    /// Aims the mind's grenade from the hand at its target, <c>HumanAI_GetGrenadeOrientationAndSpeedToTarget</c>
    /// (<c>igi2 0x4632A0</c>).
    /// </summary>
    /// <param name="soldier">The body.</param>
    /// <param name="hand">Where the grenade leaves from.</param>
    /// <param name="speed">Receives its speed, units a tick.</param>
    /// <param name="orientation">Receives its orientation, nine floats row by row.</param>
    /// <param name="flyTime">Receives the ticks it flies.</param>
    /// <returns><see langword="true"/> when a throw reaches the target.</returns>
    bool TryAimGrenade(Igi2HumanSoldier soldier, Vector3d hand, out Vector3f speed, out float[] orientation, out int flyTime)
    {
        speed = default;
        orientation = [];
        flyTime = 0;
        return false;
    }

    /// <summary>Gets where the mind's weapon fires from, <c>HumanAI+0x60</c>.</summary>
    Vector3d FirePosition => default;

    /// <summary>Gets the mind's own orientation, <c>HumanAI+0xA0</c>, nine floats row by row: a shot's when its aim has no side.</summary>
    IReadOnlyList<float> FireOrientation => [1f, 0f, 0f, 0f, 1f, 0f, 0f, 0f, 1f];

    /// <summary>Gets the point the mind aims its shots at, <c>HumanAI+0x7530</c>.</summary>
    Vector3d AimTarget => default;

    /// <summary>Gets the clip name a stand action starts from, the mind's <c>HumanAI+0x28</c>, or <see langword="null"/> for none.</summary>
    string? StandActionAnimation => null;

    /// <summary>
    /// Says a line at the mind's position, <c>HumanAI_PlaySoundAtPosition</c> (<c>igi2 0x464600</c>), queued when it may wait its
    /// turn.
    /// </summary>
    /// <param name="soldier">The body.</param>
    /// <param name="sound">The sound's name.</param>
    /// <param name="queueable">Whether the line may wait for one still timing out.</param>
    void Say(Igi2HumanSoldier soldier, string sound, bool queueable) => Say(soldier, sound);
}

/// <summary>An enemy soldier's body, as I.G.I. 2's <c>HumanSoldier_t</c> runs it.</summary>
/// <remarks>
/// <para>
/// The soldier's own part over the shared <see cref="Igi2Human"/> body: its states, its upper body, and the fields
/// <c>HumanSoldier_t</c> adds to <c>Human_s</c>. Its mind writes the same channel block the player's input does, read with
/// the soldier's own meanings (<see cref="Igi2SoldierChannel"/>).
/// </para>
/// <para>
/// A tick (<c>HumanSoldier_RunHandler</c>, <c>igi2 0x4EA190</c>) only queues the soldier for the delayed pass. There
/// (<see cref="RunDelayed"/>) the body's children run, its <c>HumanAI</c> among them, and the mind sends the update that
/// moves the body.
/// </para>
/// </remarks>
public sealed partial class Igi2HumanSoldier : Igi2Human
{
    /// <summary>The share of the light a crouching soldier is seen by, <c>0x3F4CCCCD</c>.</summary>
    public const float CrouchVisibility = 0.80000001f;

    /// <summary>The share of the light a lying soldier is seen by, <c>0x3F19999A</c>.</summary>
    public const float LieVisibility = 0.60000002f;

    private IIgi2PlayerWeaponControl _weapon;

    /// <summary>Initializes a new instance of the <see cref="Igi2HumanSoldier"/> class.</summary>
    /// <param name="weapon">The soldier's weapon; <see cref="Igi2NoWeaponControl.Instance"/> for none.</param>
    /// <param name="animation">The animation layer the states drive, over <see cref="Igi2SoldierStateAnimations.Table"/>.</param>
    /// <param name="surroundings">The world the states probe.</param>
    public Igi2HumanSoldier(IIgi2PlayerWeaponControl weapon, IIgi2HumanAnimation animation, IIgi2HumanSurroundings surroundings)
        : base(animation, surroundings, static human => ((Igi2HumanSoldier)human).CreateLocomotion())
    {
        ArgumentNullException.ThrowIfNull(weapon);
        _weapon = weapon;
        weapon.Bind(this);
    }

    /// <summary>Gets or sets the soldier's weapon; the soldier becomes its holder.</summary>
    public IIgi2PlayerWeaponControl Weapon
    {
        get => _weapon;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            _weapon = value;
            value.Bind(this);
        }
    }

    /// <summary>Gets or sets what drives the soldier, its <c>HumanAI</c>, or <see langword="null"/> for none.</summary>
    /// <remarks>With none, nothing sends the update and the body does not move, as a soldier whose mind never starts.</remarks>
    public IIgi2SoldierMind? Mind { get; set; }

    /// <inheritdoc/>
    /// <remarks>A HumanAI mind keeps its soldier on its graph; the stand-in mind does not.</remarks>
    protected override IIgi2GraphFooting? GraphFooting => Mind as IIgi2GraphFooting;

    /// <inheritdoc/>
    protected override IIgi2HumanWeapon HumanWeapon => Weapon;

    /// <summary>Gets or sets the locomotion signal word, <c>tState.bSignal</c>.</summary>
    public Igi2SoldierSignal Signal
    {
        get => (Igi2SoldierSignal)Locomotion.Flags;
        set => Locomotion.Flags = (uint)value;
    }

    /// <summary>Gets or sets the skeleton the level gave the soldier, <c>nBoneHierachy</c>.</summary>
    public int BoneHierarchy { get; set; } = 1;

    /// <summary>Gets or sets the stand animation the level gave the soldier, <c>nStandAnimation</c>.</summary>
    public int StandAnimation { get; set; } = -1;

    /// <summary>Gets or sets a value indicating whether the soldier walks rather than runs, <c>isWalk</c>.</summary>
    public bool IsWalk { get; set; } = true;

    /// <summary>Gets or sets a value indicating whether the stand animation loops, <c>isLoopStandAnimation</c>.</summary>
    public bool IsLoopStandAnimation { get; set; }

    /// <summary>Gets or sets the damage share past which a hit makes the soldier flinch, <c>vRecoilThreshold</c>.</summary>
    public float RecoilThreshold { get; set; } = 0.33000001f;

    /// <summary>Gets or sets the damage counted toward the next flinch, <c>vRecoilMeter</c>.</summary>
    public float RecoilMeter { get; set; }

    /// <summary>Gets or sets the scale on the soldier's walking and running speeds, <c>vMovementSpeedScale</c>; one each delayed tick.</summary>
    public float MovementSpeedScale { get; set; }

    /// <summary>Gets the task id the soldier was last told to activate, <c>nActivateTaskID</c>.</summary>
    public int ActivateTaskId { get; private set; }

    /// <summary>Gets or sets the soldier's life, <c>eLifeState</c>: nought while alive.</summary>
    public int LifeState { get; set; }

    /// <summary>Gets or sets the upper body's flags, <c>tUpperBodyState.bFlags</c>: bit 1 keeps the upper body off.</summary>
    public int UpperBodyFlags { get; set; }

    /// <summary>Gets the upper body's handler, <c>tUpperBodyState.pfSignalHandler</c>.</summary>
    public Igi2SoldierUpperBody UpperBodyState { get; private set; } = Igi2SoldierUpperBody.Idle;

    /// <summary>Gets how visible the soldier is, <c>vVisibilityFactor</c>: the light it stands in, less crouched or lying.</summary>
    public float VisibilityFactor { get; private set; }

    /// <summary>Gets or sets the damage the soldier died of, <c>vDeathDamage</c>.</summary>
    public float DeathDamage { get; set; }

    /// <inheritdoc/>
    /// <remarks>A soldier is never seen in first person.</remarks>
    public override int WeaponObjectCount() => 0;

    /// <summary>
    /// Readies the soldier as <c>HumanSoldier_CreateHandler</c> (<c>igi2 0x4E9E10</c>) does after the shared human's create.
    /// </summary>
    /// <remarks>
    /// The state starts in <see cref="Igi2SoldierState.Stand"/>, its entry run on the first tick; nothing in the signal. Walls do
    /// not stop a soldier (its mind keeps it to its graph), its ground is probed from 4096 above, it walks, flinches past a third
    /// of its life, and its upper body starts idle.
    /// </remarks>
    public void Create()
    {
        Locomotion.TransitionTo(Igi2SoldierState.Stand);
        Signal = Igi2SoldierSignal.None;
        CollisionZOffset = 0f;
        GroundCheckZOffset = 4096f;
        IsObjectCollision = false;
        BoneHierarchy = 1;
        StandAnimation = -1;
        IsLoopStandAnimation = false;
        IsWalk = true;
        RecoilMeter = 0f;
        MovementSpeedScale = 0f;
        UpperBodyFlags = 0;
        LifeState = 0;
        DeathDamage = 0f;
        RecoilThreshold = 0.33000001f;
        UpperBodyState = Igi2SoldierUpperBody.Idle;
        // humansoldier.c HumanSoldier_CreateHandler: soldiers reload without consuming reserve rounds.
        if (Weapon.Firing is { } firing)
        {
            firing.IsUnlimitedAmmo = true;
        }
    }

    /// <summary>Takes the tick's channels and runs the soldier: <c>HumanSoldier_HumanUpdateHandler</c> (<c>igi2 0x4EB250</c>).</summary>
    /// <param name="frame">The channel frame the mind wrote, <see cref="Igi2HumanChannels.Count"/> values.</param>
    /// <remarks>A locked channel block keeps last tick's frame (<see cref="Igi2HumanChannels.Accept"/>).</remarks>
    public void Update(ReadOnlySpan<float> frame)
    {
        Channels.Accept(frame);
        Update();
    }

    /// <summary>One tick of the soldier's body: <c>HumanSoldier_Update</c> (<c>igi2 0x4E3F50</c>).</summary>
    /// <remarks>
    /// <para>
    /// The locomotion states run, the walk toggle flips on its channel, the upper body runs and the hit bit drops. Then as for any
    /// human: the body turns, the view aims, the main clip steps, the body moves, the eyes are placed, and the weapon lets go
    /// what the tick let go of.
    /// </para>
    /// <para>A soldier riding something (a mounted gun) is carried with it instead of moving (<c>Human_Attach</c>).</para>
    /// <para>Not ported yet, and skipped where it falls: the AI's notice (<c>Human_Notify</c>).</para>
    /// </remarks>
    public void Update()
    {
        Locomotion.Tick(this);
        if (Channels[Igi2SoldierChannel.ToggleWalk] != 0f)
        {
            IsWalk = !IsWalk;
        }

        RunUpperBody();
        Signal &= ~Igi2SoldierSignal.Hit;
        UpdateBody();
        View.UpdateView(Channels);
        Animation.StepMain();
        TouchPose();
        if (AttachParent is not null)
        {
            FollowAttachment();
        }
        else
        {
            Move();
        }

        UpdateViewBody();
        Weapon.UpdateStatus();
    }

    /// <summary>
    /// The soldier's delayed half: <c>HumanSoldier_RunDelayedHandler</c> (<c>igi2 0x4EA1B0</c>), then the shared human's run
    /// handler with the body's children, its mind last.
    /// </summary>
    /// <remarks>
    /// The main clip steps a frame a tick again, the soldier is as visible as the light it stands in (less crouched or lying),
    /// and its speed scale is one. The sector the soldier's nodes place it in (<c>HumanSoldier_UpdateSector</c>, <c>0x4EA030</c>)
    /// waits on the graph nodes' sectors.
    /// </remarks>
    public void RunDelayed()
    {
        MainTimeStepReset();
        VisibilityFactor = Pose switch
        {
            Igi2HumanPose.Crouching => ((Fpu)Lightmap * CrouchVisibility).F,
            Igi2HumanPose.LyingFlat => ((Fpu)Lightmap * LieVisibility).F,
            _ => Lightmap,
        };
        MovementSpeedScale = 1f;
        RunHuman();
        Animation.AutoStep();
        TouchPose();
        Mind?.Run(this);
        Weapon.RunDelayed();
        // weapon.c Weapon_RunDelayedHandler runs the GunShot child after firing; gunshot.c GunShot_RunHandler.
        Weapon.Firing?.RunGunShot();
        TouchPose();
    }

    /// <summary>The main controller's step back to a frame a tick.</summary>
    private void MainTimeStepReset() => Animation.MainTimeStep = Igi2AnimController.TicksPerFrame;

    /// <summary>Packs this tick's signal word from the channels: <c>HumanSoldier_StateSignalHandler</c> (<c>igi2 0x4E4010</c>).</summary>
    /// <remarks>
    /// The body's own bits are kept; the main and upper-body clips' ends and the walk toggle are read; then each channel raises
    /// its bit. An activation's channel holds the task to activate, taken once (not on a mounted gun); a weapon channel puts that
    /// weapon in hand.
    /// </remarks>
    private void PackSignal()
    {
        Igi2HumanChannels c = Channels;
        Igi2SoldierSignal s = Signal & Igi2SoldierSignal.KeptMask;
        Raise(ref s, c[Igi2SoldierChannel.RollLeft], Igi2SoldierSignal.RollLeft);
        Raise(ref s, c[Igi2SoldierChannel.RollRight], Igi2SoldierSignal.RollRight);
        if (Animation.MainEnded)
        {
            s |= Igi2SoldierSignal.MainEnded;
        }

        if (Animation.UpperBodyEnded)
        {
            s |= Igi2SoldierSignal.UpperBodyEnded;
        }

        s = IsWalk ? s | Igi2SoldierSignal.Walk : s & ~Igi2SoldierSignal.Walk;
        Raise(ref s, c[Igi2SoldierChannel.StrafeLeft], Igi2SoldierSignal.StrafeLeft);
        Raise(ref s, c[Igi2SoldierChannel.StrafeRight], Igi2SoldierSignal.StrafeRight);
        Raise(ref s, c[Igi2SoldierChannel.Forward], Igi2SoldierSignal.Forward);
        Raise(ref s, c[Igi2SoldierChannel.Backward], Igi2SoldierSignal.Backward);
        Raise(ref s, c[Igi2SoldierChannel.Fire], Igi2SoldierSignal.Fire);
        Raise(ref s, c[Igi2SoldierChannel.Channel7], Igi2SoldierSignal.Channel7);
        Raise(ref s, c[Igi2SoldierChannel.Reload], Igi2SoldierSignal.Reload);
        Raise(ref s, c[Igi2SoldierChannel.Channel9], Igi2SoldierSignal.Channel9);
        Raise(ref s, c[Igi2SoldierChannel.Turn], Igi2SoldierSignal.Turn);
        Raise(ref s, c[Igi2SoldierChannel.Kneel], Igi2SoldierSignal.Kneel);
        Raise(ref s, c[Igi2SoldierChannel.LieDown], Igi2SoldierSignal.LieDown);
        Raise(ref s, c[Igi2SoldierChannel.StandAction], Igi2SoldierSignal.StandAction);
        if (c[Igi2SoldierChannel.Activate] != 0f)
        {
            s |= Igi2SoldierSignal.Activate;
            if (Locomotion.CurrentState != Igi2SoldierState.StationaryGun)
            {
                ActivateTaskId = Fpu.ToInt32(c[Igi2SoldierChannel.Activate]);
                HandleActivation();
                c[Igi2SoldierChannel.Activate] = 0f;
            }
        }

        Raise(ref s, c[Igi2SoldierChannel.Tense], Igi2SoldierSignal.Tense);
        Raise(ref s, c[Igi2SoldierChannel.Aim], Igi2SoldierSignal.Aim);
        if (c[Igi2SoldierChannel.SelectWeapon] != 0f)
        {
            SelectWeapon(Fpu.ToInt32(c[Igi2SoldierChannel.SelectWeapon]));
        }

        Raise(ref s, c[Igi2SoldierChannel.ThrowGrenade], Igi2SoldierSignal.ThrowGrenade);
        Raise(ref s, c[Igi2SoldierChannel.CloseCombat], Igi2SoldierSignal.CloseCombat);
        Raise(ref s, c[Igi2SoldierChannel.PressSwitch], Igi2SoldierSignal.PressSwitch);
        Signal = s;

        static void Raise(ref Igi2SoldierSignal signal, float channel, Igi2SoldierSignal bit)
        {
            if (channel != 0f)
            {
                signal |= bit;
            }
        }
    }

    /// <summary>Puts a weapon in hand by its <c>WEAPON_ID_*</c> number (<c>WeaponType_GetWeaponTypeFromID</c>, then <c>Weapon_Activate</c>).</summary>
    private void SelectWeapon(int number)
    {
        if (Weapon.Inventory is { } inventory && inventory.Tables.Weapon(number) is { } type && inventory.FindWeapon(type.WeaponId) is { } item)
        {
            Weapon.Activate(item);
        }
    }

    /// <summary>The weapon in hand's type, or <see langword="null"/>.</summary>
    private Igi2WeaponTypeTask? HeldType => Weapon.Inventory?.Current?.WeaponType;
}
