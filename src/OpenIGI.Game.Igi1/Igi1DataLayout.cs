using OpenIGI.Engine.Level;
using OpenIGI.Engine.Tasks;
using OpenIGI.Engine.Titles;
using OpenIGI.Engine.Vfs;
using OpenIGI.Game.World;

namespace OpenIGI.Game.Igi1;

/// <summary>Where retail Project I.G.I. keeps its data.</summary>
/// <remarks>
/// <para>
/// Every value here was a constant on the class that read it, and each one is still the same string. The
/// change is only that the engine now asks a title for them instead of knowing them.
/// </para>
/// <para>
/// TODO(phase-1): moves to <c>OpenIGI.Game.Igi1</c>.
/// </para>
/// </remarks>
public sealed class Igi1DataLayout : ITitleDataLayout
{
    /// <summary>Gets the shared instance.</summary>
    public static Igi1DataLayout Instance { get; } = new();

    /// <inheritdoc/>
    public IReadOnlyList<KeyValuePair<string, string>> StartupAliases { get; } =
    [
        new(VirtualFileSystem.MenuSystemAlias, "LOCAL:menusystem/"),
        new(VirtualFileSystem.ComputerAlias, "LOCAL:computer/"),
        new(VirtualFileSystem.IntroScreenAlias, "LOCAL:screens/intro/"),
        new(VirtualFileSystem.StatusScreenAlias, "LOCAL:screens/game/status/"),
    ];

    /// <inheritdoc/>
    public string MissionRoot => MissionRegistry.MissionRoot;

    /// <inheritdoc/>
    public string MissionScriptName => MissionRegistry.MissionScriptName;

    /// <inheritdoc/>
    public string LevelScriptName => LevelLoader.ObjectsScriptName;

    /// <inheritdoc/>
    public int FirstMissionId => MissionRegistry.FirstMissionId;

    /// <inheritdoc/>
    public string CommonSearchDirectory => MissionLoader.CommonSearchDirectory;

    /// <inheritdoc/>
    public IReadOnlyList<string> SharedArchives { get; } =
    [
        MissionLoader.CommonSpritesArchive,
        MissionLoader.CommonTexturesArchive,
    ];

    /// <inheritdoc/>
    public IReadOnlyList<string> SoundDirectories { get; } =
    [
        MissionLoader.CommonSoundDirectory,
        MissionLoader.MissionSoundDirectory,
    ];

    /// <inheritdoc/>
    public IReadOnlyList<string> StructuralTaskTypes => EngineTaskTypes.StructuralNames;
}
