using System.Text.RegularExpressions;
using OpenIGI.Core.Diagnostics;

namespace OpenIGI.Launcher.Services;

/// <summary>
/// The <c>openigi://</c> links an invite page opens the installed game with: <c>openigi://join/CODE</c> for a friend's room,
/// <c>openigi://party/CODE</c> for a friend's party. The launcher registers itself for them, and starts the game into the
/// room or the party when one is how it was opened.
/// </summary>
internal static partial class InviteLinks
{
    /// <summary>The link scheme.</summary>
    public const string Scheme = "openigi";

    /// <summary>Reads a link into what the game is started with, or <see langword="null"/> for anything else.</summary>
    /// <param name="link">The link, as the launcher was opened with it.</param>
    /// <returns><c>-join CODE</c> for a room, <c>+party_join CODE</c> for a party, or <see langword="null"/>.</returns>
    public static IReadOnlyList<string>? Arguments(string? link)
    {
        if (link is null || Link().Match(link.Trim()) is not { Success: true } match)
        {
            return null;
        }

        string code = match.Groups["code"].Value.ToUpperInvariant();
        return match.Groups["kind"].Value.Equals("party", StringComparison.OrdinalIgnoreCase) ? ["+party_join", code] : ["-join", code];
    }

    /// <summary>Finds the invite link among the arguments the launcher was started with.</summary>
    /// <param name="args">The arguments.</param>
    /// <returns>What the game is started with, or <see langword="null"/>.</returns>
    public static IReadOnlyList<string>? FromCommandLine(IEnumerable<string> args) =>
        args.Select(Arguments).FirstOrDefault(found => found is not null);

    /// <summary>Registers this launcher for <c>openigi://</c> links, for the user alone; on Windows, where links are registered so.</summary>
    /// <param name="logger">Where a failure is told.</param>
    public static void Register(Core.Diagnostics.IOpenIgiLogger logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        if (!OperatingSystem.IsWindows() || Environment.ProcessPath is not { } executable)
        {
            return;
        }

        try
        {
            using Microsoft.Win32.RegistryKey key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey($@"Software\Classes\{Scheme}");
            key.SetValue(string.Empty, "URL:OpenIGI invite");
            key.SetValue("URL Protocol", string.Empty);
            using Microsoft.Win32.RegistryKey command = key.CreateSubKey(@"shell\open\command");
            string wanted = $"\"{executable}\" \"%1\"";
            if (command.GetValue(string.Empty) as string != wanted)
            {
                command.SetValue(string.Empty, wanted);
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            logger.Warning("Invites", $"Couldn't register openigi:// links: {ex.Message}");
        }
    }

    // openigi://join/CODE or openigi://party/CODE, a code as the master server makes them; a trailing slash is a browser's.
    [GeneratedRegex("^openigi://(?<kind>join|party)/(?<code>[ABCDEFGHJKMNPQRSTUVWXYZ23456789]{6})/?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Link();
}
