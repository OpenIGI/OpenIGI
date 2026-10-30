using System.Globalization;

namespace OpenIGI.Server;

/// <summary>What the dedicated server is told on its command line, beside the host's own options.</summary>
/// <param name="Port">The UDP port it listens on.</param>
/// <param name="Name">The name it shows in the server list.</param>
/// <param name="MaxPlayers">How many players may be connected at once.</param>
/// <param name="Password">The password joining takes, or empty for none.</param>
/// <param name="Mode">The game mode it plays.</param>
internal sealed record ServerOptions(int Port, string Name, int MaxPlayers, string Password, string Mode)
{
    /// <summary>The usage text for the server's own options.</summary>
    public const string Usage =
        "Server options:\n" +
        "  -port <n>         UDP port to listen on (default 26001)\n" +
        "  -name <text>      name shown in the server list\n" +
        "  -maxplayers <n>   most players connected at once, 1-32 (default 32)\n" +
        "  -password <text>  password needed to join (default none)\n" +
        "  -mode <text>      game mode (default objective)\n" +
        "Host options such as -title, -game and -mission are passed through.";

    /// <summary>Takes the server's own options out of a command line, leaving the host's.</summary>
    /// <param name="args">The whole command line.</param>
    /// <param name="hostArgs">What is left for the host.</param>
    /// <returns>The server's options.</returns>
    /// <exception cref="ArgumentException">An option is missing its value or the value does not parse.</exception>
    public static ServerOptions Parse(string[] args, out string[] hostArgs)
    {
        int port = Net.Udp.UdpTransport.DefaultServerPort;
        string name = "OpenIGI server";
        int maxPlayers = 32;
        string password = string.Empty;
        string mode = "objective";
        List<string> rest = [];
        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];
            string Value() => i + 1 < args.Length ? args[++i] : throw new ArgumentException($"{arg} needs a value.");
            switch (arg.ToUpperInvariant())
            {
                case "-PORT":
                    port = int.Parse(Value(), CultureInfo.InvariantCulture);
                    break;
                case "-NAME":
                    name = Value();
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
                    rest.Add(arg);
                    break;
            }
        }

        if (port is < 1 or > 65535)
        {
            throw new ArgumentException("-port must be 1-65535.");
        }

        hostArgs = [.. rest];
        return new ServerOptions(port, name, maxPlayers, password, mode);
    }
}
