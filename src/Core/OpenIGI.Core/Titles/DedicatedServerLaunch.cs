using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace OpenIGI.Core.Titles;

/// <summary>
/// A dedicated server started on this machine from a form -- the game's menu or the launcher's -- rather than by hand: its
/// settings written as a <c>server.cfg</c> the server reads, and the server's own executable started headless beside it.
/// </summary>
/// <remarks>Both front ends start a server through this, so a server hosted from either is the same server run the same way.</remarks>
public sealed record DedicatedServerLaunch
{
    /// <summary>The server's executable name, without the platform's extension.</summary>
    public const string ExecutableName = "OpenIGI.Server";

    /// <summary>Gets the game it runs, as <c>igi1</c>.</summary>
    public string Title { get; init; } = "igi1";

    /// <summary>Gets its name in the browser.</summary>
    public string Name { get; init; } = "OpenIGI server";

    /// <summary>Gets its UDP port.</summary>
    public int Port { get; init; } = 26001;

    /// <summary>Gets how many players it takes, 1-32.</summary>
    public int MaxPlayers { get; init; } = 16;

    /// <summary>Gets the password to join it, or empty for anyone.</summary>
    public string Password { get; init; } = string.Empty;

    /// <summary>Gets the remote console's password, or empty for none.</summary>
    public string RconPassword { get; init; } = string.Empty;

    /// <summary>Gets the mission it plays.</summary>
    public int Mission { get; init; } = 1;

    /// <summary>Gets the mode it plays, as <c>tdm</c>.</summary>
    public string Mode { get; init; } = "tdm";

    /// <summary>Gets the region it says it plays from, or empty.</summary>
    public string Region { get; init; } = string.Empty;

    /// <summary>Gets a value indicating whether it lists itself on the master server.</summary>
    public bool Public { get; init; }

    /// <summary>Gets a value indicating whether it also hosts a WebRTC room, for browser players.</summary>
    public bool Room { get; init; } = true;

    /// <summary>Gets the installation it reads the game's data from, or <see langword="null"/> for the one it finds.</summary>
    public string? GamePath { get; init; }

    /// <summary>Gets where a server started from a form keeps its settings, log and bans: one folder a game, in the player's own.</summary>
    /// <param name="title">The game, as <c>igi1</c>.</param>
    /// <returns>The folder.</returns>
    public static string FolderFor(string title) => Path.Combine(Configuration.SettingsPaths.GetUserDirectory(), "server", title);

    /// <summary>Writes the settings as the server's <c>server.cfg</c>.</summary>
    /// <returns>The file's text.</returns>
    public string ToConfig()
    {
        StringBuilder text = new();
        text.AppendLine("# Written by OpenIGI's Host server form; edit it there, or copy it and run the server by hand.");
        Line(text, "name", Quote(Name));
        Line(text, "port", Port.ToString(CultureInfo.InvariantCulture));
        Line(text, "maxplayers", Math.Clamp(MaxPlayers, 1, 32).ToString(CultureInfo.InvariantCulture));
        Line(text, "password", Quote(Password));
        Line(text, "rcon_password", Quote(RconPassword));
        if (Region.Length > 0)
        {
            Line(text, "region", Region);
        }

        Line(text, "public", Public ? "1" : "0");
        Line(text, "room", Room ? "1" : "0");
        Line(text, "map", string.Create(CultureInfo.InvariantCulture, $"{Mission} {Mode}"));
        return text.ToString();
    }

    /// <summary>Finds the server's executable: beside the given folder (a release ships it beside the game), or where
    /// <c>OPENIGI_SERVER</c> names.</summary>
    /// <param name="besides">The folder to look in first.</param>
    /// <returns>Its path, or <see langword="null"/> when this install has none.</returns>
    public static string? FindServer(string besides)
    {
        string file = OperatingSystem.IsWindows() ? ExecutableName + ".exe" : ExecutableName;
        foreach (string? candidate in new[] { Environment.GetEnvironmentVariable("OPENIGI_SERVER"), Path.Combine(besides, file), Path.Combine(besides, "server", file) })
        {
            if (!string.IsNullOrEmpty(candidate) && File.Exists(candidate))
            {
                return Path.GetFullPath(candidate);
            }
        }

        return null;
    }

    /// <summary>Writes the settings into a folder of their own and starts the server on them, with no window.</summary>
    /// <param name="server">The server's executable (<see cref="FindServer"/>).</param>
    /// <param name="folder">Where its <c>server.cfg</c>, log and bans live; made if missing.</param>
    /// <returns>The running server.</returns>
    public Process Start(string server, string folder)
    {
        ArgumentException.ThrowIfNullOrEmpty(server);
        ArgumentException.ThrowIfNullOrEmpty(folder);
        Directory.CreateDirectory(folder);
        string config = Path.Combine(folder, "server.cfg");
        File.WriteAllText(config, ToConfig());

        ProcessStartInfo start = new(server)
        {
            WorkingDirectory = folder,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
        };
        start.ArgumentList.Add("-config");
        start.ArgumentList.Add(config);
        start.ArgumentList.Add("-title");
        start.ArgumentList.Add(Title);
        if (GamePath is { Length: > 0 } game)
        {
            start.ArgumentList.Add("-game");
            start.ArgumentList.Add(game);
        }

        return Process.Start(start) ?? throw new InvalidOperationException($"{server} did not start.");
    }

    /// <summary>Stops a server started here: <c>quit</c> at its console, so it leaves the master's list, and killed if it lingers.</summary>
    /// <param name="process">The server.</param>
    /// <param name="graceMilliseconds">How long it is given to stop by itself.</param>
    public static void Stop(Process process, int graceMilliseconds = 3000)
    {
        ArgumentNullException.ThrowIfNull(process);
        try
        {
            if (process.HasExited)
            {
                return;
            }

            process.StandardInput.WriteLine("quit");
            process.StandardInput.Flush();
            if (!process.WaitForExit(graceMilliseconds))
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or System.ComponentModel.Win32Exception)
        {
            // Gone already.
        }
    }

    private static void Line(StringBuilder text, string key, string value) => text.Append(key).Append(' ').AppendLine(value);

    private static string Quote(string value) => $"\"{value.Replace("\"", "'", StringComparison.Ordinal)}\"";
}
