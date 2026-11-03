using OpenIGI.Core.Numerics;
using OpenIGI.Editor.Core.Commands;
using OpenIGI.Game.World;

namespace OpenIGI.Editor.Terrain;

/// <summary>Lays a mountain area over the ground around a point, as one undoable step.</summary>
/// <param name="terrain">The ground.</param>
/// <param name="pointMetres">The point, in world metres.</param>
/// <remarks>
/// Undoing takes the area away with whatever was shaped in it; redoing lays a fresh one from the ground as it then is, so
/// strokes recorded on the old one do nothing when they are redone.
/// </remarks>
internal sealed class AddMountainCommand(IEditableTerrain terrain, Vector3d pointMetres) : IEditorCommand
{
    private ITerrainHeightGrid? _area;

    /// <inheritdoc/>
    public string Name => "Add Mountain Area";

    /// <summary>Lays the area for the first time.</summary>
    /// <returns>Whether there was room for one there.</returns>
    public bool TryApply()
    {
        _area = terrain.AddMountain(pointMetres);
        return _area is not null;
    }

    /// <inheritdoc/>
    public void Apply() => _area ??= terrain.AddMountain(pointMetres);

    /// <inheritdoc/>
    public void Undo()
    {
        if (_area is { } area)
        {
            terrain.RemoveMountain(area);
            _area = null;
        }
    }
}
