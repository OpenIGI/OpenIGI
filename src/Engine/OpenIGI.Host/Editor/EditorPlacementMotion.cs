using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;
using OpenIGI.Game.World;

namespace OpenIGI.Host.Editor;

/// <summary>
/// Moves what an edited task placed, live: the editor's offsets laid over whatever the title's own motions do (a door
/// still swings, from where the editor has moved it).
/// </summary>
/// <remarks>
/// The renderer asks for every placement's transform each frame, so an offset shows on the next frame with no
/// rebuild. Only the picture moves: collision, lighting bakes and AI graphs follow when the level is reloaded from
/// what was saved.
/// </remarks>
internal sealed class EditorPlacementMotion : IPlacementMotion
{
    private readonly Dictionary<GameTask, Vector3d> _offsets = new(ReferenceEqualityComparer.Instance);

    /// <summary>Gets or sets the title's own motions, which run first.</summary>
    public IPlacementMotion? Inner { get; set; }

    /// <summary>Gets whether any task is offset.</summary>
    public bool IsEmpty => _offsets.Count == 0;

    /// <summary>Sets how far everything a task placed is drawn from where it was built.</summary>
    /// <param name="owner">The task.</param>
    /// <param name="offsetMetres">The offset; zero removes it.</param>
    public void SetOffset(GameTask owner, Vector3d offsetMetres)
    {
        if (offsetMetres == default)
        {
            _offsets.Remove(owner);
        }
        else
        {
            _offsets[owner] = offsetMetres;
        }
    }

    /// <summary>Removes every offset, as when the level is rebuilt.</summary>
    public void Clear() => _offsets.Clear();

    /// <inheritdoc/>
    public bool TryGetTransform(WorldPlacement placement, float tickFraction, out Vector3d positionMetres, out Matrix3f orientation)
    {
        positionMetres = placement.Position;
        orientation = placement.Orientation;
        bool moved = Inner is { } inner && inner.TryGetTransform(placement, tickFraction, out positionMetres, out orientation);
        if (!moved)
        {
            positionMetres = placement.Position;
            orientation = placement.Orientation;
        }

        if (_offsets.TryGetValue(placement.Owner, out Vector3d offset))
        {
            positionMetres += offset;
            return true;
        }

        return moved;
    }
}
