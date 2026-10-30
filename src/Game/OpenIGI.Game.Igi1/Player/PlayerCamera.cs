using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Formats.Animation;
using OpenIGI.Game.Ai;
using OpenIGI.Game.Config;
using OpenIGI.Game.Igi1.Weapons;
using OpenIGI.Game.Igi1.World;
using OpenIGI.Game.Player;
using OpenIGI.Game.Weapons;
using OpenIGI.Game.Weapons.Ballistics;
using OpenIGI.Game.World;
using OpenIGI.Input;

namespace OpenIGI.Game.Igi1.Player;

/// <summary>
/// Drives a <see cref="PlayerBody"/> from the keyboard and mouse, and gives the renderer its eye.
/// </summary>
/// <remarks>
/// <para>
/// The counterpart to <c>FreeCamera</c>: where that flies, this one walks. It advances the body
/// on the <b>fixed thirty-hertz tick</b> rather than on real elapsed time, because the body's motion
/// is the game's own and has to stay deterministic — only the look is taken per frame, so the view
/// stays smooth at any frame rate.
/// </para>
/// <para>
/// <b>Movement is root motion, from the animation.</b> There is no speed constant anywhere: the input
/// picks a locomotion state, the state names an animation, and the animation's root-motion track says
/// how far the body moved. Changing the animation changes the speed, which is how the original works.
/// </para>
/// <para>
/// The work is shared out: <see cref="PlayerFirstPersonView"/> is the look and the eye, <see cref="PlayerShotResolver"/>
/// settles what the weapons fire, and <see cref="PlayerInteractionDriver"/> and <see cref="PlayerLadderController"/> run
/// the states that own locomotion. This class keeps <c>HumanPlayer_Update</c>'s order and the surface the host drives.
/// </para>
/// </remarks>
public sealed partial class PlayerCamera : IHostedPlayer, IPlayerInteractionHost, IWeaponWheelPlayer
{
    /// <summary>
    /// The delta translation a walk covers in one tick, in the body's frame.
    /// </summary>
    /// <remarks>
    /// <b>Divergence, temporary.</b> The animation carries this in retail. Chosen here so the two
    /// speeds are a plausible walk and run — about 2.2 and 5.5 metres a second once the
    /// <see cref="HumanMotion.DefaultDeltaTranslationScale"/> of 1.75 is applied.
    /// </remarks>
    public const float WalkDeltaPerTick = 170f;

    /// <summary>The delta translation a run covers in one tick.</summary>
    /// <remarks>See <see cref="WalkDeltaPerTick"/>; the same divergence.</remarks>
    public const float RunDeltaPerTick = 430f;

    /// <summary>World units in a metre.</summary>
    /// <remarks>
    /// The body works in the game's own integral world units, because that is what the collision
    /// octree indexes and what every constant read out of the executable is expressed in. The renderer
    /// works in metres. This is where the two meet, and keeping the conversion in one place is what
    /// stops a unit mistake reaching the physics.
    /// </remarks>
    public const double UnitsPerMetre = 4096.0;

    private readonly PlayerBody _body = new();
    private readonly PlayerInputState _input = new();
    private readonly IgiRandom _random = new();
    private readonly PlayerOptics _optics = new();
    private readonly PlayerFirstPersonView _firstPerson;
    private readonly PlayerPickups _pickups;
    private readonly PlayerShotResolver _shots;
    private readonly PlayerInteractionDriver _interaction;
    private readonly PlayerLadderController _ladder;
    private readonly PlayerFootsteps _footsteps;
    private readonly PlayerWallBlockLog _wallBlocks;
    private readonly PlayerWeaponActions _weaponActions;
    private readonly PlayerLocomotion _locomotion;
    private ObjectCollisionIndex? _walls;
    private PlayerWeapons? _weapons;
    private WeaponType? _drawnWeapon;
    private WorldInteractionRegistry? _interactions;
    private bool _mapComputerOpen;

    /// <summary>Initializes the player camera.</summary>
    /// <param name="logger">Where optional collision diagnostics are written.</param>
    public PlayerCamera(IOpenIgiLogger? logger = null)
    {
        _wallBlocks = new PlayerWallBlockLog(logger ?? NullLogger.Instance, _body);
        _firstPerson = new PlayerFirstPersonView(_body);
        _pickups = new PlayerPickups(_body);
        _shots = new PlayerShotResolver(_body, _random);
        _interaction = new PlayerInteractionDriver(this, _body, _input, _random, _shots);
        _ladder = new PlayerLadderController(_body);
        _footsteps = new PlayerFootsteps(_body);
        _weaponActions = new PlayerWeaponActions(_random);
        _locomotion = new PlayerLocomotion(_body);
    }

    /// <summary>Gets whether the expensive wall-contact diagnostic is enabled.</summary>
    internal bool WallBlockDiagnosticsEnabled => _wallBlocks.IsEnabled;

    /// <summary>Gets the body being driven.</summary>
    public PlayerBody Body => _body;

    /// <summary>Gets the input frame the last tick ran on: on a co-op client, the command sent up for it.</summary>
    public PlayerInputFrame LastFrame => _input.Current;

    /// <summary>Uses the owning runtime's live player-health accumulator.</summary>
    public void UseHealth(HumanHealth health) => _body.UseHealth(health);

    /// <summary>Gets the first-person rig the held weapon hangs off, once one has been given.</summary>
    public HumanView? View => _firstPerson.Rig;

    /// <summary>Gets the weapon machine, once the player has been given a weapon cycle.</summary>
    public PlayerWeapons? Weapons => _weapons;

    /// <inheritdoc cref="PlayerShotResolver.Impacts"/>
    public IReadOnlyList<GunImpact> Impacts => _shots.Impacts;

