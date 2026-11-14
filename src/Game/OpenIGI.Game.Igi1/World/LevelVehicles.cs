using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;
using OpenIGI.Formats.Ai;
using OpenIGI.Game.Ai;
using OpenIGI.Game.Level;
using OpenIGI.Game.Weapons;

namespace OpenIGI.Game.World;

/// <summary>
/// A level's vehicles: the cars, helicopters and mounted-gun holders, each run from its own task, with the bodies they
/// drive on, the guns and eyes they carry, their doors, rotors and wheels, and the <c>CarAI</c> that steers a car.
/// </summary>
/// <remarks>
/// <para>
/// In the original a <c>Car</c>, a <c>Heli</c>, a <c>CarAI</c> and an <c>AIStationaryGunHolder</c> are tasks with run
/// handlers of their own, apart from the <c>CutScene</c> that frames them; an <c>AnimTask</c> only sends the object it
/// targets its packets. This is that module: the cut-scene runtime makes it, because the two share the level's transform
/// states, and hands a vehicle's packets to it.
/// </para>
/// <para>
/// A car's run (<c>Car</c>): its eye, its children, its guns, then what it runs over. A helicopter's: its door first, then
/// the same. Each is run once a tick, where the level's walk reaches its task.
/// </para>
/// </remarks>
public sealed class LevelVehicles
{
    private readonly LevelMotionContext _context;
    private readonly LevelRun _walker;
    private readonly List<CarAiState> _carAis = [];
    private readonly Dictionary<GameTask, int> _lastCarTick = [];

    /// <summary>Initializes a new instance of the <see cref="LevelVehicles"/> class.</summary>
    /// <param name="context">The level's transform states and expressions, shared with the cut-scenes.</param>
    /// <param name="attachments">What rides what.</param>
    /// <param name="animations">The level's <c>AnimTask</c>s, which a vehicle's guns read the run of.</param>
    /// <param name="walker">The level's run, which runs a vehicle's children.</param>
    /// <param name="obstacleHit">What a driven car's sweep strikes, or <see langword="null"/> for nothing.</param>
    /// <param name="obstacleReleased">Whether an obstacle a car waits behind has gone, or <see langword="null"/> for never.</param>
    internal LevelVehicles(
        LevelMotionContext context,
        CutsceneAttachments attachments,
        List<CutsceneAnimationState> animations,
        LevelRun walker,
        Func<GameTask, Vector3d, Vector3d, (GameTask Owner, WorldRayHit Hit, string Label)?>? obstacleHit,
        Func<GameTask, bool>? obstacleReleased)
    {
        _context = context;
        _walker = walker;
        Ground = new VehicleGroundSupport(context);
        Bodies = new VehicleBodies(context, Ground, obstacleHit, obstacleReleased);
        Armament = new VehicleArmaments(context, _carAis, animations);
        Holders = new StationaryGunHolders(context, Armament);
        Parts = new VehicleParts(context, Armament);
        CarAi = new CarAiDrivers(context, Bodies, Armament, _carAis);
        Draw = new VehicleDraw(context, attachments, Bodies, Armament, Parts);
    }

    /// <summary>Gets what the vehicles stand on.</summary>
    internal VehicleGroundSupport Ground { get; }

    /// <summary>Gets the vehicles' bodies.</summary>
    internal VehicleBodies Bodies { get; }

    /// <summary>Gets the vehicles' guns, eyes and death zones.</summary>
    internal VehicleArmaments Armament { get; }

    /// <summary>Gets the mounted-gun holders.</summary>
    internal StationaryGunHolders Holders { get; }

    /// <summary>Gets the vehicles' doors, rotors and other parts.</summary>
    internal VehicleParts Parts { get; }

    /// <summary>Gets what steers the cars.</summary>
    internal CarAiDrivers CarAi { get; }

    /// <summary>Gets where the vehicles and what they carry are drawn.</summary>
    internal VehicleDraw Draw { get; }

    /// <summary>Gets the CarAI tasks' states, for the suites that read them.</summary>
    internal IReadOnlyList<CarAiState> CarAisForTesting => _carAis;

    /// <summary>Supplies the living player eye used by firing vehicles.</summary>
    public Func<Vector3d?>? VehicleCombatTarget
    {
        get => Armament.VehicleCombatTarget;
        set => Armament.VehicleCombatTarget = value;
    }

    /// <summary>
    /// Gets or sets where a vehicle's eye tests the same target: the point the human reports to a detection sweep
    /// (message <c>0x489620</c>), which the soldiers' sweep uses too. Falls back to <see cref="VehicleCombatTarget"/>.
    /// </summary>
    public Func<Vector3d?>? VehicleSightTarget
    {
        get => Armament.VehicleSightTarget;
        set => Armament.VehicleSightTarget = value;
    }

