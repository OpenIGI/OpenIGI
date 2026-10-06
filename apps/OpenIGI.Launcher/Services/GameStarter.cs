using System.Diagnostics;
using System.Text;
using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Titles;

namespace OpenIGI.Launcher.Services;

/// <summary>Finds the original game's data and starts the installed build against it.</summary>
/// <remarks>
/// OpenIGI ships none of the original game's files; it reads the player's own installation. The launcher
/// passes that folder and the title explicitly (<c>-title igi1 -game &lt;folder&gt;</c>, the switches
/// <c>apps/OpenIGI.Desktop</c> takes), so the game never has to guess which installation is meant.
/// </remarks>
internal static class GameStarter
{
    /// <summary>The variable that names the content profile: each game's load order of content packs.</summary>
    /// <remarks>
    /// A variable rather than an option, because the launcher may start a build from before load orders, and an
    /// unknown option stops the game where an unknown variable is ignored.
    /// </remarks>
    public const string ContentProfileVariable = OpenIGI.Core.Content.ContentProfile.EnvironmentVariable;

    /// <summary>The variable a build from before load orders reads its one folder of replacement assets from.</summary>
    /// <remarks>
    /// Still set, because the installed build may be that old; a build that reads the profile ignores it while a
    /// profile is given, so it can never put one pack above the player's order.
    /// </remarks>
    public const string LegacyOverridesVariable = "OPENIGI_OVERRIDES";

    /// <summary>The name a single-file game build is installed under, for this platform.</summary>
    public static string ExecutableName { get; } = OperatingSystem.IsWindows() ? "OpenIGI.Desktop.exe" : "OpenIGI.Desktop";

    /// <summary>The game's executable inside an installed build, for this platform.</summary>
    /// <param name="buildFolder">The build's version folder.</param>
    /// <returns>The path to start.</returns>
    public static string Executable(string buildFolder) =>
        OperatingSystem.IsWindows() ? Path.Combine(buildFolder, "OpenIGI.Desktop.exe")
        : OperatingSystem.IsMacOS() ? Path.Combine(buildFolder, "OpenIGI.app", "Contents", "MacOS", "OpenIGI.Desktop")
        : Path.Combine(buildFolder, "OpenIGI.Desktop");

    /// <summary>Whether a folder holds an installation of a title.</summary>
    /// <param name="title">The title.</param>
    /// <param name="folder">The folder.</param>
    /// <returns>Whether the title's signature file is there.</returns>
    public static bool IsInstallation(GameTitle title, string? folder) =>
        !string.IsNullOrWhiteSpace(folder) && File.Exists(Path.Combine(folder.Trim().Trim('"'), title.SignatureFile));

    /// <summary>Finds a title's installation: the folder the player chose, else the usual places.</summary>
    /// <param name="title">The title.</param>
    /// <param name="chosen">The folder in the settings, if any.</param>
    /// <returns>An installation folder, or <see langword="null"/> when none was found.</returns>
    public static string? Locate(GameTitle title, string? chosen)
    {
        ArgumentNullException.ThrowIfNull(title);
        if (IsInstallation(title, chosen))
        {
            return chosen!.Trim().Trim('"');
        }

        string? variable = Environment.GetEnvironmentVariable(title == GameTitle.Igi2 ? "IGI2_GAME_PATH" : "IGI_GAME_PATH");
        return Candidates(title).Prepend(variable).FirstOrDefault(folder => IsInstallation(title, folder));
    }

    /// <summary>The arguments the game is started with.</summary>
    /// <param name="title">The title to run.</param>
    /// <param name="gameFolder">The original game's folder.</param>
    /// <param name="extra">The player's own launch arguments.</param>
    /// <returns>The argument list.</returns>
    public static IReadOnlyList<string> Arguments(GameTitle title, string gameFolder, string? extra)
    {
        ArgumentNullException.ThrowIfNull(title);
        return ["-title", title.Id, "-game", gameFolder, .. Split(extra)];
    }