    /// <summary>Gets or sets a callback that determines whether the player is allowed to jump.</summary>
    /// <remarks>
    /// When this returns <see langword="false"/>, the jump input is ignored. Used to suppress jumping
    /// while the player is inside an elevator cab — retail does not allow jumping while riding a lift.
    /// </remarks>
    public Func<bool>? CanJump { get; set; }

    /// <summary>Gets the marks rounds have left on the world.</summary>
    public BulletMarks Marks => _shots.Marks;

    /// <inheritdoc cref="PlayerShotResolver.Ballistics"/>
    public BallisticSimulation Ballistics => _shots.Ballistics;

    /// <summary>Gets whether the Dragunov's scope is open, for the HUD's overlay.</summary>
    public bool IsSniperScoped => _optics.IsSniperScoped;

    /// <summary>Gets whether the binocular overlay is open.</summary>
    public bool IsBinocularsOpen => _optics.Binoculars.IsOpen;

    /// <summary>Gets whether binoculars opened on the last simulation tick.</summary>
    public bool BinocularsOpenedThisTick => _optics.Binoculars.OpenedThisTick;

    /// <summary>Gets whether binoculars closed on the last simulation tick.</summary>
    public bool BinocularsClosedThisTick => _optics.Binoculars.ClosedThisTick;

    /// <summary>Gets whether a held zoom key moved the binocular magnification on the last tick.</summary>
    public bool BinocularZoomSteppingThisTick => _optics.Binoculars.ZoomSteppingThisTick;

    /// <summary>Gets how far the binoculars are zoomed in, 0 at the default to 1 at the tightest zoom.</summary>
    /// <param name="currentTanX">The optics' current horizontal half-angle tangent.</param>
    public float BinocularZoomInFraction(float currentTanX) => _optics.Binoculars.ZoomInFraction(currentTanX);

    /// <summary>Closes all optical devices and restores both first-person FOV tangents.</summary>
    /// <remarks>
    /// The desktop host stops ticking the player as soon as the death camera takes over. Resetting
    /// here prevents a binocular or sniper latch and its narrowed tangent from surviving outside
    /// first-person control.
    /// </remarks>
    public void ResetOptics()
    {
        _optics.Reset();
        _firstPerson.ResetZoom();
    }

    /// <inheritdoc cref="PlayerShotResolver.Grenades"/>
    public GrenadeSimulation Grenades => _shots.Grenades;

    /// <summary>Gets the launcher missiles currently in flight.</summary>
    public MissileSimulation Missiles => _shots.Missiles;

    /// <summary>Gets the M203 rounds currently in flight.</summary>
    public M203Simulation M203 => _shots.M203;

    /// <inheritdoc cref="PlayerShotResolver.GrenadeWalls"/>
    public WorldRayCast? GrenadeWalls { get => _shots.GrenadeWalls; set => _shots.GrenadeWalls = value; }

    /// <inheritdoc cref="PlayerShotResolver.GrenadeTrace"/>
    public WorldTraceCast? GrenadeTrace { get => _shots.GrenadeTrace; set => _shots.GrenadeTrace = value; }

    /// <inheritdoc cref="PlayerShotResolver.MineSurfaceRefused"/>
    public Func<WorldTraceHit, bool>? MineSurfaceRefused { get => _shots.MineSurfaceRefused; set => _shots.MineSurfaceRefused = value; }

    /// <inheritdoc cref="PlayerShotResolver.MineModel"/>
    public Func<string, (float Bound, Vector3f BlastPoint)>? MineModel { get => _shots.MineModel; set => _shots.MineModel = value; }

    /// <summary>Gets or sets the world cast used by launcher missiles.</summary>
    public WorldRayCast? MissileWalls { get => _shots.MissileWalls; set => _shots.MissileWalls = value; }

    /// <summary>Gets or sets the rich world trace used to retain a launcher's exact impact object.</summary>
    public WorldTraceCast? MissileTrace { get => _shots.MissileTrace; set => _shots.MissileTrace = value; }

    /// <inheritdoc cref="PlayerShotResolver.TerrainTrace"/>
    public WorldTraceCast? TerrainTrace
    {
        get => _shots.TerrainTrace;
        set
        {
            _shots.TerrainTrace = value;
            _body.TerrainWalls = value is null ? null : WorldTrace.AsRayCast(value);
        }
    }

    /// <inheritdoc cref="PlayerShotResolver.Humans"/>
    public HumanShot? Humans { get => _shots.Humans; set => _shots.Humans = value; }

    /// <summary>Gets or sets a non-mutating human intersection used by launcher missiles.</summary>
    public HumanRayCast? HumanProbes { get => _shots.HumanProbes; set => _shots.HumanProbes = value; }

    /// <summary>Gets or sets the living-human query used by armed proximity mines.</summary>
    public HumanProximityCast? HumanProximityTargets
    {
        get => _shots.HumanProximityTargets;
        set => _shots.HumanProximityTargets = value;
    }

    /// <inheritdoc cref="PlayerShotResolver.GroundImpact"/>
    public Action<Vector3d>? GroundImpact { get => _shots.GroundImpact; set => _shots.GroundImpact = value; }

    /// <summary>Gets or sets where the player's door, fence and ladder noises are offered to the AI.</summary>
    /// <remarks>
    /// Invoked with <see cref="AiEvent.Door"/> whenever the player toggles a door, by hand or at the end of
    /// a pick (<c>0x40C270</c>); with <see cref="AiEvent.Fence"/> on every tick of a fence climb but the
    /// first and the last (<c>0x411770</c>); and with <see cref="AiEvent.Ladder"/> at each ladder step.
    /// </remarks>
    public Action<AiEvent>? AiNoise { get; set; }

    /// <summary>Gets how many of this player's rounds have struck a human.</summary>
    public int HumanHits => _shots.HumanHits;

