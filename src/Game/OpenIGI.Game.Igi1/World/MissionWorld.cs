using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Titles;
using OpenIGI.Game.Ai;
using OpenIGI.Game.Igi1.Ai;
using OpenIGI.Game.Igi1.Player;
using OpenIGI.Game.Igi1.Weapons;
using OpenIGI.Game.Player;
using OpenIGI.Game.Weapons;
using OpenIGI.Game.World;

namespace OpenIGI.Game.Igi1.World;

/// <summary>
/// Project I.G.I.'s played mission: the shared mission state, and the player's rig, the soldiers' simulation, the guards'
/// rounds, the ambience and the title's own tables.
/// </summary>
public sealed class MissionWorld : MissionState, IInspectableMission
{
    /// <summary>Initializes a new instance of the <see cref="MissionWorld"/> class.</summary>
    public MissionWorld() => _mineWaits = new MineFieldWaits(this);

    /// <summary>Gets or sets the doors.</summary>
    public DoorRegistry? Doors { get; set; }

    /// <summary>Gets or sets the elevators.</summary>
    public ElevatorRegistry? Elevators { get; set; }

    /// <summary>Gets or sets what the player and the soldiers use: switches, terminals, ladders and the rest.</summary>
    public WorldInteractionRegistry? Interactions { get; set; }

    /// <summary>Gets or sets the weapons lying in the level.</summary>
    public GunPickupRegistry? GunPickups { get; set; }

    /// <summary>Gets or sets the level's other pickups.</summary>
    public GenericPickupRegistry? GenericPickups { get; set; }

    /// <summary>Gets or sets the alarm speakers.</summary>
    public AlarmSpeakerRegistry? AlarmSpeakers { get; set; }

    /// <summary>Gets or sets what can be destroyed.</summary>
    public DestructibleObjectRegistry? Destructibles { get; set; }

    /// <summary>Gets or sets the security cameras.</summary>
    public SecurityCameraRegistry? SecurityCameras { get; set; }

    /// <summary>Gets or sets the radar dishes and the authored rotating tasks.</summary>
    public RotatingObjectRegistry? RotatingObjects { get; set; }

    /// <summary>Gets or sets the cutscene runtime, which also walks the level each tick.</summary>
    public CutsceneRuntime? Cutscenes { get; set; }

    /// <summary>Gets the level's expressions, which everything built for the level evaluates through; none before the cut-scenes are built.</summary>
    public LevelExpressionRuntime? LevelExpressions => Cutscenes?.Expressions;

    /// <summary>Gets or sets the objective runtime.</summary>
    public MissionObjectiveRuntime? Objectives { get; set; }

    /// <inheritdoc/>
    public override IPlacementObjects Placements => _placements ??= new Igi1PlacementObjects(this);

    private Igi1PlacementObjects? _placements;

    /// <inheritdoc/>
    public override IPlacementVariant? PlacementVariants => Interactions;

    /// <inheritdoc/>
    public override OpenIGI.Game.Hosting.IMissionCutscenes? Scenes => Cutscenes;

    /// <inheritdoc/>
    public override OpenIGI.Game.Hosting.IMissionObjectives? Progress => Objectives;

    /// <inheritdoc/>
    public override IReadOnlyList<GunPickup>? LyingGuns => GunPickups?.Items;

    /// <inheritdoc/>
    public override IReadOnlyList<SecurityCamera>? StatusCameras => SecurityCameras?.Cameras;

    /// <inheritdoc/>
    /// <remarks>An alarm light's turning reflector throws its red flare at the eye (<c>0x439F10</c>).</remarks>
    public override IReadOnlyList<WorldGlow> AlarmFlares(Vector3d eyeUnits, float tickFraction) =>
        AlarmSpeakers is { Reflectors.Count: > 0 } lights ? lights.ReflectorFlares(eyeUnits, tickFraction) : [];

    /// <inheritdoc/>
    public override CutsceneFrame? CutsceneFrameShown => IntroSkipPending ? null : Cutscenes?.Frame;

    /// <inheritdoc/>
    public override void ReleaseAlarmsAndDestructibles()
    {
        AlarmSpeakers = null;
        Destructibles = null;
    }

    /// <inheritdoc/>
    public override void ReleaseRuntimes()
    {
        Objectives?.Release();
        Objectives = null;
        Cutscenes = null;
        SecurityCameras = null;
        base.ReleaseRuntimes();
    }

    /// <summary>Gets or sets the level's authored ambience boxes.</summary>
    public AmbientAreaRegistry? Ambience { get; set; }

