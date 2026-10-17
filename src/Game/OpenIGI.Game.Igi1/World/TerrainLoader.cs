using OpenIGI.Core.Diagnostics;
using OpenIGI.Engine.Resources;
using OpenIGI.Formats.Textures;
using OpenIGI.Formats.World;
using OpenIGI.Scripting.Qvm;
using OpenIGI.Scripting.Runtime;
using OpenIGI.Game.World;

namespace OpenIGI.Game.Igi1.World;

/// <summary>
/// Loads a level's terrain: <c>Terrain_Load</c> (<c>0x4C6240</c>), plus the <c>terrain.lmp</c> that
/// <c>Terrain_LoadTextures</c> (<c>0x4C5F00</c>) reads just before it.
/// </summary>
/// <remarks>
/// <para>
/// The loader is given an <b>extensionless</b> base path and appends each extension itself, in the
/// original's order: <c>.bit</c>, <c>.hmp</c> if the file exists, <c>.cmd</c>, <c>.ctr</c>, then the
/// terrain script. The script comes last because it is what declares the materials and tile maps, and it
/// can only call the two natives while they are registered — the original registers them immediately
/// before the run and unregisters them immediately after, so nothing else can reach them.
/// </para>
/// <para>
/// The last step of the original is to relocate every octree node's <c>.cmd</c> offset into a pointer.
/// That is not done here: offsets are kept as offsets and resolved through
/// <see cref="TerrainCommands.TryGetBlock"/>, which turns level 14's 14400 impossible offsets into a
/// terrain that reports itself unusable instead of 14400 wild pointers.
/// </para>
/// </remarks>
public sealed class TerrainLoader
{
    private const string LogCategory = "Terrain";

    /// <summary>The native the terrain script declares its materials through.</summary>
    public const string MaterialNative = "CreateTerrainMaterial";

    /// <summary>The native the terrain script declares its tile maps through.</summary>
    public const string TileMapNative = "CreateTerrainTileMap";

    private readonly ResourceRegistry _resources;
    private readonly NativeRegistry _natives;
    private readonly IOpenIgiLogger _logger;

    /// <summary>Initializes a new instance of the <see cref="TerrainLoader"/> class.</summary>
    /// <param name="resources">Where the terrain files are read from.</param>
    /// <param name="natives">The script namespace the two terrain natives are registered into.</param>
    /// <param name="logger">The logger for load diagnostics.</param>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    public TerrainLoader(ResourceRegistry resources, NativeRegistry natives, IOpenIgiLogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(resources);
        ArgumentNullException.ThrowIfNull(natives);

