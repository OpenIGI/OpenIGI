using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;
using OpenIGI.Engine.Titles;
using OpenIGI.Game.Ai;
using OpenIGI.Game.Player;
using OpenIGI.Game.Weapons;

namespace OpenIGI.Game.World;

/// <summary>
/// The played mission's world, as the host keeps it for every title: the level's registries of what can be opened,
/// ridden, picked up, seen and blown up, its cutscene and objective runtimes, its collision, and the player's life.
/// </summary>
/// <remarks>
/// Each is <see langword="null"/> until the mission system that makes it has run (see the host's <c>MissionSystemNames</c>), and
/// is dropped again as the mission is released. Held in one place so that what reads the level -- the inspector, the
/// sounds, the effects -- is handed the mission rather than the whole host. A title with a simulation of its own extends it.
/// </remarks>
public class MissionState : IInspectableMission
{
    private LevelCollision _collision = LevelCollision.Empty;

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

    /// <summary>Gets or sets the objective runtime.</summary>
    public MissionObjectiveRuntime? Objectives { get; set; }

    /// <summary>Gets or sets the difficulty the player's damage is scaled by.</summary>
    public int PlayerDifficulty { get; set; }

    /// <summary>Gets or sets the player's full health.</summary>
    public float PlayerMaximumHealth { get; set; }

    /// <summary>Gets or sets how far the player's body animations carry it, the human type's delta translation scale.</summary>
    public float PlayerDeltaTranslationScale { get; set; } = HumanMotion.DefaultDeltaTranslationScale;

    /// <summary>Gets or sets the player's health.</summary>
    public HumanHealth PlayerHealth { get; set; } = null!;

    /// <summary>Gets or sets a value indicating whether the player has died.</summary>
    public bool IsPlayerDead { get; set; }

    /// <summary>Gets or sets the tick the player may grunt at being hit again — <c>0x4637C0</c>'s per-human gate.</summary>
    public int PlayerHitQuietUntil { get; set; }

    /// <summary>Gets or sets a value indicating whether the soldiers update inside the level walk.</summary>
    public bool SoldiersInWalk { get; set; }

    /// <summary>Gets or sets a value indicating whether the lifts update inside the level walk.</summary>
    public bool ElevatorsInWalk { get; set; }

    /// <summary>Gets or sets a value indicating whether the security cameras and their controls update inside the level walk.</summary>
    public bool CamerasInWalk { get; set; }

    /// <summary>Gets or sets a value indicating whether the player updates inside the level walk.</summary>
    public bool PlayerInWalk { get; set; }

    /// <summary>Gets or sets a value indicating whether the player's update has run this tick.</summary>
    public bool PlayerTicked { get; set; }

    /// <summary>Gets or sets a value indicating whether the player's update advanced the interactions itself this tick.</summary>
    public bool InteractionsTickedByPlayer { get; set; }

    /// <summary>Gets or sets the level's placed objects as the rays and the bodies collide with them.</summary>
    public ObjectCollisionIndex? Solids { get; set; }

    /// <summary>Gets the humans the level's shots may meet, from every source: the game's own soldiers, and any a mod brings.</summary>
    public LevelBodies Bodies { get; } = new();

    /// <summary>Gets or sets the level's task tree, in the order its tasks were made.</summary>
    public LevelTask? LevelRoot { get; set; }

    /// <summary>Gets or sets the mission's terrain when it is live data to stand on and trace, or <see langword="null"/>.</summary>
    /// <remarks>Mission 14 has none: it is entirely inside placed building and tunnel models, whose floors are walked on.</remarks>
    public TerrainData? Terrain { get; set; }

    /// <summary>Gets or sets the ground the player stands on: the walk's own when the game keeps one, else the terrain.</summary>
    public IGroundHeightField? Ground { get; set; }

    /// <summary>Gets the level's walls and ground as one collision: the placed objects and the terrain, the nearer answering.</summary>
    /// <remarks>Made again only when one of the three it is built from changes, so it can be asked every frame.</remarks>
    public LevelCollision Collision
    {
        get
        {
            if (!ReferenceEquals(_collision.Solids, Solids)
                || !ReferenceEquals(_collision.Terrain, Terrain)
                || !ReferenceEquals(_collision.Ground, Ground))
            {
                _collision = new LevelCollision(Solids, Terrain, Ground);
            }

            return _collision;
        }
    }

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

    /// <summary>Gets or sets the title's magic object table: what a model is, collides as and breaks into.</summary>
    public MagicObjectRegistry? MagicObjects { get; set; }

    /// <summary>Gets or sets the title's vehicle physics table.</summary>
    public VehiclePhysicsRegistry? VehiclePhysics { get; set; }

    /// <summary>Gets or sets the title's game materials, which say what a surface sounds and marks like.</summary>
    public GameMaterialRegistry? Materials { get; set; }

