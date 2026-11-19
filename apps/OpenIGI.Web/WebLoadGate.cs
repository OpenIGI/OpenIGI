using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using OpenIGI.Game.World;
using OpenIGI.Host.Infrastructure;

namespace OpenIGI.Web;

/// <summary>The browser's load gate: a mission loads once the page has put its folder in place.</summary>
/// <remarks>
/// The page reads the small files of every mission up front and a mission's large ones -- its terrain,
/// lightmaps and textures -- when it is chosen, with the shared pack of its location (I.G.I. 2's
/// <c>missions/locationN/common</c>). Being asked is the page's cue to start reading; it answers yes once
/// every file is where the game reads it.
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

        // Both are asked every time, so the page starts reading whichever is missing: a location's shared pack is
        // read when the first of its missions is chosen.
        bool mine = MissionReady(mission.MissionDirectory);
        bool common = string.IsNullOrEmpty(mission.CommonPackDirectory) || MissionReady(mission.CommonPackDirectory);
        if (mine && common)
        {
            waitingFor = null;
            return true;
        }

        waitingFor = $"the files of {(mine ? mission.CommonPackDirectory : mission.MissionDirectory)}";
        return false;
    }

    /// <inheritdoc/>
    /// <remarks>The page knows a location's shared pack, or a mission's folder, by its path; any other folder is read up front.</remarks>
    public bool IsInPlace(string directory)
    {
        ArgumentException.ThrowIfNullOrEmpty(directory);
        return MissionReady(directory);
    }

    [JSImport("missionReady", ModuleName)]
    private static partial bool MissionReady(string directory);
}
