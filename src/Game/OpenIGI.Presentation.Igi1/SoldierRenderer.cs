using System.Runtime.InteropServices;
using OpenIGI.Presentation.Cameras;
using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Resources;
using OpenIGI.Engine.Tasks;
using OpenIGI.Formats.Ai;
using OpenIGI.Formats.Animation;
using OpenIGI.Formats.Textures;
using OpenIGI.Formats.World;
using OpenIGI.Game.Ai;
using OpenIGI.Game.Physics;
using OpenIGI.Game.Player;
using OpenIGI.Game.Weapons;
using OpenIGI.Game.World;
using OpenIGI.Game;
using OpenIGI.Presentation.Rendering;
using OpenIGI.Rendering;
using OpenIGI.Scripting.Qvm;
using OpenIGI.Scripting.Runtime;
using OpenIGI.Game.Igi1.Ai;
using OpenIGI.Game.Igi1.Player;
using OpenIGI.Game.Igi1.World;
using OpenIGI.Game.Igi1.Physics;

namespace OpenIGI.Presentation.Igi1;

/// <summary>Draws a level's placed soldiers — each the model, skeleton and stand animation its own task declares.</summary>
/// <remarks>
/// <para>
/// The spawn is <c>0x456E50</c> + <c>0x45F890</c>: the bone object
/// on the declared <c>Bone Heirachy</c> skeleton, the declared <c>Stand Animation</c> (or 2) on bone 0,
/// the ordinary <c>Model</c> parameter resolved by name, and the facing a pure <c>Z(Gamma)</c>. The body
/// draws through <c>Human</c>'s ordinary render handler under the <b>world</b> projection —
/// <c>0x4D9620</c> leaves <c>+212</c> at 1, so <c>0x49F700</c>'s view-model block never rebuilds for it.
/// </para>
/// <para>
/// Soldiers sharing a <c>(model, skeleton, stand animation)</c> triple stand in the same pose, so the
/// CPU skin runs once per distinct triple and each soldier draws that mesh at its own position and
/// facing. The facing can live in the per-soldier model matrix because the pose is skinned with an
/// identity root — the root rotation only ever left-multiplies every bone (see
/// <see cref="IViewModelPresenter"/>).
/// </para>
/// </remarks>
public sealed class SoldierRenderer : IDisposable
{
    private const string LogCategory = "Soldier";
    private const string TraceLogCategory = LogCategory + ".Trace";

    // The one staged actor whose placement is traced each second, named by soldier id in
    // OPENIGI_TRACE_CUTSCENE_ACTOR. Read once: the check runs for every soldier on every tick.
    private static readonly string? TracedCutsceneActor = Environment.GetEnvironmentVariable("OPENIGI_TRACE_CUTSCENE_ACTOR");

    /// <summary>How far above the declared position the ground probe starts — 0.5 m.</summary>
    private const double ProbeAboveInUnits = 2048.0;

    /// <summary>How far below the declared position the probe reaches — 2 m, the sliding step-down budget.</summary>
    // The terrain query itself only reaches this far down (TerrainHeightQuery.SearchDepthUnits), so
    // widening the acceptance window to the same depth settles every surface the query can find.
    // Level 4 authors several soldiers 2.3-3.1m above the terrain (larger than the old 2m window),
    // which otherwise float; a placed surface the ray cast finds (a roof, a vehicle) is higher than the
    // terrain and wins, so a soldier standing on one is not pulled down. A character far above the
    // terrain (a rooftop watcher) has no surface within the search depth and is left alone.
    private const double ProbeBelowInUnits = 16385.0;

    /// <summary>
    /// Retail IGI.exe Human_GroundCollision raises a standing human's task position by 1638.4
    /// engine units before subtracting the animated root height and querying the supporting surface.
    /// </summary>
    private const double HumanGroundCheckLiftInUnits = 1638.4;

    /// <summary>
    /// Retail Human_GroundCollision supplies -40960.0 as the first distance to
    /// World_GetNextZSpecificMesh. This is deliberately deeper than the normal two-metre
    /// placement probe so a staged cutscene actor can acquire a vehicle below its task origin.
    /// </summary>
    private const double HumanGroundProbeDownInUnits = 40960.0;

    private readonly IGraphicsDevice _device;

    /// <summary>Scratch for the selection glow's subset ranges, so outlining allocates nothing.</summary>
    private readonly List<MeshDrawRange> _outlineRanges = [];
    private readonly ResourceRegistry _resources;
    private readonly MagicObjectRegistry? _magic;
    private readonly IOpenIgiLogger _logger;
    private TexTextureCache? _textureCache;
    private readonly List<Group> _groups = [];
    private readonly List<HeldWeapon> _weapons = [];
    private readonly Dictionary<string, HeldWeapon> _heldWeaponsByModel = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Builds a presenter for a weapon a soldier holds, or null when the host supplies none.</summary>
    /// <remarks>
    /// A factory rather than a type, because the renderer that draws a held weapon is title content and
    /// lives in the title's own project: naming it here would put I.G.I. 1 content back in
    /// <c>OpenIGI.Presentation</c>, which is what this class is waiting to leave. Null is a soldier that
    /// draws no weapon, which is what a test harness and a backdrop both want.
    /// </remarks>
    private readonly Func<IHeldWeaponPresenter>? _heldWeapons;
    private readonly Dictionary<SoldierView, HumanHealth> _health = [];
    private readonly Dictionary<SoldierView, HumanRecoil> _recoil = [];
    // FUN_004D9620 writes Human+0xA0 from FUN_004D0950(model), which reads the authored model bound
    // at model+0x78. This is the exact value used by the retail detector; the derived vertex radius is
    // not a substitute because it is materially smaller for the retail soldier models.
    private readonly Dictionary<SoldierView, float> _detectorMarkerScaleMetres = [];
    private readonly IgiRandom _aiRandom = new();

    // Each soldier's combat row, resolved from its own AIType at the difficulty in force. It is what
    // gives a guard its maximum health -- sub_406F70 writes the row's "Damage scale" over Human+600,
    // so a guard is worth 0.3 of a person on easy and 0.8 on hard, not the constructor's 1.0.
    private readonly Dictionary<SoldierView, AiCombatParameters> _combat = [];

    // A dead soldier cannot stay in a shared group: the group is skinned from ONE representative's pose, so
    // a corpse there would keep standing in the group's idle animation. Each gets its own single-member
    // group instead, ticked and re-skinned from its own rig, which is what lets it fall.
    private readonly Dictionary<SoldierView, AiSoldier> _brains = [];
    private readonly Dictionary<int, AiSoldier> _brainsByRuntimeId = [];
    private readonly Dictionary<GuardGeneratorTask, SoldierView> _guardGenerators = [];
    private LevelExpressionRuntime? _guardExpressions;
    private readonly Dictionary<AiStationaryGun, HeldWeapon?> _stationaryWeaponVisuals = [];

    // The weapon models the vehicles' guns carry, by model, and where the runtime poses each gun (0x470050).
    private readonly Dictionary<string, HeldWeapon?> _vehicleWeaponVisuals = new(StringComparer.OrdinalIgnoreCase);
    private Func<IEnumerable<VehicleGunVisual>>? _vehicleGuns;
    private readonly Dictionary<int, string> _lastGunnerRuntimeReports = [];
    private readonly HashSet<int> _reportedStationaryVisuals = [];
    private ScriptVarRegistry? _scriptVariables;
    private readonly EnemyDetectorOverlay _binocularOverlay = new();

    // The soldiers the last binocular scan drew a contact for, and the team colour their marker uses.
    // Kept so the remastered optics can glow exactly those bodies through cover without re-deriving the
    // detector's own visibility, keyed by view so the outline pass can find each one's mesh and group.
    private readonly Dictionary<SoldierView, BinocularMarkerColor> _binocularDetected = [];

    // LOS is a simulation fact, not a render fact. Cache it for the fixed tick so a second render
    // of the same simulation state does not cast the same three rays again.
    private readonly Dictionary<SoldierView, bool> _binocularVisibility = [];
    private int _binocularVisibilityTick = int.MinValue;

    // One prepared rig per skeleton, shared by every corpse wearing it. Building it walks the whole bone
    // graph twice, so it must not happen per death -- and it holds no live state, so it need not.
    private readonly Dictionary<BoneAnimationSet, RagdollSkeleton> _ragdollRigs = [];
    private RagdollWorld? _ragdollWorld;

    private IGroundHeightField? _terrain;
    private WorldRayCast? _cast;
    private WorldRayCast? _sight;
    private SoldierRound? _onRound;
    private string _lastCombatReport = string.Empty;
    private readonly HashSet<SoldierView> _combatClipMissing = [];

    // The alarm chain: the level-wide activation claims (dword_5BDA28), the tasks that have actually
    // been pressed, and one AlarmControl state (0x4408A0's update) per control id the soldiers name.
    // The control turns on when any soldier's named trigger has been pressed -- a recorded stand-in
    // for the control's two authored condition expressions, which the port does not evaluate yet.
    private readonly AiActivationClaims _activationClaims = new();
    private readonly HashSet<int> _activatedTasks = [];
    private AiAlarmControlRegistry? _alarmControls;
    private AiCombatTarget _playerTarget = new("HumanPlayer");
    private int _playerTeam;

    // One combat-target record per opposing soldier, created lazily as each is first engaged. The
    // friendly-vs-guard gate fight needs a real enemy record to aim at and fight, not just the player:
    // per-brain enemy selection swaps the default target onto one of these when an opposing soldier is
    // nearer than any hostile player, so Harrison's team at the gate trades fire with the guards.
    private readonly Dictionary<SoldierView, AiCombatTarget> _npcTargets = [];
    // Remembers which opposing NPC each brain was fighting. A dead NPC disappears from the next
    // nearest-target sweep; keeping this identity for one handoff lets the renderer tell AiSoldier
    // that its current target died instead of accidentally reporting the still-alive player.
    private WorldInteractionRegistry? _interactions;
    private Action<int>? _aiTaskActivator;
    private Action<WeaponType, Vector3d, Matrix3f, Vector3d>? _weaponDrop;
    private CollisionTriangleGather? _dropTriangles;
    private WorldRayCast? _dropTrace;
    private Action<string, Vector3d, bool>? _scriptSound;
    private Action<int, bool, WeaponType?, Vector3d>? _weaponSound;
    private GunPickupRegistry? _droppedWeaponPickups;
    private GameTask? _levelRoot;

    // The level's doors: ticked here (their update runs on the task pass, before the AIs), queried by
    // each walker's door hop, and switched out of the collision index while they stand open.
    private DoorRegistry? _doors;

    // The level walk this renderer's tasks update from, once AttachTo has put them in it; null when standalone.
    private LevelUpdateWalker? _walker;

    // Where the player is, read by each soldier as it updates (BeginTick).
    private Func<SoldierEnemy>? _enemy;

    // The bodies that have updated this tick, so none updates twice and EndTick can find any the walk missed.
    private readonly HashSet<SoldierView> _ticked = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<GameTask> _unwalkedSoldiers = new(ReferenceEqualityComparer.Instance);
    private bool _collectDiagnostics;
    private bool _alarmWasOn;
    private ElevatorRegistry? _elevators;
    private ObjectCollisionIndex? _solids;

    /// <summary>Gets whether any authored alarm control is sounding on the current simulation tick.</summary>
    /// <remarks>Used by placed security cameras, whose alarm loop follows the mission-wide alarm state.</remarks>
    public bool AnyAlarmOn => _alarmControls?.AnyOn ?? false;

    /// <summary>Gives the renderer the level's doors and the index their collision is switched in.</summary>
    /// <param name="doors">The door registry, or <see langword="null"/> for a level with none.</param>
    /// <param name="solids">The placed-object collision index.</param>
    public void SetDoors(DoorRegistry? doors, ObjectCollisionIndex? solids)
    {
        _doors = doors;
        _solids = solids;

        // Human_GroundCollision runs during retail task registration, before the first CutScene tick.
        // That is when Mission 1 actor 1666 acquires train car 1888 (and actor 1505 the helicopter),
        // preserving the authored parent-local origin through every later shot. Waiting for the visible
        // landing produces the right vehicle but a permanently shifted local offset inside the wagon.
        // Moving vehicle placements receive their derived transforms immediately before the first
        // Soldier tick. Ground registration must wait for that publication; probing the build-time
        // placement here can mistake a nearby dormant vehicle for the actor's support.
    }

    /// <summary>Gives the renderer the level's moving elevators.</summary>
    /// <param name="elevators">The elevator registry, or <see langword="null"/>.</param>
    public void SetElevators(ElevatorRegistry? elevators) => _elevators = elevators;

    /// <summary>Publishes a player-used task to alarm and AI activation conditions.</summary>
    public void ActivateTask(int taskId) => _activatedTasks.Add(taskId);

    /// <summary>Connects an AI's input-channel activation to the live world interaction task.</summary>
    public void SetAiTaskActivator(Action<int>? activator) => _aiTaskActivator = activator;

    /// <summary>Connects gunner AIs to the level's live StationaryGun tasks.</summary>
    public void UseInteractions(WorldInteractionRegistry? interactions) => _interactions = interactions;
    /// <summary>Sounds a soldier's death cry, at the position the body fell from.</summary>
    /// <param name="report">The sink, or <see langword="null"/> to make deaths silent.</param>
    /// <remarks>
    /// A callback rather than a <c>SoundSystem</c> reference, for the reason every other seam on this
    /// class is one: the renderer knows when a soldier dies and should not also know how a sound is
    /// played. The argument is the body's position in <b>engine units</b>.
    /// </remarks>
    public void SetDeathSound(Action<Vector3d>? report) => _deathSound = report;

    /// <summary>Publishes a gun when a dead soldier's weapon becomes a world pickup.</summary>
    public void SetWeaponDropSink(Action<WeaponType, Vector3d, Matrix3f, Vector3d>? report) => _weaponDrop = report;

    /// <summary>Gives dropped weapons the world they collide with.</summary>
    /// <param name="triangles">The solid triangles near a box, terrain included, for the realistic body.</param>
    /// <param name="trace">A cast against everything solid, terrain included, for the original's drop probe.</param>
    public void SetDroppedWeaponWorld(CollisionTriangleGather? triangles, WorldRayCast? trace)
    {
        _dropTriangles = triangles;
        _dropTrace = trace;
        _ragdollWorld = null;
    }

    /// <summary>Gives dropped weapons a collision world after they leave a dead soldier.</summary>
    public void UseDroppedWeaponPickups(GunPickupRegistry? pickups) => _droppedWeaponPickups = pickups;

    /// <summary>Sounds a soldier's footstep, already resolved against the ground it landed on.</summary>
    /// <param name="report">The sink, or <see langword="null"/> to make soldiers walk silently.</param>
    public void SetFootstepSound(Action<Footstep>? report) => _footstepSound = report;

    /// <summary>Gives the renderer the material table a footstep picks its sound from.</summary>
    /// <param name="materials">The level's materials, or <see langword="null"/> for none.</param>
    public void UseMaterials(GameMaterialRegistry? materials) => _materials = materials;

    /// <summary>The game material the level's terrain is made of, or null to fall back to <c>Ground</c>.</summary>
    /// <remarks>Guards walk on the same ground the player does; see the player camera's copy.</remarks>
    public int? TerrainGameMaterialIndex { get; set; }

    /// <summary>Sounds a soldier's grunt at being hit, rate-limited the way the original is.</summary>
    /// <param name="report">The sink, or <see langword="null"/> to make hits silent.</param>
    /// <remarks>The argument is the body's position in engine units.</remarks>
    public void SetHitSound(Action<string, int, Vector3d>? report) => _hitSound = report;

    /// <summary>Reports a close-combat strike against the player, already probability-gated.</summary>
    /// <param name="report">The sink, or <see langword="null"/> to drop strikes.</param>
    /// <remarks>
    /// Arguments are the attacker's position in engine units and the row's damage factor. The
    /// mission owns the player's health, so it applies the hit; the renderer only reports it.
    /// Strikes against non-player targets are consumed and discarded at the tick site: NPC-on-NPC
    /// melee has no victim pipeline yet (issue #235 follow-up).
    /// </remarks>
    public void SetMeleeStrikeSink(Action<Vector3d, float>? report) => _meleeStrikeSink = report;

    /// <summary>Supplies the authored animation triggers from <c>animtrigger/animtrigger.qvm</c>.</summary>
    public void SetAnimationTriggers(AnimTriggerRegistry? triggers) => _animationTriggers = triggers;

    /// <summary>Plays a definition requested by <c>AIAction_PlaySound</c>.</summary>
    public void SetScriptSound(Action<string, Vector3d, bool>? report) => _scriptSound = report;

    /// <summary>Reports every AI weapon tick, including quiet ticks needed to end automatic loops.</summary>
    public void SetWeaponSound(Action<int, bool, WeaponType?, Vector3d>? report) => _weaponSound = report;

    /// <summary>Sets how long a struck soldier stays quiet for, in ticks.</summary>
    /// <param name="cooldown">Draws the gap; see <c>HumanVariantSound.HitCooldownTicks</c>.</param>
    public void SetHitCooldown(Func<int>? cooldown) => _hitCooldown = cooldown;

    /// <summary>Sets where the player's delta-translation scale is read from — <c>humanplayer.qvm</c> argument 0.</summary>
    /// <param name="scale">Reads the scale; <see langword="null"/> uses the retail default.</param>
    /// <remarks>Ekk moves by it too: see <see cref="HumanSoldierClass.ScalesEkkRootMotion"/>.</remarks>
    public void SetPlayerDeltaTranslationScale(Func<float>? scale) => _playerDeltaTranslationScale = scale;

    /// <summary>Plays a struck soldier's grunt unless it is still inside its cooldown.</summary>
    private void ReportHitSound(
        SoldierView view,
        string prefix = HumanVariantSound.AiHitPrefix,
        int variants = HumanVariantSound.AiHitVariants)
    {
        if (_hitSound is null || _hitCooldown is null)
        {
            return;
        }

        // now >= human[529], then human[529] = now + 30..60. Held per soldier, as the original holds
        // it per human, so one guard's grunt does not silence another's.
        if (_hitQuietUntil.TryGetValue(view, out int quietUntil) && _tick < quietUntil)
        {
            return;
        }

        _hitQuietUntil[view] = _tick + _hitCooldown();
        _hitSound(prefix, variants, view.Position);
    }

    /// <summary>Resolves what a soldier's step landed on and hands the footstep out.</summary>
    /// <remarks>
    /// The material comes from its own short downward cast rather than from the ground query that moved
    /// the body — the original probes separately, from 0.05 m above the ground point to 0.5 m below it,
    /// which is short enough not to punch through a floor into whatever is beneath it.
    /// </remarks>
    private void ReportFootstep(SoldierView view)
    {
        if (_footstepSound is null || _materials is null || _cast is null)
        {
            return;
        }

        // The probe answers only for OBJECTS -- the original asks the hit task for its material, and
        // open terrain is no task in this port. A step that finds nothing solid landed on the ground,
        // so it takes the Ground row; see FootstepSounds.TerrainMaterialName.
        (Vector3d probeFrom, Vector3d probeTo) = FootstepSounds.Probe(view.Position);
        Vector3d where = view.Position;
        GameMaterial? material;

        // As the player: a face that names no walk sound is not the floor, so keep looking down.
        if (_cast(probeFrom, probeTo, out WorldRayHit hit)
            && _materials.TryGet(hit.Material, out GameMaterial struck)
            && FootstepSounds.NamesAWalk(struck))
        {
            material = struck;
            where = hit.Position;
        }
        else if (TerrainGameMaterialIndex is int terrainRow && _materials.TryGet(terrainRow, out GameMaterial declared))
        {
            material = declared;
        }
        else if (!_materials.TryGetByName(FootstepSounds.TerrainMaterialName, out GameMaterial ground))
        {
            return;
        }
        else
        {
            material = ground;
        }

        // One cursor per soldier, so two guards walking abreast do not step in unison.
        _footCursors.TryGetValue(view, out int cursor);
        string sound = FootstepSounds.Next(material, ref cursor, out int slot);
        _footCursors[view] = cursor;

        if (sound.Length > 0)
        {
            // 0x463920 is one handler for every human, so a guard walking in the rain lays down the
            // same wet layer the player does. The slot is what names it.
            _footstepSound(new Footstep(
                sound,
                where,
                FootstepSounds.HearingRadiusInUnits(material),
                slot));
        }
    }

    private Action<Vector3d>? _deathSound;
    private Action<string, int, Vector3d>? _hitSound;
    private Action<Vector3d, float>? _meleeStrikeSink;
    private AnimTriggerRegistry? _animationTriggers;
    private Func<int>? _hitCooldown;

    // The sound 0x45D3C0 adds to a soldier's grunt when a knife made the hit.
    private const string KnifeStabSound = "stab_01";
    private Func<float>? _playerDeltaTranslationScale;
    private readonly Dictionary<SoldierView, int> _hitQuietUntil = [];
    private Action<Footstep>? _footstepSound;
    private GameMaterialRegistry? _materials;
    private readonly Dictionary<SoldierView, int> _footCursors = [];
    private readonly HashSet<string> _reportedChannels = new(StringComparer.Ordinal);

    /// <summary>This frame's ground-snap results by queried position; see <see cref="SnapToGround"/>.</summary>
    private readonly Dictionary<(double X, double Y, double Z), double> _groundSnaps = [];
    private int _deathsSoFar;
    private int _tick;
    private int _travelSamples;
    private double _travelTotal;
    private double _travelMax;
    private bool _disposed;
    private int _lastDetectionShoutTick = -1;

    // The player's own third-person body -- Jones, the rig the first-person view never shows. It is NOT a
    // guard: it has no AI, is not in _groups, and the soldier tick and draw loops never touch it. It loads
    // with the level (LoadPlayerBody), stands invisibly until the player dies, then poses the death clip
    // (KillPlayerBody) and is drawn (DrawPlayerBody) once the death camera has pulled back off the eye.
    // Reuses the same BuildGroup / re-skin / draw machinery a soldier's body does. See HumanPlayerBody.
    // The player's own corpse, when g_ragdoll is on. Exactly the soldier arrangement: the retail death
    // clip keeps running underneath and keeps moving the authoritative body, and this replaces only the
    // pose and the placement that are drawn.
    private RagdollBody? _playerRagdoll;
    private SoldierView? _playerBodyView;
    private Group? _playerBodyGroup;
    private Member? _playerBodyMember;
    private bool _playerBodyActive;
    private bool _playerBodyClimbing;
    private bool _playerBodyShown;
    private bool _playerBodyFollowing;
    // The torso request the followed body last acted on. -1 is "none seen": PlayerCamera's counter
    // starts at zero and only ever climbs, so this forces a re-issue the first time the follow takes
    // the body and every time it takes it back off a ladder, a keypad or a death.
    private int _playerBodyUpperBodySequence = -1;
    private bool _playerBodySuppressRootHeight;
    private double _playerBodyVisualLift;
    private double _playerBodyRootHeightAtStart;
    private float _playerBodyFacing;
    private AiSoldier.GroundQuery? _playerBodyGround;

    // The player body's own tick-state pair and frame bookkeeping -- the same smoothing a soldier's
    // Member carries, kept as fields because there is only ever one of it.
    private Vector3d _playerBodyTickWhere;
    private Vector3d _playerBodyPrevTickWhere;
    private bool _playerBodyHasTickState;
    private int _playerBodyCaptureTick = -1;
    private int _playerBodySkinTick = -1;

    // The frame alpha BeginFrame resolved, kept for DrawPlayerBody which runs once per frame.
    private float _frameAlpha = 1f;
    private readonly Dictionary<SoldierView, int> _lastCombatState = [];
    private GrenadeSimulation? _grenades;
    private bool _frameSmooth;

