using OpenIGI.Game.World;
using OpenIGI.Game.Player;

namespace OpenIGI.Game.Igi1.World;

/// <summary>Everything the first game's mission asks of whatever shows and plays it.</summary>
public interface IIgi1Host : IIgi1MissionHost, IIgi1PlayerSetupHost, IIgi1PlayerHarmHost, IIgi1MissionSetupHost, IIgi1BlastHost, IIgi1SoundHost
{
}

/// <summary>
/// Project I.G.I.'s mission as the host runs it: its world, how it is brought up, how each tick runs, what hurts the player
/// and how the blasts resolve.
/// </summary>
public sealed class Igi1Gameplay : IMissionGameplay
{
    private readonly IIgi1Host _host;

    /// <summary>Initializes a new instance of the <see cref="Igi1Gameplay"/> class.</summary>
    /// <param name="host">What shows and plays it.</param>
    public Igi1Gameplay(IIgi1Host host)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
        Sounds = new Igi1HumanSounds(World, host);
        Audio = new Igi1MissionAudio(World, host);
        Harm = new Igi1PlayerHarm(World, Sounds, Audio, host);
        Blasts = new Igi1Blasts(World, Harm, Audio, host);
        Setup = new Igi1MissionSetup(World, Harm, Blasts, Sounds, Audio, host);
        Tick = new Igi1MissionTick(World, Harm, Blasts, Audio, host);
    }

    /// <summary>Gets the played mission's world.</summary>
    public MissionWorld World { get; } = new();

    /// <summary>Gets the player the host steers and looks through, or <see langword="null"/> before a level places one.</summary>
    public IHostedPlayer? Player => World.Player;

    /// <summary>Gets what hurts the player and how it dies.</summary>
    public Igi1PlayerHarm Harm { get; }

    /// <summary>Gets the humans' cries and the sounds of rounds landing.</summary>
    public Igi1HumanSounds Sounds { get; }

    /// <summary>Gets the mission heard: every voice its things hold.</summary>
    public Igi1MissionAudio Audio { get; }

    /// <summary>Gets what brings the mission's runtimes and registries up.</summary>
    public Igi1MissionSetup Setup { get; }

    /// <summary>Gets what resolves the level's blasts and what they break.</summary>
    public Igi1Blasts Blasts { get; }

    /// <summary>Gets the mission's tick.</summary>
    public Igi1MissionTick Tick { get; }

    /// <summary>Starts making a player for the mission being brought up.</summary>
    /// <returns>The player's setup.</returns>
    public Igi1PlayerSetup NewPlayerSetup() => new(World, Harm, Audio, _host);
}
