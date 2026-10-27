using OpenIGI.Core.Diagnostics;
using OpenIGI.Engine.Titles;
using OpenIGI.Game.Hosting;
using OpenIGI.Game.Igi1.Player;
using OpenIGI.Game.Level;
using OpenIGI.Game.World;
using OpenIGI.Game.Player;

namespace OpenIGI.Game.Igi1.World;

/// <summary>
/// Project I.G.I.'s mission as the host runs it: its world, how it is brought up, how each tick runs, what hurts the player
/// and how the blasts resolve.
/// </summary>
public sealed class Igi1Gameplay : IHostedMission, IShotTallySource, ITitleGameplay
{
    private readonly IMissionHostServices _host;

    /// <summary>Initializes a new instance of the <see cref="Igi1Gameplay"/> class.</summary>
    /// <param name="host">What shows and plays it.</param>
    public Igi1Gameplay(IMissionHostServices host)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
        Sounds = new Igi1HumanSounds(new Igi1MissionSoundStage(World, host));
        Audio = new Igi1MissionAudio(World, host);
        Impacts = new Igi1ImpactEffects(World, host);
        Impacts.Explosions.Sound = (sound, at, priority) => host.PlayDefinition(sound, at, priority);
        PlayerSurroundings = new Igi1MissionPlayerSurroundings(World, Sounds, Audio, host);
        Blasts = new Igi1Blasts(World, Audio, Impacts, host);
        World.TouchGlass = Blasts.TouchGlass;
        World.RoundContact = Blasts.ApplyRoundContact;
        Setup = new Igi1MissionSetup(World, Blasts, Sounds, Audio, Impacts, host);
        Tick = new Igi1MissionTick(World, Blasts, Audio, host);
        ComputerWorld = new OpenIGI.Game.Igi1.Computer.Igi1ComputerWorld(World, Audio);
        World.Computer = new OpenIGI.Game.Igi1.Computer.Igi1Computer(ComputerWorld);
    }

    /// <summary>Gets the played mission's world.</summary>
    public MissionWorld World { get; } = new();


    /// <inheritdoc/>
    public ShotTally? PlayerShots => World.Player?.Shots;

    /// <summary>Gets what the mission's player is hurt and dies in.</summary>
    public Igi1MissionPlayerSurroundings PlayerSurroundings { get; }

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
    /// <remarks>None of its own: the player reads the host's actions.</remarks>
    public IReadOnlyList<TitleInputBinding> InputBindings => [];

    /// <inheritdoc/>
    /// <remarks>
    /// The mission's own player on the host's title path: the player the mission stood, ticked inside the mission's steps around
    /// it (<see cref="Igi1MissionTick.StepPlayer"/>). A tick the mission refuses -- the player dead, the mission failed -- is not
    /// the player's, so nothing it would have set in motion is resolved. With no player stood, none.
    /// </remarks>
    public ITitlePlayer? CreatePlayer(in TitlePlayerRequest request)
    {
        if (World.Human is not { } human || World.Player is null)
        {
            return null;
        }

        return new Igi1TitlePlayer(human, request.Built.Ground)
        {
            Step = tick =>
            {
                bool stepped = Tick.StepPlayer(tick);
                World.PlayerTicked &= stepped;
                return stepped;
            },
        };
    }

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
    public Igi1PlayerSetup NewPlayerSetup() => new(World, PlayerSurroundings, Audio, Sounds, PlayerAnimations, _host);

    /// <summary>
    /// Gets the two animation sets every mission stands its player with, its body's (skeleton 0) and its first-person rig's
    /// (skeleton 5), read once for the installation being played: waited for here when <see cref="Prefetch"/> has not finished.
    /// </summary>
    /// <remarks>
    /// They live under common/ rather than under any level, so they are the same 2.4 MB of IFF for every mission of one
    /// installation, and parsing them was the single largest thing standing a player up used to do on the render thread. A
    /// switch of installation reads the new one's: a set left over from another would send its missions down a path theirs
    /// do not take.
    /// </remarks>
    public Igi1PlayerAnimations PlayerAnimations => ReadPlayerAnimations().GetAwaiter().GetResult();

    /// <inheritdoc/>
    public void Prefetch() => _ = ReadPlayerAnimations();

    // The read for the installation being played, started once for it.
    private Task<Igi1PlayerAnimations> ReadPlayerAnimations()
    {
        string root = _host.GamePath;
        if (_animations is not { } read || !string.Equals(read.Root, root, StringComparison.OrdinalIgnoreCase))
        {
            IOpenIgiLogger logger = ((IMissionSetupHost)_host).Logger;
            _animations = read = (root, Task.Run(() => Igi1PlayerAnimations.Read(root, logger)));
        }

        return read.Sets;
    }

    // The animation sets, by the installation they were read from.
    private (string Root, Task<Igi1PlayerAnimations> Sets)? _animations;
}