    /// <summary>Initializes a new instance of the <see cref="SoldierRenderer"/> class.</summary>
    /// <param name="device">The device to upload to and draw with.</param>
    /// <param name="resources">Where texture bytes are read from.</param>
    /// <param name="logger">Where to report problems.</param>
    /// <param name="magic">The retail multipart-object table, used to attach gun-flame tasks.</param>
    /// <param name="heldWeapons">
    /// Builds a presenter for a weapon a soldier holds, or null for a renderer that draws none. A
    /// factory rather than a type because the renderer behind it is title content and lives in the
    /// title's own project.
    /// </param>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    public SoldierRenderer(
        IGraphicsDevice device,
        ResourceRegistry resources,
        IOpenIgiLogger? logger = null,
        MagicObjectRegistry? magic = null,
        Func<IHeldWeaponPresenter>? heldWeapons = null)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(resources);

        _device = device;
        _resources = resources;
        _magic = magic;
        _heldWeapons = heldWeapons;
        _logger = logger ?? NullLogger.Instance;
    }

    /// <summary>Gets the number of soldiers that resolved and will draw.</summary>
    public int Count { get; private set; }

    /// <summary>
    /// Lists every resolved soldier for the developer inspector, in a form that outlives them.
    /// </summary>
    /// <param name="into">The list to fill; cleared first.</param>
    /// <exception cref="ArgumentNullException"><paramref name="into"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// A read-only projection rather than access to the soldiers themselves. The inspector runs on the
    /// far side of an interface that must not see gameplay types, and a panel holding a live
    /// <c>SoldierView</c> across a mission change would be holding a corpse the loader had forgotten
    /// about. Everything the panel needs is copied out here, once per frame it draws.
    /// </para>
    /// <para>
    /// The index into the filled list is the handle the inspector's commands use, which is why the
    /// order is the stable group-then-member walk rather than anything sorted.
    /// </para>
    /// </remarks>
    public void Snapshot(List<SoldierSnapshot> into)
    {
        ArgumentNullException.ThrowIfNull(into);
        into.Clear();

        foreach (Group group in _groups)
        {
            foreach (Member member in group.Soldiers)
            {
                SoldierView view = member.View;
                bool dead = view.IsDead || (_health.TryGetValue(view, out HumanHealth? health) && health.IsDead);

                into.Add(new SoldierSnapshot(
                    view.Soldier.Name ?? string.Empty,
                    view.Position / WorldScene.UnitsPerMetre,
                    dead,
                    view.CurrentAnimation,
                    _health.TryGetValue(view, out HumanHealth? hp) ? hp.RemainingFraction : -1f,
                    member.IsCutsceneActor));
            }
        }
    }

    /// <summary>Draws a selection glow around one soldier.</summary>
    /// <param name="index">The soldier's index in the order <see cref="Snapshot"/> fills.</param>
    /// <param name="camera">The eye the frame is drawn from.</param>
    /// <param name="colour">The glow colour.</param>
    /// <param name="radius">How far the glow reaches past the silhouette, in pixels.</param>
    /// <param name="throughWalls">Whether it ignores the depth buffer.</param>
    /// <returns><see langword="true"/> when something was drawn.</returns>
    /// <remarks>
    /// <para>
    /// The pose is handed to the device the same way the ordinary draw hands it over, because a
    /// soldier's mesh is uploaded as two poses the shader blends between. Without it the glow would
    /// stand in whichever pose was uploaded last and lag a step behind the body inside it.
    /// </para>
    /// <para>
    /// The walk mirrors <see cref="Snapshot"/> exactly — group, then member — because that walk is what
    /// defines the handle the inspector asks with.
    /// </para>
    /// </remarks>
    public bool DrawOutline(int index, in Camera3D camera, Bgra32 colour, float radius, bool throughWalls)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (index < 0)
        {
            return false;
        }

        int seen = 0;

        foreach (Group group in _groups)
        {
            foreach (Member member in group.Soldiers)
            {
                if (seen++ != index)
                {
                    continue;
                }

                if (!member.HasFrameState)
                {
                    return false;
                }

                _outlineRanges.Clear();
                foreach (MeshSubset subset in group.Geometry.Subsets)
                {
                    if (subset.IndexCount > 0)
                    {
                        _outlineRanges.Add(new MeshDrawRange(subset.IndexOffset, subset.IndexCount));
                    }
                }

                if (_outlineRanges.Count == 0)
                {
                    return false;
                }

                _device.SetPoseInterpolation(member.PoseBlend, member.PreviousPoseBlend);
                _device.DrawOutline(
                    member.Mesh,
                    CollectionsMarshal.AsSpan(_outlineRanges),
                    camera.CreateModelView(member.DrawnWhere, member.DrawnOrientation),
                    colour,
                    radius,
                    throughWalls);
                _device.ClearPoseInterpolation();
                return true;
            }
        }

        return false;
    }

    /// <summary>Draws a team-coloured through-wall glow over every soldier the last binocular scan marked.</summary>
    /// <remarks>
    /// Reuses the same inverted-hull selection glow as <see cref="DrawOutline"/>, keyed by the contacts
    /// <see cref="BinocularTargets"/> cached this frame, so a scanned enemy stays legible through cover
    /// while the remastered optics are up. Must run inside the scene-capture pass, before the resolve, so
    /// the glow carries the lens and bloom with the rest of the world.
    /// </remarks>
    /// <param name="camera">The eye the scene is being drawn through.</param>
    /// <param name="radiusPixels">The glow's screen-space radius, in pixels.</param>
    public void DrawBinocularOutlines(in Camera3D camera, float radiusPixels)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_binocularDetected.Count == 0)
        {
            return;
        }

        foreach (Group group in _groups)
        {
            foreach (Member member in group.Soldiers)
            {
                if (!member.HasFrameState
                    || !_binocularDetected.TryGetValue(member.View, out BinocularMarkerColor marker))
                {
                    continue;
                }

                _outlineRanges.Clear();
                foreach (MeshSubset subset in group.Geometry.Subsets)
                {
                    if (subset.IndexCount > 0)
                    {
                        _outlineRanges.Add(new MeshDrawRange(subset.IndexOffset, subset.IndexCount));
                    }
                }

                if (_outlineRanges.Count == 0)
                {
                    continue;
                }

                _device.SetPoseInterpolation(member.PoseBlend, member.PreviousPoseBlend);
                _device.DrawOutline(
                    member.Mesh,
                    CollectionsMarshal.AsSpan(_outlineRanges),
                    camera.CreateModelView(member.DrawnWhere, member.DrawnOrientation),
                    MarkerGlow(marker),
                    radiusPixels,
                    throughWalls: true);
                _device.ClearPoseInterpolation();
            }
        }
    }

    /// <summary>The through-wall glow colour for a detector marker class.</summary>
    private static Bgra32 MarkerGlow(BinocularMarkerColor marker) => marker switch
    {
        BinocularMarkerColor.Team => Bgra32.FromRgba(96, 220, 120, 200),
        BinocularMarkerColor.Disabled => Bgra32.FromRgba(96, 150, 255, 200),
        _ => Bgra32.FromRgba(255, 72, 72, 210),
    };

    /// <summary>Projects living, visible soldiers through the native ten-slot binocular detector.</summary>
    /// <param name="camera">The presented eye the contacts are projected through.</param>
    /// <param name="horizontalTangent">The optics' horizontal half-angle tangent (the zoom).</param>
    /// <param name="screenWidth">The back buffer width in pixels.</param>
    /// <param name="screenHeight">The back buffer height in pixels.</param>
    /// <param name="tick">The millisecond tick driving slot lifetime and the entry pulse.</param>
    /// <param name="maxDetectionMetres">
    /// The furthest a contact may be and still be marked, in metres of forward distance. The remastered
    /// optics pass a finite range; the vanilla optics pass positive infinity, staying retail-uncapped.
    /// </param>
    /// <param name="simulationTick">
    /// The fixed simulation tick used to cache line-of-sight results. Leave it at its default when
    /// calling this method from a one-off diagnostic projection.
    /// </param>
    public IReadOnlyList<EnemyOverlayBox> BinocularTargets(
        Camera3D camera,
        float horizontalTangent,
        int screenWidth,
        int screenHeight,
        int tick,
        float maxDetectionMetres,
        int simulationTick = int.MinValue)
    {
        if (horizontalTangent <= 0f)
        {
            _binocularOverlay.Clear();
            return [];
        }

        if (simulationTick != int.MinValue && _binocularVisibilityTick != simulationTick)
        {
            _binocularVisibility.Clear();
            _binocularVisibilityTick = simulationTick;
        }

        _binocularOverlay.BeginScan();
        _binocularDetected.Clear();
        float verticalTangent = horizontalTangent * screenHeight / Math.Max(1f, screenWidth);
        Vector3d eyeUnits = new(
            camera.Position.X * WorldScene.UnitsPerMetre,
            camera.Position.Y * WorldScene.UnitsPerMetre,
            camera.Position.Z * WorldScene.UnitsPerMetre);

        foreach ((SoldierView view, AiSoldier brain) in _brains)
        {
            if (view.IsDead || !ConditionalContainerGate.IsTaskActive(view.Soldier))
            {
                continue;
            }

            // FUN_00470F70 sizes the marker as projectionScale * Human+0xA0 / depth, where Human+0xA0
            // is the model's authored MESH+0x78 bound. For the soldier LODs that value is ~0.94 m --
            // a half-body radius -- so the rectangle only frames the body when it is centred on the
            // model's own origin, which sits that same ~0.94 m above the grounded feet. view.Position
            // is the grounded foot contact, so lift the marker centre by the bound to reach the retail
            // body origin; leaving it at the feet drew the box half into the ground, over the shins.
            if (!_detectorMarkerScaleMetres.TryGetValue(view, out float markerScaleMetres)
                || markerScaleMetres <= 0f)
            {
                continue;
            }

            Vector3d feetUnits = view.Position;
            double bodyCentreUnits = markerScaleMetres * WorldScene.UnitsPerMetre;
            Vector3d targetUnits = new(feetUnits.X, feetUnits.Y, feetUnits.Z + bodyCentreUnits);
            Vector3d delta = new(
                targetUnits.X / WorldScene.UnitsPerMetre - camera.Position.X,
                targetUnits.Y / WorldScene.UnitsPerMetre - camera.Position.Y,
                targetUnits.Z / WorldScene.UnitsPerMetre - camera.Position.Z);
            Vector3d viewPosition = camera.Orientation.Transposed().Transform(delta);
            if (viewPosition.Z <= 0f)
            {
                continue;
            }

            // The remastered detector has a maximum range: a contact past it is not marked at all.
            // Measured on the forward depth, which is the distance the marker label reports, so the cap
            // is the number the player reads. Skipped here, before the occlusion sweep, so a distant
            // soldier costs nothing.
            if (viewPosition.Z > maxDetectionMetres)
            {
                continue;
            }

            float screenX = screenWidth * 0.5f
                + (float)(viewPosition.X / (viewPosition.Z * horizontalTangent) * screenWidth * 0.5f);
            float screenY = screenHeight * 0.5f
                - (float)(viewPosition.Y / (viewPosition.Z * verticalTangent) * screenHeight * 0.5f);
            if (screenX < -8f || screenX > screenWidth + 8f || screenY < -8f || screenY > screenHeight + 8f)
            {
                continue;
            }

            // FUN_00471B60 gates a contact on FUN_004D03D0, a view-frustum cull that the in-front and
            // on-screen tests above already reproduce. The retail engine then hides fully-occluded
            // humans through its sector/portal visibility, which this reimplementation lacks. Approximate
            // that with a line-of-sight sweep: probe the feet, the body origin and the head, and keep the
            // contact when ANY of them is unobstructed. A guard whose upper body clears a low container is
            // still marked; one standing completely behind a wall is dropped, as the original drops it.
            if (_sight is { } lineOfSight)
            {
                bool anyVisible;
                if (simulationTick != int.MinValue
                    && _binocularVisibility.TryGetValue(view, out bool cachedVisibility))
                {
                    anyVisible = cachedVisibility;
                }
                else
                {
                    Vector3d headUnits = new(feetUnits.X, feetUnits.Y, feetUnits.Z + (2d * bodyCentreUnits));
                    anyVisible =
                        HasLineOfSight(lineOfSight, eyeUnits, targetUnits)
                        || HasLineOfSight(lineOfSight, eyeUnits, headUnits)
                        || HasLineOfSight(lineOfSight, eyeUnits, feetUnits);
                    if (simulationTick != int.MinValue)
                    {
                        _binocularVisibility[view] = anyVisible;
                    }
                }
                if (!anyVisible)
                {
                    continue;
                }
            }

            // FUN_00470EC0 writes the transformed depth into the value passed to FUN_00471F60;
            // the retail slot's distance is view-space Z, not Euclidean eye-to-target distance.
            float distance = (float)viewPosition.Z;

            float boxWidth = (float)(markerScaleMetres * screenWidth
                / (Math.Max(0.001, viewPosition.Z) * horizontalTangent));
            float boxHeight = (float)(markerScaleMetres * screenHeight
                / (Math.Max(0.001, viewPosition.Z) * verticalTangent));

            // FUN_00471B60 passes the target's colour flags to FUN_00471F60: extraParam1 is the
            // green flag (Team != 1, Human+0x252) and extraParam2 is the blue flag (the +0x3317
            // non-collidable byte). Live scanned soldiers are collidable, so extraParam2 stays 0;
            // team-1 guards fall through to the retail hostile-red rectangle.
            _binocularOverlay.Upsert(
                brain.RuntimeId,
                distance,
                configFlag: 1,
                screenX,
                screenY,
                eventParam: 0,
                extraParam1: view.Soldier.Team != 1 ? 1 : 0,
                extraParam2: 0,
                tick,
                boxWidth,
                boxHeight);

            // Record the contact for the through-wall glow, in the same team colour its marker uses.
            _binocularDetected[view] = view.Soldier.Team != 1
                ? BinocularMarkerColor.Team
                : BinocularMarkerColor.Hostile;
        }

        _binocularOverlay.ExpireInactive(tick);
        return _binocularOverlay.OverlayBoxes(tick);
    }

    /// <summary>How far past a see-through hit the next probe starts, so it clears that thin surface.</summary>
    private const double SightSkipEpsilonUnits = 64d;

    /// <summary>The most see-through layers a single sight probe steps through before giving up.</summary>
    private const int SightSkipLimit = 8;

    /// <summary>Whether the eye can see a point along a plain cast, any contact blocking.</summary>
    /// <remarks>
    /// The binocular overlay's rule: the retail detector culls by sector/portal visibility, which
    /// see-through surfaces do not bound. See <see cref="HasLineOfSight(WorldTraceCast, Vector3d, Vector3d, double, GameTask)"/>.
    /// </remarks>
    internal static bool HasLineOfSight(WorldRayCast cast, Vector3d from, Vector3d to)
    {
        ArgumentNullException.ThrowIfNull(cast);
        return HasLineOfSight(
            (in WorldTraceRequest request, out WorldTraceHit hit) =>
            {
                bool found = cast(request.Origin, request.End, out WorldRayHit plain);
                hit = found
                    ? WorldTraceHit.FromRayHit(request, plain, WorldTraceHitKind.StaticObject)
                    : default;
                return found;
            },
            from,
            to,
            objectTolerance: 1.0);
    }

    /// <summary>Whether the eye can see a point, treating glass and fences as non-occluding.</summary>
    /// <param name="trace">The world trace.</param>
    /// <param name="from">The eye.</param>
    /// <param name="to">The point looked at.</param>
    /// <param name="objectTolerance">
    /// How far along the eye-to-point segment, as a fraction, an object contact has to be before it stops
    /// counting as cover. <c>1</c> makes every contact block.
    /// </param>
    /// <remarks>
    /// <para>
    /// A cast returns only its NEAREST contact, so a single probe would stop at a chain-link fence and
    /// never notice a solid wall right behind it. This steps the ray past each transparent surface
    /// (glass, wire) and keeps looking, so a guard behind a fence is seen but one behind the wall the
    /// fence stands in front of is not. See <see cref="GameMaterial.IsSightTransparent"/>.
    /// </para>
    /// <para>
    /// Terrain always blocks. An object contact blocks only nearer than the tolerance: the AI's sight ray
    /// (<c>0x489B20</c>) is visible when <c>0x4CE410</c> reports no contact <em>or</em> the contact's segment
    /// fraction is at least 0.79, and <c>0x4CE410</c> forces the fraction to zero for a terrain hit.
    /// </para>
    /// </remarks>
    /// <param name="ignoredOwner">The viewer itself, when it has a body the ray would otherwise meet -- a vehicle.</param>
    /// <returns><see langword="true"/> when the line is clear.</returns>
    public static bool HasLineOfSight(
        WorldTraceCast trace,
        Vector3d from,
        Vector3d to,
        double objectTolerance,
        GameTask? ignoredOwner = null)
    {
        ArgumentNullException.ThrowIfNull(trace);

        Vector3d segment = new(to.X - from.X, to.Y - from.Y, to.Z - from.Z);
        double segmentLength = Math.Sqrt((segment.X * segment.X) + (segment.Y * segment.Y) + (segment.Z * segment.Z));
        Vector3d origin = from;
        double travelled = 0d;
        for (int step = 0; step <= SightSkipLimit; step++)
        {
            // Portal faces are not traced. They describe the doorway and window openings for the renderer's
            // sector visibility, not collision, so 0x4CE410's object handlers never meet them; a closed
            // door leaf is its own collision geometry and still blocks.
            if (!trace(new WorldTraceRequest(origin, to, WorldTraceMask.World, IgnoredOwner: ignoredOwner), out WorldTraceHit hit))
            {
                return true;
            }

            bool transparent = GameMaterial.IsSightTransparent(hit.Material);
            if (!transparent || step == SightSkipLimit)
            {
                // The traversal budget is a safety bound, not evidence that the remainder is clear: past
                // it, whatever the cast reports is cover.
                // A static object blocks wherever it is met. 0x489B20 does accept a returned fraction of 0.79 or
                // more, but the handlers never return one for placed geometry: 359 retail sight casts logged by
                // the probe came back either clear at 1.0 or blocked at exactly 0.0 -- including a mission-1
                // tower guard's ray that met the small barracks' roof 82% of the way along, which the old
                // "past 0.79 is clear" rule let through. Only a moving object keeps the tolerance, unmeasured.
                if (hit.Kind is WorldTraceHitKind.Terrain or WorldTraceHitKind.StaticObject || segmentLength <= 0d)
                {
                    return false;
                }

                return (travelled + hit.Distance) / segmentLength >= objectTolerance;
            }

            double remaining = segmentLength - travelled;
            double advance = hit.Distance + SightSkipEpsilonUnits;
            if (advance >= remaining)
            {
                return true;
            }

            travelled += advance;
            double fraction = travelled / segmentLength;
            origin = new(from.X + (segment.X * fraction), from.Y + (segment.Y * fraction), from.Z + (segment.Z * fraction));
        }

        return true;
    }

    /// <summary>Gets or sets the active authored cutscene transform source.</summary>
    public CutsceneRuntime? Cutscenes { get; set; }

    /// <summary>Gets the living soldiers the retail map computer plots as guard contacts.</summary>
    public IReadOnlyList<Vector3d> MapContacts()
    {
        List<Vector3d> contacts = [];
        foreach ((SoldierView view, AiSoldier brain) in _brains)
        {
            if (ConditionalContainerGate.IsTaskActive(view.Soldier)
                && !view.IsDead
                && (!_health.TryGetValue(view, out HumanHealth? health) || health is not { IsDead: true }))
            {
                contacts.Add(brain.Position);
            }
        }

        return contacts;
    }

    /// <summary>
    /// Every live soldier as a security camera's scan is offered it — <c>0x45FE30</c>'s description, in the
    /// global human list's order.
    /// </summary>
    /// <remarks>
    /// The lens scans for team 1 (<c>0x4415C0</c>), so a team-1 soldier counts as the cameras' own side: its
    /// body is what a camera raises the alarm on at once. The mark is the one the guards' own scan tests
    /// (<c>sub_465820</c>), the same for a body as for a standing soldier.
    /// </remarks>
    public IReadOnlyList<SecurityCameraSubject> CameraSubjects()
    {
        List<SecurityCameraSubject> subjects = [];
        foreach ((SoldierView view, AiSoldier brain) in _brains)
        {
            if (!ConditionalContainerGate.IsTaskActive(view.Soldier))
            {
                continue;
            }

            Vector3d feet = new(
                view.Position.X,
                view.Position.Y,
                SnapToGround(view.Position.X, view.Position.Y, view.Position.Z));
            bool dead = view.IsDead
                || brain.Activity == AiSoldierActivity.Dead
                || (_health.TryGetValue(view, out HumanHealth? health) && health.IsDead);
            subjects.Add(new SecurityCameraSubject(
                view,
                new Vector3d(feet.X, feet.Y, feet.Z + AiSoldier.DetectionMarkAboveFeet(crouching: false)),
                CamerasSide: view.Soldier.Team == 1,
                Alive: !dead,
                view.Soldier));
        }

        return subjects;
    }

    /// <summary>Gets the live AI brains for focused renderer-path tests.</summary>
    internal IReadOnlyList<AiSoldier> BrainsForTesting => [.. _brains.Values];

    internal IReadOnlyList<KeyValuePair<SoldierView, AiSoldier>> BrainPairsForTesting => [.. _brains];

    internal bool IsInViewForTesting(SoldierView view) => FindMember(view)?.InView ?? false;

    /// <summary>Marks every soldier drawn close up, as a frame looking at all of them would.</summary>
    internal void ObserveAllForTesting()
    {
        foreach (Group group in _groups)
        {
            foreach (Member member in group.Soldiers)
            {
                member.InView = true;
            }
        }
    }

    internal string DescribeViewForTesting(SoldierView view)
    {
        foreach (Group group in _groups)
        {
            foreach (Member member in group.Soldiers)
            {
                if (ReferenceEquals(member.View, view) && _lastObservedCamera is { } camera)
                {
                    Matrix4f mv = camera.CreateModelView(member.DrawnWhere, member.DrawnOrientation);
                    BonePose pose = view.Pose();
                    string eye = HumanAttachPoints.AiEye < pose.BoneCount ? pose.Positions[HumanAttachPoints.AiEye].ToString() : "-";
                    return $"lowestZ={group.LowestVertexZ:0.000} eyeOffset={eye} viewZ={view.Position.Z / 4096.0:0.000} drawnZ={member.DrawnWhere.Z:0.000} " +
                        $"cam={camera.Position} viewPos=({mv.M41:0.0},{mv.M42:0.0},{mv.M43:0.0})";
                }
            }
        }

        return "-";
    }

    private Camera3D? _lastObservedCamera;

    private WorldTraceCast? _sightTraceForTesting;

    /// <summary>Whether an AI sight ray from one point to another is clear, for scenario runs.</summary>
    internal bool ClearSightForTesting(Vector3d from, Vector3d to) =>
        _sightTraceForTesting is { } trace
        && HasLineOfSight(trace, from, to, AiSoldier.SightObstructionTolerance);

    /// <summary>A soldier's accumulated damage, for scenario runs.</summary>
    internal float DamageForTesting(SoldierView soldier) =>
        _health.TryGetValue(soldier, out HumanHealth? health) ? health.Damage : 0f;

    /// <summary>What an AI sight ray from one point to another meets first, for scenario traces.</summary>
    internal string DescribeSightForTesting(Vector3d from, Vector3d to)
    {
        if (_sightTraceForTesting is not { } trace)
        {
            return "no trace";
        }

        bool clear = HasLineOfSight(trace, from, to, AiSoldier.SightObstructionTolerance);
        WorldTraceRequest request = new(from, to, WorldTraceMask.World) { TracePortals = false };
        Vector3d along = to - from;
        return trace(in request, out WorldTraceHit hit)
            ? $"clear={clear} facing={((hit.Normal.X * along.X) + (hit.Normal.Y * along.Y) + (hit.Normal.Z * along.Z) < 0 ? "front" : "BACK")} hit {hit.Kind} at {hit.Distance / 4096.0:0.00} m of {Vector3d.Distance(from, to) / 4096.0:0.00} m, fraction {hit.Fraction:0.000}, material {hit.Material} '{hit.Label}' owner {hit.Owner?.Id}, at ({hit.Position.X / 4096.0:0.0},{hit.Position.Y / 4096.0:0.0},{hit.Position.Z / 4096.0:0.0})"
            : $"clear={clear} no contact";
    }

    /// <summary>Resolves a human task's live pose for an EditCamera link or target.</summary>
    public CutsceneTargetPose? CutscenePose(int taskId)
    {
        foreach (Group group in _groups)
        {
            foreach (Member member in group.Soldiers)
            {
                if (member.View.Soldier.Id == taskId && ConditionalContainerGate.IsTaskActive(member.View.Soldier))
                {
                    if (Cutscenes?.TryGetAttachedDrawTransform(
                        member.View.Soldier, out Vector3d attached, out Matrix3f attachedOrientation) == true)
                    {
                        return new CutsceneTargetPose(
                            attached * WorldScene.UnitsPerMetre,
                            attachedOrientation);
                    }

                    if (member.IsCutsceneActor)
                    {
                        // Grounded on its graph, as TickPlacementOf draws it.
                        if (!member.HasCutsceneGameplayMovement
                            && _brains.TryGetValue(member.View, out AiSoldier? stagedBrain) && stagedBrain.HasGraph && stagedBrain.HasGroundQuery
                            && !stagedBrain.GraphInParentFrame && stagedBrain.StoodOnGraph)
                        {
                            return new CutsceneTargetPose(
                                member.View.Position + new Vector3d(0d, 0d, member.View.RootSample.Z),
                                member.View.Orientation);
                        }

                        if (member.HasCutsceneGameplayMovement)
                        {
                            double groundedZ = SnapToGround(
                                member.View.Position.X,
                                member.View.Position.Y,
                                member.View.Position.Z);
                            return new CutsceneTargetPose(
                                new Vector3d(
                                    member.View.Position.X,
                                    member.View.Position.Y,
                                    groundedZ + member.View.RootSample.Z),
                                member.View.Orientation);
                        }

                        if (member.HasCutsceneRootMotion)
                        {
                            return new CutsceneTargetPose(member.View.Position, member.View.Orientation);
                        }

                        // A stationary staged task retains its authored model origin. Native
                        // 0x462390 derives feet by subtracting the bone/root offset from that origin;
                        // it does not treat the authored origin itself as a floor height.
                        HumanSoldierTask staged = member.View.Soldier;
                        return new CutsceneTargetPose(
                            staged.Position,
                            Matrix3f.FromEngineEulerAngles(0f, 0f, staged.Gamma));
                    }
                    return new CutsceneTargetPose(member.View.Position, member.View.Orientation);
                }
            }
        }

        return null;
    }

    /// <summary>Gets or sets the derived surface-detail maps, or <see langword="null"/> for none.</summary>
    /// <remarks>Owned by the shell: one pack serves every renderer that loads a texture.</remarks>
    public MaterialDetailPack? DetailMaps { get; set; }

    /// <summary>Builds the drawable soldiers from a loaded level.</summary>
    /// <param name="mission">The mission whose packs hold the models and animation sets.</param>
    /// <param name="levelRoot">The task the level's subtree hangs off.</param>
    /// <param name="terrain">The level's terrain, for standing soldiers on the ground it declares.</param>
    /// <param name="cast">The placed-object ray cast, for soldiers standing on floors and platforms.</param>
    /// <returns>The number of soldiers that resolved.</returns>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    public int Load(LoadedMission mission, GameTask levelRoot, IGroundHeightField? terrain = null, WorldRayCast? cast = null)
    {
        ArgumentNullException.ThrowIfNull(mission);
        ArgumentNullException.ThrowIfNull(levelRoot);
        ObjectDisposedException.ThrowIf(_disposed, this);

        Release();
        _terrain = terrain;
        _cast = cast;

        Dictionary<(string Model, int Skeleton, int Stand), Group> groups = [];
        foreach (HumanSoldierTask soldier in levelRoot.Descendants().OfType<HumanSoldierTask>())
        {
            if (!mission.TryGetAnimationSet(soldier.BoneHierarchy, out BoneAnimationSet? set) || set is null)
            {
                _logger.Warning(
                    LogCategory,
                    $"'{soldier.Name}' declares skeleton {soldier.BoneHierarchy}, which no pack loaded a set for.");
                continue;
            }

            SoldierView view = new(soldier, set);
            if (soldier.StandAnimation >= 0 && !view.DeclaredStandAnimationFound)
            {
                // The original's own wording (0x456E50).
                _logger.Warning(LogCategory, $"Animation #{soldier.StandAnimation} not found");
            }

            (string, int, int) key = (soldier.Model, soldier.BoneHierarchy, view.StandAnimation);
            if (!groups.TryGetValue(key, out Group? group))
            {
                group = BuildGroup(mission, soldier.Model, view);
                if (group is null)
                {
                    continue;
                }

                groups[key] = group;
                _groups.Add(group);
            }

            // Its own mesh, so it can hold its own pose (see Member) -- animated, so each tick's
            // re-skin replaces the vertices in place and the pose replaced becomes the previous-pose
            // stream the motion vectors and the between-tick blending read.
            BonePose initialPose = view.UnrotatedPose();
            Vertex3D[] initialSkin = new Vertex3D[group.Geometry.Vertices.Length];
            group.Geometry.Vertices.CopyTo(initialSkin, 0);
            bool initialSkinReady = false;
            if (group.Model is { } skinModel)
            {
                try
                {
                    MefSkinner.Skin(
                        skinModel,
                        initialPose,
                        (float)(1.0 / WorldScene.UnitsPerMetre),
                        initialSkin);
                    initialSkinReady = true;
                }
                catch (ArgumentException)
                {
                    // Keep the raw mesh if a malformed model cannot be skinned. Its collision path falls
                    // back to the authoritative pose until two valid animated uploads establish a pair.
                    initialSkinReady = false;
                }
            }

            MeshHandle own;
            try
            {
                // Both GPU streams begin with this same skin. A smoothed player shot can therefore use
                // a matching CPU pose pair even on the very first rendered frame; seeding them with raw
                // geometry would make the displayed blend untraceable until a second animation tick.
                own = _device.CreateAnimatedMesh(initialSkin, group.Geometry.Indices);
            }
            catch (InvalidOperationException)
            {
                continue;
            }

            // A human staged beside a CutScene keeps the authored object origin.  Gameplay humans are
            // grounded by their mover on the first ticks, but cinematic performers deliberately sit,
            // crouch or ride at offsets from nearby geometry.  Running the gameplay stand probe over
            // Anya, for example, put the lowest vertex of her seated clip on the control-room floor and
            // lifted the entire actor through the camera framing. Do not add the fallback standing
            // clip's root height here either: the task authors the cinematic pelvis origin, and the real
            // seated clip is selected only after the timeline starts.
            bool cutsceneActor = CinematicActor.IsStaged(soldier);
            Vector3d placement = cutsceneActor
                ? soldier.Position / WorldScene.UnitsPerMetre
                : Stand(soldier, group, terrain, cast);
            group.Soldiers.Add(new Member(view, placement, own)
            {
                IsCutsceneActor = cutsceneActor,
                DrawnPose = initialPose,
                SkinScratch = initialSkin,
                SkinnedPose = initialSkinReady ? initialPose : null,
                PreviousSkinnedPose = initialSkinReady ? initialPose : null,
            });
            if (group.Model is { NativeMarkerBound: > 0f } model
                && float.IsFinite(model.NativeMarkerBound))
            {
                _detectorMarkerScaleMetres[view] =
                    model.NativeMarkerBound / (float)WorldScene.UnitsPerMetre;
            }
            Count++;
        }

        _logger.Information(
            LogCategory,
            $"{Count} soldier(s) over {_groups.Count} distinct (model, skeleton, animation) triple(s).");

        // The first player round must not pay for parsing every candidate's collision mesh, and the
        // first lethal round must not pay for measuring the animation corpus and preparing a ragdoll
        // rig. Both operations are immutable mission data; doing them here moves their cost to the
        // already-visible mission hand-over instead of turning a Dragunov hit into a 2–5 FPS hitch.
        WarmHitTestCaches();
        return Count;
    }

    /// <summary>Primes immutable hit-test and corpse-rig data before the first round can arrive.</summary>
    /// <remarks>
    /// Collision geometry and ragdoll rigs are shared by every member of a group. They used to be
    /// created by the first shot/death that touched a group, which made a normal first kill look like a
    /// render stall. A malformed optional collision chunk still falls back to the cylinder path; it must
    /// not make mission loading fail.
    /// </remarks>
    private void WarmHitTestCaches()
    {
        int collisionMeshes = 0;
        int ragdollRigs = 0;
        foreach (Group group in _groups)
        {
            try
            {
                if (group.CollisionMesh is not null)
                {
                    collisionMeshes++;
                }
            }
            catch (PackFormatException ex)
            {
                _logger.Warning(LogCategory, $"A soldier collision mesh was not usable: {ex.Message}");
            }

            if (!GameplayCVars.Ragdoll.Value || group.Model is null || group.Soldiers.Count == 0)
            {
                continue;
            }

            if (RagdollRigFor(group.Soldiers[0].View.Bones.Set, group.Model) is not null)
            {
                ragdollRigs++;
            }
        }

        _logger.Information(
            LogCategory,
            $"Prepared {collisionMeshes} soldier collision mesh(es) and {ragdollRigs} corpse rig(s) before play.");
    }

    /// <summary>How high above a soldier's feet its broad-phase sphere is centred, in world units.</summary>
    private const double PickChestUnits = 3686.0;

    /// <summary>The broad-phase cull radius, in world units — generous (a soldier plus its outstretched arms).</summary>
    private const double PickCullRadiusUnits = 8192.0;

    /// <summary>
    /// Names the soldier under a crosshair ray by tracing its <b>skinned collision mesh</b> — the same
    /// geometry a round is scored against, so the readout hits exactly where a shot would.
    /// </summary>
    /// <param name="originUnits">The eye, in world units.</param>
    /// <param name="direction">The look direction; need not be unit length.</param>
    /// <param name="reachUnits">How far to look, in world units.</param>
    /// <param name="name">Receives the struck soldier's model instance name.</param>
    /// <param name="distanceUnits">Receives how far along the ray it sits.</param>
    /// <param name="feetUnits">Receives the struck soldier's feet position, in world units.</param>
    /// <param name="zone">Receives the hit zone the round would score, or <c>-1</c> when none.</param>
    /// <returns><see langword="false"/> when the crosshair is not on a soldier's body.</returns>
    public bool TryPick(
        Vector3d originUnits,
        Vector3d direction,
        double reachUnits,
        out string name,
        out double distanceUnits,
        out Vector3d feetUnits,
        out int zone)
    {
        name = string.Empty;
        distanceUnits = double.MaxValue;
        feetUnits = default;
        zone = -1;

        double length = Math.Sqrt(
            (direction.X * direction.X) + (direction.Y * direction.Y) + (direction.Z * direction.Z));
        if (length <= 0.0)
        {
            return false;
        }

        Vector3d dir = new(direction.X / length, direction.Y / length, direction.Z / length);
        Vector3d end = new(
            originUnits.X + (dir.X * reachUnits),
            originUnits.Y + (dir.Y * reachUnits),
            originUnits.Z + (dir.Z * reachUnits));
        bool found = false;

        foreach (Group group in _groups)
        {
            if (group.CollisionMesh is not { } mesh)
            {
                continue;
            }

            foreach (Member member in group.Soldiers)
            {
                SoldierView view = member.View;
                if (!ConditionalContainerGate.IsTaskActive(view.Soldier))
                {
                    continue;
                }
                double snappedZ = SnapToGround(view.Position.X, view.Position.Y, view.Position.Z);
                Vector3d feet = new(view.Position.X, view.Position.Y, snappedZ);

                // Broad phase: a generous body sphere, so a ray nowhere near a soldier never touches its mesh.
                Vector3d centre = new(feet.X, feet.Y, feet.Z + PickChestUnits);
                if (!RaySphere(originUnits, dir, centre, PickCullRadiusUnits, reachUnits, out _))
                {
                    continue;
                }

                Matrix3f m = view.Orientation;
                Vector3f facing = new(m.M12, m.M22, m.M32);
                if (HumanHitTest.TryCastMesh(
                        originUnits, end, feet, facing, mesh, view.Pose(), view.RootSample.Z, out HumanHit h)
                    && h.Distance < distanceUnits)
                {
                    distanceUnits = h.Distance;
                    name = group.Instance?.Name ?? "soldier";
                    feetUnits = feet;
                    zone = h.Zone;
                    found = true;
                }
            }
        }

        return found;
    }

    /// <summary>
    /// Names the detection state of the soldier under a ray, toward a target eye — the overlay's "why won't this
    /// guard see me" readout. Picks the nearest soldier the ray's body sphere touches and asks its brain which
    /// gate (cone, line of sight) is passing.
    /// </summary>
    /// <param name="originUnits">The ray origin in engine units.</param>
    /// <param name="direction">The ray direction; need not be normalised.</param>
    /// <param name="reachUnits">How far to look, in units.</param>
    /// <param name="enemyEyeUnits">The eye the guards actually test against — the player's, in units.</param>
    /// <param name="info">The formatted detection line, empty when no soldier is under the ray.</param>
    /// <returns><see langword="true"/> when a soldier was under the ray and had a brain to ask.</returns>
    public bool TryDetectionInfo(
        Vector3d originUnits,
        Vector3d direction,
        double reachUnits,
        Vector3d enemyEyeUnits,
        out string info)
    {
        info = string.Empty;

        double length = Math.Sqrt(
            (direction.X * direction.X) + (direction.Y * direction.Y) + (direction.Z * direction.Z));
        if (length <= 0.0)
        {
            return false;
        }

        Vector3d dir = new(direction.X / length, direction.Y / length, direction.Z / length);
        AiSoldier? nearest = null;
        SoldierView? nearestView = null;
        double nearestT = double.MaxValue;

        foreach ((SoldierView view, AiSoldier brain) in _brains)
        {
            if (!ConditionalContainerGate.IsTaskActive(view.Soldier))
            {
                continue;
            }
            double snappedZ = SnapToGround(view.Position.X, view.Position.Y, view.Position.Z);
            Vector3d centre = new(view.Position.X, view.Position.Y, snappedZ + PickChestUnits);
            if (RaySphere(originUnits, dir, centre, PickCullRadiusUnits, reachUnits, out double t) && t < nearestT)
            {
                nearestT = t;
                nearest = brain;
                nearestView = view;
            }
        }

        if (nearest is null)
        {
            return false;
        }

        (double distU, bool inCone, bool los, bool alerted, bool visible) = nearest.ProbeDetection(enemyEyeUnits);
        string cone = inCone ? "IN" : "out";
        string clear = los ? "clear" : "BLOCKED";
        string alert = alerted ? "yes" : "no";
        string seen = visible ? "YES" : "no";
        Member? member = nearestView is null ? null : FindMember(nearestView);
        string task = nearestView?.Soldier.Name ?? "?";
        string weapon = member?.WeaponType?.ScriptId ?? "NONE";
        string aim = nearest.Target?.ModeOf(nearest).ToString() ?? "None";
        int aimers = nearest.Target?.Count(AiAimMode.Aiming) ?? 0;
        string patrol = nearest.Arm == AiCombatArm.Patrol ? $"/{nearest.PatrolAction}" : string.Empty;
        string distance = (distU / WorldScene.UnitsPerMetre).ToString("F1", System.Globalization.CultureInfo.InvariantCulture);
        info =
            $"detect: dist={distance}m cone={cone} los={clear} alert={alert} "
            + $"seen={seen} {nearest.Activity} task={task} ai={nearest.Type}/{nearest.Arm} script={(nearest.HasScript ? 1 : 0)} "
            + $"phase={nearest.CombatPhase}{patrol} aim={aim}:{aimers} weapon={weapon} "
            + $"ammo={member?.AmmoInClip ?? 0} trig={(nearest.Combat.PullTrigger ? 1 : 0)}";
        return true;
    }

    // Nearest positive entry of a ray (unit direction) into a sphere, within reach. Distance is along the ray.
    private static bool RaySphere(Vector3d origin, Vector3d dir, Vector3d centre, double radius, double reach, out double t)
    {
        t = 0.0;
        double ox = origin.X - centre.X;
        double oy = origin.Y - centre.Y;
        double oz = origin.Z - centre.Z;
        double b = (ox * dir.X) + (oy * dir.Y) + (oz * dir.Z);
        double c = (ox * ox) + (oy * oy) + (oz * oz) - (radius * radius);
        double disc = (b * b) - c;
        if (disc < 0.0)
        {
            return false;
        }

        double root = Math.Sqrt(disc);
        double near = -b - root;
        double far = -b + root;
        t = near >= 0.0 ? near : far;
        return t >= 0.0 && t <= reach;
    }

    /// <summary>Puts a soldier's feet on whatever is under its declared position.</summary>
    /// <remarks>
    /// <para>
    /// In the original the level's declared <c>Position</c> is only where a soldier <em>starts</em>: its
    /// update runs the same movement integrator as the player's, whose ground pass (<c>0x462390</c>)
    /// snaps the body onto the surface below within the first ticks. The port does not tick soldiers
    /// yet, so a body drawn with its skeleton root at the declared height stands buried to the waist
    /// wherever the position was authored at floor level.
    /// </para>
    /// <para>
    /// This reproduces the settled state, not the mechanism: probe down from half a metre above the
    /// declared position — placed objects first, terrain as the fallback, highest surface wins — and
    /// stand the <b>skin's lowest vertex</b> on it. The original's own foot reference is
    /// <c>0x48A340</c>'s bone-0-plus-<c>Human+1300</c> offset, and the <c>+1300</c> term is still
    /// unread (<c>player.md</c> §14.9), so the mesh's own extent is the honest stand-in. A recorded
    /// divergence, retired with the soldier update.
    /// </para>
    /// </remarks>
    private static Vector3d Stand(HumanSoldierTask soldier, Group group, IGroundHeightField? terrain, WorldRayCast? cast)
    {
        Vector3d declared = soldier.Position;
        double probeTop = declared.Z + ProbeAboveInUnits;
        double groundZ = double.MinValue;
        bool foundGround = false;

        if (cast is not null
            && cast(
                new Vector3d(declared.X, declared.Y, probeTop),
                new Vector3d(declared.X, declared.Y, declared.Z - ProbeBelowInUnits),
                out WorldRayHit hit))
        {
            groundZ = hit.Position.Z;
            foundGround = true;
        }

        if (terrain is not null
            && terrain.TryGetGround(declared.X, declared.Y, probeTop, out TerrainGround ground)
            && ground.Height >= declared.Z - ProbeBelowInUnits
            && ground.Height <= probeTop)
        {
            groundZ = foundGround ? Math.Max(groundZ, ground.Height) : ground.Height;
            foundGround = true;
        }

        if (!foundGround)
        {
            groundZ = declared.Z;
        }

        return new Vector3d(
            declared.X / WorldScene.UnitsPerMetre,
            declared.Y / WorldScene.UnitsPerMetre,
            (groundZ / WorldScene.UnitsPerMetre) - group.LowestVertexZ);
    }

    /// <summary>The collision surface under a point, in world units — placed objects first, terrain second.</summary>
    /// <param name="x">The point's X, in world units.</param>
    /// <param name="y">Its Y.</param>
    /// <param name="declaredZ">The feet the query starts from; returned unchanged when nothing is under them.</param>
    /// <returns>The highest floor within the probe, or <paramref name="declaredZ"/> if none.</returns>
    /// <remarks>
    /// The same query <see cref="Stand"/> runs, without the mesh lift — for grounding a live soldier's feet
    /// each frame. It chooses the highest support in the probe and can therefore lower a soldier whose
    /// authored spawn is slightly above the terrain, as well as lift one authored just below its floor.
    /// </remarks>
    private double SnapToGround(double x, double y, double declaredZ)
    {
        // One collision cast and one terrain descent per distinct position per FRAME: the shadow
        // cascades and the geometry pass each resolve every soldier's feet from the same positions,
        // and this query was being paid once per pass. Cleared in BeginFrame, so a moved soldier is
        // re-queried the moment the simulation actually moved it.
        (double X, double Y, double Z) key = (x, y, declaredZ);
        if (_groundSnaps.TryGetValue(key, out double cached))
        {
            return cached;
        }

        double groundZ = double.MinValue;
        bool foundGround = false;
        double probeTop = declaredZ + ProbeAboveInUnits;

        if (_cast is not null
            && _cast(new Vector3d(x, y, probeTop), new Vector3d(x, y, declaredZ - ProbeBelowInUnits), out WorldRayHit hit))
        {
            groundZ = hit.Position.Z;
            foundGround = true;
        }

        if (_terrain is not null
            && _terrain.TryGetGround(x, y, probeTop, out TerrainGround ground)
            && ground.Height >= declaredZ - ProbeBelowInUnits
            && ground.Height <= probeTop)
        {
            groundZ = foundGround ? Math.Max(groundZ, ground.Height) : ground.Height;
            foundGround = true;
        }

        // Cached whether or not anything was found, because "nothing is under these feet" costs the
        // same two queries to establish as a floor does and is just as true for the rest of the frame.
        double snapped = foundGround ? groundZ : declaredZ;
        _groundSnaps[key] = snapped;
        return snapped;
    }

    /// <summary>Gives every soldier an AI that patrols the level's nav graph and reacts to the player.</summary>
    /// <param name="graphs">
    /// The level's nav graphs, keyed by the id of the <c>AIGraph</c> task that loads each. A soldier is
    /// bound to the one its own <c>HumanAI</c>'s <c>Graph ID</c> names; one that names a graph the level
    /// does not ship gets no brain rather than being routed over somebody else's node ids.
    /// </param>
    /// <param name="sight">The world trace used for line of sight, or <see langword="null"/> to see through walls.</param>
    /// <param name="scripts">Where the archetype behaviour script is read from, or <see langword="null"/> for none.</param>
    /// <param name="natives">The registry the behaviour script resolves its AI natives against.</param>
    /// <param name="levelRoot">
    /// The level's task tree, which the authored patrol paths are collected from. Without it a soldier has
    /// no path to walk and stands where the level put it.
    /// </param>
    /// <param name="config">
    /// The combat table out of <c>common/ai/settings.qsc</c>, or <see langword="null"/> to leave every
    /// soldier on the constructor's defaults.
    /// </param>
    /// <param name="difficulty">Which of the table's three columns to read.</param>
    /// <param name="variables">The live level variables used by AlarmControl expressions.</param>
    /// <returns>How many soldiers got a brain.</returns>
    /// <remarks>
    /// The phase is the soldier's index modulo four, which is what staggers the level's detection sweeps
    /// across the four ticks the original spreads them over (<c>ai.md</c> §5.1).
    /// </remarks>
    public int Animate(
        IReadOnlyDictionary<int, GraphFile> graphs,
        WorldTraceCast? sight = null,
        ResourceRegistry? scripts = null,
        NativeRegistry? natives = null,
        GameTask? levelRoot = null,
        HumanAiConfigRegistry? config = null,
        AiDifficulty difficulty = AiDifficulty.Normal,
        ScriptVarRegistry? variables = null)
    {
        ArgumentNullException.ThrowIfNull(graphs);
        ObjectDisposedException.ThrowIf(_disposed, this);

        _sight = sight is null ? null : WorldTrace.AsRayCast(sight, WorldTraceMask.World);
        _sightTraceForTesting = sight;
        _brains.Clear();
        _brainsByRuntimeId.Clear();
        _guardGenerators.Clear();
        _guardExpressions = variables is null ? null : new LevelExpressionRuntime(variables);
        _scriptVariables = variables;
        _lastDetectionShoutTick = -1;
        _combat.Clear();
        _health.Clear();
        _activatedTasks.Clear();
        _playerTarget = new AiCombatTarget("HumanPlayer");
        _levelRoot = levelRoot;
        // The player's team decides which brains may fire on it. A same-team human is never an enemy, so
        // a friendly patrol (team 0, the same as the player) is told not to engage. Resolved once per level
        // from the HumanPlayer task the mission declares -- the same source the spawn uses -- and handed to
        // each brain as its PlayerIsEnemy gate below.
        _playerTeam = levelRoot is not null && PlayerSpawn.TryFindGameplay(levelRoot, out PlayerSpawnPoint spawn)
            ? spawn.Team
            : 0;
        _alarmControls = levelRoot is not null && variables is not null
            ? AiAlarmControlRegistry.Build(levelRoot, variables)
            : null;
        if (graphs.Count == 0)
        {
            // No graphs means no patrolling -- but a brain does not need a node to see or to shoot, so
            // the soldiers are still armed and still fight from where they stand.
            _logger.Warning(LogCategory, "The level ships no nav graph, so no soldier can patrol.");
        }

        // The level's authored patrol paths, keyed by the PatrolPath task's own id -- what AIAction_Patrol's
        // first argument names (ai.md 7.1).
        AiPatrolPathRegistry? paths = levelRoot is null ? null : AiPatrolPathRegistry.Build(levelRoot);
        if (paths is not null)
        {
            _logger.Information(LogCategory, $"{paths.Count} authored patrol path(s) in this level.");
        }

        AiScriptHost? host = scripts is not null && natives is not null
            ? new AiScriptHost(natives, _logger, _aiRandom)
            : null;
        Dictionary<string, QvmProgram?> scriptCache = new(StringComparer.Ordinal);

        // One occupancy registry per level -- the global claim list at 0x57D768. Every brain shares it,
        // which is what stops a squad's free-position searches all electing the same node.
        AiNodeOccupancy occupancy = new();

        int index = 0;
        int patrolling = 0;
        int perTask = 0;
        int unroutable = 0;
        Dictionary<int, int> graphUse = [];
        foreach (Group group in _groups)
        {
            foreach (Member member in group.Soldiers)
            {
                SoldierView view = member.View;

                // Each soldier walks the graph ITS OWN HumanAI names, not the level's biggest one. A
                // soldier whose graph the level does not ship still gets a BRAIN -- nothing in detection
                // or the stand-and-fight arm needs a node, so leaving it brainless made an inert prop
                // that neither saw nor fought where retail's would still shoot back. It just cannot
                // patrol or reposition, which is what the graph is actually for.
                HumanAiTask? brainTask = FindMind(view);
                int graphId = brainTask?.GraphId ?? 0;
                if (!graphs.TryGetValue(graphId, out GraphFile? graph) || graph.RouteTable is null)
                {
                    graph = null;
                    if (brainTask is not null)
                    {
                        unroutable++;
                    }
                }

                Vector3d initialPosition = view.Position;
                if (!CinematicActor.IsStaged(view.Soldier))
                {
                    double groundedZ = SnapToGround(initialPosition.X, initialPosition.Y, initialPosition.Z);
                    if (groundedZ != initialPosition.Z)
                    {
                        _logger.Information(
                            LogCategory + ".Ground",
                            $"soldier={view.Soldier.Id} declaredZ={initialPosition.Z:0.0} " +
                            $"groundedZ={groundedZ:0.0} delta={(groundedZ - initialPosition.Z) / WorldScene.UnitsPerMetre:0.000}m");
                        initialPosition = new Vector3d(initialPosition.X, initialPosition.Y, groundedZ);
                        view.PlaceAt(initialPosition, view.Orientation);
                    }
                }

                AiSoldier brain = new(graph, initialPosition, GammaOf(view), index & 3, index, _aiRandom, view.Soldier.Class);
                // A soldier on the player's own team must not engage the player. The brain's noise and
                // vision gates read this: detection and every player-origin gunshot/impact/walk event are
                // refused for a same-team player, so the friendly patrol stands down instead of shooting us.
                brain.PlayerIsEnemy = view.Soldier.Team != _playerTeam;
                BindViewTracking(brain, view);
                brain.RuntimeId = index + 1;
                brain.SetResponseRouter(source =>
                {
                    if (_brainsByRuntimeId.TryGetValue(source, out AiSoldier? recipient))
                    {
                        recipient.ReceiveAlertResponse();
                    }
                });
                brain.SetDefaultTarget(_playerTarget);
                brain.SetAlarmStatusResolver(_alarmControls is { } controls ? controls.IsOn : null);
                brain.SetStationaryGunResolver(
                    _interactions is { } interactions ? interactions.FindStationaryGun : null);
                brain.SetPatrolPaths(paths);
                brain.SetNodeOccupancy(occupancy);
                brain.SetGroundQuery(MakeGroundQuery());
                if (_doors is { } levelDoors)
                {
                    brain.SetDoorQuery(levelDoors.Nearest);
                }
                if (levelRoot is not null)
                {
                    GameTask taskRoot = levelRoot;
                    brain.SetActivateSupport(
                        _activationClaims,
                        (id, out position) =>
                        {
                            return TryGetTaskPosition(taskRoot, id, out position);
                        },
                        id =>
                        {
                            _activatedTasks.Add(id);
                            if (brain.Arm == AiCombatArm.Gunner
                                || _interactions?.FindStationaryGun(id) is not null)
                            {
                                _logger.Information(
                                    LogCategory + ".Gunner",
                                    $"runtime={brain.RuntimeId} activation reached task={id} "
                                        + $"gunnerId={brain.GunnerId}");
                            }
                            _aiTaskActivator?.Invoke(id);
                        });
                }
                if (graph is not null)
                {
                    graphUse[graphId] = graphUse.GetValueOrDefault(graphId) + 1;
                }
                if (sight is not null)
                {
                    // HumanAI and the binocular overlay share the same transparent-surface rule. A raw
                    // nearest-hit query stops at Level 4's material-14 window and made the village gunner
                    // blind to a player immediately outside, even though the remainder of the ray was clear.
                    // Step past retail Glass/Water/Fence/Ladder faces, while an ordinary wall or physical
                    // closed-door leaf remains cover.
                    brain.SetLineOfSight((from, to) => HasLineOfSight(sight, from, to, AiSoldier.SightObstructionTolerance));
                }

                // Which script this soldier runs: its own MISSION:AI/<taskid>.qvm when the level ships one,
                // otherwise the archetype its AIType selects (ai.md 7.5a and 2.3). The per-task script is
                // what names the soldier's patrol path, so without it a guard has nothing to patrol.
                // When a per-task script exists, the archetype is passed separately so DefaultHandler can
                // delegate to it — the mechanism the original uses at 0x44E060.
                HumanAiTask? mind = brainTask;
                if (host is not null && scripts is not null)
                {
                    QvmProgram? perTaskScript = null;
                    if (mind is not null && mind.Id != GameTask.NoId)
                    {
                        perTaskScript = LoadScript(scripts, scriptCache, AiScriptApi.MissionScriptPath(mind.Id));
                        if (perTaskScript is not null)
                        {
                            perTask++;
                        }
                    }

                    AiArchetype archetype = mind?.Archetype ?? AiArchetype.Guard;
                    QvmProgram? archetypeScript = LoadScript(scripts, scriptCache, AiScriptApi.ScriptPath(archetype));

                    QvmProgram? primary = perTaskScript ?? archetypeScript;
                    QvmProgram? fallback = perTaskScript is not null ? archetypeScript : null;

                    if (primary is not null)
                    {
                        brain.SetScript(host, primary, fallback);
                        patrolling++;
                    }
                }

                _brains[view] = brain;
                _brainsByRuntimeId[brain.RuntimeId] = brain;
                index++;
            }
        }

        // AFTER the brains exist: BindCombatRows installs the behaviour on each, so running it before the
        // loop would have found no brains to install it on and every guard would have gone on standing
        // there looking at the player.
        BindCombatRows(config, difficulty);
        PrepareGuardGenerators(levelRoot);

        int hasRun = 0, total = 0;
        foreach (SoldierView v in _groups.SelectMany(g => g.Soldiers).Select(m => m.View))
        {
            total++;
            if (v.Bones.Set.TryGet(v.Soldier.Class.RunAnimation, out _))
            {
                hasRun++;
            }
        }

        _logger.Information(
            LogCategory,
            $"{_brains.Count} soldier(s) with brains ({patrolling} scripted, {perTask} from their own " +
            $"MISSION:AI script); {hasRun} of {total} soldier set(s) hold their class's run clip, the rest " +
            "fall back to the walk when chasing.");
        _logger.Information(
            LogCategory,
            $"Soldiers per nav graph: " +
            string.Join(", ", graphUse.OrderBy(p => p.Key).Select(p => $"graph{p.Key}:{p.Value}")) +
            (unroutable > 0
                ? $"; {unroutable} soldier(s) name a graph the level does not ship (they fight, but cannot patrol)"
                : string.Empty));
        return _brains.Count;
    }

    /// <summary>Puts each soldier's own weapon model in its hand.</summary>
    /// <param name="mission">The mission whose packs hold the weapon models.</param>
    /// <param name="roster">The weapon table, which maps a <c>WEAPON_ID_*</c> to the model that draws it.</param>
    /// <returns>How many soldiers ended up holding something.</returns>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// Three reads join up here. <b>What a soldier carries is authored</b>, as ordinary <c>Gun</c> child
    /// tasks of the <c>HumanSoldier</c> carrying a <c>WEAPON_ID_*</c> — the same mechanism that arms the
    /// player — and the <b>first</b> of them is the one in hand, because everything that asks a human what
    /// it is holding takes slot 0 of the list at <c>Human+1220</c> (see
    /// <see cref="PlayerLoadout.WeaponsOf"/>). <b>Which model</b> comes from that id's row of the weapon
    /// table, whose <c>GunModel</c> field is what the world draws it with. And <b>where it hangs</b> is
    /// <see cref="HumanAttachPoints.ThirdPersonWeapon"/>, bone 22, which <c>0x4600C0</c>'s third-person arm
    /// reads out of the body's own palette at <c>+2928</c>/<c>+2940</c>.
    /// </para>
    /// <para>
    /// <b>Correction to the milestone's own framing.</b> The weapon is <em>not</em> resolved from the
    /// soldier's <c>AIType</c>. The AK in <c>AITYPE_PATROL_AK</c> is part of the <c>settings.qsc</c> key
    /// <c>(AIType, Difficulty)</c>, and a level-1 guard whose type says AK routinely carries an MP5SD —
    /// because its own child task says so. Nothing in the engine maps a type to a model.
    /// </para>
    /// </remarks>
    public int Arm(LoadedMission mission, WeaponTypeRegistry roster)
    {
        ArgumentNullException.ThrowIfNull(mission);
        ArgumentNullException.ThrowIfNull(roster);
        ObjectDisposedException.ThrowIf(_disposed, this);
        _heldWeaponsByModel.Clear();

        Dictionary<string, HeldWeapon?> cache = new(StringComparer.OrdinalIgnoreCase);
        _stationaryWeaponVisuals.Clear();
        _vehicleWeaponVisuals.Clear();
        _vehicleGuns = null;
        _reportedStationaryVisuals.Clear();
        if (_interactions is { } interactions)
        {
            foreach (AiStationaryGun gun in interactions.Items
                .Select(static item => item.StationaryGun)
                .OfType<AiStationaryGun>())
            {
                if (!roster.TryGetByScriptId(gun.WeaponId, out WeaponType weapon))
                {
                    gun.BindWeapon(null);
                    _stationaryWeaponVisuals[gun] = null;
                    _logger.Warning(
                        LogCategory + ".Gunner",
                        $"task={gun.TaskId} weapon={gun.WeaponId} is not in the weapon table.");
                    continue;
                }

                gun.BindWeapon(weapon);

                // 0x478BA0 fires a mounted gun from its own model's magic vertex 2 -- the origin when
                // the model has no such vertex -- because the stand passes it no eye to start from.
                if (mission.TryGetModel(weapon.GunModel, out MefModel? gunModel)
                    && gunModel is not null
                    && gunModel.TryGetMagicVertex(2, out Vector3f muzzle))
                {
                    gun.MuzzleVertex = muzzle;
                }

                if (!cache.TryGetValue(weapon.GunModel, out HeldWeapon? held))
                {
                    held = BuildWeapon(mission, weapon.GunModel);
                    cache[weapon.GunModel] = held;
                }

                _stationaryWeaponVisuals[gun] = held;
                _logger.Information(
                    LogCategory + ".Gunner",
                    $"task={gun.TaskId} weapon={weapon.ScriptId} model={weapon.GunModel} "
                        + $"position=({gun.Position.X:0.##},{gun.Position.Y:0.##},{gun.Position.Z:0.##}) "
                        + $"visual={(held is null ? "missing" : "ready")}");
            }
        }

        int armed = 0;

        foreach (Group group in _groups)
        {
            foreach (Member member in group.Soldiers)
            {
                IReadOnlyList<LoadoutWeapon> carried = PlayerLoadout.WeaponsOf(member.View.Soldier);

                // Human inventory scan 0x44FBA0 examines every carried Weapon task, not only slot 0.
                // Category 4 publishes HumanAI+15869, which is the patrol grenade chooser's gate.
                bool hasGrenade = carried.Any(item =>
                    roster.TryGetByScriptId(item.WeaponId, out WeaponType candidate)
                    && candidate.Category == WeaponCategory.Grenade);
                if (_brains.TryGetValue(member.View, out AiSoldier? brain))
                {
                    brain.SetGrenadeAvailable(hasGrenade);
                }

                if (carried.Count == 0)
                {
                    continue;
                }

                // Slot 0 of the weapon list, which is the first child that named a weapon.
                string weaponId = carried[0].WeaponId;
                if (!roster.TryGetByScriptId(weaponId, out WeaponType weapon))
                {
                    _logger.Warning(LogCategory, $"'{weaponId}' is not in the weapon table.");
                    continue;
                }

                // The Human's held Weapon task exists independently of the third-person mesh. Combat
                // reads that task directly (IGI1 0x48D8E0; IGI2 HumanAIContent_RunFire), so a missing or
                // unrenderable model must not turn an armed guard into an unarmed AI that only stares.
                member.WeaponType = weapon;
                member.CarriedWeaponType = weapon;
                member.AmmoInClip = weapon.RoundsPerClip;

                if (!cache.TryGetValue(weapon.GunModel, out HeldWeapon? held))
                {
                    held = BuildWeapon(mission, weapon.GunModel);
                    cache[weapon.GunModel] = held;
                }

                if (held is null)
                {
                    continue;
                }

                member.Weapon = held;
                _heldWeaponsByModel[weapon.GunModel] = held;
                member.CarriedWeapon = held;
                armed++;
            }
        }

        _logger.Information(
            LogCategory,
            $"{armed} of {Count} soldier(s) hold a weapon model, over {cache.Values.Count(w => w is not null)} " +
            $"distinct model(s).");
        return armed;
    }

    /// <summary>Loads the weapon models the vehicles' guns carry and takes where the runtime poses them.</summary>
    /// <param name="mission">The mission whose packs hold the models.</param>
    /// <param name="weaponModels">Every vehicle gun's weapon model (magic-object argument 1).</param>
    /// <param name="guns">Each gun's weapon object as this tick posed it.</param>
    /// <remarks>
    /// An <c>AIStationaryGun</c> on a vehicle carries its weapon as an object of its own (<c>+452</c>), which the
    /// mount model does not include: a T80's turret has no barrel until its cannon is drawn on it. The weapon's
    /// own children come with it, the M2HB's gun flame among them.
    /// </remarks>
    public void ArmVehicleGuns(
        LoadedMission mission,
        IEnumerable<string> weaponModels,
        Func<IEnumerable<VehicleGunVisual>> guns)
    {
        ArgumentNullException.ThrowIfNull(mission);
        ArgumentNullException.ThrowIfNull(weaponModels);
        ArgumentNullException.ThrowIfNull(guns);
        ObjectDisposedException.ThrowIf(_disposed, this);
        _vehicleWeaponVisuals.Clear();
        foreach (string model in weaponModels)
        {
            if (!_vehicleWeaponVisuals.ContainsKey(model))
            {
                _vehicleWeaponVisuals[model] = BuildWeapon(mission, model);
            }
        }

        _vehicleGuns = _vehicleWeaponVisuals.Count > 0 ? guns : null;
    }

    private HeldWeapon? BuildWeapon(LoadedMission mission, string modelName)
    {
        if (string.IsNullOrEmpty(modelName))
        {
            return null;
        }

        if (!mission.TryGetModel(modelName, out MefModel? model) || model is null)
        {
            _logger.Warning(LogCategory, $"Weapon model '{modelName}' is in no pack this mission opened.");
            return null;
        }

        MeshGeometry? geometry = MefMeshBuilder.Build(model, (float)(1.0 / WorldScene.UnitsPerMetre));
        if (geometry is null)
        {
            _logger.Warning(LogCategory, $"Weapon model '{modelName}' has nothing to draw.");
            return null;
        }

        MeshHandle mesh;
        try
        {
            mesh = _device.CreateMesh(geometry.Vertices, geometry.Indices);
        }
        catch (Exception ex) when (ex is ArgumentException or GraphicsDeviceException or InvalidOperationException)
        {
            _logger.Warning(LogCategory, $"Weapon model '{modelName}' did not upload: {ex.Message}");
            return null;
        }

        (LoadedPack? pack, MtpInstance? instance) = FindInstance(mission, modelName);
        IHeldWeaponPresenter? presentation = null;
        if (_magic is not null && _heldWeapons?.Invoke() is { } candidate)
        {
            if (candidate.Load(mission, modelName))
            {
                presentation = candidate;
            }
            else
            {
                candidate.Dispose();
            }
        }

        HeldWeapon held = new(geometry, mesh, instance, pack, presentation);
        _weapons.Add(held);
        return held;
    }

    /// <summary>Reports what every soldier's AI is actually doing, for diagnosing a level that looks wrong.</summary>
    /// <remarks>
    /// Written for the "why is nobody patrolling?" question, which needs to distinguish four different
    /// failures that look identical on screen: no brain, no script, a script that never asked to patrol, and
    /// a patrol that started but cannot move.
    /// </remarks>
    public void LogAiSummary()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        Dictionary<string, int> activities = [];
        Dictionary<int, int> paths = [];
        int withRoute = 0;
        int moving = 0;

        foreach ((SoldierView view, AiSoldier brain) in _brains)
        {
            string key = brain.Activity.ToString();
            activities[key] = activities.GetValueOrDefault(key) + 1;

            if (brain.Route is { } route)
            {
                withRoute++;
                paths[route.Path.Id] = paths.GetValueOrDefault(route.Path.Id) + 1;
                if (route.Current is { } command && command.Kind.IsMovement())
                {
                    moving++;
                }
            }

            _ = view;
        }

        _logger.Information(
            LogCategory,
            $"AI: {_brains.Count} brain(s); {withRoute} on a patrol path; {moving} on a movement command.");

        foreach ((string activity, int count) in activities.OrderByDescending(p => p.Value))
        {
            _logger.Information(LogCategory, $"AI:   activity {activity}: {count}");
        }

        _logger.Information(
            LogCategory,
            $"AI: patrol paths in use: {string.Join(", ", paths.Keys.Order())}");

        if (_travelSamples > 0)
        {
            double mean = _travelTotal / _travelSamples;
            _logger.Information(
                LogCategory,
                $"AI: walk step over {_travelSamples} sample(s): mean {mean:F1} u/tick " +
                $"({mean * 30.0 / WorldScene.UnitsPerMetre:F2} m/s), max {_travelMax:F1} u/tick. " +
                $"Retail measures 160-185 u/tick (about 1.28 m/s).");
        }
    }

    /// <summary>Logs how the level's fight is going, once per change.</summary>
    /// <remarks>
    /// One line, and only when the numbers move, so it is a running commentary rather than a per-tick
    /// flood. It is what tells a "the guards do nothing" report apart into its three possible causes:
    /// nobody sees the player, somebody sees him and never engages, or somebody engages and never
    /// fires.
    /// </remarks>
    private void ReportCombat()
    {
        int seeing = 0;
        int engaging = 0;
        int chasing = 0;
        foreach (AiSoldier brain in _brains.Values)
        {
            if (brain.SeesEnemy)
            {
                seeing++;
            }

            if (brain.Activity == AiSoldierActivity.Engaging)
            {
                engaging++;
            }

            if (brain.IsMovingInCombat)
            {
                chasing++;
            }
        }

        if (seeing == 0 && engaging == 0 && RoundsFired == 0)
        {
            return;
        }

        // The port-side [aiflow] line, for diffing against the retail probe's capture: every two
        // seconds, one decision-state line per engaged soldier.
        if (engaging > 0 && _tick - _lastCombatDetailTick >= 60)
        {
            _lastCombatDetailTick = _tick;
            foreach (AiSoldier brain in _brains.Values)
            {
                if (brain.Activity == AiSoldierActivity.Engaging)
                {
                    _logger.Information(LogCategory + ".Flow", $"#{brain.RuntimeId} {brain.DescribeCombat()}");
                }
            }
        }

        string report =
            $"{seeing} soldier(s) see the player, {engaging} engaging, {chasing} chasing, {RoundsFired} round(s) fired.";
        if (report == _lastCombatReport)
        {
            return;
        }

        _lastCombatReport = report;
        _logger.Information(LogCategory, $"AI: {report}");
    }

    private int _lastCombatDetailTick;

    /// <summary>Logs each input channel a fight asks for, once per type and channel.</summary>
    /// <remarks>
    /// Deliberately the same shape as the probe's <c>[combat] channel</c> line from the retail game (logged
    /// here under the <c>Soldier.Combat</c> category rather than a tag), so
    /// a run of the port and a capture of the original can be put side by side. The slot is resolved
    /// through the port's transcription of <c>off_54151C</c>; if it disagrees with the probe's, the
    /// transcription is wrong.
    /// </remarks>
    private void ReportChannels(AiSoldier brain)
    {
        foreach (AiChannelWrite write in brain.ChannelWrites)
        {
            string line = $"channel type={(int)brain.Type} channel={(int)write.Channel} "
                + $"-> slot {write.Slot} value={write.Value:0.00}";

            if (_reportedChannels.Add(line))
            {
                _logger.Information(LogCategory + ".Combat", line);
            }
        }
    }

    private static AiCombatWeaponState CombatWeaponState(AiSoldier brain, Member held)
    {
        if (brain.MountedGun is { Weapon: { } mounted } gun)
        {
            return new AiCombatWeaponState(
                gun.AmmunitionSample,
                mounted.RoundsPerClip,
                mounted.MaximumRoundsPerBurst,
                mounted.Category);
        }

        return new AiCombatWeaponState(
            held.AmmoInClip,
            held.WeaponType?.RoundsPerClip ?? 0,
            held.WeaponType?.MaximumRoundsPerBurst ?? 0,
            held.WeaponType?.Category ?? WeaponCategory.Knife);
    }

    /// <summary>Swaps the rendered held model to the live stationary gun while a gunner is mounted.</summary>
    private void UpdateMountedWeapon(Member member, AiSoldier brain)
    {
        if (brain.MountedGun is { } mounted
            && mounted.Weapon is not null
            && _stationaryWeaponVisuals.TryGetValue(mounted, out HeldWeapon? visual)
            && visual is not null)
        {
            member.Weapon = visual;
            member.RenderedMountedGun = mounted;
            return;
        }

        if (member.RenderedMountedGun is not null)
        {
            member.Weapon = member.CarriedWeapon;
            member.RenderedMountedGun = null;
        }
    }

    /// <summary>Chooses the simulation weapon that a dead soldier drops, independent of its drawn model.</summary>
    internal static WeaponType? WeaponForDrop(WeaponType? carriedWeaponType, WeaponType? _renderedWeaponType) =>
        carriedWeaponType;

    /// <summary>Turns a raised trigger into a round, at the weapon's own cadence.</summary>
    /// <remarks>
    /// <para>
    /// The AI never fires: it holds slot 6 down and the weapon decides when a round comes out, which is
    /// the split the original has between <c>0x48D8E0</c> and the human's weapon tick <c>0x462E50</c>.
    /// The clip is what the AI watches to count its burst, so it has to drain here and nowhere else.
    /// </para>
    /// <para>
    /// The reload takes the weapon's own <c>Reload Time</c>; both of the AI's two reload routes reach
    /// the same place, so they are not told apart here.
    /// </para>
    /// </remarks>
    private bool FireIfAsked(SoldierView view, AiSoldier brain, Vector3d enemyFeet, bool enemyCrouching)
    {
        Member? member = FindMember(view);
        AiStationaryGun? mounted = brain.MountedGun;
        WeaponType? weapon = mounted?.Weapon ?? member?.WeaponType;
        if (weapon is null)
        {
            return false;
        }

        if (mounted is not null)
        {
            if (mounted.FireCooldownTicks > 0)
            {
                mounted.FireCooldownTicks--;
            }
        }
        else if (member is not null)
        {
            if (member.FireCooldown > 0)
            {
                member.FireCooldown--;
            }

            if (member.ReloadTicks > 0 && --member.ReloadTicks == 0)
            {
                member.AmmoInClip = weapon.RoundsPerClip;
            }
        }

        AiCombatCommand command = brain.Combat;

        // 0x478BA0 zeroes the gun's rounds-this-pull count whenever the trigger is up.
        if (!command.PullTrigger && member is not null)
        {
            member.RoundsThisPull = 0;
        }

        if (mounted is null && member is not null
            && (command.PressReload || command.ReloadWeaponDirectly)
            && member.ReloadTicks == 0
            && member.AmmoInClip < weapon.RoundsPerClip)
        {
            member.ReloadTicks = Math.Max(1, (int)(weapon.ReloadTimeSeconds * 30f));
            return false;
        }

        // IGI 1 does not hand slot 6 straight to the gun. The HumanSoldier body forwards it only from
        // states 6, 18, 22 and 27 (standing, running, kneeling and prone fire respectively). Previously
        // this method ran before the body state tick, so rounds left during rack/ready/recover clips.
        bool bodyForwardsTrigger = mounted is not null
            || (!brain.CombatStateChangedThisTick && brain.Body.IsFiring);
        bool blocked = mounted is not null
            ? mounted.FireCooldownTicks > 0 || mounted.AmmunitionSample <= 0
            : member is null || member.FireCooldown > 0 || member.AmmoInClip <= 0 || member.ReloadTicks > 0;
        if (!command.PullTrigger || !bodyForwardsTrigger || blocked)
        {
            return false;
        }

        if (mounted is not null)
        {
            if (!mounted.ConsumeRound())
            {
                return false;
            }
            mounted.FireCooldownTicks = GunTiming.TicksPerRound(weapon.RoundsPerMinute);
        }
        else
        {
            member!.AmmoInClip--;
            member.FireCooldown = GunTiming.TicksPerRound(weapon.RoundsPerMinute);
        }
        RoundsFired++;
        if (member is not null)
        {
            member.FiredThisTick = true;
            member.ShotSequence++;
        }

        // The round leaves along where the GUN is pointed, not at the target's true position. Retail's
        // round is a hit-scan ray resolved in the fire tick (GunShot per-tick 0x47B750): the guard's body
        // was turned onto the TRACKED aim point (0x48D8E0 holds fire until the facing is within 2 degrees
        // of it), the ray flies that way, and the hit probability rides the cast's own context (slot+68,
        // loaded from Human+1236 at spawn, sub_474360) to be rolled only when the trace actually meets a
        // human. So the tracked aim's lag against a MOVING target is a real, geometric source of misses,
        // and a round that fails the roll flies on to whatever is behind.
        //
        // A burst-firing guard carries its aim in Combat.TurnTo (the tracked point, started on the target
        // at burst setup, 0x48B9C0). A follow-firing patrol has no aim solve at all (ai.md 13.7) -- it
        // sprays along its own facing whenever the target is inside a 16.3-degree cone, so its ray runs
        // along the body's heading at the aim point's height.
        Vector3d target = brain.Combat.TurnTo ?? SprayTarget(brain, enemyFeet, enemyCrouching);
        Vector3d origin = mounted?.Position ?? brain.EyePosition();
        Vector3d aim = SpreadRound(brain, mounted, weapon, member, origin, target);
        _onRound?.Invoke(origin, aim, brain.HitProbability, weapon, view);

        // Every other soldier near the shot hears it -- the same AIEVENT_GUNSHOT a player's shot raises.
        NotifyGunshot(origin, weapon.AiDetectionRange, except: view);
        return true;
    }

    /// <summary>
    /// Where a guard's round really goes: along the gun, with the weapon's cone about it — <c>0x478BA0</c>,
    /// the same Gun_Fire the player's rounds come out of.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Retail's round leaves along the firer's own orientation (the matrix its fire message carries, or the
    /// gun's), not along a line to the target, and is then turned by rand³ × the weapon's spread about a
    /// random bearing -- the minimum spread for the first four rounds of a pull, the maximum after. A guard
    /// only aims by turning its body and view onto the point (to within 2 degrees before it opens fire), so
    /// those two errors are all the accuracy it has.
    /// </para>
    /// <para>
    /// Measured on the retail game with the probe (mission 1, Hard, a stationary guard at 15-22 m): 242
    /// rounds passed 0-3.5 m from the player and 7 hit -- about 3%. Casting every round exactly at the aim
    /// point and leaving only the probability roll made 107 of 181 hit.
    /// </para>
    /// </remarks>
    private Vector3d SpreadRound(
        AiSoldier brain,
        AiStationaryGun? mounted,
        WeaponType weapon,
        Member? member,
        Vector3d origin,
        Vector3d target)
    {
        double reach = Math.Max(Vector3d.Distance(origin, target), WorldScene.UnitsPerMetre);

        // The emplacement's barrel is aimed by its own task; a carried gun points where the soldier looks.
        Vector3f forward;
        Vector3f right;
        Vector3f up;
        if (mounted is not null)
        {
            Vector3d line = target - origin;
            forward = new Vector3f((float)(line.X / reach), (float)(line.Y / reach), (float)(line.Z / reach)).Normalized();
            right = Vector3f.Cross(forward, new Vector3f(0f, 0f, 1f)).Normalized();
            up = Vector3f.Cross(right, forward).Normalized();
        }
        else
        {
            Matrix3f view = brain.ViewOrientation.Transposed();
            forward = ToFloat(view.Transform(new Vector3d(0.0, 1.0, 0.0)));
            right = ToFloat(view.Transform(new Vector3d(1.0, 0.0, 0.0)));
            up = ToFloat(view.Transform(new Vector3d(0.0, 0.0, 1.0)));
        }

        int roundsThisPull = member?.RoundsThisPull ?? 0;
        if (member is not null)
        {
            member.RoundsThisPull++;
        }

        float cone = GunFiring.ConeAngle(weapon, roundsThisPull);
        Vector3f direction = forward;
        if (cone != 0f)
        {
            Span<float> samples =
            [
                _aiRandom.NextSingle(),
                _aiRandom.NextSingle(),
                _aiRandom.NextSingle(),
                _aiRandom.NextSingle(),
            ];
            (float radius, float bearing) = GunFiring.SampleCone(cone, samples);
            direction = GunFiring.OffsetInCone(forward, right, up, radius, bearing);
        }

        return new Vector3d(
            origin.X + (direction.X * reach),
            origin.Y + (direction.Y * reach),
            origin.Z + (direction.Z * reach));

        static Vector3f ToFloat(Vector3d v) => new((float)v.X, (float)v.Y, (float)v.Z);
    }

    /// <summary>Where a follow-firing patrol's round goes: along its own facing, at the aim point's height.</summary>
    /// <param name="brain">The shooter.</param>
    /// <param name="enemyFeet">The target's feet, in world units.</param>
    /// <param name="enemyCrouching">Whether the target is crouching.</param>
    /// <remarks>
    /// The follow fire has no burst and no aim solve (ai.md §13.7): the trigger is pressed whenever the
    /// target is inside a 16.3° cone about the shooter's forward axis, and the round flies where the BODY
    /// points. The port's facing has no pitch, so the ray borrows the aim point's height and keeps the
    /// facing's horizontal error — which is the part that decides the spray's geometric misses.
    /// </remarks>
    private static Vector3d SprayTarget(AiSoldier brain, Vector3d enemyFeet, bool enemyCrouching)
    {
        Vector3d aimPoint = AiSoldier.AimPointOf(enemyFeet, enemyCrouching);
        Vector3d eye = brain.EyePosition();
        double dx = aimPoint.X - eye.X;
        double dy = aimPoint.Y - eye.Y;
        double reach = Math.Sqrt((dx * dx) + (dy * dy));
        if (reach < 1.0)
        {
            return aimPoint;
        }

        return new Vector3d(
            eye.X + (-Math.Sin(brain.Facing) * reach),
            eye.Y + (Math.Cos(brain.Facing) * reach),
            aimPoint.Z);
    }

    /// <summary>Delivers a gunshot to every soldier in earshot — the hearing side of <see cref="AiEvent.Gunshot"/>.</summary>
    /// <param name="origin">Where the shot was fired, in world units.</param>
    /// <param name="silenced">Whether the weapon is silenced (cuts the range to a thirtieth).</param>
    /// <param name="except">A soldier not to notify — the shooter, which does not hear its own shot as news.</param>
    /// <remarks>
    /// The range gate (30 m, or 1 m silenced) is applied per soldier in
    /// <see cref="AiSoldier.HearGunshot(Vector3d, bool)"/>.
    /// The event is queued and pumped on the soldier's next tick; what it does about it is its archetype
    /// script's business.
    /// </remarks>
    public void NotifyGunshot(Vector3d origin, bool silenced, SoldierView? except = null)
        => NotifyGunshot(
            origin,
            silenced ? AiScriptApi.SilencedGunshotRange : AiScriptApi.GunshotRange,
            except);

    /// <summary>Delivers a gunshot using the firing weapon's exact AI detection range.</summary>
    public void NotifyGunshot(Vector3d origin, double range, SoldierView? except = null)
    {
        int heard = 0;
        foreach ((SoldierView view, AiSoldier brain) in _brains)
        {
            if (!ConditionalContainerGate.IsTaskActive(view.Soldier))
            {
                continue;
            }
            if (except is not null && _brains.TryGetValue(except, out AiSoldier? shooter) && ReferenceEquals(shooter, brain))
            {
                continue;
            }

            // The record carries the shooter's side (the player's, when nobody is excepted), and a
            // listener on that side does not admit it.
            int shooterTeam = except?.Soldier.Team ?? _playerTeam;
            int shooterId = except?.Soldier.Id ?? AiDetectionCandidate.PlayerId;
            if (brain.HearGunshot(origin, range, fromSameSide: shooterTeam == view.Soldier.Team, shooterId))
            {
                heard++;
            }
        }

        ReportNoise("gunshot", heard, range);
    }

    /// <summary>Delivers IGI1's type-21 explosion alert to every active soldier in the blast envelope.</summary>
    /// <param name="origin">The explosion centre in engine units.</param>
    /// <param name="rangeUnits">The authored full-radius plus falloff envelope.</param>
    /// <remarks>
    /// This is intentionally separate from <see cref="NotifyGunshot(Vector3d, double, SoldierView?)"/>. Retail registers and queues
    /// <c>AIEVENT_EXPLOSION</c> (21), so an explosive object must not masquerade as a rifle shot.
    /// </remarks>
    public void NotifyExplosion(
        Vector3d origin,
        double rangeUnits = AiSoldier.FlashbangEventRadiusInUnits)
    {
        int heard = 0;
        foreach ((SoldierView view, AiSoldier brain) in _brains)
        {
            if (!ConditionalContainerGate.IsTaskActive(view.Soldier))
            {
                continue;
            }

            if (brain.ReceiveExplosion(origin, rangeUnits))
            {
                heard++;
            }
        }

        ReportNoise("explosion", heard, rangeUnits);
    }

    /// <summary>Delivers a player's world-impact event to every guard inside its authored radius.</summary>
    /// <param name="impact">The selected nearest world contact.</param>
    /// <param name="shooterPosition">The player's live position for source-based script actions.</param>
    /// <param name="radiusInUnits">Five metres for a round; five or ten metres for a landing.</param>
    public void NotifyGroundImpact(
        Vector3d impact,
        Vector3d shooterPosition,
        double radiusInUnits = AiSoldier.GroundImpactRadiusInUnits)
    {
        int heard = 0;
        foreach ((SoldierView view, AiSoldier brain) in _brains)
        {
            if (!ConditionalContainerGate.IsTaskActive(view.Soldier))
            {
                continue;
            }
            if (brain.ReceiveGroundImpact(impact, shooterPosition, radiusInUnits))
            {
                heard++;
            }
        }

        ReportNoise("impact", heard, radiusInUnits);
    }

    /// <summary>Delivers the player's footstep noise to every guard the step's radius reaches.</summary>
    /// <param name="step">Where the foot landed, in units.</param>
    /// <param name="walkerPosition">The player's live position.</param>
    /// <param name="radiusUnits">The ground material's hearing radius × 4096 — <c>0x463920</c>.</param>
    public void NotifyWalk(Vector3d step, Vector3d walkerPosition, double radiusUnits) =>
        NotifyHumanNoise(AiEvent.Walk, step, walkerPosition, radiusUnits);

    /// <summary>Delivers a noise the player makes to every guard it reaches — see <see cref="AiSoldier.ReceiveHumanNoise"/>.</summary>
    /// <param name="type">The event: a walk, a door, a fence climb or a ladder step.</param>
    /// <param name="position">Where the noise is.</param>
    /// <param name="source">The player's live position.</param>
    /// <param name="radiusUnits">How far it carries.</param>
    public void NotifyHumanNoise(
        AiEvent type,
        Vector3d position,
        Vector3d source,
        double radiusUnits = AiSoldier.HumanNoiseRadiusInUnits)
    {
        int heard = 0;
        foreach ((SoldierView view, AiSoldier brain) in _brains)
        {
            if (!ConditionalContainerGate.IsTaskActive(view.Soldier))
            {
                continue;
            }
            if (brain.ReceiveHumanNoise(type, position, source, radiusUnits))
            {
                heard++;
            }
        }

        ReportNoise(type.ToString().ToLowerInvariant(), heard, radiusUnits);
    }

    /// <summary>Delivers IGI1's visible grenade-takeoff event (type 17).</summary>
    public void NotifyGrenadeThrown(Vector3d origin)
    {
        int seen = 0;
        foreach ((SoldierView view, AiSoldier brain) in _brains)
        {
            if (!ConditionalContainerGate.IsTaskActive(view.Soldier))
            {
                continue;
            }

            if (brain.ReceiveGrenadeThrown(origin))
            {
                seen++;
            }
        }

        ReportNoise("grenade-thrown", seen, AiSoldier.GrenadeLandRadiusInUnits);
    }

    /// <summary>Delivers IGI1's five-metre grenade-land event (type 18).</summary>
    public void NotifyGrenadeLand(
        Vector3d origin,
        double radiusUnits = AiSoldier.GrenadeLandRadiusInUnits)
    {
        int heard = 0;
        foreach ((SoldierView view, AiSoldier brain) in _brains)
        {
            if (!ConditionalContainerGate.IsTaskActive(view.Soldier))
            {
                continue;
            }

            if (brain.ReceiveGrenadeLand(origin, radiusUnits))
            {
                heard++;
            }
        }

        ReportNoise("grenade-land", heard, radiusUnits);
    }

    /// <summary>Delivers IGI1's ten-metre in-view flashbang event (type 19).</summary>
    public void NotifyFlashbang(
        Vector3d origin,
        double radiusUnits = AiSoldier.FlashbangEventRadiusInUnits)
    {
        int affected = 0;
        foreach ((SoldierView view, AiSoldier brain) in _brains)
        {
            if (!ConditionalContainerGate.IsTaskActive(view.Soldier))
            {
                continue;
            }

            if (brain.ReceiveFlashbang(origin, radiusUnits))
            {
                affected++;
            }
        }

        ReportNoise("flashbang", affected, radiusUnits);
    }

    /// <summary>
    /// Offers a soldier's round to every other soldier it passes — <c>0x4606F0</c> for rounds the player did
    /// not fire.
    /// </summary>
    /// <param name="origin">The muzzle.</param>
    /// <param name="liveEnd">Where the round stopped: the surface, the body it met, or the end of its reach.</param>
    /// <param name="shooter">The soldier that fired.</param>
    /// <param name="struck">The soldier the round struck, which takes TAKINGDAMAGE rather than a near miss.</param>
    public void NotifyRoundPassed(Vector3d origin, Vector3d liveEnd, SoldierView shooter, SoldierView? struck)
    {
        ArgumentNullException.ThrowIfNull(shooter);
        int source = shooter.Soldier.Id;
        int heard = 0;
        foreach ((SoldierView view, AiSoldier brain) in _brains)
        {
            if (!ConditionalContainerGate.IsTaskActive(view.Soldier)
                || ReferenceEquals(view, shooter)
                || ReferenceEquals(view, struck))
            {
                continue;
            }

            bool otherSide = view.Soldier.Team != shooter.Soldier.Team;
            if (brain.ReceiveGunshotMiss(origin, liveEnd, shooter.Position, otherSide, source))
            {
                heard++;
            }
        }

        ReportNoise("near-miss", heard, AiSoldier.GunshotMissRadiusInUnits);
    }

    /// <summary>
    /// Delivers the original's separate near-miss event to every guard whose live eye lies within two
    /// metres of this projectile segment.
    /// </summary>
    private void NotifyGunshotMiss(
        Vector3d origin,
        Vector3d end,
        SoldierView? struck,
        Vector3d shooterPosition)
    {
        int heard = 0;
        foreach ((SoldierView view, AiSoldier brain) in _brains)
        {
            if (!ConditionalContainerGate.IsTaskActive(view.Soldier))
            {
                continue;
            }
            // A body the round actually damaged receives TAKINGDAMAGE, not the avoidance event for a miss.
            if (struck is not null && ReferenceEquals(struck, view))
            {
                continue;
            }

            if (brain.ReceiveGunshotMiss(origin, end, shooterPosition))
            {
                heard++;
            }
        }

        ReportNoise("near-miss", heard, AiSoldier.GunshotMissRadiusInUnits);
    }

    /// <summary>One rate-limited delivery line per noise kind — the receipt half of the [aiflow] diff.</summary>
    private void ReportNoise(string kind, int heard, double radiusUnits)
    {
        if (heard == 0 || _tick - _lastNoiseLogTick < 30)
        {
            return;
        }

        _lastNoiseLogTick = _tick;
        _logger.Information(
            LogCategory + ".Flow",
            FormattableString.Invariant(
                $"noise {kind} heard by {heard} soldier(s) (radius {radiusUnits / 4096.0:0.0} m)"));
    }

    private int _lastNoiseLogTick = -100;
    private int _lastDrainLogTick = -100;

    /// <summary>The body a soldier's held weapon becomes once its hand is dead.</summary>
    /// <remarks>
    /// Measured from the weapon's own drawn geometry, so a pistol gets a pistol's capsule and a rifle a
    /// rifle's with nothing named. The vertices are in metres, because that is what the mesh builder
    /// scaled them to; the solver works in engine units.
    /// </remarks>
    private static RagdollBone? WeaponBody(HeldWeapon? weapon)
    {
        if (weapon is null || weapon.Geometry.Vertices.Length == 0)
        {
            return null;
        }

        Vector3f low = new(float.MaxValue);
        Vector3f high = new(float.MinValue);
        foreach (Vertex3D vertex in weapon.Geometry.Vertices)
        {
            low = new Vector3f(
                MathF.Min(low.X, vertex.Position.X),
                MathF.Min(low.Y, vertex.Position.Y),
                MathF.Min(low.Z, vertex.Position.Z));
            high = new Vector3f(
                MathF.Max(high.X, vertex.Position.X),
                MathF.Max(high.Y, vertex.Position.Y),
                MathF.Max(high.Z, vertex.Position.Z));
        }

        float scale = (float)WorldScene.UnitsPerMetre;
        return RagdollWeapon.FromBounds(low * scale, high * scale);
    }

    /// <summary>The skinned model a soldier wears, so its ragdoll can be sized from its own geometry.</summary>
    private MefModel? FindModel(SoldierView view)
    {
        foreach (Group group in _groups)
        {
            foreach (Member member in group.Soldiers)
            {
                if (ReferenceEquals(member.View, view))
                {
                    return group.Model;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// The humans the detection scan offers one soldier this tick — <c>0x489750</c> over each human's own
    /// description (<c>0x45FE30</c>).
    /// </summary>
    /// <remarks>
    /// The global human list's order: the player first, then every soldier whose container is live, the
    /// soldier itself left out. Each is described from this soldier's side, so the scan can register the
    /// living of the other side as enemies and the dead of its own as fallen friends.
    /// </remarks>
    private List<AiDetectionCandidate> CandidatesFor(
        SoldierView self,
        AiSoldier brain,
        Vector3d playerFeet,
        Vector3d playerMark,
        bool playerAlive,
        bool playerCrouching)
    {
        List<AiDetectionCandidate> humans =
        [
            new(AiDetectionCandidate.PlayerId, brain.PlayerIsEnemy, playerAlive, playerMark, playerFeet,
                playerCrouching, _playerTarget),
        ];
        foreach ((SoldierView view, AiSoldier other) in _brains)
        {
            if (ReferenceEquals(view, self) || !ConditionalContainerGate.IsTaskActive(view.Soldier))
            {
                continue;
            }

            Vector3d soldierFeet = new(
                view.Position.X,
                view.Position.Y,
                SnapToGround(view.Position.X, view.Position.Y, view.Position.Z));
            Vector3d soldierMark = new(
                soldierFeet.X, soldierFeet.Y, soldierFeet.Z + AiSoldier.DetectionMarkAboveFeet(crouching: false));
            AiCombatTarget record = _npcTargets.GetValueOrDefault(view)
                ?? (_npcTargets[view] = new AiCombatTarget($"Soldier{view.Soldier.Id}"));
            humans.Add(new AiDetectionCandidate(
                view.Soldier.Id,
                view.Soldier.Team != self.Soldier.Team,
                !view.IsDead && other.Activity != AiSoldierActivity.Dead,
                soldierMark,
                soldierFeet,
                Crouching: false,
                record));
        }

        return humans;
    }

    private Group? FindGroup(SoldierView view)
    {
        foreach (Group group in _groups)
        {
            foreach (Member member in group.Soldiers)
            {
                if (ReferenceEquals(member.View, view))
                {
                    return group;
                }
            }
        }

        return null;
    }

    private Member? FindMember(SoldierView view)
    {
        foreach (Group group in _groups)
        {
            foreach (Member member in group.Soldiers)
            {
                if (ReferenceEquals(member.View, view))
                {
                    return member;
                }
            }
        }

        return null;
    }

    /// <summary>Gives every soldier the combat row its own <c>AIType</c> names.</summary>
    /// <remarks>
    /// A soldier with no <c>HumanAI</c>, or one naming a type the engine does not register, is left off
    /// the table: the original never configures such a body either, so its maximum health stays the 1.0
    /// its constructor set.
    /// </remarks>
    private void BindCombatRows(HumanAiConfigRegistry? config, AiDifficulty difficulty)
    {
        if (config is null)
        {
            return;
        }

        int bound = 0;
        var arms = new Dictionary<AiCombatArm, int>();

        // The resolved accuracy per AI type actually in the level, so "I die too fast even on Easy" can be
        // read straight off the log: point-blank / cone-end hit chance is the row's HitProbability pair, and
        // a guard that shows 1.000 / 0.250 got the built-in default -- settings.qsc did not supply a row for
        // this (type, difficulty) and the difficulty is doing nothing. A genuinely lower number is the data.
        var accuracyByType = new Dictionary<AiType, (int Count, AiCombatParameters Row)>();

        foreach (Group group in _groups)
        {
            foreach (Member member in group.Soldiers)
            {
                if (FindMind(member.View)?.ResolvedType is not { } type)
                {
                    continue;
                }

                AiCombatParameters row = config.Parameters(type, difficulty);
                _combat[member.View] = row;
                bound++;

                (int count, AiCombatParameters _) = accuracyByType.GetValueOrDefault(type);
                accuracyByType[type] = (count + 1, row);

                if (_brains.TryGetValue(member.View, out AiSoldier? brain))
                {
                    Member held = member;
                    brain.SetCombat(type, row, () => CombatWeaponState(brain, held));
                    arms[brain.Arm] = arms.GetValueOrDefault(brain.Arm) + 1;
                }
            }
        }

        // The arm decides whether a soldier can chase at all: only the patrol (move-and-fight) arm hunts and
        // advances; the guard arm stands its ground. Logged so a "they don't chase" report can be told apart.
        string split = string.Join(", ", arms.OrderBy(a => a.Key).Select(a => $"{a.Key}:{a.Value}"));
        _logger.Information(
            LogCategory,
            $"AI: {bound} soldier(s) configured at {HumanAiConfigRegistry.NameOf(difficulty)} ({difficulty}); arms {split}.");

        // One line per AI type: the hit chance a guard of that type actually fires with, at this difficulty.
        // "hit@0m" is point-blank (where you die), "hit@far" is the cone's end; the round handler rolls a
        // uniform against the range-lerped value between them. clip% is the fraction of a magazine a burst
        // spends. 1.000/0.250 = the default, i.e. settings.qsc gave no row for this pair.
        foreach ((AiType type, (int count, AiCombatParameters row)) in accuracyByType.OrderBy(e => e.Key))
        {
            string name = HumanAiConfigRegistry.NameOf(difficulty);
            _logger.Information(
                LogCategory,
                FormattableString.Invariant(
                    $"AI accuracy [{type}] x{count} @ {name}: hit@0m={row.HitProbabilityAtMinimumRange:F3} hit@far={row.HitProbabilityAtMaximumRange:F3} clip%={row.MinimumClipFraction:F2}+{row.RandomClipFraction:F2} aimPrec={row.AimPrecision:F2}"));
        }
    }

    /// <summary>The <c>HumanAI</c> child a soldier's body carries, if the level gave it one.</summary>
    /// <summary>A level task's position by id — what the activate walk aims at.</summary>
    /// <param name="task">The subtree to search.</param>
    /// <param name="id">The task id the script named.</param>
    /// <param name="position">Receives the task position when present.</param>
    /// <returns>The position, or default when the id names nothing with one.</returns>
    private static bool TryGetTaskPosition(GameTask task, int id, out Vector3d position)
    {
        if (task.Id == id)
        {
            if (task is DeclaredTask declared
                && declared.TryGetValue("Position", out object? pos)
                && pos is Vector3d p)
            {
                position = p;
                return true;
            }

            position = default;
            return false;
        }

        foreach (GameTask child in task.Children)
        {
            if (TryGetTaskPosition(child, id, out position))
            {
                return true;
            }
        }

        position = default;
        return false;
    }

    private static HumanAiTask? FindMind(SoldierView view)
    {
        foreach (GameTask child in view.Soldier.Children)
        {
            if (child is HumanAiTask mind)
            {
                return mind;
            }
        }

        return null;
    }

    private QvmProgram? LoadScript(ResourceRegistry scripts, Dictionary<string, QvmProgram?> cache, string qscPath)
    {
        string path = qscPath.Replace(".qsc", ".qvm", StringComparison.Ordinal);
        if (cache.TryGetValue(path, out QvmProgram? cached))
        {
            return cached;
        }

        QvmProgram? program = null;
        try
        {
            if (scripts.TryLoad(path, out ReadOnlyMemory<byte> bytes))
            {
                program = QvmProgram.Read(bytes);
            }
            else
            {
                // Most HumanAI tasks have an override, but it is not required: an absent one runs the
                // archetype selected by AIType instead.
                _logger.Debug(LogCategory, $"No behaviour script at '{path}'.");
            }
        }
        catch (Exception ex) when (ex is IOException or QvmFormatException or InvalidOperationException)
        {
            _logger.Warning(LogCategory, $"Could not read behaviour script at '{path}': {ex.Message}");
        }

        cache[path] = program;
        return program;
    }

    /// <summary>The prepared rig for an animation set, built once and shared by every corpse wearing it.</summary>
    /// <remarks>
    /// Keyed by the SET rather than the skeleton, because the joint limits are measured from the set's
    /// own clips — the animators' demonstration of how far each joint is meant to move, which is what
    /// gives a knee a hinge without any bone being named. Building it sweeps every rotation key in the
    /// set twice, so it must happen once per mission and not once per death.
    /// </remarks>
    private RagdollSkeleton? RagdollRigFor(BoneAnimationSet set, MefModel? model)
    {
        if (_ragdollRigs.TryGetValue(set, out RagdollSkeleton? rig))
        {
            return rig;
        }

        try
        {
            rig = RagdollSkeleton.Build(set.Skeleton, RagdollLimits.Measure(set), model);
        }
        catch (ArgumentException ex)
        {
            // A rig the solver cannot use is not fatal: the retail death clip is still playing
            // underneath, so the body falls the way it always did and only the remaster is skipped.
            _logger.Warning(LogCategory, $"Skeleton {set.Skeleton.Id} cannot drive a ragdoll: {ex.Message}");
            return null;
        }

        _ragdollRigs[set] = rig;
        return rig;
    }

    /// <summary>What corpses collide against — the same two queries a living body uses.</summary>
    private RagdollWorld RagdollWorldForCorpses()
    {
        if (_ragdollWorld is { } world)
        {
            return world;
        }

        AiSoldier.GroundQuery? ground = MakeGroundQuery();
        _ragdollWorld = new RagdollWorld(
            _cast,
            ground is null ? null : (double x, double y, double z, out double groundZ) => ground(x, y, z, out groundZ))
        {
            Triangles = _dropTriangles,
            Trace = _dropTrace,
        };
        return _ragdollWorld;
    }

    /// <summary>
    /// Starts a simulated corpse from the pose a body was last drawn in, and pushes it with the round.
    /// </summary>
    /// <returns>The body, or <see langword="null"/> when ragdolls are off or the rig cannot drive one.</returns>
    /// <remarks>
    /// The origin has to be the placement the body was being drawn at — position, ground snap and the
    /// clip's own root height — or the corpse jumps at the instant the physics takes over. That is the
    /// same triple <see cref="TickPlacementOf"/> builds, in units rather than metres.
    /// </remarks>
    private RagdollBody? SeedRagdoll(
        SoldierView view,
        Vector3d originInUnits,
        Matrix3f orientation,
        int bone,
        Vector3d travel,
        WeaponCategory category,
        Vector3d carriedVelocity,
        MefModel? model,
        HeldWeapon? weapon)
    {
        if (!GameplayCVars.Ragdoll.Value || RagdollRigFor(view.Bones.Set, model) is not { } rig)
        {
            return null;
        }

        RagdollBody body;
        try
        {
            body = RagdollBody.Seed(
                rig,
                view.UnrotatedPose(),
                originInUnits,
                orientation,
                WeaponBody(weapon),
                HumanAttachPoints.ThirdPersonWeapon);
        }
        catch (ArgumentException ex)
        {
            _logger.Warning(LogCategory, $"Could not seed a ragdoll: {ex.Message}");
            return null;
        }

        body.AddVelocity(carriedVelocity);
        RagdollImpulse.Apply(
            body,
            bone,
            travel,
            RagdollImpulse.MomentumOf(rig, category, GameplayCVars.RagdollForce.Value));
        return body;
    }

    private AiSoldier.GroundQuery? MakeGroundQuery()
    {
        IGroundHeightField? terrain = _terrain;
        WorldRayCast? cast = _cast;
        if (terrain is null && cast is null)
        {
            return null;
        }

        return (x, y, z, out groundZ) =>
        {
            groundZ = double.MinValue;
            bool found = false;

            if (cast is not null
                && cast(
                    new Vector3d(x, y, z),
                    new Vector3d(x, y, z - ProbeBelowInUnits - ProbeAboveInUnits),
                    out WorldRayHit hit))
            {
                groundZ = hit.Position.Z;
                found = true;
            }

            if (terrain is not null
                && terrain.TryGetGround(x, y, z, out TerrainGround ground)
                && ground.Height >= z - ProbeBelowInUnits - ProbeAboveInUnits
                && ground.Height <= z)
            {
                if (ground.Height > groundZ)
                {
                    groundZ = ground.Height;
                }

                found = true;
            }

            return found;
        };
    }

    private static float GammaOf(SoldierView view) => view.Soldier.Gamma;

    /// <summary>A round a soldier has just fired.</summary>
    /// <param name="origin">Where it leaves, in world units — the soldier's eye.</param>
    /// <param name="aim">Where it is aimed, in world units.</param>
    /// <param name="hitProbability">
    /// The chance the AI gave it of being on target — <c>Human+1236</c>, which the weapon reads with
    /// every round.
    /// </param>
    /// <param name="weapon">What fired it.</param>
    /// <param name="shooter">The soldier that fired, or <see langword="null"/> for a player round.</param>
    public delegate void SoldierRound(
        Vector3d origin,
        Vector3d aim,
        float hitProbability,
        WeaponType weapon,
        SoldierView? shooter);

    /// <summary>Sets where a soldier's rounds go.</summary>
    /// <param name="handler">The handler, or <see langword="null"/> to let rounds vanish.</param>
    /// <remarks>
    /// The soldier raises the trigger; this is where the round becomes a cast. Keeping the two apart is
    /// the original's own split — the AI writes an input slot and the body's weapon path does the rest.
    /// </remarks>
    public void SetRoundHandler(SoldierRound? handler) => _onRound = handler;

    /// <summary>Gets where soldiers' rounds go, so another AI gun (a vehicle's) can fire through the same cast.</summary>
    public SoldierRound? RoundHandler => _onRound;

    /// <summary>Gets how many rounds the level's soldiers have fired.</summary>
    public int RoundsFired { get; private set; }

    /// <summary>Connects each retail GuardGenerator to the one authored soldier subtree it owns.</summary>
    private void PrepareGuardGenerators(GameTask? levelRoot)
    {
        _guardGenerators.Clear();
        if (levelRoot is null)
        {
            return;
        }

        foreach (GuardGeneratorTask generator in FindGuardGenerators(levelRoot))
        {
            HumanSoldierTask? authored = generator.Children.OfType<HumanSoldierTask>().FirstOrDefault();
            SoldierView? view = authored is null
                ? null
                : _brains.Keys.FirstOrDefault(candidate => ReferenceEquals(candidate.Soldier, authored));
            if (view is null)
            {
                _logger.Warning(
                    LogCategory,
                    $"{generator.Name} has no resolved HumanSoldier child; its authored spawn cannot run.");
                continue;
            }

            _guardGenerators[generator] = view;
        }

        if (_guardGenerators.Count > 0)
        {
            _logger.Information(
                LogCategory,
                $"{_guardGenerators.Count} GuardGenerator(s) own bounded authored soldier templates.");
        }
    }

    /// <summary>Runs the native generator decision before any live soldier is advanced this tick.</summary>
    /// <summary>A GuardGenerator's update (<c>0x487EE0</c>): its expression, and a replacement for a dead guard.</summary>
    private void TickGuardGenerator(GuardGeneratorTask generator)
    {
        if (!_guardGenerators.TryGetValue(generator, out SoldierView? view))
        {
            return;
        }

        bool isOn = _guardExpressions?.EvaluateCondition(generator.GenerateGuards, emptyValue: false, generator)
            ?? generator.GenerateGuards.Trim() is "1" or "TRUE" or "true";
        // Publish the first authored life immediately, but do not consume a bounded replacement
        // until its mesh and runtime subtree have actually been created. A transient upload failure
        // must leave the generator eligible to retry on the next tick.
        GuardSpawnDecision decision = generator.Advance(isOn, childIsDead: false);
        if (decision != GuardSpawnDecision.FirstSpawn
            && isOn
            && view.IsDead
            && generator.SpawnCount < generator.MaxSpawns
            && SpawnGeneratedGuard(generator, view) is { } fresh)
        {
            _ = generator.Advance(isOn: true, childIsDead: true);
            _guardGenerators[generator] = fresh;
        }
    }

    /// <summary>Clones a fresh rendered Human/HumanAI subtree while leaving the killed body in the world.</summary>
    private SoldierView? SpawnGeneratedGuard(GuardGeneratorTask generator, SoldierView deadView)
    {
        Member? deadMember = FindMember(deadView);
        if (deadMember is null || !_brains.TryGetValue(deadView, out AiSoldier? deadBrain))
        {
            _logger.Warning(LogCategory, $"{generator.Name} could not clone its missing rendered Human subtree.");
            return null;
        }

        Group? group = _groups.FirstOrDefault(candidate => candidate.Soldiers.Contains(deadMember));
        if (group is null)
        {
            _logger.Warning(LogCategory, $"{generator.Name} could not find its rendered Human group.");
            return null;
        }

        SoldierView freshView = new(deadView.Soldier, deadView.Bones.Set);
        Vector3d spawn = new(
            deadMember.Position.X * WorldScene.UnitsPerMetre,
            deadMember.Position.Y * WorldScene.UnitsPerMetre,
            deadMember.Position.Z * WorldScene.UnitsPerMetre);
        freshView.PlaceAt(spawn, Matrix3f.FromEngineEulerAngles(0f, 0f, deadView.Soldier.Gamma));

        BonePose initialPose = freshView.UnrotatedPose();
        Vertex3D[] initialSkin = new Vertex3D[group.Geometry.Vertices.Length];
        group.Geometry.Vertices.CopyTo(initialSkin, 0);
        bool initialSkinReady = false;
        if (group.Model is { } skinModel)
        {
            try
            {
                MefSkinner.Skin(
                    skinModel,
                    initialPose,
                    (float)(1.0 / WorldScene.UnitsPerMetre),
                    initialSkin);
                initialSkinReady = true;
            }
            catch (ArgumentException)
            {
                initialSkinReady = false;
            }
        }

        MeshHandle mesh;
        try
        {
            mesh = _device.CreateAnimatedMesh(initialSkin, group.Geometry.Indices);
        }
        catch (Exception ex) when (ex is ArgumentException or GraphicsDeviceException or InvalidOperationException)
        {
            _logger.Warning(LogCategory, $"{generator.Name} could not upload its cloned Human: {ex.Message}");
            return null;
        }

        HeldWeapon? weapon = null;
        if (deadMember.WeaponType is { } weaponType)
        {
            _heldWeaponsByModel.TryGetValue(weaponType.GunModel, out weapon);
        }

        Member freshMember = new(freshView, deadMember.Position, mesh)
        {
            IsCutsceneActor = false,
            DrawnPose = initialPose,
            SkinScratch = initialSkin,
            SkinnedPose = initialSkinReady ? initialPose : null,
            PreviousSkinnedPose = initialSkinReady ? initialPose : null,
            WeaponType = deadMember.WeaponType,
            Weapon = weapon,
            AmmoInClip = deadMember.WeaponType?.RoundsPerClip ?? 0,
        };
        group.Soldiers.Add(freshMember);

        if (_detectorMarkerScaleMetres.TryGetValue(deadView, out float markerScale))
        {
            _detectorMarkerScaleMetres[freshView] = markerScale;
        }
        if (_combat.TryGetValue(deadView, out AiCombatParameters? combat))
        {
            _combat[freshView] = combat;
        }
        _health[freshView] = NewHealth(freshView);

        AiSoldier freshBrain = deadBrain.Respawned(
            freshView.Position,
            freshView.Soldier.Gamma,
            brain => CombatWeaponState(brain, freshMember));
        // The corpse remains registered: sharing its runtime ID would advance one weapon sound
        // twice per tick and send replies to the old guard's alerts to its replacement instead.
        // Keep the authored Soldier.Id for mission queries, but give each live instance its own ID.
        freshBrain.RuntimeId = checked(_brainsByRuntimeId.Keys.DefaultIfEmpty(0).Max() + 1);
        BindViewTracking(freshBrain, freshView);
        freshBrain.SetDefaultTarget(_playerTarget);
        _brains[freshView] = freshBrain;
        _brainsByRuntimeId[freshBrain.RuntimeId] = freshBrain;
        Count++;

        _logger.Information(
            LogCategory,
            $"{generator.Name} spawned {generator.SpawnCount}/{generator.MaxSpawns}: "
                + $"{freshView.Soldier.Name} cloned at its authored position; the previous body remains.");
        return freshView;
    }

    private void BindViewTracking(AiSoldier brain, SoldierView view)
    {
        brain.SetScriptSound((name, attached) => _scriptSound?.Invoke(name, view.Position, attached));
        brain.SetEyeTransform(
            () =>
            {
                BonePose pose = view.Pose();
                if (HumanAttachPoints.AiEye >= pose.BoneCount)
                {
                    return new Vector3d(
                        view.Position.X,
                        view.Position.Y,
                        view.Position.Z + AiSoldier.HumanSeparationEyeHeight);
                }

                // The bone offset is from the model's origin, which is drawn above the feet by the model's
                // lowest vertex -- as IGI.exe's Human+32 stands above them. Adding it to the feet put the
                // eye at waist height, below every parapet a guard looks over.
                Vector3f eye = pose.Positions[HumanAttachPoints.AiEye];
                double originLift = FindGroup(view) is { } group ? -group.LowestVertexZ * WorldScene.UnitsPerMetre : 0.0;
                return new Vector3d(
                    view.Position.X + eye.X,
                    view.Position.Y + eye.Y,
                    view.Position.Z + originLift + eye.Z);
            },
            // The attachment supplies the animated eye POSITION only; its local rotation differs by
            // skeleton and clip. The snapshot (0x489960) tests against the view matrix: the body's heading
            // tipped by the view's own pitch.
            //
            // But only while the soldier is DRAWN. 0x44F410 copies the eye's matrix out of the bone palette
            // ([Human+0x70]+0x2B8), and the palette is filled by the skinning in the draw callback
            // (0x49F700), which runs only for a soldier inside the render camera's frustum (0x4D06C0). A
            // guard off the player's screen keeps looking the way it last did on it -- and one never drawn
            // looks along its rest pose, about five degrees off world +Y whatever its body faces. Measured
            // on the retail game with the probe: seventeen of twenty-one mission-1 guards shared that exact
            // rest matrix, and turning the view onto one made its eye follow its body within two ticks.
            () =>
            {
                Member? member = FindMember(view);
                if (member is null)
                {
                    return brain.ViewOrientation;
                }

                if (member.InView)
                {
                    member.EyeOrientation = brain.ViewOrientation;
                }

                return member.EyeOrientation ?? RestEyeOrientation;
            });
    }

    /// <summary>
    /// The eye matrix of a guard the player has never drawn -- bone 5's rest orientation, unrotated by the
    /// body, read off every undrawn guard of mission 1 by the probe (the cone test's snapshot, +24).
    /// </summary>
    internal static readonly Matrix3f RestEyeOrientation = new(
        0.9956f, -0.0927f, 0.0076f,
        0.0919f, 0.9926f, 0.0784f,
        -0.0148f, -0.0774f, 0.9968f);

    /// <summary>
    /// The soldier model's bounding radius the draw callback's frustum test scales by 1.41, in metres.
    /// </summary>
    /// <remarks>
    /// An approximation: retail reads it from the model's LOD record (<c>+60</c>), which is not parsed yet, and
    /// the skinned geometry's own bounds are not in posed model space. A metre about the model origin -- the
    /// pelvis, 0.8 m under the eye bone -- covers a standing body, and only decides guards at the screen edge.
    /// </remarks>
    private const float SkinCullRadiusMetres = 1.0f;

    /// <summary>How far from the camera a drawn soldier's eye bone is still refreshed, in metres.</summary>
    /// <remarks>
    /// Measured on the retail game: past about 51 m the soldier is drawn at its fifth LOD (0x4CFF70 returns
    /// 4; 50.6 m gave 3 and 51.9 m gave 4), and that LOD's skinning leaves the eye bone's palette entry as it
    /// was -- a guard skinned at 65 m kept its rest-pose eye, one at 50 m followed its body. The LOD pick is by
    /// view depth scaled by the field of view, so zoomed optics would reach further; that is not modelled.
    /// </remarks>
    private const double EyeRefreshDistanceMetres = 51.0;

    /// <summary>
    /// Records which soldiers the main view draws closely enough to refresh their eyes this frame -- the
    /// frustum sphere test retail's draw callback makes before it skins a body (<c>0x4D06C0</c>, the model's
    /// radius times 1.41), and the LOD range within which that skinning reaches the eye bone.
    /// </summary>
    /// <param name="camera">The frame's main camera.</param>
    /// <param name="projection">Its projection.</param>
    /// <remarks>Only the main view counts: the shadow cascades' light cameras skin nothing in the original.</remarks>
    public void ObserveView(in Camera3D camera, in Matrix4f projection)
    {
        _lastObservedCamera = camera;
        FrustumCulling.Frustum frustum = FrustumCulling.Frustum.FromProjection(projection);
        BoundingSphere test = new(Vector3f.Zero, SkinCullRadiusMetres * 1.41f);
        foreach (Group group in _groups)
        {
            foreach (Member member in group.Soldiers)
            {
                member.InView = ConditionalContainerGate.IsTaskActive(member.View.Soldier)
                    && member.HasFrameState
                    && Vector3d.Distance(member.DrawnWhere, camera.Position) <= EyeRefreshDistanceMetres
                    && frustum.SphereVisible(camera.CreateModelView(member.DrawnWhere, member.DrawnOrientation), test);
            }
        }
    }

    private static IEnumerable<GuardGeneratorTask> FindGuardGenerators(GameTask task)
    {
        foreach (GameTask child in task.Children)
        {
            if (child is GuardGeneratorTask generator)
            {
                yield return generator;
            }

            foreach (GuardGeneratorTask nested in FindGuardGenerators(child))
            {
                yield return nested;
            }
        }
    }

    /// <summary>Advances every soldier's animation by one tick and re-skins the groups.</summary>
    /// <remarks>
    /// <para>
    /// <b>Nothing advanced a soldier's rig before this existed</b>, so every guard in the level stood frozen on
    /// the first frame of its stand animation — which reads as a default A-pose. Soldiers are grouped by
    /// (model, skeleton, animation) and share one skinned mesh, so ticking the group's first soldier and
    /// re-skinning from its pose animates the whole group: they run the same clip from the same start, so they
    /// share a pose by construction.
    /// </para>
    /// <para>
    /// The device's meshes are immutable, so this destroys and re-uploads. There are only a handful of groups
    /// in a level, so it is a few uploads a tick — worth replacing with a dynamic vertex buffer, but not worth
    /// leaving the guards frozen for.
    /// </para>
    /// </remarks>
    /// <param name="enemy">The enemy's eye position, which the view cones are tested against.</param>
    /// <param name="enemyAlive">
    /// Whether the enemy is still a target. A dead player is not: the detection scan (<c>0x489750</c>)
    /// unregisters a human that reports itself invalid, so the guards lose it and stop firing at the
    /// corpse — passing <see langword="false"/> reproduces that.
    /// </param>
    /// <param name="enemyFeet">
    /// The enemy task's position — its feet, what combat aims at (<c>sub_451380</c>). Defaults to
    /// <paramref name="enemy"/> when the caller has no body to speak of.
    /// </param>
    /// <param name="enemyCrouching">Whether the enemy is crouching, which halves the aim raise.</param>
    public void Tick(
        Vector3d enemy = default,
        bool enemyAlive = true,
        Vector3d? enemyFeet = null,
        bool enemyCrouching = false)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        // Standalone (a renderer outside a level walk: the menu backdrop, tests): the same per-task updates, run
        // in one pass. Inside a mission the level walk runs each where the tree places it (AttachTo).
        SoldierEnemy fixedEnemy = new(enemy, enemyAlive, enemyFeet, enemyCrouching);
        BeginTick(() => fixedEnemy);
        foreach (GuardGeneratorTask generator in _guardGenerators.Keys.ToArray())
        {
            TickGuardGenerator(generator);
        }

        if (_doors is { } doors)
        {
            foreach (PlacedDoor door in doors.Doors)
            {
                doors.Tick(door);
                DoorTicked(door);
            }
        }

        if (_alarmControls is { } alarmControls)
        {
            alarmControls.Tick(_tick);
            AlarmControlTicked();
        }

        foreach ((SoldierView view, AiSoldier brain) in _brains)
        {
            TickSoldier(view, brain);
        }

        EndTick();
    }

    /// <summary>Puts the soldiers, their guard generators, the doors and the alarm controls into the level's walk.</summary>
    /// <param name="walker">The level walk.</param>
    /// <remarks>
    /// <para>
    /// The original has no soldier pass: each <c>HumanSoldier</c> updates where the walk reaches it
    ///, its children -- HumanView, Shadow, the gun, then the HumanAI
    /// whose CHANNELS packet moves it -- before the next soldier starts. A soldier therefore sees the soldiers
    /// before it in the tree where they are this tick, and those after it (and the player, late in
    /// <c>ConditionalContainer_560</c>) where they were last tick.
    /// </para>
    /// <para>
    /// A <c>GuardGenerator</c> (<c>0x487EE0</c>) decides and clones first, then walks its soldier; a <c>Door</c>
    /// (<c>0x446D10</c>) and an <c>AlarmControl</c> (<c>0x4408A0</c>) walk their children first, then update.
    /// Call <see cref="BeginTick"/> before the walk and <see cref="EndTick"/> after it.
    /// </para>
    /// </remarks>
    public void AttachTo(LevelUpdateWalker walker)
    {
        ArgumentNullException.ThrowIfNull(walker);
        ObjectDisposedException.ThrowIf(_disposed, this);
        _walker = walker;
        HashSet<GameTask> soldiers = new(ReferenceEqualityComparer.Instance);
        foreach (SoldierView view in _brains.Keys)
        {
            if (soldiers.Add(view.Soldier))
            {
                walker.Bind(view.Soldier, task =>
                {
                    TickSoldierTask(task);
                    walker.RunChildren(task);
                });
            }
        }

        foreach (GuardGeneratorTask generator in _guardGenerators.Keys)
        {
            walker.Bind(generator, task =>
            {
                TickGuardGenerator((GuardGeneratorTask)task);
                walker.RunChildren(task);
            });
        }

        _doors?.AttachTo(walker, DoorTicked);
        _alarmControls?.AttachTo(walker, () => _tick, _ => AlarmControlTicked());
    }

    /// <summary>Opens a tick: the tick count, and where the enemy (the player) is read from.</summary>
    /// <param name="enemy">
    /// Reads the player as a soldier sees it at the moment the soldier updates -- live, so a soldier ahead of the
    /// player in the tree sees last tick's pose.
    /// </param>
    public void BeginTick(Func<SoldierEnemy> enemy)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(enemy);
        _tick++;
        _enemy = enemy;
        _ticked.Clear();
        _collectDiagnostics = _logger.IsEnabled(LogLevel.Information);
        bool collectDiagnostics = _collectDiagnostics;

        // The liveness half of the [aiflow] diff: proof the AI tick is running at all, ten seconds apart.
        // The patrol/activity counts ride the same line so a capture can show WHEN patrols engage after
        // the intro cutscene, without a separate per-tick flood.
        if (collectDiagnostics && _tick % 300 == 1)
        {
            int onPatrol = _brains.Values.Count(b => b.Route is not null);
            Dictionary<string, int> activity = [];
            foreach (AiSoldier brain in _brains.Values)
            {
                string key = brain.Activity.ToString();
                activity[key] = activity.GetValueOrDefault(key) + 1;
            }

            string activityText = string.Join(", ", activity.OrderByDescending(p => p.Value).Select(p => $"{p.Key}={p.Value}"));
            _logger.Information(
                LogCategory + ".Flow", $"heartbeat tick={_tick} brains={_brains.Count} onPatrol={onPatrol} [{activityText}]");

            // Diagnostic-only: the Soldier.Trace category at Debug adds per-soldier detail for the friendlies
            // (ids 400-405, plus the heli crews by their mind ids), so a long lockstep run shows what they
            // actually do. At Trace it extends the same record to every active human, which is useful for
            // checking first-base guard grounding without changing the normal diagnostic volume.
            if (_logger.IsEnabled(LogLevel.Debug, TraceLogCategory))
            {
                bool traceAll = _logger.IsEnabled(LogLevel.Trace, TraceLogCategory);
                foreach (KeyValuePair<SoldierView, AiSoldier> pair in _brains)
                {
                    HumanSoldierTask soldier = pair.Key.Soldier;
                    HumanAiTask? mind = soldier.Children.OfType<HumanAiTask>().FirstOrDefault();
                    int mindId = mind?.Id ?? -1;
                    bool friendly = soldier.Id is >= 400 and <= 405
                        || soldier.Id == 428
                        || mindId is >= 540 and <= 544
                        || mindId is >= 1234 and <= 1238
                        || soldier.Id == 666;
                    if (!friendly && !traceAll)
                    {
                        continue;
                    }

                    AiSoldier brain = pair.Value;
                    string traceTag = friendly ? "friendly" : "guard";
                    string route = brain.Route is null ? "-" : $"path {brain.Route.Path.Id} cmd={brain.Route.Current?.Kind}";
                    string last = brain.LastActions.Count == 0
                        ? "none"
                        : string.Join(",", brain.LastActions.Select(a => $"{a.Name.Split('_')[^1]}({string.Join(",", a.Arguments)}) "));
                    string pending = string.Join(",", Enumerable.Range(0, 24).Select(i => (AiEvent)i).Where(t => brain.Events.PendingCount(t) > 0).Select(t => $"{t}x{brain.Events.PendingCount(t)}"));
                    string dispatched = brain.LastDispatchedEvent?.ToString() ?? "-";
                    double groundZ = SnapToGround(pair.Key.Position.X, pair.Key.Position.Y, pair.Key.Position.Z);
                    double groundGap = (pair.Key.Position.Z - groundZ) / WorldScene.UnitsPerMetre;
                    _logger.Debug(
                        TraceLogCategory,
                        $"{traceTag} soldier {soldier.Id} mind {mindId} {brain.Activity} {route} " +
                        $"evt={dispatched} last={last} pending=[{pending}] lastTick={brain.LastTick} " +
                        $"anim={pair.Key.CurrentAnimation} rm={Math.Abs(pair.Key.RootMotion().Y):0.0} " +
                        $"pos=({pair.Key.Position.X / 4096.0:0.0},{pair.Key.Position.Y / 4096.0:0.0}) " +
                        $"bpos=({brain.Position.X / 4096.0:0.0},{brain.Position.Y / 4096.0:0.0}) cur={brain.CurrentNode} dest={brain.DestinationNode} " +
                        $"target={brain.Target?.Target ?? "-"} " +
                        $"ground={groundZ / WorldScene.UnitsPerMetre:0.00} gap={groundGap:0.000}m " +
                        $"fwd={brain.LastSteeringForward:0.0} rmT={brain.LastAdvanceRm:0.0} st={brain.LastSteeringOutcome} " +
                        $"moved={(brain.LastMovedTo is { } mv ? $"({mv.X / 4096.0:0.0},{mv.Y / 4096.0:0.0})" : "-")} facing={brain.LastFacingDeg:0.0} " +
                        $"wp={(brain.DebugWaypoint is { } wpp ? $"{wpp.X / 4096.0:0.000},{wpp.Y / 4096.0:0.000}" : "-")}");

                    foreach ((SoldierView otherView, AiSoldier other) in _brains)
                    {
                        if (ReferenceEquals(other, brain))
                        {
                            continue;
                        }

                        double dx = other.Position.X - brain.Position.X;
                        double dy = other.Position.Y - brain.Position.Y;
                        double h = Math.Sqrt((dx * dx) + (dy * dy)) / 4096.0;
                        if (h < 3.0)
                        {
                            _logger.Debug(
                                TraceLogCategory,
                                $"{traceTag}   near s{soldier.Id}: s{otherView.Soldier.Id} m{otherView.Soldier.Children.OfType<HumanAiTask>().FirstOrDefault()?.Id} {h:0.000} m {other.Activity}");
                        }
                    }
                }
            }
        }
    }

    /// <summary>Publishes the living soldiers' collision bodies where they stand now.</summary>
    /// <remarks>
    /// The player's wall sweep is their only reader, and the player updates after every soldier, so publishing
    /// here before the player moves is what re-registering each soldier as it moves would give.
    /// </remarks>
    public void PublishCollisionBodies()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        SyncCollisionBodies();
    }

    /// <summary>Closes a tick: the soldiers' collision, corpses, dropped weapons and skinning.</summary>
    public void EndTick()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        bool collectDiagnostics = _collectDiagnostics;
        if (_walker is not null)
        {
            // A registered, live soldier the walk never reached sits under a task the port does not walk the way
            // the original does; tick it so it cannot freeze, and say so once.
            foreach ((SoldierView view, AiSoldier brain) in _brains)
            {
                if (_ticked.Contains(view) || !ConditionalContainerGate.IsTaskActive(view.Soldier))
                {
                    continue;
                }

                if (_unwalkedSoldiers.Add(view.Soldier))
                {
                    _logger.Warning(
                        LogCategory,
                        $"{view.Soldier.Name} is live but the level walk did not reach it; ticking it after the walk.");
                }

                TickSoldier(view, brain);
            }
        }

        // Un-brained cutscene actors ride their vehicles the same way; the brained ones did as they updated.
        AttachStagedActorsToVehicles();

        // Every soldier has moved; re-publish their bodies so the player's wall sweep treats each
        // living, active soldier as solid.
        SyncCollisionBodies();

        // This scans the complete roster. Calling it from inside the roster loop made diagnostics O(N²)
        // every tick, including when the logger discarded the result. One final-state scan is sufficient.
        if (collectDiagnostics)
        {
            ReportCombat();
        }

        // Simulated corpses advance on the tick, alongside the rigs they replaced -- and only until they
        // settle, because nothing in this game ever removes a body and a corpse that kept stepping would
        // be a cost paid for the rest of the mission.
        if (GameplayCVars.Ragdoll.Value)
        {
            RagdollWorld corpses = RagdollWorldForCorpses();
            int substeps = GameplayCVars.RagdollSubsteps.Value;
            double fall = GameplayCVars.RagdollGravity.Value;
            foreach (Group group in _groups)
            {
                foreach (Member member in group.Soldiers)
                {
                    if (member.Ragdoll is not { } corpse)
                    {
                        continue;
                    }

                    corpse.Step(corpses, substeps, fall);

                    // A corpse whose solver has gone wrong is dropped rather than drawn: the retail
                    // death clip has been running underneath the whole time, so what the player sees is
                    // the original's own fall, not a body that vanished.
                    if (!corpse.IsValid)
                    {
                        member.Ragdoll = null;
                        _logger.Warning(LogCategory, "A ragdoll left its own level; falling back to the death clip.");
                    }
                }
            }
        }

        // A GunPickup simulates independently after a dead human drops it, including when the optional
        // visual corpse ragdoll is off. Sharing these probes keeps its walls and floor identical to the
        // ones that stop the body.
        _droppedWeaponPickups?.Tick(RagdollWorldForCorpses());

        // Each soldier is skinned from ITS OWN rig, which is what the original does (0x49F700 is called
        // through a stack descriptor about once per soldier per frame). Sharing one skinned mesh per group
        // made every soldier wear the first one's pose, so once guards diverged onto real patrol paths
        // nothing appeared to animate at all.
        //
        // The skin lands in a persistent per-soldier buffer and replaces the mesh's vertices in place:
        // no allocation, no mesh churn, and the pose being replaced becomes the mesh's previous-pose
        // stream -- which is exactly the "previous frame's skinned vertices" the temporal resolve needs
        // and the second pose the between-tick blend draws against.
        foreach (Group group in _groups)
        {
            if (group.Model is null)
            {
                continue;
            }

            foreach (Member member in group.Soldiers)
            {
                if (!_brains.ContainsKey(member.View))
                {
                    // Un-brained soldiers still need their own rig advanced.
                    member.View.Tick();
                }

                BonePose drawn = PoseToDraw(member);
                member.DrawnPose = drawn;

                // A stopped one-shot (death, interaction or paused ladder pose) needs one duplicate
                // upload to settle both interpolation streams, then no more skinning or driver traffic.
                if (!member.PoseUploads.ShouldUpload(drawn))
                {
                    continue;
                }

                Vertex3D[]? skinned = member.SkinScratch;
                if (skinned is null)
                {
                    skinned = new Vertex3D[group.Geometry.Vertices.Length];
                    group.Geometry.Vertices.CopyTo(skinned, 0);
                    member.SkinScratch = skinned;
                }
                try
                {
                    MefSkinner.Skin(
                        group.Model,
                        drawn,
                        (float)(1.0 / WorldScene.UnitsPerMetre),
                        skinned);
                }
                catch (ArgumentException)
                {
                    continue;
                }

                if (!member.Mesh.IsValid)
                {
                    continue;
                }

                try
                {
                    _device.UpdateAnimatedMesh(member.Mesh, skinned);
                }
                catch (ArgumentException)
                {
                    continue;
                }

                // UpdateAnimatedMesh moves the old current stream into the previous stream before it
                // writes this pose. Keep the matching collision-pose pair so a player ray can trace the
                // very same r_smooth blend the GPU presents, rather than a newer animation frame.
                if (member.SkinnedPose is null)
                {
                    // A malformed model began with raw geometry. The first successful upload gives us a
                    // current pose but not a trustworthy previous one; wait for the next upload before
                    // claiming a blend pair that would disagree with the device.
                    member.SkinnedPose = drawn;
                    member.PreviousSkinnedPose = null;
                }
                else
                {
                    member.PreviousSkinnedPose = member.SkinnedPose;
                    member.SkinnedPose = drawn;
                }
                member.PoseFlippedSinceFrame = true;
            }
        }

        // With the poses settled, record where each body stands and faces THIS tick. The presentation
        // draws between the last two of these records, so they are the whole interface between the
        // 30 Hz simulation and however fast the display runs.
        foreach (Group group in _groups)
        {
            foreach (Member member in group.Soldiers)
            {
                CaptureTickState(member);
            }
        }
    }

    /// <summary>Updates every body a soldier task owns -- a generator's dead guard and its replacement share one.</summary>
    private void TickSoldierTask(GameTask task)
    {
        foreach ((SoldierView view, AiSoldier brain) in _brains)
        {
            if (ReferenceEquals(view.Soldier, task))
            {
                TickSoldier(view, brain);
            }
        }
    }

    /// <summary>One soldier's update: its rig, then its AI and movement.</summary>
    private void TickSoldier(SoldierView view, AiSoldier brain)
    {
        if (!ConditionalContainerGate.IsTaskActive(view.Soldier) || !_ticked.Add(view))
        {
            return;
        }

        bool collectDiagnostics = _collectDiagnostics;
        SoldierEnemy enemy = _enemy?.Invoke() ?? default;
        bool enemyAlive = enemy.Alive;
        bool enemyCrouching = enemy.Crouching;

        // Vision watches the mark; combat aims at the feet + 0.5 m (sub_451380). With no body to speak of,
        // the point given is the mark itself.
        Vector3d feet = enemy.Feet ?? enemy.Eye;
        Vector3d playerMark = enemy.Feet is null
            ? enemy.Eye
            : new Vector3d(
                feet.X,
                feet.Y,
                feet.Z + AiSoldier.DetectionMarkAboveFeet(enemyCrouching));

        // Every soldier's own rig advances (a group's shared mesh is only the model).
        view.Tick();

        // Read here, not after the clip selection below: the event belongs to the interval this
        // Tick just advanced, and SetMoving may replace the clip further down. A fresh BoneClip has
        // no interval yet, so asking afterwards would quietly swallow the step.
        if (!view.IsDead && view.Bones.SourceOf(0) is { } bone0)
        {
            if (bone0.CrossedEvent(FootstepSounds.EventId))
            {
                ReportFootstep(view);
            }

            // Authored animation triggers: event 1052 plays "gunpunch_01" (animation 70 swing),
            // event 1040 plays "bodyfall_9", etc.
            if (_animationTriggers is not null)
            {
                foreach (AnimTriggerDefinition trigger in _animationTriggers.Definitions.Values)
                {
                    if (bone0.CrossedEvent(trigger.EventId))
                    {
                        _scriptSound?.Invoke(trigger.Resource, view.Position, false);
                    }
                }
            }
            else if (bone0.CrossedEvent(1052))
            {
                _scriptSound?.Invoke("gunpunch_01", view.Position, false);
            }

            // Melee strike connects at the swing's impact frame (event 1052, time 1280.0 in 001.iff).
            // If a strike is pending against the player, deliver it at this exact moment.
            if (bone0.CrossedEvent(1052)
                && brain.ConsumeMeleeStrike(out float strikeDamage)
                && brain.PlayerIsEnemy
                && brain.TargetIsPlayer)
            {
                _meleeStrikeSink?.Invoke(brain.Position, strikeDamage);
            }
        }

        // Child Train collision starts at its authored placeholder and receives its derived consist
        // transform before this update. Retry registration here so the first live mesh pose can reproduce
        // retail's pre-scene Human_SetParent call. This is deliberately before animation root motion: the
        // authored Human position is the parent origin.
        Member? member = FindMember(view);
        if (member is { IsCutsceneActor: true })
        {
            AttachStagedActor(member);
        }

        // The AI runs second, so the root motion it is handed is this tick's, not last tick's.
        if (brain.RequestedAnimation >= 0
            && view.Bones.SourceOf(0)?.EndedThisTick == true)
        {
            brain.ReportAnimationFinished();
        }

        if (member is { IsCutsceneActor: true })
        {
            AiPatrolCommandKind? patrolCommand = brain.Route?.Current?.Kind;
            bool authoredAnimation = brain.RequestedAnimation >= 0
                || patrolCommand == AiPatrolCommandKind.Animation;
            if (authoredAnimation)
            {
                AttachCutsceneActorToMovingGround(
                    view,
                    brain.Position,
                    member.Position * WorldScene.UnitsPerMetre);
                Vector3f rootDelta = view.RootMotion();
                if (Cutscenes?.AdvanceAttachedActor(view.Soldier, rootDelta) == true)
                {
                    // Native attached movement carries Human+32 with the parent while the local
                    // frame keeps the relative pose. Track the ride on the brain so a later
                    // detach (Level 1's 1201 consist unregisters at the scene boundary) continues
                    // from the wagon instead of the stale pre-attach spawn 10 m behind it.
                    if (Cutscenes.TryGetAttachedDrawTransform(
                        view.Soldier, out Vector3d attachedNow, out _))
                    {
                        brain.SeparateTo(attachedNow * WorldScene.UnitsPerMetre);
                    }
                }
                else if (rootDelta.X != 0f || rootDelta.Y != 0f || rootDelta.Z != 0f)
                {
                    // The ordinary Human mover consumes the clip's root delta into Human+32 even
                    // when the AI action itself is a non-locomotion PlayAnimation. Cutscene jumps
                    // use exactly that path; keeping the task's authored spawn here froze Jones in
                    // mid-air while EditCamera continued targeting the moving Human.
                    // The brain's own body step (0x45D080 -> 0x4614B0) already carries the horizontal
                    // part this tick, so only the vertical part is added here. Adding both doubled
                    // every staged actor's sway: mission 8's guard 3336 drifted 61 cm where the
                    // original's drifts 30.
                    brain.SeparateTo(brain.Position + new Vector3d(0d, 0d, rootDelta.Z));
                    member.HasCutsceneRootMotion = true;
                }

            }

            if (patrolCommand is { } command && command.IsMovement())
            {
                member.HasCutsceneGameplayMovement = true;
            }
        }

        if (view.IsDead)
        {
            // A corpse is still stepped. The death state's per-tick (0x45C700) adds the fall solver's
            // corrective velocity, the soldier update moves the body by the death clip's own root
            // motion, and the ground query catches it. Freezing the body here is what left it standing.
            if (brain.Activity != AiSoldierActivity.Dead)
            {
                brain.Kill();
            }

            brain.TickDeadBody(view.RootMotion(), view.Bones.SourceOf(0)?.EndedThisTick ?? false);
            view.PlaceAt(brain.Position, view.Orientation);
            Member? corpse = FindMember(view);
            if (corpse is not null)
            {
                corpse.FiredThisTick = false;
            }
            _weaponSound?.Invoke(brain.RuntimeId, false, corpse?.WeaponType, brain.Position);
            return;
        }

        // The clip's own delta in the body's frame: forward and sideways, signed.
        Vector3f clipDelta = view.RootMotion();
        double travel = clipDelta.Y;
        double lateral = clipDelta.X;
        if (brain.SoldierClass.ScalesEkkRootMotion && brain.Type == AiType.Ekk)
        {
            // 0x4550B0 scales a female soldier's delta translation by flt_56E1E8 when her brain is Ekk.
            double scale = _playerDeltaTranslationScale?.Invoke() ?? HumanMotion.DefaultDeltaTranslationScale;
            travel *= scale;
            lateral *= scale;
        }

        // Retail ground truth, measured by hooking 0x48E920 in the original: a walking guard covers
        // 160-185 units a tick, about 1.28 m/s, varying through the gait cycle. Recorded here so the
        // port's own figure can be compared without another instrumented run.
        if (collectDiagnostics && brain.Activity == AiSoldierActivity.Patrolling && travel > 0.0)
        {
            _travelSamples++;
            _travelTotal += travel;
            _travelMax = Math.Max(_travelMax, travel);
        }

        // The alarm the soldier watches is the one ITS OWN +15044 names -- resolved per AI, as the
        // retail per-tick resolves the control task each tick. First sight of an id lazily makes
        // the state, since the ids arrive only once the CREATE dispatch has run the script.
        bool alarmOn = false;
        bool alarmWasOn = false;
        if (brain.AlarmControlId != -1)
        {
            if (_alarmControls?.TryGet(brain.AlarmControlId, out AiAlarmControlState? watched) == true)
            {
                alarmOn = watched!.IsOn;
                alarmWasOn = watched.WasOn;
            }
        }

        // The detection scan is offered every human -- the player and every soldier -- and the brain
        // fights whoever the event that started its fight named; it aims at that human.
        brain.TickAmong(
            _tick,
            CandidatesFor(view, brain, feet, playerMark, enemyAlive, enemyCrouching),
            travel,
            alarmOn,
            alarmWasOn,
            lateral);
        Vector3d aimFeet = brain.TargetFeet;
        bool aimCrouching = brain.TargetCrouching;

        // Fallback for headless environments or missing animation clips: if event 1052 could not
        // be evaluated, deliver any pending strike immediately so damage is not dropped.
        if (_combatClipMissing.Contains(view) || view.Bones.SourceOf(0) is null)
        {
            if (brain.ConsumeMeleeStrike(out float meleeDamage) && brain.TargetIsPlayer && brain.PlayerIsEnemy)
            {
                _scriptSound?.Invoke("gunpunch_01", brain.Position, false);
                _meleeStrikeSink?.Invoke(brain.Position, meleeDamage);
            }
        }

        if (member is not null)
        {
            UpdateMountedWeapon(member, brain);
            if (collectDiagnostics && brain.Arm == AiCombatArm.Gunner)
            {
                string report = $"runtime={brain.RuntimeId} gunnerId={brain.GunnerId} "
                    + $"mounted={(brain.IsMounted ? "true" : "false")} "
                    + $"task={(brain.MountedGun is null ? "-" : brain.MountedGun.TaskId.ToString(System.Globalization.CultureInfo.InvariantCulture))} "
                    + $"activity={brain.Activity}";
                if (_lastGunnerRuntimeReports.GetValueOrDefault(brain.RuntimeId) != report)
                {
                    _lastGunnerRuntimeReports[brain.RuntimeId] = report;
                    _logger.Information(LogCategory + ".Gunner", $"{report}");
                }

                if (_tick % 300 == 1)
                {
                    string timer = _scriptVariables?.TryRead("LevelTimer_1608.nTick", out QvmValue timerValue) == true
                        ? timerValue.ToString()
                        : "missing";
                    string dispatched = brain.LastDispatchedEvent?.ToString() ?? "-";
                    _logger.Information(
                        LogCategory + ".Gunner",
                        $"diagnostic runtime={brain.RuntimeId} lastEvent={dispatched} "
                        + $"timer1608={timer}");
                }
            }
        }

        // The pump half of the [aiflow] diff: an event that was QUEUED shows up here the tick it is
        // drained, whether or not any script acted on it. Noise delivered but never drained points at
        // the pump; drained but never acted on points at the script/actions.
        if (collectDiagnostics && _tick - _lastDrainLogTick >= 30)
        {
            int gunshot = brain.Events.DrainedCount(AiEvent.Gunshot);
            int miss = brain.Events.DrainedCount(AiEvent.GunshotMiss);
            int impact = brain.Events.DrainedCount(AiEvent.GroundImpact);
            int explosion = brain.Events.DrainedCount(AiEvent.Explosion);
            int grenadeThrown = brain.Events.DrainedCount(AiEvent.GrenadeThrown);
            int grenadeLand = brain.Events.DrainedCount(AiEvent.GrenadeLand);
            int flashbang = brain.Events.DrainedCount(AiEvent.Flashbang);
            int walk = brain.Events.DrainedCount(AiEvent.Walk);
            int damage = brain.Events.DrainedCount(AiEvent.TakingDamage);
            if (gunshot + miss + impact + explosion + grenadeThrown + grenadeLand + flashbang + walk + damage > 0)
            {
                _lastDrainLogTick = _tick;
                _logger.Information(
                    LogCategory + ".Flow",
                    $"#{brain.RuntimeId} drained gunshot={gunshot} miss={miss} "
                        + $"explosion={explosion} grenadeThrown={grenadeThrown} "
                        + $"grenadeLand={grenadeLand} flashbang={flashbang} "
                        + $"impact={impact} walk={walk} dmg={damage} act={brain.Activity}");
            }
        }

        if (member is not null)
        {
            member.FiredThisTick = false;
        }

        // The body machine consumes this tick's AI channel writes before the weapon consumes the
        // body's forwarded trigger. Running it after FireIfAsked let a round leave while the body
        // was still in rack/ready/recover and skipped state 18 entirely during a running burst.
        // 0x45D080 ticks the body every tick whatever the AI is doing, which is what lets a patrolling
        // guard flinch; its state drives the clip in a fight, and a flinch whatever the activity.
        // A gunner on its gun is in the use-object state (36), which only a fresh press of slot 24 -- the
        // dismount -- or death leaves.
        int? combatState = null;
        bool animationDone = (view.Bones.SourceOf(0)?.EndedThisTick ?? false)
            || _combatClipMissing.Contains(view);
        int bodyState = brain.TickCombatState(animationDone);
        if (!brain.IsMounted
            && brain.Activity is AiSoldierActivity.Engaging or AiSoldierActivity.FiringAtTask)
        {
            combatState = bodyState;
        }

        // The grenade leaves the hand when the throw state ENDS -- the clip has swung. The
        // decision to throw was the combat arm's own (the retail probability, distance band and
        // has-grenade tests in AiPatrolCombat); this is only the release.
        int previousCombatState = _lastCombatState.TryGetValue(view, out int rememberedState)
            ? rememberedState
            : -1;
        _lastCombatState[view] = combatState ?? -1;
        if (brain.SoldierClass.GrenadeThrowState is { } grenadeState
            && previousCombatState == grenadeState
            && (combatState ?? -1) != grenadeState
            && _grenades is { } lobbed)
        {
            ThrowAiGrenade(brain, feet, lobbed);
        }

        bool fired = FireIfAsked(view, brain, aimFeet, aimCrouching);
        WeaponType? soundingWeapon = brain.MountedGun?.Weapon ?? member?.WeaponType;
        Vector3d soundPosition = brain.MountedGun?.Position ?? brain.EyePosition();
        _weaponSound?.Invoke(brain.RuntimeId, fired, soundingWeapon, soundPosition);

        // sub_48A920, IGI.exe: detected_01..06, once per combat-action argument block. The
        // dword_5416CC cooldown is process/mission-wide and requires strictly more than 90 ticks.
        if (brain.DetectionShoutRequestedThisTick
            && (_lastDetectionShoutTick == -1 || _tick - _lastDetectionShoutTick > 90))
        {
            int variant = checked((int)_aiRandom.NextBounded(6) + 1);
            _scriptSound?.Invoke($"detected_{variant:00}", brain.Position, true);
            _lastDetectionShoutTick = _tick;
        }

        // An engaged guard pulls in the guards beside it: AIEVENT_ALERT with a 7 m reach and filter
        // 0x26, so only its own side admits it (sub_48A920 -> sub_48AB10). This is how one guard's
        // fight wakes the room even before anybody fires.
        if (brain.AlertPingDueThisTick)
        {
            foreach ((SoldierView neighbourView, AiSoldier neighbour) in _brains)
            {
                if (!ReferenceEquals(neighbour, brain)
                    && neighbourView.Soldier.Team == view.Soldier.Team
                    && ConditionalContainerGate.IsTaskActive(neighbourView.Soldier))
                {
                    neighbour.ReceiveAlert(brain.Position, brain.RuntimeId, brain.TargetId);
                }
            }
        }
        if (collectDiagnostics)
        {
            ReportChannels(brain);
        }

        // A patrol ANIMATION command names a clip outright; a soldier in a fight plays its WEAPON's
        // own clips; otherwise the gait picks between the walk cycle and the soldier's stand
        // animation.
        HumanSoldierClass soldierClass = brain.SoldierClass;
        int walkAnimation = soldierClass.WalkAnimation;
        int runAnimation = brain.Body.StateTable.AnimationOf(soldierClass.RunState);
        if (brain.Body.IsFlinching)
        {
            // A flinch owns the body over any other clip: every per-tick leaves for it on the pulse.
            if (!view.PlayStateAnimation(brain.Body.CurrentAnimation, soldierClass.States.Loops(brain.Body.State)))
            {
                _combatClipMissing.Add(view);
            }
        }
        else if (brain.RequestedAnimation >= 0)
        {
            view.PlayStateAnimation(brain.RequestedAnimation, loops: false);
        }
        else if (brain.IsMovingInCombat || (brain.Activity == AiSoldierActivity.Panicking && brain.WalksForward))
        {
            // The soldier is travelling a combat route this tick -- advancing, repositioning or hunting a
            // lost target while it watches. A chasing guard RUNS (retail's Combat-Follow runs in state 17
            // = anim 48 at ~4.4 m/s), and the run clip's own root motion is what carries it at that speed
            // (AiSoldier.AdvanceInCombat reads it). Fall back to the walk if the set has no run clip.
            //
            // This test MUST come before the combat-state branch: an engaged hunter otherwise plays
            // the fire pose, whose zero root motion feeds AdvanceInCombat a zero step -- the exact
            // "engaging, never moving" stall. Retail's Follow is a full-body run state (17), not the
            // stand cycle with feet glued down.
            if (runAnimation < 0 || !view.SetMoving(true, runAnimation))
            {
                view.SetMoving(true, walkAnimation);
            }
        }
        else if (brain.IsMounted)
        {
            // HumanSoldier state 36 is the attached emplacement pose. It keeps ownership while
            // the StationaryGun task reads slot 6 and fires; it does not enter the ordinary
            // 0/12/10/6 carried-weapon body cycle. It is the body's, not the brain's: a gunner the
            // Activate action put on the gun stays on it between fights.
            int mountedState = AiCombatChannels.StateOf(brain.Type, AiStanceChannel.Mounted);
            int animation = brain.Body.StateTable.AnimationOf(mountedState);
            if (animation < 0
                || !view.PlayStateAnimation(animation, soldierClass.States.Loops(mountedState)))
            {
                view.SetMoving(false, walkAnimation);
            }
        }
        else if (combatState is { } combatBodyState)
        {
            int anim = brain.Body.CurrentAnimation;
            bool placed = anim >= 0
                && view.PlayStateAnimation(anim, soldierClass.States.Loops(combatBodyState));
            if (placed)
            {
                _combatClipMissing.Remove(view);
            }
            else
            {
                view.SetMoving(false, walkAnimation);
                _combatClipMissing.Add(view);
            }
        }
        else if (brain.Activity == AiSoldierActivity.FallenFlat)
        {
            int state = AiCombatChannels.StateOf(brain.Type, AiStanceChannel.Prone);
            int animation = brain.Body.StateTable.AnimationOf(state);
            if (animation < 0
                || !view.PlayStateAnimation(animation, soldierClass.States.Loops(state)))
            {
                view.SetMoving(false, walkAnimation);
            }
        }
        else if (brain.IsCrouching)
        {
            // A patrol's Crouch command holds the kneel channel every tick (0x48E920): the body goes
            // down (state 13) and holds the kneel (19) until a walk releases it.
            int state = brain.CombatState;
            int animation = brain.Body.CurrentAnimation;
            if (animation < 0 || !view.PlayStateAnimation(animation, brain.Body.StateTable.Loops(state)))
            {
                view.SetMoving(false, walkAnimation);
            }
        }
        else if (brain.WalksForward && !brain.IsWalking)
        {
            // The patrol cursor's gait flag (+21) is applied EVERY tick by the retail walker
            // (0x48E920): a RunTo leg installs the run gait and it stays until the next movement
            // command changes it. The alarm-response routes (level 1's 2600/2605-2616) are authored
            // as RunTo, so an alarmed guard RUNS between its posts -- playing the walk clip here is
            // what made them amble out of the barracks at 1.28 m/s. The run clip's own root motion
            // carries the brain at run speed; sets without one fall back to the walk.
            if (runAnimation < 0 || !view.SetMoving(true, runAnimation))
            {
                view.SetMoving(true, walkAnimation);
            }
        }
        else
        {
            // The body walks while the walker writes forward, and stands while it holds -- at a door,
            // on arrival, or turning on the spot.
            view.SetMoving(brain.WalksForward, walkAnimation);
        }

        view.PlaceAt(brain.Position, brain.Orientation);
    }

    /// <summary>A door's collision child follows the same live transform as the drawn door.</summary>
    /// <remarks>
    /// A half-open door remains a blocker at its half-open angle instead of disappearing from sight, bullets and
    /// movement.
    /// </remarks>
    private void DoorTicked(PlacedDoor door)
    {
        if (_solids is not { } solids)
        {
            return;
        }

            // A fully-open door is passable regardless of how its leaf is authored to clear the
            // opening. Sliding leaves (nonzero Stop*) are moved out of the way by UpdateTransform
            // below, but cab doors that animate inside their model (Stop* == 0, e.g. Elevator_330's
            // leaves) never change placement, so their static mesh would keep the doorway solid for
            // ever; toggling passability on the open state makes both kinds behave like retail.
            solids.SetPassable(door.Task, door.State.IsFullyOpen);

            // Closed/open doors spend nearly all their life stationary, unless they ride a
            // moving elevator: keep collision synchronized with the rendered displacement.
            Vector3d elevatorOffset = _elevators?.GetCurrentDisplacement(door.Task) ?? default;
            if (door.State.TransformChangedThisTick || elevatorOffset != default)
            {
                solids.UpdateTransform(
                    door.Task, door.DrawPosition + elevatorOffset, door.DrawOrientation);
            }

            // Sight treats building portals as solid. Once this leaf is off its shut pose,
            // open the portal it filled so a guard in the doorway can see through.
            solids.SetSightOpening(door.Task, !door.State.IsFullyClosed, elevatorOffset);
    }

    private void AlarmControlTicked()
    {
        bool alarmIsOn = _alarmControls?.AnyOn ?? false;
        if (alarmIsOn != _alarmWasOn)
        {
            _alarmWasOn = alarmIsOn;
            _logger.Information(
                LogCategory + ".Alarm",
                $"mission alarm {(alarmIsOn ? "ON" : "OFF")} at tick {_tick}.");
        }
    }

    private void AttachCutsceneActorToMovingGround(
        SoldierView view,
        Vector3d probePosition,
        Vector3d? attachmentPosition = null,
        WorldTraceMask mask = WorldTraceMask.DynamicObjects,
        bool includeInactiveOwners = false)
    {
        if (_solids is null || Cutscenes is null)
        {
            return;
        }

        // A moving owner that leaves its conditional container is the native
        // Human_GroundCollision inactive-owner branch (0x463550): clear the parent slot first. An
        // empty query is intentionally different and leaves an existing parent in place, so this
        // check is based only on the owner's live registration state.
        if (Cutscenes.TryDetachInactiveAttachment(view.Soldier))
        {
            return;
        }

        if (Cutscenes.TryGetAttachedDrawTransform(view.Soldier, out _, out _))
        {
            // Retail 0x4626E8 only calls Human_SetParent when Human+1900 is null. Once attached,
            // the parent owns movement until a separate detach path clears it; another ground ray
            // must not silently recapture or switch the local frame.
            return;
        }

        // Retail IGI.exe 0x4623C3 samples BoneDynCubeObj's animated root, then 0x462403 forms the
        // standing ground-check Z as Human.Position.Z + 1638.4 - RootSample.Z. Querying from the task origin
        // instead hits geometry above the feet (Mission 1's helicopter underside) and parents Jones
        // to the wrong vehicle. DynamicObject membership alone is NOT the moving-platform flag
        // tested at 0x4626E1: a hovering airframe is dynamic but never captures (its riders are
        // direct QVM children); only a platform contact reaches Human_SetParent at 0x463310.
        double checkZ = probePosition.Z + HumanGroundCheckLiftInUnits - view.RootSample.Z;
        WorldTraceRequest request = new(
            new Vector3d(probePosition.X, probePosition.Y, checkZ),
            new Vector3d(probePosition.X, probePosition.Y, checkZ - HumanGroundProbeDownInUnits),
            mask,
            IgnoredOwner: view.Soldier,
            IncludeInactiveOwners: includeInactiveOwners);
        if (_solids.TryTrace(request, out WorldTraceHit hit)
            && hit.Owner is { } parent
            && CinematicActor.IsMovingPlatform(parent))
        {
            // The retail probe selects the supporting task from the live foot query, but the first
            // Human_SetParent packet still contains the staged Human+32. Our actor mover may have
            // consumed a root delta while the vehicle's conditional container was dormant, so keep
            // those two inputs distinct instead of baking that scheduling error into the local frame.
            Cutscenes.TryAttachActorToTask(
                view.Soldier,
                parent,
                attachmentPosition ?? probePosition,
                view.Orientation);
        }
    }

    private void AttachStagedActorsToVehicles()
    {
        foreach (Group group in _groups)
        {
            foreach (Member member in group.Soldiers)
            {
                if (member.IsCutsceneActor && !_brains.ContainsKey(member.View))
                {
                    AttachStagedActor(member);
                }
            }
        }
    }

    /// <summary>Retries a live cutscene actor's attachment to the moving ground under it.</summary>
    private void AttachStagedActor(Member member)
    {
        if (_solids is null
            || Cutscenes is null
            || !ConditionalContainerGate.IsTaskActive(member.View.Soldier))
        {
            return;
        }

        AttachCutsceneActorToMovingGround(
            member.View,
            member.Position * WorldScene.UnitsPerMetre,
            attachmentPosition: null,
            mask: WorldTraceMask.DynamicObjects,
            includeInactiveOwners: false);
    }

    /// <summary>Records a soldier's drawn placement for this tick, keeping the tick before.</summary>
    private void CaptureTickState(Member member)
    {
        (Vector3d where, Matrix3f orientation) = TickPlacementOf(member);
        if (TracedCutsceneActor is { } traceActor
            && traceActor == member.View.Soldier.Id.ToString(System.Globalization.CultureInfo.InvariantCulture)
            && _tick % 30 == 0)
        {
            BoneClip? clip = member.View.Bones.SourceOf(0);
            _logger.Warning(LogCategory + ".CutsceneActor",
                $"tick={_tick} actor={traceActor} active={ConditionalContainerGate.IsTaskActive(member.View.Soldier)} "
                + $"scene={Cutscenes?.ActiveScene?.Id} staged={member.IsCutsceneActor} where={where} "
                + $"view={member.View.Position} clip={member.View.CurrentAnimation} time={clip?.Time} "
                + $"root={member.View.RootSample} delta={member.View.RootMotion()}");
        }
        float facing = FacingOf(orientation);

        member.PrevTickWhere = member.HasTickState ? member.TickWhere : where;
        member.PrevTickFacing = member.HasTickState ? member.TickFacing : facing;
        member.TickWhere = where;
        member.TickFacing = facing;
        member.TickOrientation = orientation;

        if (member.Weapon is not null)
        {
            // The pose the BODY was drawn from, which for a corpse is the ragdoll's rather than the
            // rig's -- otherwise the weapon rides a hand that is no longer where the hand is drawn, and
            // hangs in the air over the body.
            BonePose pose = member.DrawnPose ?? member.View.UnrotatedPose();
            if (HumanAttachPoints.ThirdPersonWeapon < pose.BoneCount)
            {
                Vector3f hand = pose.Positions[HumanAttachPoints.ThirdPersonWeapon];
                Matrix3f rotation = pose.Rotations[HumanAttachPoints.ThirdPersonWeapon];

                // Once the weapon is a body of its own it stops riding the hand and starts being placed
                // by the solver, which is what lets it come to rest on the floor rather than through it.
                // It is written into the same pair the hand fills, so the draw path, the interpolation
                // and the motion vectors all carry on unchanged.
                if (member.Ragdoll is { HasAttachment: true } armed)
                {
                    hand = armed.AttachmentPosition;
                    rotation = armed.AttachmentRotation;
                }
                member.PrevTickHandPosition = member.HasHandState ? member.TickHandPosition : hand;
                member.PrevTickHandRotation = member.HasHandState ? member.TickHandRotation : rotation;
                member.TickHandPosition = hand;
                member.TickHandRotation = rotation;
                member.HasHandState = true;
            }
        }

        member.HasTickState = true;
    }

    /// <summary>Where a soldier's body is drawn for the current tick, in metres, and its heading.</summary>
    /// <remarks>
    /// The placement <see cref="Draw"/> used to derive per pass, moved to the tick so the ground probe
    /// runs once per soldier per tick instead of once per soldier per pass — the shadow cascades and
    /// the geometry pass then agree by construction.
    /// </remarks>
    private (Vector3d Where, Matrix3f Orientation) TickPlacementOf(Member member)
    {
        SoldierView soldier = member.View;

        // A simulated corpse carries its own placement. Its pose is built in world space relative to the
        // origin frozen at the moment it died, with the body's heading folded into the root bone's own
        // rotation, so it is drawn there with no turn applied. Reporting the same pair every tick is
        // also what makes the between-tick blend a no-op for it -- the motion is all in the pose.
        if (member.Ragdoll is { } corpse)
        {
            return (corpse.Origin / WorldScene.UnitsPerMetre, Matrix3f.Identity);
        }

        // A dormant cinematic actor may still have an AI row and therefore a brain object, but its
        // authored staging wins while its conditional scene is active.  Letting the generic brain
        // branch below ground it simply reintroduced the seated-actor lift avoided at construction.
        if (member.IsCutsceneActor)
        {
            if (Cutscenes?.TryGetAttachedDrawTransform(
                soldier.Soldier, out Vector3d attached, out Matrix3f attachedOrientation) == true)
            {
                // Human_SetParent derives Human+1904 from the actor's already-staged Human+32. For a
                // seated actor that position is the model/pelvis origin, not gameplay feet. The probe's
                // Mission 1 trace fixes Jones at local (47.574,-2649.593,-866.474) inside heli 1502;
                // Human_AttachedMovement then consumes only the per-tick root DELTA into that local
                // offset. Adding the absolute Human+1300 sample here commits it a second time, lifting
                // Jones into the cabin roof until only his legs survive the depth test.
                return (attached, attachedOrientation);
            }

            // A graph inside a placed model parents the actor to that model instead (0x462390 -> 0x463310): mission 9's
            // intro office holds Anya where she was placed, so she keeps the staging below.
            // An actor with an AI graph is grounded on it every tick like any soldier, and 0x462390 then
            // sets Human+32 to that ground plus the clip's root-bone height (0x48A340). Mission 8's intro
            // guards were drawn at their authored origin or, once their idle clip moved them, at their
            // feet -- 3336 stood waist-deep in the ground beside the second truck.
            // Only once the brain HAS stood it there: before its first tick the position is still the authored
            // model origin, which for a set-dressed actor already sits a root height above the floor -- mission
            // 4's Anya at 1.98 m -- and adding the root height again stood her a whole hip-height up.
            if (!member.HasCutsceneGameplayMovement
                && _brains.TryGetValue(soldier, out AiSoldier? stagedBrain) && stagedBrain.HasGraph && stagedBrain.HasGroundQuery
                && !stagedBrain.GraphInParentFrame && stagedBrain.StoodOnGraph)
            {
                // The graph branch takes its height from the graph, which the brain has already applied.
                return (new Vector3d(
                    soldier.Position.X / WorldScene.UnitsPerMetre,
                    soldier.Position.Y / WorldScene.UnitsPerMetre,
                    (soldier.Position.Z + soldier.RootSample.Z) / WorldScene.UnitsPerMetre), soldier.Orientation);
            }

            if (member.HasCutsceneGameplayMovement)
            {
                double groundedZ = SnapToGround(soldier.Position.X, soldier.Position.Y, soldier.Position.Z);
                return (new Vector3d(
                    soldier.Position.X / WorldScene.UnitsPerMetre,
                    soldier.Position.Y / WorldScene.UnitsPerMetre,
                    (groundedZ + soldier.RootSample.Z) / WorldScene.UnitsPerMetre), soldier.Orientation);
            }

            if (member.HasCutsceneRootMotion)
            {
                return (soldier.Position / WorldScene.UnitsPerMetre, soldier.Orientation);
            }
            // The authored model origin is already staged (Anya HQ, vehicle briefing sets).
            // Adding an absolute root sample here lifts the whole set-relative performance.
            return (member.Position, soldier.Orientation);
        }

        // A soldier with a brain is drawn where its AI put it, lifted by the CLIP'S OWN ROOT HEIGHT.
        // That is the original's Human+1300 (see SoldierView.RootSample): every retail skeleton's
        // bone 0 rest translation is zero, so the pose puts the pelvis at the object origin and the
        // feet a metre below it, and the root track's Z is what bridges the two. Only the Z is taken:
        // the horizontal part of the root track is what MOVES the body — the mover consumes the same
        // track's per-tick delta — so adding it again would run the model a stride ahead of its feet.
        if (_brains.TryGetValue(soldier, out AiSoldier? brain) && brain.HasGroundQuery)
        {
            // Snap the feet to whatever the collision puts under them, exactly as the static-soldier
            // Stand() does. A level often authors a soldier a little below its floor; the query only
            // pulls UP onto a floor within the half-metre probe, so a soldier already on its floor
            // does not move.
            double groundedZ = SnapToGround(soldier.Position.X, soldier.Position.Y, soldier.Position.Z);
            return (new Vector3d(
                soldier.Position.X / WorldScene.UnitsPerMetre,
                soldier.Position.Y / WorldScene.UnitsPerMetre,
                (groundedZ + soldier.RootSample.Z) / WorldScene.UnitsPerMetre), soldier.Orientation);
        }

        if (_brains.ContainsKey(soldier))
        {
            return (new Vector3d(
                soldier.Position.X / WorldScene.UnitsPerMetre,
                soldier.Position.Y / WorldScene.UnitsPerMetre,
                member.Position.Z), soldier.Orientation);
        }

        return (member.Position, soldier.Orientation);
    }

    /// <summary>The pose a body is skinned from this tick — its rig's, or its corpse's.</summary>
    /// <remarks>
    /// The single branch the whole feature costs the render path. A ragdoll's pose comes back out of
    /// <c>BonePose.Evaluate</c> like any other, so the skinner, the shaders and the previous-pose stream
    /// that feeds the temporal resolve are all untouched by it.
    /// </remarks>
    private static BonePose PoseToDraw(Member member) =>
        member.Ragdoll?.Pose() ?? member.View.UnrotatedPose();

    /// <summary>A body's heading out of its world 3×3 — the inverse of <c>Z(facing)</c>.</summary>
    /// <remarks>
    /// The second column is the body's forward, and a soldier's forward is
    /// <c>(-sin f, cos f)</c> (see <see cref="AiSoldier"/>), so the angle reads straight back out.
    /// Orientations are pure Z rotations for every body this renderer draws.
    /// </remarks>
    private static float FacingOf(in Matrix3f orientation) => MathF.Atan2(-orientation.M12, orientation.M22);

    /// <summary>Resolves where every soldier is drawn for one rendered frame.</summary>
    /// <param name="tickFraction">How far the frame sits between the last two ticks, 0 to 1.</param>
    /// <param name="smooth">
    /// Whether to draw between the two tick states. Off, every value is this tick's verbatim — the
    /// original's stepped presentation, which the vanilla reference captures depend on being exact.
    /// </param>
    /// <remarks>
    /// Once per frame, before any pass draws. <see cref="Draw"/> runs once per shadow cascade and once
    /// for the geometry pass, so anything resolved per frame — the blend, the ground snap, the motion
    /// bookkeeping — has to be resolved here or the passes would disagree with each other.
    /// </remarks>
    public void BeginFrame(float tickFraction, bool smooth)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        float alpha = Math.Clamp(tickFraction, 0f, 1f);
        _frameAlpha = alpha;
        _frameSmooth = smooth;
        _groundSnaps.Clear();

        foreach (Group group in _groups)
        {
            foreach (Member member in group.Soldiers)
            {
                ResolveFrameState(member, alpha, smooth);
            }
        }

        // The player's own body is not in _groups, so it resolves here rather than in the loop -- and it
        // has to resolve HERE rather than inside its draw, because its draw now runs once per shadow
        // cascade as well as once for the geometry pass, and the frame bookkeeping below records what was
        // on screen LAST frame. Doing it per draw would make a body cast three shadows' worth of history
        // and report itself as never having moved.
        ResolvePlayerBodyFrameState();
    }

    /// <summary>Computes one soldier's drawn placement, pose blend and motion for this frame.</summary>
    private void ResolveFrameState(Member member, float alpha, bool smooth)
    {
        if (!member.HasTickState)
        {
            // Drawn before its first tick: stand exactly where the level put it.
            CaptureTickState(member);
        }

        // The rigid half. When nothing moved — or smoothing is off — the tick's own values pass
        // through verbatim, so the vanilla picture is not merely close but identical.
        Vector3d where = smooth && member.PrevTickWhere != member.TickWhere
            ? PresentationBlend.Lerp(member.PrevTickWhere, member.TickWhere, alpha)
            : member.TickWhere;

        Matrix3f orientation = smooth && member.PrevTickFacing != member.TickFacing
            ? Matrix3f.FromEngineEulerAngles(0f, 0f, PresentationBlend.LerpAngle(member.PrevTickFacing, member.TickFacing, alpha))
            : member.TickOrientation;

        member.PreviousDrawn = member.HasFrameState ? (member.DrawnWhere, member.DrawnOrientation) : null;
        member.DrawnWhere = where;
        member.DrawnOrientation = orientation;

        // The pose half. Smoothing draws 1-alpha of the previous tick's skin; the blend that described
        // LAST frame's pose is kept so the geometry pass can reconstruct last frame's vertex from the
        // same two streams. When the streams flipped since the last frame the old blend no longer names
        // the right pair, and the previous stream itself — the whole of last tick's pose — is the
        // closest expressible stand-in; by then last frame's blend was nearly there anyway.
        member.PreviousPoseBlend = member.PoseFlippedSinceFrame ? 1f : member.PoseBlend;
        member.PoseBlend = smooth ? 1f - alpha : 0f;
        member.PoseFlippedSinceFrame = false;

        // A body's very first frame has no history anywhere on screen; say so rather than let the
        // resolve reproject into whatever stood there before.
        member.NoHistoryThisFrame = !member.HasFrameState;
        member.HasFrameState = true;

        // The held weapon rides the hand bone, interpolated the same way — rigid, so its previous
        // transform alone reprojects it exactly.
        if (member.Weapon is not null && member.HasHandState)
        {
            Vector3f hand;
            Matrix3f handRotation;
            if (smooth)
            {
                hand = member.PrevTickHandPosition + ((member.TickHandPosition - member.PrevTickHandPosition) * alpha);
                handRotation = PresentationBlend.InterpolateRotation(member.PrevTickHandRotation, member.TickHandRotation, alpha);
            }
            else
            {
                hand = member.TickHandPosition;
                handRotation = member.TickHandRotation;
            }

            // M . v, so the transpose of the row-vector Transform (see DrawWeapon's remarks).
            Vector3f turned = orientation.Transposed().Transform(hand);
            member.PreviousWeapon = member.HasWeaponFrameState
                ? (member.DrawnWeaponOrigin, member.DrawnWeaponRotation)
                : null;
            member.DrawnWeaponOrigin = new Vector3d(
                where.X + (turned.X / WorldScene.UnitsPerMetre),
                where.Y + (turned.Y / WorldScene.UnitsPerMetre),
                where.Z + (turned.Z / WorldScene.UnitsPerMetre));
            member.DrawnWeaponRotation = orientation * handRotation;
            member.HasWeaponFrameState = true;
        }
    }

    /// <summary>Draws every soldier.</summary>
    /// <param name="camera">The eye the scene is drawn from.</param>
    /// <param name="projection">The world's view-to-clip transform — soldiers use no other.</param>
    /// <remarks>
    /// Called once per shadow cascade and once for the geometry pass; everything per-frame was settled
    /// by <see cref="BeginFrame"/>, so every pass draws the same bodies in the same places.
    /// </remarks>
    public void Draw(in Camera3D camera, in Matrix4f projection)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_groups.Count == 0 && _stationaryWeaponVisuals.Count == 0 && _vehicleGuns is null)
        {
            return;
        }

        _device.SetProjection(projection);
        _device.SetCullMode(CullMode.None);
        _device.SetDepthState(DepthState.TestAndWrite);

        // StationaryGun's holder is a separate authored model. An AI gunner replaces it with the
        // mounted soldier's hand attachment, but the first-person player has no equivalent world
        // attachment, so the holder stays visible while the player owns the task.
        DrawStationaryGuns(camera, projection);
        DrawVehicleGuns(camera, projection);

        // Motion is a geometry-pass concern: only the G-buffer records it, and the previous pose is
        // built against the scene camera, which the shadow cascades' light cameras are not.

        foreach (Group group in _groups)
        {
            foreach (Member member in group.Soldiers)
            {
                if (!ConditionalContainerGate.IsTaskActive(member.View.Soldier))
                {
                    continue;
                }

                // A dead soldier keeps being drawn -- a body stays on the ground in the original, and
                // vanishing was only ever the port having no death animation to play. Each soldier
                // carries its own skinned mesh, so a corpse is posed by its own death clip.
                if (!member.HasFrameState)
                {
                    ResolveFrameState(member, 1f, smooth: false);
                }

                Matrix4f modelView = camera.CreateModelView(member.DrawnWhere, member.DrawnOrientation);

                // The pose blend applies to the body's animated mesh in every pass — the shadow pass
                // blends too, or the body would stand a step from its own shadow.
                _device.SetPoseInterpolation(member.PoseBlend, member.PreviousPoseBlend);

                // The gun first, with its OWN previous transform: it is placed by the hand bone, and
                // handing it the body's previous pose would report it sliding by the distance between
                // the two.
                DrawWeapon(member, camera, projection);

                // Matte, and out of the ambient occlusion: see SurfaceMaterial.Body. The occlusion's
                // right repair is a thickness test in the search itself; until that exists, soldiers
                // are left out rather than shaded wrongly.
                _device.SetMaterialOverride(SurfaceMaterial.Body with
                {
                    NoHistory = member.NoHistoryThisFrame,
                });

                foreach (MeshSubset subset in group.Geometry.Subsets)
                {
                    if (subset.IndexCount == 0)
                    {
                        continue;
                    }

                    TextureHandle diffuse = TextureHandle.None;
                    if (subset.HasMaterial
                        && group.Instance is { } instance
                        && group.Pack is { } pack
                        && pack.TryGetTextureName(instance, subset.MaterialSlot, out string texture))
                    {
                        diffuse = Textures.Get(texture);
                    }

                    _device.DrawMesh(
                        member.Mesh,
                        subset.IndexOffset,
                        subset.IndexCount,
                        modelView,
                        diffuse,
                        TextureHandle.None,
                        BlendMode.Opaque);
                }

                _device.SetMaterialOverride(SurfaceMaterial.Default);
                _device.ClearPoseInterpolation();
            }
        }
    }

    private void DrawStationaryGuns(in Camera3D camera, in Matrix4f projection)
    {
        foreach ((AiStationaryGun gun, HeldWeapon? visual) in _stationaryWeaponVisuals)
        {
            bool playerUsing = gun.IsOccupied && _interactions?.Items.Any(item =>
                ReferenceEquals(item.StationaryGun, gun) && item.IsPlayerUsingStationaryGun) == true;
            if (!ShouldDrawStaticStationaryGun(gun, playerUsing) || visual is null)
            {
                continue;
            }

            if (_reportedStationaryVisuals.Add(gun.TaskId))
            {
                _logger.Information(
                    LogCategory + ".Gunner",
                    $"static visual task={gun.TaskId} weapon={gun.WeaponId} "
                    + $"position=({gun.Position.X:0.##},{gun.Position.Y:0.##},{gun.Position.Z:0.##}) drawn");
            }

            // 0x474420's pose: on the stand's mount point, turned and elevated by the shown aim. The
            // gun's barrel is authored on local +Y and the stand's on local -Y; the π inside
            // GunOrientation is what swings one onto the other.
            if (visual.Presentation is { } presentation)
            {
                presentation.Draw(
                    new BoneAttachment(gun.GunPosition, gun.GunOrientation),
                    camera,
                    projection,
                    firedThisTick: false,
                    shotSequence: 0,
                    WeaponDrawParts.BodyOnly);
            }
            else
            {
                // No multipart presenter is built for the level's weapons, so the gun is drawn the way a
                // soldier's is: as its one rigid mesh. Requiring the presenter here is what left every
                // stand empty.
                DrawRigidWeapon(visual, gun.GunPosition / WorldScene.UnitsPerMetre, gun.GunOrientation, camera);
            }
        }
    }

    private void DrawVehicleGuns(in Camera3D camera, in Matrix4f projection)
    {
        if (_vehicleGuns is not { } guns)
        {
            return;
        }

        foreach (VehicleGunVisual gun in guns())
        {
            if (!_vehicleWeaponVisuals.TryGetValue(gun.WeaponModel, out HeldWeapon? visual) || visual is null)
            {
                continue;
            }

            if (visual.Presentation is { } presentation)
            {
                presentation.Draw(
                    new BoneAttachment(gun.PositionUnits, gun.Orientation),
                    camera,
                    projection,
                    firedThisTick: false,
                    shotSequence: 0,
                    WeaponDrawParts.BodyOnly);
            }
            else
            {
                DrawRigidWeapon(visual, gun.PositionUnits / WorldScene.UnitsPerMetre, gun.Orientation, camera);
            }
        }
    }

    /// <summary>Gets whether the holder model remains the visible gun for this ownership state.</summary>
    internal static bool ShouldDrawStaticStationaryGun(AiStationaryGun gun, bool playerUsing)
    {
        ArgumentNullException.ThrowIfNull(gun);
        return !gun.IsOccupied || playerUsing;
    }

    /// <summary>Gets a value indicating whether the player's third-person body is posed and drawing.</summary>
    /// <remarks>Set once the player has died and <see cref="KillPlayerBody"/> has run; the death camera reads it.</remarks>
    public bool IsPlayerBodyActive => _playerBodyActive;

    /// <summary>Scores a guard's round on the <b>player's own body</b> — its skinned collision mesh.</summary>
    /// <param name="origin">Where the round starts, in world units.</param>
    /// <param name="end">Where it stops.</param>
    /// <param name="playerFeetUnits">The player's feet, in world units.</param>
    /// <param name="playerYaw">Which way the player faces, in radians.</param>
    /// <param name="hit">The contact, when there is one.</param>
    /// <returns><see langword="false"/> when the round misses the player's body, or no body is loaded.</returns>
    /// <remarks>
    /// The original resolves a shot at the player exactly as at a soldier — against the human's skinned
    /// collision mesh — so a round through a limb scores that limb (×0.5–0.75), not the torso. The port
    /// had cast at a 0.55 m cylinder and scored every hit as the trunk, which made the player both a
    /// fatter target and one that took maximum damage from every round. This poses the player's own
    /// (otherwise idle) third-person body at the live position and heading and traces the real mesh.
    /// </remarks>
    public bool TryHitPlayerBody(
        Vector3d origin, Vector3d end, Vector3d playerFeetUnits, float playerYaw, out HumanHit hit)
    {
        hit = default;
        if (_playerBodyView is not { } view || _playerBodyGroup?.CollisionMesh is not { } mesh)
        {
            return false;
        }

        view.PlaceAt(playerFeetUnits, Matrix3f.FromEngineEulerAngles(0f, 0f, playerYaw));
        Matrix3f m = view.Orientation;
        Vector3f facing = new(m.M12, m.M22, m.M32);
        return HumanHitTest.TryCastMesh(
            origin, end, playerFeetUnits, facing, mesh, view.Pose(), view.RootSample.Z, out hit);
    }

    /// <summary>Every face of the player's own body a round's line crosses, nearest first.</summary>
    /// <param name="origin">Where the round starts, in world units.</param>
    /// <param name="end">Where it stops.</param>
    /// <param name="playerFeetUnits">The player's feet, in world units.</param>
    /// <param name="playerYaw">Which way the player faces, in radians.</param>
    /// <param name="crossings">The crossings, entries and exits, in order along the ray.</param>
    /// <returns><see langword="false"/> when no body is loaded; an empty list when the round misses it.</returns>
    /// <remarks>
    /// <see cref="TryHitPlayerBody"/>'s cast, carried on through the body: a round that penetrates it meets its
    /// exit, and may meet another part behind (<c>0x47B750</c>).
    /// </remarks>
    public bool TryCastPlayerBody(
        Vector3d origin,
        Vector3d end,
        Vector3d playerFeetUnits,
        float playerYaw,
        out IReadOnlyList<HumanBodyCrossing> crossings)
    {
        crossings = [];
        if (_playerBodyView is not { } view || _playerBodyGroup?.CollisionMesh is not { } mesh)
        {
            return false;
        }

        view.PlaceAt(playerFeetUnits, Matrix3f.FromEngineEulerAngles(0f, 0f, playerYaw));
        Matrix3f m = view.Orientation;
        Vector3f facing = new(m.M12, m.M22, m.M32);
        crossings = HumanHitTest.CastMeshCrossings(
            origin, end, playerFeetUnits, facing, mesh, view.Pose(), view.RootSample.Z);
        return true;
    }

    /// <summary>
    /// Loads the player's third-person body — the model the level's <c>HumanPlayer</c> declares, on
    /// skeleton 0's set, standing in clip 2 — so the death camera has a body to reveal.
    /// </summary>
    /// <param name="mission">The mission whose packs hold the model.</param>
    /// <param name="model">The player's model, from the <c>HumanPlayer</c> task's <c>Model</c> parameter.</param>
    /// <param name="set">Skeleton 0's animation set (<c>000.iff</c>), the same one the player walks on.</param>
    /// <param name="position">Where the level places the player, in engine units.</param>
    /// <param name="gamma">The heading the player starts facing.</param>
    /// <returns><see langword="false"/> when the model does not resolve or does not upload.</returns>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    /// <remarks>
    /// The body is built from a synthetic <see cref="HumanSoldierTask"/> because that is all
    /// <see cref="SoldierView"/> needs — skeleton, stand clip, position and heading — and the player's
    /// constructor (<c>0x410370</c>) builds exactly that on <c>Human+104</c> (see <see cref="HumanPlayerBody"/>).
    /// It is kept out of <c>_groups</c> so nothing else draws or ticks it; only the death path does.
    /// </remarks>
    public bool LoadPlayerBody(
        LoadedMission mission, string model, BoneAnimationSet set, Vector3d position, float gamma)
    {
        ArgumentNullException.ThrowIfNull(mission);
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(set);
        ObjectDisposedException.ThrowIf(_disposed, this);

        ReleasePlayerBody();

        HumanSoldierTask task = new()
        {
            Model = model,
            BoneHierarchy = set.Skeleton.Id,
            StandAnimation = HumanPlayerBody.StandAnimation,
            Position = position,
            Gamma = gamma,
        };

        SoldierView view;
        try
        {
            view = new SoldierView(task, set);
        }
        catch (ArgumentException ex)
        {
            _logger.Warning(LogCategory, $"The player body could not be posed on skeleton {set.Skeleton.Id}: {ex.Message}");
            return false;
        }

        Group? group = BuildGroup(mission, model, view);
        if (group is null)
        {
            return false;
        }

        // The player's body is also a hit-test candidate for incoming AI rounds. Prime the same immutable
        // data as the mission soldiers so the first hostile hit cannot trigger a synchronous parse/build.
        try
        {
            _ = group.CollisionMesh;
        }
        catch (PackFormatException ex)
        {
            _logger.Warning(LogCategory, $"The player collision mesh was not usable: {ex.Message}");
        }

        if (GameplayCVars.Ragdoll.Value && group.Model is not null)
        {
            _ = RagdollRigFor(set, group.Model);
        }

        MeshHandle own;
        try
        {
            own = _device.CreateAnimatedMesh(group.Geometry.Vertices, group.Geometry.Indices);
        }
        catch (Exception ex) when (ex is ArgumentException or GraphicsDeviceException or InvalidOperationException)
        {
            _logger.Warning(LogCategory, $"The player body '{model}' did not upload: {ex.Message}");
            return false;
        }

        _playerBodyView = view;
        _playerBodyGroup = group;
        _playerBodyMember = new Member(view, default, own);
        _playerBodyGround = MakeGroundQuery();
        _playerBodyActive = false;
        _playerBodyHasTickState = false;
        _playerBodyCaptureTick = -1;
        _playerBodySkinTick = -1;
        _logger.Information(LogCategory, $"Loaded the player's third-person body '{model}' on skeleton {set.Skeleton.Id}.");
        return true;
    }

    /// <summary>
    /// Poses the player's body with its death fall and starts drawing it — the player has died.
    /// </summary>
    /// <param name="deathAnimation">The fall clip the death state chose, from the player's hit-zone row.</param>
    /// <param name="feet">Where the body falls, in engine units — the player's own position.</param>
    /// <param name="gamma">Which way the body faces as it goes down.</param>
    /// <param name="bone">The bone the killing round went through, or <c>-1</c> when none was named.</param>
    /// <param name="travel">The killing round's own line, from the muzzle towards where it ended.</param>
    /// <param name="category">What fired it, which is where the impulse's magnitude comes from.</param>
    /// <param name="velocity">What the player was doing at the moment of death, in units per tick.</param>
    /// <remarks>
    /// Does nothing when no body was loaded; the death still happens, just without a body to show. The
    /// last four arguments are the ragdoll's, and are ignored entirely when <c>g_ragdoll</c> is off —
    /// the retail clip above them runs either way.
    /// </remarks>
    public void KillPlayerBody(
        int deathAnimation,
        Vector3d feet,
        float gamma,
        int bone = -1,
        Vector3d travel = default,
        WeaponCategory category = WeaponCategory.Gun,
        Vector3d velocity = default)
    {
        if (_playerBodyView is null)
        {
            return;
        }

        _playerBodyFacing = gamma;
        _playerBodySuppressRootHeight = false;
        _playerBodyVisualLift = 0.0;
        _playerBodyView.PlaceAt(feet, Matrix3f.FromEngineEulerAngles(0f, 0f, gamma));
        _playerBodyView.Kill(deathAnimation);
        _playerBodyActive = true;

        // Seeded before the tick state is captured, so the very first capture is already the corpse's
        // own placement rather than the clip's -- a death is a cut and there is nothing to blend from.
        _playerRagdoll = SeedRagdoll(
            _playerBodyView,
            new Vector3d(feet.X, feet.Y, feet.Z + _playerBodyView.RootSample.Z),
            _playerBodyView.Orientation,
            bone,
            travel,
            category,
            velocity,
            _playerBodyGroup?.Model,
            weapon: null);

        // A death is a cut, not a movement: the body appears where the player fell, and nothing on
        // screen is its history. Snap the interpolation pair and start the frame bookkeeping over.
        CapturePlayerBodyTickState(snap: true);
        if (_playerBodyMember is { } fresh)
        {
            fresh.HasFrameState = false;
            fresh.HasTickState = false;
        }

        // The fall clip has to resolve in the player's set, or Kill silently keeps the idle and the body
        // never falls; reported once so a wrong id is visible rather than a body that just stands there.
        if (_playerBodyView.CurrentAnimation != deathAnimation)
        {
            _logger.Warning(
                LogCategory,
                $"The player's death clip {deathAnimation} is not in its set; the body keeps clip "
                    + $"{_playerBodyView.CurrentAnimation} and will not fall.");
        }
    }

    /// <summary>Advances the player body's death clip by one tick and re-skins it, once it is active.</summary>
    /// <remarks>
    /// The same per-tick re-skin a corpse gets: the clip advances, holding its last frame when it runs out,
    /// and the body's own mesh is rebuilt from the new pose so the fall plays through.
    /// </remarks>
    public void TickPlayerBody()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_playerBodyActive
            || _playerBodyView is null
            || _playerBodyMember is null
            || _playerBodyGroup?.Model is not { } model)
        {
            return;
        }

        _playerBodyView.Tick();

        // The knock-back. A dying body is thrown -- the fall clip's own root-motion track carries it back
        // and down, and the retail mover consumes exactly that (0x4109A0 samples the body clip's root delta
        // through sub_4D2FB0 and moves the human by it, until 0x40E620 sees the body grounded and stops it).
        // Advancing the clip WITHOUT moving the body left it crumpling on the spot instead of being thrown,
        // which is the missing "knocked to the ground" feel. This is the corpse move a soldier already gets
        // (AiSoldier.TickDeadBody): the horizontal root motion, rotated by the facing, plus a ground snap.
        Vector3f rootMotion = _playerBodyView.RootMotion();
        Vector3d world = SoldierDeathFall.RotateAboutZ(
            new Vector3d(rootMotion.X, rootMotion.Y, rootMotion.Z), _playerBodyFacing);

        double px = _playerBodyView.Position.X + world.X;
        double py = _playerBodyView.Position.Y + world.Y;
        double pz = _playerBodyView.Position.Z + world.Z;
        if (_playerBodyGround is { } ground
            && ground(px, py, pz + ProbeAboveInUnits, out double groundZ))
        {
            pz = groundZ;
        }

        _playerBodyView.PlaceAt(new Vector3d(px, py, pz), _playerBodyView.Orientation);

        // The corpse, when there is one. It is stepped after the retail move above rather than instead
        // of it: that move is what the rest of the game believes, and only the drawing changes here.
        if (_playerRagdoll is { } playerCorpse)
        {
            playerCorpse.Step(
                RagdollWorldForCorpses(),
                GameplayCVars.RagdollSubsteps.Value,
                GameplayCVars.RagdollGravity.Value);
            if (!playerCorpse.IsValid)
            {
                _playerRagdoll = null;
                _logger.Warning(LogCategory, "The player's ragdoll left the level; falling back to the death clip.");
            }
        }

        ReskinPlayerBody(_playerBodyView, model);
        CapturePlayerBodyTickState(snap: false);
    }

    /// <summary>Gets whether the player's third-person body is being shown climbing a ladder.</summary>
    public bool IsPlayerBodyClimbing => _playerBodyClimbing;

    /// <summary>Stands the player's body at a place and draws it, outside death and outside climbing.</summary>
    /// <param name="feetUnits">Where the body stands, in engine units — the level's own spawn.</param>
    /// <param name="facing">Which way it faces, as the spawn's gamma.</param>
    /// <remarks>
    /// <para>
    /// A third reason to draw the body, and deliberately a separate flag rather than a reuse of either
    /// existing one. The mission-entry shot dives onto the character standing at their spawn and seats
    /// the view inside their head, so it needs the body visible for a second — but it must not touch
    /// <c>_playerBodyActive</c>, which is the death latch and also drives the fall's per-tick re-skin and
    /// the knock-back, nor the climbing latch, which the ladder code clears on its own schedule.
    /// </para>
    /// <para>
    /// <b>It has to place and re-skin, not merely make visible.</b> <see cref="LoadPlayerBody"/> uploads
    /// the model's REST mesh and leaves the body unplaced — the death path places and poses it, the
    /// climbing path places and poses it, and until one of them runs there is no pose in the vertex
    /// buffer at all. Drawing it in that state put enormous black shards across the middle of the descent,
    /// which is exactly what an unskinned rig looks like when its vertices are still at the origin.
    /// </para>
    /// <para>
    /// Cleared by <see cref="HidePlayerBody"/> and by <see cref="Release"/>, so a mission that ends
    /// mid-shot does not leave a body standing in the next one.
    /// </para>
    /// </remarks>
    public void StandPlayerBodyAt(Vector3d feetUnits, float facing)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_playerBodyView is not { } view)
        {
            return;
        }

        bool appearing = !_playerBodyShown;
        _playerBodyFacing = facing;
        _playerBodySuppressRootHeight = false;
        _playerBodyVisualLift = 0.0;
        view.PlaceAt(feetUnits, Matrix3f.FromEngineEulerAngles(0f, 0f, facing));

        // Its own stand clip, advanced so the body breathes rather than being a statue -- and re-skinned
        // at most once per simulation tick, which is the same rate the climbing path uploads at and the
        // rate the previous-pose stream expects.
        //
        // ON THE FRAME IT APPEARS THE SKIN IS NOT OPTIONAL. The mesh was uploaded from the model's REST
        // geometry and holds no pose until something puts one in it, so if the per-tick guard happened to
        // skip that first frame the body was drawn once from its rest pose -- which is a rig splayed
        // across the world and, from the camera, a flash over the whole screen. The visibility flag is
        // therefore only raised AFTER a successful skin: until the body has a pose it is not drawn.
        bool posed = !appearing;
        if ((appearing || _playerBodySkinTick != _tick) && _playerBodyGroup?.Model is { } model)
        {
            view.Tick();
            ReskinPlayerBody(view, model);
            posed = true;
        }

        if (!posed)
        {
            return;
        }

        _playerBodyShown = true;

        // Appearing is a cut: nothing on screen is this body's history, so neither the interpolation pair
        // nor the temporal history may bridge from wherever it last stood.
        CapturePlayerBodyTickState(snap: appearing);
        if (appearing && _playerBodyMember is { } fresh)
        {
            fresh.HasFrameState = false;
        }
    }

    /// <summary>Stops drawing the standing body — the view has reached the head.</summary>
    public void HidePlayerBody()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _playerBodyShown = false;
    }

    /// <summary>Gets whether the third-person view is currently placing and animating the body.</summary>
    public bool IsPlayerBodyFollowing => _playerBodyFollowing;

    /// <summary>Gets or sets whether the followed body is actually drawn this frame.</summary>
    /// <value>
    /// <see langword="true"/> once the camera has left the head far enough for a body to be a body
    /// rather than the inside of a skull filling the screen.
    /// </value>
    /// <remarks>
    /// Deliberately separate from <see cref="FollowPlayerBody"/>'s latch, and read per FRAME rather than
    /// per tick. The follow keeps placing, animating and capturing the tick pair for the whole of the
    /// move between the two views; only the draw is switched, so crossing the threshold cannot be
    /// mistaken for the body appearing out of nowhere and reset the interpolation and temporal history
    /// that make it move smoothly.
    /// </remarks>
    public bool PlayerBodyFollowVisible { get; set; } = true;

    /// <summary>Raised where a round met a body, so the blood can be thrown there.</summary>
    /// <remarks>
    /// <c>Human</c>'s damage handler (<c>0x45F930</c>) calls the blood (<c>0x463AD0</c>) as its very
    /// first act, <b>before</b> the invulnerability and instant-death gates and before any damage is
    /// applied. So this is raised on contact, not on damage: a friendly round or one on an invulnerable
    /// guard still makes it bleed, exactly as it does in the original. The first argument identifies
    /// the body, whose two particle systems the effect runtime keeps and reuses.
    /// </remarks>
    public Action<object, Vector3d, Vector3f>? BloodSpray { get; set; }

    /// <summary>Walks the third-person body with the player, in the clip the player's own legs are in.</summary>
    /// <param name="feetUnits">Where the player is standing, in engine units — <c>PlayerBody.Position</c>.</param>
    /// <param name="facing">Which way the body faces, in radians — the player's yaw.</param>
    /// <param name="animation">
    /// The clip the player's <see cref="HumanLocomotionAnimator"/> is playing, or
    /// <see cref="HumanLocomotionStates.NoAnimation"/> for the states that name none.
    /// </param>
    /// <param name="loops">Whether that state's table entry loops.</param>
    /// <param name="pose">
    /// The half of the body the legs' clip does not drive — the weapon's torso animation and where the
    /// player is looking. See <see cref="PlayerBodyPose"/>.
    /// </param>
    /// <remarks>
    /// <para>
    /// <b>Called on the simulation tick, not on the frame.</b> The body's clip advances one authoring
    /// frame per <see cref="SoldierView.Tick"/> and the player's animator advances one per tick, so
    /// stepping it here — and only here — is what keeps the two in phase without any clock being copied
    /// between them: the same state entered on the same tick starts the same clip at the same zero. Step
    /// it from the draw instead and a display slower than 30 Hz walks the legs at less than the speed the
    /// body is actually travelling, which is a character skating along the ground.
    /// </para>
    /// <para>
    /// The placement is the ordinary one a soldier gets — the task position lifted by the clip's own root
    /// height (<see cref="PlayerBodyPlacement"/>) — so a crouch sits where a crouching guard sits.
    /// </para>
    /// <para>
    /// <paramref name="facing"/> is a <b>gamma</b>, not the player's yaw: the two run opposite ways, and
    /// the caller negates. See the call site in <c>DesktopGame</c>.
    /// </para>
    /// </remarks>
    public void FollowPlayerBody(Vector3d feetUnits, float facing, int animation, bool loops, in PlayerBodyPose pose)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_playerBodyView is not { } view)
        {
            return;
        }

        bool appearing = !_playerBodyFollowing;
        _playerBodyFacing = facing;
        _playerBodySuppressRootHeight = false;
        _playerBodyVisualLift = 0.0;

        if (animation != HumanLocomotionStates.NoAnimation)
        {
            // A no-op when the state has not changed, so holding a direction does not restart the walk
            // every tick — the same contract the player's own animator keeps.
            view.PlayStateAnimation(animation, loops);
        }

        // The torso, on bone 1. A new request is a new clip even when it names the one already playing,
        // because a second shot has to restart the fire animation -- so the SEQUENCE decides, not the id.
        if (pose.UpperBodySequence != _playerBodyUpperBodySequence)
        {
            _playerBodyUpperBodySequence = pose.UpperBodySequence;
            if (pose.UpperBodyAnimation < 0)
            {
                view.StopUpperBodyAnimation();
            }
            else
            {
                view.PlayUpperBodyAnimation(
                    pose.UpperBodyAnimation, pose.UpperBodyLoops, pose.UpperBodyBlendSeconds);
            }
        }

        view.AimAlong(pose.AimPitch);
        view.PlaceAt(feetUnits, Matrix3f.FromEngineEulerAngles(0f, 0f, facing));

        // The mesh is uploaded from the model's REST geometry and holds no pose until something puts one
        // in it, so the first skin is not optional: without it the body's very first drawn frame is a rig
        // splayed across the world. Once per simulation tick after that.
        bool posed = !appearing;
        if ((appearing || _playerBodySkinTick != _tick) && _playerBodyGroup?.Model is { } model)
        {
            view.Tick();
            ReskinPlayerBody(view, model);
            posed = true;
        }

        if (!posed)
        {
            return;
        }

        _playerBodyFollowing = true;
        CapturePlayerBodyTickState(snap: appearing);
        if (appearing && _playerBodyMember is { } fresh)
        {
            fresh.HasFrameState = false;
        }
    }

    /// <summary>Stops following the player — the view has gone back inside their head.</summary>
    /// <remarks>
    /// <para>
    /// <b>And gives the rig back.</b> The torso clip on bone 1 and the aim bend belong to the follow and
    /// to nothing else: while the player is on a ladder, at a keypad, cutting a wire or dead, an authored
    /// full-body clip owns the whole character, and a weapon stance left in its shoulders replaces the
    /// half of that clip above the waist. <see cref="SoldierView.StartLadderAnimation"/> and
    /// <see cref="SoldierView.Kill"/> release it too, so no ordering between this and them can leave the
    /// override behind — but doing it here as well is what makes the invariant a property of the follow
    /// rather than of a list of call sites.
    /// </para>
    /// <para>
    /// Cheap to repeat: this is called on every tick the follow is not running, and both releases return
    /// at once when there is nothing to release.
    /// </para>
    /// </remarks>
    public void HidePlayerBodyFollowing()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_playerBodyFollowing)
        {
            return;
        }

        _playerBodyFollowing = false;
        _playerBodyUpperBodySequence = -1;
        _playerBodyView?.ReleaseUpperBody();
    }

    /// <summary>Where the third-person body's hand is this frame, for the weapon it is holding.</summary>
    /// <returns>
    /// The hand bone's world placement in engine units, or <see langword="null"/> when no body is being
    /// followed or the rig has no such bone.
    /// </returns>
    /// <remarks>
    /// <see cref="HumanAttachPoints.ThirdPersonWeapon"/> — bone 22, the arm <c>0x4600C0</c> resolves for
    /// a body seen from outside, which is the same bone a guard's rifle rides. Interpolated between the
    /// same two ticks the body itself is drawn between, or the gun steps at 30 Hz in a hand that glides.
    /// </remarks>
    public BoneAttachment? PlayerBodyHandAttachment()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_playerBodyFollowing || _playerBodyView is not { } view || _playerBodyMember is not { } member)
        {
            return null;
        }

        BonePose pose = view.UnrotatedPose();
        if (HumanAttachPoints.ThirdPersonWeapon >= pose.BoneCount)
        {
            return null;
        }

        Vector3f hand = pose.Positions[HumanAttachPoints.ThirdPersonWeapon];
        Matrix3f handRotation = pose.Rotations[HumanAttachPoints.ThirdPersonWeapon];
        if (_frameSmooth && member.HasHandState)
        {
            hand = member.PrevTickHandPosition + ((member.TickHandPosition - member.PrevTickHandPosition) * _frameAlpha);
            handRotation = PresentationBlend.InterpolateRotation(
                member.PrevTickHandRotation, member.TickHandRotation, _frameAlpha);
        }

        (Vector3d where, Matrix3f orientation) = PlayerBodyPlacement();
        if (_frameSmooth && _playerBodyHasTickState)
        {
            where = PresentationBlend.Lerp(_playerBodyPrevTickWhere, _playerBodyTickWhere, _frameAlpha);
        }

        // M . v — the palette's own convention — is the transpose of the row-vector Transform, the same
        // turn DrawWeapon applies to a soldier's hand.
        Vector3f turned = orientation.Transposed().Transform(hand);
        return new BoneAttachment(
            new Vector3d(
                (where.X * WorldScene.UnitsPerMetre) + turned.X,
                (where.Y * WorldScene.UnitsPerMetre) + turned.Y,
                (where.Z * WorldScene.UnitsPerMetre) + turned.Z),
            orientation * handRotation);
    }

    /// <summary>Starts the third-person body in one of IGI1's ladder animation states.</summary>
    /// <param name="animation">The state's authored clip.</param>
    /// <param name="loops">Whether the clip loops.</param>
    /// <param name="backwards">Whether it starts at its end and runs backwards.</param>
    /// <param name="paused">Whether its playback rate starts at zero.</param>
    public void StartPlayerLadderAnimation(
        int animation,
        bool loops,
        bool backwards = false,
        bool paused = false)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_playerBodyView is not { } view)
        {
            return;
        }

        StartPlayerTraversalAnimation(
            view, animation, loops, backwards, paused, suppressRootHeight: true, visualLift: 0.0);

        // Mounting is a cut: the body appears on the rungs, so neither the interpolation pair nor the
        // temporal history may bridge from wherever it stood before.
        ResetPlayerTraversalHistory();
    }

    /// <summary>Starts a non-ladder interaction clip with its authored root height intact.</summary>
    /// <remarks>
    /// Wire animations 172/173 carry a non-zero root-height track. IGI1 adds that sample when locating
    /// the body; suppressing it as if the wire were a ladder leaves both hands visibly off the cable.
    /// </remarks>
    public void StartPlayerInteractionAnimation(int animation, bool loops)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_playerBodyView is not { } view)
        {
            return;
        }

        StartPlayerTraversalAnimation(
            view,
            animation,
            loops,
            backwards: false,
            paused: false,
            suppressRootHeight: false,

            // With the exact 3686.4-unit task anchor from sub_411F20, the posed wrist centres of both
            // retail wire clips sit 375..567 units below the cable. Half a retail model grid cell puts
            // the palms around the cable without moving the gameplay anchor, projection or end margins.
            visualLift: IsWireAnimation(animation) ? 512.0 : 0.0);
        ResetPlayerTraversalHistory();
    }

    private static bool IsWireAnimation(int animation) =>
        animation == HumanLocomotionStates.For(42).AnimationId
        || animation == HumanLocomotionStates.For(43).AnimationId;

    private void StartPlayerTraversalAnimation(
        SoldierView view,
        int animation,
        bool loops,
        bool backwards,
        bool paused,
        bool suppressRootHeight,
        double visualLift)
    {
        _playerBodyClimbing = true;
        _playerBodySuppressRootHeight = suppressRootHeight;
        _playerBodyVisualLift = visualLift;
        view.StartLadderAnimation(animation, loops, backwards, paused);
        _playerBodyRootHeightAtStart = view.RootSample.Z;
    }

    private void ResetPlayerTraversalHistory()
    {
        // Mounting is a cut: the body appears on the rungs, so neither the interpolation pair nor the
        // temporal history may bridge from wherever it stood before.
        _playerBodyHasTickState = false;
        _playerBodySkinTick = -1;
        if (_playerBodyMember is { } member)
        {
            member.HasFrameState = false;
        }
    }

    /// <summary>Changes animation 168's playback direction without restarting or snapping its pose.</summary>
    /// <param name="direction">−1 down, 0 paused at a boundary, or +1 up.</param>
    public void SetPlayerLadderPlaybackDirection(int direction) =>
        _playerBodyView?.SetLadderPlaybackDirection(direction);

    /// <summary>Advances the current ladder clip and returns its movement and state signals.</summary>
    /// <param name="facing">Which way the body faces — into the ladder.</param>
    /// <param name="rootDelta">
    /// Receives the clip's root-motion delta for this tick, already rotated into the facing frame (world
    /// units) — the same transform a walking guard's motion gets. The caller applies it to the position.
    /// </param>
    /// <param name="rungBoundary">Receives whether animation 168 crossed authored event 8.</param>
    /// <param name="ended">Receives whether a non-looping top transition reached its endpoint.</param>
    /// <remarks>Ticks and re-skins the body but does not move it; placement is the caller's, via
    /// <see cref="PlacePlayerBodyClimbing"/>.</remarks>
    public void AdvancePlayerLadderAnimation(
        float facing,
        out Vector3d rootDelta,
        out bool rungBoundary,
        out bool ended)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        rootDelta = default;
        rungBoundary = false;
        ended = false;
        if (_playerBodyView is not { } view || _playerBodyGroup?.Model is not { } model)
        {
            return;
        }

        _playerBodyClimbing = true;
        _playerBodyFacing = facing;
        view.Tick();

        Vector3f rm = view.RootMotion();
        rootDelta = SoldierDeathFall.RotateAboutZ(new Vector3d(rm.X, rm.Y, rm.Z), facing);
        rungBoundary = view.CrossedLadderBoundary;
        ended = view.ClipEndedThisTick;
        ReskinPlayerBody(view, model);

        // The caller places the body with the delta straight after; the placement call captures the
        // tick state, so nothing is recorded here off a stale position.
    }

    /// <summary>Places the climbing body without advancing its clip — holds the pose at a rung boundary.</summary>
    /// <param name="taskPosition">Where IGI1 places the human task root, in engine units.</param>
    /// <param name="facing">Which way it faces — into the ladder.</param>
    public void PlacePlayerBodyClimbing(Vector3d taskPosition, float facing)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_playerBodyView is not { } view)
        {
            return;
        }

        _playerBodyClimbing = true;
        _playerBodyFacing = facing;
        view.PlaceAt(taskPosition, Matrix3f.FromEngineEulerAngles(0f, 0f, facing));

        // A newly paused rung must upload its held pose into both interpolation streams. ReskinPlayerBody's
        // pose latch permits that one duplicate upload, then suppresses further skinning until the pose moves.
        // AdvancePlayerLadderAnimation already uploads each genuinely moving tick.
        if (_playerBodySkinTick != _tick && _playerBodyGroup?.Model is { } model)
        {
            ReskinPlayerBody(view, model);
        }

        CapturePlayerBodyTickState(snap: false);
    }

    /// <summary>Gets the interaction task position adjusted by the clip's visible root-height change.</summary>
    /// <remarks>
    /// The body renderer adds the current root Z above the task, while the third-person camera normally
    /// follows the task itself. Following the delta from the clip's entry height keeps an airborne vault
    /// framed without treating the standing pelvis height as an extra metre of world elevation.
    /// </remarks>
    public Vector3d PlayerInteractionCameraFeet(Vector3d taskPosition)
    {
        if (_playerBodyView is not { } view || _playerBodySuppressRootHeight)
        {
            return taskPosition;
        }

        return new Vector3d(
            taskPosition.X,
            taskPosition.Y,
            taskPosition.Z + view.RootSample.Z - _playerBodyRootHeightAtStart + _playerBodyVisualLift);
    }

    /// <summary>Stops showing the climbing body — the player has left the ladder.</summary>
    public void HidePlayerBodyClimbing()
    {
        _playerBodyClimbing = false;
        _playerBodyVisualLift = 0.0;
        _playerBodyRootHeightAtStart = 0.0;
    }

    // Re-poses the player body's animated mesh in place. Shared by the death fall and the ladder climb.
    private void ReskinPlayerBody(SoldierView view, MefModel model)
    {
        if (_playerBodyMember is not { } member || _playerBodyGroup is null || !member.Mesh.IsValid)
        {
            return;
        }

        BonePose pose = _playerRagdoll is { } corpse ? corpse.Pose() : view.UnrotatedPose();
        member.DrawnPose = pose;
        if (!member.PoseUploads.ShouldUpload(pose))
        {
            _playerBodySkinTick = _tick;
            return;
        }

        Vertex3D[]? skinned = member.SkinScratch;
        if (skinned is null)
        {
            skinned = new Vertex3D[_playerBodyGroup.Geometry.Vertices.Length];
            _playerBodyGroup.Geometry.Vertices.CopyTo(skinned, 0);
            member.SkinScratch = skinned;
        }
        try
        {
            MefSkinner.Skin(model, pose, (float)(1.0 / WorldScene.UnitsPerMetre), skinned);
        }
        catch (ArgumentException)
        {
            return;
        }

        try
        {
            _device.UpdateAnimatedMesh(member.Mesh, skinned);
        }
        catch (ArgumentException)
        {
            return;
        }

        member.PoseFlippedSinceFrame = true;
        _playerBodySkinTick = _tick;
    }

    /// <summary>Where the player's body is drawn, in metres, and which way it is turned.</summary>
    /// <remarks>
    /// <para>
    /// A body is normally shifted by its clip's root height (<c>Human+1300</c>), which for a fall, stand
    /// or wire interaction is part of the authored placement. A ladder position is different: IGI1 writes
    /// the human task root itself (bottom + 3915.776 vertically), and clip 168's lowest foot is authored
    /// at −4128.768. Those values put the foot at ground height already, so adding a pose-derived pelvis
    /// lift there duplicates the mount offset and starts Jones a body-height above the first rung.
    /// </para>
    /// <para>
    /// A simulated corpse answers with its own frozen origin and no turn at all: its pose is already in
    /// world space relative to that origin, with the heading folded into the root bone's rotation.
    /// </para>
    /// </remarks>
    private (Vector3d Where, Matrix3f Orientation) PlayerBodyPlacement()
    {
        if (_playerRagdoll is { } corpse)
        {
            return (corpse.Origin / WorldScene.UnitsPerMetre, Matrix3f.Identity);
        }

        if (_playerBodyView is not { } view)
        {
            return (default, Matrix3f.Identity);
        }

        double lift = (_playerBodySuppressRootHeight ? 0.0 : view.RootSample.Z) + _playerBodyVisualLift;
        return (
            new Vector3d(
                view.Position.X / WorldScene.UnitsPerMetre,
                view.Position.Y / WorldScene.UnitsPerMetre,
                (view.Position.Z + lift) / WorldScene.UnitsPerMetre),
            view.Orientation);
    }

    /// <summary>Gets where the player's corpse actually is, in engine units — what the death camera frames.</summary>
    /// <value>
    /// The body's centre of mass, or <see langword="null"/> when no ragdoll is running — in which case
    /// the death camera's own fixed point off the authoritative body is still the right answer.
    /// </value>
    /// <remarks>
    /// Without this the camera keeps looking at the point the retail clip moved the body to, which once
    /// physics is drawing it somewhere else means framing an empty patch of floor.
    /// </remarks>
    public Vector3d? PlayerRagdollCentre => _playerRagdoll?.Centre;

    /// <summary>Records where the player's body stands this tick, keeping the tick before.</summary>
    /// <param name="snap">
    /// Whether this placement is a discontinuity — a death, a ladder mount — after which the pair
    /// must not be interpolated across.
    /// </param>
    private void CapturePlayerBodyTickState(bool snap)
    {
        if (_playerBodyView is not { } view)
        {
            return;
        }

        Vector3d where = PlayerBodyPlacement().Where;

        if (snap || !_playerBodyHasTickState)
        {
            _playerBodyPrevTickWhere = where;
        }
        else if (_playerBodyCaptureTick != _tick)
        {
            _playerBodyPrevTickWhere = _playerBodyTickWhere;
        }

        // A second capture in the same tick only refines the current state; the previous one stands.
        _playerBodyTickWhere = where;

        // The hand the third-person weapon rides, captured in the same pair and by the same rules a
        // soldier's is (ResolveTickState). Only the followed body needs it: the death and ladder paths
        // draw no weapon, and asking a posed rig for a bone it has costs one array read.
        if (_playerBodyFollowing
            && _playerBodyMember is { } member
            && HumanAttachPoints.ThirdPersonWeapon < view.UnrotatedPose().BoneCount)
        {
            BonePose pose = view.UnrotatedPose();
            Vector3f hand = pose.Positions[HumanAttachPoints.ThirdPersonWeapon];
            Matrix3f rotation = pose.Rotations[HumanAttachPoints.ThirdPersonWeapon];
            bool fresh = snap || !member.HasHandState;
            member.PrevTickHandPosition = fresh
                ? hand
                : _playerBodyCaptureTick != _tick ? member.TickHandPosition : member.PrevTickHandPosition;
            member.PrevTickHandRotation = fresh
                ? rotation
                : _playerBodyCaptureTick != _tick ? member.TickHandRotation : member.PrevTickHandRotation;
            member.TickHandPosition = hand;
            member.TickHandRotation = rotation;
            member.HasHandState = true;
        }

        _playerBodyCaptureTick = _tick;
        _playerBodyHasTickState = true;
    }

    /// <summary>Settles where the player's own body is drawn this frame, and what it moved from.</summary>
    /// <remarks>
    /// <para>
    /// The <see cref="ResolveFrameState"/> a soldier gets, for the one body that is not in a group. Its
    /// placement is a ladder position or a fall or a walk — <see cref="PlayerBodyPlacement"/> knows which
    /// — interpolated between the last two tick captures exactly as a guard's is.
    /// </para>
    /// <para>
    /// A body normally sits at its clip's own root height (<c>Human+1300</c>), which for a fall, a stand
    /// or a wire interaction is part of the authored placement. A ladder position is not: IGI1 writes the
    /// human task root itself (bottom + 3915.776 vertically) and clip 168's lowest foot is authored at
    /// −4128.768, which put the foot at ground height already, so adding a pose-derived pelvis lift there
    /// duplicates the mount offset and starts Jones a body-height above the first rung. That case clears
    /// the lift rather than being special-cased here.
    /// </para>
    /// </remarks>
    private void ResolvePlayerBodyFrameState()
    {
        if (_playerBodyView is null || _playerBodyMember is not { } member)
        {
            return;
        }

        (Vector3d where, Matrix3f orientation) = PlayerBodyPlacement();
        if (_frameSmooth && _playerBodyHasTickState)
        {
            where = PresentationBlend.Lerp(_playerBodyPrevTickWhere, _playerBodyTickWhere, _frameAlpha);
        }

        member.PreviousDrawn = member.HasFrameState ? (member.DrawnWhere, member.DrawnOrientation) : null;
        member.DrawnWhere = where;
        member.DrawnOrientation = orientation;
        member.PreviousPoseBlend = member.PoseFlippedSinceFrame ? 1f : member.PoseBlend;
        member.PoseBlend = _frameSmooth ? 1f - _frameAlpha : 0f;
        member.PoseFlippedSinceFrame = false;
        member.NoHistoryThisFrame = !member.HasFrameState;
        member.HasFrameState = true;
    }

    /// <summary>Draws the player's third-person body, when it is active — what the death camera looks at.</summary>
    /// <param name="camera">The eye, which by now is the death camera pulled back off the body.</param>
    /// <param name="projection">The projection to draw with.</param>
    /// <remarks>
    /// Drawn on its own, not in the soldier loop, and only when active — so it never appears in front of
    /// the first-person view during normal play. The body sits at its own position lifted by the death
    /// clip's root height, exactly as a fallen soldier is (see <see cref="Draw"/>).
    /// </remarks>
    public void DrawPlayerBody(in Camera3D camera, in Matrix4f projection)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        bool visible = _playerBodyActive
            || _playerBodyClimbing
            || _playerBodyShown
            || (_playerBodyFollowing && PlayerBodyFollowVisible);

        // Drawn for nobody, but THERE for a ray. In first person the body is not drawn -- the camera
        // would be inside its head -- and until a reflection could shade a hit from the hit's own
        // material, that also meant it could never appear in a window, because the only geometry a ray
        // can strike is geometry something submitted. It can be shaded now, so it is submitted now: the
        // player gets a reflection of themselves, and a shadow, without their own legs in shot.
        bool tracedOnly = !visible && _playerBodyFollowing;

        if ((!visible && !tracedOnly)
            || _playerBodyView is null
            || _playerBodyMember is not { } member
            || _playerBodyGroup is not { } group)
        {
            return;
        }

        if (tracedOnly)
        {
            SubmitPlayerBodyToTracedWorld(camera, member, group);
            return;
        }

        _device.SetProjection(projection);
        _device.SetCullMode(CullMode.None);
        _device.SetDepthState(DepthState.TestAndWrite);

        Matrix4f modelView = camera.CreateModelView(member.DrawnWhere, member.DrawnOrientation);

        _device.SetPoseInterpolation(member.PoseBlend, member.PreviousPoseBlend);

        // Matte, like every other body: cloth and skin do not shine. It keeps its ambient occlusion,
        // which is the one way it differs from a guard's.
        _device.SetMaterialOverride(new SurfaceMaterial(NoHistory: member.NoHistoryThisFrame, Matte: true));

        foreach (MeshSubset subset in group.Geometry.Subsets)
        {
            if (subset.IndexCount == 0)
            {
                continue;
            }

            TextureHandle diffuse = TextureHandle.None;
            if (subset.HasMaterial
                && group.Instance is { } instance
                && group.Pack is { } pack
                && pack.TryGetTextureName(instance, subset.MaterialSlot, out string texture))
            {
                diffuse = Textures.Get(texture);
            }

            _device.DrawMesh(
                member.Mesh,
                subset.IndexOffset,
                subset.IndexCount,
                modelView,
                diffuse,
                TextureHandle.None,
                BlendMode.Opaque);
        }

        _device.SetMaterialOverride(SurfaceMaterial.Default);

        _device.ClearPoseInterpolation();
    }

    /// <summary>Puts the body into the traced world without drawing any of it.</summary>
    /// <param name="camera">The eye, for the same model-view the drawn path composes.</param>
    /// <param name="member">The body's frame state, which carries its posed mesh.</param>
    /// <param name="group">The body's geometry and the pack its textures come from.</param>
    /// <remarks>
    /// No device state is touched — no projection, no cull mode, no depth state, no pose interpolation —
    /// because none of it reaches a ray and every one of them would leak into whatever draws next. The
    /// pose does not need setting either: the vertex buffer this admits already holds the skinned result,
    /// written by <see cref="ReskinPlayerBody"/> on the animation tick rather than at the draw.
    /// </remarks>
    private void SubmitPlayerBodyToTracedWorld(in Camera3D camera, Member member, Group group)
    {
        Matrix4f modelView = camera.CreateModelView(member.DrawnWhere, member.DrawnOrientation);

        int subsets = 0;
        int textured = 0;
        string first = "none";

        foreach (MeshSubset subset in group.Geometry.Subsets)
        {
            if (subset.IndexCount == 0)
            {
                continue;
            }

            TextureHandle diffuse = TextureHandle.None;
            if (subset.HasMaterial
                && group.Instance is { } instance
                && group.Pack is { } pack
                && pack.TryGetTextureName(instance, subset.MaterialSlot, out string texture))
            {
                diffuse = Textures.Get(texture);
                if (diffuse != TextureHandle.None)
                {
                    textured++;
                    if (first == "none")
                    {
                        first = texture;
                    }
                }
            }

            subsets++;
            // Reflections only: the camera stands inside this body, so to every other ray -- the GI's and
            // the shadows' -- it is an occluder wrapped around the eye, which darkened whatever wall the
            // player faced into a cloud that followed the view. A primitive hidden in game should not
            // affect indirect lighting unless it asks to.
            _device.SubmitTracedMesh(
                member.Mesh, subset.IndexOffset, subset.IndexCount, modelView, diffuse, firstPersonOnly: true);
        }

        // Once, and only for what a reflection needs to know. A body that reflects as a flat silhouette
        // has run out of texture somewhere along a chain that crosses three files, and this says whether
        // it got out of THIS one: no texture here and the ray has nothing to shade from whatever the
        // renderer does later. Named rather than counted, so the name can be checked against the pack.
        if (!_playerBodyTracedReported)
        {
            _playerBodyTracedReported = true;
            _logger.Information(
                LogCategory,
                $"Player body in the traced world: {subsets} subset(s), {textured} with a texture "
                    + $"(first '{first}'); instance {(group.Instance is null ? "absent" : "present")}, "
                    + $"pack {(group.Pack is null ? "absent" : "present")}.");
        }
    }

    private bool _playerBodyTracedReported;

    private void ReleasePlayerBody()
    {
        if (_playerBodyGroup is { } group && group.Mesh.IsValid)
        {
            _device.DestroyMesh(group.Mesh);
        }

        if (_playerBodyMember is { } member && member.Mesh.IsValid)
        {
            _device.DestroyMesh(member.Mesh);
        }

        _playerBodyView = null;
        _playerBodyGroup = null;
        _playerBodyMember = null;
        _playerRagdoll = null;
        _playerBodyActive = false;
        _playerBodyClimbing = false;
        _playerBodyShown = false;
        _playerBodyFollowing = false;
        _playerBodyUpperBodySequence = -1;
        _playerBodyVisualLift = 0.0;
        _playerBodyRootHeightAtStart = 0.0;
        _playerBodyHasTickState = false;
        _playerBodyCaptureTick = -1;
        _playerBodySkinTick = -1;
    }

    /// <summary>Gives the soldiers the grenade simulation their throws land in.</summary>
    /// <param name="grenades">The shared pool — the same one the player's own throws fly in.</param>
    public void SetGrenades(GrenadeSimulation? grenades) => _grenades = grenades;

    /// <summary>Applies one explosion to every living soldier in reach.</summary>
    /// <param name="position">The blast's centre, in world units.</param>
    /// <param name="radiusUnits">The full-damage radius.</param>
    /// <param name="falloffUnits">The taper past it — zero for the shipped grenade.</param>
    /// <param name="damageFactor">The blast's damage factor.</param>
    /// <remarks>
    /// The retail rules through the port's own pieces: the dose is
    /// <see cref="ExplosionDamage.Dose"/> with the body radius at a third, and it lands on
    /// <b>zone 13</b> — an explosion never headshots, however the target stands. A kill goes through
    /// the same death path a bullet's does, with the blast direction as the round.
    /// </remarks>
    public void ApplyExplosion(Vector3d position, float radiusUnits, float falloffUnits, float damageFactor)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        foreach (Group group in _groups)
        {
            foreach (Member member in group.Soldiers)
            {
                SoldierView soldier = member.View;
                if (!ConditionalContainerGate.IsTaskActive(soldier.Soldier))
                {
                    continue;
                }
                HumanHealth health = HealthOf(soldier);

                if (health.IsDead)
                {
                    continue;
                }

                Vector3d feet = soldier.Position;
                double distance = Math.Sqrt(
                    ((feet.X - position.X) * (feet.X - position.X))
                    + ((feet.Y - position.Y) * (feet.Y - position.Y))
                    + ((feet.Z - position.Z) * (feet.Z - position.Z)));

                float dose = ExplosionDamage.Dose(
                    (float)distance,
                    ExplosionDamage.EffectiveBodyRadius((float)HumanSeparation.RadiusInUnits),
                    radiusUnits,
                    falloffUnits,
                    damageFactor);
                if (dose <= 0f)
                {
                    continue;
                }

                AiSoldier? brain = _brains.TryGetValue(soldier, out AiSoldier? b) ? b : null;
                if (brain?.IsInvulnerable ?? false)
                {
                    continue;
                }

                float dealt = health.Apply(ExplosionDamage.Zone, dose, brain?.IsInstantDeathDisabled ?? false);
                brain?.ReceiveDamage(position);

                if (health.IsDead)
                {
                    // 0x45A3D0 records death mode 0 for a blast: the body is thrown, turned to it.
                    HumanHit hit = new(feet, distance, ExplosionDamage.Zone, 0f, 0f);
                    Die(soldier, hit, position, feet, WeaponCategory.Grenade, SoldierDeathMode.Untouched);
                }
                else
                {
                    ReactToHit(soldier, brain, ExplosionDamage.Zone, dealt, explosion: true);
                }
            }
        }
    }

    // The lob: reach the target's feet in the settings' own fly time, and cut the fuse to the
    // settings' explode time. The velocity is the closed-form ballistic answer for the port's
    // per-tick integrator: v = d/T + g(T+1)/2 upward, which lands EXACTLY on the target after T
    // ticks of `v.z -= g; p += v`.
    private void ThrowAiGrenade(AiSoldier brain, Vector3d target, GrenadeSimulation grenades)
    {
        AiCombatParameters? parameters = brain.CombatParameters;
        if (parameters is null)
        {
            return;
        }

        float flySeconds = parameters.GrenadeMinimumFlyTimeSeconds
            + ((float)_aiRandom.NextDouble() * parameters.GrenadeRandomFlyTimeSeconds);
        int flyTicks = Math.Max(1, (int)(flySeconds * 30f));

        Vector3d origin = brain.EyePosition();
        Vector3f velocity = new(
            (float)((target.X - origin.X) / flyTicks),
            (float)((target.Y - origin.Y) / flyTicks),
            (float)((target.Z - origin.Z) / flyTicks) + (GrenadePhysics.GravityPerTick * (flyTicks + 1) * 0.5f));

        float fuseSeconds = parameters.GrenadeMinimumExplodeTimeSeconds
            + ((float)_aiRandom.NextDouble() * parameters.GrenadeRandomExplodeTimeSeconds);
        int fuseTicks = fuseSeconds > 0f ? Math.Max(1, (int)(fuseSeconds * 30f)) : GrenadePhysics.FuseTicks;

        grenades.SpawnWithVelocity(origin, velocity, GrenadeExplosionType.Normal, fuseTicks);
    }

    /// <summary>Draws the one-tick muzzle flames of every soldier that fired, additively.</summary>
    /// <param name="camera">The eye the scene is drawn from.</param>
    /// <param name="projection">The world's view-to-clip transform.</param>
    /// <remarks>
    /// Once per frame, in the <b>forward</b> pass — never from <see cref="Draw"/>, which serves the
    /// geometry and shadow passes where an additive glow would be shaded like scenery. The flame is
    /// drawn at the same resolved weapon transform the body pass used, so the two halves of the gun
    /// agree to the pixel.
    /// </remarks>
    public void DrawMuzzleFlashes(in Camera3D camera, in Matrix4f projection)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        foreach (Group group in _groups)
        {
            foreach (Member member in group.Soldiers)
            {
                if (!member.FiredThisTick
                    || !member.HasWeaponFrameState
                    || member.Weapon is not { } weapon
                    || weapon.Presentation is not { } presentation)
                {
                    continue;
                }

                presentation.Draw(
                    new BoneAttachment(
                        member.DrawnWeaponOrigin * WorldScene.UnitsPerMetre,
                        member.DrawnWeaponRotation),
                    camera,
                    projection,
                    firedThisTick: true,
                    member.ShotSequence,
                    WeaponDrawParts.FlameOnly);
            }
        }

        // A gun the player is firing from its stand flames at the same pose DrawStationaryGuns drew.
        foreach ((AiStationaryGun gun, HeldWeapon? visual) in _stationaryWeaponVisuals)
        {
            if (!gun.FiredThisTick || visual?.Presentation is not { } gunPresentation)
            {
                continue;
            }

            gunPresentation.Draw(
                new BoneAttachment(gun.GunPosition, gun.GunOrientation),
                camera,
                projection,
                firedThisTick: true,
                gun.ShotSequence,
                WeaponDrawParts.FlameOnly);
        }

        // A vehicle's gun flames on the ticks it puts a round out, on the weapon object DrawVehicleGuns drew. Only a
        // weapon model with a gun-flame child has one: a T80's M2HB does, its cannon does not.
        if (_vehicleGuns is { } vehicleGuns)
        {
            foreach (VehicleGunVisual gun in vehicleGuns())
            {
                if (!gun.FiredThisTick
                    || !_vehicleWeaponVisuals.TryGetValue(gun.WeaponModel, out HeldWeapon? visual)
                    || visual?.Presentation is not { } vehiclePresentation)
                {
                    continue;
                }

                vehiclePresentation.Draw(
                    new BoneAttachment(gun.PositionUnits, gun.Orientation),
                    camera,
                    projection,
                    firedThisTick: true,
                    gun.ShotSequence,
                    WeaponDrawParts.FlameOnly);
            }
        }
    }

    /// <summary>Draws the weapon a soldier is holding, at its hand bone's own world transform.</summary>
    /// <param name="member">The soldier.</param>
    /// <param name="camera">The eye.</param>
    /// <param name="projection">The current world projection.</param>
    /// <remarks>
    /// <para>
    /// The attachment handlers (<c>0x4600C0</c>, <c>player.md</c> §13) do exactly two things: they add the
    /// object's own position to the bone's, and they <b>copy the bone's rotation whole</b>. There is no
    /// per-weapon offset anywhere in the engine, so the model's own origin is where the hand grips it.
    /// </para>
    /// <para>
    /// The transform itself — hand bone through the body's heading, interpolated between ticks — was
    /// resolved by <see cref="ResolveFrameState"/>. The composed rotation is <c>heading · bone</c>,
    /// applied as <c>M·v</c>, which is the convention
    /// <see cref="Camera3D.CreateModelView(Vector3d, Matrix3f)"/> and the bone palette both use —
    /// <b>not</b> <see cref="Matrix3f.Transform(Vector3f)"/>'s row-vector one, which is its inverse.
    /// </para>
    /// <para>
    /// A gun is rigid, so unlike the body its previous <em>transform</em> reprojects every vertex
    /// exactly; last frame's is reported as its previous pose on the geometry pass.
    /// </para>
    /// </remarks>
    private void DrawWeapon(Member member, in Camera3D camera, in Matrix4f projection)
    {
        if (member.Weapon is not { } weapon || !member.HasWeaponFrameState)
        {
            return;
        }

        if (weapon.Presentation is { } presentation)
        {
            // Body only: the muzzle flame is LIGHT, and drawing it here put it into the deferred
            // G-buffer to be shaded like scenery -- a one-frame dark square at the muzzle. It is
            // drawn by DrawMuzzleFlashes instead, in the forward pass, like the player's own.
            presentation.Draw(
                new BoneAttachment(
                    member.DrawnWeaponOrigin * WorldScene.UnitsPerMetre,
                    member.DrawnWeaponRotation),
                camera,
                projection,
                firedThisTick: false,
                member.ShotSequence,
                WeaponDrawParts.BodyOnly);
            return;
        }

        DrawRigidWeapon(weapon, member.DrawnWeaponOrigin, member.DrawnWeaponRotation, camera);
    }

    // A weapon model drawn whole as one rigid mesh, for when no multipart presenter was built for it.
    // The origin is in metres, as CreateModelView takes it.
    private void DrawRigidWeapon(HeldWeapon weapon, Vector3d originMetres, Matrix3f rotation, in Camera3D camera)
    {
        Matrix4f modelView = camera.CreateModelView(originMetres, rotation);

        foreach (MeshSubset subset in weapon.Geometry.Subsets)
        {
            if (subset.IndexCount == 0)
            {
                continue;
            }

            TextureHandle diffuse = TextureHandle.None;
            if (subset.HasMaterial
                && weapon.Instance is { } instance
                && weapon.Pack is { } pack
                && pack.TryGetTextureName(instance, subset.MaterialSlot, out string texture))
            {
                diffuse = Textures.Get(texture);
            }

            _device.DrawMesh(
                weapon.Mesh,
                subset.IndexOffset,
                subset.IndexCount,
                modelView,
                diffuse,
                TextureHandle.None,
                BlendMode.Opaque);
        }

    }

    /// <summary>Offers a round to every soldier and scores it against the nearest one it meets.</summary>
    /// <param name="origin">Where the round starts, in world units.</param>
    /// <param name="end">Where it stops.</param>
    /// <param name="damageFactor">The weapon's <c>Damage Factor</c>.</param>
    /// <param name="category">The firing weapon's category, which decides how a body it kills falls.</param>
    /// <param name="hit">The nearest soldier struck.</param>
    /// <returns><see langword="false"/> when the round meets nobody.</returns>
    /// <remarks>
    /// <b>The damage is applied here</b>, because the health belongs to the soldier and not to the
    /// round. A soldier already dead is skipped rather than shot again — the original stops a corpse
    /// taking damage through a flag on the human, and this is the same effect with the state that
    /// exists.
    /// </remarks>
    public bool TryShoot(
        Vector3d origin,
        Vector3d end,
        float damageFactor,
        WeaponCategory category,
        out HumanHit hit)
    {
        hit = default;

        HumanHealth? struck = null;
        SoldierView? struckView = null;
        Vector3d ray = new(end.X - origin.X, end.Y - origin.Y, end.Z - origin.Z);
        double reach = Math.Sqrt((ray.X * ray.X) + (ray.Y * ray.Y) + (ray.Z * ray.Z));
        Vector3d direction = reach > 0.0
            ? new Vector3d(ray.X / reach, ray.Y / reach, ray.Z / reach)
            : default;

        foreach (Group group in _groups)
        {
            foreach (Member member in group.Soldiers)
            {
                SoldierView soldier = member.View;
                if (!ConditionalContainerGate.IsTaskActive(soldier.Soldier))
                {
                    continue;
                }
                HumanHealth health = HealthOf(soldier);

                if (health.IsDead)
                {
                    continue;
                }

                // The player fires against the body the crosshair was drawn over. With r_smooth that is
                // the last presentation transform, not the newer simulation state the next tick has just
                // published. AI fire stays on its own fixed-tick path below: this is only the player-shot
                // delegate supplied by Shooter().
                bool presented = _frameSmooth && member.HasFrameState;
                Vector3d feet;
                Matrix3f m;
                if (presented)
                {
                    Vector3d root = member.DrawnWhere * WorldScene.UnitsPerMetre;
                    feet = new Vector3d(root.X, root.Y, root.Z - soldier.RootSample.Z);
                    m = member.DrawnOrientation;
                }
                else
                {
                    // r_smooth off is the vanilla simulation transform verbatim. The feet the round is
                    // cast at must be the SAME feet the body is drawn on — grounded onto the floor — and
                    // the bones sit a root height above them, exactly as the draw lifts the pelvis.
                    double snappedZ = SnapToGround(soldier.Position.X, soldier.Position.Y, soldier.Position.Z);
                    feet = new Vector3d(soldier.Position.X, soldier.Position.Y, snappedZ);
                    m = soldier.Orientation;
                }
                Vector3f facing = new(m.M12, m.M22, m.M32);

                // The faithful path: trace the round at the soldier's skinned collision mesh and score it by
                // the bone the round actually went through (BoneDynCubeObj_TraceLinesHandler). Skinning every
                // collision vertex for every guard is far costlier than the ray itself, so reject bodies the
                // segment cannot reach with the same generous body sphere TryPick uses. Starting inside the
                // sphere still reaches the mesh, which preserves point-blank hits even when the round ends
                // before it leaves the broad-phase sphere. Only if the model ships no collision mesh does it
                // fall back to the cylinder-and-nearest-bone stand-in.
                bool met;
                HumanHit candidate;
                if (group.CollisionMesh is { } collision)
                {
                    Vector3d centre = new(feet.X, feet.Y, feet.Z + PickChestUnits);
                    double offsetX = origin.X - centre.X;
                    double offsetY = origin.Y - centre.Y;
                    double offsetZ = origin.Z - centre.Z;
                    bool startsInside = (offsetX * offsetX) + (offsetY * offsetY) + (offsetZ * offsetZ)
                        <= PickCullRadiusUnits * PickCullRadiusUnits;
                    if (reach <= 0.0 || (!startsInside
                        && !RaySphere(origin, direction, centre, PickCullRadiusUnits, reach, out _)))
                    {
                        continue;
                    }

                    if (presented
                        && member.SkinnedPose is { } currentPose
                        && member.PreviousSkinnedPose is { } previousPose)
                    {
                        // The renderer already owns both poses used by the motion-blended draw. Reuse
                        // them directly; do not re-evaluate the animation for a candidate that has
                        // already survived the broad phase.
                        met = HumanHitTest.TryCastMeshBlended(
                            origin,
                            end,
                            member.DrawnWhere * WorldScene.UnitsPerMetre,
                            facing,
                            collision,
                            currentPose,
                            previousPose,
                            member.DrawnOrientation,
                            member.PoseBlend,
                            out candidate);
                    }
                    else
                    {
                        // Pose evaluation (especially a presented-orientation re-evaluation) skins the
                        // complete rig. Keep it behind the ray/sphere rejection so off-ray soldiers do
                        // not pay that cost on every scoped shot.
                        BonePose pose = presented
                            ? PoseAtPresentationOrientation(soldier, m)
                            : soldier.Pose();
                        met = HumanHitTest.TryCastMesh(
                            origin, end, feet, facing, collision, pose, soldier.RootSample.Z, out candidate);
                    }
                }
                else
                {
                    // The cylinder fallback still needs a pose for bone-zone refinement, but only after
                    // there is no collision mesh broad phase to skip.
                    BonePose pose = presented
                        ? PoseAtPresentationOrientation(soldier, m)
                        : soldier.Pose();
                    met = HumanHitTest.TryCast(
                        origin, end, feet, facing, pose, out candidate, soldier.RootSample.Z);
                }

                if (!met)
                {
                    continue;
                }

                if (struck is null || candidate.Distance < hit.Distance)
                {
                    hit = candidate;
                    struck = health;
                    struckView = soldier;
                }
            }
        }

        // Human_EvaluateGunShotHandler evaluates each live projectile segment independently of ordinary
        // shot audibility. Do this after choosing the nearest collision so the struck guard gets the
        // TAKINGDAMAGE path while nearby guards get GUNSHOTMISS.
        NotifyGunshotMiss(origin, end, struckView, origin);

        if (struck is null)
        {
            return false;
        }

        // 0x45F94A, before anything else the damage handler does.
        if (BloodSpray is { } bleed)
        {
            Vector3d line = end - origin;
            double length = line.Length;
            bleed(
                (object?)struckView ?? struck,
                hit.Position,
                length > 1e-6
                    ? new Vector3f(
                        (float)(line.X / length), (float)(line.Y / length), (float)(line.Z / length))
                    : default);
        }

        // The soldier's brain carries two flags the damage handler 0x45D3C0 reads: invulnerability skips
        // the whole damage block (the round still MEETS the body, it just does nothing), and instant-death
        // being disabled makes a head/neck hit take the plain damage factor rather than the ×100 lethal
        // multiplier. Both default off, so an ordinary guard is unaffected.
        AiSoldier? brain = struckView is not null && _brains.TryGetValue(struckView, out AiSoldier? b) ? b : null;

        // Friendly fire: the player's round cannot hurt a soldier on the player's own team. Retail disables
        // same-team damage (the round still meets the body and the hit registers, but it does nothing), so
        // a friendly stands where the round landed without a scratch. Same mechanism as invulnerability:
        // the whole damage block is skipped, and the death branch never runs.
        bool friendlyFire = struckView is not null && struckView.Soldier.Team == _playerTeam;
        bool invulnerable = friendlyFire || (brain?.IsInvulnerable ?? false);
        if (!invulnerable)
        {
            float dealt = struck.Apply(hit.Zone, damageFactor, brain?.IsInstantDeathDisabled ?? false);
            brain?.ReceiveDamage(origin);

            if (struck.IsDead && struckView is not null)
            {
                Die(struckView, hit, origin, end, category, SoldierDeath.ModeFor(category));
            }
            else if (struckView is not null)
            {
                ReactToHit(struckView, brain, hit.Zone, dealt, explosion: false);

                // 0x45D3C0 adds the blade's own sound to the grunt when the weapon's category is the knife.
                if (category == WeaponCategory.Knife && struckView.Soldier.Class.SoundsKnifeStab)
                {
                    _scriptSound?.Invoke(KnifeStabSound, struckView.Position, true);
                }
            }
        }

        // Deliberately the same shape as the probe's [dmg] line from the retail game (probes.cpp
        // OnDamageHandler), so a run of the port and a capture of the original diff directly. The port
        // resolves the zone from the pose (HumanHitTest.NearestBone) where the engine reads a part index
        // the bullet physics recorded, so a zone that disagrees for the same shot is the one to chase.
        _logger.Information(LogCategory + ".Damage",
            $"port zone={hit.Zone} ({HumanDamage.ZoneName(hit.Zone)}) bone={hit.Bone} dmgFactor={damageFactor:0.000} "
            + $"invuln={(invulnerable ? 1 : 0)} instDeathDisabled={(brain?.IsInstantDeathDisabled ?? false ? 1 : 0)} "
            + $"friendly={(friendlyFire ? 1 : 0)} accum={struck.Damage:0.000} max={struck.MaximumHealth:0.000} dead={(struck.IsDead ? 1 : 0)}");

        return true;
    }

    /// <summary>Evaluates a soldier's current animation at the heading actually displayed for this frame.</summary>
    /// <remarks>
    /// A <see cref="SoldierView"/> caches a faced pose, whereas drawing applies the r_smooth orientation
    /// after the identical unrotated animation. Re-evaluating only the cached pose here gives the hit mesh
    /// the same animated limbs and presented heading without changing the authoritative AI transform.
    /// </remarks>
    private static BonePose PoseAtPresentationOrientation(SoldierView soldier, in Matrix3f orientation)
    {
        Matrix3f simulationOrientation = soldier.Orientation;
        if (simulationOrientation == orientation)
        {
            return soldier.Pose();
        }

        Vector3d simulationPosition = soldier.Position;
        try
        {
            soldier.PlaceAt(simulationPosition, orientation);
            return soldier.Pose();
        }
        finally
        {
            soldier.PlaceAt(simulationPosition, simulationOrientation);
        }
    }

    /// <summary>Gets a soldier's damage state, creating it on the first hit.</summary>
    private HumanHealth HealthOf(SoldierView soldier)
    {
        if (!_health.TryGetValue(soldier, out HumanHealth? health))
        {
            health = NewHealth(soldier);
            _health[soldier] = health;
        }

        return health;
    }

    /// <summary>A non-lethal hit: the soldier's class decides whether it flinches, and it grunts.</summary>
    /// <remarks>
    /// Every hit handler plays the grunt on the non-lethal branch, and an explosion only when it made the
    /// soldier react (<c>0x45A3D0</c>). The flinch is the bit the handlers raise for the body machine.
    /// </remarks>
    private void ReactToHit(SoldierView soldier, AiSoldier? brain, int zone, float dealt, bool explosion)
    {
        if (!_recoil.TryGetValue(soldier, out HumanRecoil? recoil))
        {
            float? configured = _combat.TryGetValue(soldier, out AiCombatParameters? row) ? row.RecoilThreshold : null;
            recoil = soldier.Soldier.Class.CreateRecoil(configured);
            _recoil[soldier] = recoil;
        }

        HumanRecoilOutcome outcome = recoil.Absorb(dealt);
        if (outcome.Flinches)
        {
            brain?.Flinch(zone);
        }

        if (!explosion)
        {
            ReportHitSound(soldier);
        }
        else if (outcome.Reacted)
        {
            ReportHitSound(soldier, HumanVariantSound.AiExplosionHitPrefix, HumanVariantSound.AiExplosionHitVariants);
        }
    }

    /// <summary>
    /// A fresh damage state — <c>0x489C30</c> over the class's hit-zone table, with the settings row's
    /// maximum health once <c>0x406F70</c> has configured the soldier.
    /// </summary>
    private HumanHealth NewHealth(SoldierView soldier) => new(
        soldier.Soldier.Class.HitZones,
        _combat.TryGetValue(soldier, out AiCombatParameters? row) ? row.MaximumHealth : HumanHitZoneTable.MaximumHealth);

    /// <summary>Puts a soldier into the general death state — <c>0x45C440</c>, joined up.</summary>
    /// <remarks>
    /// <para>
    /// Four things happen here and every one of them is read. The body <b>turns to the round</b>
    /// (<c>0x45DAA0</c>); the death <b>animation is chosen</b> from the killing weapon's category, the zone
    /// struck and which way the round came (<see cref="SoldierDeath.Choose"/>); the <b>fall solver</b>
    /// fans its nine rays and returns the corrective velocity (<see cref="SoldierDeathFall"/>); and the
    /// brain is told, so it steps the body every tick from then on instead of freezing it.
    /// </para>
    /// </remarks>
    private void Die(
        SoldierView view,
        HumanHit hit,
        Vector3d origin,
        Vector3d end,
        WeaponCategory category,
        SoldierDeathMode mode)
    {
        Matrix3f orientation = view.Orientation;
        Vector3f forward = new(orientation.M12, orientation.M22, orientation.M32);

        HumanSoldierClass soldierClass = view.Soldier.Class;
        _brains.TryGetValue(view, out AiSoldier? brain);
        int scriptOverride = brain?.DeathAnimationOverride ?? -1;
        Vector3d travel = new(end.X - origin.X, end.Y - origin.Y, end.Z - origin.Z);
        float facing = GammaOf(view);

        int animation;
        if (brain?.Body.PostureDeathAnimation is { } postureDeath)
        {
            // A kneeling or prone body dies in its posture's own state (0x45C5A0, 0x45C650), which plays a
            // fixed clip and does not turn.
            animation = postureDeath;
        }
        else
        {
            // 0x45C440 turns the body to the round (0x45DAA0) only in its thrown branch, which the script's
            // override skips: a crumple falls with the facing it died with.
            bool front = true;
            if (scriptOverride == -1 && soldierClass.TurnsToTheShot(mode))
            {
                front = SoldierDeath.TurnToTheShot(travel, forward, ref facing);
            }

            // The engine's own rand(2). Alternating is not the same distribution, but the two retail
            // head-row entries are equal and the two crumple clips are a pair, so what the flip has to do
            // is vary them.
            int flip = _deathsSoFar++ & 1;
            animation = soldierClass.ChooseDeathAnimation(mode, hit.Zone, front, flip, scriptOverride);
        }

        Vector3d fall = default;
        if (animation >= 0 && view.Bones.Set.TryGet(animation, out BoneAnimation clip) && _cast is { } cast)
        {
            fall = SoldierDeathFall.Solve(
                clip,
                view.Position,
                facing,
                (from, to) => cast(from, to, out WorldRayHit blocked)
                    ? new SoldierDeathRay(true, blocked.Position, Vector3d.Distance(from, blocked.Position))
                    : default);
        }

        view.PlaceAt(view.Position, Matrix3f.FromEngineEulerAngles(0f, 0f, facing));
        view.Kill(animation);

        if (brain is not null)
        {
            brain.Kill(fall, facing);
        }

        // And the death cry, from here rather than from AiSoldier.Kill: the per-tick loop calls Kill on
        // every corpse it steps, so a hook there would restart the sound thirty times a second. This is
        // the alive-to-dead edge, and it happens once.
        _deathSound?.Invoke(view.Position);
        _footCursors.Remove(view);

        // Everything above is the original's, and it stays that way whatever follows: the body's
        // authoritative position, the clip that moves it and what the guards around it are told are all
        // exactly what they were. The ragdoll replaces THE POSE THAT IS DRAWN, and nothing else.
        if (FindMember(view) is { } member)
        {
            Vector3f carried = view.RootMotion();
            double grounded = SnapToGround(view.Position.X, view.Position.Y, view.Position.Z);
            Vector3d ragdollOrigin = new(view.Position.X, view.Position.Y, grounded + view.RootSample.Z);
            Vector3d dropPosition = view.Position;
            Matrix3f dropRotation = view.Orientation;
            WeaponType? droppedWeapon = WeaponForDrop(member.CarriedWeaponType, member.WeaponType);
            if (droppedWeapon is not null)
            {
                BonePose pose = view.UnrotatedPose();
                if (HumanAttachPoints.ThirdPersonWeapon < pose.BoneCount)
                {
                    Vector3f hand = pose.Positions[HumanAttachPoints.ThirdPersonWeapon];
                    Vector3f offset = view.Orientation.Transposed().Transform(hand);
                    dropPosition = ragdollOrigin + new Vector3d(offset.X, offset.Y, offset.Z);
                    dropRotation = view.Orientation * pose.Rotations[HumanAttachPoints.ThirdPersonWeapon];
                }
            }
            member.Ragdoll = SeedRagdoll(
                view,
                ragdollOrigin,
                view.Orientation,
                hit.Bone,
                travel,
                category,

                // What the body was already doing. A soldier's clip root motion is its only velocity --
                // the port gives one nothing else -- and it is a per-tick delta already, so it goes in
                // as it stands, turned out of body space by the heading.
                SoldierDeathFall.RotateAboutZ(new Vector3d(carried.X, carried.Y, carried.Z), facing),
                FindModel(view),
                weapon: null);

            if (droppedWeapon is { } dropped)
            {
                member.Weapon = null;
                _weaponDrop?.Invoke(
                    dropped,
                    dropPosition,
                    dropRotation,
                    DropVelocity(SoldierDeathFall.RotateAboutZ(new Vector3d(carried.X, carried.Y, carried.Z), facing), travel));
            }
        }
    }

    /// <summary>What a gun leaves a dying hand with: the body's own motion plus a share of the round's knock.</summary>
    /// <param name="carried">The body's motion, units per tick, in world space.</param>
    /// <param name="travel">The killing round's line.</param>
    /// <returns>The launch velocity, units per tick.</returns>
    /// <remarks>
    /// The grip opens as the round lands, so the gun keeps what the body was doing and takes a small
    /// push along the shot -- sixty centimetres a second, a sideways stumble rather than a throw -- with a
    /// little lift from the arm flinching. Only the realistic drop reads it; the original's ignores it.
    /// </remarks>
    internal static Vector3d DropVelocity(Vector3d carried, Vector3d travel)
    {
        const double knock = 0.6 * 4096.0 / 30.0;
        const double lift = 0.3 * 4096.0 / 30.0;
        Vector3d flat = new(travel.X, travel.Y, 0.0);
        double length = flat.Length;
        Vector3d push = length > 1e-6 ? flat * (knock / length) : default;
        return carried + push + new Vector3d(0.0, 0.0, lift);
    }

    /// <summary>Gets a delegate that offers rounds to these soldiers.</summary>
    /// <returns>The delegate the player takes.</returns>
    public HumanShot Shooter() => TryShoot;

    /// <summary>
    /// Offers an AI-fired round to the soldiers and scores it against the nearest one it meets.
    /// This is the NPC-vs-NPC counterpart to <see cref="TryShoot"/>: a guard's round can strike a
    /// friendly, and a friendly's can strike a guard, so Harrison's team at the gate trades fire with
    /// the guards instead of only ever being aimed at the player.
    /// </summary>
    /// <param name="origin">Where the round starts, in world units.</param>
    /// <param name="end">Where the round stops.</param>
    /// <param name="hitProbability">
    /// The firing AI's chance the round is on target (its <c>Human+1236</c>). Retail rolls it only against the
    /// player, so a soldier the round meets never consults it; it is kept for the callers' symmetry.
    /// </param>
    /// <param name="weapon">The firing weapon, whose damage factor and category apply on a hit.</param>
    /// <param name="shooter">The soldier that fired; its own body is never offered the round.</param>
    /// <param name="worldHit">
    /// The nearest world surface ahead, when the world blocks the ray; a soldier behind it is never
    /// offered the round (the wall already took it). <see langword="null"/> when no surface stopped it.
    /// </param>
    /// <param name="hit">The soldier struck, when one is.</param>
    /// <returns>
    /// <see langword="true"/> when a soldier consumed the round (damaged, or a same-team/invulnerable
    /// body the round met and stopped at); <see langword="false"/> when it met nobody and flies on.
    /// </returns>
    /// <remarks>
    /// <b>The damage is applied here</b>, exactly as in <see cref="TryShoot"/>. The Human's evaluate
    /// (<c>0x45E3E0</c>) decides each body the round meets, nearest first: one on the shooter's own side
    /// (<c>Human+252</c>) lets it pass through to the next, any other takes it -- with no probability roll,
    /// which retail makes only for the player. Scripted invulnerability (500.qvm sets
    /// <c>SetInvulnerability(TRUE)</c> on the friendly squad, so they cannot die) makes the round
    /// <em>meet</em> the body but do nothing; the body is still a physical obstacle that consumes it.
    /// </remarks>
    public bool TryShootAiRound(
        Vector3d origin,
        Vector3d end,
        float hitProbability,
        WeaponType weapon,
        SoldierView? shooter,
        WorldRayHit? worldHit,
        out HumanHit hit)
    {
        hit = default;

        List<(HumanHealth health, SoldierView view, HumanHit candidate, double distance)> candidates = [];

        foreach (Group group in _groups)
        {
            foreach (Member member in group.Soldiers)
            {
                SoldierView soldier = member.View;
                if (!ConditionalContainerGate.IsTaskActive(soldier.Soldier)
                    || ReferenceEquals(soldier, shooter))
                {
                    continue;
                }
                HumanHealth health = HealthOf(soldier);
                if (health.IsDead)
                {
                    continue;
                }

                // Same facing/feet/pose resolution as TryShoot: cast at the skinned collision mesh when
                // the model ships one, else the cylinder-and-nearest-bone stand-in, both on the grounded
                // feet the body is actually drawn on.
                Matrix3f m = soldier.Orientation;
                Vector3f facing = new(m.M12, m.M22, m.M32);
                double snappedZ = SnapToGround(soldier.Position.X, soldier.Position.Y, soldier.Position.Z);
                Vector3d feet = new(soldier.Position.X, soldier.Position.Y, snappedZ);

                bool met = group.CollisionMesh is { } collision
                    ? HumanHitTest.TryCastMesh(
                        origin, end, feet, facing, collision, soldier.Pose(), soldier.RootSample.Z, out HumanHit candidate)
                    : HumanHitTest.TryCast(
                        origin, end, feet, facing, soldier.Pose(), out candidate, soldier.RootSample.Z);
                if (!met)
                {
                    continue;
                }

                // A wall between the muzzle and this soldier stops the round before it arrives.
                double distance = candidate.Distance;
                if (worldHit is { } surface && surface.Distance < distance - 40.0)
                {
                    continue;
                }
                candidates.Add((health, soldier, candidate, distance));
            }
        }

        // Where the round stops if nobody takes it: the surface in its way, or the end of its reach.
        Vector3d unstopped = worldHit is { } wall ? wall.Position : end;
        if (candidates.Count == 0)
        {
            PassedBy(unstopped, null);
            return false;
        }

        // Nearest first: retail's cast meets the nearest human and rolls the probability there; a
        // failed roll flies the round on to the next one behind.
        candidates.Sort(static (a, b) => a.distance.CompareTo(b.distance));

        foreach ((HumanHealth health, SoldierView struckView, HumanHit candidate, _) in candidates)
        {
            // No probability roll against a soldier: the Human's evaluate (0x45E3E0) rolls the round's
            // probability only when the struck human is player 0. Any other body is hit outright unless
            // the shooter is on its side (Human+252), and then the round passes THROUGH it and flies on.
            if (shooter is not null && struckView.Soldier.Team == shooter.Soldier.Team)
            {
                continue;
            }

            hit = candidate;
            AiSoldier? brain = _brains.TryGetValue(struckView, out AiSoldier? b) ? b : null;

            // Scripted invulnerability makes the round MEET the body but do nothing -- exactly like the
            // player's TryShoot. The round is still consumed: an invulnerable body is a physical obstacle.
            bool invulnerable = brain?.IsInvulnerable ?? false;
            if (!invulnerable)
            {
                float dealt = health.Apply(candidate.Zone, weapon.DamageFactor, brain?.IsInstantDeathDisabled ?? false);

                if (health.IsDead)
                {
                    Die(struckView, candidate, origin, end, weapon.Category, SoldierDeath.ModeFor(weapon.Category));
                }
                else
                {
                    brain?.ReceiveDamage(origin, shooter?.Soldier.Id ?? AiDetectionCandidate.PlayerId);
                    ReactToHit(struckView, brain, candidate.Zone, dealt, explosion: false);
                }
            }

            _logger.Information(
                LogCategory + ".Damage",
                $"port zone={candidate.Zone} ({HumanDamage.ZoneName(candidate.Zone)}) bone={candidate.Bone} "
                    + $"dmgFactor={weapon.DamageFactor:0.000} invuln={(invulnerable ? 1 : 0)} "
                    + $"instDeathDisabled={(brain?.IsInstantDeathDisabled ?? false ? 1 : 0)} "
                    + $"accum={health.Damage:0.000} max={health.MaximumHealth:0.000} "
                    + $"dead={(health.IsDead ? 1 : 0)}");
            PassedBy(candidate.Position, struckView);
            return true;
        }

        // Every candidate rolled a miss -- the round flew past them all and continues beyond.
        PassedBy(unstopped, null);
        return false;

        void PassedBy(Vector3d stop, SoldierView? struck)
        {
            if (shooter is not null)
            {
                NotifyRoundPassed(origin, stop, shooter, struck);
            }
        }
    }

    /// <summary>Gets the player's team, resolved once per level from the declared HumanPlayer task.</summary>
    /// <remarks>
    /// This is what the round handler compares a shooter's team against to decide whether a round may
    /// strike the player: a guard (opposing team) may, a friendly (same team) never does.
    /// </remarks>
    public int PlayerTeam => _playerTeam;

    /// <summary>Pushes every living, active soldier's body into the shared collision index.</summary>
    /// <remarks>
    /// Called once per tick after the roster has moved. A body is the analytic
    /// <see cref="RayCapsule"/> the player's wall sweep collides with; only soldiers that are alive
    /// and whose conditional container is live get one, so a dormant sentry or a corpse is no
    /// obstacle. The same <see cref="ObjectCollisionIndex"/> instance is the one the player camera
    /// uses for its walls, so a soldier presents a real wall in retail's spirit.
    /// </remarks>
    private void SyncCollisionBodies()
    {
        if (_solids is not { } solids)
        {
            return;
        }

        int count = 0;
        foreach (SoldierView view in _brains.Keys)
        {
            if (!view.IsDead && ConditionalContainerGate.IsTaskActive(view.Soldier))
            {
                count++;
            }
        }

        if (count == 0)
        {
            solids.SetHumanBodies([]);
            return;
        }

        HumanBody[] bodies = new HumanBody[count];
        int index = 0;
        foreach (SoldierView view in _brains.Keys)
        {
            if (view.IsDead || !ConditionalContainerGate.IsTaskActive(view.Soldier))
            {
                continue;
            }

            Vector3d pos = view.Position;
            double radius = 0.55 * WorldScene.UnitsPerMetre;
            bodies[index++] = new HumanBody(
                new Vector3d(pos.X, pos.Y, pos.Z),
                new Vector3d(pos.X, pos.Y, pos.Z + (1.8 * WorldScene.UnitsPerMetre)),
                radius);
        }

        solids.SetHumanBodies(bodies);
    }

    /// <summary>Every soldier where it stands at this moment, for an area counting as the level walk reaches it.</summary>
    public IEnumerable<HumanOccupant> LiveOccupants()
    {
        foreach (SoldierView view in _brains.Keys)
        {
            yield return new HumanOccupant(view.Soldier.Id, view.Soldier.Team, view.Position, view.IsDead);
        }
    }

    /// <summary>Reports every soldier's current presence for mission queries.</summary>
    /// <remarks>
    /// One occupant per placed soldier (dormant or active): its task id, team, current position and
    /// death. The host feeds this to the mission runtime on every tick so AreaActivate volumes count
    /// the friendly squad's advance and <c>HumanSoldier_N.isDead</c> tracks the guards the squad kills.
    /// </remarks>
    public IReadOnlyCollection<HumanOccupant> Occupants()
    {
        if (_brains.Count == 0)
        {
            return Array.Empty<HumanOccupant>();
        }

        HumanOccupant[] occupants = new HumanOccupant[_brains.Count];
        int index = 0;
        foreach (SoldierView view in _brains.Keys)
        {
            occupants[index++] = new HumanOccupant(
                view.Soldier.Id,
                view.Soldier.Team,
                view.Position,
                view.IsDead);
        }
        return occupants;
    }

    /// <summary>A nearest-human intersection that does not apply damage or AI events.</summary>
    public HumanRayCast Probe() => TryIntersect;

    /// <summary>A living-human proximity query for armed mines.</summary>
    public HumanProximityCast ProximityProbe() => TryProximity;

    /// <summary>Finds the nearest living soldier inside a mine's activation radius.</summary>
    public bool TryProximity(Vector3d center, float radiusUnits, out HumanProximityTarget target)
    {
        target = default;
        double radiusSquared = radiusUnits * radiusUnits;
        bool found = false;

        foreach (Group group in _groups)
        {
            foreach (Member member in group.Soldiers)
            {
                SoldierView soldier = member.View;
                if (!ConditionalContainerGate.IsTaskActive(soldier.Soldier)
                    || soldier.IsDead
                    || (_health.TryGetValue(soldier, out HumanHealth? health) && health is { IsDead: true }))
                {
                    continue;
                }

                Vector3d position = soldier.Position;
                double distanceSquared = Vector3d.DistanceSquared(center, position);
                if (distanceSquared > radiusSquared)
                {
                    continue;
                }

                double distance = Math.Sqrt(distanceSquared);
                if (!found || distance < target.Distance)
                {
                    target = new HumanProximityTarget(position, distance);
                    found = true;
                }
            }
        }

        return found;
    }

    /// <summary>Finds the nearest living soldier on a segment without scoring the hit.</summary>
    public bool TryIntersect(Vector3d origin, Vector3d end, out HumanHit hit)
    {
        hit = default;
        bool found = false;

        foreach (Group group in _groups)
        {
            foreach (Member member in group.Soldiers)
            {
                SoldierView soldier = member.View;
                if (!ConditionalContainerGate.IsTaskActive(soldier.Soldier)
                    || soldier.IsDead
                    || (_health.TryGetValue(soldier, out HumanHealth? health) && health is { IsDead: true }))
                {
                    continue;
                }

                Matrix3f m = soldier.Orientation;
                Vector3f facing = new(m.M12, m.M22, m.M32);
                double snappedZ = SnapToGround(soldier.Position.X, soldier.Position.Y, soldier.Position.Z);
                Vector3d feet = new(soldier.Position.X, soldier.Position.Y, snappedZ);
                bool met = group.CollisionMesh is { } collision
                    ? HumanHitTest.TryCastMesh(
                        origin, end, feet, facing, collision, soldier.Pose(), soldier.RootSample.Z, out HumanHit candidate)
                    : HumanHitTest.TryCast(
                        origin, end, feet, facing, soldier.Pose(), out candidate, soldier.RootSample.Z);
                if (met && (!found || candidate.Distance < hit.Distance))
                {
                    hit = candidate;
                    found = true;
                }
            }
        }

        return found;
    }

    /// <summary>Gets how many soldiers have been killed.</summary>
    public int Killed
    {
        get
        {
            int dead = 0;
            foreach (HumanHealth health in _health.Values)
            {
                if (health.IsDead)
                {
                    dead++;
                }
            }

            return dead;
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Release();

        _textureCache?.Clear();
    }

    private Group? BuildGroup(LoadedMission mission, string modelName, SoldierView posed)
    {
        if (!mission.TryGetModel(modelName, out MefModel? model) || model is null)
        {
            _logger.Warning(LogCategory, $"'{posed.Soldier.Name}' names model '{modelName}', which no pack holds.");
            return null;
        }

        if (!MefSkinner.IsSkinned(model))
        {
            _logger.Warning(LogCategory, $"'{modelName}' is {model.Format}, not a skinned model.");
            return null;
        }

        MeshGeometry? geometry = MefMeshBuilder.Build(model, 1f);
        if (geometry is null)
        {
            _logger.Warning(LogCategory, $"'{modelName}' has nothing to draw.");
            return null;
        }

        Vertex3D[] skinned = [.. geometry.Vertices];
        int written;
        try
        {
            // The unrotated pose: the facing is applied per soldier in the model matrix instead.
            written = MefSkinner.Skin(model, posed.UnrotatedPose(), (float)(1.0 / WorldScene.UnitsPerMetre), skinned);
        }
        catch (ArgumentException ex)
        {
            _logger.Warning(LogCategory, $"'{modelName}' could not be posed: {ex.Message}");
            return null;
        }

        // The lowest point of the posed skin, in metres below the skeleton root. Z rotation cannot
        // change it, so it is a property of the (model, skeleton, animation) triple, and it is what
        // Stand() rests on the ground. Only the base vertices count - the array's tail holds the
        // extra-influence records, which no draw record indexes.
        float lowest = 0f;
        for (int i = 0; i < written; i++)
        {
            lowest = Math.Min(lowest, skinned[i].Position.Z);
        }

        MeshHandle mesh;
        try
        {
            mesh = _device.CreateMesh(skinned, geometry.Indices);
        }
        catch (Exception ex) when (ex is ArgumentException or GraphicsDeviceException)
        {
            _logger.Warning(LogCategory, $"'{modelName}' did not upload: {ex.Message}");
            return null;
        }

        (LoadedPack? pack, MtpInstance? instance) = FindInstance(mission, modelName);
        return new Group(geometry, mesh, instance, pack, lowest) { Model = model };
    }

    private static (LoadedPack? Pack, MtpInstance? Instance) FindInstance(LoadedMission mission, string name)
    {
        foreach (LoadedPack pack in new[] { mission.LevelPack, mission.CommonPack })
        {
            foreach (MtpInstance instance in pack.Instances)
            {
                if (string.Equals(instance.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    return (pack, instance);
                }
            }
        }

        return (null, null);
    }

    private void Release()
    {
        foreach (Group group in _groups)
        {
            if (group.Mesh.IsValid)
            {
                _device.DestroyMesh(group.Mesh);
            }

            // Each soldier owns a mesh of its own now, and every one of them has to go back.
            foreach (Member member in group.Soldiers)
            {
                if (member.Mesh.IsValid)
                {
                    _device.DestroyMesh(member.Mesh);
                }
            }
        }

        foreach (HeldWeapon weapon in _weapons)
        {
            weapon.Presentation?.Dispose();
            if (weapon.Mesh.IsValid)
            {
                _device.DestroyMesh(weapon.Mesh);
            }
        }

        _weapons.Clear();
        _heldWeaponsByModel.Clear();
        _vehicleWeaponVisuals.Clear();
        _vehicleGuns = null;
        _groups.Clear();
        _health.Clear();
        _detectorMarkerScaleMetres.Clear();
        _binocularDetected.Clear();
        _binocularOverlay.Clear();
        _binocularVisibility.Clear();
        _binocularVisibilityTick = int.MinValue;
        _cast = null;
        _sight = null;
        _ragdollRigs.Clear();
        _ragdollWorld = null;
        ReleasePlayerBody();
        Count = 0;
    }

    private sealed class Group(MeshGeometry geometry, MeshHandle mesh, MtpInstance? instance, LoadedPack? pack, float lowestVertexZ)
    {
        public MeshGeometry Geometry { get; } = geometry;

        public MeshHandle Mesh { get; set; } = mesh;

        public MtpInstance? Instance { get; } = instance;

        public LoadedPack? Pack { get; } = pack;

        public float LowestVertexZ { get; } = lowestVertexZ;

        public List<Member> Soldiers { get; } = [];

        /// <summary>The model every soldier in the group draws, kept so the group can be re-skinned each tick.</summary>
        public MefModel? Model { get; set; }

        private MefCollisionMesh? _collisionMesh;
        private bool _collisionMeshRead;

        /// <summary>The model's skinned collision mesh, read once — the geometry a round is scored against.</summary>
        public MefCollisionMesh? CollisionMesh
        {
            get
            {
                if (!_collisionMeshRead)
                {
                    _collisionMeshRead = true;
                    if (Model is not null && MefCollisionMesh.TryRead(Model.Container, out MefCollisionMesh? read))
                    {
                        _collisionMesh = read;
                    }
                }

                return _collisionMesh;
            }
        }

        /// <summary>Whether the one-time collision-chunk read has already happened.</summary>
        public bool CollisionMeshPrepared => _collisionMeshRead;
    }

    /// <summary>
    /// One soldier drawn from the group's geometry, with <b>its own skinned mesh</b>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The group used to share a single skinned mesh, posed from its first member. That was only ever
    /// defensible while every soldier in a group ran the same clip from the same start; once they were given
    /// real patrol paths they diverged, and the whole group rendered whichever pose the representative
    /// happened to be in. With most guards standing and a few walking, that read as <b>no soldier
    /// animating at all</b>.
    /// </para>
    /// <para>
    /// Hooking the original's skinner (<c>0x49F700</c>) settles what it does: it is called through a
    /// stack-allocated descriptor about once per soldier per frame, so the original skins <b>every soldier
    /// separately, every frame</b>. This does the same. It costs one mesh upload per soldier per tick
    /// instead of one per group, which is the price of each soldier having its own pose.
    /// </para>
    /// </remarks>
    private sealed class Member(SoldierView view, Vector3d position, MeshHandle mesh)
    {
        public SoldierView View { get; } = view;

        /// <summary>Whether the last frame's main view drew this soldier -- retail's cue to re-skin it.</summary>
        public bool InView { get; set; }

        /// <summary>The eye orientation from the last frame that drew this soldier, or none yet.</summary>
        public Matrix3f? EyeOrientation { get; set; }

        public Vector3d Position { get; } = position;

        /// <summary>Whether the authored origin is cinematic staging rather than a gameplay spawn.</summary>
        public bool IsCutsceneActor { get; init; }

        /// <summary>Whether an authored one-shot has moved this staged Human from its spawn.</summary>
        public bool HasCutsceneRootMotion { get; set; }

        /// <summary>Whether an authored WalkTo/RunTo route has handed placement to the Human mover.</summary>
        public bool HasCutsceneGameplayMovement { get; set; }

        public MeshHandle Mesh { get; set; } = mesh;

        /// <summary>What this soldier holds, or <see langword="null"/> when the level armed it with nothing.</summary>
        public HeldWeapon? Weapon { get; set; }

        /// <summary>The carried weapon restored after a temporary stationary-gun mount ends.</summary>
        public HeldWeapon? CarriedWeapon { get; set; }

        /// <summary>The carried weapon table row restored after a temporary stationary-gun mount ends.</summary>
        public WeaponType? CarriedWeaponType { get; set; }

        /// <summary>The stationary gun whose model currently replaces the carried weapon.</summary>
        public AiStationaryGun? RenderedMountedGun { get; set; }

        /// <summary>Whether a round left this soldier's barrel on the current simulation tick.</summary>
        public bool FiredThisTick { get; set; }

        /// <summary>Monotonic round serial used to vary the retail gun-flame attachment.</summary>
        public int ShotSequence { get; set; }

        /// <summary>The weapon's table row, which the AI reads its clip size and burst length from.</summary>
        public WeaponType? WeaponType { get; set; }

        /// <summary>How many rounds are in the clip. The AI watches this change to count its burst.</summary>
        public int AmmoInClip { get; set; }

        /// <summary>Ticks left before the weapon's cadence lets another round out.</summary>
        public int FireCooldown { get; set; }

        /// <summary>Rounds fired since the trigger went down — the gun's <c>+320</c> that picks the spread.</summary>
        public int RoundsThisPull { get; set; }

        /// <summary>Ticks left of a reload.</summary>
        public int ReloadTicks { get; set; }

        /// <summary>The persistent buffer this soldier's pose is skinned into, allocated once.</summary>
        public Vertex3D[]? SkinScratch { get; set; }

        /// <summary>
        /// The pose this soldier was skinned from this tick — its rig's, or its corpse's.
        /// </summary>
        /// <remarks>
        /// Kept because <b>anything attached to the body has to be placed from the same pose the body was
        /// drawn from</b>, and for a corpse that is not the pose its rig is playing. Reading the rig
        /// directly is what left a dead guard's rifle hanging in the air where the death animation would
        /// have put his hand.
        /// </remarks>
        public BonePose? DrawnPose { get; set; }

        /// <summary>The pose in the animated mesh's current GPU vertex stream.</summary>
        /// <remarks>
        /// This is separate from <see cref="DrawnPose"/> because a stopped pose may not upload again;
        /// player collision needs the exact pair that is still resident on the device.
        /// </remarks>
        public BonePose? SkinnedPose { get; set; }

        /// <summary>The pose in the animated mesh's previous GPU vertex stream.</summary>
        public BonePose? PreviousSkinnedPose { get; set; }

        /// <summary>Tracks whether this pose still needs an animated-buffer upload.</summary>
        public PoseUploadLatch PoseUploads { get; } = new();

        /// <summary>
        /// The simulated corpse this soldier is drawn as once it has been killed, or
        /// <see langword="null"/> while it is alive or when <c>g_ragdoll</c> is off.
        /// </summary>
        /// <remarks>
        /// Presentation only. The soldier's brain still runs its retail death — same tick, same
        /// authoritative position, same consequences for the guards around it — and this replaces
        /// nothing but the pose that reaches the skinner.
        /// </remarks>
        public RagdollBody? Ragdoll { get; set; }

        /// <summary>Whether the animated mesh took a new pose since the last frame's bookkeeping.</summary>
        /// <remarks>
        /// When it did, the previous-pose stream now holds what was on screen last frame, and the
        /// blend that described last frame is expressed against the OLD stream pair — see
        /// <see cref="ResolveFrameState"/> for how each case is reported.
        /// </remarks>
        public bool PoseFlippedSinceFrame { get; set; }

        // ---- Captured on the simulation tick: the two states the presentation draws between. ----

        /// <summary>Whether the tick states below have been captured at least once.</summary>
        public bool HasTickState { get; set; }

        /// <summary>Where the body is drawn for the current tick, in metres.</summary>
        public Vector3d TickWhere { get; set; }

        /// <summary>Where it was drawn the tick before.</summary>
        public Vector3d PrevTickWhere { get; set; }

        /// <summary>This tick's world 3×3, verbatim — what an unblended frame draws, to the bit.</summary>
        public Matrix3f TickOrientation { get; set; }

        /// <summary>This tick's heading in radians, for interpolating.</summary>
        public float TickFacing { get; set; }

        /// <summary>The tick before's heading.</summary>
        public float PrevTickFacing { get; set; }

        /// <summary>Whether the hand-bone states below are captured (only armed soldiers need them).</summary>
        public bool HasHandState { get; set; }

        /// <summary>The hand bone's position in body space this tick, in world units.</summary>
        public Vector3f TickHandPosition { get; set; }

        /// <summary>The tick before's.</summary>
        public Vector3f PrevTickHandPosition { get; set; }

        /// <summary>The hand bone's rotation this tick.</summary>
        public Matrix3f TickHandRotation { get; set; } = Matrix3f.Identity;

        /// <summary>The tick before's.</summary>
        public Matrix3f PrevTickHandRotation { get; set; } = Matrix3f.Identity;

        // ---- Resolved once per rendered frame by ResolveFrameState; every pass draws from these. ----

        /// <summary>Whether a frame has been resolved at all.</summary>
        public bool HasFrameState { get; set; }

        /// <summary>Where the body is drawn this frame — between the two tick states when smoothing.</summary>
        public Vector3d DrawnWhere { get; set; }

        /// <summary>Its world 3×3 this frame.</summary>
        public Matrix3f DrawnOrientation { get; set; } = Matrix3f.Identity;

        /// <summary>Where it was drawn LAST frame — the rigid half of its motion vector.</summary>
        public (Vector3d Where, Matrix3f Orientation)? PreviousDrawn { get; set; }

        /// <summary>This frame's share of the previous-pose vertex stream in the drawn position.</summary>
        public float PoseBlend { get; set; }

        /// <summary>The share that reconstructs LAST frame's position from the same streams.</summary>
        public float PreviousPoseBlend { get; set; }

        /// <summary>Whether this frame is the body's first — nothing on screen is its history.</summary>
        public bool NoHistoryThisFrame { get; set; }

        /// <summary>Whether the weapon transform below is resolved.</summary>
        public bool HasWeaponFrameState { get; set; }

        /// <summary>The held weapon's world origin this frame, in metres.</summary>
        public Vector3d DrawnWeaponOrigin { get; set; }

        /// <summary>Its world rotation this frame — heading times hand bone.</summary>
        public Matrix3f DrawnWeaponRotation { get; set; } = Matrix3f.Identity;

        /// <summary>Last frame's weapon transform — a rigid model's exact previous pose.</summary>
        public (Vector3d Origin, Matrix3f Rotation)? PreviousWeapon { get; set; }
    }

    /// <summary>
    /// One weapon model, uploaded once and drawn by every soldier carrying that weapon.
    /// </summary>
    /// <remarks>
    /// Unlike a body, a gun is a rigid model: it is not skinned and its pose never changes, so a single
    /// mesh serves every soldier and only the transform differs. That transform is the hand bone's, which
    /// is why the body mesh has to be posed but this one does not.
    /// </remarks>
    private sealed class HeldWeapon(
        MeshGeometry geometry,
        MeshHandle mesh,
        MtpInstance? instance,
        LoadedPack? pack,
        IHeldWeaponPresenter? presentation)
    {
        public MeshGeometry Geometry { get; } = geometry;

        public MeshHandle Mesh { get; } = mesh;

        public MtpInstance? Instance { get; } = instance;

        public LoadedPack? Pack { get; } = pack;

        /// <summary>The multipart retail object, including its transient TASKTYPE_GUNFLAME child.</summary>
        public IHeldWeaponPresenter? Presentation { get; } = presentation;
    }

    // A model texture just uploaded gets the detail map derived from it, keyed by its name and its override's stamp.
    private void AttachDetail(string name, TextureHandle handle, TextureImage image) =>
        DetailMaps?.Attach(
            MaterialDetailPack.KeyFor(name, _resources.OverrideStamp(LoadedPack.TextureResourceName(name))),
            handle,
            image.GetLevel(0),
            image.Width,
            image.Height);

    // The textures the models name, loaded on first use.
    private TexTextureCache Textures => _textureCache ??= new(_device, _resources, _logger, LogCategory) { Loaded = AttachDetail };
}
