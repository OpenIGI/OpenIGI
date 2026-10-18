using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;
using OpenIGI.Game.Ai;
using OpenIGI.Game.Igi1.Ai;
using OpenIGI.Game.Igi1.Player;
using OpenIGI.Game.Player;
using OpenIGI.Game.Weapons;
using OpenIGI.Game.World;

namespace OpenIGI.Game.Igi1.World;

/// <summary>
/// The played mission's world, as the host keeps it: the level's registries of what can be opened, ridden, picked
/// up, seen, heard and blown up, and its cutscene and objective runtimes.
/// </summary>
/// <remarks>
/// Each is <see langword="null"/> until the mission system that makes it has run (see the host's <c>MissionSystemNames</c>), and
/// is dropped again as the mission is released. Held in one place so that what reads the level -- the inspector, the
/// sounds, the effects -- is handed the mission rather than the whole host.
/// </remarks>
public sealed class MissionWorld
{
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

    /// <summary>Gets or sets the level's authored ambience boxes.</summary>
    public AmbientAreaRegistry? Ambience { get; set; }

    /// <summary>Gets or sets the cutscene runtime, which also walks the level each tick.</summary>
    public CutsceneRuntime? Cutscenes { get; set; }

    /// <summary>Gets or sets the objective runtime.</summary>
    public MissionObjectiveRuntime? Objectives { get; set; }

    /// <summary>Gets or sets the difficulty the player's damage is scaled by.</summary>
    public int PlayerDifficulty { get; set; }

    /// <summary>Gets or sets the player's full health.</summary>
    public float PlayerMaximumHealth { get; set; }

    /// <summary>Gets or sets how far the player's body animations carry it, the human type's delta translation scale.</summary>
    public float PlayerDeltaTranslationScale { get; set; } = HumanMotion.DefaultDeltaTranslationScale;

    /// <summary>Gets or sets the damage factor an electric fence deals the player each tick it is touched.</summary>
    public float ElectricFenceDose { get; set; } = ElectricFenceContact.DefaultDamageFactor;

    /// <summary>Gets or sets the player's health.</summary>
    public HumanHealth PlayerHealth { get; set; } = null!;

    /// <summary>Gets or sets a value indicating whether the player has died.</summary>
    public bool IsPlayerDead { get; set; }

    /// <summary>Gets or sets the tick the player may grunt at being hit again — <c>0x4637C0</c>'s per-human gate.</summary>
    public int PlayerHitQuietUntil { get; set; }

    /// <summary>Gets or sets a value indicating whether the soldiers update inside the level walk.</summary>
    public bool SoldiersInWalk { get; set; }

    /// <summary>Gets or sets a value indicating whether the player updates inside the level walk.</summary>
    public bool PlayerInWalk { get; set; }

    /// <summary>Gets or sets a value indicating whether the player's update has run this tick.</summary>
    public bool PlayerTicked { get; set; }

    /// <summary>Gets or sets a value indicating whether the player's update advanced the interactions itself this tick.</summary>
    public bool InteractionsTickedByPlayer { get; set; }

    /// <summary>Gets or sets what the soldiers read the player as, asked each tick.</summary>
    public Func<SoldierEnemy>? CurrentEnemy { get; set; }

    /// <summary>Gets or sets the level's placed objects as the rays and the bodies collide with them.</summary>
    public ObjectCollisionIndex? Solids { get; set; }

    /// <summary>Gets or sets the trace of the objects and the terrain together.</summary>
    public WorldTraceCast? Trace { get; set; }

    /// <summary>Gets or sets what blood and debris are traced against: the placed objects and the terrain.</summary>
    public WorldRayCast? EffectWalls { get; set; }

    /// <summary>Gets the missiles the vehicles have fired.</summary>
    public MissileSimulation VehicleMissiles { get; } = new();

    /// <summary>Gets or sets the vehicle whose gun's round is being cast, so the cast passes through its own hull.</summary>
    public GameTask? VehicleRoundOwner { get; set; }

    /// <summary>Gets or sets the level's fans and radar heads, which turn a step each tick.</summary>
    public IPlacementSpin? Spins { get; set; }

    /// <summary>Gets or sets the guards' rounds this mission, once the brains are animated.</summary>
    public GuardRounds? GuardRounds { get; set; }

    /// <summary>Gets or sets the soldiers' simulation: their brains, bodies and rounds, or <see langword="null"/> before a level has any.</summary>
    public SoldierSimulation? Soldiers { get; set; }

    /// <summary>Gets or sets the player's rig: its body, eyes, weapons and the impacts its rounds made, or <see langword="null"/> before a level places one.</summary>
    public PlayerCamera? Player { get; set; }

    /// <summary>Gets or sets the title's magic object table: what a model is, collides as and breaks into.</summary>
    public MagicObjectRegistry? MagicObjects { get; set; }

    /// <summary>Gets or sets the title's vehicle physics table.</summary>
    public VehiclePhysicsRegistry? VehiclePhysics { get; set; }

    /// <summary>Gets or sets the title's human AI table.</summary>
    public HumanAiConfigRegistry? AiConfig { get; set; }

    /// <summary>Gets or sets the title's game materials, which say what a surface sounds and marks like.</summary>
    public GameMaterialRegistry? Materials { get; set; }

    /// <summary>Gets or sets the title's ammunition table.</summary>
    public AmmoTypeRegistry? AmmoTypes { get; set; }

    /// <summary>Gets or sets the title's weapon table.</summary>
    public WeaponTypeRegistry? WeaponTypes { get; set; }

    /// <summary>Gets or sets the generator the guards' rounds draw their spread from.</summary>
    public Random ShotRandom { get; set; } = null!;

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
}