    /// <summary>Gets or sets the title's ammunition table.</summary>
    public AmmoTypeRegistry? AmmoTypes { get; set; }

    /// <summary>Gets or sets the title's weapon table.</summary>
    public WeaponTypeRegistry? WeaponTypes { get; set; }

    /// <summary>Gets or sets the generator the guards' rounds draw their spread from.</summary>
    public Random ShotRandom { get; set; } = null!;

    /// <summary>Gets the engine's own generator, for the sound-variant draws <c>0x4638A0</c> makes and the other draws that share its stream.</summary>
    public IgiRandom SoundRandom { get; } = new();

    /// <summary>Gets or sets the red wash a hit on the player raises, which the HUD draws.</summary>
    public HumanDamageFlash DamageFlash { get; set; } = null!;

    /// <summary>Drops the level's runtimes as the mission is left: the objectives, the cutscenes, the walk and the cameras.</summary>
    public void ReleaseRuntimes()
    {
        Objectives?.Release();
        Objectives = null;
        Cutscenes = null;
        SoldiersInWalk = false;
        PlayerInWalk = false;
        ElevatorsInWalk = false;
        CamerasInWalk = false;
        SecurityCameras = null;
    }

    /// <summary>Takes the tables the running title publishes.</summary>
    /// <param name="services">The title session's services.</param>
    /// <remarks>
    /// Asked for, not required: these are tables one game publishes and the other does not, and the previous game's must
    /// not be left standing when this one has none.
    /// </remarks>
    public virtual void AdoptTitleServices(IServiceCatalog services)
    {
        ArgumentNullException.ThrowIfNull(services);
        MagicObjects = services.TryGet(out MagicObjectRegistry? magic) ? magic : null;
        VehiclePhysics = services.TryGet(out VehiclePhysicsRegistry? vehicles) ? vehicles : null;
    }

    /// <summary>Starts the player's life afresh: full health, alive, and no wash on the screen.</summary>
    /// <param name="difficulty">The profile difficulty the player's own hits are scaled by.</param>
    public virtual void ResetPlayerLife(int difficulty)
    {
        // The player's own damage. Its zone table is the player's, whose head is worth 1.0 rather than the
        // soldiers' 100 -- humanplayer.qvm overrides the executable's default (weapons.md 11). The player's
        // own hit handler (0x412440) additionally scales every hit by the profile difficulty: x0.75 easy,
        // x1.0 normal, x1.25 hard -- a scale that applies to the PLAYER only, never to a soldier.
        PlayerDifficulty = difficulty;

        // 0x489C30 supplies the universal base value first. Mission loading replaces it with argument 7
        // from humanplayer.qvm, just as HumanPlayer's constructor overwrites Human+600 at 0x4104E1. Do
        // not bake in the shipped script's 3.0: an externally retuned humanplayer.qvm owns this range.
        PlayerMaximumHealth = HumanHitZoneTable.MaximumHealth;
        PlayerHealth = new(HumanHitZoneTable.Player, PlayerMaximumHealth)
        {
            DamageScale = HumanHealth.DifficultyDamageScale(PlayerDifficulty),
        };
        IsPlayerDead = false;

        // The red wash the screen takes on a hit -- four corner alphas that decay a forty-fifth a tick
        // (sub_411300) and paint a screen-filling red quad (HumanPlayer_DrawDamageFlash). It survives the
        // player's death, so the corpse's-eye view stays red rather than snapping back to a clean picture.
        DamageFlash = new();
    }

    /// <summary>Starts a fresh life at the same difficulty: the last one's death and its wash do not carry over.</summary>
    public virtual void StartFreshLife()
    {
        PlayerHealth = new(HumanHitZoneTable.Player, PlayerMaximumHealth)
        {
            DamageScale = HumanHealth.DifficultyDamageScale(PlayerDifficulty),
        };
        IsPlayerDead = false;
        DamageFlash = new();
    }

    /// <summary>Forgets the title's own player, as another takes the level or the level goes.</summary>
    public virtual void ForgetPlayer()
    {
    }

    /// <summary>Takes the level's soldiers as the frame draws them; a title with a simulation of its own reads it from them.</summary>
    /// <param name="bodies">The soldiers, or <see langword="null"/> to drop them.</param>
    public virtual void AdoptSoldiers(ISoldierBodies? bodies)
    {
    }

    /// <summary>
    /// Drops what the level put in the world as the mission is left: the collision and the registries, and the rockets the
    /// vehicles fired, which must not keep flying into the next run.
    /// </summary>
    public virtual void ReleaseWorld()
    {
        Bodies.Clear();
        EffectWalls = null;
        Solids = null;
        LevelRoot = null;
        Destructibles = null;
        Materials = null;
        Doors = null;
        Elevators = null;
        Interactions = null;
        GunPickups = null;
        GenericPickups = null;
        VehicleMissiles.Clear();
    }
}
