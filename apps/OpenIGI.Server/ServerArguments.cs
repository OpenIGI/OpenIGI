using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using OpenIGI.Host.Dedicated;
using OpenIGI.Host.Multiplayer;

namespace OpenIGI.Server;

/// <summary>How the command line and <c>server.cfg</c> meet: the line names the file, and wins wherever it says something.</summary>
internal static class ServerArguments
{
    /// <summary>Takes <c>-config &lt;file&gt;</c> out of a command line.</summary>
    /// <param name="args">The command line.</param>
    /// <param name="path">The file; left as it was when not named.</param>
    /// <returns>The rest of the line.</returns>
    public static string[] TakeConfig(string[] args, ref string path)
    {
        List<string> rest = [];
        for (int i = 0; i < args.Length; i++)
        {
            if (string.Equals(args[i], "-config", StringComparison.OrdinalIgnoreCase))
            {
                path = i + 1 < args.Length ? args[++i] : throw new ArgumentException("-config needs a file.");
            }
            else if (!string.Equals(args[i], "-console", StringComparison.OrdinalIgnoreCase))
            {
                rest.Add(args[i]);
            }
        }

        return [.. rest];
    }

    /// <summary>Gets whether the command line gave an option.</summary>
    /// <param name="args">The command line.</param>
    /// <param name="option">The option, as <c>-port</c>.</param>
    /// <returns><see langword="true"/> when it did.</returns>
    public static bool Given(string[] args, string option) => args.Any(a => string.Equals(a, option, StringComparison.OrdinalIgnoreCase));

    /// <summary>The file's settings with the command line's put over them.</summary>
    /// <param name="config">The file's.</param>
    /// <param name="session">The command line's multiplayer options.</param>
    /// <param name="args">The command line, to tell what it gave from its defaults.</param>
    /// <returns>The settings run with.</returns>
    public static ServerConfig Merge(ServerConfig config, SessionArguments session, string[] args) => config with
    {
        Name = Given(args, "-servername") ? session.ServerName : config.Name,
        Port = Given(args, "-port") ? session.Port : config.Port,
        MaxPlayers = Given(args, "-maxplayers") ? Math.Clamp(session.MaxPlayers, 1, 32) : config.MaxPlayers,
        Password = Given(args, "-password") ? session.Password : config.Password,
        Public = Given(args, "-public") || config.Public,
    };
}

/// <summary>Windows' timer at a millisecond while the server runs, so its one-millisecond sleeps are one; elsewhere nothing.</summary>
internal sealed partial class TimerResolution : IDisposable
{
    private readonly bool _raised;

    private TimerResolution(bool raised) => _raised = raised;

    /// <summary>Raises the timer's resolution for the life of the object.</summary>
    /// <returns>It.</returns>
    public static TimerResolution Fine() => new(OperatingSystem.IsWindows() && TimeBeginPeriod(1) == 0);

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_raised)
        {
            _ = TimeEndPeriod(1);
        }
    }

    [LibraryImport("winmm.dll", EntryPoint = "timeBeginPeriod")]
    private static partial uint TimeBeginPeriod(uint period);

    [LibraryImport("winmm.dll", EntryPoint = "timeEndPeriod")]
    private static partial uint TimeEndPeriod(uint period);
}

/// <summary>The operator's typed commands, read from the terminal on a thread of their own and queued for the loop.</summary>
internal static class OperatorInput
{
    /// <summary>Starts reading, when there is a terminal to read (a service has none), or when asked to regardless.</summary>
    /// <param name="commands">Where each line goes.</param>
    /// <param name="always">Whether to read even from redirected input (a pipe, a container's attached stdin).</param>
    public static void Start(ConcurrentQueue<string> commands, bool always)
    {
        // The one place the server touches the console directly: its operator's input is not a log, so it is read here.
#pragma warning disable RS0030
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        if (Console.IsInputRedirected && !always)
        {
            return;
        }

        Thread reader = new(() =>
        {
            while (Console.ReadLine() is { } line)
            {
                commands.Enqueue(line);
            }
        })
        {
            IsBackground = true,
            Name = "Server console",
        };
#pragma warning restore RS0030
        reader.Start();
    }
}
