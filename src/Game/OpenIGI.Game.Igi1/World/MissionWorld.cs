using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;
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

    /// <summary>Gets or sets the soldiers' simulation: their brains, bodies and rounds, or <see langword="null"/> before a level has any.</summary>
    public SoldierSimulation? Soldiers { get; set; }

    /// <summary>Gets or sets the world the original's physics objects step in: the cars, and the thrown grenades.</summary>
    public IRetailCarWorld? RetailWorld { get; set; }

    /// <summary>Gets the missiles' own random stream, seeded once and drawn by every missile's smoke and wobble.</summary>
    public MissileRandom MissileStream { get; } = new();

    /// <summary>Gets or sets the player's rig: its body, eyes, weapons and the impacts its rounds made, or <see langword="null"/> before a level places one.</summary>
    public PlayerCamera? Player { get; set; }

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
    /// <remarks>The soldiers and the player go with the rest.</remarks>
    public override void ReleaseWorld()
    {
        Soldiers = null;
        SoldierPresenter = null;
        Player = null;
        PlayerTask = null;
        base.ReleaseWorld();
    }
}
