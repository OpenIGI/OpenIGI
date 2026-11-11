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
public class MissionState
{
    /// <summary>Gets the parts of the level a match's rounds put back, in the order they go back.</summary>
    /// <remarks>
    /// Lifts before doors, so a door riding a car is re-placed against the car where it stands again. A title with parts of
    /// its own adds them (<see cref="ILevelReset"/>); every part listed here that is <see langword="null"/> is skipped.
    /// </remarks>
    protected virtual IEnumerable<ILevelReset?> LevelParts => [];

    /// <summary>Records the whole level as it stands now, as what <see cref="ResetToLevelStart"/> puts back.</summary>
    public void MarkLevelStart()
    {
        foreach (ILevelReset? part in LevelParts)
        {
            part?.MarkLevelStart();
        }
    }

    /// <summary>Puts the whole level back as <see cref="MarkLevelStart"/> found it, without loading anything.</summary>
    public void ResetToLevelStart()
    {
        foreach (ILevelReset? part in LevelParts)
        {
            part?.ResetToLevelStart(Solids);
        }
    }

    private LevelCollision _collision = LevelCollision.Empty;

    /// <summary>Gets the mission's objects as the picture asks after them, or <see langword="null"/> for a mission with none of its own.</summary>
    public virtual IPlacementObjects? Placements => null;

    /// <summary>Gets which of their models the mission's own objects make placements wear, or <see langword="null"/> when none change.</summary>
    public virtual IPlacementVariant? PlacementVariants => null;

    /// <summary>Gets the mission's cut-scenes, or <see langword="null"/> before they are built.</summary>
    public virtual Hosting.IMissionCutscenes? Scenes => null;

    /// <summary>Gets the mission's objectives and status messages, or <see langword="null"/> before they are built.</summary>
    public virtual Hosting.IMissionObjectives? Progress => null;

    /// <summary>Gets the weapons lying in the level, or <see langword="null"/> before they are built.</summary>
    public virtual IReadOnlyList<GunPickup>? LyingGuns => null;

    /// <summary>Gets the security cameras whose status lights are drawn, or <see langword="null"/> when the level has none.</summary>
    public virtual IReadOnlyList<SecurityCamera>? StatusCameras => null;

    /// <summary>Gets the flares the level's turning alarm lights throw at an eye.</summary>
    /// <param name="eyeUnits">Where the eye is, in engine units.</param>
    /// <param name="tickFraction">How far the frame is between two ticks.</param>
    /// <returns>The flares; none for a level without alarm lights.</returns>
    public virtual IReadOnlyList<WorldGlow> AlarmFlares(Vector3d eyeUnits, float tickFraction) => [];

    /// <summary>Drops what the soldiers' bring-up made of the level's alarms and what can be destroyed, as a mission is left.</summary>
    public virtual void ReleaseAlarmsAndDestructibles()
    {
    }

    /// <summary>
    /// Gets or sets whether an intro skipped at load (no cinematics, or <c>OPENIGI_BREAK_CUTSCENE</c>) is still playing
    /// through the ticks whose walk reads the break and closes it; until it has stopped, nothing of it is drawn or heard.
    /// </summary>
    public bool IntroSkipPending { get; set; }

    /// <summary>Gets the cutscene frame to present: none while a skipped intro is still closing.</summary>
    public virtual CutsceneFrame? CutsceneFrameShown => null;

    /// <summary>Gets or sets the difficulty the player's damage is scaled by.</summary>
    public int PlayerDifficulty { get; set; }

    /// <summary>Gets or sets a value indicating whether the player has died.</summary>
    public virtual bool IsPlayerDead { get; set; }

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

    /// <summary>Gets the explosion targets and humans another game made for the level, which join its lists after its own.</summary>
    public LevelJoiners Joined { get; } = new();