    /// <summary>
    /// Gets what this player's rounds have done to the soldiers of either game, hit by hit and kill by kill, for the display's
    /// marks. <b>Not retail.</b> Carried with each round the player fires.
    /// </summary>
    public ShotTally Shots { get; } = new();

    /// <summary>Gets the zone the last round to strike a human landed in, or <c>-1</c>.</summary>
    public int LastHumanZone => _shots.LastHumanZone;

    /// <summary>Gives the player the game material table, so its rounds can mark what they hit.</summary>
    /// <param name="materials">The table read from <c>material/material.qvm</c>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="materials"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// Without it nothing is marked at all — which is the honest failure, because the material is what
    /// decides both the texture and the size and neither has a sensible default.
    /// </remarks>
    public void UseMaterials(GameMaterialRegistry materials)
    {
        ArgumentNullException.ThrowIfNull(materials);
        _shots.Materials = materials;
        _footsteps.Materials = materials;
    }

    /// <inheritdoc cref="PlayerFootsteps.TerrainGameMaterialIndex"/>
    public int? TerrainGameMaterialIndex
    {
        get => _footsteps.TerrainGameMaterialIndex;
        set => _footsteps.TerrainGameMaterialIndex = value;
    }

    /// <summary>
    /// Gets the pitch the view is actually drawn at — the look, plus the recoil sitting on top of it.
    /// </summary>
    /// <remarks>
    /// <c>0x464800</c> writes <c>view+288 = view+296 + view+292</c>: the accumulated recoil is
    /// <b>added</b> to the mouse-driven pitch rather than folded into it, which is what lets it decay
    /// back to where you were aiming instead of leaving the view permanently raised.
    /// </remarks>
    public float AimPitch => _body.Pitch + (_weapons?.RecoilPitch ?? 0f);

    /// <summary>Gets whether the weapon in hand, or the gun the player is on, fired on the last tick.</summary>
    public bool FiredThisTick => MountedStationaryGun is { } mounted
        ? mounted.FiredThisTick
        : _weapons?.FiredThisTick ?? false;

    /// <summary>Gets whether the M16A2 launched an M203 round on the last tick.</summary>
    /// <remarks>
    /// The alternate callback is a separate native weapon task. Keeping this bit visible to the host
    /// prevents the rifle's ordinary automatic sound from replacing the launch report.
    /// </remarks>
    public bool M203LaunchedThisTick => _weapons is not null && _weapons.M203Shots.Count > 0;

    /// <summary>Gets whether a grenade throw is being charged — the hand cooking it.</summary>
    public bool IsChargingThrow => _weapons?.IsChargingThrow ?? false;

    /// <summary>Gets the serial of the last round fired.</summary>
    public int ShotSequence => _weapons?.ShotSequence ?? 0;

    /// <summary>Gets the weapon being fired: the mounted gun on a stand, else the one in hand, else none.</summary>
    /// <remarks>The report, the noise the AI hears and the damage factor all belong to the gun that fired.</remarks>
    public WeaponType? HeldWeaponType => _interaction.MountedGun?.Weapon ?? _weapons?.InHand?.Weapon;

    /// <summary>Gets the stationary gun the player is on and firing, or <see langword="null"/>.</summary>
    public AiStationaryGun? MountedStationaryGun => _interaction.MountedStationaryGun;

    /// <summary>Gets whether the player is on a stationary gun, looking down its barrel.</summary>
    /// <remarks>
    /// State 33 (<c>0x40C970</c>) sets no third-person camera mode, unlike the other interaction states;
    /// the gun hands the view its own eye instead (<c>0x474420</c> → <c>0x4658D0</c>, mode 4).
    /// </remarks>
    public bool IsMountedStationaryGun => MountedStationaryGun is not null;

    /// <summary>Gets whether the weapon in hand has changed since the model was last loaded.</summary>
    /// <remarks>
    /// Cycling weapons swaps the model as well as the animations, and the renderer holds the model — so
    /// it needs telling. Cleared by <see cref="AcknowledgeWeaponChange"/>.
    /// </remarks>
    public bool WeaponChanged => !ReferenceEquals(_drawnWeapon, HeldWeaponType);

    /// <summary>Records that the renderer has caught up with <see cref="HeldWeaponType"/>.</summary>
    public void AcknowledgeWeaponChange() => _drawnWeapon = HeldWeaponType;

    /// <summary>Gives the player its weapon cycle and starts its weapon machine.</summary>
    /// <param name="weaponCycle">
    /// <c>DefineHumanPlayerWeaponCycle</c>'s ids, in order. It is what decides which weapon starts in
    /// hand: the cycler's index begins at <c>-1</c>, so the first update lands on entry 0 and steps on
    /// until it finds one the player carries.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="weaponCycle"/> is <see langword="null"/>.</exception>
    public void UseWeaponCycle(IReadOnlyList<int> weaponCycle)
    {
        ArgumentNullException.ThrowIfNull(weaponCycle);
        _weapons = new PlayerWeapons(weaponCycle, _random);
    }

    /// <summary>Puts a set of cheats in force on the body and the weapons.</summary>
    /// <param name="cheats">The cheats, or <see cref="PlayerCheatOptions.Retail"/> for none.</param>
    /// <remarks>
    /// <see cref="Tick"/> and <see cref="TickMapComputer"/> already apply <see cref="PlayerCheatOptions.Current"/>
    /// every tick, so a change from Extras or the console lands on the next one without a restart.
    /// </remarks>
    public void ApplyCheats(PlayerCheatOptions cheats)
    {
        _body.ApplyCheats(cheats);
        _weapons?.ApplyCheats(cheats);
    }

