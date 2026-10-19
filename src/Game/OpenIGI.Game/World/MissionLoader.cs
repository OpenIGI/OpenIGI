using OpenIGI.Core.Diagnostics;
using OpenIGI.Engine.Audio;
using OpenIGI.Engine.Level;
using OpenIGI.Engine.Resources;
using OpenIGI.Engine.Titles;
using OpenIGI.Formats;
using OpenIGI.Engine.Time;
using OpenIGI.Engine.Vfs;

namespace OpenIGI.Game.World;

/// <summary>
/// Loads a mission: the ordered sequence <c>Mission_Load</c> (<c>0x415B30</c>) performs, from the
/// descriptor lookup to the level's task tree.
/// </summary>
/// <remarks>
/// <para>
/// The order is the specification. Each step depends on the ones before it — the <c>MISSION:</c> alias
/// has to exist before the terrain path resolves, both packs have to be open before
/// <c>objects.qvm</c> names a model, and the search directory is re-pointed <em>between</em> the two
/// pack opens because a pack's archives are addressed relative to it.
/// </para>
/// <para>
/// Steps this does not perform yet are logged, not skipped silently. AI graph files are
/// detected here; the desktop host still parses them and applies the AIGraph world offset.
/// A caller can see exactly what a loaded mission is missing from
/// <see cref="LoadedMission.PendingSteps"/>.
/// </para>
/// </remarks>
public sealed class MissionLoader
{
    private const string LogCategory = "Mission";

    /// <summary>The resource search directory the original falls back to for shared content.</summary>
    public const string CommonSearchDirectory = "LOCAL:common/";

    /// <summary>The sprite archive mounted for every mission.</summary>
    public const string CommonSpritesArchive = "LOCAL:common/sprites/sprites.res";

    /// <summary>The texture archive mounted for every mission.</summary>
    public const string CommonTexturesArchive = "LOCAL:common/textures/textures.res";

    /// <summary>The shared sound directory, walked by <c>0x4E68D0</c> for every mission.</summary>
    public const string CommonSoundDirectory = "LOCAL:common/sounds";

    /// <summary>The mission's own sound directory, walked after the shared one.</summary>
    /// <remarks>
    /// Order matters and is the original's: the first mount of a name wins, so a mission cannot shadow a
    /// common sound by shipping one of the same name.
    /// </remarks>
    public const string MissionSoundDirectory = "MISSION:sounds";

    private readonly IMissionCatalogue<MissionDescriptor> _registry;

    // Which encodings this title's packs may hold. Null keeps the reader's own defaults, which is what
    // Project I.G.I. has always used; I.G.I. 2 ships mesh version 0.16 where the first game ships 0.10,
    // and a pack opened under the wrong profile refuses every model in it.
    private readonly AssetFormatProfile? _formats;
    private readonly VirtualFileSystem _fileSystem;
    private readonly ResourceRegistry _resources;
    private readonly LevelLoader _levelLoader;
    private readonly TerrainLoader? _terrainLoader;
    private readonly SoundBank? _soundBank;
    private readonly IOpenIgiLogger _logger;

