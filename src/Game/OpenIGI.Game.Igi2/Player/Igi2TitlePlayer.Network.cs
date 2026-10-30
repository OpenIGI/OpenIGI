using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi2.Animation;
using OpenIGI.Game.Igi2.Audio;
using OpenIGI.Game.Igi2.Collision;
using OpenIGI.Game.Igi2.Flow;
using OpenIGI.Game.Igi2.Humans;
using OpenIGI.Game.Igi2.Multiplayer;
using OpenIGI.Game.Igi2.ViewModel;
using OpenIGI.Game.Igi2.Weapons;
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
internal sealed record Igi2PlayerSpawnKit(
    Igi2CollisionWorld World,
    Igi2HumanPlayerGeneral General,
    OpenIGI.Game.Ai.AiDifficulty Difficulty,
    Igi2AnimationLibrary? Clips,
    Func<string, Formats.World.MefModel?> Models,
    Igi2PlayerTuning? Tuning,
    Igi2Sounds Sounds,
    Engine.Titles.IServiceCatalog Services);

/// <summary>The level's other players: those a server runs for its clients, beside the level's own.</summary>
public sealed partial class Igi2TitlePlayer : IReplicatedTitle
{
    /// <inheritdoc/>
    public IReplicatedWorld CreateReplicatedWorld(bool includesHost) => new Igi2ReplicatedWorld(this) { IncludesHost = includesHost };

    /// <inheritdoc/>
    public IReplicatedClientWorld CreateClientWorld() => new Igi2ClientWorld(this);

    /// <summary>Gets the channel frame the level's own player ran on in its last tick: on a client, the command sent up.</summary>
    public ReadOnlySpan<float> LastFrame => _frame;

    private readonly List<Igi2NetworkHuman> _networkHumans = [];

    /// <summary>Gets the ticks the level has run, the clock a server's commands are stamped against.</summary>
    public long TickCount => _ticks;

    /// <summary>Gets the players the server runs for its clients, in the order they joined; empty in a single-player level.</summary>
    public IReadOnlyList<Igi2NetworkHuman> NetworkHumans => _networkHumans;

    /// <summary>Gets or sets what the level's own player was made from, which another is made from too.</summary>
    internal Igi2PlayerSpawnKit? SpawnKit { get; set; }

    /// <summary>Gets or sets what puts the other players where a client's player saw them as its shots are traced: lag compensation.</summary>
    internal Func<Igi2NetworkHuman, IDisposable?>? NetworkRewind { get; set; }

    /// <summary>Gets the areas' rosters, which count every player in the level.</summary>
    internal List<PlayerRoster<Flow.IIgi2AreaOccupant>> AreaRosters { get; } = [];

    /// <summary>Gets the smoke's rosters, which burn every player in the level.</summary>
    internal List<PlayerRoster<Igi2HumanPlayer>> SmokeRosters { get; } = [];

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
        ArgumentNullException.ThrowIfNull(model);
        if (SpawnKit is not { } kit)
        {
            return null;
        }

        Igi2NetworkHuman added = Igi2NetworkHumanFactory.Make(this, kit, _surroundings, model, position, gamma, team);
        _networkHumans.Add(added);
        Igi2HumanPlayer human = added.Human;
        if (MapHumans is List<Igi2Human> humans)
        {
            humans.Add(human);
        }

        (Ai?.Detection ?? _surroundings.Detection)?.Register(human);
        Explosions?.AddTarget(human);
        Ai?.Players.Add(human);
        foreach (PlayerRoster<Flow.IIgi2AreaOccupant> roster in AreaRosters)
        {
            roster.Add(human);
        }

        foreach (PlayerRoster<Igi2HumanPlayer> roster in SmokeRosters)
        {
            roster.Add(human);
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
        (MapHumans as List<Igi2Human>)?.Remove(human);
        (Ai?.Detection ?? _surroundings.Detection)?.Remove(human);
        Explosions?.RemoveTarget(human);
        Ai?.Players.Remove(human);
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
            if (other.Weapons is not { } weapons)
            {
                continue;
            }

            using IDisposable? rewound = NetworkRewind?.Invoke(other);
            weapons.RunGunShot();
        }
    }

    /// <summary>The other players' delayed halves, after the level's own, each on the command its client sent for the tick.</summary>
    private void RunNetworkHumans()
    {
        foreach (Igi2NetworkHuman other in _networkHumans)
        {
            other.RunDelayed(_ticks);
        }
    }
}
