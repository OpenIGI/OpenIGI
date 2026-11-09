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

    /// <summary>Holds every player where it stands, or lets them go: a round's freeze time, when they may look and buy but not
    /// move, jump, fire or use.</summary>
    /// <param name="held">Whether they are held.</param>
    void HoldStill(bool held)
    {
    }

    /// <summary>Makes the world one where the players fight each other rather than the level together.</summary>
    /// <param name="teams">Whether in two teams (a team mate's rounds never hurt) or every player for itself.</param>
    /// <returns><see langword="false"/> when the world can't be played so: its mode stays its own.</returns>
    bool TryPlayVersus(bool teams) => false;

    /// <summary>Gets where a player stands, or <see langword="null"/> when it isn't in the world (or the world doesn't say).</summary>
    /// <param name="slot">The player's slot.</param>
    /// <returns>Its feet, in world units.</returns>
    Core.Numerics.Vector3d? PositionOf(int slot) => null;

    /// <summary>Gets the way a player faces, or <see langword="null"/> when it isn't in the world (or the world doesn't say).</summary>
    /// <param name="slot">The player's slot.</param>
    /// <returns>Its body's yaw, in radians.</returns>
    float? YawOf(int slot) => null;

    /// <summary>Shows a player at a mode's work -- kneeling to plant the bomb, or defusing it -- or back at its own moves.</summary>
    /// <param name="slot">The player's slot.</param>
    /// <param name="work">The work, or <see langword="null"/> for none.</param>
    /// <param name="heldTicks">How many ticks it has held at it.</param>
    /// <param name="totalTicks">How many the work takes.</param>
    /// <remarks>Its body plays the work's clip, on the server and so on every window the snapshots reach.</remarks>
    void ShowWork(int slot, Modes.ModeWorkKind? work, int heldTicks, int totalTicks)
    {
    }

    /// <summary>Shows which player carries the mode's bomb, for every window to draw it on that player's back.</summary>
    /// <param name="slot">The carrier's slot, or -1 when nobody carries it (dropped, planted, or none in play).</param>
    void ShowCarrier(int slot)
    {
    }

    /// <summary>Gets whether a living player is at a plant: the bomb in hand and its fire or use key held.</summary>
    /// <param name="slot">The player's slot.</param>
    /// <returns><see langword="true"/> while it plants; a title with no bomb in hand plants on the use key alone.</returns>
    bool IsPlanting(int slot) => IsUsing(slot);

    /// <summary>Takes a player's request to drop the bomb it holds, made with the drop key since the last time it was taken.</summary>
    /// <param name="slot">The player's slot.</param>
    /// <returns><see langword="true"/> once for each press with the bomb in hand.</returns>
    bool TakeBombDrop(int slot) => false;

    /// <summary>Throws the mode's bomb out of a player's hands into the world, as a dropped gun is thrown, for every machine to fly.</summary>
    /// <param name="slot">The player who lets it go, or -1 for one no longer there.</param>
    /// <param name="from">Where the player stood, in world units.</param>
    /// <param name="yaw">The way it faced, in radians.</param>
    /// <param name="far">Whether it is thrown on purpose (the long throw) or let fall by a body that went down (the short).</param>
    /// <returns><see langword="false"/> when this world throws nothing, and the bomb is only laid where it stood.</returns>
    bool ThrowBomb(int slot, Core.Numerics.Vector3d from, float yaw, bool far) => false;

    /// <summary>Gets where the thrown bomb is now, in world units, or <see langword="null"/> while none is out.</summary>
    Core.Numerics.Vector3d? ThrownBombAt => null;

    /// <summary>Takes the thrown bomb out of the world: picked up, or the round over.</summary>
    void TakeThrownBomb()
    {
    }

    /// <summary>Gets whether a living player holds the mode's bomb in hand, which it must to plant it.</summary>
    /// <param name="slot">The player's slot.</param>
    /// <returns><see langword="true"/> when it does, and for a title whose bomb is never held.</returns>
    bool HoldsBomb(int slot) => true;

    /// <summary>Gets whether a living player is holding the use key: what plants and defuses a bomb.</summary>
    /// <param name="slot">The player's slot.</param>
    /// <returns><see langword="true"/> while it holds it.</returns>
    bool IsUsing(int slot) => false;

    /// <summary>Hands a living player a weapon, with rounds of its ammunition: what it bought.</summary>
    /// <param name="slot">The player's slot.</param>
    /// <param name="weapon">The title's weapon script id.</param>
    /// <param name="rounds">The rounds it comes with.</param>
    /// <returns><see langword="true"/> when it was handed over.</returns>
    bool Give(int slot, string weapon, int rounds) => false;

    /// <summary>
    /// Gets how many rounds of a weapon's ammunition a living player's pouch has room for, filled up to a number of rounds or
    /// the type's carry limit, whichever is fewer: what buying its ammunition would add.
    /// </summary>
    /// <param name="slot">The player's slot.</param>
    /// <param name="weapon">The title's weapon script id.</param>
    /// <param name="upTo">The rounds it is filled to.</param>
    /// <returns>The room, nought when full; <see langword="null"/> when it does not carry the weapon.</returns>
    int? AmmoRoom(int slot, string weapon, int upTo) => null;

    /// <summary>Gets what became of the players' rounds at one another since the last call, as lines for the server's log.</summary>
    /// <returns>The lines; none for a world that keeps no count.</returns>
    IReadOnlyList<string> TakeCombatReport() => [];

    /// <summary>Describes a player as an admin's <c>where</c> lists it: where it stands, its health, whether it is down.</summary>
    /// <param name="slot">Its slot.</param>
    /// <returns>The description, or <see langword="null"/> for no such player.</returns>
    string? Describe(int slot) => null;

    /// <summary>Puts a player a few metres in front of another, facing it, as an admin's <c>tp</c> asks.</summary>
    /// <param name="slot">The player moved.</param>
    /// <param name="toSlot">The player it is put before.</param>
    /// <param name="metres">How far in front.</param>
    /// <returns>Why it could not be, or <see langword="null"/>.</returns>
    string? Teleport(int slot, int toSlot, double metres) => "this game can't move its players";

    /// <summary>
    /// Says a match begins -- the first on the map, or one played again on it -- just before its players are put back: each is
    /// armed from nothing at that spawn, as at a first one, rather than keeping what it carried through the last.
    /// </summary>
    void BeginMatch()
    {
    }

    /// <summary>Takes the players re-armed since last asked (respawned), for their own games to carry the same.</summary>
    /// <returns>Each one's slot, kit, and whether it was armed from nothing.</returns>
    IReadOnlyList<RearmedPlayer> TakeRearmed() => [];

    /// <summary>Shows a blast the mode set off -- a bomb's -- where it is, as the world shows its own explosions.</summary>
    /// <param name="at">Where, in world units.</param>
    /// <param name="radius">How far it reaches, in world units.</param>
    void Blast(Core.Numerics.Vector3d at, double radius)
    {
    }

    /// <summary>Kills a player outright, as the world kills one that strayed out of play.</summary>
    /// <param name="slot">The player's slot.</param>
    void KillPlayer(int slot)
    {
    }

    /// <summary>Plays the world as a multiplayer map lays it out: where players start, where play is.</summary>
    /// <param name="layout">The map's layout.</param>
    void UseLayout(Modes.MpLayout layout)
    {
    }

    /// <summary>Puts a player on the team its mode chose, for a world that plays teams against each other.</summary>
    /// <param name="slot">The player's slot.</param>
    /// <param name="team">The team.</param>
    void SetTeam(int slot, int team)
    {
    }

    /// <summary>Takes the deaths since the last call: each player killed, and by whom, by entity id.</summary>
    /// <returns>The deaths; the killer is -1 when nobody killed it.</returns>
    IReadOnlyList<WorldDeath> TakeDeaths();

    /// <summary>Takes the hits since the last call: on players, and players' on the level's soldiers.</summary>
    /// <returns>The hits.</returns>
    IReadOnlyList<CombatNote> TakeCombat() => [];

    /// <summary>Gets the map's round objectives, or none for a map played without them.</summary>
    IReadOnlyList<IRoundObjective> RoundObjectives { get; }

    /// <summary>Gets or sets the order of the objective being played, whose spawn areas new players stand in; zero for none.</summary>
    int CurrentObjective { get; set; }

    /// <summary>Puts a client's dead player back in the level at a spawn point, alive.</summary>
    /// <param name="slot">The client's slot.</param>
    void Respawn(int slot);

    /// <summary>Records the level as it stands, as what <see cref="ResetToLevelStart"/> puts back: as the session takes it up.</summary>
    void MarkLevelStart()
    {
    }

    /// <summary>Puts the level back as <see cref="MarkLevelStart"/> found it, without loading anything: a match's new round.</summary>
    void ResetToLevelStart()
    {
    }

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

    /// <summary>Records the client's copy of the level as it stands, as what <see cref="ResetToLevelStart"/> puts back.</summary>
    void MarkLevelStart()
    {
    }

    /// <summary>Puts the server's kit on the client's own player, after the server re-armed its copy (a respawn).</summary>
    /// <param name="kit">Each weapon's script id and the rounds of its ammunition.</param>
    /// <param name="fresh">Whether the server armed its copy from nothing, a respawn after a death.</param>
    void ArmOwn(IReadOnlyList<(string Weapon, int Rounds)> kit, bool fresh)
    {
    }

    /// <summary>Gets whether the client's own player carries a weapon.</summary>
    /// <param name="weapon">The title's weapon script id.</param>
    /// <returns><see langword="true"/> when it is carried.</returns>
    bool CarriesOwn(string weapon) => false;

    /// <summary>Hands the client's own player a weapon the server handed its copy (a buy).</summary>
    /// <param name="weapon">The title's weapon script id.</param>
    /// <param name="rounds">The rounds its ammunition is filled to.</param>
    /// <param name="equip">Whether the gun is new to the player, bought rather than its ammunition, and is taken in hand.</param>
    void GiveOwn(string weapon, int rounds, bool equip)
    {
    }

    /// <summary>Puts the client's copy of the level back as <see cref="MarkLevelStart"/> found it, as the server's was.</summary>
    void ResetToLevelStart()
    {
    }

    /// <summary>Gets the entity ids of the other players shown.</summary>
    IReadOnlyCollection<int> Shown { get; }

    /// <summary>Gets where the client's own player stands, in world units.</summary>
    Vector3d LocalPosition { get; }

    /// <summary>Moves the client's own player, as a correction from the server puts it where the server has it.</summary>
    /// <param name="offset">How far, in world units.</param>
    /// <param name="serverGrounded">Whether the server has the player standing on the ground there.</param>
    void MoveLocal(Vector3d offset, bool serverGrounded);

    /// <summary>Takes the sides the players are on, which say whose names and revive marks this player is shown.</summary>
    /// <param name="mode">The session's mode.</param>
    /// <param name="teamOf">A player's team by its entity, or <see langword="null"/> when it isn't known.</param>
    /// <param name="ownTeam">This player's team, or <see langword="null"/> when it isn't known.</param>
    void Sides(string mode, Func<int, int?> teamOf, int? ownTeam)
    {
    }

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
    /// <param name="predictions">This client's predictions of its own player, for what the server did to it that the client learns of late.</param>
    void ApplyWorld(Snapshot snapshot, int? ownEntity, int ticksAhead, PredictionReconciler predictions)
    {
    }

    /// <summary>Gets whether the client's own player holds the mode's bomb in hand, or <see langword="null"/> where the title never holds one.</summary>
    bool? HoldsBomb => null;

    /// <summary>Gets the platform -- a lift -- the client's own player stands on now, and where this client's car stands.</summary>
    /// <remarks>
    /// Its prediction is weighed on the platform: the server carries the player each of its ticks and runs its commands as
    /// they come, a tick or two earlier or later in the car's travel than here, which in the world was an error of that much.
    /// </remarks>
    PlatformFrame? LocalPlatform => null;

    /// <summary>Gets where this client's copy of a platform stands now, in world units, or <see langword="null"/>.</summary>
    /// <param name="index">The platform, in the snapshot's order of lifts.</param>
    /// <returns>Where it stands.</returns>
    /// <remarks>
    /// The others riding it are drawn on it: they are sampled in the past, and the car this client rides is the server's
    /// projected on into its present, so drawn where the snapshots had them they stood under its floor or in its roof.
    /// </remarks>
    Vector3d? PlatformOrigin(int index) => null;
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

    /// <summary>Server to client, reliable: a fresh challenge for the client's identity to sign before it is let in.</summary>
    Prove = 12,

    /// <summary>Client to server, reliable: the client's public key and its signature over the challenge.</summary>
    Proof = 13,

    /// <summary>Server to client, reliable: where the match stands -- its mode, phase, round, winner and clock.</summary>
    MatchState = 14,

    /// <summary>A kill for the feed: a <see cref="Modes.KillNote"/>.</summary>
    KillNote = 15,

    /// <summary>The level was put back to where the match began (a new round): a client puts its own copy back too, before
    /// anything the server sends after it.</summary>
    LevelReset = 17,

    /// <summary>
    /// The remote console: from a client, a password and a command for the server's console; from the server, its answer's lines.
    /// Only ever over a sealed line -- UDP's AES-GCM or WebRTC's DTLS -- so the password is never readable on the way.
    /// </summary>
    Rcon = 16,

    /// <summary>Server to client: hits the client's player scored or took (<see cref="CombatNote"/>), for its hit marker and
    /// the marks of where shots came from.</summary>
    Combat = 18,

    /// <summary>Server to client: a weapon the server handed the client's player (a buy), for the client's own copy of it to
    /// carry too; the weapon's script id and the rounds of its ammunition.</summary>
    Given = 19,

    /// <summary>Server to client: the client's player was re-armed (a respawn), for its own copy to carry the same; whether
    /// from nothing, then each weapon's script id and the rounds of its ammunition.</summary>
    Kit = 20,

    /// <summary>Server to client, reliable: the vote for the next map as that client sees it (<see cref="Modes.NextMapVote"/>), or
    /// that it closed.</summary>
    MapVote = 21,

    /// <summary>
    /// Server to client, reliable: the files the server plays a map from (<see cref="SessionFileList"/>), which the client's own
    /// must match; a differing one it asks for.
    /// </summary>
    Files = 22,

    /// <summary>Client to server, reliable: the files of a list it was sent that it lacks, by place in the list.</summary>
    FileWant = 23,

    /// <summary>Server to client, reliable: a piece of a file asked for.</summary>
    FilePart = 24,
}

/// <summary>A player the server re-armed: what it carries now, and whether it was armed from nothing.</summary>
/// <param name="Slot">Its slot.</param>
/// <param name="Fresh">Whether it was armed from nothing (it had died).</param>
/// <param name="Kit">Each weapon's script id and the rounds of its ammunition.</param>
public sealed record RearmedPlayer(int Slot, bool Fresh, IReadOnlyList<(string Weapon, int Rounds)> Kit);
