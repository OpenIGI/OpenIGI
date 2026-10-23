using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;
using OpenIGI.Formats.Animation;
using OpenIGI.Formats.World;
using OpenIGI.Game.Ai;
using OpenIGI.Game.Igi1.Physics;
using OpenIGI.Game.Igi1.Player;
using OpenIGI.Game.Igi1.Weapons;
using OpenIGI.Game.Igi1.World;
using OpenIGI.Game.Physics;
using OpenIGI.Game.Player;
using OpenIGI.Game.Weapons;
using OpenIGI.Game.World;

namespace OpenIGI.Game.Igi1.Ai;

/// <content>
/// How the soldiers are wired to the level and the game around them: the services they cast against, the sinks their
/// sounds, deaths and rounds go to, and the face the rest of the game reads them through -- each of which hands on to
/// the part that owns it.
/// </content>
public sealed partial class SoldierSimulation
{
    /// <summary>How far above the declared position the ground probe starts — 0.5 m.</summary>
    internal const double ProbeAboveInUnits = SoldierContext.ProbeAboveInUnits;

    /// <summary>How high above a soldier's feet its broad-phase sphere is centred, in world units.</summary>
    internal const double PickChestUnits = SoldierBodyQueries.PickChestUnits;

    /// <summary>The broad-phase cull radius, in world units — generous (a soldier plus its outstretched arms).</summary>
    internal const double PickCullRadiusUnits = SoldierBodyQueries.PickCullRadiusUnits;

    /// <inheritdoc cref="SoldierSightQueries.RestEyeOrientation"/>
    internal static readonly Matrix3f RestEyeOrientation = SoldierSightQueries.RestEyeOrientation;

    internal Action<string, Vector3d, bool>? ScriptSound;

    internal Action<int, bool, WeaponType?, Vector3d>? WeaponSound;

    internal Action<Vector3d, float>? MeleeStrikeSink;

    internal AnimTriggerRegistry? AnimationTriggers;

    internal Func<float>? PlayerDeltaTranslationScale;

    internal GrenadeSimulation? Grenades;

    // The level's doors: ticked here (their update runs on the task pass, before the AIs), queried by
    // each walker's door hop, and switched out of the collision index while they stand open.
    internal DoorRegistry? Doors;

    internal AiAlarmControlRegistry? AlarmControls;

    internal GunPickupRegistry? DroppedWeaponPickups;

    internal ScriptVarRegistry? ScriptVariables;

    private ElevatorRegistry? _elevators;

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

    /// <summary>Diagnostics: told of every carried or mounted round as it is spread -- the shooter, its rounds this pull, the base forward.</summary>
    public static Action<Igi1HumanAi, int, Vector3f>? RoundSpreadTraced { get; set; }

    /// <summary>Diagnostics: told of every gun a dying soldier drops -- its id, the drop point, its feet and root height.</summary>
    public static Action<int, Vector3d, Vector3d, float>? WeaponDropTraced { get; set; }

    /// <summary>Diagnostics: told of every palette rebuild a cast makes -- the soldier, what cast, and its segment.</summary>
    public static Action<int, string, Vector3d, Vector3d>? PaletteRebuildTraced { get; set; }

    /// <summary>Diagnostics: the soldier whose update ran last, for attributing draws.</summary>
    public static int TickingSoldierId { get; set; } = -1;

    /// <summary>Diagnostics: told of every round a soldier takes -- its id, zone, bone, factor, the damage after it and the maximum.</summary>
    public static Action<int, int, int, float, float, float>? DamageTraced { get; set; }

    /// <summary>Gets whether any authored alarm control is sounding on the current simulation tick.</summary>
    /// <remarks>Used by placed security cameras, whose alarm loop follows the mission-wide alarm state.</remarks>
    public bool AnyAlarmOn => AlarmControls?.AnyOn ?? false;

    /// <summary>Gets whether one alarm control's alarm is sounding this tick.</summary>
    /// <param name="controlId">The control's task id.</param>
    /// <returns>Whether it is on; an id that names no control is off.</returns>
    public bool IsAlarmOn(int controlId) => AlarmControls?.IsOn(controlId) ?? false;

    /// <summary>Gets or sets the active authored cutscene transform source.</summary>
    public CutsceneRuntime? Cutscenes { get => _context.Cutscenes; set => _context.Cutscenes = value; }

    /// <summary>The game material the level's terrain is made of, or null to fall back to <c>Ground</c>.</summary>
    /// <remarks>Guards walk on the same ground the player does; see the player camera's copy.</remarks>
    public int? TerrainGameMaterialIndex
    {
        get => _footsteps.TerrainGameMaterialIndex;
        set => _footsteps.TerrainGameMaterialIndex = value;
    }