    /// <summary>
    /// Gets or sets a value indicating whether the level lets the menus open: its scripts' <c>MenuManager_SetEnabled</c>, which
    /// puts the manager in its do-nothing state (<c>0x4179B0</c> sets 16; <c>0x418080</c> runs nothing there) until it is enabled
    /// again and goes idle (17). Project I.G.I.'s levels disable it for their cinematics.
    /// </summary>
    public bool MenusEnabled { get; set; } = true;

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
                || !ReferenceEquals(_collision.Ground, Ground)
                || _collision.TerrainMaterialRow != TerrainMaterialRow)
            {
                _collision = new LevelCollision(Solids, Terrain, Ground, TerrainMaterialRow);
            }

            return _collision;
        }
    }

    /// <summary>Gets or sets the game-material row the level's terrain is made of, which an impact on open ground marks as.</summary>
    public int? TerrainMaterialRow { get; set; }

    /// <summary>Gets the trace of the objects and the terrain together (<see cref="LevelCollision.Trace"/>).</summary>
    public WorldTraceCast? Trace => Collision.Trace;

    /// <summary>Gets what blood and debris are traced against (<see cref="LevelCollision.EffectWalls"/>).</summary>
    public WorldRayCast? EffectWalls => Collision.EffectWalls;

    /// <summary>Gets what blood droplets fall onto (<see cref="LevelCollision.DropletWalls"/>).</summary>
    public WorldRayCast? DropletWalls => Collision.DropletWalls;

    /// <summary>Gets the missiles the vehicles have fired.</summary>
    public MissileSimulation VehicleMissiles { get; } = new();

    /// <summary>Gets or sets the vehicle whose gun's round is being cast, so the cast passes through its own hull.</summary>
    public GameTask? VehicleRoundOwner { get; set; }

    /// <summary>
    /// Gets or sets what a round landing on one of the level's placed objects does there, as the mission's own game answers it:
    /// where it landed, the placed task and placement struck, and the round's damage. Another game's rounds hand their
    /// contacts to it, so a camera, a pane or a barrel they hit breaks as one the mission's own player shot would.
    /// </summary>
    public Action<Vector3d, GameTask?, WorldPlacement?, float>? RoundContact { get; set; }

    /// <summary>Gets or sets what finds a model by the name level data gives it, as the level's walk did; null when the title offers none.</summary>
    public Func<string, WorldModelBinding?>? Models { get; set; }

    /// <summary>Gets or sets what plays a skinned model in the level's clips, for a viewer; null when the title offers none.</summary>
    public Func<OpenIGI.Formats.World.MefModel, string?, IModelAnimator?>? Animators { get; set; }

    /// <summary>Gets or sets what plays an animation file on a body of the level, for a viewer; null when the title offers none.</summary>
    public Func<ReadOnlyMemory<byte>, AnimationFilePreview?>? AnimationFiles { get; set; }

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

    /// <summary>Drops the level's runtimes as the mission is left: the objectives, the cutscenes, the walk and the cameras.</summary>
    public virtual void ReleaseRuntimes()
    {
        IntroSkipPending = false;
        SoldiersInWalk = false;
        PlayerInWalk = false;
        ElevatorsInWalk = false;
        CamerasInWalk = false;
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

    /// <summary>Starts the player's life afresh at a difficulty: alive, and what the title keeps of its life reset.</summary>
    /// <param name="difficulty">The profile difficulty the player's own hits are scaled by.</param>
    public virtual void ResetPlayerLife(int difficulty)
    {
        PlayerDifficulty = difficulty;
        IsPlayerDead = false;
    }

    /// <summary>Starts a fresh life at the same difficulty: the last one's death does not carry over.</summary>
    public virtual void StartFreshLife() => IsPlayerDead = false;

    /// <summary>
    /// Gets a value indicating whether the mission has a player of its own standing: the one its game's level places, rather than
    /// another game's standing in it or none.
    /// </summary>
    public virtual bool HasOwnPlayer => false;

    /// <summary>Gets the rockets the mission's own player has fired, in the air; <see langword="null"/> without a player of its own.</summary>
    public virtual Weapons.MissileSimulation? PlayerMissiles => null;

    /// <summary>Holds the mission's own player in its computer, or lets it go, as the computer opens and closes.</summary>
    /// <param name="open">Whether the computer is open.</param>
    public virtual void SetPlayerComputerOpen(bool open)
    {
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
        Joined.Clear();
        MenusEnabled = true;
        TerrainMaterialRow = null;
        Solids = null;
        LevelRoot = null;
        Materials = null;
        VehicleMissiles.Clear();
    }
}
