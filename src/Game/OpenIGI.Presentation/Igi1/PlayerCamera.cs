using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;
using OpenIGI.Formats.Animation;
using OpenIGI.Game.Ai;
using OpenIGI.Game.Config;
using OpenIGI.Game.Player;
using OpenIGI.Game.Weapons.Ballistics;
using OpenIGI.Game.Weapons;
using OpenIGI.Game.World;
using OpenIGI.Game;
using OpenIGI.Input;
using OpenIGI.Presentation.Cameras;
using OpenIGI.Presentation.Rendering;
using OpenIGI.Rendering;

namespace OpenIGI.Presentation.Igi1;

/// <summary>
/// Drives a <see cref="PlayerBody"/> from the keyboard and mouse, and gives the renderer its eye.
/// </summary>
/// <remarks>
/// <para>
/// The counterpart to <see cref="FreeCamera"/>: where that flies, this one walks. It advances the body
/// on the <b>fixed thirty-hertz tick</b> rather than on real elapsed time, because the body's motion
/// is the game's own and has to stay deterministic — only the look is taken per frame, so the view
/// stays smooth at any frame rate.
/// </para>
/// <para>
/// <b>Movement is root motion, from the animation.</b> There is no speed constant anywhere: the input
/// picks a locomotion state, the state names an animation, and the animation's root-motion track says
/// how far the body moved. Changing the animation changes the speed, which is how the original works.
/// </para>
/// </remarks>
public sealed class PlayerCamera
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
    private readonly IOpenIgiLogger _logger;
    private readonly PlayerInputState _input = new();
    private readonly IgiRandom _random = new();
    private readonly Mp5SightZoom _sightZoom = new();
    private readonly DragunovScope _dragunovScope = new();
    private readonly BinocularsView _binocularsView = new();
    private readonly List<GunImpact> _impacts = [];
    private readonly BallisticSimulation _ballistics = new();
    private readonly GrenadeSimulation _grenades = new();
    private readonly MissileSimulation _missiles = new();
    private readonly M203Simulation _m203 = new();
    private readonly HumanLadderClimb _climb = new();
    private HumanLocomotionAnimator? _animator;
    private ObjectCollisionIndex? _walls;
    private string _lastBlockLabel = string.Empty;
    private HumanView? _view;
    private PlayerWeapons? _weapons;
    private WeaponType? _drawnWeapon;
    private GameMaterialRegistry? _materials;
    private int _footCursor;
    private bool _wasCapturing;
    private IReadOnlyList<LadderPlacement> _ladders = [];
    private LadderPlacement? _nearLadder;
    private bool _nearLadderAtTop;
    private double _nearLadderAngle = double.MaxValue;
    private int _nearLadderIndex = -1;
    private WorldInteractionRegistry? _interactions;
    private GunPickupRegistry? _gunPickups;
    private WorldInteraction? _nearInteraction;
    private WorldInteraction? _activeInteraction;

    // The gun on the stand the player is on, when they are on one. The emplacement's weapon is its
    // own task in the original, fired by the stand with the occupant's trigger (0x474360); this is it.
    private HeldGun? _mountedGun;
    private int _interactionTick;
    private bool _interactionUseArmed;
    private Vector3d _fenceRootDelta;
    private Vector3d _fenceAppliedDelta;
    private bool _fenceClipEnded;
    private int _electricFenceCooldown;
    private float _interactionFacing;
    private Vector3d _wireDirection;
    private double _wireSpeed;
    private bool _mapComputerOpen;
    /// <summary>
    /// The walk/run latch. <b>True is the retail default</b>: the byte the toggle flips,
    /// <c>HumanPlayer+3397</c>, is zeroed by the constructor at <c>0x410370</c>, and the flag word is
    /// built with <c>if (byte) flags |= 0x40000; else flags &amp;= ~0x40000</c> at <c>0x4105EA</c> — so a
    /// zero byte means the <i>walking</i> bit is clear, which selects the running set, states 1–8.
    /// A player therefore spawns running and the first press of the toggle makes them walk.
    /// </summary>
    private bool _running = true;
    private GenericPickupRegistry? _genericPickups;

    /// <summary>Initializes the player camera.</summary>
    /// <param name="logger">Where optional collision diagnostics are written.</param>
    public PlayerCamera(IOpenIgiLogger? logger = null)
    {
        _logger = logger ?? NullLogger.Instance;
    }

    /// <summary>Gets whether the expensive wall-contact diagnostic is enabled.</summary>
    internal bool WallBlockDiagnosticsEnabled => _logger.IsEnabled(LogLevel.Information);

    // The first-person rig's last two tick states, captured at the end of Tick() — what the
    // presentation draws between when r_smooth is on. All of it is kept in bone/eye space WITHOUT the
    // look folded in, so the per-frame mouse orientation composes on top and the weapon stays welded
    // to the view however fast the frames come.
    private Vector3d _prevTickEye;
    private Vector3d _tickEye;
    private readonly HumanLandingViewKick _landingKick = new();
    private readonly HumanViewShake _viewShake = new();
    private double _prevTickWallPullback;
    private double _tickWallPullback;
    private Vector3f _prevTickRootMotion;
    private Vector3f _tickRootMotion;
    private ViewBonePair _weaponBone;
    private ViewBonePair _magazineBone;
    private ViewBonePair _movingPartBone;
    private float _prevTickSwayPitch;
    private float _tickSwayPitch;
    private float _prevTickSwayYaw;
    private float _tickSwayYaw;
    private float _prevTickRecoilPitch;
    private float _tickRecoilPitch;
    private float _prevTickTanX = HumanView.DefaultTanX;
    private float _tickTanX = HumanView.DefaultTanX;
    private float _prevTickViewModelTanX = HumanView.DefaultViewModelTanX;
    private float _tickViewModelTanX = HumanView.DefaultViewModelTanX;

    /// <summary>Gets the body being driven.</summary>
    public PlayerBody Body => _body;

    /// <summary>Uses the owning runtime's live player-health accumulator.</summary>
    public void UseHealth(HumanHealth health) => _body.UseHealth(health);

    /// <summary>Gets the first-person rig the held weapon hangs off, once one has been given.</summary>
    public HumanView? View => _view;

    /// <summary>Gets the weapon machine, once the player has been given a weapon cycle.</summary>
    public PlayerWeapons? Weapons => _weapons;

    /// <summary>Gets what the last tick's rounds hit.</summary>
    /// <remarks>Empty on every tick nothing was fired, which is most of them.</remarks>
    public IReadOnlyList<GunImpact> Impacts => _impacts;

    /// <summary>Gets or sets a callback that determines whether the player is allowed to jump.</summary>
    /// <remarks>
    /// When this returns <see langword="false"/>, the jump input is ignored. Used to suppress jumping
    /// while the player is inside an elevator cab — retail does not allow jumping while riding a lift.
    /// </remarks>
    public Func<bool>? CanJump { get; set; }

    /// <summary>Gets the marks rounds have left on the world.</summary>
    public BulletMarks Marks { get; } = new();

    private readonly List<PendingMark> _pendingMarks = [];

    /// <summary>Gets the rounds in flight under <c>g_ballistics</c>, for the tracer renderer.</summary>
    /// <remarks>Empty whenever the option is off — nothing is ever spawned into it.</remarks>
    public BallisticSimulation Ballistics => _ballistics;

    /// <summary>Gets whether the Dragunov's scope is open, for the HUD's overlay.</summary>
    public bool IsSniperScoped => _dragunovScope.IsScoped;

    /// <summary>Gets whether the binocular overlay is open.</summary>
    public bool IsBinocularsOpen => _binocularsView.IsOpen;

    /// <summary>Gets whether binoculars opened on the last simulation tick.</summary>
    public bool BinocularsOpenedThisTick => _binocularsView.OpenedThisTick;

    /// <summary>Gets whether binoculars closed on the last simulation tick.</summary>
    public bool BinocularsClosedThisTick => _binocularsView.ClosedThisTick;

    /// <summary>Gets whether the binocular zoom target changed on the last simulation tick.</summary>
    public bool BinocularZoomTargetChangedThisTick => _binocularsView.ZoomTargetChangedThisTick;

    /// <summary>Gets whether a held zoom key moved the binocular magnification on the last tick.</summary>
    public bool BinocularZoomSteppingThisTick => _binocularsView.ZoomSteppingThisTick;

    /// <summary>Gets how far the binoculars are zoomed in, 0 at the default to 1 at the tightest zoom.</summary>
    /// <param name="currentTanX">The optics' current horizontal half-angle tangent.</param>
    public float BinocularZoomInFraction(float currentTanX) => _binocularsView.ZoomInFraction(currentTanX);

    /// <summary>Closes all optical devices and restores both first-person FOV tangents.</summary>
    /// <remarks>
    /// The desktop host stops ticking the player as soon as the death camera takes over. Resetting
    /// here prevents a binocular or sniper latch and its narrowed tangent from surviving outside
    /// first-person control.
    /// </remarks>
    public void ResetOptics()
    {
        _binocularsView.Reset();
        _sightZoom.Reset();
        _dragunovScope.Reset();

        if (_view is { } view)
        {
            view.TanX = HumanView.DefaultTanX;
            view.ViewModelTanX = HumanView.DefaultViewModelTanX;
        }

        _prevTickTanX = HumanView.DefaultTanX;
        _tickTanX = HumanView.DefaultTanX;
        _prevTickViewModelTanX = HumanView.DefaultViewModelTanX;
        _tickViewModelTanX = HumanView.DefaultViewModelTanX;
        HasViewTickPair = false;
    }

    /// <summary>Gets the grenades in the air, both the player's and the AI's.</summary>
    /// <remarks>
    /// One simulation for both throwers so there is one place to tick, draw and detonate from — the
    /// soldiers spawn their lobs into it through the renderer's wiring.
    /// </remarks>
    public GrenadeSimulation Grenades => _grenades;

    /// <summary>Gets the launcher missiles currently in flight.</summary>
    public MissileSimulation Missiles => _missiles;

    /// <summary>Gets the M203 rounds currently in flight.</summary>
    public M203Simulation M203 => _m203;

    /// <summary>Gets or sets what a grenade bounces off — the world <b>including the terrain</b>.</summary>
    /// <remarks>
    /// Deliberately not <see cref="PlayerBody.Walls"/>: that is the object index alone, because the
    /// body meets the terrain through its own ground query. A grenade has no ground query — without
    /// the terrain in its cast it falls straight through the floor of the world.
    /// </remarks>
    public WorldRayCast? GrenadeWalls { get; set; }

    /// <summary>
    /// Gets or sets the rich world trace used by proximity mines to distinguish an object from terrain.
    /// </summary>
    public WorldTraceCast? GrenadeTrace { get; set; }

    /// <summary>Gets or sets the world cast used by launcher missiles.</summary>
    public WorldRayCast? MissileWalls { get; set; }

    /// <summary>Gets or sets the rich world trace used to retain a launcher's exact impact object.</summary>
    public WorldTraceCast? MissileTrace { get; set; }

    /// <summary>Gets or sets a trace through the terrain, for the casts a ground query cannot answer.</summary>
    /// <remarks>
    /// The view model's wall pullback sweeps <em>forward</em>, so it wants a ray through the terrain
    /// rather than the height under a point -- which is why it arrives as a cast the host supplies and not
    /// as something read off the ground the body stands on. A title whose terrain nothing can trace
    /// leaves this null, and the pullback then meets placed objects only.
    /// </remarks>
    public WorldTraceCast? TerrainTrace { get; set; }

    /// <summary>Gets or sets what offers this player's rounds to the humans in the world.</summary>
    /// <remarks>
    /// Without it rounds stop at walls and hit nobody. A round takes whichever of the two is
    /// <b>nearer</b>, so a guard standing in front of a wall is hit and the wall is not marked.
    /// </remarks>
    public HumanShot? Humans { get; set; }

    /// <summary>Gets or sets a non-mutating human intersection used by launcher missiles.</summary>
    public HumanRayCast? HumanProbes { get; set; }

    /// <summary>Gets or sets the living-human query used by armed proximity mines.</summary>
    public HumanProximityCast? HumanProximityTargets { get; set; }

    /// <summary>Gets or sets where actual projectile contacts are offered to the AI event broadcaster.</summary>
    /// <remarks>
    /// This is invoked only when the world surface is the nearest hit. A human in front of that surface
    /// receives damage instead and does not also manufacture a ground-impact event behind itself.
    /// </remarks>
    public Action<Vector3d>? GroundImpact { get; set; }

    /// <summary>Gets or sets where the player's door, fence and ladder noises are offered to the AI.</summary>
    /// <remarks>
    /// Invoked with <see cref="AiEvent.Door"/> whenever the player toggles a door, by hand or at the end of
    /// a pick (<c>0x40C270</c>); with <see cref="AiEvent.Fence"/> on every tick of a fence climb but the
    /// first and the last (<c>0x411770</c>); and with <see cref="AiEvent.Ladder"/> at each ladder step.
    /// </remarks>
    public Action<AiEvent>? AiNoise { get; set; }

    /// <summary>Gets how many of this player's rounds have struck a human.</summary>
    public int HumanHits { get; private set; }

    /// <summary>Gets the zone the last round to strike a human landed in, or <c>-1</c>.</summary>
    public int LastHumanZone { get; private set; } = -1;

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
        _materials = materials;
    }

    /// <summary>The game material the level's terrain is made of, or null to fall back to <c>Ground</c>.</summary>
    /// <remarks>
    /// Read from the level's own terrain materials — see
    /// <see cref="FootstepSounds.TerrainGameMaterialIndex"/>. It is <c>Snow</c> in the four Arctic
    /// missions and <c>Ground</c> everywhere else, which is the difference between crunching through
    /// snow and treading on earth.
    /// </remarks>
    public int? TerrainGameMaterialIndex { get; set; }

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
    public WeaponType? HeldWeaponType => _mountedGun?.Weapon ?? _weapons?.InHand?.Weapon;

    /// <summary>Gets the stationary gun the player is on and firing, or <see langword="null"/>.</summary>
    public AiStationaryGun? MountedStationaryGun => _mountedGun is null ? null : _activeInteraction?.StationaryGun;

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
        _view = new HumanView(set);
        PoseChanged = true;
        SyncView();
    }

    /// <summary>Adds a weapon to what the player carries.</summary>
    /// <param name="weapon">The weapon's roster entry.</param>
    /// <param name="ammo">Its ammunition record, or <see langword="null"/> when it names no calibre.</param>
    /// <returns><see langword="false"/> when the player has no weapon machine yet.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="weapon"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// Carrying is not holding. Which of them ends up in hand is the weapon cycle's decision, taken on
    /// the first tick — see <see cref="UseWeaponCycle"/>.
    /// </remarks>
    public bool Carry(WeaponType weapon, AmmoType? ammo = null)
    {
        ArgumentNullException.ThrowIfNull(weapon);
        if (_weapons is null)
        {
            return false;
        }

        _weapons.Carry(weapon, ammo);
        return true;
    }

    /// <summary>Carries every weapon in the roster that is not carried already — the all-weapons cheat.</summary>
    /// <param name="roster">The weapons loaded for the current mission.</param>
    /// <param name="ammunition">The ammunition table each weapon's calibre is resolved against.</param>
    /// <returns>How many weapons were newly carried; zero once everything is.</returns>
    /// <remarks>
    /// Safe to call every tick. A weapon already carried keeps its magazine; a new one joins the end of
    /// the switch cycle so the cycle keys can reach it.
    /// </remarks>
    public int EnsureAllWeapons(IReadOnlyList<WeaponType> roster, AmmoTypeRegistry ammunition)
    {
        ArgumentNullException.ThrowIfNull(roster);
        ArgumentNullException.ThrowIfNull(ammunition);

        if (_weapons is null)
        {
            return 0;
        }

        int added = 0;
        foreach (WeaponType weapon in roster)
        {
            if (_weapons.HasWeapon(weapon.WeaponId))
            {
                continue;
            }

            bool hasAmmo = ammunition.TryGetByCalibre(weapon.CalibreId, out AmmoType ammo);
            _weapons.Carry(weapon, hasAmmo ? ammo : null);
            _weapons.EnsureCycleContains(weapon.WeaponId);
            added++;
        }

        return added;
    }

    /// <summary>Plays a weapon action on the first-person rig.</summary>
    /// <param name="weapon">The weapon being held.</param>
    /// <param name="action">What the human is doing with it.</param>
    /// <param name="fireVariant">Which of the three firing clips, for <see cref="WeaponAction.Fire"/>.</param>
    /// <returns><see langword="false"/> when there is no rig, or the rig has no such animation.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="weapon"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// A weapon's <b>full-body</b> animation ids are ids in the view rig's own set, not the body's:
    /// <c>0x489F20</c> plays them on bone 0 of <c>human-&gt;view-&gt;boneObj</c>. The <c>UpperBody*</c>
    /// half goes to bone 1 of the body's own rig and is not played here — the third-person body is not
    /// drawn for the player.
    /// </remarks>
    public bool PlayWeaponAction(WeaponType weapon, WeaponAction action, int fireVariant = 0)
    {
        ArgumentNullException.ThrowIfNull(weapon);
        if (_view is null)
        {
            return false;
        }

        WeaponAnimations animations = WeaponAnimationTable.Select(weapon, action, fireVariant);
        int animationId = animations.FullBody;
        bool loops = animations.LoopFullBody;

        // The upper-body half, recorded for the third-person body to play on bone 1. 0x4137E0 tests the
        // two ids INDEPENDENTLY -- `if (v4 != -1) sub_489F20(...); if (v5 != -1) sub_489ED0(...)` -- so
        // this is settled before the full-body early-out below rather than after it. An action whose
        // full-body id is -1 still moves the torso in retail.
        //
        // Bumped on every request rather than only on a change: PlayerWeapons clears RequestedAction at
        // the top of each tick and raises it only when the machine actually dispatches, so this is
        // called at exactly the cadence 0x4137E0 runs at — and a second shot asks for the SAME clip and
        // has to restart it, which a change test would swallow.
        if (animations.UpperBody >= 0)
        {
            _upperBodyAnimation = animations.UpperBody;
            _upperBodyLoops = animations.LoopUpperBody;
            _upperBodyBlendSeconds = animations.UpperBodyBlendSeconds;
            _upperBodySequence++;
        }

        // Retail's 0x40FB90 forwards FireAnimation2 to the animation dispatcher verbatim, and the
        // shipped medipack leaves that slot at -1. 0x4D61D0 leaves the view untouched for a negative
        // id, so the one-shot entry clip ("insert syringe") simply holds its final frame for the whole
        // sustained injection. Substituting a *looping* FireAnimation1 here replayed that entry over
        // and over while Fire was held; the syringe must be inserted once, then held -- not on repeat.
        if (animationId < 0)
        {
            return false;
        }

        // 0x489F20 forwards the action's blend to BoneObj_Play. The standing/moving actions ease over
        // 0.4 s; firing and reloading snap. This is what eases the medipack (and every weapon) back to
        // its stand pose on release instead of teleporting from the injection frame.
        if (_view.PlayWeaponAnimation(animationId, loops, animations.FullBodyBlendSeconds) is null)
        {
            return false;
        }

        // Action 6 alone skips the clip forward by a random fraction, which is why two shots do not
        // look identical. It sets the clip's CLOCK, not its rate -- see weapons.md section 20.
        if (action == WeaponAction.FireTimed)
        {
            _view.SkipTo(WeaponAnimationTable.TimedFireStart(_random.NextSingle()));
        }

        PoseChanged = true;
        return true;
    }

    /// <summary>Gets where the held weapon's model goes, in engine units, or <see langword="null"/>.</summary>
    public BoneAttachment? HeldWeapon => _view is null ? null : _view.HeldWeapon;

    /// <summary>Gets where the held weapon's magazine goes, in engine units, or <see langword="null"/>.</summary>
    /// <remarks>
    /// Retail's <c>TASKTYPE_GUNCLIP</c>, <c>TASKTYPE_CABARREL</c> and <c>TASKTYPE_GUNX2</c> children all
    /// ask the first-person human for attachment 2, which is palette bone 52 (<c>0x4601A0</c>), rather
    /// than reusing the gun attachment at bone 3.
    /// </remarks>
    public BoneAttachment? HeldWeaponSecondary => _view is null ? null : _view.HeldWeaponSecondary;

    /// <summary>Gets where the held weapon's slide or bolt goes, in engine units, or <see langword="null"/>.</summary>
    /// <remarks>
    /// <c>TASKTYPE_GUNMOVINGPART</c> and <c>TASKTYPE_CACLIP</c> ask for attachment 3 — palette bone 55
    /// (<c>0x460210</c>).
    /// </remarks>
    public BoneAttachment? HeldWeaponTertiary => _view is null ? null : _view.HeldWeaponTertiary;

    /// <summary>Gets whether the rig's pose has advanced since it was last acknowledged.</summary>
    /// <remarks>
    /// Only an advancing clip moves the bones — turning to look does not, because the object's own
    /// rotation is applied at draw rather than baked into the pose. So a renderer that has to rebuild
    /// something per pose can key off this rather than off the frame.
    /// </remarks>
    public bool PoseChanged { get; private set; } = true;

    /// <summary>Clears <see cref="PoseChanged"/>.</summary>
    public void AcknowledgePose() => PoseChanged = false;

    // Binds the rig to the body. The original does more here (0x464BD0 eases the aim and pushes the eye
    // out of walls with a multi-ray cast); the view-model's separate forward clearance probe is updated
    // immediately before this final binding. What is transcribed here is the attachment's eye transform,
    // built from the aim angles with 0x4B38E0's own Euler order, and the fact that 0x4D38C0 poses the
    // rig with a zero root and the object's own 3x3 above it.
    private void SyncView()
    {
        if (_view is null)
        {
            return;
        }

        _view.Eye = _body.Eye;
        _view.EyeOrientation = Matrix3f.FromEngineEulerAngles(
            AimPitch + _viewShake.Pitch, _viewShake.Roll, -_body.Yaw + _viewShake.Yaw);

        // 0x464BD0 composes the object's orientation as the eye's TIMES the weapon sway, dotting the
        // eye's rows with the sway's columns. The sway is the two angles a weapon change tips the rig
        // by, and it is the identity whenever nothing is being put away. The object's POSITION is not
        // the eye -- HumanView.Position derives it, because the rig's own root-motion track is where
        // each weapon's hold offset lives.
        _view.Orientation = _weapons is null
            ? _view.EyeOrientation
            : _view.EyeOrientation * _weapons.Sway.ToMatrix();
    }

    /// <summary>Gets whether the cursor should be held while the view is being turned.</summary>
    public bool IsCapturingMouse { get; private set; }

    /// <summary>Gets or sets how far the mouse turns the view, and which way its vertical axis runs.</summary>
    /// <remarks>
    /// The host resolves this from the active profile every frame, so a change made in the options menu
    /// takes effect on the next one. See <see cref="MouseLookSettings"/> for why the rate is what it is.
    /// </remarks>
    public MouseLookSettings LookSettings { get; set; } = MouseLookSettings.Default;

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
        _animator?.Enter(open ? 45 : HumanLocomotionStates.Idle);

        if (open && _weapons?.InHand is { } held)
        {
            // State 45 requests Crouch in HumanLocomotionWeaponActions. Play it before the first
            // frozen computer tick so the first-person rig leaves the previous gun pose immediately.
            PlayWeaponAction(held.Weapon, WeaponAction.Crouch);
        }

        PoseChanged = true;
    }

    /// <summary>Lets the pointer go and drops the presentation's tick pairs, for a frozen frame.</summary>
    /// <remarks>
    /// <para>
    /// What the pause menu needs of the player, and only that: the body is not moved, nothing is placed
    /// again, and the mission is left exactly where it stood.
    /// </para>
    /// <para>
    /// The mouse is released because <see cref="Look"/> claims the cursor unconditionally — a first-person
    /// view looks wherever the mouse goes — so a menu drawn over it would be unreachable. Clearing the
    /// capture also re-arms the settling frame, which is what stops the warp that re-capturing produces on
    /// resume from arriving as a movement the player never made.
    /// </para>
    /// <para>
    /// The tick pairs go with it, because a pair kept across the menu is a pair whose two halves are on
    /// either side of it: the first frame after resuming would draw the view somewhere between the tick
    /// before the pause and the tick after it. Dropping them makes the next tick build a fresh pair from
    /// where the player still is, at the cost of one tick drawn unsmoothed.
    /// </para>
    /// </remarks>
    public void HoldStill()
    {
        ReleaseLook();
        HasViewTickPair = false;
        _weaponBone.Forget();
        _magazineBone.Forget();
        _movingPartBone.Forget();
    }

    /// <summary>Gives the pointer up without disturbing the presentation, and re-arms the settling frame.</summary>
    /// <remarks>
    /// <para>
    /// For everything that takes the mouse away from the look but leaves the player standing where they
    /// were: the death camera's orbit, the third-person interaction camera, the map computer. Each of
    /// those drops the cursor lock, and dropping the lock makes the backend re-centre its virtual pointer
    /// — so the first motion after the lock comes back is a jump that has nothing to do with the player's
    /// hand. <see cref="Look"/> skips exactly one frame after this, which is what absorbs it.
    /// </para>
    /// <para>
    /// Unlike <see cref="HoldStill"/> the rig's tick pair is kept, because the player is still being
    /// drawn: dropping it here would cost a visibly unsmoothed frame every time the map computer closed.
    /// </para>
    /// </remarks>
    public void ReleaseLook()
    {
        IsCapturingMouse = false;
        _wasCapturing = false;
    }

    /// <summary>Gets the eye the scene is drawn from, in the renderer's metres.</summary>
    /// <remarks>On a stationary gun it is the gun's own sight, turned and kicked with the barrel.</remarks>
    public Camera3D Camera => MountedStationaryGun is { } mounted
        ? GunSight(mounted)
        : new(
            ToMetres(_body.Eye + LandingKickOffsetUnits + ViewShakeOffsetUnits),
            Shaken(_body.Yaw, AimPitch, _viewShake.Pitch, _viewShake.Roll, _viewShake.Yaw));

    // The camera 0x45FCD0 builds in mode 4: the gun's point and orientation used outright. The eye's
    // right, forward and up are the orientation's columns 0, 1 and 2, as they are for the walking eye.
    private static Camera3D GunSight(AiStationaryGun gun)
    {
        Matrix3f m = gun.GunOrientation;
        return new Camera3D(
            ToMetres(gun.SightPosition),
            Matrix3f.FromRows(
                new Vector3f(m.M11, m.M21, m.M31),
                new Vector3f(m.M13, m.M23, m.M33),
                new Vector3f(m.M12, m.M22, m.M32)));
    }

    /// <summary>Gets the dip the last landing installed in the view.</summary>
    /// <remarks>
    /// Armed by <see cref="InstallLandingViewKick"/> off the body's fall impact and stepped beside the
    /// weapon sway, which is where <c>0x464BD0</c> steps both.
    /// </remarks>
    public HumanLandingViewKick LandingKick => _landingKick;

    /// <summary>The landing dip as a world-space displacement, in units — <c>0x464BD0</c> adds it to Z.</summary>
    private Vector3d LandingKickOffsetUnits => new(0.0, 0.0, _landingKick.OffsetUnits);

    /// <summary>Gets the shake the last hit put in the view.</summary>
    public HumanViewShake ViewShake => _viewShake;

    /// <summary>Arms the view shake, as the player's hit handlers do through <c>0x465970</c>.</summary>
    /// <param name="positionUnits">The full eye displacement, in units.</param>
    /// <param name="angleRadians">The full angle, in radians.</param>
    /// <param name="ticks">How many ticks it fades over.</param>
    public void InstallViewShake(float positionUnits, float angleRadians, int ticks) =>
        _viewShake.Install(positionUnits, angleRadians, ticks);

    /// <summary>The shake's eye displacement in world units: along the view's horizontal right, and up.</summary>
    private Vector3d ViewShakeOffsetUnits => new(
        _viewShake.RightUnits * Math.Cos(_body.Yaw),
        -_viewShake.RightUnits * Math.Sin(_body.Yaw),
        _viewShake.UpUnits);

    /// <summary>The camera's look with a shake folded in — the engine's gamma and the roll about forward.</summary>
    private static Matrix3f Shaken(float yaw, float pitch, float shakePitch, float shakeRoll, float shakeYaw)
    {
        // The engine's eye is Z(-yaw) Y(roll) X(pitch); the shake adds to its gamma, which is minus the yaw.
        Matrix3f look = Camera3D.LookRotation(yaw - shakeYaw, pitch + shakePitch);
        if (shakeRoll == 0f)
        {
            return look;
        }

        float c = MathF.Cos(shakeRoll);
        float sn = MathF.Sin(shakeRoll);
        Vector3f right = new(look.M11, look.M12, look.M13);
        Vector3f up = new(look.M21, look.M22, look.M23);
        Vector3f forward = new(look.M31, look.M32, look.M33);
        return Matrix3f.FromRows((right * c) + (up * sn), (up * c) - (right * sn), forward);
    }

    /// <summary>Arms the landing dip, as <c>0x408460</c> does through <c>0x4659B0</c>.</summary>
    /// <param name="amplitudeUnits">
    /// <see cref="HumanFallImpact.ViewKickUnits"/> from the landing the body just resolved.
    /// </param>
    public void InstallLandingViewKick(float amplitudeUnits) => _landingKick.Install(amplitudeUnits);

    /// <summary>Gets where the player is standing, in metres, for diagnostics.</summary>
    public Vector3d Position => ToMetres(_body.Position);

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

    /// <summary>The footstep the player's locomotion clip took this tick, if it took one.</summary>
    /// <returns>The footstep, or <see langword="null"/> when no step landed.</returns>
    /// <remarks>
    /// <para>
    /// The player's rig is <see cref="HumanLocomotionAnimator"/>, not the <c>BoneClip</c> a soldier
    /// walks on, so the event has to be asked of the animator — but it is the same clip carrying the
    /// same event 1010, and the animator now tests the same tick interval a <c>BoneClip</c> does.
    /// </para>
    /// <para>
    /// The material comes from its own short downward cast, as the original's does, and a step taken in
    /// mid-air is not a step at all.
    /// </para>
    /// </remarks>
    public Footstep? TakeFootstep()
    {
        if (_animator is null
            || _materials is null
            || !_body.IsGrounded
            || !_animator.CrossedEvent(FootstepSounds.EventId))
        {
            return null;
        }

        if (!TryGetGroundMaterial(out GameMaterial material, out Vector3d where))
        {
            return null;
        }

        string sound = FootstepSounds.Next(material, ref _footCursor, out int slot);
        if (sound.Length == 0)
        {
            return null;
        }

        // The crouch byte does two things in 0x463920: it drops the voice to two thirds and it skips
        // the AI hearing event. A zero radius carries the second half to the caller.
        bool crouched = _body.IsCrouched;
        return new Footstep(
            sound,
            where,
            crouched ? 0.0 : FootstepSounds.HearingRadiusInUnits(material),
            slot,
            crouched ? FootstepSounds.CrouchedVolumeScale : 1f);
    }

    /// <summary>The sound the ground makes when the player lands on it, or null when it names none.</summary>
    /// <returns>The definition name and where to play it, in engine units.</returns>
    /// <remarks>
    /// Both airborne states play the struck material's land sound on contact — <c>0x40BE40</c> for a
    /// fall and <c>0x40BBD0</c> for a jump — reading the same <c>Human+1772</c> the footstep handler
    /// reads. It is separate from, and additional to, the <c>player_fall_N</c> that a damaging drop
    /// plays out of <c>0x408460</c>.
    /// </remarks>
    public (string Sound, Vector3d PositionInUnits)? TakeLandingSound()
    {
        if (!TryGetGroundMaterial(out GameMaterial material, out Vector3d where))
        {
            return null;
        }

        string sound = FootstepSounds.LandSound(material);
        return sound.Length == 0 ? null : (sound, where);
    }

    /// <summary>Finds the material under the feet with the original's own short downward probe.</summary>
    /// <param name="material">Receives the material.</param>
    /// <param name="where">Receives the ground contact point, in engine units.</param>
    /// <returns><see langword="false"/> when there is no material table, or no row to fall back to.</returns>
    /// <remarks>
    /// The probe answers only for OBJECTS. A step that finds nothing solid landed on open terrain,
    /// which the original resolves by asking the terrain's own task -- something this port has no
    /// equivalent of, so it takes the Ground row by name instead.
    /// </remarks>
    private bool TryGetGroundMaterial(out GameMaterial material, out Vector3d where)
    {
        (Vector3d from, Vector3d to) = FootstepSounds.Probe(_body.Position);
        where = _body.Position;

        if (_materials is null)
        {
            material = null!;
            return false;
        }

        // The SURFACE cast, not the body's. The player moves against collision level one -- the
        // human-collision hull, every face of which is material 21 and names no walk sound at all --
        // so probing with it found a floor made of nothing and left every step on placed geometry
        // silent. The general cast is the geometry bullets hit, and its faces carry the real materials:
        // wood, concrete, metal, gravel. The guards were already using it.
        //
        // A struck face still only answers if it names something to walk on; see FootstepSounds.NamesAWalk.
        if (_walls?.Caster() is { } walls
            && walls(from, to, out WorldRayHit hit)
            && _materials.TryGet(hit.Material, out GameMaterial struck)
            && FootstepSounds.NamesAWalk(struck))
        {
            material = struck;
            where = hit.Position;
            return true;
        }

        // Open terrain: the level says what its ground is made of, exactly as a face does.
        if (TerrainGameMaterialIndex is int terrain && _materials.TryGet(terrain, out material))
        {
            return true;
        }

        return _materials.TryGetByName(FootstepSounds.TerrainMaterialName, out material);
    }

    /// <summary>Gets whether the player is crouched.</summary>
    public bool IsCrouched => _body.IsCrouched;

    /// <summary>Gets whether the last tick's move was stopped by a wall.</summary>
    public bool TouchedWall => _body.TouchedWall;

    /// <summary>Puts the player on the ground under a point.</summary>
    /// <param name="terrain">The level's terrain, or <see langword="null"/> for an object-only interior.</param>
    /// <param name="at">Where to try standing, in the renderer's metres.</param>
    /// <param name="yaw">Which way to face.</param>
    /// <returns><see langword="false"/> when there is no ground under the point.</returns>
    public bool Place(IGroundHeightField? terrain, Vector3d at, float yaw)
    {
        _body.Position = new Vector3d(at.X * UnitsPerMetre, at.Y * UnitsPerMetre, at.Z * UnitsPerMetre);
        _body.Yaw = yaw;

        // A placement is a teleport; the rig's tick pair must not be interpolated across it, and a
        // landing dip still playing belongs to the ground that was left behind.
        HasViewTickPair = false;
        _landingKick.Reset();
        _viewShake.Reset();
        return _body.PlaceOnGround(terrain);
    }

    /// <summary>Gives the player the animation set its skeleton uses.</summary>
    /// <param name="set">The set, normally <c>000.iff</c> — the one the player's own skeleton indexes.</param>
    /// <exception cref="ArgumentNullException"><paramref name="set"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// Without this the player still stands and falls but cannot walk, because nothing supplies a
    /// delta translation — which is the honest failure mode rather than a guessed speed.
    /// </remarks>
    public void UseAnimations(BoneAnimationSet set)
    {
        ArgumentNullException.ThrowIfNull(set);
        _animator = new HumanLocomotionAnimator(set);
    }

    /// <summary>Gets the cast a mark's corner test probes with, or null when no walls are loaded.</summary>
    /// <remarks>
    /// It has to be the same collision the rounds strike. <c>0x47D920</c> probes each corner a few
    /// millimetres either side of the struck face, so a cast over any other mesh -- the player's
    /// movement level in particular -- misses every corner and drops every mark.
    /// </remarks>
    public WorldRayCast? MarkProbe => _walls?.Caster();

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
    public int LocomotionState => _animator?.State ?? 0;

    /// <summary>Gets the clip the player's legs are actually playing, for the third-person body to mirror.</summary>
    /// <value>
    /// The animation id the current locomotion state names, or
    /// <see cref="HumanLocomotionStates.NoAnimation"/> for the states that name none.
    /// </value>
    /// <remarks>
    /// Asked of the animator rather than looked up again from the state, so there is one answer to the
    /// question of what the player is doing rather than two that can disagree — the body drawn from
    /// outside is the same character the simulation is moving.
    /// </remarks>
    public int LocomotionAnimation => _animator?.AnimationId ?? HumanLocomotionStates.NoAnimation;

    /// <summary>Gets whether the clip <see cref="LocomotionAnimation"/> names repeats.</summary>
    /// <remarks>The state table's mode of one; every movement state carries it, the one-shots do not.</remarks>
    public bool LocomotionAnimationLoops => _animator?.Loops ?? true;

    /// <summary>Makes the level's ladders climbable.</summary>
    /// <param name="ladders">The ladders the scene collected, in world units.</param>
    /// <exception cref="ArgumentNullException"><paramref name="ladders"/> is <see langword="null"/>.</exception>
    public void UseLadders(IReadOnlyList<LadderPlacement> ladders)
    {
        ArgumentNullException.ThrowIfNull(ladders);
        _ladders = ladders;
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
        _gunPickups = pickups;
    }

    /// <summary>Gives the player the level's automatic generic item pickups.</summary>
    public void UseGenericPickups(GenericPickupRegistry pickups)
    {
        ArgumentNullException.ThrowIfNull(pickups);
        _genericPickups = pickups;
    }

    /// <summary>Raised for each weapon collected from the world this tick.</summary>
    public event Action<GunPickup>? GunPickedUp;

    /// <summary>Raised for each authored generic item collected from the world this tick.</summary>
    public event Action<GenericPickup>? GenericPickedUp;

    /// <summary>Raised when a task is pressed or a held interaction completes.</summary>
    public event Action<int>? TaskActivated;

    /// <summary>Raised when the player mounts a stationary gun whose weapon was never bound.</summary>
    public event Action<AiStationaryGun>? UnarmedStationaryGun;

    /// <summary>Raised when a powered electric fence contact passes the retail 45-tick guard.</summary>
    public event Action<WorldInteraction>? ElectrifiedFenceTouched;

    /// <summary>Gets whether a non-ladder interaction currently owns locomotion.</summary>
    public bool IsInWorldInteraction => _activeInteraction is not null;

    /// <summary>Gets the active interaction's retail locomotion state, or -1.</summary>
    public int InteractionLocomotionState => _activeInteraction is null
        ? -1
        : HumanInteractionDispatch.Dispatch(
            _activeInteraction.Type,
            DoorNeedsPicking(_activeInteraction)).LocomotionState;

    /// <summary>Gets where the third-person interaction body is placed.</summary>
    public Vector3d InteractionFeet => _body.Position;

    /// <summary>Gets the interaction body's facing in the engine/model gamma convention.</summary>
    /// <remarks>
    /// The walking camera's yaw is horizontally mirrored relative to IGI's <c>Z(gamma)</c> body frame
    /// (the same conversion <see cref="LadderPlacement.PlayerYaw"/> documents). The animated body and
    /// mode-3 camera consume the original gamma, so feeding them the camera yaw turns the cable pose
    /// the wrong way and leaves the authored hand contact laterally off the wire.
    /// </remarks>
    public float InteractionFacing => _interactionFacing;

    /// <summary>Consumes this tick's authored root motion for a self-driving interaction.</summary>
    /// <param name="rootDelta">The clip's world-space root step this tick.</param>
    /// <param name="clipEnded">Whether a non-looping clip reached its end this tick.</param>
    /// <remarks>
    /// <para>
    /// Fence animation 171 carries Jones across the obstacle in its root track, and the mover (<c>0x4614B0</c>)
    /// applies all three components of the step to the Human's origin -- the height included, which is how the
    /// origin rises over the fence. The body is drawn at that origin with the root's own height taken out
    /// (<see cref="SoldierRenderer.StartPlayerInteractionAnimation"/>), as on a ladder. Keeping the task at its
    /// mount height and drawing the root height on top instead put the whole vault 0.96 m too high: the mount
    /// is already the origin, 3891.2 above the fence's base, and the clip's first root sample is 3938.6.
    /// </para>
    /// <para>
    /// The step moved is the clip's PREVIOUS one. A clip node samples its root and takes the difference
    /// (<c>0x4D4840</c>) before it advances its clock (<c>0x4D4B60</c>), so the origin stands still on the
    /// update that mounts, and the frame on which the clip ends is never moved at all. Measured on mission 1's
    /// fence, retail's origin is the port's of one update before, tick for tick, and its fall starts with the
    /// second-to-last step, not the last.
    /// </para>
    /// </remarks>
    internal void ApplyInteractionRootMotion(Vector3d rootDelta, bool clipEnded = false)
    {
        if (_activeInteraction?.Type != InteractionActivateType.Fence)
        {
            return;
        }

        _body.Position += _fenceRootDelta;
        _fenceAppliedDelta = _fenceRootDelta;
        _fenceRootDelta = rootDelta;
        _fenceClipEnded |= clipEnded;
    }

    /// <summary>Gets whether the active clip loops in the original state initializer.</summary>
    public bool InteractionAnimationLoops => InteractionLocomotionState is 30 or 32 or 35 or 36 or 42 or 43;

    /// <summary>Gets the retail held-interaction progress, or <see langword="null"/> outside a timed state.</summary>
    /// <remarks>
    /// HumanPlayer+3432 is enabled only by states 30, 31, 32, 35 and 36. The status handler divides
    /// the current tick at +3428 by the authored total at +3424; fence, wire and stationary-gun states
    /// deliberately do not display this bar.
    /// </remarks>
    public float? InteractionProgress => _activeInteraction?.Type switch
    {
        InteractionActivateType.Door or InteractionActivateType.Cabinet
            or InteractionActivateType.Terminal or InteractionActivateType.Generic
            or InteractionActivateType.PlaceExplosive when _activeInteraction.DurationTicks > 0 =>
                Math.Clamp(_interactionTick / (float)_activeInteraction.DurationTicks, 0f, 1f),
        _ => null,
    };

    /// <summary>Gets the retail full-body animation selected by the active state.</summary>
    public int InteractionAnimation => _activeInteraction?.Type switch
    {
        InteractionActivateType.Door => 89,
        InteractionActivateType.Cabinet => 84,
        InteractionActivateType.Terminal => 90,
        InteractionActivateType.StationaryGun => 198,
        InteractionActivateType.Fence => HumanLocomotionStates.For(41).AnimationId,
        InteractionActivateType.Wire => Math.Abs(_wireDirection.Z) > Math.Sin(0.034906585)
            ? HumanLocomotionStates.For(43).AnimationId
            : HumanLocomotionStates.For(42).AnimationId,
        InteractionActivateType.Generic or InteractionActivateType.PlaceExplosive =>
            _activeInteraction.ActivateAnimation,
        _ => -1,
    };

    /// <summary>Gets the retail action sprite selected by the current task.</summary>
    public string? ActionSprite => _nearInteraction is { } interaction
        ? DoorNeedsPicking(interaction) ? "action_picklock.spr" : interaction.ActionSprite
        : ShowLadderAction ? StatusScreenLayout.LadderActionSprite : null;

    /// <summary>What the remaster's world-anchored interaction marker should point at.</summary>
    /// <param name="tickBlend">
    /// How far this frame stands between the last two simulation states, so a marker on something that
    /// moves — a swinging door, a button riding a lift — sits where that thing is being drawn rather
    /// than where the tick left it.
    /// </param>
    /// <returns>The offer to mark, or <see langword="null"/> when there is none.</returns>
    /// <remarks>
    /// <para>
    /// The same three offers the retail action sprite is chosen from — the resolved activatable, the
    /// ladder in reach, and the timed state currently running — described as flat data so the prompt
    /// can be laid out and tested without reaching back into the simulation.
    /// </para>
    /// <para>
    /// A running interaction is only offered while it has a progress reading. That is the retail
    /// condition for a progress display (HumanPlayer+3432 is set by states 30, 31, 32, 35 and 36 alone),
    /// and it is the right one here too: a fence vault, a wire ride and a mounted gun are not things the
    /// player is being invited to press a key for while they happen.
    /// </para>
    /// </remarks>
    public InteractionPromptTarget? InteractionPrompt(float tickBlend)
    {
        if (_activeInteraction is { } running)
        {
            return InteractionProgress is { } progress
                ? Describe(running, _body.Eye, tickBlend, progress)
                : null;
        }

        if (_nearInteraction is { } offered)
        {
            return Describe(offered, _body.Eye, tickBlend, progress: null);
        }

        return ShowLadderAction && _nearLadder is { } ladder
            ? DescribeLadder(ladder, _nearLadderAtTop, _nearLadderIndex)
            : null;
    }

    /// <summary>Gets whether the player is on a ladder.</summary>
    public bool IsOnLadder => _climb.IsOnLadder;

    /// <summary>Gets where the climbing body's feet are, in engine units.</summary>
    public Vector3d LadderFeet => _body.Position;

    /// <summary>Gets the yaw the climbing body faces — into the ladder, in radians.</summary>
    public float LadderFacing => _climb.Facing;

    /// <summary>Gets the active retail ladder locomotion phase.</summary>
    public LadderTraversalPhase LadderPhase => _climb.Phase;

    /// <summary>Gets animation 168's current playback direction.</summary>
    public int LadderDirection => _climb.Direction;

    /// <summary>Gets the climb animation — state 37's binding, animation 168 in the player's set.</summary>
    public static int LadderClimbAnimation => HumanLocomotionStates.For(37).AnimationId;

    /// <summary>Gets state 40's slide animation, 169 in the retail player set.</summary>
    public static int LadderSlideAnimation => HumanLocomotionStates.For(40).AnimationId;

    /// <summary>Gets states 38/39's shared top transition animation, 170 in the retail player set.</summary>
    public static int LadderTopAnimation => HumanLocomotionStates.For(38).AnimationId;

    /// <summary>Gets whether a ladder is in reach and its retail action icon should be shown.</summary>
    public bool ShowLadderAction => _nearLadder is not null && !_climb.IsOnLadder;

    /// <summary>Puts the player at a spawn the level declared.</summary>
    /// <param name="terrain">The level's terrain, or <see langword="null"/> for an object-only interior.</param>
    /// <param name="spawn">The level's own <c>HumanPlayer</c> start.</param>
    /// <param name="registrationGround">
    /// Optional object collision view that includes the gameplay container before an opening cutscene
    /// registers it. Ordinary movement continues to use the live collision supplied by <see cref="UseWalls"/>.
    /// </param>
    /// <returns><see langword="false"/> when there is no ground under it.</returns>
    /// <remarks>
    /// A spawn's position is already in the game's world units — unlike a scene placement, which the
    /// scene builder has divided into metres for the renderer — so it goes to the body untouched.
    /// </remarks>
    public bool PlaceAt(IGroundHeightField? terrain, PlayerSpawnPoint spawn, WorldRayCast? registrationGround = null)
    {
        _body.Position = spawn.Position;
        // HumanPlayer's objects.qvm Gamma is the engine object's facing angle. The camera stores a
        // world-to-view yaw, whose horizontal convention is its inverse; this is the same conversion
        // used when authored interaction poses hand the player's facing back to the camera.
        _body.Yaw = -spawn.Gamma;
        _body.Pitch = 0f;
        HasViewTickPair = false;
        _landingKick.Reset();
        _viewShake.Reset();

        WorldRayCast? liveWalls = _body.Walls;
        if (registrationGround is not null)
        {
            _body.Walls = registrationGround;
        }

        try
        {
            return _body.PlaceOnGround(terrain);
        }
        finally
        {
            _body.Walls = liveWalls;
        }
    }

    private static Vector3d ToMetres(Vector3d units) =>
        new(units.X / UnitsPerMetre, units.Y / UnitsPerMetre, units.Z / UnitsPerMetre);

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

        MouseState mouse = input.Mouse;

        // A first-person view looks wherever the mouse goes - no button held. That is the difference
        // between standing in a level and inspecting one, and holding right mouse to turn is a scene
        // view's idiom that FreeCamera keeps and this must not.
        IsCapturingMouse = true;

        // The first frame is still skipped: capturing the cursor warps it, and that warp arrives as a
        // movement the player never made.
        bool settling = !_wasCapturing;
        _wasCapturing = true;

        if (!settling)
        {
            // Mouse right turns right. The camera's forward is (sin yaw, cos yaw), so turning right -
            // from +Y toward +X - means yaw increases. Both deltas are in mouse COUNTS, not pixels: the
            // cursor is locked in raw motion mode, so no pointer ballistics stand between the sensor and
            // this line. PitchDelta carries the profile's invert, so it is added rather than subtracted.
            //
            // The look is slowed in step with any optical zoom: at the Dragunov's tightest scope a mouse
            // count moves the crosshair across a magnified field, so without this the aim gets harder the
            // more you zoom in. The scale is the zoom ratio and is 1 (no change) whenever the view is not
            // zoomed. See MouseLookSettings.ZoomSensitivityScale.
            // On a stand the look turns the gun, not the head: 0x474190 adds the occupant's two look
            // deltas to the gun's own elevation and traverse, and state 33 locks the view's pitch
            // (0x464700). Mouse right turns right, which is a clockwise traverse.
            if (MountedStationaryGun is { } mounted)
            {
                mounted.Steer(
                    LookSettings.PitchDelta(mouse.DeltaY),
                    -LookSettings.YawDelta(mouse.DeltaX));
                FaceAlongGun(mounted);
                SyncView();
                return;
            }

            float zoomLook = _view is { } zoomView
                ? MouseLookSettings.ZoomSensitivityScale(zoomView.TanX)
                : 1f;
            _body.Yaw += LookSettings.YawDelta(mouse.DeltaX) * zoomLook;
            _body.Pitch = Math.Clamp(
                _body.Pitch + (LookSettings.PitchDelta(mouse.DeltaY) * zoomLook),
                -PlayerBody.PitchLimit,
                PlayerBody.PitchLimit);
        }

        // The rig follows the look every frame, not every tick: the weapon is attached to the view and
        // has to stay welded to it however fast the frames come.
        SyncView();
    }

    /// <summary>Advances the body by one simulation tick.</summary>
    /// <param name="input">The window's input.</param>
    /// <param name="terrain">The level's terrain.</param>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// The order is <c>HumanPlayer_Update</c>'s, and it is load-bearing in two places. The weapon
    /// machine runs <b>before</b> the rig's clips advance, so it sees the clip-end flag as of the last
    /// advance — which is what makes "the animation has finished" a one-tick-late signal in the original
    /// too. And the trigger message goes to the gun <b>after</b> the rig has been placed, because the
    /// muzzle is this tick's eye.
    /// </para>
    /// </remarks>
    public void Tick(InputState input, IGroundHeightField? terrain)
    {
        ArgumentNullException.ThrowIfNull(input);

        ApplyCheats(PlayerCheatOptions.Current);

        _input.Accept(SampleFrame(input, InputProfile));
        if (_input.IsRisingEdge(PlayerInputSlot.ToggleWalkRun))
        {
            _running = !_running;
        }
        _interactions?.Tick();

        // On a ladder the ordinary walk, gravity and collision do not run. The rung-by-rung climb (state
        // 37) is driven by the host once per tick through DecideLadderStep and the body clip, because
        // it is coupled to the third-person body's animation; nothing to do here.
        if (_climb.IsOnLadder)
        {
            return;
        }

        if (_activeInteraction is not null)
        {
            InteractionActivateType type = _activeInteraction.Type;
            TickInteraction(input, terrain);

            // A finished fence vault enters state 29 inside HumanPlayer_Update's state machine (0x4109A0), and
            // the same update's mover then makes the fall's first move. Every other interaction stays put
            // for the tick it ends on, as before.
            if (_activeInteraction is not null || type != InteractionActivateType.Fence)
            {
                return;
            }
        }

        // Which ladder, if any, is in reach — for the action icon and for mounting.
        UpdateNearLadder();
        double interactionAngle = double.MaxValue;
        _nearInteraction = _interactions?.Resolve(
            _body.Eye, _body.Yaw, _body.Pitch, out interactionAngle);
        if (_nearLadder is not null && _nearLadderAngle <= interactionAngle)
        {
            _nearInteraction = null;
        }
        else if (_nearInteraction is not null)
        {
            _nearLadder = null;
        }

        // The use key mounts the ladder in reach. Taken on the rising edge so holding it does not remount
        // the instant the climb ends.
        if (_input.IsRisingEdge(PlayerInputSlot.Activate) && _nearInteraction is not null)
        {
            Activate(_nearInteraction, terrain);
            return;
        }

        if (_nearLadder is not null && _input.IsRisingEdge(PlayerInputSlot.Activate))
        {
            MountLadder();
            return;
        }

        // The action names are the game's own, from the binding table at 0x536558: MoveUp and
        // MoveDown are forward and back - named for the screen, as the era's binding dialogs were.
        float forward = Axis(_input, PlayerInputSlot.MoveUp, PlayerInputSlot.MoveDown);
        float strafe = Axis(_input, PlayerInputSlot.MoveRight, PlayerInputSlot.MoveLeft);
        bool crouch = _input.IsSet(PlayerInputSlot.Crouch);

        // Jump is the rising edge, not the level, so holding it does not bounce.
        // The player cannot jump while inside an elevator cab — the lift floor is not a surface
        // the jump integrator expects, and retail does not allow it.
        bool jump = _input.IsRisingEdge(PlayerInputSlot.Jump) && (CanJump?.Invoke() ?? true);

        PlayerCommand command = new(forward, strafe, 0f, 0f, _running, crouch, jump);

        Vector3d previousPosition = _body.Position;

        // The weapon machine, before the clips advance: 0x4EC1E0 over the weapon table runs in
        // HumanPlayer_Update ahead of 0x464800, so its clip-end flag is last tick's.
        TickWeapons();

        if (_animator is null)
        {
            // No animation set, so nothing can say how far a step covers. Standing and falling still
            // work; walking does not, which is better than inventing a speed.
            _body.Tick(command, terrain, default);
        }
        else
        {
            _body.TickAnimated(command, terrain, _animator);
        }

        WorldInteraction? electricFence = _interactions?.FindElectrifiedFenceContact(
            previousPosition,
            _body.Position);
        if (ElectricFenceContact.Advance(ref _electricFenceCooldown, electricFence) is { } touchedFence)
        {
            ElectrifiedFenceTouched?.Invoke(touchedFence);
        }

        // Diagnostic: name whatever the wall sweep stopped the body against, so a doorway you cannot walk
        // through can be traced to the exact model and task blocking it. Only while trying to move.
        LogWallBlockIfAny(forward, strafe);

        if (_weapons is not null && _gunPickups is not null)
        {
            foreach (GunPickup pickup in _gunPickups.CollectAt(_body.Position, _weapons))
            {
                GunPickedUp?.Invoke(pickup);
            }
        }

        if (_genericPickups is { } genericPickups)
        {
            foreach (GenericPickup pickup in genericPickups.CollectAt(_body.Position))
            {
                GenericPickedUp?.Invoke(pickup);
            }
        }

        _view?.Tick();

        // Retail probes the normal placed-object collision level and terrain from the eye, not the
        // enlarged collision LOD used to keep the player's body out of narrow gaps. This affects only
        // the first-person rig: the camera and the fired round still begin at the true eye.
        SyncView();
        if (_view is { } view)
        {
            view.TickWallPullback(ViewModelWalls());
        }

        // Where 0x464BD0 steps it: after the state machines have read it and before the rig is placed.
        _weapons?.Sway.Advance();

        // The hit shake and the landing dip are stepped by the same function, in the same place, and for
        // the same reason: they displace the camera this tick places rather than the body the state
        // machines just moved. The shake comes first (0x464BD0).
        _viewShake.Advance(_random.NextDouble);
        _landingKick.Advance();

        PoseChanged = true;
        SyncView();

        // 0x462E50, which HumanPlayer_Update calls after 0x464BD0 has committed the view.
        ResolveFire();

        // After the fire, so a round spawned this tick flies its first segment in the same tick --
        // otherwise every shot would carry a one-tick delay on top of its real travel time.
        TickBallistics();

        // After the fire has (possibly) started a new clip, so the pair describes what this tick
        // actually shows.
        CaptureViewTickState();
    }

    /// <summary>
    /// Advances only the presentation while the map computer is open. The player remains grounded and
    /// motionless, but the first-person rig keeps advancing exactly as the retail state-45 handler does.
    /// </summary>
    public void TickMapComputer()
    {
        if (!_mapComputerOpen)
        {
            return;
        }

        // The console stays usable over the map, so a cheat can change while it is open.
        ApplyCheats(PlayerCheatOptions.Current);

        // Map controls are consumed by ComputerMap, not HumanPlayer. Feeding an empty frame prevents a
        // held movement/fire key from leaking into the dormant weapon machine while preserving its clock.
        _input.Accept(default);
        _animator?.Enter(45);
        TickWeapons();
        _view?.Tick();
        _weapons?.Sway.Advance();

        // 0x464BD0 runs whatever the locomotion state is, so a dip that began before the computer was
        // opened keeps playing out rather than freezing the view part-way down.
        _viewShake.Advance(_random.NextDouble);
        _landingKick.Advance();
        PoseChanged = true;
        SyncView();
        CaptureViewTickState();
    }

    /// <summary>Stops the held-weapon machine because the player has died.</summary>
    /// <remarks>
    /// Death stops ordinary player ticks immediately, so this cannot wait for the next input frame to
    /// release a held trigger.
    /// </remarks>
    public void StopWeaponsForDeath()
    {
        // A player killed on a stand lets go of it, or the gun stays occupied and firing.
        if (_activeInteraction?.Type == InteractionActivateType.StationaryGun)
        {
            EndInteraction(completed: false, terrain: null);
        }

        _weapons?.StopForDeath();
    }

    private void Activate(WorldInteraction interaction, IGroundHeightField? terrain)
    {
        InteractionDispatchResult dispatch = HumanInteractionDispatch.Dispatch(
            interaction.Type, DoorNeedsPicking(interaction));
        switch (dispatch.Action)
        {
            case InteractionAction.PressSwitch:
                interaction.Press();
                TaskActivated?.Invoke(interaction.Task.Id);
                break;
            case InteractionAction.OpenDoor:
                interaction.Door?.Toggle();
                AiNoise?.Invoke(AiEvent.Door);
                TaskActivated?.Invoke(interaction.Task.Id);
                break;
            case InteractionAction.MountWire:
                BeginWire(interaction);
                return;
            case InteractionAction.ClimbFence:
            case InteractionAction.PickLock:
            case InteractionAction.EnterState:
                if (interaction.Type == InteractionActivateType.StationaryGun && !interaction.BeginUse())
                {
                    _nearInteraction = null;
                    return;
                }

                _activeInteraction = interaction;
                _interactionTick = 0;
                _interactionUseArmed = false;
                double standingHeight = _body.Position.Z;
                (Vector3d position, float facing) = interaction.BodyPose(_body.Position);
                if (interaction.Type == InteractionActivateType.PlaceExplosive)
                {
                    // State 36 places from the eye, not the feet: it casts to the target, backs the body off the
                    // contact and moves the charge model there, all on entry.
                    ChargePlantPose plant = interaction.PlantPose(_body.Eye, PlantTrace());
                    interaction.PlaceTask(plant.Contact, plant.Facing);
                    (position, facing) = (plant.Position, plant.Facing);
                }

                _body.Position = position;
                SnapBodyForInteraction(_body, interaction.Type, terrain, standingHeight);
                _interactionFacing = facing;
                _body.Yaw = -facing;
                _fenceRootDelta = default;
                _fenceAppliedDelta = default;
                _fenceClipEnded = false;
                if (interaction.Type != InteractionActivateType.StationaryGun)
                {
                    interaction.BeginUse();
                }
                else if (interaction.StationaryGun is { Weapon: null } unarmed)
                {
                    // The stand is mounted but has no gun to fire: its WEAPON_ID_* never resolved against
                    // the roster (SoldierRenderer.Arm binds it). Say so, rather than leave the player in
                    // the interaction orbit wondering why nothing fires.
                    UnarmedStationaryGun?.Invoke(unarmed);
                }
                else if (interaction.StationaryGun is { Weapon: { } gunWeapon } gun)
                {
                    // State 33 puts the carried weapon away (0x4137E0 with none) and the stand's own
                    // gun takes the trigger. An emplacement gun names no ammunition record, so it
                    // never spends from a magazine; the stand counts its own rounds.
                    _weapons?.ClearFiredThisTick();
                    _mountedGun = new HeldGun(gunWeapon);
                    gun.TickOccupied();
                    FaceAlongGun(gun);
                    HasViewTickPair = false;
                    SyncView();
                }
                if (interaction.Type == InteractionActivateType.Fence && interaction.IsElectrified)
                {
                    if (ElectricFenceContact.TryShock(ref _electricFenceCooldown, interaction) is { } touchedFence)
                    {
                        ElectrifiedFenceTouched?.Invoke(touchedFence);
                    }
                }
                break;
        }

        _nearInteraction = null;
    }

    internal static void SnapBodyForInteraction(
        PlayerBody body,
        InteractionActivateType type,
        IGroundHeightField? terrain,
        double? standingHeight = null)
    {
        if (type is InteractionActivateType.Door
            or InteractionActivateType.Cabinet
            or InteractionActivateType.Terminal
            or InteractionActivateType.Generic
            or InteractionActivateType.PlaceExplosive)
        {
            if (standingHeight is { } floorReference)
            {
                // The authored interaction root is relative to the task and can sit above the floor
                // (Terminal_333's root is at keyboard height). Start Human_SnapToGround from the
                // human's standing height so collision furniture above their feet cannot become the
                // new floor. The 0.4 m probe still admits ordinary floor variation at the new X/Y.
                body.Position = new Vector3d(body.Position.X, body.Position.Y, floorReference);
            }

            // IGI2 HumanPlayer_InitStateActivate* names this sequence explicitly: Human_PopPosition,
            // then Human_SnapToGround. IGI1 has the same pair at sub_4631A0/sub_463260.
            body.SnapInteractionToGround(terrain);
        }
    }

    private void BeginWire(WorldInteraction wire)
    {
        Vector3d segment = wire.End - wire.Anchor;
        if (segment.LengthSquared == 0.0)
        {
            return;
        }

        // sub_411F20 projects onto the wire but never mounts on a raw endpoint: both ends reserve
        // exactly 3072 units. Mounting on the endpoint made the next state-42/43 tick read "at end"
        // and immediately hand the body back to the ground mover.
        Vector3d bodyPoint = wire.WireBodyPoint(_body.Eye);
        _wireDirection = segment.Normalized();
        if (_wireDirection.Z > 0.0)
        {
            _wireDirection = -_wireDirection;
        }

        _wireSpeed = 0.0;
        _body.BeginLadderTraversal();
        _body.Position = bodyPoint;
        _body.Yaw = (float)Math.Atan2(_wireDirection.X, _wireDirection.Y);
        _interactionFacing = -_body.Yaw;
        _activeInteraction = wire;
        _interactionTick = 0;
        _interactionUseArmed = false;
        wire.BeginUse();
        _nearInteraction = null;
    }

    private void TickInteraction(InputState input, IGroundHeightField? terrain)
    {
        WorldInteraction interaction = _activeInteraction!;
        bool use = _input.IsSet(PlayerInputSlot.Activate);
        if (!use)
        {
            _interactionUseArmed = true;
        }

        if (interaction.Type == InteractionActivateType.StationaryGun)
        {
            // 0x40CAE0: a fresh Use press leaves the gun.
            if (use && _interactionUseArmed)
            {
                EndInteraction(completed: false, terrain);
                return;
            }

            TickMountedGun();
            return;
        }

        if (interaction.Type == InteractionActivateType.Wire)
        {
            TickWire(terrain, use);
            return;
        }

        // All timed retail states are cancelled as soon as Use is released. Fence climbing is a
        // committed traversal, so it is the sole exception.
        if (!use && interaction.Type != InteractionActivateType.Fence)
        {
            EndInteraction(completed: false, terrain);
            return;
        }

        _interactionTick++;
        if (interaction.Type == InteractionActivateType.Fence)
        {
            TickFence(interaction, terrain);
            return;
        }

        if (_interactionTick >= interaction.DurationTicks)
        {
            EndInteraction(completed: true, terrain);
        }
    }

    private void TickWire(IGroundHeightField? terrain, bool use)
    {
        WorldInteraction wire = _activeInteraction!;
        bool sliding = Math.Abs(_wireDirection.Z) > Math.Sin(0.034906585);
        if (sliding)
        {
            _wireSpeed = (-_wireDirection.Z * 88.309761) + (_wireSpeed * 0.99000001);
            _body.Position += _wireDirection * _wireSpeed;
        }
        else
        {
            float along = Axis(_input, PlayerInputSlot.MoveUp, PlayerInputSlot.MoveDown);
            _body.Position += _wireDirection * (along * 512.0);
        }

        Vector3d segment = wire.End - wire.Anchor;
        // The body-to-cable offset is perpendicular to the cable, so projecting the body itself gives
        // the same along-wire coordinate. Adding world Z here is wrong for a sloped wire frame.
        double coordinate = Vector3d.Dot(_body.Position - wire.Anchor, segment) / segment.LengthSquared;
        double margin = Math.Min(0.49, 3072.0 / segment.Length);
        if ((use && _interactionUseArmed) || coordinate < margin || coordinate > 1.0 - margin)
        {
            EndInteraction(completed: false, terrain);
        }
    }

    private void TickFence(WorldInteraction fence, IGroundHeightField? terrain)
    {
        // State 41 plants the task root at the authored fence mount and lets animation 171 carry it over.
        // It leaves on the clip's end bit (0x40DD00); the six-second duration accessor (0x440270), the clip's
        // own length, only bounds a climb whose clip never reports. At the end the origin is wherever the clip
        // left it -- 0.94 m up, on the far side -- and state 29 drops it from there with the last step moved as
        // its velocity, rather than snapping to a landing point.
        if (!_fenceClipEnded && _interactionTick < fence.DurationTicks)
        {
            AiNoise?.Invoke(AiEvent.Fence);
        }
        else
        {
            _body.BeginFallFromVault(_body.Position, _fenceAppliedDelta);
            EndInteraction(completed: false, terrain);
        }
    }

    /// <summary>One tick of the gun the player is on: its kick, its trigger and its rounds.</summary>
    /// <remarks>
    /// <para>
    /// <c>0x474190</c> with an occupant: the barrel is shown at its aim plus the last kick, then the
    /// stand sends its gun the occupant's fire input (<c>0x474360</c>) and keeps the kick it answers.
    /// </para>
    /// <para>
    /// The message carries <b>no origin and no orientation</b> — both are null, where the carried-gun
    /// path (<c>0x462E50</c>) passes the eye. So <c>0x478BA0</c> falls back on the gun's own frame: the
    /// round leaves the gun model's magic vertex 2 along the gun's barrel, not the player's eye.
    /// </para>
    /// </remarks>
    private void TickMountedGun()
    {
        _impacts.Clear();
        if (_mountedGun is null || MountedStationaryGun is not { } gun)
        {
            TickBallistics();
            return;
        }

        gun.TickOccupied();
        FaceAlongGun(gun);

        bool trigger = _input.IsSet(PlayerInputSlot.Fire)
            && (gun.Ammunition < 0 || gun.RemainingAmmunition > 0);
        _mountedGun.Tick(trigger);

        Matrix3f m = gun.GunOrientation;
        Vector3f right = new(m.M11, m.M21, m.M31);
        Vector3f forward = new(m.M12, m.M22, m.M32);
        Vector3f up = new(m.M13, m.M23, m.M33);
        GunFireResult result = _mountedGun.Fire(trigger, gun.MuzzlePosition, forward, right, up, _random);
        if (result.Fired)
        {
            gun.ConsumeRound();
        }

        gun.RecordShot(result.Fired, result.RecoilX, result.RecoilZ);
        if (result.Fired)
        {
            ResolveMountedRounds(result.Projectiles, _mountedGun.Weapon);
        }

        TickBallistics();
        SyncView();
    }

    // The rounds of a mounted gun, down the same fork as a carried one: flown with g_ballistics on and
    // a weapon a profile accepts, else the retail ray. The muzzle is the gun's own, so a streak needs no
    // offset to be seen leaving it.
    private void ResolveMountedRounds(IReadOnlyList<GunProjectile> shots, WeaponType weapon)
    {
        bool simulate = GameplayCVars.Ballistics.Value && BallisticProfile.TryCreate(weapon, null) is not null;
        foreach (GunProjectile shot in shots)
        {
            if (simulate)
            {
                _ballistics.Spawn(
                    shot,
                    weapon,
                    null,
                    weapon.DamageFactor,
                    weapon.Category,
                    default,
                    GameplayCVars.BallisticsMinimumFlight.Value);
            }
            else
            {
                ResolveShot(shot, weapon.DamageFactor, weapon.Category, weapon.PenetrationPower);
            }
        }
    }

    // Keeps the body facing down the barrel, so the compass reads true on the stand and the player
    // steps off looking where the gun was.
    private void FaceAlongGun(AiStationaryGun gun)
    {
        Matrix3f m = gun.GunOrientation;
        _body.Yaw = MathF.Atan2(m.M12, m.M22);
        _body.Pitch = Math.Clamp(
            MathF.Asin(Math.Clamp(m.M32, -1f, 1f)),
            -PlayerBody.PitchLimit,
            PlayerBody.PitchLimit);
    }

    private void EndInteraction(bool completed, IGroundHeightField? terrain)
    {
        WorldInteraction interaction = _activeInteraction!;
        if (completed)
        {
            interaction.CompleteUse();
            if (interaction.Door is not null)
            {
                // A finished pick unlocks and toggles the door, and posts the same noise (0x40C160).
                AiNoise?.Invoke(AiEvent.Door);
            }

            TaskActivated?.Invoke(interaction.Task.Id);
        }
        else
        {
            interaction.CancelUse();
        }

        if (_mountedGun is not null)
        {
            // 0x40CAE0 hands the view back (0x464710) and the carried weapon returns with it.
            _mountedGun = null;
            HasViewTickPair = false;
        }

        _activeInteraction = null;
        _interactionTick = 0;
        _interactionFacing = -_body.Yaw;
        RestoreBodyAfterInteraction(_body, interaction.Type, terrain);
        PoseChanged = true;
        SyncView();
    }

    internal static void RestoreBodyAfterInteraction(
        PlayerBody body,
        InteractionActivateType type,
        IGroundHeightField? terrain)
    {
        if (type == InteractionActivateType.Wire)
        {
            // Wire cleanup enters the ordinary fall state. Snapping to the terrain here is what made a
            // release/end look like an instant teleport to ground instead of a drop from the cable.
            body.EndLadderTraversal(terrain, placeOnGround: false);
        }
        // A fence ends in the fall it has already been handed (PlayerBody.BeginFallFromVault), so it, too, is left
        // where it is.
        // Retail self-driving interaction states leave the root exactly where their initializer put it.
        // Re-running spawn placement here searches up to half a kilometre downward and can move the
        // player onto an unrelated surface beneath the building when Use is released.
    }

    private static bool DoorNeedsPicking(WorldInteraction interaction)
    {
        return interaction.Type == InteractionActivateType.Door && interaction.IsLocked;
    }

    private static InteractionPromptTarget Describe(
        WorldInteraction interaction,
        Vector3d eye,
        float tickBlend,
        float? progress) =>
        new(
            interaction.Task.Id,
            interaction.Type,
            interaction.ActionSprite,
            interaction.MarkerPoint(eye, tickBlend),
            interaction.DurationTicks,
            DoorNeedsPicking(interaction),

            // A door that is not fully closed is offered as one to close, because that is what the
            // toggle this prompt is describing will do to it.
            interaction.Door is { IsFullyClosed: false },
            progress);

    private static InteractionPromptTarget DescribeLadder(LadderPlacement ladder, bool atTop, int index) =>
        new(
            // Negative, so a ladder's identity can never collide with a declared task's id.
            -(index + 1),
            InteractionActivateType.Ladder,
            StatusScreenLayout.LadderActionSprite,
            LadderMarker(ladder, atTop),
            DurationTicks: 0,
            Locked: false,
            DoorOpen: false,
            Progress: null);

    /// <summary>The rung a ladder's marker sits on: the one the player is about to take hold of.</summary>
    /// <remarks>
    /// Fixed to the end being mounted rather than tracking the player up and down the stile. A ladder
    /// is metres long and a marker in the middle of one marks nothing, but a marker that slides as the
    /// player walks belongs to the player rather than to the ladder.
    /// </remarks>
    private static Vector3d LadderMarker(LadderPlacement ladder, bool atTop)
    {
        if (atTop)
        {
            return ladder.Top;
        }

        Vector3d climb = ladder.Top - ladder.Bottom;
        double length = climb.Length;
        return length <= 0.0
            ? ladder.Bottom
            : ladder.Bottom + (climb / length * Math.Min(length, LadderGrabHeightUnits));
    }

    /// <summary>How far up a ladder a climber takes hold — chest height, in world units.</summary>
    private const double LadderGrabHeightUnits = 1.25 * WorldScene.UnitsPerMetre;

    private int _upperBodyAnimation = -1;
    private bool _upperBodyLoops;
    private float _upperBodyBlendSeconds;
    private int _upperBodySequence;

    /// <summary>Gets the weapon clip the player's TORSO is playing, or <c>-1</c> when it has none.</summary>
    /// <value>An animation id in the human's own skeleton-0 set, not the first-person rig's.</value>
    /// <remarks>
    /// <para>
    /// The other half of every weapon action. <c>0x4137E0</c> picks a pair — a full-body clip for the
    /// first-person rig and an <c>UpperBody*</c> clip for the human's own body — and plays them on two
    /// different skeletons. Only the first is visible through the player's own eyes, which is why the
    /// second went unused here until there was a camera that could see the body.
    /// </para>
    /// <para>
    /// Reported rather than played, because the body belongs to the renderer: this object owns the
    /// simulation's copy of what the torso was asked to do, and <see cref="UpperBodySequence"/> is how a
    /// consumer notices a new request without having to diff the id — a second shot plays the same clip
    /// again and must restart it.
    /// </para>
    /// </remarks>
    public int UpperBodyAnimation => _upperBodyAnimation;

    /// <summary>Gets whether <see cref="UpperBodyAnimation"/> repeats.</summary>
    public bool UpperBodyAnimationLoops => _upperBodyLoops;

    /// <summary>Gets how long the torso clip eases in — 0.4 s for stances, immediate for a shot.</summary>
    public float UpperBodyBlendSeconds => _upperBodyBlendSeconds;

    /// <summary>Gets a counter bumped every time a new torso clip is requested.</summary>
    public int UpperBodySequence => _upperBodySequence;

    /// <summary>Gets whether the rig has two tick states for the presentation to draw between.</summary>
    public bool HasViewTickPair { get; private set; }

    /// <summary>Records the rig's per-tick halves, keeping the tick before's.</summary>
    private void CaptureViewTickState()
    {
        if (_view is not { } view)
        {
            HasViewTickPair = false;
            return;
        }

        // The dip belongs to the camera the frame is drawn from, so it is folded in here rather than at
        // the eye the body reports: the blend between the two ticks then carries it for free, and the
        // listener and the rig stay on the same head they always were.
        Vector3d eye = view.Eye + LandingKickOffsetUnits + ViewShakeOffsetUnits;
        _prevTickEye = HasViewTickPair ? _tickEye : eye;
        _tickEye = eye;

        // The shake's angles step on the tick too, and sit in both the camera and the rig.
        _prevTickShake = HasViewTickPair ? _tickShake : (_viewShake.Pitch, _viewShake.Roll, _viewShake.Yaw);
        _tickShake = (_viewShake.Pitch, _viewShake.Roll, _viewShake.Yaw);

        double wallPullback = view.WallPullbackInUnits;
        _prevTickWallPullback = HasViewTickPair ? _tickWallPullback : wallPullback;
        _tickWallPullback = wallPullback;

        Vector3f rootMotion = view.RootMotion;
        _prevTickRootMotion = HasViewTickPair ? _tickRootMotion : rootMotion;
        _tickRootMotion = rootMotion;

        // The sway IS the weapon-change animation — no clip plays; two angles ease over eleven ticks
        // (see HumanViewSway). They step on the tick, so they interpolate like everything else here.
        float swayPitch = _weapons?.Sway.Pitch ?? 0f;
        float swayYaw = _weapons?.Sway.Yaw ?? 0f;
        _prevTickSwayPitch = HasViewTickPair ? _tickSwayPitch : swayPitch;
        _prevTickSwayYaw = HasViewTickPair ? _tickSwayYaw : swayYaw;
        _tickSwayPitch = swayPitch;
        _tickSwayYaw = swayYaw;

        // The view kick accumulates and decays on the tick (ResolveFire ran just above), and it sits
        // in both the camera and the rig's eye orientation -- so both take the same blended value, or
        // the weapon would shear against the view with every shot.
        float recoilPitch = _weapons?.RecoilPitch ?? 0f;
        _prevTickRecoilPitch = HasViewTickPair ? _tickRecoilPitch : recoilPitch;
        _tickRecoilPitch = recoilPitch;

        // GunMP5SD changes both FOV tangents on the fixed tick. Preserve that exact retail state
        // transition, but keep the pair so an uncapped presentation can draw the space between the
        // two values instead of exposing the original game's 30 Hz steps.
        _prevTickTanX = HasViewTickPair ? _tickTanX : view.TanX;
        _prevTickViewModelTanX = HasViewTickPair ? _tickViewModelTanX : view.ViewModelTanX;
        _tickTanX = view.TanX;
        _tickViewModelTanX = view.ViewModelTanX;

        // The weapon bone out of the UNROTATED pose — the identity-root one — so the look can be
        // applied per frame at draw. Equivalent by the same argument the third-person draw leans on:
        // the root rotation only ever left-multiplies every bone.
        BonePose unrotated = view.Bones.Pose();
        _weaponBone.Capture(unrotated, HumanAttachPoints.FirstPersonWeapon);

        // The magazine and the moving part ride bones of their own, and a reload clip moves all three
        // independently -- so all three need the pair an uncapped presentation interpolates between, or
        // a slide would step at 30 Hz against a gun body that glides.
        _magazineBone.Capture(unrotated, HumanAttachPoints.FirstPersonSecondary);
        _movingPartBone.Capture(unrotated, HumanAttachPoints.FirstPersonTertiary);

        HasViewTickPair = true;
    }

    /// <summary>The drawn pitch with the view kick blended between its last two tick values.</summary>
    /// <param name="alpha">How far the frame sits between the last two ticks, 0 to 1.</param>
    /// <remarks>
    /// The mouse half (<see cref="PlayerBody.Pitch"/>) is per frame already; only the recoil
    /// accumulates and decays on the tick. At <paramref name="alpha"/> 1 this is
    /// <see cref="AimPitch"/> exactly.
    /// </remarks>
    private float SmoothedAimPitch(float alpha) => HasViewTickPair
        ? _body.Pitch + _prevTickRecoilPitch + ((_tickRecoilPitch - _prevTickRecoilPitch) * alpha)
        : AimPitch;

    /// <summary>The camera's orientation for a frame between two ticks — the kick glides, the look is now.</summary>
    /// <param name="alpha">How far the frame sits between the last two ticks, 0 to 1.</param>
    /// <remarks>
    /// The rig takes the same blended pitch through <see cref="SmoothedRig"/>, so the weapon never
    /// shears against the view mid-kick.
    /// </remarks>
    public Matrix3f SmoothedLookRotation(float alpha)
    {
        (float pitch, float roll, float yaw) = SmoothedShake(alpha);
        return Shaken(_body.Yaw, SmoothedAimPitch(alpha), pitch, roll, yaw);
    }

    private (float Pitch, float Roll, float Yaw) _prevTickShake;
    private (float Pitch, float Roll, float Yaw) _tickShake;

    // The shake's three angles blended between their last two tick values.
    private (float Pitch, float Roll, float Yaw) SmoothedShake(float alpha) => HasViewTickPair
        ? (PresentationBlend.LerpAngle(_prevTickShake.Pitch, _tickShake.Pitch, alpha),
            PresentationBlend.LerpAngle(_prevTickShake.Roll, _tickShake.Roll, alpha),
            PresentationBlend.LerpAngle(_prevTickShake.Yaw, _tickShake.Yaw, alpha))
        : (_viewShake.Pitch, _viewShake.Roll, _viewShake.Yaw);

    /// <summary>Where the ear is for a frame between two ticks, in metres.</summary>
    /// <param name="alpha">How far the frame sits between the last two ticks, 0 to 1.</param>
    /// <returns>The blended eye position in metres.</returns>
    /// <remarks>
    /// The eye rather than <see cref="Position"/>, which is the feet: the listener belongs in the head,
    /// and blended from the same tick pair the view uses so a source does not step across the stereo
    /// image at 30 Hz under a camera that glides.
    /// </remarks>
    public Vector3d SmoothedEyeMetres(float alpha) => ToMetres(
        HasViewTickPair ? PresentationBlend.Lerp(_prevTickEye, _tickEye, alpha) : _body.Eye);

    /// <summary>Gets the complete first-person camera at one shared between-tick presentation point.</summary>
    public Camera3D SmoothedCamera(float alpha) =>
        new(SmoothedEyeMetres(alpha), SmoothedLookRotation(alpha));

    /// <summary>Gets or sets the ray the crosshair sits on — the drawn camera's own centre line.</summary>
    /// <value>
    /// The camera's position in engine units and its unit forward, or <see langword="null"/> — the
    /// default, and the only value first person ever holds — to fire straight along the eye's own
    /// forward, exactly as the original does.
    /// </value>
    /// <remarks>
    /// <para>
    /// <b>The one thing the third-person view changes about the game rather than about the picture.</b>
    /// A camera behind and to the side of the eye does not share its sight line, so a fixed crosshair
    /// and a shot along the body's forward cannot both be right. Rather than let the crosshair wander to
    /// wherever the round would land — truthful, and unusable — the shot converges on what the middle of
    /// the screen is over.
    /// </para>
    /// <para>
    /// <b>A ray rather than a point, and this is the whole of the fix.</b> Handing in a point meant the
    /// renderer decided what the crosshair was over, and it answered with a different test from the one
    /// the bullet uses — it saw walls and terrain and looked straight through people. So a shot aimed at
    /// a guard converged on the hillside behind him and landed half a metre wide, which is exactly the
    /// distance between the camera and the eye. Resolving the ray HERE settles it against
    /// <see cref="HumanProbes"/> and the same <c>WorldTraceMask.World</c> trace <see cref="ResolveShot"/>
    /// marches, so the crosshair's target and the round's target are found by one query and cannot
    /// disagree.
    /// </para>
    /// <para>
    /// The round still <b>leaves the eye</b>: only its direction is turned, which keeps the muzzle, the
    /// tracer and the wall probe where they were. And the turn is the identity whenever the camera is at
    /// the eye, so first person needs no flag to be unaffected.
    /// </para>
    /// </remarks>
    public (Vector3d OriginUnits, Vector3f Direction)? AimConvergenceRay { get; set; }

    /// <summary>How far the convergence probe reaches, in engine units — 300 m.</summary>
    /// <remarks>
    /// Past this the angle between a shot from the eye and one from a camera 2.6 m behind it is a
    /// fraction of a degree. A ray that reaches the end unobstructed converges on the end, which is the
    /// same answer as not converging at all.
    /// </remarks>
    public const double AimConvergenceReachInUnits = 300.0 * UnitsPerMetre;

    /// <summary>Finds what the crosshair is over, with the tests a round would meet on the way.</summary>
    /// <returns>The world point, in engine units, or <see langword="null"/> when nothing is aimed.</returns>
    /// <remarks>
    /// The picking rule itself is <see cref="AimConvergence"/>, which is where the reasoning lives and
    /// where it is tested; this only names the three sources. They are the SAME ones a round meets:
    /// <see cref="_walls"/> is what <see cref="ResolveShot"/> marches, <see cref="TerrainTrace"/> is the
    /// cast through the ground the height field cannot answer, and <see cref="HumanProbes"/> is
    /// the reporting half of the <see cref="Humans"/> cast it offers rounds to.
    /// </remarks>
    private Vector3d? ResolveAimConvergence()
    {
        if (AimConvergenceRay is not { } ray)
        {
            return null;
        }

        return AimConvergence.Resolve(
            ray.OriginUnits,
            ray.Direction,
            AimConvergenceReachInUnits,
            _walls?.Tracer(),
            TerrainTrace,
            HumanProbes);
    }

    /// <summary>Gets the two FOV tangents blended for an uncapped presentation frame.</summary>
    /// <param name="alpha">How far the frame sits between the last two ticks, 0 to 1.</param>
    /// <remarks>
    /// This changes only presentation. <see cref="Mp5SightZoom"/> still advances by the original
    /// fixed-tick multipliers, so the sight reaches and leaves its limits in exactly the same number
    /// of simulation ticks at every render frame rate.
    /// </remarks>
    public SightZoomFrame SmoothedSightZoom(float alpha)
    {
        if (_view is not { } view)
        {
            return new SightZoomFrame(HumanView.DefaultTanX, HumanView.DefaultViewModelTanX);
        }

        if (!HasViewTickPair)
        {
            return new SightZoomFrame(view.TanX, view.ViewModelTanX);
        }

        float t = Math.Clamp(alpha, 0f, 1f);
        return new SightZoomFrame(
            _prevTickTanX + ((_tickTanX - _prevTickTanX) * t),
            _prevTickViewModelTanX + ((_tickViewModelTanX - _prevTickViewModelTanX) * t));
    }

    /// <summary>The rig's transform and held-weapon attachments for a frame between two ticks.</summary>
    /// <param name="alpha">How far the frame sits between the last two ticks, 0 to 1.</param>
    /// <returns>
    /// The rig origin in engine units, its orientation, and the three weapon attachments — the body's
    /// bone, the magazine's and the moving part's — when those bones exist.
    /// </returns>
    /// <remarks>
    /// The tick-stepped halves — the eye the body carried, the wall pullback, the clip's hold offset,
    /// the weapon bone, and the sway angles that ARE the weapon-change animation — are interpolated between the pair;
    /// the LOOK is this frame's own, because the mouse already runs per frame and the arms must stay
    /// welded to it. At <paramref name="alpha"/> 1 this is the rig's un-smoothed transform exactly.
    /// </remarks>
    public (
        Vector3d OriginUnits,
        Matrix3f Orientation,
        BoneAttachment? HeldWeapon,
        BoneAttachment? Magazine,
        BoneAttachment? MovingPart) SmoothedRig(float alpha)
    {
        if (_view is not { } view || !HasViewTickPair)
        {
            return (
                _view?.Position ?? default,
                _view?.Orientation ?? Matrix3f.Identity,
                _view?.HeldWeapon,
                _view?.HeldWeaponSecondary,
                _view?.HeldWeaponTertiary);
        }

        // The same composition SyncView commits — eye orientation times sway — with the tick-stepped
        // angles blended: the sway pair (the weapon-change animation) and the recoil kick inside the
        // aim pitch. The mouse-driven part of the pitch and the yaw are this frame's own.
        (float shakePitch, float shakeRoll, float shakeYaw) = SmoothedShake(alpha);
        Matrix3f eyeLook = Matrix3f.FromEngineEulerAngles(
            SmoothedAimPitch(alpha) + shakePitch, shakeRoll, -_body.Yaw + shakeYaw);
        Matrix3f look = eyeLook * Matrix3f.FromEngineEulerAngles(
                PresentationBlend.LerpAngle(_prevTickSwayPitch, _tickSwayPitch, alpha),
                0f,
                PresentationBlend.LerpAngle(_prevTickSwayYaw, _tickSwayYaw, alpha));

        Vector3d eye = PresentationBlend.Lerp(_prevTickEye, _tickEye, alpha);

        // M · v — the palette's own convention — is the transpose of the row-vector Transform.
        Vector3f held = look.Transposed().Transform(
            PresentationBlend.Lerp(_prevTickRootMotion, _tickRootMotion, alpha));
        double wallPullback = _prevTickWallPullback + ((_tickWallPullback - _prevTickWallPullback) * alpha);
        Vector3d origin = new(
            eye.X + held.X + (eyeLook.M12 * wallPullback),
            eye.Y + held.Y + (eyeLook.M22 * wallPullback),
            eye.Z + held.Z + (eyeLook.M32 * wallPullback));

        return (
            origin,
            look,
            _weaponBone.Smoothed(origin, look, alpha),
            _magazineBone.Smoothed(origin, look, alpha),
            _movingPartBone.Smoothed(origin, look, alpha));
    }

    /// <summary>One view-rig attachment bone over the last two ticks, ready to interpolate.</summary>
    /// <remarks>
    /// The bone is taken from the UNROTATED pose — the identity-root one — so the look can be applied
    /// per frame at draw. Equivalent by the argument the third-person draw leans on: the root rotation
    /// only ever left-multiplies every bone.
    /// </remarks>
    private struct ViewBonePair()
    {
        private Vector3f _previousPosition;
        private Vector3f _position;
        private Matrix3f _previousRotation = Matrix3f.Identity;
        private Matrix3f _rotation = Matrix3f.Identity;
        private bool _hasPair;

        /// <summary>Reads this tick's value, keeping the previous one, when the rig has the bone.</summary>
        /// <param name="pose">The rig's unrotated pose.</param>
        /// <param name="bone">The bone index, from <see cref="HumanAttachPoints"/>.</param>
        public void Capture(BonePose pose, int bone)
        {
            if (bone >= pose.BoneCount)
            {
                _hasPair = false;
                return;
            }

            Vector3f position = pose.Positions[bone];
            Matrix3f rotation = pose.Rotations[bone];
            _previousPosition = _hasPair ? _position : position;
            _previousRotation = _hasPair ? _rotation : rotation;
            _position = position;
            _rotation = rotation;
            _hasPair = true;
        }

        /// <summary>Drops the pair, so the next capture starts a fresh one from where the rig now is.</summary>
        public void Forget() => _hasPair = false;

        /// <summary>Resolves the attachment for a frame between the two ticks.</summary>
        /// <param name="origin">The rig origin the frame drew at, in engine units.</param>
        /// <param name="look">The rig's orientation this frame.</param>
        /// <param name="alpha">How far the frame sits between the last two ticks, 0 to 1.</param>
        /// <returns>The attachment, or <see langword="null"/> when the rig has no such bone.</returns>
        public readonly BoneAttachment? Smoothed(Vector3d origin, Matrix3f look, float alpha)
        {
            if (!_hasPair)
            {
                return null;
            }

            // M . v -- the palette's own convention -- is the transpose of the row-vector Transform.
            Vector3f bone = look.Transposed().Transform(
                PresentationBlend.Lerp(_previousPosition, _position, alpha));
            return new BoneAttachment(
                new Vector3d(origin.X + bone.X, origin.Y + bone.Y, origin.Z + bone.Z),
                look * PresentationBlend.InterpolateRotation(_previousRotation, _rotation, alpha));
        }
    }

    // The world cast state 36 plants along (0x4CC2A0): objects and the ground, nearest first.
    private WorldTraceCast? PlantTrace() => (_walls, TerrainTrace) switch
    {
        ({ } walls, { } ground) => WorldTrace.Nearest(walls.Tracer(), ground),
        ({ } walls, null) => walls.Tracer(),
        (null, { } ground) => ground,
        _ => null,
    };

    private WorldRayCast ViewModelWalls()
    {
        WorldTraceCast trace = (_walls, TerrainTrace) switch
        {
            ({ } walls, { } ground) => WorldTrace.Nearest(walls.Tracer(), ground),
            ({ } walls, null) => walls.Tracer(),
            (null, { } ground) => ground,
            _ => throw new InvalidOperationException("The player has neither terrain nor object collision."),
        };
        return WorldTrace.AsRayCast(trace);
    }

    /// <summary>The heights the doorway diagnostic probes forward at, in world units above the feet.</summary>
    /// <remarks>Ankle, waist, chest and head — enough to catch a threshold, a lintel or a mid wall.</remarks>
    private static readonly double[] BlockProbeHeights = [409.6, 1638.4, 4096.0, 7372.8];

    /// <summary>How far ahead the diagnostic probes for the blocking object, in world units (about 1.5 m).</summary>
    private const double BlockProbeReachUnits = 6144.0;

    /// <summary>
    /// Names the object the wall sweep stopped the body against, when the body is pressed into one while
    /// trying to move — the "what am I walking into" log for the doorway-collision hunt.
    /// </summary>
    /// <remarks>
    /// It fires only on a fresh contact (the blocking object changed) so a held-forward push logs once,
    /// not every tick. The line names the model instance, the placing task, and where the ray met it. The
    /// configured logger records it only at verbose information level and buffers any file output.
    /// </remarks>
    private void LogWallBlockIfAny(float forward, float strafe)
    {
        // Identifying the blocker casts four extra world rays. Do none of that work unless verbose
        // diagnostics are actually being collected; minimal/off logging must be a zero-cost path.
        if (!WallBlockDiagnosticsEnabled
            || _walls is null
            || !_body.TouchedWall
            || (forward == 0f && strafe == 0f))
        {
            _lastBlockLabel = string.Empty;
            return;
        }

        // Forward is the body's own facing, (sin, cos) — the way the walk carries it (PlayerBody.FacingBasis).
        double fx = Math.Sin(_body.Yaw);
        double fy = Math.Cos(_body.Yaw);

        string label = string.Empty;
        WorldRayHit nearest = default;
        double best = double.MaxValue;
        double bestHeight = 0.0;

        foreach (double height in BlockProbeHeights)
        {
            Vector3d origin = new(_body.Position.X, _body.Position.Y, _body.Position.Z + height);
            Vector3d end = new(origin.X + (fx * BlockProbeReachUnits), origin.Y + (fy * BlockProbeReachUnits), origin.Z);

            if (_walls.TryIdentify(origin, end, out string hitLabel, out WorldRayHit hit) && hit.Distance < best)
            {
                best = hit.Distance;
                label = hitLabel;
                nearest = hit;
                bestHeight = height;
            }
        }

        if (label.Length == 0 || label == _lastBlockLabel)
        {
            return;
        }

        _lastBlockLabel = label;

        double probeMetres = bestHeight / UnitsPerMetre;
        double aheadMetres = best / UnitsPerMetre;
        string line = FormattableString.Invariant(
            $"BLOCKED by {label} at ({nearest.Position.X:F0}, {nearest.Position.Y:F0}, {nearest.Position.Z:F0}) — probe {probeMetres:F2}m, {aheadMetres:F2}m ahead; player ({_body.Position.X:F0}, {_body.Position.Y:F0}, {_body.Position.Z:F0}) yaw {_body.Yaw:F2}");

        _logger.Information("Player", line);
        if (label.Contains("Fence", StringComparison.OrdinalIgnoreCase))
        {
            WorldInteraction? fence = _interactions?.FindElectrifiedFenceNear(nearest.Position);
            _logger.Information(
                "Player.Fence",
                $"wall contact task={(fence is null ? "-" : fence.Task.Id.ToString(System.Globalization.CultureInfo.InvariantCulture))} "
                    + $"powered={(fence?.IsElectrified == true ? "true" : "false")} "
                    + $"contact={(fence is null ? "miss" : "candidate")} "
                    + $"player=({_body.Position.X:F0},{_body.Position.Y:F0},{_body.Position.Z:F0})");
        }
    }

    // The nearest ladder that is close enough, faced and within height to mount.
    private void UpdateNearLadder()
    {
        _nearLadder = null;
        _nearLadderAtTop = false;
        _nearLadderAngle = double.MaxValue;
        _nearLadderIndex = -1;
        double best = double.MaxValue;

        for (int index = 0; index < _ladders.Count; index++)
        {
            LadderPlacement ladder = _ladders[index];

            // Human_UpdateActivate evaluates tasks from the HumanView position, not the task's feet.
            // Using the feet makes the strict ladder-origin height test fail until the player jumps.
            if (!ladder.CanActivate(_body.Eye, _body.Yaw, out bool atTop, out double angle))
            {
                continue;
            }

            // The shared resolver keeps the most head-on task, not the nearest one.
            if (angle < best)
            {
                best = angle;
                _nearLadder = ladder;
                _nearLadderAtTop = atTop;
                _nearLadderAngle = angle;

                // Kept so the marker can tell one ladder from another: a level declares no task id for
                // them, and the prompt has to know when the thing under the player's aim has changed.
                _nearLadderIndex = index;
            }
        }
    }

    // Puts the player on the ladder in reach: face it and snap onto it.
    private void MountLadder()
    {
        LadderPlacement ladder = _nearLadder!;

        // sub_4113E0 takes the task's gamma, adds PI and commits both exact column-based mount offsets.
        _body.BeginLadderTraversal();
        _climb.Mount(ladder, _nearLadderAtTop);
        _body.Yaw = ladder.PlayerYaw;
        _body.Position = _climb.Position;
        _nearLadder = null;
        _nearLadderIndex = -1;
        PoseChanged = true;
        SyncView();
    }

    /// <summary>Decides the next rung at a boundary — begins a rung, hands off, or holds (state 37).</summary>
    /// <param name="up">Whether up (W) is held.</param>
    /// <param name="down">Whether down (S) is held.</param>
    /// <param name="slide">Whether slide (use) is held.</param>
    /// <param name="terrain">The level terrain for a foot dismount.</param>
    /// <returns>The decision; a stepping result means the host should start the rung's clip.</returns>
    /// <remarks>
    /// Up/down only act at a rung boundary. Use is checked independently and may enter the retail slide
    /// state during a rung (<c>sub_40D6B0</c>).
    /// </remarks>
    public LadderStepResult DecideLadderStep(bool up, bool down, bool slide, IGroundHeightField? terrain)
    {
        LadderStepResult result = _climb.Decide(up, down, slide);
        if (result == LadderStepResult.Dismounted)
        {
            _body.Position = _climb.Position;
            _body.EndLadderTraversal(terrain, placeOnGround: true);
        }
        else
        {
            _body.Position = _climb.Position;
        }

        return result;
    }

    /// <summary>Moves the climbing body by the climb clip's root-motion delta for this tick.</summary>
    /// <param name="rootDelta">The clip's root motion, rotated into the facing frame, in world units.</param>
    public void MoveLadder(Vector3d rootDelta)
    {
        _climb.Move(rootDelta);
        _body.Position = _climb.Position;
    }

    /// <summary>Moves the body by the frame animation 168 runs on entering state 37.</summary>
    /// <param name="rootDelta">That frame's root motion, rotated into the facing frame, in world units.</param>
    public void MoveLadderEntryFrame(Vector3d rootDelta)
    {
        _climb.MoveEntryFrame(rootDelta);
        _body.Position = _climb.Position;
    }

    /// <summary>Commits the rung whose clip has just completed, returning the climb to a boundary.</summary>
    public void CompleteLadderStep()
    {
        _climb.CompleteStep();
        _body.Position = _climb.Position;
    }

    /// <summary>Takes a crossing of rung event 8 unless it repeats the event time the climb last took.</summary>
    /// <param name="time">The crossed event's authored time.</param>
    /// <returns><see langword="true"/> when the crossing counts; see <see cref="HumanLadderClimb.AcceptRungEvent"/>.</returns>
    public bool AcceptLadderRungEvent(float time) => _climb.AcceptRungEvent(time);

    /// <summary>Marks a crossing of rung event 8 in states 37, 38 or 39: the AI hears it and a rung sounds.</summary>
    /// <returns>The rung sound's definition name, to be played at <see cref="LadderFeet"/>.</returns>
    /// <remarks>
    /// The three states run the same block (<c>0x40D6B0</c>, <c>0x40D920</c>, <c>0x40DA80</c>), so the
    /// top transitions in animation 170 make both the noise and the sound too, not only a climbed rung.
    /// </remarks>
    public string TakeLadderStep()
    {
        AiNoise?.Invoke(AiEvent.Ladder);
        return _climb.NextStepSound();
    }

    /// <summary>Consumes animation 170's endpoint and resolves the roof when state 39 detaches.</summary>
    /// <param name="terrain">The level terrain used by the final ground placement.</param>
    public void CompleteLadderTopTransition(IGroundHeightField? terrain)
    {
        LadderTraversalPhase previous = _climb.Phase;
        if (!_climb.CompleteTopTransition())
        {
            return;
        }

        _body.Position = _climb.Position;
        if (previous == LadderTraversalPhase.GettingOffTop)
        {
            // The clip endpoint is authored on the roof. Resolve the highest object/terrain surface below
            // it once before restoring normal gravity, preventing a fractional endpoint from tunnelling.
            _body.EndLadderTraversal(terrain, placeOnGround: true);
        }
    }

    /// <summary>Advances state 40 and detaches when the normal mover reports ground contact.</summary>
    /// <param name="rootDelta">Animation 169's world-space root delta.</param>
    /// <param name="terrain">The level terrain.</param>
    /// <returns><see langword="true"/> when the slide ended this tick.</returns>
    public bool TickLadderSlide(Vector3d rootDelta, IGroundHeightField? terrain)
    {
        if (_climb.Phase != LadderTraversalPhase.SlidingDown)
        {
            return false;
        }

        bool grounded = _body.TickLadderSlide(terrain, rootDelta);
        _climb.UpdateSlidePosition(_body.Position);
        if (!grounded)
        {
            return false;
        }

        _climb.CompleteSlide();
        _body.EndLadderTraversal(terrain, placeOnGround: false);
        return true;
    }

    private void TickWeapons()
    {
        if (_weapons is null)
        {
            return;
        }

        // What the locomotion state asks the torso to play. Standing is the fallback for the states
        // that declare nothing -- the jump, the falls and the climbs leave the request where it was.
        WeaponAction locomotion = HumanLocomotionWeaponActions.TryGet(LocomotionState, out WeaponAction declared)
            ? declared
            : WeaponAction.Stand;

        _weapons.Tick(_input, _view?.ClipEnded ?? false, _body.IsGrounded, locomotion);

        if (_weapons.State == HumanWeaponState.Medipack)
        {
            float progress = _view?.ClipProgress ?? 0f;
            // The medipack task owns both its entry/loop action and the terminal FireAnimation3
            // request. In particular, full health is a terminal condition for the task, not a
            // reason for the presentation layer to replace the newly requested clip with Stand.
            _ = _weapons.TryUseMedipack(_body.Health, progress);
        }

        if (_weapons.InHand is { } held && _weapons.RequestedAction is { } action)
        {
            PlayWeaponAction(held.Weapon, action, _weapons.RequestedFireVariant);
        }
        else if (_weapons.InHand is null && _upperBodyAnimation >= 0)
        {
            // 0x4137E0's action zero: it clears Human+3420 and calls 0x489F00, which stands the upper
            // body down. Empty hands are the state that reaches it — there is no weapon record to pick a
            // torso clip out of — and without this the body holds the last weapon's stance after the
            // player puts it away.
            _upperBodyAnimation = -1;
            _upperBodySequence++;
        }
    }

    private void ResolveFire()
    {
        _impacts.Clear();

        if (_view is not null)
        {
            // Two machines share the alternate-fire action and the weapon in hand picks which one
            // answers: the MP5's toggled sight, or the Dragunov's held scope. The idle one is still
            // ticked with the current weapon so its own weapon-change reset fires; its answer is a
            // default frame and is discarded.
            WeaponType? heldWeapon = _weapons?.InHand?.Weapon;
            bool dragunov = heldWeapon is not null
                && string.Equals(heldWeapon.Weapon.TypeName, DragunovScope.TaskType, StringComparison.Ordinal);

            SightZoomFrame zoom;
            if (dragunov)
            {
                _ = _sightZoom.Tick(heldWeapon, false, _view.TanX, _view.ViewModelTanX);
                zoom = _dragunovScope.Tick(
                    heldWeapon,
                    _input.Current.IsSet(PlayerInputSlot.AlternateFire),
                    _input.Previous.IsSet(PlayerInputSlot.AlternateFire),
                    _input.Current.IsSet(PlayerInputSlot.ZoomIn),
                    _input.Current.IsSet(PlayerInputSlot.ZoomOut),
                    _view.TanX,
                    _view.ViewModelTanX);
            }
            else
            {
                _ = _dragunovScope.Tick(heldWeapon, false, false, false, false, _view.TanX, _view.ViewModelTanX);
                bool binocular = heldWeapon?.Category == WeaponCategory.Binocular;
                if (!binocular && _binocularsView.HasOpticsState)
                {
                    // Binoculars are not ticked once the weapon swap has selected another weapon.
                    // Clear their latch and restore the camera before MP5/other sight zoom is applied.
                    _binocularsView.Reset();
                    _view.TanX = HumanView.DefaultTanX;
                    _view.ViewModelTanX = HumanView.DefaultViewModelTanX;
                }

                zoom = binocular
                    ? _binocularsView.Tick(
                        heldWeapon,
                        _weapons?.ConsumeBinocularToggle() == true,
                        _input.IsSet(PlayerInputSlot.ZoomIn),
                        _input.IsSet(PlayerInputSlot.ZoomOut),
                        _view.TanX,
                        _view.ViewModelTanX)
                    : _sightZoom.Tick(
                        heldWeapon,
                        _input.IsRisingEdge(PlayerInputSlot.AlternateFire),
                        _view.TanX,
                        _view.ViewModelTanX);
            }

            _view.TanX = zoom.TanX;
            _view.ViewModelTanX = zoom.ViewModelTanX;
        }

        if (_weapons is null || _view is null)
        {
            return;
        }

        // Binoculars are an optical device, not a gun. The weapon state machine ignores Fire while
        // they are held, and this guard keeps a stale trigger/request from reaching the generic gun
        // resolver if the input changes during the weapon-swap transition.
        if (_weapons.InHand?.Weapon.Category == WeaponCategory.Binocular)
        {
            return;
        }

        // The barrel is the view's +Y axis and the cone opens across its +X and +Z -- 0x462E50 picks
        // exactly the middle column of the view's 3x3 out for the direction.
        Matrix3f m = _view.EyeOrientation;
        Vector3f forward = new(m.M12, m.M22, m.M32);
        Vector3f right = new(m.M11, m.M21, m.M31);
        Vector3f up = new(m.M13, m.M23, m.M33);

        // ...unless a camera that is not at the eye has said what the crosshair is over. See
        // AimConvergencePointUnits: null in first person, and null is the retail path untouched.
        AimBasis.Converge(_view.Eye, ResolveAimConvergence(), ref forward, ref right, ref up);

        // A scoped rifle is braced: 0x472E50 multiplies the round's sampled kick by 0.05 before the
        // accumulators see it, and this is the same factor at the same point.
        float recoilScale = _dragunovScope.IsScoped ? DragunovScope.ScopedRecoilFactor : 1f;
        if (!_weapons.ResolveFire(_view.Eye, forward, right, up, recoilScale))
        {
            return;
        }

        if (_weapons.M203Shots.Count > 0)
        {
            bool rejected = false;
            foreach (M203Projectile shot in _weapons.M203Shots)
            {
                rejected |= !_m203.Spawn(shot);
            }

            if (rejected)
            {
                _weapons.RefundRejectedSimulationShot();
            }

            return;
        }

        float damageFactor = _weapons.InHand?.Weapon.DamageFactor ?? 0f;

        // The category travels with the round because it decides how a body this round kills falls:
        // 0x45D3C0 records a shotgun kill as a throw and everything else as a crumple, and the death
        // state picks a different family of clips for each.
        WeaponCategory category = _weapons.InHand?.Weapon.Category ?? WeaponCategory.Gun;

        // A thrown round is a GRENADE, and a grenade was never hit-scan in the original either --
        // it is a physics object with a four-second fuse. It flies the same on every profile.
        WeaponType? thrownWeapon = _weapons.InHand?.Weapon;
        if (thrownWeapon is not null && thrownWeapon.Category == WeaponCategory.ProximityMine)
        {
            WorldTraceCast? mineTrace = GrenadeTrace;
            if (mineTrace is null && GrenadeWalls is { } mineWalls)
            {
                mineTrace = WorldTrace.FromRayCast(mineWalls);
            }

            foreach (GunProjectile shot in _weapons.Shots)
            {
                // The mine task is placed at the first surface under the crosshair. It is not a
                // thrown grenade: starting it at the muzzle with zero velocity made it drop through
                // the level until the physics solver happened to find a floor, which is visibly
                // different from retail and makes wall/prop placement impossible.
                _grenades.PlaceMine(
                    shot.Origin,
                    _body.Position,
                    shot.Direction,
                    shot.RangeInUnits,
                    mineTrace);
            }

            return;
        }

        if (thrownWeapon is not null && thrownWeapon.Category == WeaponCategory.Grenade)
        {
            // The physics table keys the explosion type by MODEL: 135_01_1 is the frag, 137_01_1
            // the flashbang.
            GrenadeExplosionType explosion = string.Equals(thrownWeapon.GunModel, "137_01_1", StringComparison.OrdinalIgnoreCase)
                ? GrenadeExplosionType.Flashbang
                : GrenadeExplosionType.Normal;

            // The speed is the CHARGE'S: retail winds the throw up while fire is held and releases
            // on the retail power curve, up to the table's own fifteen metres a second.
            float throwSpeed = PlayerWeapons.ChargedThrowSpeed(
                thrownWeapon.MuzzleVelocity, _weapons.ThrowChargeTicks);

            float radius = ExplosionDamage.DefaultGrenadeRadiusMetres;
            float falloff = ExplosionDamage.DefaultGrenadeFalloffMetres;
            float damage = ExplosionDamage.DefaultGrenadeDamageFactor;
            if (thrownWeapon.RetailConfig is { } config)
            {
                if (WeaponConfigRegistry.TryGetRealTag(config, "Grenade - Explosion radius (meters)", out float configured))
                {
                    radius = configured;
                }
                if (WeaponConfigRegistry.TryGetRealTag(config, "Grenade - Explosion falloff (meters)", out configured))
                {
                    falloff = configured;
                }
                if (WeaponConfigRegistry.TryGetRealTag(config, "Grenade - Explosion damage factor", out configured))
                {
                    damage = configured;
                }
            }

            foreach (GunProjectile shot in _weapons.Shots)
            {
                _grenades.Spawn(
                    shot.Origin,
                    shot.Direction,
                    throwSpeed,
                    explosion,
                    radiusMetres: radius,
                    falloffMetres: falloff,
                    damageFactor: damage);
            }

            return;
        }

        // A missile task is a physics object, not a gun ray. The launcher binding selects this path
        // for every missile carrier; no weapon id or model name is special-cased here.
        WeaponType? missileWeapon = _weapons.InHand?.Weapon;
        if (missileWeapon is not null
            && string.Equals(missileWeapon.Projectile.TypeName, "TASKTYPE_MISSILE", StringComparison.Ordinal))
        {
            bool rejected = false;
            foreach (GunProjectile shot in _weapons.Shots)
            {
                MissileType missileType = MissileFlight.TypeForLauncher(missileWeapon);
                // Direct / keyboard rockets fly along the barrel. Using hit-scan EffectiveRange as
                // the aim point lofted anti-tank shots 25 m above a phantom far point.
                Vector3d target = missileType is MissileType.MissileDirect or MissileType.MissileKeyboard
                    ? shot.Origin + new Vector3d(
                        shot.Direction.X * MissileFlight.UnitsPerMetre,
                        shot.Direction.Y * MissileFlight.UnitsPerMetre,
                        shot.Direction.Z * MissileFlight.UnitsPerMetre)
                    : shot.Origin + new Vector3d(
                        shot.Direction.X * shot.RangeInUnits,
                        shot.Direction.Y * shot.RangeInUnits,
                        shot.Direction.Z * shot.RangeInUnits);

                rejected |= !_missiles.Spawn(
                    shot.Origin,
                    target,
                    missileType,
                    MissileFlight.LaunchSpeedMetresPerSecond(missileWeapon),
                    shot.Direction,
                    string.IsNullOrWhiteSpace(missileWeapon.Projectile.Text) ? "110_02_1" : missileWeapon.Projectile.Text,
                    sourceWeaponId: missileWeapon.WeaponId);
            }

            if (rejected)
            {
                _weapons.RefundRejectedSimulationShot();
            }

            return;
        }

        // The opt-in fork. With g_ballistics on, a round that can fly is spawned into the simulation
        // instead of being resolved here -- it arrives ticks later, in TickBallistics. A weapon no
        // profile accepts (the knife, a rocket carrier) falls through to the retail ray either way,
        // so the option never silently disarms anything.
        WeaponType? held = _weapons.InHand?.Weapon;
        AmmoType? ammo = _weapons.InHand?.Gun.Ammo;
        bool simulate = GameplayCVars.Ballistics.Value
            && held is not null
            && BallisticProfile.TryCreate(held, ammo) is not null;

        // Where the round is DRAWN from: the gun sits right of and below the eye the simulation
        // fires from, and a tracer leaving dead screen centre is invisible -- it recedes into a
        // point behind the crosshair. The offset is presentation only; the flight is the retail
        // eye-origin geometry either way.
        Vector3d visualOffset = new(
            ((right.X * 0.22) - (up.X * 0.16)) * UnitsPerMetre,
            ((right.Y * 0.22) - (up.Y * 0.16)) * UnitsPerMetre,
            ((right.Z * 0.22) - (up.Z * 0.16)) * UnitsPerMetre);

        foreach (GunProjectile shot in _weapons.Shots)
        {
            if (simulate)
            {
                _ballistics.Spawn(
                    shot,
                    held!,
                    ammo,
                    damageFactor,
                    category,
                    visualOffset,
                    GameplayCVars.BallisticsMinimumFlight.Value);
            }
            else if (category == WeaponCategory.Knife)
            {
                ResolveStab(shot, right, damageFactor, category);
            }
            else
            {
                ResolveShot(shot, damageFactor, category, held?.PenetrationPower ?? 0f);
            }
        }
    }

    /// <summary>Advances the ballistic rounds and lands whatever arrived this tick.</summary>
    /// <remarks>
    /// The impact side-effects are exactly <see cref="ResolveShot"/>'s, applied on the tick the round
    /// arrives rather than the tick it was fired: the hit counters, the AI's ground-impact notice and
    /// the decal. Damage needs nothing here — the <see cref="Humans"/> delegate applied it during the
    /// cast, the same way it does for a ray.
    /// </remarks>
    private void TickBallistics()
    {
        // The grenades fly every tick regardless of the tracer option -- they are the retail
        // behaviour, not a remaster one. Detonations are drained by the game, which owns the
        // effects and the blast's victims.
        _grenades.Tick(GrenadeWalls ?? _body.Walls, HumanProximityTargets, GrenadeTrace);
        if (MissileTrace is { } missileTrace)
        {
            _missiles.TickWithMissileContext(
                (LiveMissile _, Vector3d from, Vector3d to, out WorldTraceHit hit) =>
                    missileTrace(new WorldTraceRequest(from, to, WorldTraceMask.World), out hit),
                HumanProbes);
        }
        else
        {
            _missiles.Tick(MissileWalls ?? GrenadeWalls ?? _body.Walls, HumanProbes);
        }
        _m203.Tick(GrenadeWalls ?? _body.Walls, HumanProbes);

        if (_ballistics.Rounds.Count == 0)
        {
            return;
        }

        WorldRayCast? walls = _body.Walls;
        _ballistics.Tick(
            walls,
            Humans,
            GameplayCVars.BallisticsGravity.Value,
            GameplayCVars.BallisticsDrag.Value,
            _walls?.Tracer(),
            BallisticEntryOf);

        foreach (BallisticImpact impact in _ballistics.Impacts)
        {
            if (impact.StruckHuman)
            {
                HumanHits++;
                LastHumanZone = impact.Human.Zone;
            }
            else if (impact.StruckWall)
            {
                GroundImpact?.Invoke(impact.Wall.Position);

                // The surface decides what the hit looks like, out of the material its own face names,
                // and the struck object decides whether it takes a mark at all.
                if (_materials is { } materials)
                {
                    Marks.TryAdd(
                        impact.Wall,
                        BulletMarks.SurfaceOf(impact.Owner, impact.Placement),
                        materials,
                        isEntry: !impact.Wall.IsExit,
                        exitCrossing: impact.Wall.IsExit,
                        probe: MarkProbe,
                        placement: impact.Placement);
                }
            }

            _impacts.Add(new GunImpact(
                impact.Position,
                impact.StruckHuman || impact.StruckWall,
                impact.RangeInUnits,
                impact.Owner,
                impact.Placement,
                StruckHuman: impact.StruckHuman,
                Wall: impact.StruckWall ? impact.Wall : default,
                Direction: impact.Direction));
        }
    }

    // A flying round meets a surface or a body under the hit-scan march's own rule: the material row against
    // the weapon's power, and only an object the trace named can be passed. A glance stops a flying round.
    private GunShotImpact BallisticEntryOf(
        BallisticRound round,
        in WorldRayHit surface,
        bool namedObject,
        Vector3d direction) =>
        _materials is { } rows && rows.TryGet(surface.Material, out GameMaterial row)
            ? GunShotBallistics.ResolveEntry(
                round.Weapon.PenetrationPower,
                row,
                namedObject,
                GunShotBallistics.IncidenceRadians(direction, surface.Normal),
                alreadyDeflected: false,
                _random.NextSingle)
            : GunShotImpact.Stopped;

    /// <summary>Resolves one round the way <c>0x47B750</c> does — as a march, not a single cast.</summary>
    /// <remarks>
    /// <para>
    /// The round is hit-scan and settled entirely in the tick it is fired, but it does not stop at the
    /// first thing it meets. The original loops while range is left, and each surface either stops the
    /// round, is penetrated, or deflects it — the rules are <see cref="GunShotBallistics"/>, which had
    /// been written from this same function and then never driven by anything.
    /// </para>
    /// <para>
    /// Each surface's own row supplies the two numbers: <see cref="GameMaterial.BulletResistance"/>
    /// against the weapon's penetration power (<c>0x47BD88</c>), and
    /// <see cref="GameMaterial.RicochetAngleDegrees"/> against the angle of arrival (<c>0x47BB96</c>).
    /// A deflection is allowed once per round and is tested BEFORE penetration, exactly as the original
    /// orders them, so a grazing round glances rather than punching through.
    /// </para>
    /// <para>
    /// A crossing is an <b>entry</b> or an <b>exit</b>, never both: <c>0x4CD300</c> reports the two as
    /// the halves of one condition (<c>*a7 = c0; *a8 = !c0</c>), so the pair the trace hands back always
    /// has exactly one side set. An entry sounds and marks the surface and is what penetration and
    /// deflection are tested on; an exit leaves only the far-side decal, silently, and arms the latch
    /// that stops the round on its next entry (<c>0x47BB0A</c>) — which is what keeps a round that
    /// punched through a wall from carrying on through the whole level.
    /// </para>
    /// </remarks>
    private void ResolveShot(
        GunProjectile shot,
        float damageFactor,
        WeaponCategory category,
        float penetrationPower)
    {
        Vector3d direction = new(shot.Direction.X, shot.Direction.Y, shot.Direction.Z);
        Vector3d origin = shot.Origin;
        float remaining = shot.RangeInUnits;
        bool deflected = false;
        bool leftASurface = false;

        // The impacts the march records, marked only once it is over: 0x47C262 decides each one's entry
        // flag after the loop, from whether the round ever glanced and from which way the LAST surface
        // faced. A round that glances therefore marks even the surfaces it met BEFORE the glance as exits.
        _pendingMarks.Clear();
        March();
        CommitMarks(deflected);

        void March()
        {
            // The march is bounded by the range it spends, but a surface reporting a zero-length step would
            // spend none. Cap the passes at the number of impacts one round may record.
            for (int pass = 0; pass < GunShotBallistics.MaximumRecordedImpacts && remaining > 0f; pass++)
            {
                Vector3d end = origin + (direction * remaining);

                // The direction the effect dispatcher carries with the contact. It changes mid-march when
                // the round glances, so it is taken fresh each pass rather than from the shot.
                Vector3f travel = new((float)direction.X, (float)direction.Y, (float)direction.Z);

                WorldTraceHit richHit = default;
                bool struckWall = _walls is not null
                    && _walls.TryTrace(new WorldTraceRequest(origin, end, WorldTraceMask.World), out richHit);
                WorldRayHit hit = struckWall ? richHit.RayHit : default;

                // The wall first, then the guards short of it. A guard in front of a wall takes the round
                // and the wall stays clean; one behind it is never offered the damage, and the wall is
                // marked. The callback damages as it is called, so the stop has to go in with the round.
                HumanHit human = default;
                bool struckHuman = Humans is { } humans
                    && humans(
                        origin,
                        end,
                        damageFactor,
                        category,
                        struckWall ? hit.Distance : double.PositiveInfinity,
                        alreadyHeard: pass > 0,
                        out human)
                    && (!struckWall || human.Distance < hit.Distance);

                if (struckHuman && ResolveBody(human, travel))
                {
                    continue;
                }

                if (struckHuman)
                {
                    return;
                }

                if (!struckWall)
                {
                    _impacts.Add(new GunImpact(end, false, remaining, Direction: travel));
                    return;
                }

                remaining = GunShotBallistics.ConsumeRange(remaining, (float)hit.Distance);

                // Coming OUT of something. The original marks the far side and says nothing: no sound, no
                // hit message, no penetration test. It only remembers that it happened.
                if (hit.IsExit)
                {
                    Record(hit, richHit, exitCrossing: true);

                    leftASurface = true;
                    if (remaining <= 0f)
                    {
                        return;
                    }

                    origin = StepOff(hit.Position, direction);
                    continue;
                }

                GroundImpact?.Invoke(hit.Position);

                _impacts.Add(new GunImpact(
                    hit.Position,
                    true,
                    remaining,
                    richHit.Owner,
                    richHit.Placement,
                    Wall: hit,
                    Direction: travel));

                // A round that has already come out of something stops on the next thing it enters,
                // whatever that thing is made of — it has spent its one pass through.
                if (leftASurface)
                {
                    Record(hit, richHit, exitCrossing: false);
                    return;
                }

                GameMaterial? struck = _materials is { } rows && rows.TryGet(hit.Material, out GameMaterial row)
                    ? row
                    : null;
                if (struck is null || remaining <= 0f)
                {
                    Record(hit, richHit, exitCrossing: false);
                    return;
                }

                // The graze first, then the bite. The collision index has already turned the normal to face
                // the ray, so the angle between the reversed travel and that normal is the incidence 0x4B2B30
                // returns. The original will not let a round through a surface whose trace named no object,
                // whatever the margin, so a hit on the bare level shell always stops it.
                GunShotImpact outcome = GunShotBallistics.ResolveEntry(
                    penetrationPower,
                    struck,
                    namedObject: richHit.Owner is not null,
                    GunShotBallistics.IncidenceRadians(direction, hit.Normal),
                    deflected,
                    _random.NextSingle);
                if (outcome == GunShotImpact.Deflected)
                {
                    // 0x47BD7B sounds the whizz directly here rather than through the material's
                    // window, so a round that glances always gets one.
                    deflected = true;
                    Deflected?.Invoke(hit.Position);

                    // 0x47C262: a round that has glanced marks every surface it met as an EXIT, which
                    // on metal and concrete is the dent rather than the hole. The latch is set here,
                    // before the impact is recorded at 0x47BDED; CommitMarks reads it.
                    Record(hit, richHit, exitCrossing: false);
                    direction = Reflect(direction, hit.Normal);
                    origin = StepOff(hit.Position, direction);
                    continue;
                }

                if (outcome == GunShotImpact.Stopped)
                {
                    Record(hit, richHit, exitCrossing: false);
                    return;
                }

                Record(hit, richHit, exitCrossing: false);
                origin = StepOff(hit.Position, direction);
            }
        }

        // A body is a surface like any other to 0x47B750: the trace's nearest contact, run through the same
        // branches as a wall. An entry face takes the hit -- the damage the Humans callback has already
        // applied -- and then the same graze and bite as a wall, against the face's own material: Flesh and
        // BloodyFlesh resist 1, so a power-2 or power-3 round goes on through and a power-1 round three times
        // in five. An exit face is the round leaving the body: no handler, and the latch that stops the round
        // on the next thing it enters, after that thing has taken the hit. Probed on the guards' rounds
        // through the player (pairs of hits from one round); the player's rounds take the same march.
        // Returns whether the march carries on.
        bool ResolveBody(in HumanHit human, Vector3f travel)
        {
            remaining = GunShotBallistics.ConsumeRange(remaining, (float)human.Distance);

            if (human.IsExit)
            {
                leftASurface = true;
                if (remaining <= 0f)
                {
                    return false;
                }

                origin = StepOff(human.Position, direction);
                return true;
            }

            HumanHits++;
            LastHumanZone = human.Zone;
            _impacts.Add(new GunImpact(human.Position, true, remaining, StruckHuman: true, Direction: travel));

            if (leftASurface
                || remaining <= 0f
                || human.Material < 0
                || _materials is not { } rows
                || !rows.TryGet(human.Material, out GameMaterial skin))
            {
                return false;
            }

            GunShotImpact outcome = GunShotBallistics.ResolveEntry(
                penetrationPower,
                skin,
                namedObject: true,
                GunShotBallistics.IncidenceRadians(direction, human.Normal),
                deflected,
                _random.NextSingle);
            if (outcome == GunShotImpact.Stopped)
            {
                return false;
            }

            if (outcome == GunShotImpact.Deflected)
            {
                deflected = true;
                Deflected?.Invoke(human.Position);
                direction = Reflect(direction, human.Normal);
            }

            origin = StepOff(human.Position, direction);
            return true;
        }

        // One recorded impact. The surface decides what it looks like, out of the material its own face
        // names, and the struck OBJECT decides whether it takes a mark at all -- terrain and the types
        // that refuse the decal message never do.
        void Record(in WorldRayHit surface, in WorldTraceHit contact, bool exitCrossing)
        {
            if (_pendingMarks.Count < GunShotBallistics.MaximumRecordedImpacts)
            {
                _pendingMarks.Add(new PendingMark(surface, BulletMarks.SurfaceOf(contact), exitCrossing, contact.Placement));
            }
        }
    }

    /// <summary>Marks the impacts one round recorded, with the flags the original gives them.</summary>
    /// <param name="deflected">Whether the round glanced at any point.</param>
    /// <remarks>
    /// <c>0x47C262</c>: every impact is an exit once the round has glanced. Otherwise each is an entry
    /// except the last, which is an exit when its surface faced away from the round.
    /// </remarks>
    private void CommitMarks(bool deflected)
    {
        if (_materials is not { } table)
        {
            _pendingMarks.Clear();
            return;
        }

        for (int i = 0; i < _pendingMarks.Count; i++)
        {
            PendingMark pending = _pendingMarks[i];
            bool isEntry = !deflected && (i < _pendingMarks.Count - 1 || !pending.Hit.IsExit);
            Marks.TryAdd(pending.Hit, pending.Surface, table, isEntry, pending.ExitCrossing, MarkProbe, pending.Placement);
        }

        _pendingMarks.Clear();
    }

    /// <summary>How far the knife reaches, in world units: <c>0x46E033</c> scales the blade by 8192.</summary>
    public const double KnifeReachInUnits = 8192.0;

    /// <summary>The blade angles <c>KnifeStab</c> tries, in order, in radians about the view's up axis.</summary>
    /// <remarks>
    /// <c>0x46DF70</c> starts at <c>-0.2617994</c> (-15 degrees) and adds <c>0.17453294</c> (10 degrees)
    /// twice, stopping at the first angle that lands.
    /// </remarks>
    public static readonly float[] KnifeFanRadians = [-0.2617994f, -0.2617994f + 0.17453294f, -0.2617994f + (2f * 0.17453294f)];

    /// <summary>The direction of one blade angle.</summary>
    /// <param name="forward">The view's forward (+Y) axis.</param>
    /// <param name="right">The view's right (+X) axis.</param>
    /// <param name="angle">The angle, in radians.</param>
    /// <returns>The blade direction.</returns>
    /// <remarks>
    /// <c>0x4B3790</c> post-multiplies the view by the Z rotation <c>0x4B3BE0</c> builds, and the stab
    /// takes the result's middle column: <c>cos a * forward - sin a * right</c>. A negative angle
    /// therefore swings the blade to the right.
    /// </remarks>
    public static Vector3d KnifeBladeDirection(Vector3f forward, Vector3f right, float angle)
    {
        double c = Math.Cos(angle);
        double s = Math.Sin(angle);
        return new Vector3d(
            (c * forward.X) - (s * right.X),
            (c * forward.Y) - (s * right.Y),
            (c * forward.Z) - (s * right.Z));
    }

    /// <summary>Resolves a knife stab the way <c>KnifeStab</c> (<c>0x46DFB0</c>) does.</summary>
    /// <remarks>
    /// <para>
    /// The knife is not a gun ray. Its task sweeps the blade across <see cref="KnifeFanRadians"/>, a
    /// <see cref="KnifeReachInUnits"/> cast at each, and hands the first thing that answers the hit
    /// message that message and nothing else: it never records an impact, so there is no
    /// <c>GunShotEffect</c>, no mark, no impact sound and no dust.
    /// </para>
    /// <para>
    /// A body answers, so it ends the sweep. The port cannot ask a wall whether its type answers, so a
    /// wall does not end it; the first wall met is reported, flagged <see cref="GunImpact.Melee"/>, only
    /// when no angle reached a body -- that is what lets a stab break a pane or silence a speaker.
    /// </para>
    /// </remarks>
    private void ResolveStab(GunProjectile shot, Vector3f right, float damageFactor, WeaponCategory category)
    {
        Vector3f forward = shot.Direction;
        Vector3d origin = shot.Origin;
        float range = (float)KnifeReachInUnits;
        GunImpact? firstWall = null;

        foreach (float angle in KnifeFanRadians)
        {
            Vector3d direction = KnifeBladeDirection(forward, right, angle);
            Vector3d end = origin + (direction * KnifeReachInUnits);
            Vector3f travel = new((float)direction.X, (float)direction.Y, (float)direction.Z);

            WorldTraceHit contact = default;
            bool struckWall = _walls is not null
                && _walls.TryTrace(new WorldTraceRequest(origin, end, WorldTraceMask.World), out contact);

            // A body behind the wall is out of reach: the blade stops where the wall is. The stab is no
            // GunShot round, so nobody hears a near miss (0x4606F0 walks only the round list).
            Vector3d reach = struckWall ? contact.Position : end;
            if (Humans is { } humans
                && humans(
                    origin, reach, damageFactor, category, double.PositiveInfinity, alreadyHeard: true, out HumanHit human)
                && !human.IsExit)
            {
                HumanHits++;
                LastHumanZone = human.Zone;
                _impacts.Add(new GunImpact(
                    human.Position, true, range, StruckHuman: true, Direction: travel, Melee: true));
                return;
            }

            if (struckWall && firstWall is null)
            {
                firstWall = new GunImpact(
                    contact.Position,
                    true,
                    range,
                    contact.Owner,
                    contact.Placement,
                    Wall: contact.RayHit,
                    Direction: travel,
                    Melee: true);
            }
        }

        _impacts.Add(firstWall ?? new GunImpact(
            origin + (new Vector3d(forward.X, forward.Y, forward.Z) * KnifeReachInUnits),
            false,
            range,
            Direction: forward,
            Melee: true));
    }

    private readonly record struct PendingMark(
        WorldRayHit Hit,
        object? Surface,
        bool ExitCrossing,
        WorldPlacement? Placement);

    /// <summary>Raised where a round glanced off a surface, so the whizz can be sounded there.</summary>
    /// <remarks>
    /// Separate from the ordinary contact notice because the original sounds a deflection directly at
    /// <c>0x47BD7B</c>, outside the material's own quiet window.
    /// </remarks>
    public Action<Vector3d>? Deflected { get; set; }

    /// <summary>The angle a round arrives at, measured off the surface normal.</summary>
    internal static float IncidenceRadians(Vector3d direction, Vector3f normal) =>
        GunShotBallistics.IncidenceRadians(direction, normal);

    /// <summary>Mirrors a direction about a surface normal, as the deflection at <c>0x47BC8E</c> does.</summary>
    internal static Vector3d Reflect(Vector3d direction, Vector3f normal)
    {
        double dot = (direction.X * normal.X) + (direction.Y * normal.Y) + (direction.Z * normal.Z);
        Vector3d reflected = new(
            direction.X - (2.0 * dot * normal.X),
            direction.Y - (2.0 * dot * normal.Y),
            direction.Z - (2.0 * dot * normal.Z));
        double length = Math.Sqrt(
            (reflected.X * reflected.X) + (reflected.Y * reflected.Y) + (reflected.Z * reflected.Z));
        return length > 0.0
            ? new Vector3d(reflected.X / length, reflected.Y / length, reflected.Z / length)
            : direction;
    }

    /// <summary>Where the next cast starts — five millimetres past the face, so it cannot meet it again.</summary>
    private static Vector3d StepOff(Vector3d position, Vector3d direction) =>
        new(
            position.X + (direction.X * GunShotBallistics.PenetrationStepOffUnits),
            position.Y + (direction.Y * GunShotBallistics.PenetrationStepOffUnits),
            position.Z + (direction.Z * GunShotBallistics.PenetrationStepOffUnits));

    // 0x45E060, for the slots this port can supply. Every action slot carries the MASKED BIT VALUE and
    // never 1, because the original stores `mask & bit` -- a consumer that compared against 1 would
    // read almost every action as released.
    internal static PlayerInputFrame SampleFrame(InputState input, PlayerProfile? profile)
    {
        PlayerInputFrame frame = default;
        foreach ((PlayerInputSlot slot, PlayerAction action) in PlayerInputBindings.All)
        {
            Set(
                ref frame,
                slot,
                PlayerProfileInputBindings.IsDown(profile, input, PlayerInputBindings.ConfigName(action)));
        }

        return frame;
    }

    private static void Set(ref PlayerInputFrame frame, PlayerInputSlot slot, bool down)
    {
        if (down && PlayerInputBindings.TryGetAction(slot, out PlayerAction action))
        {
            frame[(int)slot] = (uint)action;
        }
    }

    private static float Axis(PlayerInputState input, PlayerInputSlot positive, PlayerInputSlot negative)
    {
        float value = 0f;
        if (input.IsSet(positive))
        {
            value += 1f;
        }

        if (input.IsSet(negative))
        {
            value -= 1f;
        }

        return value;
    }
}
