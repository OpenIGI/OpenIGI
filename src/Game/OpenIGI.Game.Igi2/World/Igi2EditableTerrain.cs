using OpenIGI.Formats.Ilff;
using OpenIGI.Game.World;

namespace OpenIGI.Game.Igi2.World;

/// <summary>
/// I.G.I. 2's ground opened for editing: each terrain tile's height map as a grid, changed in place so that the ground the
/// level stands on, and the drawing of it, follow at once.
/// </summary>
/// <remarks>
/// <para>
/// The heights are the tiles' own <see cref="Formats.World.Igi2TerrainMap"/>s, which the collision and the ground queries
/// read sample by sample; what is built from them ahead of time (the quadtree's height bounds) hears of a change through
/// <see cref="Changed"/>.
/// </para>
/// <para>
/// Saving writes the level's <c>heightmaps/heightmaps.res</c> again with the changed height maps in it and every other
/// entry as it was. The archive rebuilds byte for byte from its own entries (all 24 retail ones do), so the file written
/// differs from the shipped one only in the samples that were changed, and runs in the original as well.
/// </para>
/// </remarks>
public sealed partial class Igi2EditableTerrain : IEditableTerrain
{
    private readonly Grid[] _grids;
    private readonly IReadOnlyDictionary<int, TerrainMaterialTask> _materials;

    internal Igi2EditableTerrain(Igi2TerrainSurface surface)
    {
        _grids = [.. surface.Tiles.Select(static (tile, index) => new Grid(tile, index))];
        _materials = surface.Materials;
    }

    /// <summary>Raised when samples of a tile changed: the tile's index in the surface, and the changed columns and rows.</summary>
    public event Action<int, int, int, int, int>? Changed;

    /// <inheritdoc/>
    public IReadOnlyList<ITerrainHeightGrid> Grids => _grids;

    /// <inheritdoc/>
    public bool TryGetHeight(double xMetres, double yMetres, out double zMetres)
    {
        // The last grid that covers the point is the ground there, as the original searches its maps backwards.
        for (int i = _grids.Length - 1; i >= 0; i--)
        {
            if (_grids[i].TrySample(xMetres, yMetres, out zMetres))
            {
                return true;
            }
        }

        zMetres = 0.0;
        return false;
    }

    /// <inheritdoc/>
    public void Refresh(ITerrainHeightGrid grid, int left, int top, int right, int bottom)
    {
        if (grid is Grid own && own.Index < _grids.Length && ReferenceEquals(_grids[own.Index], own))
        {
            Changed?.Invoke(own.Index, left, top, right, bottom);
        }
    }

    /// <inheritdoc/>
    public IReadOnlyList<(string MissionPath, byte[] Bytes)> ChangedFiles(Func<string, ReadOnlyMemory<byte>?> read)
    {
        ArgumentNullException.ThrowIfNull(read);
        // Each changed map by its entry: the heights a sculpt moved, the materials paint changed.
        Dictionary<string, byte[]> changed = new(StringComparer.OrdinalIgnoreCase);
        foreach (Grid grid in _grids)
        {
            if (grid.IsChanged && grid.Tile.HeightEntry is { } heights)
            {
                changed[heights] = grid.Tile.Heights.Write();
            }

            if (grid.IsPainted && grid.Tile.MaterialEntry is { } materials && grid.Tile.Materials is { } map)
            {
                changed[materials] = map.Write();
            }
        }

        string path = ArchiveMissionPath;
        if (changed.Count == 0 || read(path) is not { } original)
        {
            return [];
        }

        // Typed as the nullable memory WithBodies wants: a bare null would become an empty byte array, and so an empty body
        // for every entry left alone.
        IresArchive archive = IresArchive.Read(original).WithBodies(entry =>
            changed.TryGetValue(Path.GetFileName(entry.Name), out byte[]? body) ? body : (ReadOnlyMemory<byte>?)null);
        byte[] bytes = IresWriter.Write([.. archive.Entries.Select(static e => new IresWriter.Entry(e.Name, e.Kind, e.Data))]);
        return [(path, bytes)];
    }

    /// <inheritdoc/>
    public void MarkSaved()
    {
        foreach (Grid grid in _grids)
        {
            grid.MarkSaved();
        }
    }

    // heightmaps.res relative to the mission's folder: the resource name with its alias taken off.
    private static string ArchiveMissionPath =>
        Igi2TerrainSurface.ArchiveResourceName[(Igi2TerrainSurface.ArchiveResourceName.IndexOf(':') + 1)..];

    private sealed class Grid(Igi2TerrainTile tile, int index) : ITerrainHeightGrid
    {
        private int _savedRevision = tile.Heights.Revision;
        private int _savedMaterials = tile.Materials?.Revision ?? 0;
        private readonly byte[] _loadedMaterials = tile.Materials is { } map ? map.Samples.ToArray() : [];

        public Igi2TerrainTile Tile { get; } = tile;

        public int Index { get; } = index;

        public string Name => Tile.HeightEntry ?? $"tile {Tile.Id}";

        public int Width => Tile.Width;

        public int Height => Tile.Height;

        public bool IsChanged => Tile.Heights.Revision != _savedRevision;

        public bool IsPainted => (Tile.Materials?.Revision ?? 0) != _savedMaterials;

        // The material ids as the level shipped them, row by row.
        public byte[] LoadedMaterials => _loadedMaterials;

        public (double X, double Y) SampleMetres(int x, int y) =>
            (Tile.SampleX(x) / WorldScene.UnitsPerMetre, Tile.SampleY(y) / WorldScene.UnitsPerMetre);

        public (double Column, double Row) ToGrid(double xMetres, double yMetres) =>
            (((xMetres * WorldScene.UnitsPerMetre) - Tile.SampleX(0)) / Tile.TexelWidth,
             ((yMetres * WorldScene.UnitsPerMetre) - Tile.SampleY(0)) / Tile.TexelHeight);

        public double GetHeight(int x, int y) => Tile.HeightAt(x, y) / WorldScene.UnitsPerMetre;

        public void SetHeight(int x, int y, double zMetres) =>
            Tile.Heights.SetHeight(x, y, (float)((zMetres * WorldScene.UnitsPerMetre) - Tile.BaseHeight));

        public void MarkSaved()
        {
            _savedRevision = Tile.Heights.Revision;
            _savedMaterials = Tile.Materials?.Revision ?? 0;
        }
    }
}
