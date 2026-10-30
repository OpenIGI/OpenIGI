using System.Globalization;

namespace OpenIGI.Host.Multiplayer;

/// <summary>The multiplayer options of a command line, the game's and the dedicated server's alike.</summary>
/// <param name="Connect">The server to join, as <c>host[:port]</c>, or <see langword="null"/>.</param>
/// <param name="Listen">Whether to host a listen server.</param>
/// <param name="Port">The UDP port a server listens on.</param>
/// <param name="PlayerName">The name played under.</param>
/// <param name="ServerName">The name a server shows in the list.</param>
/// <param name="MaxPlayers">How many clients a server takes.</param>
/// <param name="Password">The password: a server's to require, a client's to give.</param>
/// <param name="Mode">The game mode a server plays.</param>
/// <param name="Join">The room code to join, or <see langword="null"/>.</param>
/// <param name="Host">Whether to host through a room: friends anywhere join by its code, no port opened.</param>
/// <param name="Public">Whether a hosted room is listed in the server browser, rather than reached by its code alone.</param>
public sealed record SessionArguments(
    string? Connect,
    bool Listen,
    int Port,
    string PlayerName,
    string ServerName,
    int MaxPlayers,
    string Password,
    string Mode,
    string? Join = null,
    bool Host = false,
    bool Public = false)
{
    /// <summary>The UDP port a server listens on unless told otherwise, as the original's <c>svport</c>.</summary>
    public const int DefaultPort = 26001;

    /// <summary>The usage text.</summary>
    public const string Usage =
        "Multiplayer options:\n" +
        "  -host                   host a room on the mission being played; friends join by its code\n" +
        "  -public                 list the hosted room in the server browser\n" +
        "  -join <code>            join a room by its code\n" +
        "  -connect <host[:port]>  join a server by address (LAN or dedicated)\n" +
        "  -listen                 host a listen server on a UDP port, for LAN\n" +
        "  -port <n>               UDP port a server listens on (default 26001)\n" +
        "  -name <text>            player name (default Jones)\n" +
        "  -servername <text>      name shown in the server list\n" +
        "  -maxplayers <n>         most clients at once, 1-32 (default 32)\n" +
        "  -password <text>        password a server requires, or a client gives\n" +
        "  -mode <text>            game mode (default objective)";

    /// <summary>Takes the multiplayer options out of a command line, leaving the rest.</summary>
    /// <param name="args">The whole command line.</param>
    /// <param name="rest">What is left.</param>
    /// <returns>The options.</returns>
    /// <exception cref="ArgumentException">An option is missing its value or the value does not parse.</exception>
    public static SessionArguments Parse(string[] args, out string[] rest)
    {
        ArgumentNullException.ThrowIfNull(args);
        string? connect = null;
        bool listen = false;
        int port = DefaultPort;
        string playerName = "Jones";
        string serverName = "OpenIGI server";
        int maxPlayers = 32;
        string password = string.Empty;
        string mode = "objective";
        string? join = null;
        bool host = false;
        bool listed = false;
        List<string> left = [];
        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];
            string Value() => i + 1 < args.Length ? args[++i] : throw new ArgumentException($"{arg} needs a value.");
            switch (arg.ToUpperInvariant())
            {
                case "-CONNECT":
                    connect = Value();
                    break;
                case "-LISTEN":
                    listen = true;
                    break;
                case "-HOST":
                    host = true;
                    break;
                case "-PUBLIC":
                    listed = true;
                    break;
                case "-JOIN":
                    join = Value();
                    break;
                case "-PORT":
                    port = int.Parse(Value(), CultureInfo.InvariantCulture);
                    break;
                case "-NAME":
                    playerName = Value();
                    break;
                case "-SERVERNAME":
                    serverName = Value();
                    break;
                case "-MAXPLAYERS":
                    maxPlayers = Math.Clamp(int.Parse(Value(), CultureInfo.InvariantCulture), 1, 32);
                    break;
                case "-PASSWORD":
                    password = Value();
                    break;
                case "-MODE":
                    mode = Value();
                    break;
                default:
                    left.Add(arg);
                    break;
            }
        }

        if (port is < 1 or > 65535)
        {
            throw new ArgumentException("-port must be 1-65535.");
        }

        rest = [.. left];
        return new SessionArguments(connect, listen, port, playerName, serverName, maxPlayers, password, mode, join, host, listed);
    }
}