        _resources = resources;
        _natives = natives;
        _logger = logger ?? NullLogger.Instance;
    }

    /// <summary>Loads a level's terrain.</summary>
    /// <param name="basePath">
    /// The extensionless base path, as <see cref="MissionDescriptor.TerrainBasePath"/> builds it — for
    /// example <c>LOCAL:missions/location0/level1/terrain/terrain</c>.
    /// </param>
    /// <returns>The terrain, or <see langword="null"/> when the level ships no <c>.ctr</c> or <c>.cmd</c>.</returns>
    /// <exception cref="ArgumentException"><paramref name="basePath"/> is null or empty.</exception>
    /// <remarks>
    /// A missing file is reported and returns <see langword="null"/> rather than throwing: a level
    /// without terrain has to remain playable, and the original leaves the pointers null too.
    /// </remarks>
    public TerrainData? Load(string basePath)
    {
        ArgumentException.ThrowIfNullOrEmpty(basePath);

        if (!_resources.TryLoad(basePath + ".ctr", out ReadOnlyMemory<byte> ctrBytes))
        {
            // "No chunked terrain HERE", not "no terrain". This reader is Project I.G.I.'s octree, and a
            // title whose ground is a different format entirely -- I.G.I. 2's flat grid of .thm tiles
            // inside heightmaps.res -- ships nothing at this path and has terrain all the same.
            _logger.Warning(LogCategory, $"'{basePath}.ctr' is absent; no chunked terrain is loaded from this path.");
            return null;
        }

        if (!_resources.TryLoad(basePath + ".cmd", out ReadOnlyMemory<byte> cmdBytes))
        {
            _logger.Warning(LogCategory, $"'{basePath}.cmd' is absent; no chunked terrain is loaded from this path.");
            return null;
        }

        TerrainOctree octree;
        try
        {
            octree = TerrainOctree.Read(ctrBytes);
        }
        catch (PackFormatException ex)
        {
            _logger.Warning(LogCategory, $"'{basePath}.ctr' is malformed: {ex.Message}");
            return null;
        }

        TerrainCommands commands = TerrainCommands.Read(cmdBytes);

        // .bit is not optional in retail data, but a stub reads as an empty table rather than an error —
        // the original allocates a zeroed one when the file is shorter than its own header.
        TerrainVisibility visibility = TerrainVisibility.Read(
            _resources.TryLoad(basePath + ".bit", out ReadOnlyMemory<byte> bitBytes) ? bitBytes : default);

        // Seven of the fourteen retail levels ship no .hmp at all. The original guards with a file-exists
        // test and leaves the pointer null, so an absent heightmap is a normal level, not a broken one.
        TerrainHeightmap? heightmap = _resources.TryLoad(basePath + ".hmp", out ReadOnlyMemory<byte> hmpBytes)
            ? TerrainHeightmap.Read(hmpBytes)
            : null;

        TerrainLightmaps? lightmaps = _resources.TryLoad(basePath + ".lmp", out ReadOnlyMemory<byte> lmpBytes)
            ? TerrainLightmaps.Read(lmpBytes)
            : null;

        TexAtlasFile? tiles = ReadTiles(basePath + ".tex");

        Dictionary<int, TerrainMaterial> materials = [];
        Dictionary<int, TerrainTileMap> tileMaps = [];
        RunScript(basePath + ".qvm", materials, tileMaps);

        List<string> defects = [.. octree.Defects];
        AddBlockDefects(octree, commands, defects);

        TerrainData terrain = new(
            octree,
            commands,
            visibility,
            heightmap,
            lightmaps,
            tiles,
            materials,
            tileMaps,
            defects);

        _logger.Information(
            LogCategory,
            $"Loaded terrain from '{basePath}': {octree.Count} node(s), {commands.Length} byte(s) of geometry, "
                + $"{visibility.GridCount} bitmap(s), {heightmap?.GridCount ?? 0} heightmap(s), "
                + $"{lightmaps?.OccupiedCount ?? 0} lightmap page(s), {tiles?.Tiles.Count ?? 0} ground tile(s), "
                + $"{materials.Count} material(s), {tileMaps.Count} tile map(s).");

        if (!terrain.IsUsable)
        {
            _logger.Warning(LogCategory, $"The terrain is not drawable: {string.Join("; ", defects)}.");
        }

        return terrain;
    }

    /// <summary>Reads the level's ground texture atlas.</summary>
    /// <remarks>
    /// It is a multi-image <c>"LOOP"</c> container rather than a single texture, which is why it needs
    /// <see cref="TexAtlasFile"/> and not the ordinary texture loader. A level without one still draws
    /// its terrain, untextured.
    /// </remarks>
    private TexAtlasFile? ReadTiles(string path)
    {
        if (!_resources.TryLoad(path, out ReadOnlyMemory<byte> bytes))
        {
            _logger.Warning(LogCategory, $"'{path}' is absent; the terrain draws untextured.");
            return null;
        }

        try
        {
            return TexAtlasFile.Read(bytes);
        }
        catch (TextureFormatException ex)
        {
            _logger.Warning(LogCategory, $"'{path}' did not decode: {ex.Message}");
            return null;
        }
    }

    /// <summary>Checks that every node addresses a real block.</summary>
    /// <remarks>
    /// All 11022 to 12706 records of every sound retail level resolve, so anything less means the file is
    /// not live data. Level 14 resolves 802 of 15202, and even those are coincidences of a self-describing
    /// header rather than blocks anyone authored.
    /// </remarks>
    private static void AddBlockDefects(TerrainOctree octree, TerrainCommands commands, List<string> defects)
    {
        int unresolved = 0;
        ReadOnlySpan<TerrainNode> nodes = octree.Nodes;
        for (int i = 0; i < nodes.Length; i++)
        {
            if (!commands.TryGetBlock(nodes[i].CommandOffset, out _))
            {
                unresolved++;
            }
        }

        if (unresolved > 0)
        {
            defects.Add($"{unresolved} of {nodes.Length} node(s) address no block in the geometry blob");
        }
    }

    /// <summary>Registers the two terrain natives, runs the terrain script, and unregisters them again.</summary>
    private void RunScript(
        string path,
        Dictionary<int, TerrainMaterial> materials,
        Dictionary<int, TerrainTileMap> tileMaps)
    {
        if (!_resources.TryLoad(path, out ReadOnlyMemory<byte> bytes))
        {
            _logger.Warning(LogCategory, $"'{path}' is absent; the terrain has no materials or tile maps.");
            return;
        }

        _natives.RegisterFunction(
            MaterialNative,
            call => ReadMaterial(call, materials),
            TerrainMaterial.ArgumentCount);
        _natives.RegisterFunction(
            TileMapNative,
            call => ReadTileMap(call, tileMaps),
            TerrainTileMap.ArgumentCount);

        try
        {
            QvmProgram program = QvmProgram.Read(bytes);
            var interpreter = new QvmInterpreter(program, _natives);
            QvmError error = interpreter.Run();
            if (error != QvmError.None)
            {
                _logger.Warning(LogCategory, $"'{path}' faulted with {error}: {interpreter.ErrorMessage}");
            }
        }
        catch (QvmFormatException ex)
        {
            _logger.Warning(LogCategory, $"'{path}' is malformed: {ex.Message}");
        }
        finally
        {
            _natives.Unregister(MaterialNative);
            _natives.Unregister(TileMapNative);
        }
    }

    /// <summary>Reads one <c>CreateTerrainMaterial</c> call.</summary>
    /// <remarks>
    /// The two halved floats are halved here, where the original halves them on the way into its table,
    /// so a band holds what the renderer will actually multiply by.
    /// </remarks>
    private QvmValue ReadMaterial(QvmCallContext call, Dictionary<int, TerrainMaterial> materials)
    {
        if (call.ArgumentCount < TerrainMaterial.ArgumentCount)
        {
            _logger.Warning(
                LogCategory,
                $"{MaterialNative} was called with {call.ArgumentCount} arguments, expected {TerrainMaterial.ArgumentCount}.");
            return QvmValue.Zero;
        }

        int id = call.GetInt32(0);
        int flags = call.GetInt32(1);

        TerrainMaterialBand[] bands = new TerrainMaterialBand[TerrainMaterial.BandCount];
        for (int band = 0; band < bands.Length; band++)
        {
            int at = 2 + (band * TerrainMaterial.ValuesPerBand);
            bands[band] = new TerrainMaterialBand(
                call.GetInt32(at),
                call.GetSingle(at + 1),
                call.GetSingle(at + 2),
                call.GetSingle(at + 3) * 0.5f,
                call.GetInt32(at + 4),
                call.GetSingle(at + 5) * 0.5f,
                call.GetInt32(at + 6));
        }

        materials[id] = new TerrainMaterial(id, flags, bands);
        return QvmValue.Zero;
    }

    /// <summary>Reads one <c>CreateTerrainTileMap</c> call.</summary>
    private QvmValue ReadTileMap(QvmCallContext call, Dictionary<int, TerrainTileMap> tileMaps)
    {
        if (call.ArgumentCount < TerrainTileMap.ArgumentCount)
        {
            _logger.Warning(
                LogCategory,
                $"{TileMapNative} was called with {call.ArgumentCount} arguments, expected {TerrainTileMap.ArgumentCount}.");
            return QvmValue.Zero;
        }

        int id = call.GetInt32(0);
        byte[] tiles = new byte[TerrainTileMap.TileCount];
        for (int i = 0; i < tiles.Length; i++)
        {
            tiles[i] = (byte)call.GetInt32(i + 1);
        }

        tileMaps[id] = new TerrainTileMap(id, tiles);
        return QvmValue.Zero;
    }
}
