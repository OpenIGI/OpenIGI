using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using OpenIGI.Game.World;
using OpenIGI.Host.Infrastructure;

namespace OpenIGI.Web;

/// <summary>The browser's load gate: a mission loads once the page has put its folder in place.</summary>
/// <remarks>
/// The page reads the small files of every mission up front and a mission's large ones -- its terrain,
/// lightmaps and textures -- when it is chosen. Being asked is the page's cue to start reading; it answers
/// yes once every file is where the game reads it.
/// </remarks>
[SupportedOSPlatform("browser")]
internal sealed partial class WebLoadGate : ILoadGate
{
    /// <summary>The name the page registers game-files.js's imports under.</summary>
    public const string ModuleName = "openigi-files";

    /// <inheritdoc/>
    public bool IsReady(MissionDescriptor mission, out string? waitingFor)
    {
        ArgumentNullException.ThrowIfNull(mission);

        if (MissionReady(mission.MissionDirectory))
        {
            waitingFor = null;
            return true;
        }

        waitingFor = $"the files of {mission.MissionDirectory}";
        return false;
    }

    [JSImport("missionReady", ModuleName)]
    private static partial bool MissionReady(string directory);
}
