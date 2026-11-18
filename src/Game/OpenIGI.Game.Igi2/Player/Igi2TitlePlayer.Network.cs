using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi2.Animation;
using OpenIGI.Game.Igi2.Audio;
using OpenIGI.Game.Igi2.Collision;
using OpenIGI.Game.Igi2.Humans;
using OpenIGI.Game.Igi2.Multiplayer;
using OpenIGI.Game.Igi2.ViewModel;
using OpenIGI.Game.Multiplayer;
using OpenIGI.Game.Player;

namespace OpenIGI.Game.Igi2.Player;

/// <summary>What the level's own player was made from, kept so another player can be made the same way.</summary>
/// <param name="World">The level's collision.</param>
/// <param name="General">The player tuning.</param>
/// <param name="Difficulty">The active difficulty.</param>
/// <param name="Clips">The level's clips.</param>
/// <param name="Models">The level's models, by name.</param>
/// <param name="Tuning">The player script's tuning, or <see langword="null"/>.</param>
/// <param name="Sounds">The level's sounds.</param>
/// <param name="Services">The session's tables.</param>
/// <param name="Looks">
/// The models another player's body is found among: the level's, then a co-op look's the level lacks (<see cref="Igi2LookPacks"/>).
/// </param>
internal sealed record Igi2PlayerSpawnKit(
    Igi2CollisionWorld World,
    Igi2HumanPlayerGeneral General,
    OpenIGI.Game.Ai.AiDifficulty Difficulty,
    Igi2AnimationLibrary? Clips,
    Func<string, Formats.World.MefModel?> Models,
    Igi2PlayerTuning? Tuning,
    Igi2Sounds Sounds,
    Engine.Titles.IServiceCatalog Services,
    Func<string, Formats.World.MefModel?> Looks);

/// <summary>The level's other players: those a server runs for its clients, beside the level's own.</summary>
public sealed partial class Igi2TitlePlayer : IReplicatedTitle, ICoopMarks
{
    /// <summary>Gets or sets what tells this level's other players' names and fallen marks: the session's world, server's or client's.</summary>
    internal ICoopMarks? Marks { get; set; }

    /// <inheritdoc/>
    public IReadOnlyList<OpenIGI.Game.Ai.WorldNameTag> NameTags() => Marks?.NameTags() ?? [];

    /// <inheritdoc/>
    public IReadOnlyList<OpenIGI.Game.Ai.ReviveMarker> ReviveMarkers() => Marks?.ReviveMarkers() ?? [];

    /// <inheritdoc/>
    /// <remarks>
    /// The mission's id, as a session names it and a client follows its server on to it: left unnamed, a server gone on to the
    /// next mission named the one it left, and every client loaded that again.
    /// </remarks>
    string IReplicatedTitle.Map => MissionId.ToString(System.Globalization.CultureInfo.InvariantCulture);

    // IReplicatedTitle.Level stays this player: each level makes a player of its own (Igi2Gameplay), so another level is another one.

    /// <inheritdoc/>
    public IReplicatedWorld CreateReplicatedWorld(bool includesHost) => new Igi2ReplicatedWorld(this) { IncludesHost = includesHost };

    /// <inheritdoc/>
    public IReplicatedClientWorld CreateClientWorld() => new Igi2ClientWorld(this);

    /// <summary>
    /// Gets or sets the server's cutscene key, on a co-op client: its scenes end when its host skips them, and its own key is
    /// not read. <see langword="null"/> elsewhere, where the player's own key is.
    /// </summary>
    internal bool? FollowedBreakKey { get; set; }

    /// <summary>Gets the channel frame the level's own player ran on in its last tick: on a client, the command sent up.</summary>
    public ReadOnlySpan<float> LastFrame => _frame;

    private readonly List<Igi2NetworkHuman> _networkHumans = [];

    /// <summary>Gets every task of the level running, or none between levels.</summary>
    /// <returns>The level's tasks, in the tree's order.</returns>
    public IEnumerable<Engine.Tasks.GameTask> LevelTasks() => (Level?.Root ?? LevelRoot)?.Descendants() ?? [];

    /// <summary>Gets or sets the level's task tree, which a level without a run of its own (a multiplayer map, for now) is read through.</summary>
    internal Engine.Tasks.GameTask? LevelRoot { get; set; }

    /// <summary>Gets the ticks the level has run, the clock a server's commands are stamped against.</summary>
    public long TickCount => _ticks;

    /// <summary>Gets the players the server runs for its clients, in the order they joined; empty in a single-player level.</summary>
    public IReadOnlyList<Igi2NetworkHuman> NetworkHumans => _networkHumans;

    /// <summary>Gets or sets what the level's own player was made from, which another is made from too.</summary>
    internal Igi2PlayerSpawnKit? SpawnKit { get; set; }

    /// <summary>Gets or sets what puts the other players where a client's player saw them as its shots are traced: lag compensation.</summary>
    internal Func<Igi2NetworkHuman, IDisposable?>? NetworkRewind { get; set; }