    /// <summary>Gives the player its first-person rig.</summary>
    /// <param name="set">The set for skeleton 5, read from <c>common/anims/005.iff</c>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="set"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// Every <c>Human</c> creates one of these in its constructor and initialises it with skeleton 5 and
    /// the model <c>005_01_1</c> (<c>0x45EB30</c>, <c>0x464660</c>). Without it a weapon has no bone to
    /// hang off and nothing is drawn — which is the honest failure, not a weapon pasted onto the screen.
    /// </remarks>
    public void UseViewRig(BoneAnimationSet set)
    {
        ArgumentNullException.ThrowIfNull(set);

        // Mission restart can rebuild the first-person rig on an existing camera. Clear every
        // optic's weapon identity and the input edge shadow before attaching the new bone object;
        // otherwise a binocular/scope latch from the previous level can make the first toggle or
        // zoom press disappear, and a held key can be interpreted as an old edge.
        ResetOptics();
        _input.Reset();
        _firstPerson.UseRig(set);
        PoseChanged = true;
        SyncView();
    }

    /// <inheritdoc cref="PlayerArsenal.Carry"/>
    public bool Carry(WeaponType weapon, AmmoType? ammo = null) => PlayerArsenal.Carry(_weapons, weapon, ammo);

    /// <inheritdoc cref="PlayerArsenal.EnsureAllWeapons"/>
    public int EnsureAllWeapons(IReadOnlyList<WeaponType> roster, AmmoTypeRegistry ammunition) =>
        PlayerArsenal.EnsureAllWeapons(_weapons, roster, ammunition);

    /// <inheritdoc cref="PlayerWeaponActions.Play"/>
    public bool PlayWeaponAction(WeaponType weapon, WeaponAction action, int fireVariant = 0)
    {
        if (!_weaponActions.Play(_firstPerson.Rig, weapon, action, fireVariant))
        {
            return false;
        }

        PoseChanged = true;
        return true;
    }

    /// <summary>Gets where the held weapon's model goes, in engine units, or <see langword="null"/>.</summary>
    public BoneAttachment? HeldWeapon => _firstPerson.HeldWeapon;

    /// <inheritdoc cref="PlayerFirstPersonView.HeldWeaponSecondary"/>
    public BoneAttachment? HeldWeaponSecondary => _firstPerson.HeldWeaponSecondary;

    /// <inheritdoc cref="PlayerFirstPersonView.HeldWeaponTertiary"/>
    public BoneAttachment? HeldWeaponTertiary => _firstPerson.HeldWeaponTertiary;

    /// <summary>Gets whether the rig's pose has advanced since it was last acknowledged.</summary>
    /// <remarks>
    /// Only an advancing clip moves the bones — turning to look does not, because the object's own
    /// rotation is applied at draw rather than baked into the pose. So a renderer that has to rebuild
    /// something per pose can key off this rather than off the frame.
    /// </remarks>
    public bool PoseChanged { get; private set; } = true;

    /// <summary>Clears <see cref="PoseChanged"/>.</summary>
    public void AcknowledgePose() => PoseChanged = false;

    // Binds the rig to the body; see PlayerFirstPersonView.Sync.
    private void SyncView() => _firstPerson.Sync(AimPitch, _weapons);

    /// <summary>Gets whether the cursor should be held while the view is being turned.</summary>
    public bool IsCapturingMouse => _firstPerson.IsCapturingMouse;

    /// <inheritdoc cref="PlayerFirstPersonView.LookSettings"/>
    public MouseLookSettings LookSettings
    {
        get => _firstPerson.LookSettings;
        set => _firstPerson.LookSettings = value;
    }

    /// <summary>Gets or sets the active profile whose persisted controls drive gameplay.</summary>
    /// <remarks>The host refreshes this reference every frame, so switching or editing a profile is immediate.</remarks>
    public PlayerProfile? InputProfile { get; set; }

    /// <summary>Determines whether a configured action is held in the current input snapshot.</summary>
    public bool IsActionDown(InputState input, PlayerAction action) =>
        PlayerProfileInputBindings.IsDown(InputProfile, input, PlayerInputBindings.ConfigName(action));

    /// <summary>Determines whether a configured action was pressed in the current input snapshot.</summary>
    public bool WasActionPressed(InputState input, PlayerAction action) =>
        PlayerProfileInputBindings.WasPressed(InputProfile, input, PlayerInputBindings.ConfigName(action));

    /// <summary>Gets whether the human is held in retail locomotion state 45 for the map computer.</summary>
    public bool IsMapComputerOpen => _mapComputerOpen;

    /// <summary>
    /// Enters or leaves the map-computer state without letting the normal movement tick consume the
    /// opening key. Retail weapon state 11 immediately installs human locomotion state 45
    /// (<c>0x40FDA0</c>), whose declared weapon action is the crouched hold pose.
    /// </summary>
    public void SetMapComputerOpen(bool open)
    {
        if (_mapComputerOpen == open)
        {
            return;
        }

        if (open)
        {
            // The Ready state calls 0x4659E0 on the view just before it enters weapon state 11: the
            // scope latch clears and both tangents go home. Without this a sight, scope or binocular
            // zoom survives behind the map and comes back narrowed when play resumes.
            ResetOptics();
        }

        _mapComputerOpen = open;
        _weapons?.SetMapComputerOpen(open);

        if (!open)
        {
            // The open computer holds the window's input, so the frames this player was given while it was up were
            // empty -- and the key that closed it is most likely still down on the next one. Against an empty frame
            // that reads as a fresh press, and the ready state opens the computer again the moment it closes. Entered
            // as held, the key has to be let go before it can open the computer; one already let go is no worse off.
            PlayerInputFrame keyHeld = _input.Current;
            keyHeld[(int)PlayerInputSlot.MapComputer] = 1f;
            _input.Accept(keyHeld);
        }

        _locomotion.Animator?.Enter(open ? 45 : HumanLocomotionStates.Idle);

        if (open && _weapons?.InHand is { } held)
        {
            // State 45 requests Crouch in HumanLocomotionWeaponActions. Play it before the first
            // frozen computer tick so the first-person rig leaves the previous gun pose immediately.
            PlayWeaponAction(held.Weapon, WeaponAction.Crouch);
        }

        PoseChanged = true;
    }

