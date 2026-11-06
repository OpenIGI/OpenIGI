using System.Globalization;
using OpenIGI.Host.Multiplayer;

namespace OpenIGI.Host.Dedicated;

/// <summary>What the server console works on: the players, the map and mode, the bans, the rotation.</summary>
public interface IServerControl
{
    /// <summary>Gets the server's name.</summary>
    string Name { get; }

    /// <summary>Gets the map played.</summary>
    string Map { get; }

    /// <summary>Gets the mode played.</summary>
    string Mode { get; }

    /// <summary>Gets the most players it takes.</summary>
    int MaxPlayers { get; }

    /// <summary>Gets the players.</summary>
    IReadOnlyList<ServerPlayer> Players { get; }

    /// <summary>Gets the bans.</summary>
    BanList Bans { get; }

    /// <summary>Gets the maps played in turn.</summary>
    IReadOnlyList<RotationEntry> Rotation { get; }

    /// <summary>Puts a player out.</summary>
    /// <param name="slot">Its slot.</param>
    /// <param name="reason">What it is told.</param>
    /// <returns><see langword="true"/> when there was such a player.</returns>
    bool Remove(int slot, string reason);

    /// <summary>Says a line to every player.</summary>
    /// <param name="text">The line.</param>
    void Say(string text);

    /// <summary>Goes on to a mission now, under a mode or the one played.</summary>
    /// <param name="mission">The mission.</param>
    /// <param name="mode">The mode, or <see langword="null"/>.</param>
    /// <returns>Why it could not, or <see langword="null"/>.</returns>
    string? ChangeMap(int mission, string? mode);

    /// <summary>Goes on to the rotation's next map now.</summary>
    /// <returns>The entry gone on to, or <see langword="null"/> with no rotation.</returns>
    RotationEntry? NextMap();

    /// <summary>Puts a mode on from the next map.</summary>
    /// <param name="mode">The mode.</param>
    /// <returns><see langword="true"/> for a mode the server plays.</returns>
    bool UseMode(string mode);

    /// <summary>Stops the server.</summary>
    void Quit();
}

/// <summary>
/// The dedicated server's console: the commands its operator types at its terminal (and, later, sends from the game over the
/// remote console), each answered in lines of text. Players are named by their name, an unambiguous start of it, or <c>#slot</c>.
/// </summary>
/// <param name="server">What the commands work on.</param>
public sealed class ServerConsole(IServerControl server)
{
    /// <summary>The commands, as <c>help</c> lists them.</summary>
    public static readonly IReadOnlyList<string> Help =
    [
        "status                         the server, its map and its players",
        "say <text>                     tell every player",
        "kick <player> [reason]         put a player out",
        "ban <player> [minutes] [reason] put a player out and keep its key out (0 or none: for ever)",
        "unban <name|id>                lift a ban",
        "bans                           the bans in force",
        "map <mission> [mode]           go on to a mission now",
        "next                           go on to the rotation's next map",
        "mode <mode>                    play a mode from the next map",
        "rotation                       the maps played in turn",
        "quit                           stop the server",
    ];

    /// <summary>Runs a command.</summary>
    /// <param name="line">The command as typed.</param>
    /// <returns>Its answer.</returns>
    public IReadOnlyList<string> Execute(string line)
    {
        string[] words = ServerConfig.Words(line ?? string.Empty);
        if (words.Length == 0)
        {
            return [];
        }

        string rest = words.Length > 1 ? string.Join(' ', words[1..]) : string.Empty;
        return words[0].ToLowerInvariant() switch
        {
            "help" or "?" => Help,
            "status" => Status(),
            "say" when rest.Length > 0 => Said(rest),
            "kick" when words.Length > 1 => Kick(words[1], words.Length > 2 ? string.Join(' ', words[2..]) : "kicked by the server"),
            "ban" when words.Length > 1 => BanPlayer(words),
            "unban" when words.Length > 1 => server.Bans.Remove(words[1]) is { } lifted ? [$"Lifted the ban on {lifted.Name} ({lifted.PlayerId})."] : [$"No ban on '{words[1]}'."],
            "bans" => Bans(),
            "map" when words.Length > 1 => Map(words),
            "next" => server.NextMap() is { } next ? [$"Going on to mission {next.Mission} ({ModeOf(next)})."] : ["There is no rotation; use map <mission> [mode]."],
            "mode" when words.Length > 1 => server.UseMode(words[1]) ? [$"{words[1].ToLowerInvariant()} is played from the next map."] : [$"'{words[1]}' is not a mode: {string.Join(", ", Game.Multiplayer.Modes.GameModes.All)}."],
            "rotation" => Rotation(),
            "quit" or "exit" or "stop" => Quit(),
            _ => [$"Unknown command or missing argument: {words[0]}. Type help."],
        };
    }