    /// <summary>Gets the player's team, resolved once per level from the declared HumanPlayer task.</summary>
    /// <remarks>
    /// This is what the round handler compares a shooter's team against to decide whether a round may
    /// strike the player: a guard (opposing team) may, a friendly (same team) never does.
    /// </remarks>
    public int PlayerTeam => _context.PlayerTeam;

    /// <summary>Gets the level's player task, which the player's rounds name as their shooter; <see langword="null"/> for none.</summary>
    public HumanPlayerTask? PlayerTask => _context.PlayerTask;

    /// <inheritdoc cref="SoldierNoiseBroadcaster.Joined"/>
    public LevelJoiners? Joined
    {
        get => _noise.Joined;
        set
        {
            _noise.Joined = value;
            _binoculars.Joined = value;
        }
    }

    /// <summary>Gets where soldiers' rounds go, so another AI gun (a vehicle's) can fire through the same cast.</summary>
    public SoldierRound? RoundHandler => _fire.OnRound;

    /// <summary>Gets how many rounds the level's soldiers have fired.</summary>
    public int RoundsFired { get => _fire.RoundsFired; internal set => _fire.RoundsFired = value; }

    /// <inheritdoc cref="SoldierGunShotRounds.MarchesRounds"/>
    public bool MarchesRounds => _rounds.MarchesRounds;

    /// <inheritdoc cref="SoldierGunShotRounds.GunShotTracers"/>
    public IEnumerable<GunShotTracer> GunShotTracers => _rounds.GunShotTracers;

    /// <inheritdoc cref="SoldierDamage.BloodSpray"/>
    public Action<object, Vector3d, Vector3f>? BloodSpray { get => _damage.BloodSpray; set => _damage.BloodSpray = value; }

    /// <inheritdoc cref="SoldierBodyQueries.Killed"/>
    public int Killed => _bodies.Killed;

    /// <summary>Gets whether the player's third-person body is being shown climbing a ladder.</summary>
    public bool IsPlayerBodyClimbing => _playerBody.Climbing;

    /// <summary>Gets whether the third-person view is currently placing and animating the body.</summary>
    public bool IsPlayerBodyFollowing => _playerBody.Following;

    /// <summary>Gets the soldiers as the simulation walks them: each drawn group's roster, in the groups' order.</summary>
    internal List<SoldierModelGroup> Roster => _context.Roster;

    /// <summary>Gets each soldier's damage state.</summary>
    internal Dictionary<SoldierView, HumanHealth> Health => _context.Health;

    /// <inheritdoc cref="SoldierContext.DetectorMarkerScaleMetres"/>
    internal Dictionary<SoldierView, float> DetectorMarkerScaleMetres => _context.DetectorMarkerScaleMetres;

    /// <summary>Gets each soldier's brain.</summary>
    internal Dictionary<SoldierView, Igi1HumanAi> Brains => _context.Brains;

    /// <summary>Gets each brain by its runtime id.</summary>
    internal Dictionary<int, Igi1HumanAi> BrainsByRuntimeId => _context.BrainsByRuntimeId;

    /// <summary>Gets the stream the soldiers draw from -- the level's one global stream.</summary>
    internal IgiRandom AiRandom => _context.AiRandom;

    /// <summary>Gets or sets the simulation's tick count.</summary>
    internal int TickCount { get => _context.TickCount; set => _context.TickCount = value; }

    /// <summary>Gets or sets the level's terrain.</summary>
    internal IGroundHeightField? Terrain { get => _context.Terrain; set => _context.Terrain = value; }

    /// <summary>Gets or sets the cast against placed collision a body stands on and a round meets.</summary>
    internal WorldRayCast? Cast { get => _context.Cast; set => _context.Cast = value; }

    /// <summary>Gets or sets the cast a sight line is tested with.</summary>
    internal WorldRayCast? Sight { get => _context.Sight; set => _context.Sight = value; }

    /// <inheritdoc cref="SoldierBrainBinder.Interactions"/>
    internal WorldInteractionRegistry? Interactions { get => _brains.Interactions; set => _brains.Interactions = value; }

    /// <inheritdoc cref="SoldierDamage.BodyFell"/>
    internal Action<SoldierFall>? BodyFell { get => _damage.BodyFell; set => _damage.BodyFell = value; }

