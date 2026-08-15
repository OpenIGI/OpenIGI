using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Numerics;
using OpenIGI.Formats.Animation;
using OpenIGI.Engine.Tasks;
using OpenIGI.Game;
using OpenIGI.Game.Config;
using OpenIGI.Game.Player;
using OpenIGI.Game.Weapons;
using OpenIGI.Game.Weapons.Ballistics;
using OpenIGI.Game.World;
using OpenIGI.Input;
using OpenIGI.Rendering;

namespace OpenIGI.Desktop;

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
    private readonly IIgiLogger _logger;
    private readonly PlayerInputState _input = new();
    private readonly IgiRandom _random = new();
    private readonly Mp5SightZoom _sightZoom = new();
    private readonly DragunovScope _dragunovScope = new();
    private readonly List<GunImpact> _impacts = [];
    private readonly BallisticSimulation _ballistics = new();
    private readonly GrenadeSimulation _grenades = new();
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
    private WorldInteractionRegistry? _interactions;
    private GunPickupRegistry? _gunPickups;
    private WorldInteraction? _nearInteraction;
    private WorldInteraction? _activeInteraction;
    private int _interactionTick;
    private bool _interactionUseArmed;
    private Vector3d _fenceExitPosition;
    private float _interactionFacing;
    private Vector3d _wireDirection;
    private double _wireSpeed;
    private bool _mapComputerOpen;
    private bool _running;
    private GenericPickupRegistry? _genericPickups;

    /// <summary>Initializes the player camera.</summary>
    /// <param name="logger">Where optional collision diagnostics are written.</param>
    public PlayerCamera(IIgiLogger? logger = null)
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
    private double _prevTickWallPullback;
    private double _tickWallPullback;
    private Vector3f _prevTickRootMotion;
    private Vector3f _tickRootMotion;
    private Vector3f _prevTickWeaponBonePosition;
    private Vector3f _tickWeaponBonePosition;
    private Matrix3f _prevTickWeaponBoneRotation = Matrix3f.Identity;
    private Matrix3f _tickWeaponBoneRotation = Matrix3f.Identity;
    private bool _hasWeaponBonePair;
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

    /// <summary>Gets the rounds in flight under <c>g_ballistics</c>, for the tracer renderer.</summary>
    /// <remarks>Empty whenever the option is off — nothing is ever spawned into it.</remarks>
    public BallisticSimulation Ballistics => _ballistics;

    /// <summary>Gets whether the Dragunov's scope is open, for the HUD's overlay.</summary>
    public bool IsSniperScoped => _dragunovScope.IsScoped;

    /// <summary>Gets the grenades in the air, both the player's and the AI's.</summary>
    /// <remarks>
    /// One simulation for both throwers so there is one place to tick, draw and detonate from — the
    /// soldiers spawn their lobs into it through the renderer's wiring.
    /// </remarks>
    public GrenadeSimulation Grenades => _grenades;

    /// <summary>Gets or sets what a grenade bounces off — the world <b>including the terrain</b>.</summary>
    /// <remarks>
    /// Deliberately not <see cref="PlayerBody.Walls"/>: that is the object index alone, because the
    /// body meets the terrain through its own ground query. A grenade has no ground query — without
    /// the terrain in its cast it falls straight through the floor of the world.
    /// </remarks>
    public WorldRayCast? GrenadeWalls { get; set; }

    /// <summary>Gets or sets what offers this player's rounds to the humans in the world.</summary>
    /// <remarks>
    /// Without it rounds stop at walls and hit nobody. A round takes whichever of the two is
    /// <b>nearer</b>, so a guard standing in front of a wall is hit and the wall is not marked.
    /// </remarks>
    public HumanShot? Humans { get; set; }

    /// <summary>Gets or sets where actual projectile contacts are offered to the AI event broadcaster.</summary>
    /// <remarks>
    /// This is invoked only when the world surface is the nearest hit. A human in front of that surface
    /// receives damage instead and does not also manufacture a ground-impact event behind itself.
    /// </remarks>
    public Action<Vector3d>? GroundImpact { get; set; }

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

    /// <summary>
    /// Gets the pitch the view is actually drawn at — the look, plus the recoil sitting on top of it.
    /// </summary>
    /// <remarks>
    /// <c>0x464800</c> writes <c>view+288 = view+296 + view+292</c>: the accumulated recoil is
    /// <b>added</b> to the mouse-driven pitch rather than folded into it, which is what lets it decay
    /// back to where you were aiming instead of leaving the view permanently raised.
    /// </remarks>
    public float AimPitch => _body.Pitch + (_weapons?.RecoilPitch ?? 0f);

    /// <summary>Gets whether the weapon in hand fired on the last tick.</summary>
    public bool FiredThisTick => _weapons?.FiredThisTick ?? false;

    /// <summary>Gets the serial of the last round fired.</summary>
    public int ShotSequence => _weapons?.ShotSequence ?? 0;

    /// <summary>Gets the weapon in hand, or <see langword="null"/> when there is none.</summary>
    public WeaponType? HeldWeaponType => _weapons?.InHand?.Weapon;

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
        if (animations.FullBody < 0)
        {
            return false;
        }

        if (_view.PlayWeaponAnimation(animations.FullBody, animations.LoopFullBody) is null)
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
        _view.EyeOrientation = Matrix3f.FromEngineEulerAngles(AimPitch, 0f, -_body.Yaw);

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
        _hasWeaponBonePair = false;
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
    public Camera3D Camera => new(ToMetres(_body.Eye), Camera3D.LookRotation(_body.Yaw, AimPitch));

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

        // The probe answers only for OBJECTS. A step that finds nothing solid landed on open terrain,
        // which the original resolves by asking the terrain's own task -- something this port has no
        // equivalent of, so it takes the Ground row by name instead.
        (Vector3d from, Vector3d to) = FootstepSounds.Probe(_body.Position);
        Vector3d where = _body.Position;
        GameMaterial material;

        if (_body.Walls is { } walls
            && walls(from, to, out WorldRayHit hit)
            && _materials.TryGet(hit.Material, out GameMaterial struck))
        {
            material = struck;
            where = hit.Position;
        }
        else if (!_materials.TryGetByName(FootstepSounds.TerrainMaterialName, out material))
        {
            return null;
        }

        string sound = FootstepSounds.Next(material, ref _footCursor);
        return sound.Length == 0
            ? null
            : new Footstep(sound, where, FootstepSounds.HearingRadiusInUnits(material));
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
    public bool Place(TerrainData? terrain, Vector3d at, float yaw)
    {
        _body.Position = new Vector3d(at.X * UnitsPerMetre, at.Y * UnitsPerMetre, at.Z * UnitsPerMetre);
        _body.Yaw = yaw;

        // A placement is a teleport; the rig's tick pair must not be interpolated across it.
        HasViewTickPair = false;
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

    /// <summary>Consumes this tick's authored horizontal root motion for a self-driving interaction.</summary>
    /// <remarks>
    /// Fence animation 171 carries Jones across the obstacle in its root track. As with ordinary grounded
    /// locomotion, X/Y move the human task while the current root Z is presentation height; applying Z here
    /// as well would lift the task and then add the same vault height again when the body is drawn.
    /// </remarks>
    internal void ApplyInteractionRootMotion(Vector3d rootDelta)
    {
        if (_activeInteraction?.Type != InteractionActivateType.Fence)
        {
            return;
        }

        _body.Position = new Vector3d(
            _body.Position.X + rootDelta.X,
            _body.Position.Y + rootDelta.Y,
            _body.Position.Z);
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
            ActiveAnimationParameter(_activeInteraction.Task),
        _ => -1,
    };

    /// <summary>Gets the retail action sprite selected by the current task.</summary>
    public string? ActionSprite => _nearInteraction is { } interaction
        ? DoorNeedsPicking(interaction) ? "action_picklock.spr" : interaction.ActionSprite
        : ShowLadderAction ? StatusScreenLayout.LadderActionSprite : null;

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
    public bool PlaceAt(TerrainData? terrain, PlayerSpawnPoint spawn, WorldRayCast? registrationGround = null)
    {
        _body.Position = spawn.Position;
        _body.Yaw = spawn.Gamma;
        _body.Pitch = 0f;
        HasViewTickPair = false;

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
            _body.Yaw += LookSettings.YawDelta(mouse.DeltaX);
            _body.Pitch = Math.Clamp(
                _body.Pitch + LookSettings.PitchDelta(mouse.DeltaY),
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
    public void Tick(InputState input, TerrainData? terrain)
    {
        ArgumentNullException.ThrowIfNull(input);

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
            TickInteraction(input, terrain);
            return;
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
            view.TickWallPullback(ViewModelWalls(terrain));
        }

        // Where 0x464BD0 steps it: after the state machines have read it and before the rig is placed.
        _weapons?.Sway.Advance();

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

        // Map controls are consumed by ComputerMap, not HumanPlayer. Feeding an empty frame prevents a
        // held movement/fire key from leaking into the dormant weapon machine while preserving its clock.
        _input.Accept(default);
        _animator?.Enter(45);
        TickWeapons();
        _view?.Tick();
        _weapons?.Sway.Advance();
        PoseChanged = true;
        SyncView();
        CaptureViewTickState();
    }

    /// <summary>Stops the held-weapon machine because the player has died.</summary>
    /// <remarks>
    /// Death stops ordinary player ticks immediately, so this cannot wait for the next input frame to
    /// release a held trigger.
    /// </remarks>
    public void StopWeaponsForDeath() => _weapons?.StopForDeath();

    private void Activate(WorldInteraction interaction, TerrainData? terrain)
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
                TaskActivated?.Invoke(interaction.Task.Id);
                break;
            case InteractionAction.MountWire:
                BeginWire(interaction);
                return;
            case InteractionAction.ClimbFence:
            case InteractionAction.PickLock:
            case InteractionAction.EnterState:
                _activeInteraction = interaction;
                _interactionTick = 0;
                _interactionUseArmed = false;
                (Vector3d position, float facing) = interaction.BodyPose(_body.Position);
                _body.Position = position;
                SnapBodyForInteraction(_body, interaction.Type, terrain);
                _interactionFacing = facing;
                _body.Yaw = -facing;
                _fenceExitPosition = interaction.Type == InteractionActivateType.Fence
                    ? interaction.FenceExitPoint(position)
                    : position;
                interaction.BeginUse();
                break;
        }

        _nearInteraction = null;
    }

    internal static void SnapBodyForInteraction(
        PlayerBody body,
        InteractionActivateType type,
        TerrainData? terrain)
    {
        if (type is InteractionActivateType.Door
            or InteractionActivateType.Cabinet
            or InteractionActivateType.Terminal
            or InteractionActivateType.Generic
            or InteractionActivateType.PlaceExplosive)
        {
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

    private void TickInteraction(InputState input, TerrainData? terrain)
    {
        WorldInteraction interaction = _activeInteraction!;
        bool use = _input.IsSet(PlayerInputSlot.Activate);
        if (!use)
        {
            _interactionUseArmed = true;
        }

        if (interaction.Type == InteractionActivateType.StationaryGun)
        {
            if (use && _interactionUseArmed)
            {
                EndInteraction(completed: false, terrain);
            }
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

    private void TickWire(TerrainData? terrain, bool use)
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

    private void TickFence(WorldInteraction fence, TerrainData? terrain)
    {
        // State 41 plants the task root at the authored fence mount and lets animation 171 carry the
        // visible body over it. Its own duration accessor returns six seconds (0x440270), not the
        // one-second parabolic placeholder this replaces.
        if (_interactionTick >= fence.DurationTicks)
        {
            _body.Position = _fenceExitPosition;
            EndInteraction(completed: false, terrain);
        }
    }

    private void EndInteraction(bool completed, TerrainData? terrain)
    {
        WorldInteraction interaction = _activeInteraction!;
        if (completed)
        {
            interaction.CompleteUse();
            TaskActivated?.Invoke(interaction.Task.Id);
        }
        else
        {
            interaction.CancelUse();
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
        TerrainData? terrain)
    {
        if (type == InteractionActivateType.Wire)
        {
            // Wire cleanup enters the ordinary fall state. Snapping to the terrain here is what made a
            // release/end look like an instant teleport to ground instead of a drop from the cable.
            body.EndLadderTraversal(terrain, placeOnGround: false);
        }
        else if (type == InteractionActivateType.Fence)
        {
            // The authored traversal ends above its far-side landing point; settle that endpoint onto
            // the nearest terrain or placed-model surface before ordinary locomotion resumes.
            body.PlaceOnGround(terrain);
        }
        // Retail self-driving interaction states leave the root exactly where their initializer put it.
        // Re-running spawn placement here searches up to half a kilometre downward and can move the
        // player onto an unrelated surface beneath the building when Use is released.
    }

    private static bool DoorNeedsPicking(WorldInteraction interaction)
    {
        return interaction.Type == InteractionActivateType.Door && interaction.IsLocked;
    }

    private static int ActiveAnimationParameter(DeclaredTask task) =>
        task.TryGetValue("Activate Anim", out object? raw) && raw is int animation ? animation : -1;

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

        _prevTickEye = HasViewTickPair ? _tickEye : view.Eye;
        _tickEye = view.Eye;

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
        if (HumanAttachPoints.FirstPersonWeapon < unrotated.BoneCount)
        {
            Vector3f position = unrotated.Positions[HumanAttachPoints.FirstPersonWeapon];
            Matrix3f rotation = unrotated.Rotations[HumanAttachPoints.FirstPersonWeapon];
            _prevTickWeaponBonePosition = _hasWeaponBonePair ? _tickWeaponBonePosition : position;
            _prevTickWeaponBoneRotation = _hasWeaponBonePair ? _tickWeaponBoneRotation : rotation;
            _tickWeaponBonePosition = position;
            _tickWeaponBoneRotation = rotation;
            _hasWeaponBonePair = true;
        }

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
    public Matrix3f SmoothedLookRotation(float alpha) =>
        Camera3D.LookRotation(_body.Yaw, SmoothedAimPitch(alpha));

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

    /// <summary>The rig's transform and held-weapon attachment for a frame between two ticks.</summary>
    /// <param name="alpha">How far the frame sits between the last two ticks, 0 to 1.</param>
    /// <returns>
    /// The rig origin in engine units, its orientation, and the weapon attachment when the bone
    /// exists.
    /// </returns>
    /// <remarks>
    /// The tick-stepped halves — the eye the body carried, the wall pullback, the clip's hold offset,
    /// the weapon bone, and the sway angles that ARE the weapon-change animation — are interpolated between the pair;
    /// the LOOK is this frame's own, because the mouse already runs per frame and the arms must stay
    /// welded to it. At <paramref name="alpha"/> 1 this is the rig's un-smoothed transform exactly.
    /// </remarks>
    public (Vector3d OriginUnits, Matrix3f Orientation, BoneAttachment? HeldWeapon) SmoothedRig(float alpha)
    {
        if (_view is not { } view || !HasViewTickPair)
        {
            return (_view?.Position ?? default, _view?.Orientation ?? Matrix3f.Identity, _view?.HeldWeapon);
        }

        // The same composition SyncView commits — eye orientation times sway — with the tick-stepped
        // angles blended: the sway pair (the weapon-change animation) and the recoil kick inside the
        // aim pitch. The mouse-driven part of the pitch and the yaw are this frame's own.
        Matrix3f eyeLook = Matrix3f.FromEngineEulerAngles(SmoothedAimPitch(alpha), 0f, -_body.Yaw);
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

        BoneAttachment? weapon = null;
        if (_hasWeaponBonePair)
        {
            Vector3f bone = look.Transposed().Transform(
                PresentationBlend.Lerp(_prevTickWeaponBonePosition, _tickWeaponBonePosition, alpha));
            weapon = new BoneAttachment(
                new Vector3d(origin.X + bone.X, origin.Y + bone.Y, origin.Z + bone.Z),
                look * PresentationBlend.InterpolateRotation(
                    _prevTickWeaponBoneRotation, _tickWeaponBoneRotation, alpha));
        }

        return (origin, look, weapon);
    }

    private WorldRayCast ViewModelWalls(TerrainData? terrain)
    {
        WorldTraceCast trace = (_walls, terrain) switch
        {
            ({ } walls, { } ground) => WorldTrace.Nearest(walls.Tracer(), TerrainRayTrace.Caster(ground)),
            ({ } walls, null) => walls.Tracer(),
            (null, { } ground) => TerrainRayTrace.Caster(ground),
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
    }

    // The nearest ladder that is close enough, faced and within height to mount.
    private void UpdateNearLadder()
    {
        _nearLadder = null;
        _nearLadderAtTop = false;
        _nearLadderAngle = double.MaxValue;
        double best = double.MaxValue;

        foreach (LadderPlacement ladder in _ladders)
        {
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
    public LadderStepResult DecideLadderStep(bool up, bool down, bool slide, TerrainData? terrain)
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

    /// <summary>Commits the rung whose clip has just completed, returning the climb to a boundary.</summary>
    public void CompleteLadderStep()
    {
        _climb.CompleteStep();
        _body.Position = _climb.Position;
    }

    /// <summary>Consumes animation 170's endpoint and resolves the roof when state 39 detaches.</summary>
    /// <param name="terrain">The level terrain used by the final ground placement.</param>
    public void CompleteLadderTopTransition(TerrainData? terrain)
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
    public bool TickLadderSlide(Vector3d rootDelta, TerrainData? terrain)
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

        if (_weapons.InHand is { } held && _weapons.RequestedAction is { } action)
        {
            PlayWeaponAction(held.Weapon, action, _weapons.RequestedFireVariant);
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
                zoom = _sightZoom.Tick(
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

        // The barrel is the view's +Y axis and the cone opens across its +X and +Z -- 0x462E50 picks
        // exactly the middle column of the view's 3x3 out for the direction.
        Matrix3f m = _view.EyeOrientation;
        Vector3f forward = new(m.M12, m.M22, m.M32);
        Vector3f right = new(m.M11, m.M21, m.M31);
        Vector3f up = new(m.M13, m.M23, m.M33);

        // A scoped rifle is braced: 0x472E50 multiplies the round's sampled kick by 0.05 before the
        // accumulators see it, and this is the same factor at the same point.
        float recoilScale = _dragunovScope.IsScoped ? DragunovScope.ScopedRecoilFactor : 1f;
        if (!_weapons.ResolveFire(_view.Eye, forward, right, up, recoilScale))
        {
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

            foreach (GunProjectile shot in _weapons.Shots)
            {
                _grenades.Spawn(shot.Origin, shot.Direction, throwSpeed, explosion);
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
            else
            {
                _impacts.Add(Trace(shot, damageFactor, category));
            }
        }
    }

    /// <summary>Advances the ballistic rounds and lands whatever arrived this tick.</summary>
    /// <remarks>
    /// The impact side-effects are exactly <see cref="Trace"/>'s, applied on the tick the round
    /// arrives rather than the tick it was fired: the hit counters, the AI's ground-impact notice and
    /// the decal. Damage needs nothing here — the <see cref="Humans"/> delegate applied it during the
    /// cast, the same way it does for a ray.
    /// </remarks>
    private void TickBallistics()
    {
        // The grenades fly every tick regardless of the tracer option -- they are the retail
        // behaviour, not a remaster one. Detonations are drained by the game, which owns the
        // effects and the blast's victims.
        _grenades.Tick(GrenadeWalls ?? _body.Walls);

        if (_ballistics.Rounds.Count == 0)
        {
            return;
        }

        WorldRayCast? walls = _body.Walls;
        _ballistics.Tick(
            walls,
            Humans,
            GameplayCVars.BallisticsGravity.Value,
            GameplayCVars.BallisticsDrag.Value);

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

                // The surface decides what the hit looks like, out of the material its own face names.
                if (_materials is { } materials)
                {
                    Marks.TryAdd(impact.Wall, materials);
                }
            }

            _impacts.Add(new GunImpact(
                impact.Position,
                impact.StruckHuman || impact.StruckWall,
                impact.RangeInUnits));
        }
    }

    // One cast, not the full loop. 0x47B750 keeps casting while range is left, penetrating and
    // ricocheting; that needs the surface properties the collision index does not carry yet, so this
    // stops at the first thing hit. What IS retail is where the ray starts, which way it goes and how
    // long it is -- all three come out of the weapon record. Recorded in TODO.md.
    private GunImpact Trace(GunProjectile shot, float damageFactor, WeaponCategory category)
    {
        Vector3d direction = new(shot.Direction.X, shot.Direction.Y, shot.Direction.Z);
        Vector3d end = shot.Origin + (direction * shot.RangeInUnits);

        WorldTraceHit richHit = default;
        bool struckWall = _walls is not null
            && _walls.TryTrace(
                new WorldTraceRequest(shot.Origin, end, WorldTraceMask.World),
                out richHit);
        WorldRayHit hit = struckWall ? richHit.RayHit : default;

        // Both, then the nearer. A guard in front of a wall takes the round and the wall stays clean;
        // one behind it does not, and the wall is marked.
        HumanHit human = default;
        bool struckHuman = Humans is { } humans
            && humans(shot.Origin, end, damageFactor, category, out human)
            && (!struckWall || human.Distance < hit.Distance);

        if (struckHuman)
        {
            HumanHits++;
            LastHumanZone = human.Zone;
            return new GunImpact(human.Position, true, shot.RangeInUnits);
        }

        if (struckWall)
        {
            GroundImpact?.Invoke(hit.Position);

            // The surface decides what the hit looks like, out of the material its own face names.
            if (_materials is { } materials)
            {
                Marks.TryAdd(hit, materials);
            }

            return new GunImpact(hit.Position, true, shot.RangeInUnits, richHit.Owner);
        }

        return new GunImpact(end, false, shot.RangeInUnits);
    }

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
