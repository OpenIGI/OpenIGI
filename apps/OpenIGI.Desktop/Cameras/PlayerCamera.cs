using OpenIGI.Core.Numerics;
using OpenIGI.Formats.Animation;
using OpenIGI.Game.Player;
using OpenIGI.Game.Weapons;
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
    /// <summary>How far the mouse turns the view, in radians per unit of motion.</summary>
    public const float MouseSensitivity = 0.0035f;

    /// <summary>How fast the arrow keys turn, in radians a second.</summary>
    public const float KeyTurnRate = 1.8f;

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

    /// <summary>How far past the head the get-off step places the player, onto the ledge, in world units.</summary>
    public const double GetOffReachUnits = 2048.0;

    private readonly PlayerBody _body = new();
    private readonly PlayerInputState _input = new();
    private readonly IgiRandom _random = new();
    private readonly List<GunImpact> _impacts = [];
    private readonly HumanLadderClimb _climb = new();
    private HumanLocomotionAnimator? _animator;
    private ObjectCollisionIndex? _walls;
    private string _lastBlockLabel = string.Empty;
    private HumanView? _view;
    private PlayerWeapons? _weapons;
    private WeaponType? _drawnWeapon;
    private GameMaterialRegistry? _materials;
    private bool _wasCapturing;
    private IReadOnlyList<LadderPlacement> _ladders = [];
    private LadderPlacement? _nearLadder;
    private LadderPlacement? _mountedLadder;
    private bool _nearLadderAtTop;

    // The first-person rig's last two tick states, captured at the end of Tick() — what the
    // presentation draws between when r_smooth is on. All of it is kept in bone/eye space WITHOUT the
    // look folded in, so the per-frame mouse orientation composes on top and the weapon stays welded
    // to the view however fast the frames come.
    private Vector3d _prevTickEye;
    private Vector3d _tickEye;
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

    /// <summary>Gets the body being driven.</summary>
    public PlayerBody Body => _body;

    /// <summary>Gets the first-person rig the held weapon hangs off, once one has been given.</summary>
    public HumanView? View => _view;

    /// <summary>Gets the weapon machine, once the player has been given a weapon cycle.</summary>
    public PlayerWeapons? Weapons => _weapons;

    /// <summary>Gets what the last tick's rounds hit.</summary>
    /// <remarks>Empty on every tick nothing was fired, which is most of them.</remarks>
    public IReadOnlyList<GunImpact> Impacts => _impacts;

    /// <summary>Gets the marks rounds have left on the world.</summary>
    public BulletMarks Marks { get; } = new();

    /// <summary>Gets or sets what offers this player's rounds to the humans in the world.</summary>
    /// <remarks>
    /// Without it rounds stop at walls and hit nobody. A round takes whichever of the two is
    /// <b>nearer</b>, so a guard standing in front of a wall is hit and the wall is not marked.
    /// </remarks>
    public HumanShot? Humans { get; set; }

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

    // Binds the rig to the body. The original does far more here (0x464BD0 eases the aim, adds the
    // weapon sway and the head bob, and pushes the eye out of walls with a multi-ray cast), and none of
    // that is built; what is transcribed is the part the attachment depends on -- the eye's transform,
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

    /// <summary>Gets whether the player is crouched.</summary>
    public bool IsCrouched => _body.IsCrouched;

    /// <summary>Gets whether the last tick's move was stopped by a wall.</summary>
    public bool TouchedWall => _body.TouchedWall;

    /// <summary>Puts the player on the ground under a point.</summary>
    /// <param name="terrain">The level's terrain.</param>
    /// <param name="at">Where to try standing, in the renderer's metres.</param>
    /// <param name="yaw">Which way to face.</param>
    /// <returns><see langword="false"/> when there is no ground under the point.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="terrain"/> is <see langword="null"/>.</exception>
    public bool Place(TerrainData terrain, Vector3d at, float yaw)
    {
        ArgumentNullException.ThrowIfNull(terrain);

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
        _body.Walls = walls.Caster();
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

    /// <summary>Gets whether the player is on a ladder.</summary>
    public bool IsOnLadder => _climb.IsOnLadder;

    /// <summary>Gets where the climbing body's feet are, in engine units.</summary>
    public Vector3d LadderFeet => _body.Position;

    /// <summary>Gets the yaw the climbing body faces — into the ladder, in radians.</summary>
    public float LadderFacing => _climb.Facing;

    /// <summary>Gets whether the climb is at a rung boundary, ready to take up/down input.</summary>
    public bool LadderAtBoundary => _climb.AtBoundary;

    /// <summary>Gets the climb animation — state 37's binding, animation 168 in the player's set.</summary>
    public static int LadderClimbAnimation => HumanLocomotionStates.For(37).AnimationId;

    /// <summary>Gets whether a ladder is in reach and the use prompt should be shown.</summary>
    public bool ShowInteractPrompt => _nearLadder is not null && !_climb.IsOnLadder;

    /// <summary>Gets the prompt to show when something is in reach.</summary>
    public string InteractPromptText => _nearLadder is not null ? "Press E to climb" : string.Empty;

    /// <summary>Puts the player at a spawn the level declared.</summary>
    /// <param name="terrain">The level's terrain.</param>
    /// <param name="spawn">The level's own <c>HumanPlayer</c> start.</param>
    /// <returns><see langword="false"/> when there is no ground under it.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="terrain"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// A spawn's position is already in the game's world units — unlike a scene placement, which the
    /// scene builder has divided into metres for the renderer — so it goes to the body untouched.
    /// </remarks>
    public bool PlaceAt(TerrainData terrain, PlayerSpawnPoint spawn)
    {
        ArgumentNullException.ThrowIfNull(terrain);

        _body.Position = spawn.Position;
        _body.Yaw = spawn.Gamma;
        _body.Pitch = 0f;
        HasViewTickPair = false;
        return _body.PlaceOnGround(terrain);
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
            // from +Y toward +X - means yaw increases.
            _body.Yaw += mouse.DeltaX * MouseSensitivity;
            _body.Pitch = Math.Clamp(
                _body.Pitch - (mouse.DeltaY * MouseSensitivity),
                -PlayerBody.PitchLimit,
                PlayerBody.PitchLimit);
        }

        KeyboardState keys = input.Keyboard;
        _body.Yaw += Axis(keys, KeyCode.Right, KeyCode.Left) * seconds * KeyTurnRate;

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
    public void Tick(InputState input, TerrainData terrain)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(terrain);

        _input.Accept(SampleFrame(input));

        // On a ladder the ordinary walk, gravity and collision do not run. The rung-by-rung climb (state
        // 37) is driven by the host once per tick through DecideLadderStep / AdvanceLadderPhase, because
        // it is coupled to the third-person body's animation; nothing to do here.
        if (_climb.IsOnLadder)
        {
            return;
        }

        // Which ladder, if any, is in reach — for the prompt and for mounting.
        UpdateNearLadder();

        KeyboardState keys = input.Keyboard;

        // The use key mounts the ladder in reach. Taken on the rising edge so holding it does not remount
        // the instant the climb ends.
        if (_nearLadder is not null && _input.IsRisingEdge(PlayerInputSlot.Activate))
        {
            MountLadder();
            return;
        }

        // The action names are the game's own, from the binding table at 0x536558: MoveUp and
        // MoveDown are forward and back - named for the screen, as the era's binding dialogs were.
        float forward = Axis(keys, KeyCode.W, KeyCode.S);
        float strafe = Axis(keys, KeyCode.D, KeyCode.A);
        bool run = keys.IsDown(KeyCode.LeftShift) || keys.IsDown(KeyCode.RightShift);
        bool crouch = keys.IsDown(KeyCode.LeftControl) || keys.IsDown(KeyCode.RightControl);

        // Jump is the rising edge, not the level, so holding it does not bounce.
        bool jump = _input.IsRisingEdge(PlayerInputSlot.Jump);

        PlayerCommand command = new(forward, strafe, 0f, 0f, run, crouch, jump);

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

        _view?.Tick();

        // Where 0x464BD0 steps it: after the state machines have read it and before the rig is placed.
        _weapons?.Sway.Advance();

        PoseChanged = true;
        SyncView();

        // 0x462E50, which HumanPlayer_Update calls after 0x464BD0 has committed the view.
        ResolveFire();

        // After the fire has (possibly) started a new clip, so the pair describes what this tick
        // actually shows.
        CaptureViewTickState();
    }

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

    /// <summary>The rig's transform and held-weapon attachment for a frame between two ticks.</summary>
    /// <param name="alpha">How far the frame sits between the last two ticks, 0 to 1.</param>
    /// <returns>
    /// The rig origin in engine units, its orientation, and the weapon attachment when the bone
    /// exists.
    /// </returns>
    /// <remarks>
    /// The tick-stepped halves — the eye the body carried, the clip's hold offset, the weapon bone,
    /// and the sway angles that ARE the weapon-change animation — are interpolated between the pair;
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
        Matrix3f look = Matrix3f.FromEngineEulerAngles(SmoothedAimPitch(alpha), 0f, -_body.Yaw)
            * Matrix3f.FromEngineEulerAngles(
                PresentationBlend.LerpAngle(_prevTickSwayPitch, _tickSwayPitch, alpha),
                0f,
                PresentationBlend.LerpAngle(_prevTickSwayYaw, _tickSwayYaw, alpha));

        Vector3d eye = PresentationBlend.Lerp(_prevTickEye, _tickEye, alpha);

        // M · v — the palette's own convention — is the transpose of the row-vector Transform.
        Vector3f held = look.Transposed().Transform(
            PresentationBlend.Lerp(_prevTickRootMotion, _tickRootMotion, alpha));
        Vector3d origin = new(eye.X + held.X, eye.Y + held.Y, eye.Z + held.Z);

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
    /// not every tick. The line names the model instance, the placing task, and where the ray met it, and
    /// is appended to <c>doorblock.log</c> beside the executable as well as written to the console.
    /// </remarks>
    private void LogWallBlockIfAny(float forward, float strafe)
    {
        if (_walls is null || !_body.TouchedWall || (forward == 0f && strafe == 0f))
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

        Console.WriteLine(line);
        try
        {
            File.AppendAllText(
                Path.Combine(AppContext.BaseDirectory, "doorblock.log"), line + Environment.NewLine);
        }
        catch (IOException)
        {
            // A diagnostic must never take the game down; a locked or unwritable log is simply skipped.
        }
    }

    // The nearest ladder that is close enough, faced and within height to mount.
    private void UpdateNearLadder()
    {
        _nearLadder = null;
        _nearLadderAtTop = false;
        double best = double.MaxValue;

        foreach (LadderPlacement ladder in _ladders)
        {
            if (!ladder.CanActivate(_body.Position, _body.Yaw, out bool atTop))
            {
                continue;
            }

            double dx = ladder.Bottom.X - _body.Position.X;
            double dy = ladder.Bottom.Y - _body.Position.Y;
            double distance = (dx * dx) + (dy * dy);
            if (distance < best)
            {
                best = distance;
                _nearLadder = ladder;
                _nearLadderAtTop = atTop;
            }
        }
    }

    // Puts the player on the ladder in reach: face it and snap onto it.
    private void MountLadder()
    {
        LadderPlacement ladder = _nearLadder!;

        // Face the LADDER'S OWN heading, not the approach direction. Verified against the running game
        // (Ladder_ClimbTick fwd=): the body's gamma is fixed by the ladder's orientation
        // (LadderPlacement.Facing = atan2(M21, M11)), so it always faces the wall square-on however you
        // walked up to it -- the approach-based angle drifted a few degrees and swung the stand-off with it.
        float facing = ladder.Facing;

        _climb.Mount(ladder.ClimbLine, facing, _nearLadderAtTop);
        _mountedLadder = ladder;
        _body.Yaw = facing;
        _body.Position = _climb.Position;
        _nearLadder = null;
        PoseChanged = true;
        SyncView();
    }

    /// <summary>Decides the next rung at a boundary — begins a rung, hands off, or holds (state 37).</summary>
    /// <param name="up">Whether up (W) is held.</param>
    /// <param name="down">Whether down (S) is held.</param>
    /// <param name="slide">Whether slide (use) is held.</param>
    /// <returns>The decision; a stepping result means the host should start the rung's clip.</returns>
    /// <remarks>Only acts at a rung boundary; a rung in progress cannot be cancelled (<c>sub_40D6B0</c>).</remarks>
    public LadderStepResult DecideLadderStep(bool up, bool down, bool slide)
    {
        LadderStepResult result = _climb.Decide(up, down, slide);
        switch (result)
        {
            case LadderStepResult.ReachedTop when _mountedLadder is { } topped:
                // Step off the head onto the ledge, forward along the body's own facing (-sin, cos).
                double fx = -Math.Sin(_body.Yaw);
                double fy = Math.Cos(_body.Yaw);
                _body.Position = new Vector3d(
                    topped.Top.X + (fx * GetOffReachUnits),
                    topped.Top.Y + (fy * GetOffReachUnits),
                    topped.Top.Z);
                _mountedLadder = null;
                break;

            case LadderStepResult.Sliding:
            case LadderStepResult.Dismounted:
                if (_mountedLadder is { } footed)
                {
                    _body.Position = footed.Bottom;
                }

                _mountedLadder = null;
                break;

            default:
                _body.Position = _climb.Position;
                break;
        }

        return result;
    }

    /// <summary>Moves the climbing body by the climb clip's root-motion delta for this tick.</summary>
    /// <param name="rootDelta">The clip's root motion, rotated into the facing frame, in world units.</param>
    /// <returns><see langword="true"/> when this move completed a rung.</returns>
    public bool MoveLadder(Vector3d rootDelta)
    {
        bool rungDone = _climb.Move(rootDelta);
        _body.Position = _climb.Position;
        return rungDone;
    }

    /// <summary>Commits the rung whose clip has just completed, returning the climb to a boundary.</summary>
    public void CompleteLadderStep()
    {
        _climb.CompleteStep();
        _body.Position = _climb.Position;
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

        if (!_weapons.ResolveFire(_view.Eye, forward, right, up))
        {
            return;
        }

        float damageFactor = _weapons.InHand?.Weapon.DamageFactor ?? 0f;

        // The category travels with the round because it decides how a body this round kills falls:
        // 0x45D3C0 records a shotgun kill as a throw and everything else as a crumple, and the death
        // state picks a different family of clips for each.
        WeaponCategory category = _weapons.InHand?.Weapon.Category ?? WeaponCategory.Gun;
        foreach (GunProjectile shot in _weapons.Shots)
        {
            _impacts.Add(Trace(shot, damageFactor, category));
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

        WorldRayHit hit = default;
        bool struckWall = _body.Walls is { } cast && cast(shot.Origin, end, out hit);

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
            // The surface decides what the hit looks like, out of the material its own face names.
            if (_materials is { } materials)
            {
                Marks.TryAdd(hit, materials);
            }

            return new GunImpact(hit.Position, true, shot.RangeInUnits);
        }

        return new GunImpact(end, false, shot.RangeInUnits);
    }

    // 0x45E060, for the slots this port can supply. Every action slot carries the MASKED BIT VALUE and
    // never 1, because the original stores `mask & bit` -- a consumer that compared against 1 would
    // read almost every action as released.
    private static PlayerInputFrame SampleFrame(InputState input)
    {
        PlayerInputFrame frame = default;
        KeyboardState keys = input.Keyboard;

        Set(ref frame, PlayerInputSlot.MoveLeft, keys.IsDown(KeyCode.A));
        Set(ref frame, PlayerInputSlot.MoveRight, keys.IsDown(KeyCode.D));
        Set(ref frame, PlayerInputSlot.MoveUp, keys.IsDown(KeyCode.W));
        Set(ref frame, PlayerInputSlot.MoveDown, keys.IsDown(KeyCode.S));
        Set(ref frame, PlayerInputSlot.Fire, input.Mouse.IsDown(MouseButton.Left));
        Set(ref frame, PlayerInputSlot.AlternateFire, input.Mouse.IsDown(MouseButton.Right));
        Set(ref frame, PlayerInputSlot.Reload, keys.IsDown(KeyCode.R));
        Set(ref frame, PlayerInputSlot.Activate, keys.IsDown(KeyCode.E));
        Set(ref frame, PlayerInputSlot.Crouch, keys.IsDown(KeyCode.LeftControl) || keys.IsDown(KeyCode.RightControl));
        Set(ref frame, PlayerInputSlot.Jump, keys.IsDown(KeyCode.Space));
        Set(ref frame, PlayerInputSlot.NextWeapon, keys.IsDown(KeyCode.Q));
        Set(ref frame, PlayerInputSlot.PreviousWeapon, keys.IsDown(KeyCode.Z));

        // 1 to 9, as the original binds them: nine weapon CATEGORIES, not nine weapons. Pressing the
        // same one again steps to the next weapon of that category you are carrying.
        for (int i = 0; i < HumanPlayerWeaponCategory.CategoryCount; i++)
        {
            Set(
                ref frame,
                PlayerInputSlot.WeaponCategory1 + i,
                keys.IsDown((KeyCode)((int)KeyCode.D1 + i)));
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

    private static float Axis(KeyboardState keys, KeyCode positive, KeyCode negative)
    {
        float value = 0f;
        if (keys.IsDown(positive))
        {
            value += 1f;
        }

        if (keys.IsDown(negative))
        {
            value -= 1f;
        }

        return value;
    }
}
