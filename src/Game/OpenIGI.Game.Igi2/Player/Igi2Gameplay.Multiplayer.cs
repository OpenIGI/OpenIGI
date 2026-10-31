using OpenIGI.Engine.Tasks;
using OpenIGI.Game.Igi2.Multiplayer;
using OpenIGI.Game.Multiplayer;
using OpenIGI.Game.Player;

namespace OpenIGI.Game.Igi2.Player;

/// <summary>Standing the level's own player on a multiplayer map, which places no player of its own.</summary>
public sealed partial class Igi2Gameplay
{
    /// <summary>The first team's body models, <c>NetManager_atGoodModel</c> (<c>igi2 0x6BBF74</c>).</summary>
    public static readonly IReadOnlyList<string> GoodModels = ["jones", "igiagent", "csf"];

    /// <summary>The second team's body models, <c>NetManager_atBadModel</c> (<c>igi2 0x6BBF8C</c>).</summary>
    public static readonly IReadOnlyList<string> BadModels = ["resg", "les", "chn"];

    /// <summary>
    /// Finds where a multiplayer map starts the level's own player: the first team's spawn area's point, as a joining client
    /// of that team would be put.
    /// </summary>
    /// <param name="root">The level.</param>
    /// <param name="models">The level's models, which decide the body worn.</param>
    /// <param name="spawn">Where, facing which way, in which body, on which team.</param>
    /// <returns><see langword="false"/> when the level has no spawn areas either: neither a campaign mission nor a multiplayer map.</returns>
    /// <remarks>
    /// A multiplayer map's objects declare no <c>HumanPlayer</c>: the original makes every player as it joins
    /// (<c>Client_CreateHuman</c>), in a body named <c>"%s%d_1"</c> from its team's model table and a face. The level's own
    /// player here is the listen server's; a dedicated server keeps it out of the session.
    /// </remarks>
    internal static bool TryFindMultiplayerSpawn(GameTask? root, Func<string, Formats.World.MefModel?> models, out PlayerSpawnPoint spawn)
    {
        ArgumentNullException.ThrowIfNull(models);
        spawn = default;
        if (root is null || TeamModel(TeamRoster.Good, models) is not { } model)
        {
            return false;
        }

        Igi2SpawnAreaTask? area = root.Descendants().OfType<Igi2SpawnAreaTask>().FirstOrDefault(a => a.Team == TeamRoster.Good && a.Points().Any());
        if (area is null || new Igi2SpawnSelector().Pick(area) is not { } point)
        {
            return false;
        }

        spawn = new PlayerSpawnPoint(point.Position, point.Gamma, model, TeamRoster.Good);

        // The level's own systems are built around its player's task (Igi2LevelBuild), so the map is given one where its
        // player stands, as a campaign level declares it; a client's player is made when it joins, as the original makes all.
        if (root.Tree.New(root, root.Tree.Types.Get(PlayerTaskTypeName)) is HumanPlayerTask task)
        {
            task.SpawnPosition = point.Position;
            task.SpawnGamma = point.Gamma;
            task.PlayerModel = model;
            task.PlayerTeam = TeamRoster.Good;
        }

        return true;
    }

    /// <summary>The script name the player's task is registered under.</summary>
    private const string PlayerTaskTypeName = "HumanPlayer";

    /// <summary>Gets the first body of a team's table the level's packs hold.</summary>
    /// <param name="team">The team.</param>
    /// <param name="models">The level's models.</param>
    /// <returns>The model's name, or <see langword="null"/> when the packs hold none of them.</returns>
    internal static string? TeamModel(int team, Func<string, Formats.World.MefModel?> models)
    {
        ArgumentNullException.ThrowIfNull(models);
        foreach (string body in team == TeamRoster.Bad ? BadModels : GoodModels)
        {
            for (int face = 0; face <= 5; face++)
            {
                foreach (string name in new[] { $"{body}{face}_1", $"{body}_{face}" })
                {
                    if (models(name) is not null)
                    {
                        return name;
                    }
                }
            }
        }

        return null;
    }
}
