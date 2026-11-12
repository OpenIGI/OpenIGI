using OpenIGI.Core.Diagnostics;
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
public sealed class Igi1Gameplay : IHostedMission, IShotTallySource, OpenIGI.Game.Multiplayer.IReplicatedTitle
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
        Harm = new Igi1PlayerHarm(World, PlayerSurroundings);
        Blasts = new Igi1Blasts(World, Harm, Audio, Impacts, host);
        World.TouchGlass = Blasts.TouchGlass;
        World.RoundContact = Blasts.ApplyRoundContact;
        World.PlayerEvents = new Igi1PlayerEvents(World, Harm);
        Setup = new Igi1MissionSetup(World, Harm, Blasts, Sounds, Audio, Impacts, host);
        Tick = new Igi1MissionTick(World, Harm, Blasts, Audio, host);
        ComputerWorld = new OpenIGI.Game.Igi1.Computer.Igi1ComputerWorld(World, Audio);
        World.Computer = new OpenIGI.Game.Igi1.Computer.Igi1Computer(ComputerWorld);
    }

    /// <summary>Gets the played mission's world.</summary>
    public MissionWorld World { get; } = new();

    /// <summary>Gets what hurts a player of the mission -- its own or a co-op partner -- and how it dies.</summary>
    public Igi1PlayerHarm Harm { get; }

    /// <summary>Gets what is kept of the save games across a restart: the slot a load asked for.</summary>
    public OpenIGI.Game.Saves.SaveSession SaveSession { get; } = new();

    /// <summary>Gets or sets the save games of the level that is up, made once it is whole.</summary>
    public Saves.Igi1MissionSaves? Saves { get; set; }

    /// <inheritdoc/>
    /// <remarks>
    /// None while they are switched off (<c>g_igi1_saves</c>), and none in a multiplayer session of any kind -- co-op, a
    /// versus match, a server, a demo played back -- whose level is the server's to keep
    /// (<see cref="OpenIGI.Game.Saves.SaveGameLock"/>): not for its host either, and not while it waits alone for others.
    /// </remarks>
    public ITitleSaveGames? SaveGames =>
        GameplayCVars.Igi1Saves.Value && !OpenIGI.Game.Saves.SaveGameLock.IsHeld && !World.CoopClient && World.NetworkPlayers.Count == 0
            ? Saves
            : null;

    /// <inheritdoc/>
    public string Map => World.MissionId.ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <inheritdoc/>
    /// <remarks>The title outlives its missions; each load builds a new level tree, and between two there is none.</remarks>
    public object? Level => World.LevelRoot;


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
        if (World.Player is not { } rig)
        {
            return null;
        }

        // The title player is the mission's own rig and life; what hurts it stays the mission's (Harm), which reaches a
        // session's other players too.
        return new Igi1TitlePlayer(new Igi1HumanPlayer(rig, World.PlayerLife, PlayerSurroundings), request.Built.Ground)
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
    public Igi1PlayerSetup NewPlayerSetup() => PlayerSetup = new(World, Harm, Audio, Sounds, PlayerAnimations, _host);

    /// <summary>Records the level as it stands, for <see cref="ResetToLevelStart"/>: as a match's session takes it up.</summary>
    public void MarkLevelStart() => World.MarkLevelStart();

    /// <summary>
    /// Puts the level back as <see cref="MarkLevelStart"/> found it, without loading anything: the world's parts, and what play
    /// left in it -- every grenade, mine, rocket and round in flight, every bullet hole and scorch, every burst still burning.
    /// The players themselves are the match's to put back.
    /// </summary>
    public void ResetToLevelStart()
    {
        World.ResetToLevelStart();
        World.OfferedPrompt = null;
        foreach (Player.PlayerCamera rig in Rigs())
        {
            rig.Grenades.Clear();
            rig.Missiles.Clear();
            rig.M203.Clear();
            rig.Marks.Clear();
            rig.ControlTaken = false;
        }

        ClearWhatPlayLeft();
    }

    /// <summary>
    /// Makes the level that is up ready for a save game to be read onto it, once the save has checked (I.G.I. 2's
    /// <c>Game_LoadFromSlot</c> deletes what a load makes again the same way): the player stands again if the level had killed
    /// it; every soldier is made afresh, so its dead stand and no mind keeps the fight it was in
    /// (<c>HumanAI_DeSerializeHandler</c> starts each mind afresh too); and what play left in the level that no save keeps --
    /// rockets in flight, bullet holes, bursts still burning -- goes. Each part a save keeps then reads itself back.
    /// </summary>
    /// <remarks>Not retail: Project I.G.I. kept no save games.</remarks>
    public void MakeReadyForSave()
    {
        if (World.Player is { } rig && (World.IsPlayerDead || World.PlayerLife.Health.IsDead))
        {
            Harm.RevivePlayer(at: null, rig.Body.Yaw);
        }

        // The task's own death latch (Human+3317), which the level's scripts read: a save is never made of a dead player.
        World.PlayerTask?.IsDead = false;
        World.Soldiers?.RemakeForLoad();
        World.OfferedPrompt = null;
        ClearWhatPlayLeft();
    }

    // What play left in the level that none of its parts keeps: the eye and controls a mod had taken, the rockets in flight,
    // every impact and every burst still burning.
    private void ClearWhatPlayLeft()
    {
        // A round starts with the player's own eye and controls, whatever a mod had taken in the last one.
        World.ViewTakeover = null;
        World.ShownProjectiles.Clear();
        World.VehicleMissiles.Clear();
        Impacts.Runtime.Clear();
        _host.LevelWasReset();
    }

    // Every player's rig: the mission's own and each partner's, whose weapons leave their marks in the level.
    private IEnumerable<Player.PlayerCamera> Rigs()
    {
        if (World.Player is { } own)
        {
            yield return own;
        }

        foreach (Multiplayer.Igi1NetworkPlayer partner in World.NetworkPlayers)
        {
            yield return partner.Player;
        }
    }

    /// <inheritdoc/>
    public OpenIGI.Game.Multiplayer.IReplicatedWorld CreateReplicatedWorld(bool includesHost) =>
        new Multiplayer.Igi1ReplicatedWorld(this) { IncludesHost = includesHost };

    /// <inheritdoc/>
    public OpenIGI.Game.Multiplayer.IReplicatedClientWorld CreateClientWorld() => new Multiplayer.Igi1ClientWorld(this);

    /// <summary>Gets the bring-up of the mission's player, once made, which a co-op session's other players are made by.</summary>
    public Igi1PlayerSetup? PlayerSetup { get; private set; }


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
            IMissionSetupHost setup = _host;
            IOpenIgiLogger logger = setup.Logger;
            _animations = read = (root, setup.Work.Run(() => Igi1PlayerAnimations.Read(root, logger)));
        }

        return read.Sets;
    }

    // The animation sets, by the installation they were read from.
    private (string Root, Task<Igi1PlayerAnimations> Sets)? _animations;
}
