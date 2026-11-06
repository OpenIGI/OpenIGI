using OpenIGI.Editor.Core.Commands;
using OpenIGI.Game.World;

namespace OpenIGI.Editor.Terrain;

/// <summary>One stroke of material paint, as one undoable step: every cell it changed, with how it was before.</summary>
/// <param name="terrain">The ground painted.</param>
/// <param name="layer">The material layer painted, a <see cref="TerrainPaintLayer.Index"/>.</param>
internal sealed class TerrainPaintStroke(IEditableTerrain terrain, int layer) : IEditorCommand
{
    private readonly Dictionary<(int Grid, int X, int Y), TerrainPaintCell> _cells = [];
    private readonly List<TerrainPaintCell> _scratch = [];

    /// <inheritdoc/>
    public string Name => "Paint Terrain";

    /// <summary>Gets whether the stroke changed anything.</summary>
    public bool IsEmpty => _cells.Count == 0;

    /// <summary>Gets how many cells the stroke changed.</summary>
    public int Count => _cells.Count;

    /// <summary>Paints a dab, remembering each cell's state before the stroke first touched it.</summary>
    /// <param name="xMetres">The dab's centre, world X in metres.</param>
    /// <param name="yMetres">The dab's centre, world Y in metres.</param>
    /// <param name="radiusMetres">Its radius.</param>
    /// <param name="lay">Whether it lays the material or takes it away.</param>
    public void Dab(double xMetres, double yMetres, double radiusMetres, bool lay)
    {
        _scratch.Clear();
        terrain.Paint(layer, xMetres, yMetres, radiusMetres, lay, _scratch);
        foreach (TerrainPaintCell cell in _scratch)
        {
            // A cell painted again keeps the value it had before the stroke first touched it.
            _cells[(cell.Grid, cell.X, cell.Y)] = _cells.TryGetValue((cell.Grid, cell.X, cell.Y), out TerrainPaintCell known)
                ? cell with { Before = known.Before }
                : cell;
        }
    }

    /// <inheritdoc/>
    public void Apply() => terrain.SetPaint(_cells.Values, after: true);

    /// <inheritdoc/>
    public void Undo() => terrain.SetPaint(_cells.Values, after: false);
}
