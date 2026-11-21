using System.Globalization;
using OpenIGI.Core.Diagnostics;
using System.Text.RegularExpressions;

namespace OpenIGI.Launcher.Services;

/// <summary>What an <c>openigi://</c> link asks: which game to start, and what to start it with.</summary>
/// <param name="Game">The game, by title id: <c>igi1</c> or <c>igi2</c>.</param>
/// <param name="Arguments">What the game is started with: <c>-join CODE</c>, <c>+party_join CODE</c> or <c>-connect host:port</c>.</param>
internal sealed record Invite(string Game, IReadOnlyList<string> Arguments);

/// <summary>
/// The <c>openigi://</c> links the website and invite pages open the installed game with: <c>openigi://join/CODE</c> for a room,
/// <c>openigi://party/CODE</c> for a friend's party, and <c>openigi://connect/HOST:PORT</c> for a dedicated server by its
/// address; each may say its game (<c>?game=igi2</c>; Project I.G.I. when it doesn't). The launcher registers itself for them,
/// and starts that game into the room, the party or the server when one is how it was opened.
/// </summary>
internal static partial class InviteLinks
{
    /// <summary>The link scheme.</summary>
    public const string Scheme = "openigi";

    // The games a link may name, and the one it means when it names none (an invite page from before links named one).
    private static readonly string[] Games = ["igi1", "igi2"];

    /// <summary>Reads a link, or <see langword="null"/> for anything else.</summary>
    /// <param name="link">The link, as the launcher was opened with it.</param>
    /// <returns>The game and what it is started with, or <see langword="null"/>.</returns>
    public static Invite? Read(string? link)
    {
        if (link is null || Link().Match(link.Trim()) is not { Success: true } match)
        {
            return null;
        }

        string game = match.Groups["game"].Success ? match.Groups["game"].Value.ToLowerInvariant() : Games[0];
        if (!Games.Contains(game))
        {
            return null;
        }

        string kind = match.Groups["kind"].Value.ToLowerInvariant();
        string target = match.Groups["target"].Value;
        if (kind == "connect")
        {
            return Address(target) is { } address ? new Invite(game, ["-connect", address]) : null;
        }

        if (!Code().IsMatch(target))
        {
            return null;
        }

        string code = target.ToUpperInvariant();
        return new Invite(game, kind == "party" ? ["+party_join", code] : ["-join", code]);
    }

    /// <summary>Finds the invite link among the arguments the launcher was started with.</summary>
    /// <param name="args">The arguments.</param>
    /// <returns>The invite, or <see langword="null"/>.</returns>
    public static Invite? FromCommandLine(IEnumerable<string> args) => args.Select(Read).FirstOrDefault(found => found is not null);

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

    // A server's address as a link may give it: a host name or an IPv4 address, and a port; anything else (a path, a user, a
    // second colon) is not an address, so a link can only ever have the game connect to a server, never pass it anything more.
    private static string? Address(string target)
    {
        if (HostPort().Match(target) is not { Success: true } match
            || !int.TryParse(match.Groups["port"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out int port)
            || port is < 1 or > 65535)
        {
            return null;
        }

        return string.Create(CultureInfo.InvariantCulture, $"{match.Groups["host"].Value.ToLowerInvariant()}:{port}");
    }

    // openigi://<join|party|connect>/<target>, optionally ?game=<id>; a trailing slash is a browser's.
    [GeneratedRegex(@"^openigi://(?<kind>join|party|connect)/(?<target>[^/?#\s]+)/?(\?game=(?<game>[a-z0-9]{1,8}))?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Link();

    // A code as the master server makes them.
    [GeneratedRegex("^[ABCDEFGHJKMNPQRSTUVWXYZ23456789]{6}$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Code();

    // A host name (letters, digits, dots and dashes) or a dotted IPv4 address, a colon, and a port of up to five digits.
    [GeneratedRegex(@"^(?<host>[A-Za-z0-9](?:[A-Za-z0-9.-]{0,251}[A-Za-z0-9])?):(?<port>[0-9]{1,5})$", RegexOptions.CultureInvariant)]
    private static partial Regex HostPort();
}
