using System.Globalization;

namespace OpenIGI.Host.Dedicated;

/// <summary>A map of a dedicated server's rotation: a mission and the mode it is played under.</summary>
/// <param name="Mission">The mission's id.</param>
/// <param name="Mode">The mode's id.</param>
public readonly record struct RotationEntry(int Mission, string Mode);

/// <summary>
/// A dedicated server's settings, from its <c>server.cfg</c>: one setting a line, <c>key value</c>, a <c>#</c> or <c>//</c> starting
/// a comment, a value in quotes keeping its spaces. <c>map</c> lines, one a map, make the rotation, played in order and round again.
/// </summary>
/// <remarks>
/// <code>
/// name "OpenIGI | Search &amp; Destroy"
/// port 26001
/// maxplayers 16
/// password ""
/// rcon_password "change-me"
/// motd "Be nice. Bombs at A and B."
/// public 1          # listed on the master server
/// room 1            # also hosts a WebRTC room, so browsers can join
/// mp_buytime 30     # a server setting by its name: seconds to buy in a round
/// map 1 sd
/// map 2 tdm
/// </code>
/// A line naming one of <see cref="Game.Multiplayer.NetCVars.ServerSettings"/> sets it as the file is read.
/// The command line wins over the file: a server started with <c>-servername</c> or <c>-port</c> keeps them.
/// </remarks>
public sealed record ServerConfig
{
    /// <summary>The file's name beside the server, unless <c>-config</c> names another.</summary>
    public const string DefaultFileName = "server.cfg";

    /// <summary>Gets the name shown in the server list.</summary>
    public string Name { get; init; } = "OpenIGI server";

    /// <summary>Gets the UDP port.</summary>
    public int Port { get; init; } = 26001;

    /// <summary>Gets the most players at once.</summary>
    public int MaxPlayers { get; init; } = 16;

    /// <summary>Gets the most players from one internet address at once, 0 for any (<c>maxperaddress</c>).</summary>
    /// <remarks>Raised for a LAN event or players behind a carrier's shared address; it is what stops one machine filling the server.</remarks>
    public int MaxPerAddress { get; init; } = 4;

    /// <summary>Gets the password players give, or empty for none.</summary>
    public string Password { get; init; } = string.Empty;

    /// <summary>Gets the password the remote console takes, or empty for no remote console.</summary>
    public string RconPassword { get; init; } = string.Empty;

    /// <summary>Gets the line each player is told as it joins, or empty.</summary>
    public string Motd { get; init; } = string.Empty;

    /// <summary>Gets the region the server lists itself in, as <c>eu</c>, or empty.</summary>
    public string Region { get; init; } = string.Empty;

    /// <summary>Gets a value indicating whether the server lists itself on the master server.</summary>
    public bool Public { get; init; } = true;

    /// <summary>Gets a value indicating whether the server also hosts a WebRTC room, so browsers can join it.</summary>
    public bool Room { get; init; } = true;

    /// <summary>Gets the master server.</summary>
    public Uri Master { get; init; } = Net.Rooms.RoomTransport.DefaultMaster;

    /// <summary>Gets how long a player may stand idle before it is put off, in seconds; 0 never (<c>afk_kick</c>).</summary>
    public int IdleKickSeconds { get; init; }

    /// <summary>
    /// Gets a value indicating whether the server keeps a recording of each map from its start, for <c>demo_save</c>
    /// (<c>record_demos</c>): off by default, as a long-running server would hold up to 128 MB of it.
    /// </summary>
    public bool RecordDemos { get; init; }

    /// <summary>Gets the ban list's file, beside the server unless rooted.</summary>
    public string BansFile { get; init; } = "bans.json";

    /// <summary>Gets the log file, or empty for the console alone.</summary>
    public string LogFile { get; init; } = "server.log";

    /// <summary>Gets the maps played in turn; empty for the command line's one map, played again and again.</summary>
    public IReadOnlyList<RotationEntry> Rotation { get; init; } = [];

    /// <summary>Reads a file, or the defaults when there is none.</summary>
    /// <param name="path">The file.</param>
    /// <param name="problems">Each line that could not be read, to be told.</param>
    /// <returns>The settings.</returns>
    public static ServerConfig Load(string path, List<string> problems)
    {
        ArgumentNullException.ThrowIfNull(problems);
        return File.Exists(path) ? Parse(File.ReadAllLines(path), problems) : new ServerConfig();
    }

