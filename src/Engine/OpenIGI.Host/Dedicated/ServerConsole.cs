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

    /// <summary>Lists every player: its team, where it stands, its health.</summary>
    /// <returns>A line each.</returns>
    IReadOnlyList<string> Where() => [];

    /// <summary>Puts one player a few metres in front of another, facing it.</summary>
    /// <param name="who">The player moved, by name.</param>
    /// <param name="before">The player it is put before, by name.</param>
    /// <param name="metres">How far in front.</param>
    /// <returns>What happened.</returns>
    string Teleport(string who, string before, double metres) => "This server can't move its players.";

    /// <summary>Saves the session so far as a demo.</summary>
    /// <param name="path">Where to.</param>
    /// <returns>Why it could not be, or <see langword="null"/> once saved.</returns>
    string? SaveDemo(string path) => "this server keeps no recording";

    /// <summary>Goes on to the rotation's next map now.</summary>
    /// <returns>The entry gone on to, or <see langword="null"/> with no rotation.</returns>
    RotationEntry? NextMap();

    /// <summary>Decides the match now on the scores as they stand: its card, and the vote for the next map.</summary>
    void EndMatch()
    {
    }

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
        "where                          every player's team, place, health and gun",
        "tp <player> <other> [metres]   put a player in front of another, facing it (5 m)",
        "sv_shotlog 0|1                 log every round a player fires at the others (on)",
        "mp_buytime <seconds>           how long a round's buy time lasts, from the next round (20; 0 for none)",
        "mp_timelimit <minutes>         how long a deathmatch lasts, from the next map (0 for the mode's ten)",
        "mp_roundendtime <seconds>      a round's result shown before the next (5); mp_bombendtime after a blast (10)",
        "mp_moneystart|mp_moneycap <$>  start money (1000) and the most a player holds (15000)",
        "mp_moneykill|mp_moneyteamkill  pay for a kill (800) and the fine for a team kill (4000)",
        "mp_moneywin|mp_moneylose <$>   a round's pay to its winners (1250) and losers (750)",
        "mp_moneyplant <$>              the bomb planter's pay (300)",
        "<setting>                      a setting's value, range and what it does",
        "ban <player> [minutes] [reason] put a player out and keep its key out (0 or none: for ever)",
        "unban <name|id>                lift a ban",
        "bans                           the bans in force",
        "map <mission> [mode]           go on to a mission now",
        "next                           go on to the rotation's next map",
        "endmatch                       decide the match now; the players vote the next map",
        "mode <mode>                    play a mode from the next map",
        "rotation                       the maps played in turn",
        "demo_save [name]               save the session so far as a demo in demos/ (record_demos 1)",
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
            "where" => server.Where() is { Count: > 0 } where ? where : ["No one is playing."],
            "tp" when words.Length > 2 => [server.Teleport(
                words[1],
                words[2],
                words.Length > 3 && double.TryParse(words[3], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double metres) ? Math.Clamp(metres, 0.5, 100) : 5)],
            "endmatch" => EndMatch(),
            "next" => server.NextMap() is { } next ? [$"Going on to mission {next.Mission} ({ModeOf(next)})."] : ["There is no rotation; use map <mission> [mode]."],
            "mode" when words.Length > 1 => server.UseMode(words[1]) ? [$"{words[1].ToLowerInvariant()} is played from the next map."] : [$"'{words[1]}' is not a mode: {string.Join(", ", Game.Multiplayer.Modes.GameModes.All)}."],
            "rotation" => Rotation(),
            "demo_save" => SaveDemo(words.Length > 1 ? words[1] : null),
            "quit" or "exit" or "stop" => Quit(),
            _ when ServerConfig.ServerSetting(words[0]) is { } setting => Setting(setting, words.Length > 1 ? words[1] : null),
            _ => [$"Unknown command or missing argument: {words[0]}. Type help."],
        };
    }

    // A server setting shown, or set to a value.
    private static string[] Setting(Core.Configuration.ICVar setting, string? value)
    {
        if (value is null)
        {
            return [$"{setting.Name} is {setting.ValueText} (default {setting.DefaultText}{(setting.RangeText.Length > 0 ? $", {setting.RangeText}" : string.Empty)}).", setting.Help];
        }

        return setting.TrySetText(value, out string? why) ? [$"{setting.Name} is {setting.ValueText}."] : [$"{setting.Name}: {why ?? "not a value it takes"}."];
    }

    // A demo saved in the demos folder beside the server, named as asked or by the time.
    private string[] SaveDemo(string? name)
    {
        string file = string.Concat((name ?? DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss", CultureInfo.InvariantCulture)).Split(Path.GetInvalidFileNameChars()));
        string path = Path.Combine(AppContext.BaseDirectory, "demos", file.EndsWith(Game.Multiplayer.DemoFile.Extension, StringComparison.OrdinalIgnoreCase) ? file : file + Game.Multiplayer.DemoFile.Extension);
        return server.SaveDemo(path) is { } problem ? [$"Not saved: {problem}."] : [$"Saved {path}."];
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

    private string[] EndMatch()
    {
        server.EndMatch();
        return ["The match is decided; the players vote the next map."];
    }

    private string[] Quit()
    {
        server.Quit();
        return ["Stopping."];
    }

    private string ModeOf(RotationEntry entry) => entry.Mode.Length > 0 ? entry.Mode : server.Mode;
}
