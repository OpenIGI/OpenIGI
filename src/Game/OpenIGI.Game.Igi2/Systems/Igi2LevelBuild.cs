using OpenIGI.Engine.Tasks;
using OpenIGI.Game.Igi2.Flow;
using OpenIGI.Game.Igi2.Humans;
using OpenIGI.Game.Igi2.Player;
using OpenIGI.Game.Igi2.Scripting;
using OpenIGI.Game.Level;
using OpenIGI.Game.Player;
using OpenIGI.Scripting.Runtime;

namespace OpenIGI.Game.Igi2.Systems;

/// <summary>
/// An I.G.I. 2 level being brought up: the request and the player it is for, and what each of its systems leaves for the ones
/// after it.
/// </summary>
/// <remarks>
/// The lists here fill as the systems run, in their order: the things the player can activate, the things that move, the
/// guns, turrets and cars, and the level's tasks by id. A system reads only what an earlier one has left; see
/// <see cref="Igi2LevelSystems"/> for the order.
/// </remarks>
public sealed class Igi2LevelBuild : LevelBuild
{
    /// <summary>Initializes a new instance of the <see cref="Igi2LevelBuild"/> class.</summary>
    /// <param name="request">The level and the session.</param>
    /// <param name="player">The player the level is brought up for.</param>
    /// <param name="level">The level's run.</param>
    /// <param name="natives">The script namespace the level's expressions run in.</param>
    /// <param name="functions">The script functions the level's text calls.</param>
    /// <param name="playerTask">The level's player task.</param>
    /// <param name="models">The level's models by name.</param>
    public Igi2LevelBuild(
        in TitlePlayerRequest request,
        Igi2TitlePlayer player,
        Igi2LevelRun level,
        NativeRegistry natives,
        Igi2ScriptFunctions functions,
        GameTask playerTask,
        Func<string, Formats.World.MefModel?> models)
        : base(level, request.Services)
    {
        Request = request;
        Player = player ?? throw new ArgumentNullException(nameof(player));
        Level = level;
        Natives = natives ?? throw new ArgumentNullException(nameof(natives));
        Functions = functions ?? throw new ArgumentNullException(nameof(functions));
        PlayerTask = playerTask ?? throw new ArgumentNullException(nameof(playerTask));
        Models = models ?? throw new ArgumentNullException(nameof(models));
        Magic = request.Services.TryGet(out World.Igi2MagicObjTypes? magic) ? magic : null;

        // What every task of the level may be handed from the start (the Services system binds them).
        LevelServices.Provide(models);
        LevelServices.Provide(level);
        LevelServices.Provide(player.Sounds);
        if (player.Explosions is { } explosions)
        {
            LevelServices.Provide(explosions);
        }
    }

    /// <summary>Gets the level and the session.</summary>
    public TitlePlayerRequest Request { get; }

    /// <summary>Gets the player.</summary>
    public Igi2TitlePlayer Player { get; }

    /// <summary>Gets the level's run.</summary>
    public Igi2LevelRun Level { get; }

    /// <summary>Gets the script namespace.</summary>
    public NativeRegistry Natives { get; }

    /// <summary>Gets the script functions.</summary>
    public Igi2ScriptFunctions Functions { get; }

    /// <summary>Gets the level's player task, which the player is bound to.</summary>
    public GameTask PlayerTask { get; }

    /// <summary>Gets the level's models by name.</summary>
    public Func<string, Formats.World.MefModel?> Models { get; }

    /// <summary>Gets the magic object types, when the session loaded them.</summary>
    public World.Igi2MagicObjTypes? Magic { get; }

    /// <summary>Gets or sets the level's sectors and their portals.</summary>
    public World.Igi2SectorGraph? Sectors { get; set; }

    /// <summary>Gets or sets what finds the room a point stands in, over <see cref="Sectors"/>.</summary>
    public World.Igi2SectorLocator? SectorLocator { get; set; }

    /// <summary>Gets or sets what hears the level: the game's camera, inside its sector.</summary>
    public Audio.Igi2CameraListener? Listener { get; set; }

    /// <summary>Gets the ambient areas, in the tree's order.</summary>
    public List<Audio.Igi2AmbientAreaTask> Ambience { get; } = [];

    /// <summary>Gets what the player can activate, in the order it is found.</summary>
    public List<Interaction.IIgi2Activatable> Activatables { get; } = [];

    /// <summary>Gets what moves the bodies on it.</summary>
    public List<Interaction.IIgi2Mover> Movers { get; } = [];

    /// <summary>Gets or sets what the detectors see, once made.</summary>
    public Detection.Igi2DetectionWorld? Sight { get; set; }

    /// <summary>Gets the list every human tells its place to, <c>Human_Notify</c>'s.</summary>
    public Weapons.Igi2NotifyList Notified { get; } = new();

    /// <summary>Gets the mounted guns.</summary>
    public List<Weapons.Igi2StationaryGunTask> Guns { get; } = [];

    /// <summary>Gets the automatic guns.</summary>
    public List<Weapons.Igi2AiStationaryGunHolderTask> Turrets { get; } = [];

    /// <summary>Gets the cars.</summary>
    public List<Vehicles.Igi2CarTask> Cars { get; } = [];

    /// <summary>Gets the level's tasks by id, the humans standing for their own: what a script or a minefield names.</summary>
    public Dictionary<int, object> PlacedById { get; } = [];

    /// <summary>Gets the session's AI, when it has one.</summary>
    public Ai.Runtime.Igi2AiWorld? SessionAi => Services.TryGet(out Ai.Runtime.Igi2AiWorld? ai) ? ai : null;

    /// <summary>
    /// Gets every human of the level in <c>Human_ptQTaskList</c>'s order: each registers as it is made (<c>Human_CreateHandler</c>),
    /// so in the tree's order, the player where its <c>HumanPlayer</c> task stands.
    /// </summary>
    /// <returns>The humans.</returns>
    public List<Igi2Human> LevelHumans() => Igi2Gameplay.LevelHumans(Root, Player);
}