    /// <summary>Reads settings from lines.</summary>
    /// <param name="lines">The lines.</param>
    /// <param name="problems">Each line that could not be read.</param>
    /// <returns>The settings.</returns>
    public static ServerConfig Parse(IEnumerable<string> lines, List<string> problems)
    {
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentNullException.ThrowIfNull(problems);
        ServerConfig config = new();
        List<RotationEntry> rotation = [];
        int number = 0;
        foreach (string raw in lines)
        {
            number++;
            string[] words = Words(raw);
            if (words.Length == 0)
            {
                continue;
            }

            string key = words[0].ToLowerInvariant();
            string value = words.Length > 1 ? words[1] : string.Empty;
            bool ok = true;
            switch (key)
            {
                case "name" or "hostname":
                    config = config with { Name = value };
                    break;
                case "port":
                    ok = int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int port) && port is > 0 and < 65536;
                    config = ok ? config with { Port = port } : config;
                    break;
                case "maxplayers":
                    ok = int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int players) && players is >= 1 and <= 32;
                    config = ok ? config with { MaxPlayers = players } : config;
                    break;
                case "maxperaddress":
                    ok = int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int perAddress) && perAddress is >= 0 and <= 32;
                    config = ok ? config with { MaxPerAddress = perAddress } : config;
                    break;
                case "password":
                    config = config with { Password = value };
                    break;
                case "rcon_password":
                    config = config with { RconPassword = value };
                    break;
                case "afk_kick" or "idle_kick":
                    ok = int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int idle) && idle >= 0;
                    config = ok ? config with { IdleKickSeconds = idle } : config;
                    break;
                case "motd":
                    config = config with { Motd = value };
                    break;
                case "region":
                    config = config with { Region = value.ToLowerInvariant() };
                    break;
                case "public":
                    ok = Flag(value, out bool listed);
                    config = config with { Public = listed };
                    break;
                case "room":
                    ok = Flag(value, out bool room);
                    config = config with { Room = room };
                    break;
                case "record_demos":
                    ok = Flag(value, out bool record);
                    config = config with { RecordDemos = record };
                    break;
                case "master":
                    ok = Uri.TryCreate(value, UriKind.Absolute, out Uri? master) && master.Scheme is "https" or "http";
                    config = ok ? config with { Master = master! } : config;
                    break;
                case "bans":
                    config = config with { BansFile = value };
                    break;
                case "log":
                    config = config with { LogFile = value };
                    break;
                case "map":
                    ok = int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int mission) && mission > 0
                        && (words.Length < 3 || Game.Multiplayer.Modes.GameModes.All.Contains(words[2], StringComparer.OrdinalIgnoreCase));
                    if (ok)
                    {
                        rotation.Add(new RotationEntry(mission, words.Length > 2 ? words[2].ToLowerInvariant() : string.Empty));
                    }

                    break;
                default:
                    if (ServerSetting(key) is { } setting)
                    {
                        if (!setting.TrySetText(value, out string? why))
                        {
                            problems.Add(string.Create(CultureInfo.InvariantCulture, $"line {number}: {key}: {why ?? "not a value it takes"}"));
                        }

                        continue;
                    }

                    ok = false;
                    break;
            }

            if (!ok)
            {
                problems.Add(string.Create(CultureInfo.InvariantCulture, $"line {number}: '{raw.Trim()}' is not understood"));
            }
        }

        return config with { Rotation = rotation };
    }

    /// <summary>Finds a server setting by its name.</summary>
    /// <param name="name">The name, in any case.</param>
    /// <returns>The setting, or <see langword="null"/> when it is not one a server's operator may set.</returns>
    internal static Core.Configuration.ICVar? ServerSetting(string name) =>
        Game.Multiplayer.NetCVars.ServerSettings.FirstOrDefault(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase));

    // A line's words: split on spaces, a quoted value kept whole, a comment cut off.
    internal static string[] Words(string line)
    {
        List<string> words = [];
        int i = 0;
        while (i < line.Length)
        {
            char c = line[i];
            if (char.IsWhiteSpace(c))
            {
                i++;
                continue;
            }

            // A comment: '//', or '#' opening the line or standing apart -- '#3' is a console's slot, not a comment.
            bool hash = c == '#' && (words.Count == 0 || i + 1 >= line.Length || char.IsWhiteSpace(line[i + 1]));
            if (hash || (c == '/' && i + 1 < line.Length && line[i + 1] == '/'))
            {
                break;
            }

            if (c == '"')
            {
                int end = line.IndexOf('"', i + 1);
                end = end < 0 ? line.Length : end;
                words.Add(line[(i + 1)..end]);
                i = end + 1;
                continue;
            }

            int start = i;
            while (i < line.Length && !char.IsWhiteSpace(line[i]))
            {
                i++;
            }

            words.Add(line[start..i]);
        }

        return [.. words];
    }

    private static bool Flag(string value, out bool flag)
    {
        flag = value is "1" or "true" or "yes" or "on";
        return flag || value is "0" or "false" or "no" or "off";
    }
}