    /// <inheritdoc cref="GuardGeneratorSpawner.SpawnedBody"/>
    internal Func<GuardGeneratorTask, SoldierModelGroup, SoldierAgent, SoldierAgent, bool>? SpawnedBody
    {
        get => _generators.SpawnedBody;
        set => _generators.SpawnedBody = value;
    }

    /// <inheritdoc cref="GuardGeneratorSpawner.BodyRemoved"/>
    internal Action<SoldierAgent>? BodyRemoved { get => _generators.BodyRemoved; set => _generators.BodyRemoved = value; }

    /// <inheritdoc cref="SoldierNoiseBroadcaster.Humans"/>
    internal List<IHumanAiEventTarget> Humans => _noise.Humans;

    /// <inheritdoc cref="SoldierBinocularScan.Overlay"/>
    internal EnemyDetectorOverlay BinocularOverlay => _binoculars.Overlay;

    /// <inheritdoc cref="SoldierBinocularScan.Detected"/>
    internal Dictionary<SoldierView, BinocularMarkerColor> BinocularDetected => _binoculars.Detected;

    /// <summary>Gets the joined humans the last binocular scan marked, and their marker colours.</summary>
    internal Dictionary<IAiDetectable, BinocularMarkerColor> BinocularJoinedDetected => _binoculars.JoinedDetected;

    /// <inheritdoc cref="SoldierBinocularScan.Visibility"/>
    internal Dictionary<SoldierView, bool> BinocularVisibility => _binoculars.Visibility;

    /// <inheritdoc cref="SoldierBinocularScan.VisibilityTick"/>
    internal int BinocularVisibilityTick { get => _binoculars.VisibilityTick; set => _binoculars.VisibilityTick = value; }

    /// <inheritdoc cref="SoldierRagdolls.RagdollRigs"/>
    internal Dictionary<BoneAnimationSet, RagdollSkeleton> RagdollRigs => _ragdolls.RagdollRigs;

    /// <inheritdoc cref="SoldierRagdolls.RagdollWorld"/>
    internal RagdollWorld? RagdollWorld { get => _ragdolls.RagdollWorld; set => _ragdolls.RagdollWorld = value; }

    /// <inheritdoc cref="SoldierPlayerBody.View"/>
    internal SoldierView? PlayerBodyView { get => _playerBody.View; set => _playerBody.View = value; }

    /// <inheritdoc cref="SoldierPlayerBody.Model"/>
    internal SoldierBodyModel? PlayerBodyModel { get => _playerBody.Model; set => _playerBody.Model = value; }

    /// <inheritdoc cref="SoldierPlayerBody.Climbing"/>
    internal bool PlayerBodyClimbing { get => _playerBody.Climbing; set => _playerBody.Climbing = value; }

    /// <inheritdoc cref="SoldierPlayerBody.Shown"/>
    internal bool PlayerBodyShown { get => _playerBody.Shown; set => _playerBody.Shown = value; }

    /// <inheritdoc cref="SoldierPlayerBody.Following"/>
    internal bool PlayerBodyFollowing { get => _playerBody.Following; set => _playerBody.Following = value; }

    /// <inheritdoc cref="SoldierPlayerBody.UpperBodySequence"/>
    internal int PlayerBodyUpperBodySequence
    {
        get => _playerBody.UpperBodySequence;
        set => _playerBody.UpperBodySequence = value;
    }

    /// <inheritdoc cref="SoldierPlayerBody.SuppressRootHeight"/>
    internal bool PlayerBodySuppressRootHeight
    {
        get => _playerBody.SuppressRootHeight;
        set => _playerBody.SuppressRootHeight = value;
    }

    /// <inheritdoc cref="SoldierPlayerBody.VisualLift"/>
    internal double PlayerBodyVisualLift { get => _playerBody.VisualLift; set => _playerBody.VisualLift = value; }

    /// <inheritdoc cref="SoldierPlayerBody.RootHeightAtStart"/>
    internal double PlayerBodyRootHeightAtStart
    {
        get => _playerBody.RootHeightAtStart;
        set => _playerBody.RootHeightAtStart = value;
    }

    /// <summary>Gets the live AI brains for focused renderer-path tests.</summary>
    internal IReadOnlyList<Igi1HumanAi> BrainsForTesting => [.. Brains.Values];

    internal IReadOnlyList<KeyValuePair<SoldierView, Igi1HumanAi>> BrainPairsForTesting => [.. Brains];

    /// <summary>Gets each soldier's gun-shot task, by the soldier that fires it, for the scenario runs that trace tracers.</summary>
    internal IReadOnlyDictionary<SoldierView, GunShotTask> GunShotsForTesting => _rounds.GunShots;

