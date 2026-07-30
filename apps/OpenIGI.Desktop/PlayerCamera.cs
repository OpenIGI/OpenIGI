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

    private readonly PlayerBody _body = new();
    private readonly PlayerInputState _input = new();
    private readonly IgiRandom _random = new();
    private readonly List<GunImpact> _impacts = [];
    private HumanLocomotionAnimator? _animator;
    private HumanView? _view;
    private PlayerWeapons? _weapons;
    private WeaponType? _drawnWeapon;
    private GameMaterialRegistry? _materials;
    private bool _wasCapturing;

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

        KeyboardState keys = input.Keyboard;

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

        _view?.Tick();

        // Where 0x464BD0 steps it: after the state machines have read it and before the rig is placed.
        _weapons?.Sway.Advance();

        PoseChanged = true;
        SyncView();

        // 0x462E50, which HumanPlayer_Update calls after 0x464BD0 has committed the view.
        ResolveFire();
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
