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
public sealed class MissionWorld : MissionState
{
    /// <summary>Gets or sets the level's authored ambience boxes.</summary>
    public AmbientAreaRegistry? Ambience { get; set; }

    /// <summary>
    /// Gets or sets where the ear is for the next tick's ambient areas, in engine units: the camera as the last tick left it,
    /// or <see langword="null"/> before any camera was placed.
    /// </summary>
    public Vector3d? AmbienceListener { get; set; }

    /// <summary>Gets or sets the damage factor an electric fence deals the player each tick it is touched.</summary>
    public float ElectricFenceDose { get; set; } = ElectricFenceContact.DefaultDamageFactor;

    /// <summary>Gets or sets what the soldiers read the player as, asked each tick.</summary>
    public Func<SoldierEnemy>? CurrentEnemy { get; set; }

    /// <summary>Gets or sets the guards' rounds this mission, once the brains are animated.</summary>
    public GuardRounds? GuardRounds { get; set; }

    /// <summary>Gets or sets what breaks a pane a grenade, a missile or a car strikes, answering whether it broke.</summary>
    public Func<RetailGlassPane, Vector3d, bool>? TouchGlass { get; set; }

    /// <summary>Gets or sets the level's smoke plumes.</summary>
    public Igi1SmokePlumes? SmokePlumes { get; set; }

    /// <summary>Gets or sets the level's minefields.</summary>
    public IReadOnlyList<Igi1MineField> MineFields { get; set; } = [];

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

    /// <summary>Gets the other players a co-op client's level shows, as the server's snapshots have them.</summary>
    public List<Multiplayer.Igi1NetworkPlayer> ShownPartners { get; } = [];

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
            if (PlayerHealth.IsDead)
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

    /// <summary>Gets or sets the white-out a flashbang raises, which the screen draws.</summary>
    public FlashbangScreenFlash FlashbangFlash { get; set; } = null!;

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
        ElectricFenceDose = ElectricFenceContact.DefaultDamageFactor;
        FlashbangFlash = new();
    }

    /// <inheritdoc/>
    public override void StartFreshLife()
    {
        base.StartFreshLife();
        FlashbangFlash = new();
    }

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
        PartnerBodies = null;
        PartnerRan = null;
        CoopClient = false;
        Soldiers = null;
        SoldierPresenter = null;
        Player = null;
        PlayerTask = null;
        base.ReleaseWorld();
    }
}