    /// <summary>Starts the game.</summary>
    /// <param name="buildFolder">The installed build.</param>
    /// <param name="arguments">The arguments, from <see cref="Arguments"/>.</param>
    /// <param name="environment">
    /// Variables set for the game on top of the launcher's own, such as <see cref="ContentProfileVariable"/> naming the
    /// content pack the game reads its replacement assets from; <see langword="null"/> for none.
    /// </param>
    /// <param name="followOutput">
    /// Whether the game's output is redirected for <see cref="LauncherLog.Follow"/> to read, and asked for in
    /// <see cref="LogWire"/> lines. Leave it off when nothing will read it.
    /// </param>
    /// <returns>The game's process.</returns>
    /// <exception cref="FileNotFoundException">The build has no executable for this platform.</exception>
    public static Process Start(
        string buildFolder,
        IReadOnlyList<string> arguments,
        IReadOnlyDictionary<string, string>? environment = null,
        bool followOutput = false)
    {
        string executable = Executable(buildFolder);
        if (!File.Exists(executable))
        {
            throw new FileNotFoundException("The installed build has no game executable for this platform.", executable);
        }

        if (!OperatingSystem.IsWindows())
        {
            // An archive extracted by an older tar, or copied by hand, can lose the executable bit.
            UnixFileMode mode = File.GetUnixFileMode(executable);
            File.SetUnixFileMode(executable, mode | UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute);
        }

        // No console window, whatever the build: a build from before the game became a windowed program would
        // otherwise open one beside it.
        ProcessStartInfo start = new(executable)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(executable)!,
            RedirectStandardOutput = followOutput,
            RedirectStandardError = followOutput,
        };
        foreach (string argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        foreach ((string name, string value) in environment ?? new Dictionary<string, string>())
        {
            start.Environment[name] = value;
        }

        if (followOutput)
        {
            start.Environment[LogWire.StreamVariable] = LogWire.StreamValue;
        }

        return Process.Start(start) ?? throw new InvalidOperationException("The game did not start.");
    }

    /// <summary>Splits a launch-argument line the way a shell would: on spaces, keeping quoted runs together.</summary>
    /// <param name="line">The line.</param>
    /// <returns>The arguments.</returns>
    public static IReadOnlyList<string> Split(string? line)
    {
        List<string> parts = [];
        if (string.IsNullOrWhiteSpace(line))
        {
            return parts;
        }

        StringBuilder current = new();
        bool quoted = false;
        bool any = false;
        foreach (char c in line)
        {
            if (c == '"')
            {
                quoted = !quoted;
                any = true;
            }
            else if (char.IsWhiteSpace(c) && !quoted)
            {
                if (any)
                {
                    parts.Add(current.ToString());
                    current.Clear();
                    any = false;
                }
            }
            else
            {
                current.Append(c);
                any = true;
            }
        }

        if (any)
        {
            parts.Add(current.ToString());
        }

        return parts;
    }

    // Where retail installs of the two games land by default, and the folders people commonly use.
    internal static IEnumerable<string> Candidates(GameTitle title)
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string[] names = title == GameTitle.Igi2
            ? [Path.Combine("IGI 2 Covert Strike", "pc"), Path.Combine("I.G.I 2 Covert Strike", "pc"), Path.Combine("IGI2", "pc")]
            : ["Project IGI", Path.Combine("Project IGI", "pc"), Path.Combine("I'm Going In", "pc"), "IGI"];
        string[] roots = OperatingSystem.IsWindows()
            ? [
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                @"C:\Games",
                Path.Combine(home, "Games"),
            ]
            : [Path.Combine(home, "Games"), home];

        foreach (string root in roots.Where(static r => !string.IsNullOrEmpty(r)))
        {
            foreach (string name in names)
            {
                yield return Path.Combine(root, name);
            }
        }
    }
}
