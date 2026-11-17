using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;

namespace OpenIGI.Web;

/// <summary>Opens a page in a new tab of the player's browser: the Discord sign-in, from the profile page.</summary>
[SupportedOSPlatform("browser")]
internal static partial class WebLinks
{
    /// <summary>Opens a page.</summary>
    /// <param name="url">Its address; only https is opened.</param>
    [JSImport("openLink", "openigi-platform")]
    public static partial void Open(string url);
}
