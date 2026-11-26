using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;

namespace OpenIGI.Web;

/// <summary>Where the game sends the page: another tab for a link, or the page itself into the other game.</summary>
[SupportedOSPlatform("browser")]
internal static partial class WebLinks
{
    /// <summary>The name main.js registers the page's own imports under.</summary>
    public const string PageModule = "openigi-page";

    /// <summary>Opens a page in a new tab of the player's browser: the Discord sign-in, from the profile page.</summary>
    /// <param name="url">Its address; only https is opened.</param>
    [JSImport("openLink", "openigi-platform")]
    public static partial void Open(string url);

    /// <summary>Reloads the page into another game, once the player's files are kept.</summary>
    /// <param name="titleId">The game's id.</param>
    /// <returns>Whether the page is going there.</returns>
    /// <remarks>
    /// The page holds one game's files at a time, so the switcher's other game is opened anew -- its files downloaded
    /// once and kept, like the first's. The player's own folder (the sign-in, the profiles, the settings) is the same
    /// for both games and goes with them.
    /// </remarks>
    [JSImport("openGame", PageModule)]
    public static partial bool OpenGame(string titleId);
}
