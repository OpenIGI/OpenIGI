using OpenIGI.Core.Diagnostics;
using OpenIGI.Engine.Level;
using OpenIGI.Engine.Resources;
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
/// Steps this does not perform yet are logged, not skipped silently: the terrain loader is still blocked
/// on open format questions, and AI graphs belong to M10. A caller can see exactly what a loaded mission
/// is missing from <see cref="LoadedMission.PendingSteps"/>.
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

    private readonly MissionRegistry _registry;
    private readonly VirtualFileSystem _fileSystem;
    private readonly ResourceRegistry _resources;
    private readonly LevelLoader _levelLoader;
    private readonly IIgiLogger _logger;

    /// <summary>Initializes a new instance of the <see cref="MissionLoader"/> class.</summary>
    /// <param name="registry">The mission table, already scanned.</param>
    /// <param name="fileSystem">The file system whose <c>MISSION:</c> alias this rebinds.</param>
    /// <param name="resources">Where packs and scripts are read from.</param>
    /// <param name="levelLoader">Builds the level's task tree from <c>objects.qvm</c>.</param>
    /// <param name="logger">The logger for load diagnostics.</param>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    public MissionLoader(
        MissionRegistry registry,
        VirtualFileSystem fileSystem,
        ResourceRegistry resources,
        LevelLoader levelLoader,
        IIgiLogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(resources);
        ArgumentNullException.ThrowIfNull(levelLoader);

        _registry = registry;
        _fileSystem = fileSystem;
        _resources = resources;
        _levelLoader = levelLoader;
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
        MissionDescriptor descriptor = _registry.Require(missionId);
        _logger.Information(LogCategory, $"Loading {descriptor}.");

        // 2. Bind MISSION: before anything resolves a path through it.
        _fileSystem.SetAlias(VirtualFileSystem.MissionAlias, descriptor.MissionAliasTarget);

        List<string> pending = [];

        // 3-4. Terrain. The loader is not written yet; say so rather than pretend the world is complete.
        pending.Add($"terrain from '{descriptor.TerrainBasePath}'");

        // 5-6. The common pack, opened from its own directory with LOCAL:common/ as the fallback.
        LoadedPack common = LoadedPack.Open(
            $"LOCAL:{descriptor.CommonPackDirectory}/",
            descriptor.CommonPackName,
            _resources,
            _logger);

        // 7. The level pack, from the level's directory. The search directory moves first: a pack's
        //    archives are addressed relative to it, so opening the level pack against the common
        //    directory would silently bind the wrong archives.
        LoadedPack level = LoadedPack.Open(
            $"LOCAL:{descriptor.LevelPackDirectory}/",
            descriptor.LevelPackName,
            _resources,
            _logger);

        // 9-11. Shared sound, sprite and texture content.
        MountShared(pending);

        // 14. The level's task tree.
        _levelLoader.LoadMission($"LOCAL:{descriptor.MissionDirectory}", tickRate);

        // 16. AI navigation graphs are M10's.
        pending.Add("AI navigation graphs");

        if (pending.Count > 0)
        {
            _logger.Warning(LogCategory, $"Mission {descriptor.Id} loaded without: {string.Join(", ", pending)}.");
        }

        return new LoadedMission(descriptor, common, level, pending);
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

        // Sound banks are M7's; the directories the original mounts are recorded so the wiring is
        // obvious when the audio backend lands.
        pending.Add("sound banks (LOCAL:common/sounds, MISSION:sounds)");
    }
}
