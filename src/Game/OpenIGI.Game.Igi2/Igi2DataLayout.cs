using OpenIGI.Engine.Tasks;
using OpenIGI.Engine.Titles;
using OpenIGI.Engine.Vfs;

namespace OpenIGI.Game.Igi2;

/// <summary>Where retail I.G.I. 2 keeps its data.</summary>
/// <remarks>
/// <para>
/// Read off a retail 1.1 installation and off <c>igi2.exe</c>'s own strings, which carry the alias
/// targets verbatim — <c>LOCAL:common/textures/%s</c>, <c>LOCAL:common/fonts/font1.fnt</c>,
/// <c>MISSION:sounds/</c>, <c>LANGUAGE:objectives.res</c>, <c>COMPUTER:computer.res</c>. The alias
/// vocabulary is identical to I.G.I. 1's; only the targets differ, and most of those do not.
/// </para>
/// <para>
/// The data root is the installation's <c>pc</c> directory, so these paths are relative to that and read
/// exactly like I.G.I. 1's. The directories are upper case on disk (<c>MISSIONS</c>, <c>COMMON</c>),
/// which is invisible on Windows and will need case-insensitive resolution on Linux; that belongs to the
/// virtual file system rather than here.
/// </para>
/// <para>
/// TODO(phase-1): moves to <c>OpenIGI.Game.Igi2</c>.
/// </para>
/// </remarks>
public sealed class Igi2DataLayout : ITitleDataLayout
{
    /// <inheritdoc/>
    public IReadOnlyList<KeyValuePair<string, string>> StartupAliases { get; } =
    [
        new(VirtualFileSystem.MenuSystemAlias, "LOCAL:menusystem/"),
        new(VirtualFileSystem.ComputerAlias, "LOCAL:computer/"),
    ];

    /// <inheritdoc/>
    public string MissionRoot => "LOCAL:missions";

    /// <inheritdoc/>
    /// <remarks>Unchanged from I.G.I. 1: every level directory holds a <c>mission.qvm</c>.</remarks>
    public string MissionScriptName => "mission.qvm";

    /// <inheritdoc/>
    public string LevelScriptName => "objects.qvm";

    /// <inheritdoc/>
    public int FirstMissionId => 1;

    /// <inheritdoc/>
    public string CommonSearchDirectory => "LOCAL:common/";

    /// <inheritdoc/>
    /// <remarks>
    /// I.G.I. 2 packs a level's own textures and models beside it rather than in one shared archive per
    /// kind, so there is no equivalent of I.G.I. 1's <c>common/sprites/sprites.res</c> pair to mount for
    /// every mission. Left empty until a level actually needs one, rather than guessing at a name.
    /// </remarks>
    public IReadOnlyList<string> SharedArchives { get; } = [];

    /// <inheritdoc/>
    public IReadOnlyList<string> SoundDirectories { get; } =
    [
        "LOCAL:common/sounds",
        "MISSION:sounds",
    ];

    /// <inheritdoc/>
    /// <remarks>
    /// The two the game's own text is read from, the status messages' and the computer's (<see cref="Flow.Igi2Strings"/>).
    /// Its tree ships <c>LANGUAGE/</c> in upper case beside <c>MISSIONS</c>; the local device finds either casing.
    /// </remarks>
    public IReadOnlyList<string> StringArchives { get; } = Flow.Igi2Strings.Archives;

    /// <inheritdoc/>
    /// <remarks>
    /// I.G.I. 1's set less <c>Container</c>, which I.G.I. 2's editor declares with a parameter of its
    /// own. Pre-registering the engine's parameterless one fails the arity check on every I.G.I. 2
    /// level; the other five are undeclared by both games.
    /// </remarks>
    public IReadOnlyList<string> StructuralTaskTypes { get; } =
    [
        EngineTaskTypes.Level,
        EngineTaskTypes.Static,
        EngineTaskTypes.Dynamic,
        EngineTaskTypes.Idle,
        EngineTaskTypes.CameraBase,
    ];
}