    /// <summary>Finds a player by <c>#slot</c>, its whole name, or an unambiguous start of it.</summary>
    /// <param name="who">What was typed.</param>
    /// <param name="player">The player.</param>
    /// <param name="problem">Why none was found.</param>
    /// <returns><see langword="true"/> when one was.</returns>
    public bool TryFind(string who, out ServerPlayer player, out string problem)
    {
        IReadOnlyList<ServerPlayer> players = server.Players;
        player = default;
        problem = string.Empty;
        if (who.StartsWith('#') && int.TryParse(who.AsSpan(1), NumberStyles.Integer, CultureInfo.InvariantCulture, out int slot))
        {
            int at = players.ToList().FindIndex(p => p.Slot == slot);
            if (at >= 0)
            {
                player = players[at];
                return true;
            }

            problem = $"No player in slot {slot}.";
            return false;
        }

        ServerPlayer[] exact = [.. players.Where(p => string.Equals(p.Name, who, StringComparison.OrdinalIgnoreCase))];
        ServerPlayer[] found = exact.Length > 0 ? exact : [.. players.Where(p => p.Name.StartsWith(who, StringComparison.OrdinalIgnoreCase))];
        if (found.Length == 1)
        {
            player = found[0];
            return true;
        }

        problem = found.Length == 0
            ? $"No player called '{who}'."
            : $"'{who}' could be {string.Join(", ", found.Select(p => $"{p.Name} (#{p.Slot})"))}; name one by #slot.";
        return false;
    }

    private string[] Status()
    {
        List<string> lines =
        [
            $"{server.Name}  ·  mission {server.Map}  ·  {server.Mode}  ·  {server.Players.Count}/{server.MaxPlayers} players",
        ];
        foreach (ServerPlayer p in server.Players.OrderBy(p => p.Slot))
        {
            lines.Add(string.Create(
                CultureInfo.InvariantCulture,
                $"  #{p.Slot,-3} {p.Name,-20} team {p.Team,2}  {p.Kills,3}/{p.Deaths,-3}  {p.Ping,4} ms  {p.PlayerId}"));
        }

        return [.. lines];
    }

    private string[] Said(string text)
    {
        server.Say(text);
        return [$"Said: {text}"];
    }

    private string[] Kick(string who, string reason)
    {
        if (!TryFind(who, out ServerPlayer player, out string problem))
        {
            return [problem];
        }

        server.Remove(player.Slot, reason);
        return [$"Kicked {player.Name}: {reason}."];
    }

    private string[] BanPlayer(string[] words)
    {
        if (!TryFind(words[1], out ServerPlayer player, out string problem))
        {
            return [problem];
        }

        int next = 2;
        double minutes = 0;
        if (words.Length > 2 && double.TryParse(words[2], NumberStyles.Float, CultureInfo.InvariantCulture, out double given))
        {
            minutes = Math.Max(0, given);
            next = 3;
        }

        string reason = words.Length > next ? string.Join(' ', words[next..]) : "banned by the server";
        if (player.PlayerId.Length == 0)
        {
            return [$"{player.Name} has no verified key to ban; kicked instead."];
        }

        Ban ban = server.Bans.Add(player.PlayerId, player.Name, minutes, reason);
        server.Remove(player.Slot, server.Bans.Check(ban.PlayerId) ?? reason);
        return [minutes > 0 ? string.Create(CultureInfo.InvariantCulture, $"Banned {player.Name} for {minutes:0.#} minutes: {reason}.") : $"Banned {player.Name}: {reason}."];
    }

    private string[] Bans()
    {
        IReadOnlyList<Ban> bans = server.Bans.Bans;
        return bans.Count == 0
            ? ["No bans."]
            : [.. bans.Select(b => $"  {b.Name,-20} {b.PlayerId}  {(b.Until is { } until ? $"until {until:yyyy-MM-dd HH:mm} UTC" : "for ever")}  {b.Reason}")];
    }

    private string[] Map(string[] words)
    {
        if (!int.TryParse(words[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int mission))
        {
            return [$"'{words[1]}' is not a mission number."];
        }

        string? mode = words.Length > 2 ? words[2] : null;
        return server.ChangeMap(mission, mode) is { } problem ? [problem] : [$"Going on to mission {mission} ({mode ?? server.Mode})."];
    }

    private string[] Rotation()
    {
        IReadOnlyList<RotationEntry> rotation = server.Rotation;
        return rotation.Count == 0
            ? [$"No rotation: mission {server.Map} is played again."]
            : [.. rotation.Select((e, i) => string.Create(CultureInfo.InvariantCulture, $"  {i + 1}. mission {e.Mission}  {ModeOf(e)}"))];
    }

    private string[] Quit()
    {
        server.Quit();
        return ["Stopping."];
    }

    private string ModeOf(RotationEntry entry) => entry.Mode.Length > 0 ? entry.Mode : server.Mode;
}