    /// <summary>
    /// Gets or sets where the ear is for the next tick's ambient areas, in engine units: the camera as the last tick left it,
    /// or <see langword="null"/> before any camera was placed.
    /// </summary>
    public Vector3d? AmbienceListener { get; set; }

    /// <summary>Gets the player's life: its health, its death, and what a hit leaves on the screen.</summary>
    public Igi1PlayerLife PlayerLife { get; } = new();

    /// <inheritdoc/>
    public override bool IsPlayerDead { get => PlayerLife.IsDead; set => PlayerLife.IsDead = value; }

    /// <summary>Gets or sets what the soldiers read the player as, asked each tick.</summary>
    public Func<SoldierEnemy>? CurrentEnemy { get; set; }

    /// <summary>Gets or sets the guards' rounds this mission, once the brains are animated.</summary>
    public GuardRounds? GuardRounds { get; set; }

    /// <summary>Gets or sets what breaks a pane a grenade, a missile or a car strikes, answering whether it broke.</summary>
    public Func<RetailGlassPane, Vector3d, bool>? TouchGlass { get; set; }

    /// <summary>Gets or sets the level's smoke plumes.</summary>
    public Igi1SmokePlumes? SmokePlumes { get; set; }

    /// <summary>Gets or sets the level's minefields.</summary>
    public IReadOnlyList<Igi1MineFieldTask> MineFields { get; set; } = [];

    /// <inheritdoc/>
    /// <remarks>The shared parts, then the panes of glass the cars meet and the level's own minefields' waits.</remarks>
    /// <remarks>Lifts before doors, so a door riding a car is re-placed against the car where it stands again.</remarks>
    protected override IEnumerable<ILevelReset?> LevelParts =>
    [
        Elevators, Doors, Interactions, GunPickups, GenericPickups, AlarmSpeakers, Destructibles, SecurityCameras, RotatingObjects,
        Objectives, RetailWorld as ILevelReset, _mineWaits,
    ];

    private readonly MineFieldWaits _mineWaits;

    // Each minefield's wait between its blasts, as the match began.
    private sealed class MineFieldWaits(MissionWorld world) : ILevelReset
    {
        private int[] _start = [];

        public void MarkLevelStart() => _start = [.. world.MineFields.Select(static f => f.Pause)];

        public void ResetToLevelStart(ObjectCollisionIndex? solids)
        {
            for (int i = 0; i < world.MineFields.Count && i < _start.Length; i++)
            {
                world.MineFields[i].Pause = _start[i];
            }
        }
    }

    /// <summary>Gets or sets what the minefields' conditions are read through.</summary>
    public LevelExpressionRuntime? MineFieldExpressions { get; set; }

    /// <summary>Gets or sets the soldiers' simulation: their brains, bodies and rounds, or <see langword="null"/> before a level has any.</summary>
    public SoldierSimulation? Soldiers { get; set; }

    /// <summary>
    /// Gets another game's player standing in the rig's stead, as the mission's player task's body (<see cref="HumanTask.Body"/>):
    /// where its feet and eyes are and how it stands, which the mission's guards, areas and lifts read for the player's; or
    /// <see langword="null"/> while the rig is the player, or no player stands.
    /// </summary>
    public IHumanBodyState? PlayerStandIn => Player is null && PlayerTask?.Body is { } body and not Igi1PlayerBody ? body : null;

    /// <summary>Gets or sets the world the original's physics objects step in: the cars, and the thrown grenades.</summary>
    public IRetailCarWorld? RetailWorld { get; set; }

    /// <summary>Gets the missiles' own random stream, seeded once and drawn by every missile's smoke and wobble.</summary>
    public MissileRandom MissileStream { get; } = new();

    /// <summary>Gets or sets the player's rig: its body, eyes, weapons and the impacts its rounds made, or <see langword="null"/> before a level places one.</summary>
    public PlayerCamera? Player { get; set; }

    /// <summary>Gets a co-op session's other players, run after the mission's own each tick; empty when playing alone.</summary>
    public List<Multiplayer.Igi1NetworkPlayer> NetworkPlayers { get; } = [];

    /// <summary>Gets or sets the id of the mission this level is.</summary>
    public int MissionId { get; set; }

    /// <summary>Gets or sets the saves made this mission, each of which uses one of the charges its difficulty allows.</summary>
    public int SavesUsed { get; set; }

    /// <summary>Gets or sets the games loaded this mission.</summary>
    public int GameLoads { get; set; }

    /// <summary>Gets the other players a co-op client's level shows, as the server's snapshots have them.</summary>
    public List<Multiplayer.Igi1NetworkPlayer> ShownPartners { get; } = [];