    /// <inheritdoc cref="PlayerFirstPersonView.HoldStill"/>
    public void HoldStill() => _firstPerson.HoldStill();

    /// <inheritdoc cref="PlayerFirstPersonView.ReleaseLook"/>
    public void ReleaseLook() => _firstPerson.ReleaseLook();

    /// <summary>Gets the eye the scene is drawn from, in the renderer's metres.</summary>
    /// <remarks>On a stationary gun it is the gun's own sight, turned and kicked with the barrel.</remarks>
    public Camera3D Camera => _firstPerson.Camera(AimPitch, MountedStationaryGun);

    /// <inheritdoc cref="PlayerFirstPersonView.LandingKick"/>
    public HumanLandingViewKick LandingKick => _firstPerson.LandingKick;

    /// <summary>Gets the shake the last hit put in the view.</summary>
    public HumanViewShake ViewShake => _firstPerson.ViewShake;

    /// <inheritdoc cref="PlayerFirstPersonView.InstallViewShake"/>
    public void InstallViewShake(float positionUnits, float angleRadians, int ticks) =>
        _firstPerson.InstallViewShake(positionUnits, angleRadians, ticks);

    /// <inheritdoc cref="PlayerFirstPersonView.InstallLandingViewKick"/>
    public void InstallLandingViewKick(float amplitudeUnits) => _firstPerson.InstallLandingViewKick(amplitudeUnits);

    /// <summary>Gets where the player is standing, in metres, for diagnostics.</summary>
    public Vector3d Position => WorldScene.ToMetres(_body.Position);

    /// <summary>Gets where the player is standing, in the game's world units, for the debug overlay.</summary>
    public Vector3d PositionUnits => _body.Position;

    /// <summary>Gets the eye position, in world units.</summary>
    public Vector3d EyeUnits => _body.Eye;

    /// <summary>Gets which way the body faces, in radians.</summary>
    public float Yaw => _body.Yaw;

    /// <summary>Gets how far up or down the view looks, in radians.</summary>
    public float Pitch => _body.Pitch;

    /// <summary>Gets whether the player is standing on something.</summary>
    public bool IsGrounded => _body.IsGrounded;

    /// <inheritdoc cref="PlayerFootsteps.TakeFootstep"/>
    public Footstep? TakeFootstep() => _footsteps.TakeFootstep(_locomotion.Animator);

    /// <inheritdoc cref="PlayerFootsteps.TakeLandingSound"/>
    public (string Sound, Vector3d PositionInUnits)? TakeLandingSound() => _footsteps.TakeLandingSound();

    /// <summary>Gets whether the player is crouched.</summary>
    public bool IsCrouched => _body.IsCrouched;

    /// <summary>Gets whether the last tick's move was stopped by a wall.</summary>
    public bool TouchedWall => _body.TouchedWall;

    /// <inheritdoc cref="PlayerPlacement.Place"/>
    public bool Place(IGroundHeightField? terrain, Vector3d at, float yaw)
    {
        // A placement is a teleport; the rig's tick pair must not be interpolated across it, and a
        // landing dip still playing belongs to the ground that was left behind.
        _firstPerson.Pair.HasPair = false;
        _firstPerson.ResetKicks();
        return PlayerPlacement.Place(_body, terrain, at, yaw);
    }

    /// <inheritdoc cref="PlayerLocomotion.Use"/>
    public void UseAnimations(BoneAnimationSet set)
    {
        BodyAnimations = set;
        _locomotion.Use(set);
    }

    /// <summary>Gets the body's animation set, skeleton 0, once given; a co-op partner's ladder climbs on it.</summary>
    public BoneAnimationSet? BodyAnimations { get; private set; }

    /// <summary>Gets the ladder clip the climb is in, or -1 off a ladder: what another player's window draws it doing.</summary>
    public int LadderAnimation => !IsOnLadder
        ? -1
        : LadderPhase switch
        {
            LadderTraversalPhase.SlidingDown => LadderSlideAnimation,
            LadderTraversalPhase.GettingOnTop or LadderTraversalPhase.GettingOffTop => LadderTopAnimation,
            _ => LadderClimbAnimation,
        };

    /// <inheritdoc cref="PlayerShotResolver.MarkProbe"/>
    public WorldRayCast? MarkProbe => _shots.MarkProbe;

    /// <summary>Makes the level's placed objects solid.</summary>
    /// <param name="walls">The index built over the scene's placements.</param>
    /// <exception cref="ArgumentNullException"><paramref name="walls"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// Without this the body still stands on the terrain and falls off it, but walks through every
    /// crate and wall in the level — which is what it did before the sweep existed.
    /// </remarks>
    public void UseWalls(ObjectCollisionIndex walls)
    {
        ArgumentNullException.ThrowIfNull(walls);
        _walls = walls;
        _shots.Walls = walls;
        _footsteps.Walls = walls;
        _body.Walls = walls.PlayerCaster();

        // A mark is held in the frame of the object it struck, taken from the pose the round met.
        Marks.Poses = walls.TryGetPose;
    }

