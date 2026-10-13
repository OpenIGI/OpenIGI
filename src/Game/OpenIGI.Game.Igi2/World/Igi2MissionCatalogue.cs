using System.Diagnostics.CodeAnalysis;
using OpenIGI.Core.Diagnostics;
using OpenIGI.Engine.Resources;
using OpenIGI.Engine.Titles;
using OpenIGI.Engine.Vfs;
using OpenIGI.Game.World;
using OpenIGI.Scripting.Runtime;

namespace OpenIGI.Game.Igi2.World;

/// <summary>I.G.I. 2's missions, as its own <c>mission.qvm</c> scripts publish them.</summary>
/// <remarks>
/// <para>
/// The walk is the shared one — a script per mission directory — and the reader is not. I.G.I. 2's
/// <c>DefineMission</c> takes <b>fourteen</b> arguments where the first game's takes eleven, and the
/// difference is not a suffix: the fourth argument of the first game is the id of the mission that
/// follows, and I.G.I. 2 has nothing of the kind.
/// </para>
/// <para>
/// Read from <c>Mission_DefineMissionCB</c> (<c>igi2 0x40E2D0</c>), which is unambiguous because it
/// evaluates each argument into a fixed offset of a 1,968-byte record:
/// </para>
/// <code>
///  0  int     the mission id, checked for uniqueness against every mission already registered
///  1..4       four SINGLE-CHARACTER strings, packed big-endian into one word the code calls
///             aazGameModeID: 'S','N','G','L' for the campaign and 'M','U','L','T' for a
///             multiplayer map
///  5  string  the name key            (+16,   32 bytes)
///  6  string  the description key     (+48, 1024 bytes)
///  7  string  a tag, empty everywhere (+1072,  32 bytes)
///  8  string  the mission directory   (+1104, 256 bytes)
///  9  string  the common directory    (+1360, 256 bytes)
/// 10  string  the level directory     (+1616, 256 bytes)
/// 11  string  the common pack name    (+1872,  32 bytes)
/// 12  string  the level pack name     (+1904,  32 bytes)
/// 13  string  the briefing sprite     (+1936,  32 bytes)
/// </code>
/// <para>
/// <b>Those offsets are what settles the packs.</b> <c>Game_CreateHandler</c> (<c>igi2ed 0x4469A0</c>)
/// reads the descriptor at <c>+0x550</c>, <c>+0x750</c>, <c>+0x650</c> and <c>+0x770</c> — which are
/// 1360, 1872, 1616 and 1904, the common directory and its pack name and then the level directory and
/// its pack name. So the four packs a mission opens are the two global ones and the two this record
/// names, and neither the order nor the names are inferred from the directory tree.
/// </para>
/// </remarks>
public sealed class Igi2MissionCatalogue : IMissionCatalogue<MissionDescriptor>
{
    /// <summary>The category load diagnostics are logged under.</summary>
    public const string LogCategory = "igi2.missions";

    /// <summary>How many arguments this title's <c>DefineMission</c> takes.</summary>
    public const int ArgumentCount = 14;

    /// <summary>The game mode the shipped campaign declares.</summary>
    public const string SinglePlayerMode = "SNGL";

    /// <summary>The game mode the shipped multiplayer maps declare.</summary>
    public const string MultiplayerMode = "MULT";

    /// <summary>The pack every mission opens first, before anything the mission itself names.</summary>
    /// <remarks><c>MTP_Load("common")</c>, the first of <c>Game_CreateHandler</c>'s five.</remarks>
    public static readonly PackReference GlobalCommonPack = new("LOCAL:common/", "common");

    /// <summary>The pack opened second.</summary>
    /// <remarks>
    /// <c>MTP_Load("new")</c>. The name is the game's own; whatever it once meant, it is a second global
    /// pack whose contents shadow every mission's.
    /// </remarks>
    public static readonly PackReference GlobalNewPack = new("LOCAL:common/", "new");

    private readonly List<MissionDescriptor> _missions = [];
    private readonly Dictionary<int, MissionDescriptor> _byId = [];
    private readonly IOpenIgiLogger _logger;

    /// <summary>Initialises a new instance of the <see cref="Igi2MissionCatalogue"/> class.</summary>
    /// <param name="logger">Where scan diagnostics go.</param>
    public Igi2MissionCatalogue(IOpenIgiLogger? logger = null) => _logger = logger ?? NullLogger.Instance;

    /// <inheritdoc/>
    public IReadOnlyList<MissionDescriptor> Missions => _missions;

    /// <inheritdoc/>
    public int Count => _missions.Count;

    /// <summary>Gets the missions of the single-player campaign, in the order the scan found them.</summary>
    /// <remarks>
    /// By mode rather than by a chain, because there is no chain: the first game names the mission that
    /// follows each one and I.G.I. 2 does not. Ordering a campaign is therefore a separate question, and
    /// this only says which missions are in one.
    /// </remarks>
    public IEnumerable<MissionDescriptor> Campaign =>
        _missions.Where(static m => string.Equals(m.GameMode, SinglePlayerMode, StringComparison.Ordinal));

    /// <inheritdoc/>
    public void Register(NativeRegistry natives)
    {
        ArgumentNullException.ThrowIfNull(natives);
        // Variadic, as the original declares it -- the engine spells that as no declared count. There is
        // no companion constant to register: the first game's MISSION_NEXT_MISSION_UNDEFINED exists for
        // an argument this title does not have.
        natives.RegisterFunction(MissionRegistry.DefineMissionNative, Define);
    }