    /// <summary>Initializes a new instance of the <see cref="MissionLoader"/> class.</summary>
    /// <param name="registry">The mission table, already scanned.</param>
    /// <param name="fileSystem">The file system whose <c>MISSION:</c> alias this rebinds.</param>
    /// <param name="resources">Where packs and scripts are read from.</param>
    /// <param name="levelLoader">Builds the level's task tree from <c>objects.qvm</c>.</param>
    /// <param name="logger">The logger for load diagnostics.</param>
    /// <param name="terrainLoader">
    /// Loads the level's terrain. Optional because it needs the script namespace the terrain's own two
    /// natives are registered into, which not every caller has; without one the mission loads without
    /// terrain and says so in <see cref="LoadedMission.PendingSteps"/>.
    /// </param>
    /// <param name="soundBank">
    /// <param name="formats">Which encodings this title's packs may hold, or null for the reader's defaults.</param>
    /// The bank the mission's sound directories are mounted into. Optional for the same reason the
    /// terrain loader is: a caller with no audio still loads missions, and says what it went without.
    /// </param>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    public MissionLoader(
        IMissionCatalogue<MissionDescriptor> registry,
        VirtualFileSystem fileSystem,
        ResourceRegistry resources,
        LevelLoader levelLoader,
        IOpenIgiLogger? logger = null,
        TerrainLoader? terrainLoader = null,
        SoundBank? soundBank = null,
        AssetFormatProfile? formats = null)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(resources);
        ArgumentNullException.ThrowIfNull(levelLoader);

        _registry = registry;
        _formats = formats;
        _fileSystem = fileSystem;
        _resources = resources;
        _levelLoader = levelLoader;
        _terrainLoader = terrainLoader;
        _soundBank = soundBank;
        _logger = logger ?? NullLogger.Instance;
    }

    /// <summary>Loads a mission by id.</summary>
    /// <param name="missionId">The mission id, as registered by <c>DefineMission</c>.</param>
    /// <param name="tickRate">The simulation rate the level runs at.</param>
    /// <returns>The loaded mission: its descriptor, both packs, and what is still outstanding.</returns>
    /// <exception cref="EngineFatalException">The mission id is unknown, or a required file is missing or malformed.</exception>
    public LoadedMission Load(int missionId, int tickRate = GameClock.DefaultTickRate)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(tickRate);

        // 1. The descriptor, with the original's wording when the id is unknown.
        // The original's own failure, word for word: Mission_GetMissionFromID returns nothing and the
        // caller aborts. Through the interface rather than MissionRegistry.Require, because the
        // catalogue is now the title's -- but the message a caller sees must not depend on which title
        // is running.
        if (!_registry.TryGet(missionId, out MissionDescriptor? descriptor))
        {
            throw new EngineFatalException($"The mission ID {missionId} does not exist.");
        }

        _logger.Information(LogCategory, $"Loading {descriptor}.");

        // 2. Bind MISSION: before anything resolves a path through it.
        _fileSystem.SetAlias(VirtualFileSystem.MissionAlias, descriptor.MissionAliasTarget);

        List<string> pending = [];

        // 3-4. Terrain, from the extensionless base path the loader appends each extension to. It runs
        //      before the packs because its own script has to see a clean namespace for its two natives.
        TerrainData? terrain = null;
        if (_terrainLoader is null)
        {
            pending.Add($"terrain from '{descriptor.TerrainBasePath}'");
        }
        else
        {
            terrain = _terrainLoader.Load(descriptor.TerrainBasePath);
            if (terrain is null)
            {
                pending.Add($"terrain from '{descriptor.TerrainBasePath}' (files absent)");
            }
            else if (!terrain.IsUsable)
            {
                pending.Add($"terrain from '{descriptor.TerrainBasePath}' ({string.Join("; ", terrain.Defects)})");
            }
        }

        // 5-7. The packs, in the order the title opens them, each from its own directory. The search
        //      directory moves first every time: a pack's archives are addressed relative to it, so
        //      opening one against another's directory silently binds the wrong archives.
        //
        //      Project I.G.I. names two and the descriptor falls back to exactly those. I.G.I. 2 names
        //      four -- Game_CreateHandler (igi2ed 0x4469A0) loads the global common pack, then "new",
        //      then the location's, then the level's -- and MTP_VirModel_GetPtr returns the first match
        //      from the head, so the earlier a pack is opened the more it shadows.
        List<LoadedPack> packs = [];
        foreach (PackReference reference in descriptor.PacksToOpen)
        {
            packs.Add(LoadedPack.Open(reference.Directory, reference.Name, _resources, _logger, _formats));
        }

        LoadedPack common = packs[0];
        LoadedPack level = packs[^1];

        // 9-11. Shared sound, sprite and texture content.
        MountShared(pending);

        // 14. The level's task tree.
        _levelLoader.LoadMission($"LOCAL:{descriptor.MissionDirectory}", tickRate);

        // DiscardTerrain is authored in objects.qvm rather than a terrain file. Publish it immediately:
        // scene placement, AI settling, player collision and rendering all consume the same terrain after
        // this point, just as retail's spatial task traversal supplies the volumes to all of them.
        if (terrain is { IsUsable: true })
        {
            TerrainDiscardBinding.Bind(terrain, _levelLoader.LevelRoot);
        }

        // 16. AI navigation graphs: one graphN.dat per AIGraph task (0x4F9770 world offset
        // is applied by the desktop host). Do not claim they are missing when the level ships them.
        IReadOnlyList<string> graphFiles = _fileSystem.EnumerateFiles("MISSION:graphs", "graph*.dat");
        if (graphFiles.Count == 0)
        {
            pending.Add("AI navigation graphs");
        }
        else
        {
            _logger.Information(LogCategory, $"{graphFiles.Count} AI navigation graph file(s) present.");
        }

        if (pending.Count > 0)
        {
            _logger.Warning(LogCategory, $"Mission {descriptor.Id} loaded without: {string.Join(", ", pending)}.");
        }

        return new LoadedMission(descriptor, common, level, pending, terrain, packs);
    }

    private void MountShared(List<string> pending)
    {
        foreach (string archive in new[] { CommonSpritesArchive, CommonTexturesArchive })
        {
            if (!_resources.TryLoad(archive, out _))
            {
                _logger.Warning(LogCategory, $"Shared archive '{archive}' is absent.");
                pending.Add(archive);
            }
        }

        // 9. Sound banks. The original walks both directories with 0x4E68D0, loading every .wav as an
        //    ILSF sound and every .res as an archive of them. The shared directory goes first because
        //    the first mount of a name is the one that answers to it.
        if (_soundBank is null)
        {
            pending.Add($"sound banks ({CommonSoundDirectory}, {MissionSoundDirectory})");
            return;
        }

        int sounds = _soundBank.MountDirectory(CommonSoundDirectory)
            + _soundBank.MountDirectory(MissionSoundDirectory);

        if (sounds == 0)
        {
            pending.Add($"sound banks ({CommonSoundDirectory}, {MissionSoundDirectory}) held no sounds");
        }
        else
        {
            _logger.Information(LogCategory, $"{sounds} sound(s) mounted.");
        }
    }
}