    /// <summary>
    /// Gets what the other players' weapons -- and the soldiers' -- have in the world as a co-op client's level shows it, from
    /// the server's snapshots: their grenades, mines, rockets and launcher rounds.
    /// </summary>
    public List<OpenIGI.Game.Weapons.ShownProjectile> ShownProjectiles { get; } = [];

    /// <summary>
    /// Gets every other player in the level as this machine knows it: on a server the partners it runs, on a client those the
    /// server's snapshots show. What the level asks of "the player" -- an area entered -- it asks of each of these too.
    /// </summary>
    public IReadOnlyList<Multiplayer.Igi1NetworkPlayer> Partners => CoopClient ? ShownPartners : NetworkPlayers;

    /// <summary>Gets or sets what tells the frame which co-op partners to draw each tick, or <see langword="null"/> playing alone.</summary>
    public Func<IReadOnlyList<Ai.PartnerBody>>? PartnerBodies { get; set; }

    /// <summary>Gets or sets the mission's simulation ticks, the clock a session's commands are stamped against.</summary>
    /// <remarks>It runs whatever the mission's own player is doing: dead, in a cut-scene or at the computer.</remarks>
    public long PlayerTicks { get; set; }

    /// <summary>Gets or sets a value indicating whether this mission is a co-op client's, whose player's death the server decides.</summary>
    /// <remarks>Its level never sees the player dead: the session respawns it, and the server's level decides the mission.</remarks>
    public bool CoopClient { get; set; }

    /// <summary>Gets or sets the entity whose rounds are landing now, for a co-op server's record of them: the mission's own player's, nought, unless a partner's are.</summary>
    public int ImpactSource { get; set; }

    /// <summary>Gets or sets what a co-op server does once a partner has run its tick: shows where its rounds landed.</summary>
    public Action<Multiplayer.Igi1NetworkPlayer>? PartnerRan { get; set; }

    /// <summary>
    /// Gets what mods run on each tick the player updates, just before the update: the place for a move of a mod's own to
    /// advance, with the player's controls still to be read. Empty unless a mod adds to it. <b>Not retail.</b>
    /// </summary>
    /// <remarks>Not run on a tick the player does not update: dead, in a cutscene, at the map computer.</remarks>
    public List<Action> BeforePlayerUpdate { get; } = [];

    /// <summary>
    /// Gets what mods run at the end of every tick, once the level walk is done, whether the player updated or not. Empty
    /// unless a mod adds to it. <b>Not retail.</b>
    /// </summary>
    /// <remarks>
    /// Not the <c>LevelTicked</c> event: an event's subscribers only read, and the game plays the same without them, where
    /// what runs here may change the level, such as giving the player his controls back.
    /// </remarks>
    public List<Action> TickEnded { get; } = [];

    /// <summary>
    /// Gets where mods offer the player a prompt: each is asked on every tick the player updates, and the first to offer one
    /// is shown, when the game has none of its own there. Empty unless a mod adds to it. <b>Not retail.</b>
    /// </summary>
    public List<Func<PromptOffer?>> PromptOffers { get; } = [];

    /// <summary>Gets or sets the prompt a mod offers this tick, the one drawn; <see langword="null"/> for none.</summary>
    public PromptOffer? OfferedPrompt { get; set; }

    /// <summary>
    /// Gets or sets the player's view as a mod has taken it, or <see langword="null"/> while the player's own eye has it.
    /// <b>Not retail.</b>
    /// </summary>
    public ViewTakeover? ViewTakeover { get; set; }

    /// <summary>Gets or sets a value indicating whether a co-op session's partners have run this tick.</summary>
    public bool PartnersTicked { get; set; }

    /// <summary>Gets or sets the computer the player carries, the original's own.</summary>
    public OpenIGI.Game.Igi1.Computer.Igi1Computer? Computer { get; set; }

    /// <summary>Gets or sets the title's human AI table.</summary>
    public HumanAiConfigRegistry? AiConfig { get; set; }

    /// <summary>Gets or sets the title's animation triggers, the sounds the first-person clips name by event, or <see langword="null"/>.</summary>
    public AnimTriggerRegistry? AnimationTriggers { get; set; }

    /// <summary>Gets the soldiers' presenter, which draws the simulation it carries, or <see langword="null"/> while none is up.</summary>
    public ISoldierPresenter? SoldierPresenter { get; private set; }

    /// <summary>Gets or sets what tells the session's events what each tick changed, or <see langword="null"/> before a level is up.</summary>
    internal Igi1EventWatch? EventWatch { get; set; }

    /// <summary>Gets or sets the level's player task, which the rounds and blasts that reach the player are sent to.</summary>
    public HumanPlayerTask? PlayerTask { get; set; }