    /// <inheritdoc/>
    public int Scan(VirtualFileSystem fileSystem, ResourceRegistry resources, NativeRegistry natives)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(resources);
        ArgumentNullException.ThrowIfNull(natives);

        _missions.Clear();
        _byId.Clear();

        int scripts = ScriptTableLoader.WalkAndRun(
            fileSystem,
            resources,
            natives,
            MissionRegistry.MissionRoot,
            MissionRegistry.MissionScriptName,
            _logger);

        _logger.Information(
            LogCategory,
            $"Registered {Count} mission(s) from {scripts} script(s); {Campaign.Count()} in the campaign.");
        return Count;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// <para>
    /// The campaign's missions by ascending id, then everything else the same way. Ascending id is the
    /// campaign's own grouping rather than an arbitrary sort: a mission's id is its location and its
    /// level read together — <c>location1/level1</c> is 11 and <c>location2/LEVEL2</c> is 22 — so
    /// ascending id walks the locations in turn and the levels within each.
    /// </para>
    /// <para>
    /// <b>The mode filter is what makes it work.</b> The shipped multiplayer maps take ids 1, 5 and 100,
    /// which are below and above the campaign's 11 to 36; sorting every mission by id together would put
    /// two deathmatch maps ahead of the first mission of the game.
    /// </para>
    /// <para>
    /// It is still a listing order and not a progression. Nothing in this title's <c>DefineMission</c>
    /// says which mission follows which, so a real campaign flow has to come from the front end.
    /// </para>
    /// </remarks>
    public IEnumerable<MissionDescriptor> InCampaignOrder()
    {
        foreach (MissionDescriptor mission in Campaign.OrderBy(static m => m.Id))
        {
            yield return mission;
        }

        foreach (MissionDescriptor mission in _missions
            .Where(static m => !string.Equals(m.GameMode, SinglePlayerMode, StringComparison.Ordinal))
            .OrderBy(static m => m.Id))
        {
            yield return mission;
        }
    }

    /// <inheritdoc/>
    /// <remarks>
    /// The campaign by ascending id, and nothing else — a multiplayer map is not part of a progression.
    /// This title asserts no order of its own, so the id's own structure is what supplies one.
    /// </remarks>
    public IEnumerable<MissionDescriptor> InProgressionOrder() => Campaign.OrderBy(static m => m.Id);

    /// <inheritdoc/>
    public bool TryGet(int id, [NotNullWhen(true)] out MissionDescriptor? descriptor) =>
        _byId.TryGetValue(id, out descriptor);

    /// <inheritdoc/>
    public override string ToString() => $"I.G.I. 2 missions, {Count} registered";

    /// <summary>Packs four single-character arguments into the mode the original stores.</summary>
    /// <param name="a">The first character.</param>
    /// <param name="b">The second.</param>
    /// <param name="c">The third.</param>
    /// <param name="d">The fourth.</param>
    /// <returns>The four characters, in order.</returns>
    /// <remarks>
    /// The original packs them into a word big-endian — <c>a &lt;&lt; 24 | b &lt;&lt; 16 | c &lt;&lt; 8 | d</c>
    /// — which is the same four characters in the same order, so this keeps them as text. A word would
    /// have to be unpacked again by everything that wanted to read it.
    /// </remarks>
    public static string ModeOf(string a, string b, string c, string d) =>
        string.Concat(First(a), First(b), First(c), First(d));

    private static string First(string value) => value.Length > 0 ? value[..1] : string.Empty;

    private static string Normalize(string path) => path.Replace('\\', '/').TrimEnd('/');

    private QvmValue Define(QvmCallContext call)
    {
        string missionDirectory = Normalize(call.GetString(8));
        string commonDirectory = Normalize(call.GetString(9));
        string levelDirectory = Normalize(call.GetString(10));
        string commonPack = call.GetString(11);
        string levelPack = call.GetString(12);

        MissionDescriptor descriptor = new(
            call.GetInt32(0),
            call.GetString(5),
            call.GetString(6),
            call.GetString(7),

            // No mission names the one that follows it; the campaign's order is the order the missions are defined in, as
            // Game_RunHandler takes the next node of Mission_tMissionList, and is filled in as the next one is defined.
            MissionDescriptor.NextMissionUndefined,
            missionDirectory,
            commonDirectory,
            levelDirectory,
            commonPack,
            levelPack,
            call.GetString(13))
        {
            GameMode = ModeOf(call.GetString(1), call.GetString(2), call.GetString(3), call.GetString(4)),

            // The two global packs first, then the two this mission names. Earlier shadows later.
            Packs =
            [
                GlobalCommonPack,
                GlobalNewPack,
                new PackReference($"LOCAL:{commonDirectory}/", commonPack),
                new PackReference($"LOCAL:{levelDirectory}/", levelPack),
            ],
        };

        if (!_byId.TryAdd(descriptor.Id, descriptor))
        {
            throw new EngineFatalException($"Mission ID {descriptor.Id} not unique");
        }

        if (string.Equals(descriptor.GameMode, SinglePlayerMode, StringComparison.Ordinal)
            && _missions.FindLastIndex(static m => string.Equals(m.GameMode, SinglePlayerMode, StringComparison.Ordinal)) is var last and >= 0)
        {
            MissionDescriptor previous = _missions[last] with { NextMissionId = descriptor.Id };
            _missions[last] = previous;
            _byId[previous.Id] = previous;
        }

        _missions.Add(descriptor);
        return QvmValue.FromInt32(1);
    }
}
