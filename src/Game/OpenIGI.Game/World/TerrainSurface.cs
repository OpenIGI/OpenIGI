using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;
using OpenIGI.Formats.World;

namespace OpenIGI.Game.World;

/// <summary>
/// What decides which ground material covers a point and which authored terrain cubes do not exist:
/// the level's <c>TextureModifier</c> and <c>DiscardTerrain</c> tasks, plus the tile maps and materials
/// the terrain script declared.
/// </summary>
/// <remarks>
/// <para>
/// The original keeps the active layers on a four-byte stack (<c>0xBA5060</c>) that its octree descent
/// pushes and pops as it enters and leaves each modifier's region — most recently entered in the low
/// byte, and tested first. A modifier entered deeper in the tree is the smaller and more specific one,
/// so the ordering here is by <see cref="TerrainTileLayer.Level"/> descending, which is the same
/// ordering by construction.
/// </para>
/// <para>
/// A point no layer covers takes <see cref="BaseMaterial"/>. That is not a fallback invented here: with
/// no layer bound the original's per-vertex material is zero, which is the branch it takes on every
/// level before any modifier is entered.
/// </para>
/// </remarks>
public sealed class TerrainSurface
{
    private const string LogCategory = "Terrain";

    /// <summary>The task type that declares a tile layer.</summary>
    public const string TextureModifierType = "TextureModifier";

    /// <summary>The task type that removes an octree cube of terrain.</summary>
    public const string DiscardTerrainType = "DiscardTerrain";

    /// <summary>The material a point takes when no layer covers it.</summary>
    public const int BaseMaterial = 0;

    /// <summary>How many layers the original's stack holds at once.</summary>
    public const int MaximumActiveLayers = 4;

    private TerrainSurface(
        TerrainData terrain,
        TerrainTileLayer[] layers,
        TerrainDiscardRegion[] discards,
        TerrainHeightBindings heights)
    {
        Terrain = terrain;
        Layers = layers;
        Discards = discards;
        Heights = heights;
    }

    /// <summary>Gets the terrain the layers belong to.</summary>
    public TerrainData Terrain { get; }

    /// <summary>Gets the layers, most specific first.</summary>
    /// <remarks>
    /// An array, not a wrapped list: the chunk builder consults it per vertex and the octree descent
    /// consults <see cref="Discards"/> per visited node, so the interface indirection was measurable.
    /// </remarks>
    public TerrainTileLayer[] Layers { get; }

    /// <summary>Gets the octree cubes whose terrain the level removes, most specific first.</summary>
    public TerrainDiscardRegion[] Discards { get; }

    /// <summary>Gets the heightmap bindings the level's <c>HeightMap</c> tasks publish.</summary>
    /// <remarks>
    /// Bound here, beside the discards, because both are read by the same three consumers — the chunk
    /// mesh, the ground query and the ray test — and the surface is what a level load hands them.
    /// </remarks>
    public TerrainHeightBindings Heights { get; }

    /// <summary>
    /// Builds the surface by walking a level's task tree for its <c>TextureModifier</c>s and
    /// <c>DiscardTerrain</c>s.
    /// </summary>
    /// <param name="terrain">The loaded terrain.</param>
    /// <param name="levelRoot">The task the level's subtree hangs off.</param>
    /// <param name="logger">Where to report modifiers or discard volumes that do not resolve.</param>
    /// <returns>The surface.</returns>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    public static TerrainSurface Build(TerrainData terrain, GameTask levelRoot, IIgiLogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(terrain);
        ArgumentNullException.ThrowIfNull(levelRoot);

        IIgiLogger log = logger ?? NullLogger.Instance;
        List<TerrainTileLayer> layers = [];
        int skippedModifiers = 0;

        foreach (GameTask task in Walk(levelRoot))
        {
            if (task is not DeclaredTask declared
                || !string.Equals(declared.Type.Name, TextureModifierType, StringComparison.Ordinal))
            {
                continue;
            }

            if (!WorldScene.TryGetPosition(declared, out Vector3d metres))
            {
                skippedModifiers++;
                continue;
            }

            // WorldScene hands positions back in metres; the terrain works in engine units.
            Vector3d position = new(
                metres.X * WorldScene.UnitsPerMetre,
                metres.Y * WorldScene.UnitsPerMetre,
                metres.Z * WorldScene.UnitsPerMetre);

            if (TerrainTileLayer.TryCreate(
                    position,
                    GetInt32(declared, "Level"),
                    GetInt32(declared, "Material Index"),
                    GetInt32(declared, "Bitmap ID"),
                    terrain.Visibility,
                    out TerrainTileLayer? layer)
                && layer is not null)
            {
                layers.Add(layer);
            }
            else
            {
                skippedModifiers++;
            }
        }