    /// <summary>Gets or sets the player's own hit handlers, which its task is given when the player is stood in the level.</summary>
    public Igi1PlayerEvents? PlayerEvents { get; set; }

    /// <summary>Clears the grenades, missiles and launcher rounds in flight, as <c>Game_CutsceneDelete</c> does.</summary>
    public void ClearTransientProjectiles()
    {
        Player?.Grenades.Clear();
        Player?.Missiles.Clear();
        VehicleMissiles.Clear();
        Player?.M203.Clear();
    }

    /// <summary>Advances missiles fired by authored vehicles against the player's own body.</summary>
    public void TickVehicleMissiles()
    {
        if (Player is not { } player)
        {
            return;
        }
        bool PlayerProbe(Vector3d from, Vector3d to, out HumanHit hit)
        {
            hit = default;
            if (PlayerLife.Health.IsDead)
            {
                return false;
            }
            if (Soldiers?.TryHitPlayerBody(from, to, player.Body.Position, player.Body.Yaw, out hit) == true)
            {
                return true;
            }
            Vector3f facing = new(-MathF.Sin(player.Body.Yaw), MathF.Cos(player.Body.Yaw), 0f);
            return HumanHitTest.TryCast(from, to, player.Body.Position, facing, pose: null, out hit);
        }
        MissileWorldTraceCast? walls = Trace is { } trace
            ? (LiveMissile missile, Vector3d from, Vector3d to, out WorldTraceHit hit) =>
            {
                bool found = trace(
                    new WorldTraceRequest(
                        from,
                        to,
                        WorldTraceMask.World,
                        IgnoredOwner: missile.CollisionOwner),
                    out hit);
                return found;
            }
            : EffectWalls is { } effectWalls
                ? (LiveMissile _, Vector3d from, Vector3d to, out WorldTraceHit hit) =>
                {
                    bool found = effectWalls(from, to, out WorldRayHit rayHit);
                    hit = found
                        ? WorldTraceHit.FromRayHit(
                            new WorldTraceRequest(from, to, WorldTraceMask.World),
                            rayHit,
                            WorldTraceHitKind.StaticObject)
                        : default;
                    return found;
                }
                : null;
        VehicleMissiles.TickWithMissileContext(walls, PlayerProbe);
    }

    /// <inheritdoc/>
    public override void AdoptTitleServices(IServiceCatalog services)
    {
        base.AdoptTitleServices(services);
        AnimationTriggers = services.TryGet(out AnimTriggerRegistry? triggers) ? triggers : null;
        AiConfig = services.TryGet(out HumanAiConfigRegistry? ai) ? ai : null;
    }

    /// <inheritdoc/>
    public override void ResetPlayerLife(int difficulty)
    {
        base.ResetPlayerLife(difficulty);
        PlayerLife.Reset(difficulty);
    }

    /// <inheritdoc/>
    public override void StartFreshLife()
    {
        base.StartFreshLife();
        PlayerLife.StartFresh(PlayerDifficulty);
    }

    /// <inheritdoc/>
    public override bool HasOwnPlayer => Player is not null;

    /// <inheritdoc/>
    public override MissileSimulation? PlayerMissiles => Player?.Missiles;

    /// <inheritdoc/>
    public override void SetPlayerComputerOpen(bool open) => Player?.SetMapComputerOpen(open);

    /// <inheritdoc/>
    public override void ForgetPlayer() => Player = null;

    /// <summary>Takes the level's soldiers as the frame draws them, and the simulation they carry.</summary>
    /// <param name="bodies">The soldiers, or <see langword="null"/> to drop them.</param>
    public override void AdoptSoldiers(ISoldierBodies? bodies)
    {
        if (SoldierPresenter is not null)
        {
            Bodies.Remove(SoldierPresenter);
        }

        SoldierPresenter = bodies as ISoldierPresenter;
        Soldiers = SoldierPresenter?.Simulation;
        Soldiers?.Joined = Joined;
        if (SoldierPresenter is not null)
        {
            Bodies.Add(SoldierPresenter);
        }
    }

    /// <inheritdoc/>
    /// <remarks>The soldiers and the player go with the rest, and a co-op session's partners with the level they were in.</remarks>
    public override void ReleaseWorld()
    {
        NetworkPlayers.Clear();
        ShownPartners.Clear();
        ShownProjectiles.Clear();
        PartnerBodies = null;
        PartnerRan = null;
        CoopClient = false;
        Soldiers = null;
        SoldierPresenter = null;
        Player = null;
        PlayerTask = null;
        Destructibles = null;
        Doors = null;
        Elevators = null;
        Interactions = null;
        GunPickups = null;
        GenericPickups = null;
        base.ReleaseWorld();
    }
}