    /// <summary>Gives the player the tuning <c>humanplayer.qvm</c> publishes.</summary>
    /// <param name="parameters">The script's 43 values, converted into engine units.</param>
    /// <exception cref="ArgumentNullException"><paramref name="parameters"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// The jump height, the jump's horizontal carry, the air control and the root-motion scale all come
    /// from here. Without it the body falls back on the retail numbers, which is right for this game but
    /// wrong in principle — a mod that retunes the player would be ignored.
    /// </remarks>
    public void UseParameters(HumanPlayerParameters parameters)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        _body.Parameters = parameters;
    }

    /// <summary>Gets the locomotion state the player is in, for diagnostics.</summary>
    public int LocomotionState => _locomotion.State;

    /// <inheritdoc cref="PlayerLocomotion.Animation"/>
    public int LocomotionAnimation => _locomotion.Animation;

    /// <inheritdoc cref="PlayerLocomotion.AnimationLoops"/>
    public bool LocomotionAnimationLoops => _locomotion.AnimationLoops;

    /// <summary>Makes the level's ladders climbable.</summary>
    /// <param name="ladders">The ladders the scene collected, in world units.</param>
    /// <exception cref="ArgumentNullException"><paramref name="ladders"/> is <see langword="null"/>.</exception>
    public void UseLadders(IReadOnlyList<LadderPlacement> ladders)
    {
        ArgumentNullException.ThrowIfNull(ladders);
        _ladder.Use(ladders);
    }

    /// <summary>Gives the player the level's direct activatable tasks.</summary>
    public void UseInteractions(WorldInteractionRegistry interactions)
    {
        ArgumentNullException.ThrowIfNull(interactions);
        _interactions = interactions;
    }

    /// <summary>Gives the player the level's automatic weapon pickups.</summary>
    public void UseGunPickups(GunPickupRegistry pickups)
    {
        ArgumentNullException.ThrowIfNull(pickups);
        _pickups.UseGuns(pickups);
    }

    /// <summary>Gives the player the level's automatic generic item pickups.</summary>
    public void UseGenericPickups(GenericPickupRegistry pickups)
    {
        ArgumentNullException.ThrowIfNull(pickups);
        _pickups.UseGeneric(pickups);
    }

    /// <summary>Raised for each weapon collected from the world this tick.</summary>
    public event Action<GunPickup>? GunPickedUp
    {
        add => _pickups.GunPickedUp += value;
        remove => _pickups.GunPickedUp -= value;
    }

    /// <summary>Raised for each authored generic item collected from the world this tick.</summary>
    public event Action<GenericPickup>? GenericPickedUp
    {
        add => _pickups.GenericPickedUp += value;
        remove => _pickups.GenericPickedUp -= value;
    }

    /// <summary>Raised when a task is pressed or a held interaction completes.</summary>
    public event Action<int>? TaskActivated;

    /// <summary>Raised when the player mounts a stationary gun whose weapon was never bound.</summary>
    public event Action<AiStationaryGun>? UnarmedStationaryGun;

    /// <summary>Raised when a powered electric fence contact passes the retail 45-tick guard.</summary>
    public event Action<WorldInteraction>? ElectrifiedFenceTouched;

    /// <summary>Gets whether a non-ladder interaction currently owns locomotion.</summary>
    public bool IsInWorldInteraction => _interaction.Active is not null;

    /// <summary>Gets the active interaction's retail locomotion state, or -1.</summary>
    public int InteractionLocomotionState => _interaction.LocomotionState;

    /// <summary>Gets where the third-person interaction body is placed.</summary>
    public Vector3d InteractionFeet => _body.Position;

    /// <inheritdoc cref="PlayerInteractionDriver.Facing"/>
    public float InteractionFacing => _interaction.Facing;

    /// <inheritdoc cref="PlayerInteractionDriver.ApplyRootMotion"/>
    public void ApplyInteractionRootMotion(Vector3d rootDelta, bool clipEnded = false, float? stepEventTime = null) =>
        _interaction.ApplyRootMotion(rootDelta, clipEnded, stepEventTime);

    /// <inheritdoc cref="PlayerInteractionDriver.IsOnLevelWire"/>
    public bool IsOnLevelWire => _interaction.IsOnLevelWire;

    /// <inheritdoc cref="PlayerInteractionDriver.WireDragDirection"/>
    public int WireDragDirection => _interaction.WireDragDirection;

    /// <inheritdoc cref="PlayerInteractionDriver.TakeWireDragSound"/>
    public string? TakeWireDragSound() => _interaction.TakeWireDragSound();

    /// <summary>Gets whether the active clip loops in the original state initializer.</summary>
    public bool InteractionAnimationLoops => _interaction.AnimationLoops;

    /// <inheritdoc cref="PlayerInteractionDriver.Progress"/>
    public float? InteractionProgress => _interaction.Progress;

    /// <summary>Gets the retail full-body animation selected by the active state.</summary>
    public int InteractionAnimation => _interaction.Animation;

    /// <summary>Gets the retail action sprite selected by the current task.</summary>
    public string? ActionSprite => _interaction.ActionSprite(_ladder);

    /// <inheritdoc cref="PlayerInteractionDriver.Prompt"/>
    public InteractionPromptTarget? InteractionPrompt(float tickBlend) =>
        _interaction.Prompt(_body.Eye, tickBlend, _ladder);

    /// <summary>Gets whether the player is on a ladder.</summary>
    public bool IsOnLadder => _ladder.IsOnLadder;

    /// <summary>Gets where the climbing body's feet are, in engine units.</summary>
    public Vector3d LadderFeet => _body.Position;

    /// <summary>Gets the yaw the climbing body faces — into the ladder, in radians.</summary>
    public float LadderFacing => _ladder.Facing;

    /// <summary>Gets the active retail ladder locomotion phase.</summary>
    public LadderTraversalPhase LadderPhase => _ladder.Phase;

    /// <summary>Gets animation 168's current playback direction.</summary>
    public int LadderDirection => _ladder.Direction;

    /// <summary>Gets the climb animation — state 37's binding, animation 168 in the player's set.</summary>
    public static int LadderClimbAnimation => PlayerLadderController.ClimbAnimation;

    /// <summary>Gets state 40's slide animation, 169 in the retail player set.</summary>
    public static int LadderSlideAnimation => PlayerLadderController.SlideAnimation;

    /// <summary>Gets states 38/39's shared top transition animation, 170 in the retail player set.</summary>
    public static int LadderTopAnimation => PlayerLadderController.TopAnimation;

    /// <summary>Gets whether a ladder is in reach and its retail action icon should be shown.</summary>
    public bool ShowLadderAction => _ladder.ShowAction;

    /// <inheritdoc cref="PlayerPlacement.PlaceAt"/>
    public bool PlaceAt(IGroundHeightField? terrain, PlayerSpawnPoint spawn, WorldRayCast? registrationGround = null)
    {
        _firstPerson.Pair.HasPair = false;
        _firstPerson.ResetKicks();
        return PlayerPlacement.PlaceAt(_body, terrain, spawn, registrationGround);
    }

    /// <summary>Takes the look from this frame's mouse motion.</summary>
    /// <param name="input">The window's input.</param>
    /// <param name="seconds">Real seconds since the last call, for the key turn.</param>
    /// <exception cref="ArgumentNullException"><paramref name="input"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// Looking is per frame and movement is per tick, deliberately: the view should be as smooth as the
    /// display allows, while the body must step identically however fast the frames come.
    /// </remarks>
    public void Look(InputState input, float seconds)
    {
        ArgumentNullException.ThrowIfNull(input);

        if (_firstPerson.Turn(input.Mouse, MountedStationaryGun) is { } steered)
        {
            _interaction.FaceAlongGun(steered);
            SyncView();
            return;
        }

        // The rig follows the look every frame, not every tick: the weapon is attached to the view and
        // has to stay welded to it however fast the frames come.
        SyncView();
    }

    /// <summary>
    /// Advances only the presentation while the map computer is open. The player remains grounded and
    /// motionless, but the first-person rig keeps advancing exactly as the retail state-45 handler does.
    /// </summary>
    /// <param name="input">
    /// The input, whose map key closes the computer as state 45's run does (<c>0x40E3C0</c>); or <see langword="null"/> for an
    /// empty frame, where the host closes it.
    /// </param>
    /// <returns><see langword="true"/> when the map key went down this tick.</returns>
    public bool TickMapComputer(InputState? input = null)
    {
        if (!_mapComputerOpen)
        {
            return false;
        }

        // The console stays usable over the map, so a cheat can change while it is open.
        ApplyCheats(PlayerCheatOptions.Current);

        // Map controls are consumed by ComputerMap, not HumanPlayer, and the weapon machine waits out state 11 whatever is
        // held. Without the host's input the frame is empty, which keeps the machine's clock.
        _input.Accept(input is null ? default : PlayerInputSampler.SampleFrame(input, InputProfile));
        bool close = input is not null && _input.IsRisingEdge(PlayerInputSlot.MapComputer);
        _locomotion.Animator?.Enter(45);
        TickWeapons();
        _firstPerson.Rig?.Tick();
        _weapons?.Sway.Advance();

        // 0x464BD0 runs whatever the locomotion state is, so a dip that began before the computer was
        // opened keeps playing out rather than freezing the view part-way down.
        _firstPerson.AdvanceKicks(_random.NextDouble);
        PoseChanged = true;
        SyncView();
        CaptureViewTickState();
        return close;
    }

    /// <summary>Gets the difference a co-op server's correction left between the eye and the body, fading.</summary>
    public OpenIGI.Game.Multiplayer.PredictionErrorSmoothing ViewError { get; } = new();

    /// <summary>Takes the held-weapon machine out of death, as a co-op player is brought back.</summary>
    public void ReviveWeapons() => _weapons?.Revive();

    /// <summary>Stops the held-weapon machine because the player has died.</summary>
    /// <remarks>
    /// Death stops ordinary player ticks immediately, so this cannot wait for the next input frame to
    /// release a held trigger.
    /// </remarks>
    public void StopWeaponsForDeath()
    {
        // A player killed on a stand lets go of it, or the gun stays occupied and firing.
        if (_interaction.Active?.Type == InteractionActivateType.StationaryGun)
        {
            _interaction.End(completed: false, terrain: null);
        }

        _weapons?.StopForDeath();
    }

    /// <inheritdoc cref="PlayerWeaponActions.UpperBodyAnimation"/>
    public int UpperBodyAnimation => _weaponActions.UpperBodyAnimation;

    /// <summary>Gets whether <see cref="UpperBodyAnimation"/> repeats.</summary>
    public bool UpperBodyAnimationLoops => _weaponActions.UpperBodyAnimationLoops;

    /// <summary>Gets how long the torso clip eases in — 0.4 s for stances, immediate for a shot.</summary>
    public float UpperBodyBlendSeconds => _weaponActions.UpperBodyBlendSeconds;

    /// <summary>Gets a counter bumped every time a new torso clip is requested.</summary>
    public int UpperBodySequence => _weaponActions.UpperBodySequence;

    /// <summary>Gets whether the rig has two tick states for the presentation to draw between.</summary>
    public bool HasViewTickPair => _firstPerson.Pair.HasPair;

    /// <summary>Records the rig's per-tick halves, keeping the tick before's.</summary>
    private void CaptureViewTickState() => _firstPerson.Capture(_weapons);

    /// <inheritdoc cref="PlayerFirstPersonView.SmoothedLookRotation"/>
    public Matrix3f SmoothedLookRotation(float alpha) => _firstPerson.SmoothedLookRotation(AimPitch, alpha);

    /// <inheritdoc cref="PlayerFirstPersonView.SmoothedEyeMetres"/>
    public Vector3d SmoothedEyeMetres(float alpha) => _firstPerson.SmoothedEyeMetres(alpha);

    /// <summary>Gets the complete first-person camera at one shared between-tick presentation point.</summary>
    public Camera3D SmoothedCamera(float alpha) =>
        new(SmoothedEyeMetres(alpha), SmoothedLookRotation(alpha));

    /// <inheritdoc cref="PlayerShotResolver.AimConvergenceRay"/>
    public (Vector3d OriginUnits, Vector3f Direction)? AimConvergenceRay
    {
        get => _shots.AimConvergenceRay;
        set => _shots.AimConvergenceRay = value;
    }

    /// <inheritdoc cref="PlayerShotResolver.AimConvergenceReachInUnits"/>
    public const double AimConvergenceReachInUnits = PlayerShotResolver.AimConvergenceReachInUnits;

    /// <inheritdoc cref="PlayerViewInterpolator.SmoothedSightZoom"/>
    public SightZoomFrame SmoothedSightZoom(float alpha) => _firstPerson.SmoothedSightZoom(alpha);

    /// <inheritdoc cref="PlayerViewInterpolator.SmoothedRig"/>
    public (
        Vector3d OriginUnits,
        Matrix3f Orientation,
        BoneAttachment? HeldWeapon,
        BoneAttachment? Magazine,
        BoneAttachment? MovingPart) SmoothedRig(float alpha) =>
        _firstPerson.SmoothedRig(AimPitch, alpha);

    /// <inheritdoc cref="PlayerLadderController.DecideStep"/>
    public LadderStepResult DecideLadderStep(bool up, bool down, bool slide) =>
        _ladder.DecideStep(up, down, slide);

    /// <inheritdoc cref="PlayerLadderController.Move"/>
    public void MoveLadder(Vector3d rootDelta) => _ladder.Move(rootDelta);

    /// <inheritdoc cref="PlayerLadderController.MoveEntryFrame"/>
    public void MoveLadderEntryFrame(Vector3d rootDelta) => _ladder.MoveEntryFrame(rootDelta);

    /// <summary>Commits the rung whose clip has just completed, returning the climb to a boundary.</summary>
    public void CompleteLadderStep() => _ladder.CompleteStep();

    /// <inheritdoc cref="PlayerLadderController.AcceptRungEvent"/>
    public bool AcceptLadderRungEvent(float time) => _ladder.AcceptRungEvent(time);

    /// <inheritdoc cref="PlayerLadderController.TakeStep"/>
    public string TakeLadderStep() => _ladder.TakeStep(AiNoise);

    /// <inheritdoc cref="PlayerLadderController.CompleteTopTransition"/>
    public void CompleteLadderTopTransition() => _ladder.CompleteTopTransition();

    /// <inheritdoc cref="PlayerLadderController.TickSlide"/>
    public bool TickLadderSlide(Vector3d rootDelta, IGroundHeightField? terrain) => _ladder.TickSlide(rootDelta, terrain);

    private void TickWeapons()
    {
        if (_weapons is null)
        {
            return;
        }

        if (_weaponActions.Tick(_weapons, _input, _firstPerson.Rig, _body, LocomotionState))
        {
            PoseChanged = true;
        }
    }

    // 0x462E50, which HumanPlayer_Update calls after 0x464BD0 has committed the view: the optics
    // first, then whatever the weapon in hand fires.
    private void ResolveFire()
    {
        _shots.ClearImpacts();

        HumanView? view = _firstPerson.Rig;
        if (view is not null)
        {
            _optics.Tick(_weapons, view, _input);
        }

        if (_weapons is null || view is null)
        {
            return;
        }

        _shots.Fire(_weapons, view, _optics.IsSniperScoped);
    }

    /// <inheritdoc cref="PlayerShotResolver.KnifeReachInUnits"/>
    public const double KnifeReachInUnits = PlayerShotResolver.KnifeReachInUnits;

    /// <inheritdoc cref="PlayerShotResolver.KnifeFanRadians"/>
    public static readonly float[] KnifeFanRadians = PlayerShotResolver.KnifeFanRadians;

    /// <inheritdoc cref="PlayerShotResolver.KnifeBladeDirection"/>
    public static Vector3d KnifeBladeDirection(Vector3f forward, Vector3f right, float angle) =>
        PlayerShotResolver.KnifeBladeDirection(forward, right, angle);

    /// <inheritdoc cref="PlayerShotResolver.Deflected"/>
    public Action<Vector3d>? Deflected { get => _shots.Deflected; set => _shots.Deflected = value; }

    // The interaction driver's view of this player.
    PlayerWeapons? IPlayerInteractionHost.Weapons => _weapons;
    void IPlayerInteractionHost.SyncView() => SyncView();
    void IPlayerInteractionHost.MarkPoseChanged() => PoseChanged = true;
    void IPlayerInteractionHost.DropViewTickPair() => _firstPerson.Pair.HasPair = false;
    WorldTraceCast? IPlayerInteractionHost.PlantTrace() => PlayerWorldTraces.Plant(_walls, TerrainTrace);
    void IPlayerInteractionHost.OnTaskActivated(int taskId) => TaskActivated?.Invoke(taskId);
    void IPlayerInteractionHost.OnUnarmedStationaryGun(AiStationaryGun gun) => UnarmedStationaryGun?.Invoke(gun);
    void IPlayerInteractionHost.OnElectrifiedFenceTouched(WorldInteraction fence) => ElectrifiedFenceTouched?.Invoke(fence);
    void IPlayerInteractionHost.OnAiNoise(AiEvent noise) => AiNoise?.Invoke(noise);
}