    /// <summary>Tests the firing vehicle's muzzle-to-player sight segment.</summary>
    public Func<GameTask, Vector3d, Vector3d, bool>? VehicleCombatLineOfSight
    {
        get => Armament.VehicleCombatLineOfSight;
        set => Armament.VehicleCombatLineOfSight = value;
    }

    /// <summary>Receives a vehicle missile only after sight, retention and gun cadence allow it.</summary>
    public Action<VehicleCombatShot>? VehicleCombatShotFired
    {
        get => Armament.VehicleCombatShotFired;
        set => Armament.VehicleCombatShotFired = value;
    }

    /// <summary>Gets or sets the level's random stream, which the vehicles' parts and guns draw from.</summary>
    public IgiRandom? VehicleRandom
    {
        get => Parts.Random;
        set
        {
            Parts.Random = value;
            Armament.Random = value;
        }
    }

    /// <summary>Gets or sets who a mounted-gun holder's eye looks for.</summary>
    public Func<IReadOnlyList<AiDetectable>>? GunHolderSubjects
    {
        get => Holders.Subjects;
        set => Holders.Subjects = value;
    }

    /// <summary>Gets the level's mounted-gun holders.</summary>
    public IEnumerable<GameTask> GunHolders => Holders.Holders;

    /// <summary>Gets a holder's team and whether it is on.</summary>
    /// <param name="holder">The holder's task.</param>
    /// <returns>The state, or <see langword="null"/> for a task that is no holder.</returns>
    public (int Team, bool On)? GunHolderState(GameTask holder) => Holders.StateOf(holder);

    /// <summary>Gets a holder's detection record.</summary>
    /// <param name="holder">The holder's task.</param>
    /// <returns>The record, or <see langword="null"/> for a task that is no holder.</returns>
    public AiDetectionRecord? GunHolderRecord(GameTask holder) => Holders.RecordOf(holder);

    /// <inheritdoc cref="VehicleArmaments.ConfigureVehicleCombat"/>
    public void ConfigureVehicleCombat(GameTask vehicle, WeaponType weapon) => Armament.ConfigureVehicleCombat(vehicle, weapon);

    /// <inheritdoc cref="VehicleArmaments.ConfigureVehicleGuns"/>
    public void ConfigureVehicleGuns(GameTask vehicle, IReadOnlyList<VehicleGun> guns) => Armament.ConfigureVehicleGuns(vehicle, guns);

    /// <inheritdoc cref="VehicleBodies.VehicleSoundSources"/>
    public IEnumerable<VehicleSoundSource> VehicleSoundSources() => Bodies.VehicleSoundSources();

    /// <inheritdoc cref="VehicleArmaments.VehicleGunSounds"/>
    public IEnumerable<VehicleGunSound> VehicleGunSounds() => Armament.VehicleGunSounds();

    /// <inheritdoc cref="VehicleArmaments.ConfigureDeathZones"/>
    public void ConfigureDeathZones(GameTask vehicle, IReadOnlyList<VehicleDeathZone> zones) => Armament.ConfigureDeathZones(vehicle, zones);

    /// <inheritdoc cref="VehicleArmaments.TryRunOver"/>
    public bool TryRunOver(Vector3d humanOrigin, out GameTask? vehicle) => Armament.TryRunOver(humanOrigin, out vehicle);

    /// <inheritdoc cref="VehicleArmaments.GunTurret"/>
    public VehicleGunTurret? GunTurret(GameTask vehicle, int index) => Armament.GunTurret(vehicle, index);

    /// <summary>Settles every collision-enabled helicopter onto whatever is under it.</summary>
    /// <remarks>
    /// A one-shot placement pass, not a consequence of the collision index being available: the index is what every
    /// vehicle's ground query reads, and only a helicopter authored with collision wants to be re-seated on it.
    /// </remarks>
    public void RestHelicoptersOnSupport() => Ground.RestCollisionHelisOnSupport();

    /// <inheritdoc cref="VehicleBodies.ConfigureRetailCars"/>
    public void ConfigureRetailCars(
        IRetailCarWorld world,
        Func<string, OpenIGI.Formats.World.MefModel?> models,
        Func<string, MagicObjectDefinition?>? magic = null) =>
        Bodies.ConfigureRetailCars(world, models, magic);

    /// <inheritdoc cref="CarAiDrivers.ConfigureCarAi"/>
    public void ConfigureCarAi(IReadOnlyDictionary<int, GraphFile> graphs) => CarAi.ConfigureCarAi(graphs);