    /// <summary>Gets a client's eye eased over the server's corrections of its own player, as I.G.I.'s is: the body moves at once, the eye follows.</summary>
    public PredictionErrorSmoothing ViewError { get; } = new();

    /// <summary>Gets the areas' rosters, which count every player in the level.</summary>
    internal List<PlayerRoster<Flow.IIgi2AreaOccupant>> AreaRosters { get; } = [];

    /// <summary>Gets the smoke's rosters, which burn every player in the level.</summary>
    internal List<PlayerRoster<Igi2HumanPlayer>> SmokeRosters { get; } = [];

    /// <summary>
    /// Gets or sets the players the level's scripts weigh, <c>HumanPlayer_isDead</c> among them (none left alive): a server's
    /// other players stand in it beside the level's own, so one player down is not the mission lost.
    /// </summary>
    internal PlayerRoster<Igi2HumanPlayer>? ScriptPlayers { get; set; }

    /// <summary>
    /// Makes another player in the level, armed, bodied and in every list the level's own player is in, driven by the commands
    /// its client sends.
    /// </summary>
    /// <param name="model">The body model it wears.</param>
    /// <param name="position">Where its feet stand, in world units.</param>
    /// <param name="gamma">The heading it faces.</param>
    /// <param name="team">Its team.</param>
    /// <returns>The player, or <see langword="null"/> when the level was made without a spawn kit.</returns>
    public Igi2NetworkHuman? AddNetworkHuman(string model, Vector3d position, float gamma, int team)
    {
        if (MakeOtherPlayer(model, position, gamma, team) is not { } added)
        {
            return null;
        }

        Register(added.Human);
        return added;
    }

    // A server's other player in every list the level's own player is in: seen and hunted, struck by blasts, weighed by the
    // scripts, counted by the areas and burnt by the smoke.
    private void Register(Igi2HumanPlayer human)
    {
        (Ai?.Detection ?? _surroundings.Detection)?.Register(human);
        Explosions?.AddTarget(human);
        Ai?.Players.Add(human);
        ScriptPlayers?.Add(human);
        foreach (PlayerRoster<Flow.IIgi2AreaOccupant> roster in AreaRosters)
        {
            roster.Add(human);
        }

        foreach (PlayerRoster<Igi2HumanPlayer> roster in SmokeRosters)
        {
            roster.Add(human);
        }
    }

    /// <summary>Gets the body a player of a side wears: on a multiplayer map its team's (<c>NetManager_atGoodModel</c> /
    /// <c>atBadModel</c>), elsewhere the level's own player's.</summary>
    /// <param name="team">The player's team.</param>
    /// <returns>The model's name, or <see langword="null"/> when the level's player has none.</returns>
    internal string? PlayerBodyFor(int team) =>
        Human.Model is not { } own ? null
            : (SpawnKit is { } kit && IsMultiplayerMap ? Igi2Gameplay.TeamModel(team, kit.Models) : null) ?? own.Name;

    /// <summary>
    /// Gets the body a player wears: in a campaign mission played together, the co-op look it chose (<see cref="PlayerSkins.Igi2"/>)
    /// when that look's body can be had, here or from the pack that ships it; otherwise its side's (<see cref="PlayerBodyFor(int)"/>).
    /// </summary>
    /// <param name="team">The player's team.</param>
    /// <param name="look">The look it chose, a <see cref="PlayerSkins"/> id, or <see langword="null"/>.</param>
    /// <returns>The model's name, or <see langword="null"/> when the level's player has none.</returns>
    internal string? PlayerBodyFor(int team, string? look) =>
        !IsMultiplayerMap && PlayerSkins.Igi2.Of(look).Model is { Length: > 0 } worn && SpawnKit?.Looks(worn) is not null
            ? worn
            : PlayerBodyFor(team);

    // Whether the level is a multiplayer map (its spawn areas), whose sides' bodies are its own, rather than a campaign mission.
    private bool IsMultiplayerMap => LevelTasks().OfType<Igi2SpawnAreaTask>().Any();

    /// <summary>Puts a player taken out (<see cref="RemoveNetworkHuman"/>) back in the level as it was, its client come back.</summary>
    /// <param name="player">The player.</param>
    public void ReturnNetworkHuman(Igi2NetworkHuman player)
    {
        ArgumentNullException.ThrowIfNull(player);
        if (_networkHumans.Contains(player))
        {
            return;
        }

        _networkHumans.Add(player);
        player.Human.IsGone = false;
        (MapHumans as List<Igi2Human>)?.Add(player.Human);
        if (player.Human.Collider is { } collider)
        {
            player.World.AddBody(collider);
        }

        Register(player.Human);
    }