        // Deeper in the octree means smaller and more specific, and the original's stack tests the most
        // recently entered first. Ordering by level descending reproduces that without the descent.
        layers.Sort((a, b) => b.Level.CompareTo(a.Level));
        TerrainDiscardBinding discardBinding = TerrainDiscardBinding.Bind(terrain, levelRoot);
        TerrainDiscardRegion[] discards = [.. discardBinding.Regions];
        TerrainHeightBindings heights = TerrainHeightBindings.Bind(terrain, levelRoot);

        log.Information(
            LogCategory,
            $"Surface: {layers.Count} tile layer(s)"
                + (skippedModifiers > 0 ? $", {skippedModifiers} modifier(s) unresolved" : string.Empty)
                + $", {discards.Length} terrain discard(s)"
                + (discardBinding.Skipped > 0 ? $", {discardBinding.Skipped} discard(s) unresolved" : string.Empty)
                + $", {heights.Entries.Count} heightmap binding(s)"
                + (heights.Skipped > 0 ? $", {heights.Skipped} heightmap(s) unresolved" : string.Empty)
                + $", {terrain.Materials.Count} material(s), {terrain.TileMaps.Count} tile map(s).");

        return new TerrainSurface(terrain, [.. layers], discards, heights);
    }

    /// <summary>Builds a surface with no layers, so every point takes the base material.</summary>
    /// <param name="terrain">The loaded terrain.</param>
    /// <returns>The surface.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="terrain"/> is <see langword="null"/>.</exception>
    public static TerrainSurface Bare(TerrainData terrain)
    {
        ArgumentNullException.ThrowIfNull(terrain);
        return new TerrainSurface(terrain, [], [.. TerrainDiscardBinding.For(terrain).Regions], TerrainHeightBindings.For(terrain));
    }

    /// <summary>Gets the material covering a world position.</summary>
    /// <param name="worldX">The world X in engine units.</param>
    /// <param name="worldY">The world Y in engine units.</param>
    /// <returns>The material id, <see cref="BaseMaterial"/> when no layer covers the point.</returns>
    /// <remarks>
    /// Only the first <see cref="MaximumActiveLayers"/> layers that could apply are consulted, because
    /// that is all the original's stack holds. Levels place far more than four modifiers, but they are
    /// spread across the map and rarely more than a couple overlap.
    /// </remarks>
    public int MaterialAt(double worldX, double worldY)
    {
        int consulted = 0;
        foreach (TerrainTileLayer layer in Layers)
        {
            if (!Overlaps(layer, worldX, worldY))
            {
                continue;
            }

            if (layer.Covers(worldX, worldY))
            {
                return layer.MaterialId;
            }

            if (++consulted == MaximumActiveLayers)
            {
                break;
            }
        }

        return BaseMaterial;
    }

    /// <summary>Resolves the material a chunk should be textured with, falling back when one is missing.</summary>
    /// <param name="materialId">The material id a vertex selected.</param>
    /// <param name="material">Receives the material.</param>
    /// <returns><see langword="false"/> when neither the id nor the base material is declared.</returns>
    public bool TryGetMaterial(int materialId, out TerrainMaterial? material) =>
        Terrain.Materials.TryGetValue(materialId, out material)
        || Terrain.Materials.TryGetValue(BaseMaterial, out material);

    /// <inheritdoc/>
    public override string ToString() =>
        $"terrain surface, {Layers.Length} layer(s), {Discards.Length} discard(s)";

    private static bool Overlaps(TerrainTileLayer layer, double worldX, double worldY)
    {
        double extent = 2.0 * layer.Extent;
        return worldX >= layer.OriginX
            && worldY >= layer.OriginY
            && worldX < layer.OriginX + extent
            && worldY < layer.OriginY + extent;
    }

    private static int GetInt32(DeclaredTask task, string parameter) =>
        task.TryGetValue(parameter, out object? value) && value is int number ? number : -1;

    private static IEnumerable<GameTask> Walk(GameTask root)
    {
        foreach (GameTask child in root.Children)
        {
            yield return child;
            foreach (GameTask nested in Walk(child))
            {
                yield return nested;
            }
        }
    }
}
