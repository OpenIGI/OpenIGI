using System.Collections.ObjectModel;
using System.Runtime.CompilerServices;
using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;
using OpenIGI.Formats.World;

namespace OpenIGI.Game.World;

/// <summary>
/// Binds a level's <c>DiscardTerrain</c> tasks to the terrain shared by rendering and collision.
/// </summary>
/// <remarks>
/// Terrain files load before <c>objects.qvm</c>, so the files cannot contain these volumes. Retail
/// publishes them from tasks into the same spatial traversal used by both render and collision queries.
/// The weak binding mirrors that lifetime without making the format layer depend on game tasks.
/// </remarks>
public sealed class TerrainDiscardBinding
{
    private static readonly ConditionalWeakTable<TerrainData, TerrainDiscardBinding> Bindings = new();
    private static readonly TerrainDiscardBinding Empty = new([], 0);

    private readonly TerrainDiscardRegion[] _regions;

    private TerrainDiscardBinding(TerrainDiscardRegion[] regions, int skipped)
    {
        _regions = regions;
        Regions = new ReadOnlyCollection<TerrainDiscardRegion>(regions);
        Skipped = skipped;
    }

    /// <summary>Gets the resolved discard cubes, most specific first.</summary>
    public IReadOnlyList<TerrainDiscardRegion> Regions { get; }

    /// <summary>Gets how many authored discard tasks could not be resolved.</summary>
    public int Skipped { get; }

    /// <summary>Builds and publishes the task binding for a terrain.</summary>
    public static TerrainDiscardBinding Bind(TerrainData terrain, GameTask levelRoot)
    {
        ArgumentNullException.ThrowIfNull(terrain);
        ArgumentNullException.ThrowIfNull(levelRoot);

        List<TerrainDiscardRegion> regions = [];
        int skipped = 0;
        foreach (GameTask task in levelRoot.Descendants())
        {
            if (task is not DeclaredTask declared
                || !string.Equals(
                    declared.Type.Name,
                    TerrainSurface.DiscardTerrainType,
                    StringComparison.Ordinal))
            {
                continue;
            }

            if (!WorldScene.TryGetPosition(declared, out Vector3d metres))
            {
                skipped++;
                continue;
            }

            Vector3d position = new(
                metres.X * WorldScene.UnitsPerMetre,
                metres.Y * WorldScene.UnitsPerMetre,
                metres.Z * WorldScene.UnitsPerMetre);

            if (TerrainDiscardRegion.TryCreate(
                    position,
                    GetInt32(declared, "Level"),
                    out TerrainDiscardRegion? region)
                && region is not null)
            {
                regions.Add(region);
            }
            else
            {
                skipped++;
            }
        }

        regions.Sort((a, b) => b.Level.CompareTo(a.Level));
        TerrainDiscardBinding binding = new([.. regions], skipped);

        Bindings.Remove(terrain);
        Bindings.Add(terrain, binding);
        return binding;
    }

    /// <summary>Gets the binding published for a terrain, or an empty binding when none was authored.</summary>
    public static TerrainDiscardBinding For(TerrainData terrain)
    {
        ArgumentNullException.ThrowIfNull(terrain);
        return Bindings.TryGetValue(terrain, out TerrainDiscardBinding? binding) ? binding : Empty;
    }

    /// <summary>Tests whether a collision leaf belongs to a discarded terrain cube.</summary>
    public bool Discards(in TerrainDescentCursor cursor)
    {
        Vector3d centre = new(cursor.Centre.X, cursor.Centre.Y, cursor.Centre.Z);
        foreach (TerrainDiscardRegion region in _regions)
        {
            if (region.ContainsCube(centre, cursor.Extent))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Tests the strict point bounds used by retail collision handler <c>sub_52BF00</c>.</summary>
    public bool Discards(double x, double y, double z)
    {
        foreach (TerrainDiscardRegion region in _regions)
        {
            if (region.Contains(x, y, z))
            {
                return true;
            }
        }

        return false;
    }

    private static int GetInt32(DeclaredTask task, string parameter) =>
        task.TryGetValue(parameter, out object? value) && value is int number ? number : -1;

}