    /// <summary>Sets the stream the soldiers draw from -- the level's one global stream -- before they are animated.</summary>
    /// <param name="random">The stream.</param>
    public void UseRandom(IgiRandom random) => _context.AiRandom = random ?? throw new ArgumentNullException(nameof(random));

    /// <summary>Gives the renderer the level's doors and the index their collision is switched in.</summary>
    /// <param name="doors">The door registry, or <see langword="null"/> for a level with none.</param>
    /// <param name="solids">The placed-object collision index.</param>
    public void SetDoors(DoorRegistry? doors, ObjectCollisionIndex? solids)
    {
        Doors = doors;
        _context.Solids = solids;

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
    public void ActivateTask(int taskId) => _brains.ActivateTask(taskId);

    /// <inheritdoc cref="SoldierBrainBinder.ActivateAsGuard"/>
    public void ActivateAsGuard(int taskId) => _brains.ActivateAsGuard(taskId);

    /// <summary>Connects an AI's input-channel activation to the live world interaction task.</summary>
    public void SetAiTaskActivator(Action<int>? activator) => _brains.TaskActivator = activator;

    /// <summary>Connects gunner AIs to the level's live StationaryGun tasks.</summary>
    public void UseInteractions(WorldInteractionRegistry? interactions) => Interactions = interactions;

    /// <summary>Sounds a soldier's death cry, at the position the body fell from.</summary>
    /// <param name="report">The sink, or <see langword="null"/> to make deaths silent.</param>
    /// <remarks>
    /// A callback rather than a <c>SoundSystem</c> reference, for the reason every other seam on this
    /// class is one: the renderer knows when a soldier dies and should not also know how a sound is
    /// played. The argument is the body's position in <b>engine units</b>.
    /// </remarks>
    public void SetDeathSound(Action<Vector3d>? report) => _damage.DeathSound = report;

    /// <summary>Publishes a gun when a dead soldier's weapon becomes a world pickup.</summary>
    public void SetWeaponDropSink(Action<WeaponType, Vector3d, Matrix3f, Vector3d>? report) => _damage.WeaponDrop = report;

    /// <inheritdoc cref="SoldierRagdolls.SetDroppedWeaponWorld"/>
    public void SetDroppedWeaponWorld(CollisionTriangleGather? triangles, WorldRayCast? trace) =>
        _ragdolls.SetDroppedWeaponWorld(triangles, trace);

    /// <summary>Gives dropped weapons a collision world after they leave a dead soldier.</summary>
    public void UseDroppedWeaponPickups(GunPickupRegistry? pickups) => DroppedWeaponPickups = pickups;

    /// <summary>Sounds a soldier's footstep, already resolved against the ground it landed on.</summary>
    /// <param name="report">The sink, or <see langword="null"/> to make soldiers walk silently.</param>
    public void SetFootstepSound(Action<Footstep>? report) => _footsteps.FootstepSound = report;

    /// <summary>Gives the renderer the material table a footstep picks its sound from.</summary>
    /// <param name="materials">The level's materials, or <see langword="null"/> for none.</param>
    public void UseMaterials(GameMaterialRegistry? materials) => _footsteps.Materials = materials;

    /// <summary>Sounds a soldier's grunt at being hit, rate-limited the way the original is.</summary>
    /// <param name="report">The sink, or <see langword="null"/> to make hits silent.</param>
    /// <remarks>The argument is the body's position in engine units.</remarks>
    public void SetHitSound(Action<string, int, Vector3d>? report) => _damage.HitSound = report;

    /// <summary>Reports a close-combat strike against the player, already probability-gated.</summary>
    /// <param name="report">The sink, or <see langword="null"/> to drop strikes.</param>
    /// <remarks>
    /// Arguments are the attacker's position in engine units and the row's damage factor. The
    /// mission owns the player's health, so it applies the hit; the renderer only reports it.
    /// Strikes against non-player targets are consumed and discarded at the tick site: NPC-on-NPC
    /// melee has no victim pipeline yet (issue #235 follow-up).
    /// </remarks>
    public void SetMeleeStrikeSink(Action<Vector3d, float>? report) => MeleeStrikeSink = report;

    /// <summary>Supplies the authored animation triggers from <c>animtrigger/animtrigger.qvm</c>.</summary>
    public void SetAnimationTriggers(AnimTriggerRegistry? triggers) => AnimationTriggers = triggers;

    /// <summary>Plays a definition requested by <c>AIAction_PlaySound</c>.</summary>
    public void SetScriptSound(Action<string, Vector3d, bool>? report) => ScriptSound = report;

    /// <summary>Reports every AI weapon tick, including quiet ticks needed to end automatic loops.</summary>
    public void SetWeaponSound(Action<int, bool, WeaponType?, Vector3d>? report) => WeaponSound = report;

    /// <summary>Sets how long a struck soldier stays quiet for, in ticks.</summary>
    /// <param name="cooldown">Draws the gap; see <c>HumanVariantSound.HitCooldownTicks</c>.</param>
    public void SetHitCooldown(Func<int>? cooldown) => _damage.HitCooldown = cooldown;

    /// <summary>Sets where the player's delta-translation scale is read from — <c>humanplayer.qvm</c> argument 0.</summary>
    /// <param name="scale">Reads the scale; <see langword="null"/> uses the retail default.</param>
    /// <remarks>Ekk moves by it too: see <see cref="HumanSoldierClass.ScalesEkkRootMotion"/>.</remarks>
    public void SetPlayerDeltaTranslationScale(Func<float>? scale) => PlayerDeltaTranslationScale = scale;

    /// <summary>Gives the soldiers the grenade simulation their throws land in.</summary>
    /// <param name="grenades">The shared pool — the same one the player's own throws fly in.</param>
    public void SetGrenades(GrenadeSimulation? grenades) => Grenades = grenades;

    /// <summary>Sets where a soldier's rounds go.</summary>
    /// <param name="handler">The handler, or <see langword="null"/> to let rounds vanish.</param>
    /// <remarks>
    /// The soldier raises the trigger; this is where the round becomes a cast. Keeping the two apart is
    /// the original's own split — the AI writes an input slot and the body's weapon path does the rest.
    /// </remarks>
    public void SetRoundHandler(SoldierRound? handler) => _fire.OnRound = handler;

    /// <summary>Marches the soldiers' rounds through a world, as their guns' <c>GunShot</c> tasks do.</summary>
    /// <param name="world">The world the rounds are cast through.</param>
    /// <param name="sounds">The impact sound windows every gun shares.</param>
    /// <param name="afterMarch">Runs once every task has marched: the chips the impacts placed detonate.</param>
    /// <remarks>
    /// With a world set, a round leaves the gun in the soldier's update but is cast only when the tasks tick,
    /// after the level's walk (<see cref="FinishTick"/>); the round handler is then told of it for its streak alone.
    /// </remarks>
    public void SetGunShotWorld(IGunShotWorld? world, GunShotSoundWindows? sounds, Action? afterMarch) =>
        _rounds.SetGunShotWorld(world, sounds, afterMarch);

    /// <inheritdoc cref="SoldierBodyQueries.Snapshot"/>
    public void Snapshot(List<SoldierSnapshot> into) => _bodies.Snapshot(into);

    /// <inheritdoc cref="SoldierBinocularScan.BinocularTargets"/>
    public IReadOnlyList<EnemyOverlayBox> BinocularTargets(
        Camera3D camera,
        float horizontalTangent,
        int screenWidth,
        int screenHeight,
        int tick,
        float maxDetectionMetres,
        int simulationTick = int.MinValue) =>
        _binoculars.BinocularTargets(camera, horizontalTangent, screenWidth, screenHeight, tick, maxDetectionMetres, simulationTick);

    /// <inheritdoc cref="SoldierSightQueries.HasLineOfSight(WorldTraceCast, Vector3d, Vector3d, double, GameTask)"/>
    public static bool HasLineOfSight(
        WorldTraceCast trace,
        Vector3d from,
        Vector3d to,
        double objectTolerance,
        GameTask? ignoredOwner = null) =>
        SoldierSightQueries.HasLineOfSight(trace, from, to, objectTolerance, ignoredOwner);

    /// <inheritdoc cref="SoldierBodyQueries.MapContacts"/>
    public IReadOnlyList<Vector3d> MapContacts() => _bodies.MapContacts();

    /// <inheritdoc cref="SoldierBodyQueries.CameraSubjects"/>
    public IReadOnlyList<SecurityCameraSubject> CameraSubjects() => _bodies.CameraSubjects();

    /// <inheritdoc cref="SoldierCutsceneStaging.CutscenePose"/>
    public CutsceneTargetPose? CutscenePose(int taskId) => _staging.CutscenePose(taskId);

    /// <inheritdoc cref="SoldierBodyQueries.TryDetectionInfo"/>
    public bool TryDetectionInfo(
        Vector3d originUnits,
        Vector3d direction,
        double reachUnits,
        Vector3d enemyEyeUnits,
        out string info) =>
        _bodies.TryDetectionInfo(originUnits, direction, reachUnits, enemyEyeUnits, out info);

    /// <inheritdoc cref="SoldierNoiseBroadcaster.NotifyGunshot(Vector3d, bool, SoldierView)"/>
    public void NotifyGunshot(Vector3d origin, bool silenced, SoldierView? except = null) =>
        _noise.NotifyGunshot(origin, silenced, except);

    /// <inheritdoc cref="SoldierNoiseBroadcaster.NotifyGunshot(Vector3d, double, SoldierView, Vector3f)"/>
    public void NotifyGunshot(Vector3d origin, double range, SoldierView? except = null, Vector3f line = default) =>
        _noise.NotifyGunshot(origin, range, except, line);

    /// <inheritdoc cref="SoldierNoiseBroadcaster.NotifyExplosion"/>
    public void NotifyExplosion(Vector3d origin, double rangeUnits = Igi1HumanAi.FlashbangEventRadiusInUnits) =>
        _noise.NotifyExplosion(origin, rangeUnits);

    /// <inheritdoc cref="SoldierNoiseBroadcaster.NotifyGroundImpact"/>
    public void NotifyGroundImpact(
        Vector3d impact,
        Vector3d shooterPosition,
        double radiusInUnits = Igi1HumanAi.GroundImpactRadiusInUnits,
        Vector3f flight = default) =>
        _noise.NotifyGroundImpact(impact, shooterPosition, radiusInUnits, flight);

    /// <inheritdoc cref="SoldierNoiseBroadcaster.NotifyWalk"/>
    public void NotifyWalk(Vector3d step, Vector3d walkerPosition, double radiusUnits) =>
        _noise.NotifyWalk(step, walkerPosition, radiusUnits);

    /// <inheritdoc cref="SoldierNoiseBroadcaster.NotifyHumanNoise"/>
    public void NotifyHumanNoise(
        AiEvent type,
        Vector3d position,
        Vector3d source,
        double radiusUnits = Igi1HumanAi.HumanNoiseRadiusInUnits) =>
        _noise.NotifyHumanNoise(type, position, source, radiusUnits);

    /// <inheritdoc cref="SoldierNoiseBroadcaster.NotifyGrenadeThrown"/>
    public void NotifyGrenadeThrown(Vector3d origin) => _noise.NotifyGrenadeThrown(origin);

    /// <inheritdoc cref="SoldierNoiseBroadcaster.NotifyGrenadeLand"/>
    public void NotifyGrenadeLand(Vector3d origin, double radiusUnits = Igi1HumanAi.GrenadeLandRadiusInUnits) =>
        _noise.NotifyGrenadeLand(origin, radiusUnits);

    /// <inheritdoc cref="SoldierNoiseBroadcaster.NotifyFlashbang"/>
    public void NotifyFlashbang(Vector3d origin, double radiusUnits = Igi1HumanAi.FlashbangEventRadiusInUnits) =>
        _noise.NotifyFlashbang(origin, radiusUnits);

    /// <inheritdoc cref="SoldierNoiseBroadcaster.NotifyRoundPassed"/>
    public void NotifyRoundPassed(Vector3d origin, Vector3d liveEnd, SoldierView shooter, SoldierView? struck) =>
        _noise.NotifyRoundPassed(origin, liveEnd, shooter, struck);

    /// <inheritdoc cref="SoldierGunShotRounds.TryCastRoundAtSoldiers"/>
    public bool TryCastRoundAtSoldiers(
        Vector3d origin,
        Vector3d end,
        SoldierView? shooter,
        int shooterTeam,
        out SoldierView? struck,
        out HumanHit hit) =>
        _rounds.TryCastRoundAtSoldiers(origin, end, shooter, shooterTeam, out struck, out hit);

    /// <inheritdoc cref="SoldierDamage.StrikeWithRound"/>
    public void StrikeWithRound(
        SoldierView struck,
        in HumanHit hit,
        Vector3d origin,
        Vector3d end,
        WeaponType weapon,
        float damageFactor,
        SoldierView? shooter) =>
        _damage.StrikeWithRound(struck, in hit, origin, end, weapon, damageFactor, shooter);

    /// <inheritdoc cref="SoldierGunShotRounds.TryShootAiRound"/>
    public bool TryShootAiRound(
        Vector3d origin,
        Vector3d end,
        float hitProbability,
        WeaponType weapon,
        SoldierView? shooter,
        WorldRayHit? worldHit,
        out HumanHit hit) =>
        _rounds.TryShootAiRound(origin, end, hitProbability, weapon, shooter, worldHit, out hit);

    /// <inheritdoc cref="SoldierDamage.ApplyExplosion"/>
    public void ApplyExplosion(Vector3d position, float radiusUnits, float falloffUnits, float damageFactor)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _damage.ApplyExplosion(position, radiusUnits, falloffUnits, damageFactor);
    }