    /// <inheritdoc cref="CarAiDrivers.CarAiTargetNode"/>
    public int? CarAiTargetNode(GameTask car) => CarAi.CarAiTargetNode(car);

    /// <inheritdoc cref="VehicleBodies.VehicleOccupants"/>
    public IReadOnlyList<VehicleOccupant> VehicleOccupants() => Bodies.VehicleOccupants();

    /// <inheritdoc cref="VehicleBodies.BindVehicleWheels"/>
    public void BindVehicleWheels(WorldScene scene, Func<string, float>? modelRadius = null) =>
        Bodies.BindVehicleWheels(scene, modelRadius);

    /// <inheritdoc cref="VehicleParts.Configure"/>
    public void ConfigureVehicleParts(WorldScene scene, Func<string, MagicObjectDefinition?> magic, Func<MagicObjectDefinition, string> taskTypeName) =>
        Parts.Configure(scene, magic, taskTypeName);

    /// <inheritdoc cref="VehicleParts.Made"/>
    public void VehiclePartMade(WorldPlacement placement) => Parts.Made(placement);

    /// <inheritdoc cref="VehicleArmaments.BindVehicleGunPlacements"/>
    public void BindVehicleGunPlacements(WorldScene scene) => Armament.BindVehicleGunPlacements(scene);

    /// <inheritdoc cref="VehicleArmaments.VehicleGunVisuals"/>
    public IEnumerable<VehicleGunVisual> VehicleGunVisuals() => Armament.VehicleGunVisuals();

    /// <inheritdoc cref="VehicleDraw.TryGetWheelDraw"/>
    public bool TryGetWheelDraw(WorldPlacement placement, out Matrix3f rotation) => Draw.TryGetWheelDraw(placement, out rotation);

    /// <inheritdoc cref="VehicleBodies.RetailVehicleModels"/>
    public IReadOnlyList<RetailRayModel> RetailVehicleModels() => Bodies.RetailVehicleModels();

    /// <summary>Makes these the vehicles a level's vehicle tasks run through, each registered as its type needs.</summary>
    /// <param name="levelRoot">The level.</param>
    /// <param name="visionPhases">The phase the load gave each eye in the round of detection sweeps.</param>
    /// <remarks>A holder's record takes the phase the load gave it (<c>0x46E7F0</c> initialises it after its children post-load).</remarks>
    internal void Adopt(GameTask levelRoot, IReadOnlyDictionary<GameTask, int> visionPhases)
    {
        foreach (GameTask task in levelRoot.Descendants())
        {
            if (task is not VehicleTask typed)
            {
                continue;
            }

            if (typed is StationaryGunHolderTask)
            {
                Holders.Register(task, visionPhases.TryGetValue(task, out int phase) ? phase : Armament.NextVisionPhase());
            }

            typed.Vehicles = this;
        }
    }

    /// <summary>Gets a value indicating whether a level's run is the one these vehicles run in.</summary>
    /// <param name="level">The run.</param>
    /// <returns>Whether it is theirs.</returns>
    internal bool RunsIn(LevelRun level) => ReferenceEquals(level, _walker);

    /// <summary>A car's run: its eye, its children, its guns, what it runs over; once a tick.</summary>
    /// <param name="car">The car's task.</param>
    internal void RunCar(GameTask car)
    {
        if (_lastCarTick.TryGetValue(car, out int last) && last == _context.SimulationTick)
        {
            return;
        }

        _lastCarTick[car] = _context.SimulationTick;
        Armament.TickVehicleVision(car);
        _walker.RunChildren(car);
        Armament.TickVehicleCombat(car);
        Armament.TickDeathZones(car);
    }

    /// <summary>A helicopter's run: its door, its eye, its children, its guns, what it runs over.</summary>
    /// <param name="heli">The helicopter's task.</param>
    internal void RunHeli(GameTask heli)
    {
        Parts.TickDoor(heli);
        Armament.TickVehicleVision(heli);
        _walker.RunChildren(heli);
        Armament.TickVehicleCombat(heli);
        Armament.TickDeathZones(heli);
    }

    /// <summary>A mounted-gun holder's run, its children run where its own update places them.</summary>
    /// <param name="holder">The holder's task.</param>
    internal void RunGunHolder(GameTask holder) => Holders.Tick(holder, () => _walker.RunChildren(holder));

    /// <summary>A CarAI's run (<c>0x425DE0</c>).</summary>
    /// <param name="driver">What the CarAI drives.</param>
    internal void RunCarAi(CarAiState driver) => CarAi.RunFromWalk(driver);
}
