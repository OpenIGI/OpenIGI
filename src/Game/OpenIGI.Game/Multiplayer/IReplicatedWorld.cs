using OpenIGI.Core.Numerics;

namespace OpenIGI.Game.Multiplayer;

/// <summary>What a title gives the server session: its players made and taken out, their commands, and the world each tick.</summary>
/// <remarks>
/// The one seam between the shared session and a game. I.G.I. 2's is its level with a networked human per client
/// (<c>Igi2TitlePlayer.AddNetworkHuman</c>); I.G.I.'s will be its own. The session never sees a title's types.
/// </remarks>
public interface IReplicatedWorld
{
    /// <summary>Gets the title's channel frame length, which every client's commands carry.</summary>
    int ChannelCount { get; }

    /// <summary>Gets the tick the world runs next, the clock clients' commands are stamped against.</summary>
    long CurrentTick { get; }

    /// <summary>Gets or sets the server's recent snapshots, against which a client's shots are traced as its client saw the world.</summary>
    /// <remarks>Set by the session; a world without it traces every shot against the world as it stands.</remarks>
    LagCompensation? LagCompensation { get; set; }

    /// <summary>Makes the player for a client who has joined.</summary>
    /// <param name="slot">The client's slot.</param>
    /// <param name="name">The name it plays under.</param>
    /// <returns>The entity id its human goes by in snapshots, or <see langword="null"/> when it cannot be placed.</returns>
    int? AddPlayer(int slot, string name);

    /// <summary>Dresses a player in the look it chose, a <see cref="PlayerSkins"/> id.</summary>
    /// <param name="slot">The player's slot; <see cref="ServerSession.HostSlot"/> for a listen server's own.</param>
    /// <param name="skin">The look.</param>
    void SetSkin(int slot, string skin)
    {
    }

    /// <summary>Takes a dropped client's player out of the level and keeps it aside, to be given back if the player comes back.</summary>
    /// <param name="slot">The client's slot.</param>
    /// <param name="parked">What the player is kept under.</param>
    /// <returns><see langword="true"/> when it was kept; a world that keeps none takes it out for good.</returns>
    bool ParkPlayer(int slot, int parked)
    {
        RemovePlayer(slot);
        return false;
    }

    /// <summary>Gives a kept player back to a client coming back, under its new slot, where and as it was.</summary>
    /// <param name="parked">What it was kept under.</param>
    /// <param name="slot">The client's new slot.</param>
    /// <returns>The entity it goes by now, or <see langword="null"/> when none was kept.</returns>
    int? UnparkPlayer(int parked, int slot) => null;

    /// <summary>Forgets a kept player whose client never came back.</summary>
    /// <param name="parked">What it was kept under.</param>
    void ForgetPlayer(int parked)
    {
    }

    /// <summary>Takes out a client's player, as it leaves.</summary>
    /// <param name="slot">The client's slot.</param>
    void RemovePlayer(int slot);

    /// <summary>Gets the buffer the world takes a client's player's commands from, tick by tick.</summary>
    /// <param name="slot">The client's slot.</param>
    /// <returns>The buffer, or <see langword="null"/> when the client has no player.</returns>
    UserCommandBuffer? CommandsFor(int slot);

    /// <summary>Gets the world as it stands at the end of a tick.</summary>
    /// <param name="tick">The tick just run.</param>
    /// <returns>The snapshot.</returns>
    Snapshot Capture(long tick);

    /// <summary>Gets the team a client's player is on.</summary>
    /// <param name="slot">The client's slot.</param>
    /// <returns>The team, or -1.</returns>
    int TeamOf(int slot);

    /// <summary>Takes the deaths since the last call: each player killed, and by whom, by entity id.</summary>
    /// <returns>The deaths; the killer is -1 when nobody killed it.</returns>
    IReadOnlyList<WorldDeath> TakeDeaths();

    /// <summary>Gets the map's round objectives, or none for a map played without them.</summary>
    IReadOnlyList<IRoundObjective> RoundObjectives { get; }

    /// <summary>Gets or sets the order of the objective being played, whose spawn areas new players stand in; zero for none.</summary>
    int CurrentObjective { get; set; }

    /// <summary>Puts a client's dead player back in the level at a spawn point, alive.</summary>
    /// <param name="slot">The client's slot.</param>
    void Respawn(int slot);

    /// <summary>Writes the level's running state for the clients to take, or nothing for a world without one.</summary>
    /// <returns>The state, or <see langword="null"/>.</returns>
    /// <remarks>Sent reliably to each client whenever it differs from what that client last had.</remarks>
    byte[]? CaptureLevel() => null;

    /// <summary>Gets whether this world holds its dead until another player revives them, rather than respawning them.</summary>
    bool HoldsTheDead => false;

    /// <summary>Runs a tick of reviving and says who stood up: the slots whose players are alive again.</summary>
    /// <returns>The revived slots.</returns>
    IReadOnlyList<int> TakeRevivals() => [];

    /// <summary>Gets whether every player is down and the squad can stand again where it last stood together.</summary>
    bool CanRetryFromCheckpoint => false;

    /// <summary>Stands every player up again where the squad last stood together, the level as it is.</summary>
    void RetryFromCheckpoint()
    {
    }

    /// <summary>Gets how far a fallen player's revival has gone, in <c>[0,1]</c>, or <see langword="null"/> when it is up.</summary>
    /// <param name="entity">The player's entity.</param>
    /// <returns>The progress.</returns>
    float? ReviveProgressOf(int entity) => null;
}