    /// <inheritdoc cref="SoldierBodyQueries.LiveOccupants"/>
    public IEnumerable<HumanOccupant> LiveOccupants() => _bodies.LiveOccupants();

    /// <inheritdoc cref="SoldierBodyQueries.Occupants"/>
    public IReadOnlyCollection<HumanOccupant> Occupants() => _bodies.Occupants();

    /// <summary>A nearest-human intersection that does not apply damage or AI events.</summary>
    public HumanRayCast Probe() => TryIntersect;

    /// <summary>A living-human proximity query for armed mines.</summary>
    public HumanProximityCast ProximityProbe() => TryProximity;

    /// <inheritdoc cref="SoldierBodyQueries.TryProximity"/>
    public bool TryProximity(Vector3d center, float radiusUnits, out HumanProximityTarget target) =>
        _bodies.TryProximity(center, radiusUnits, out target);

    /// <inheritdoc cref="SoldierBodyQueries.TryIntersect"/>
    public bool TryIntersect(Vector3d origin, Vector3d end, out HumanHit hit) => _bodies.TryIntersect(origin, end, out hit);

    /// <summary>Stops drawing the standing body — the view has reached the head.</summary>
    public void HidePlayerBody()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _playerBody.Shown = false;
    }

    /// <inheritdoc cref="SoldierPlayerBody.HideFollowing"/>
    public void HidePlayerBodyFollowing()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _playerBody.HideFollowing();
    }

    /// <summary>Changes animation 168's playback direction without restarting or snapping its pose.</summary>
    /// <param name="direction">−1 down, 0 paused at a boundary, or +1 up.</param>
    public void SetPlayerLadderPlaybackDirection(int direction) =>
        PlayerBodyView?.SetLadderPlaybackDirection(direction);

    /// <inheritdoc cref="SoldierPlayerBody.InteractionCameraFeet"/>
    public Vector3d PlayerInteractionCameraFeet(Vector3d taskPosition) => _playerBody.InteractionCameraFeet(taskPosition);

    /// <inheritdoc cref="SoldierPlayerBody.HideClimbing"/>
    public void HidePlayerBodyClimbing() => _playerBody.HideClimbing();

    /// <inheritdoc cref="SoldierPlayerBody.TryHitPlayerBody"/>
    public bool TryHitPlayerBody(
        Vector3d origin, Vector3d end, Vector3d playerFeetUnits, float playerYaw, out HumanHit hit) =>
        _playerBody.TryHitPlayerBody(origin, end, playerFeetUnits, playerYaw, out hit);

    /// <inheritdoc cref="SoldierPlayerBody.TryCastPlayerBody"/>
    public bool TryCastPlayerBody(
        Vector3d origin,
        Vector3d end,
        Vector3d playerFeetUnits,
        float playerYaw,
        out IReadOnlyList<HumanBodyCrossing> crossings) =>
        _playerBody.TryCastPlayerBody(origin, end, playerFeetUnits, playerYaw, out crossings);

    /// <inheritdoc cref="SoldierBinocularScan.MarkerGlow"/>
    internal static Bgra32 MarkerGlow(BinocularMarkerColor marker) => SoldierBinocularScan.MarkerGlow(marker);

    /// <inheritdoc cref="SoldierSightQueries.HasLineOfSight(WorldRayCast, Vector3d, Vector3d)"/>
    internal static bool HasLineOfSight(WorldRayCast cast, Vector3d from, Vector3d to) =>
        SoldierSightQueries.HasLineOfSight(cast, from, to);

    /// <inheritdoc cref="SoldierSightQueries.RaySphere"/>
    internal static bool RaySphere(Vector3d origin, Vector3d dir, Vector3d centre, double radius, double reach, out double t) =>
        SoldierSightQueries.RaySphere(origin, dir, centre, radius, reach, out t);

    /// <inheritdoc cref="SoldierSightQueries.SegmentMeetsSphere"/>
    internal static bool SegmentMeetsSphere(Vector3d from, Vector3d to, Vector3d centre, double radius) =>
        SoldierSightQueries.SegmentMeetsSphere(from, to, centre, radius);

    /// <inheritdoc cref="SoldierContext.Stand"/>
    internal static Vector3d Stand(HumanSoldierTask soldier, SoldierModelGroup group, IGroundHeightField? terrain, WorldRayCast? cast) =>
        SoldierContext.Stand(soldier, group, terrain, cast);

    /// <inheritdoc cref="SoldierBodyQueries.FacingOf"/>
    internal static float FacingOf(in Matrix3f orientation) => SoldierBodyQueries.FacingOf(in orientation);

    /// <inheritdoc cref="SoldierBodyQueries.PoseAtPresentationOrientation"/>
    internal static BonePose PoseAtPresentationOrientation(SoldierView soldier, in Matrix3f orientation) =>
        SoldierBodyQueries.PoseAtPresentationOrientation(soldier, in orientation);

    /// <inheritdoc cref="SoldierPlayerBody.IsWireAnimation"/>
    internal static bool IsWireAnimation(int animation) => SoldierPlayerBody.IsWireAnimation(animation);

    /// <inheritdoc cref="SoldierDamage.WeaponForDrop"/>
    internal static WeaponType? WeaponForDrop(WeaponType? carriedWeaponType, WeaponType? _renderedWeaponType) =>
        SoldierDamage.WeaponForDrop(carriedWeaponType, _renderedWeaponType);

    /// <inheritdoc cref="SoldierContext.ForgetGroundSnaps"/>
    internal void ForgetGroundSnaps() => _context.ForgetGroundSnaps();

    /// <inheritdoc cref="SoldierContext.SnapToGround"/>
    internal double SnapToGround(double x, double y, double declaredZ) => _context.SnapToGround(x, y, declaredZ);

    /// <inheritdoc cref="SoldierContext.MakeGroundQuery"/>
    internal Igi1HumanAi.GroundQuery? MakeGroundQuery() => _context.MakeGroundQuery();

    /// <inheritdoc cref="SoldierContext.HealthOf"/>
    internal HumanHealth HealthOf(SoldierView soldier) => _context.HealthOf(soldier);

    /// <summary>The skinned model a soldier wears, so its ragdoll can be sized from its own geometry.</summary>
    internal MefModel? FindModel(SoldierView view) => _context.FindRoster(view)?.Body.Model;

    /// <inheritdoc cref="SoldierSightQueries.RefreshEyesAlongPlayerRound"/>
    internal void RefreshEyesAlongPlayerRound(Vector3d from, Vector3d to) => _sight.RefreshEyesAlongPlayerRound(from, to);

    /// <inheritdoc cref="SoldierNoiseBroadcaster.NotifyGunshotMiss"/>
    internal void NotifyGunshotMiss(Vector3d origin, Vector3d end, SoldierView? struck, Vector3d shooterPosition) =>
        _noise.NotifyGunshotMiss(origin, end, struck, shooterPosition);

    /// <inheritdoc cref="SoldierRagdolls.RagdollRigFor"/>
    internal RagdollSkeleton? RagdollRigFor(BoneAnimationSet set, MefModel? model) => _ragdolls.RagdollRigFor(set, model);

    /// <inheritdoc cref="SoldierRagdolls.RagdollWorldForCorpses"/>
    internal RagdollWorld RagdollWorldForCorpses() => _ragdolls.RagdollWorldForCorpses();

    /// <inheritdoc cref="SoldierPlayerBody.StartTraversalAnimation"/>
    internal void StartPlayerTraversalAnimation(
        SoldierView view,
        int animation,
        bool loops,
        bool backwards,
        bool paused,
        bool suppressRootHeight,
        double visualLift) =>
        _playerBody.StartTraversalAnimation(view, animation, loops, backwards, paused, suppressRootHeight, visualLift);

    /// <summary>Marks every soldier drawn close up, as a frame looking at all of them would.</summary>
    internal void ObserveAllForTesting()
    {
        foreach (SoldierModelGroup group in Roster)
        {
            foreach (SoldierAgent member in group.Agents)
            {
                member.InView = true;
            }
        }
    }

    /// <inheritdoc cref="SoldierSightQueries.ClearSightForTesting"/>
    internal bool ClearSightForTesting(Vector3d from, Vector3d to) => _sight.ClearSightForTesting(from, to);

    /// <summary>A soldier's accumulated damage, for scenario runs.</summary>
    internal float DamageForTesting(SoldierView soldier) =>
        Health.TryGetValue(soldier, out HumanHealth? health) ? health.Damage : 0f;

    /// <inheritdoc cref="SoldierSightQueries.DescribeSightForTesting"/>
    internal string DescribeSightForTesting(Vector3d from, Vector3d to) => _sight.DescribeSightForTesting(from, to);
}
