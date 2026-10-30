using OpenIGI.Game.Hosting;
using OpenIGI.Game.Level;
using OpenIGI.Game.World;
using OpenIGI.Game.Player;

namespace OpenIGI.Game.Igi1.World;

/// <summary>
/// Project I.G.I.'s mission as the host runs it: its world, how it is brought up, how each tick runs, what hurts the player
/// and how the blasts resolve.
/// </summary>
public sealed class Igi1Gameplay : IHostedMission, IShotTallySource, OpenIGI.Game.Multiplayer.IReplicatedTitle
{
    private readonly IMissionHostServices _host;

    /// <summary>Initializes a new instance of the <see cref="Igi1Gameplay"/> class.</summary>
    /// <param name="host">What shows and plays it.</param>
    public Igi1Gameplay(IMissionHostServices host)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
        Sounds = new Igi1HumanSounds(World, host);
        Audio = new Igi1MissionAudio(World, host);
        Impacts = new Igi1ImpactEffects(World, host);
        Impacts.Explosions.Sound = (sound, at, priority) => host.PlayDefinition(sound, at, priority);
        Harm = new Igi1PlayerHarm(World, Sounds, Audio, host);
        Blasts = new Igi1Blasts(World, Harm, Audio, Impacts, host);
        World.TouchGlass = Blasts.TouchGlass;
        World.RoundContact = Blasts.ApplyRoundContact;
        World.PlayerEvents = new Igi1PlayerEvents(World, Harm, Blasts, Sounds);
        Setup = new Igi1MissionSetup(World, Harm, Blasts, Sounds, Audio, Impacts, host);
        Tick = new Igi1MissionTick(World, Harm, Blasts, Audio, host);
        ComputerWorld = new OpenIGI.Game.Igi1.Computer.Igi1ComputerWorld(World, Audio);
        World.Computer = new OpenIGI.Game.Igi1.Computer.Igi1Computer(ComputerWorld);
    }

    /// <summary>Gets the played mission's world.</summary>
    public MissionWorld World { get; } = new();

    /// <summary>Gets the player the host steers and looks through, or <see langword="null"/> before a level places one.</summary>
    public IHostedPlayer? Player => World.Player;

    /// <inheritdoc/>
    public ShotTally? PlayerShots => World.Player?.Shots;

    /// <summary>Gets what hurts the player and how it dies.</summary>
    public Igi1PlayerHarm Harm { get; }

    /// <summary>Gets the humans' cries and the sounds of rounds landing.</summary>
    public Igi1HumanSounds Sounds { get; }

    /// <summary>Gets the mission heard: every voice its things hold.</summary>
    public Igi1MissionAudio Audio { get; }

    /// <summary>Gets the original's own impact effects: the dust and chips, and the blood.</summary>
    public Igi1ImpactEffects Impacts { get; }

    /// <summary>Gets what brings the mission's runtimes and registries up.</summary>
    public Igi1MissionSetup Setup { get; }

    /// <summary>Gets what resolves the level's blasts and what they break.</summary>
    public Igi1Blasts Blasts { get; }

    /// <summary>Gets what the computer asks of the level, which the presentation gives its fonts and sprites.</summary>
    public OpenIGI.Game.Igi1.Computer.Igi1ComputerWorld ComputerWorld { get; }

    /// <summary>Gets the mission's tick.</summary>
    public Igi1MissionTick Tick { get; }

    /// <inheritdoc/>
    MissionState IHostedMission.World => World;

    /// <inheritdoc/>
    IMissionTick IHostedMission.Tick => Tick;

    /// <inheritdoc/>
    IMissionSetup IHostedMission.Setup => Setup;

    /// <inheritdoc/>
    IMissionAudio IHostedMission.Audio => Audio;

    /// <inheritdoc/>
    IMissionCries IHostedMission.Sounds => Sounds;

    /// <inheritdoc/>
    IMissionImpacts IHostedMission.Impacts => Impacts;

    /// <inheritdoc/>

    /// <inheritdoc/>
    IPlayerBringUp IHostedMission.NewPlayerSetup() => NewPlayerSetup();

    /// <inheritdoc/>
    public void AddMissionSystems(LevelSystems<MissionLevelBuild> systems) => Igi1MissionSystems.AddTo(systems, this, _host);

    /// <summary>Starts making a player for the mission being brought up.</summary>
    /// <returns>The player's setup.</returns>
    public Igi1PlayerSetup NewPlayerSetup() => PlayerSetup = new(World, Harm, Audio, Sounds, _host);

    /// <inheritdoc/>
    public OpenIGI.Game.Multiplayer.IReplicatedWorld CreateReplicatedWorld(bool includesHost) =>
        new Multiplayer.Igi1ReplicatedWorld(this) { IncludesHost = includesHost };

    /// <inheritdoc/>
    public OpenIGI.Game.Multiplayer.IReplicatedClientWorld CreateClientWorld() => new Multiplayer.Igi1ClientWorld(this);

    /// <summary>Gets the bring-up of the mission's player, once made, which a co-op session's other players are made by.</summary>
    public Igi1PlayerSetup? PlayerSetup { get; private set; }
}