/// <summary>A player killed in the level.</summary>
/// <param name="Victim">The entity id of the player killed.</param>
/// <param name="Killer">The entity id of its killer, or -1 when nobody killed it.</param>
public readonly record struct WorldDeath(int Victim, int Killer);

/// <summary>A title's player that can host a session: it hands the session the level as an <see cref="IReplicatedWorld"/>.</summary>
public interface IReplicatedTitle
{
    /// <summary>Gets the map the level is, as a session names it: the mission's id for a campaign level.</summary>
    string Map => string.Empty;

    /// <summary>
    /// Gets what stands for the level now loaded: another object once another level is, the same one again included; or
    /// <see langword="null"/> between levels, when none is up -- which is never a level of its own to travel to.
    /// </summary>
    object? Level => this;

    /// <summary>Makes the level's replicated world.</summary>
    /// <param name="includesHost">Whether the level's own player is a player of the session: a listen server, not a dedicated one.</param>
    /// <returns>The world.</returns>
    IReplicatedWorld CreateReplicatedWorld(bool includesHost);

    /// <summary>Makes the client's side of the level: the other players shown as the snapshots have them.</summary>
    /// <returns>The client world.</returns>
    IReplicatedClientWorld CreateClientWorld();
}

/// <summary>A client's copy of the level, where the other players are shown as the server's snapshots have them.</summary>
public interface IReplicatedClientWorld
{
    /// <summary>Gets the channel frame the client's own player ran on this tick, which is the command sent up for it.</summary>
    ReadOnlySpan<float> LocalFrame { get; }

    /// <summary>Gets the entity ids of the other players shown.</summary>
    IReadOnlyCollection<int> Shown { get; }

    /// <summary>Gets where the client's own player stands, in world units.</summary>
    Vector3d LocalPosition { get; }

    /// <summary>Moves the client's own player, as a correction from the server puts it where the server has it.</summary>
    /// <param name="offset">How far, in world units.</param>
    void MoveLocal(Vector3d offset);

    /// <summary>Takes another player's look, a <see cref="PlayerSkins"/> id, from the server's scoreboard.</summary>
    /// <param name="entity">Its entity.</param>
    /// <param name="skin">Its look.</param>
    void Skin(int entity, string skin)
    {
    }

    /// <summary>Names another player, from the server's scoreboard.</summary>
    /// <param name="entity">Its entity.</param>
    /// <param name="name">Its name.</param>
    void Name(int entity, string name)
    {
    }

    /// <summary>Takes what the server says of the client's own player that the client cannot know itself: what it was hurt by.</summary>
    /// <param name="self">The client's own player as the newest snapshot has it.</param>
    void ApplyOwn(in ReplicatedHuman self);

    /// <summary>Shows another player where a snapshot has it, making it the first time.</summary>
    /// <param name="human">The player, sampled at the moment drawn.</param>
    void Show(in ReplicatedHuman human);

    /// <summary>Takes out a player the snapshots no longer hold.</summary>
    /// <param name="id">Its entity id.</param>
    void Hide(int id);

    /// <summary>Takes the server level's running state, as its world's <see cref="IReplicatedWorld.CaptureLevel"/> wrote it.</summary>
    /// <param name="state">The state.</param>
    void ApplyLevel(byte[] state)
    {
    }

    /// <summary>Takes the rest of the world from the newest snapshot: what is not a human, such as the doors.</summary>
    /// <param name="snapshot">The newest snapshot.</param>
    /// <param name="ownEntity">This client's own entity, whose rounds it showed already, or <see langword="null"/>.</param>
    /// <param name="ticksAhead">
    /// How many ticks this client's own world has run past the snapshot's, counted in its own commands: what its player
    /// stands in -- a lift -- is where the server's will be that many ticks on.
    /// </param>
    void ApplyWorld(Snapshot snapshot, int? ownEntity, int ticksAhead)
    {
    }

    /// <summary>Gets how far, in world units, this client's own player may stand from the server's word without being put back.</summary>
    /// <remarks>
    /// Nought leaves the session's own tolerance. A player riding a moving lift is given more: the server carries it each of
    /// its ticks and runs its commands as they come, so the two drift by a tick of the lift's travel and back as the queue does.
    /// </remarks>
    double PredictionSlack => 0;
}

/// <summary>The kinds of message a session sends, the first byte of each.</summary>
public enum SessionMessage : byte
{
    /// <summary>Server to client, reliable: the client's entity id, the tick rate and the map.</summary>
    Welcome = 1,

    /// <summary>Server to client, sequenced: a snapshot.</summary>
    Snapshot = 2,

    /// <summary>Client to server, sequenced: the newest snapshot it holds and its newest commands.</summary>
    Commands = 3,

    /// <summary>Either way, reliable: a line of chat.</summary>
    Chat = 4,

    /// <summary>Server to client, reliable: every player's standing, the team scores and the round.</summary>
    Scoreboard = 5,

    /// <summary>Server to client, reliable: the level's running state, as the title writes it.</summary>
    Level = 6,

    /// <summary>Server to client, reliable: the server has gone on to another level, which the client loads to play on.</summary>
    Travel = 7,

    /// <summary>Either way, reliable: a place one player marked for the others.</summary>
    Mark = 8,

    /// <summary>Either way, unreliable: a moment of one player's voice, encoded.</summary>
    Voice = 9,

    /// <summary>
    /// Server to client, reliable: the server has begun loading another level, which the client loads alongside it; the
    /// <see cref="Travel"/> that follows, once the server's is up and everyone's is loaded, is the word to begin it.
    /// </summary>
    Going = 10,

    /// <summary>Client to server, reliable: the level the server went to is loaded here, held until the word to begin.</summary>
    Ready = 11,
}