    /// <summary>
    /// Makes a client's copy of another player: bodied, armed and drawn, standing in the level's humans so it blocks and is struck,
    /// but posed by the snapshots (<see cref="Igi2NetworkHuman.PuppetTarget"/>) and nothing of the level's own to count.
    /// </summary>
    /// <param name="model">The body model it wears.</param>
    /// <param name="position">Where its feet stand, in world units.</param>
    /// <param name="gamma">The heading it faces.</param>
    /// <param name="team">Its team.</param>
    /// <returns>The puppet, or <see langword="null"/> when the level was made without a spawn kit.</returns>
    /// <remarks>
    /// The soldiers, the areas, the smoke and the blasts are the server's to weigh against its players: a client that put its
    /// copies in their lists had its own guards hunt them, and its own areas and alarms counted them. The scripts' "player dead"
    /// weighs them, though, as the server's does its players: a client fallen while its partners stand is not the mission lost,
    /// and its own level showed "Mission failed" when it weighed itself alone.
    /// </remarks>
    public Igi2NetworkHuman? AddPartnerPuppet(string model, Vector3d position, float gamma, int team)
    {
        if (MakeOtherPlayer(model, position, gamma, team) is not { } puppet)
        {
            return null;
        }

        ScriptPlayers?.Add(puppet.Human);

        // Its harm is the server's: this client's own rounds struck it and it bled, cried out and flinched on this screen even
        // where the server spared it (friendly fire off), and its own player feels the hits the server's copy takes (FeelHit).
        puppet.Human.Spares = static _ => true;
        return puppet;
    }

    // Another player made and stood in the level's humans, in neither the soldiers' nor the level's lists yet.
    private Igi2NetworkHuman? MakeOtherPlayer(string model, Vector3d position, float gamma, int team)
    {
        ArgumentNullException.ThrowIfNull(model);
        if (SpawnKit is not { } kit)
        {
            return null;
        }

        Igi2NetworkHuman added = Igi2NetworkHumanFactory.Make(this, kit, _surroundings, model, position, gamma, team);
        _networkHumans.Add(added);
        if (MapHumans is List<Igi2Human> humans)
        {
            humans.Add(added.Human);
        }

        return added;
    }

    /// <summary>Takes a player out of the level, as its client leaves.</summary>
    /// <param name="player">The player.</param>
    /// <returns><see langword="true"/> when it was in the level.</returns>
    public bool RemoveNetworkHuman(Igi2NetworkHuman player)
    {
        ArgumentNullException.ThrowIfNull(player);
        if (!_networkHumans.Remove(player))
        {
            return false;
        }

        Igi2HumanPlayer human = player.Human;
        human.IsGone = true;
        (MapHumans as List<Igi2Human>)?.Remove(human);
        (Ai?.Detection ?? _surroundings.Detection)?.Remove(human);
        Explosions?.RemoveTarget(human);
        Ai?.Players.Remove(human);
        ScriptPlayers?.Remove(human);
        foreach (PlayerRoster<Flow.IIgi2AreaOccupant> roster in AreaRosters)
        {
            roster.Remove(human);
        }

        foreach (PlayerRoster<Igi2HumanPlayer> roster in SmokeRosters)
        {
            roster.Remove(human);
        }

        if (human.Collider is { } collider)
        {
            player.World.RemoveBody(collider);
        }

        return true;
    }

    /// <summary>The other players' bodies, posed and dressed as the soldiers' are, to be drawn beside the level's own.</summary>
    private IEnumerable<TitleWorldModel> NetworkModels()
    {
        foreach (Igi2NetworkHuman other in _networkHumans)
        {
            if (other.Human.Model is not { } model)
            {
                continue;
            }

            foreach (TitleWorldModel part in Igi2BodyFrame.Build(
                other.Human, other.Human.BodyPose(), model.Name, other.Weapons?.Inventory.Current?.WeaponType.FixedModel, _ticks, other.Weapons?.Firing.Flashes))
            {
                yield return part;
            }
        }
    }

    /// <summary>The other players' shots of the last tick, traced where the level's own player's are.</summary>
    private void RunNetworkGunShots()
    {
        foreach (Igi2NetworkHuman other in _networkHumans)
        {
            RunNetworkGunShot(other);
        }
    }

    // One other player's shots, traced with the others put back where its client saw them as it fired.
    private void RunNetworkGunShot(Igi2NetworkHuman other)
    {
        if (other.Weapons is not { } weapons)
        {
            return;
        }

        using IDisposable? rewound = NetworkRewind?.Invoke(other);
        weapons.RunGunShot();
    }

    /// <summary>The other players' delayed halves, after the level's own, each on the commands of its client's due this tick.</summary>
    private void RunNetworkHumans()
    {
        foreach (Igi2NetworkHuman other in _networkHumans)
        {
            other.RunDelayed(_ticks, RunNetworkGunShot, NetworkRewind);
        }
    }

    /// <summary>Gets how the level's own player stood as its last tick's frame began, which a client's command carries.</summary>
    internal Igi2CommandStart CommandStart { get; private set; }

    /// <summary>Gets what the level's own player's last tick left it with, which a client's command carries.</summary>
    internal Igi2CommandEnd CommandEnd =>
        new(Human.ChosenWeaponNumber, Human.IsWalk, Human.View.FieldOfView, Human.View.ObjectFieldOfView);

    /// <summary>
    /// Gets or sets whether the level's own player's stream is set from each tick's command seed: on a co-op client, whose
    /// server's copy of it draws from the same seeds.
    /// </summary>
    internal bool SeedsCommands { get; set; }
}
