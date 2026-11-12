using OpenIGI.Core.Numerics;
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
    private readonly Dictionary<PlacementKey, EditorPose> _poses = [];

    /// <summary>Gets or sets the title's own motions, which run first.</summary>
    public IPlacementMotion? Inner { get; set; }

    /// <summary>Gets whether any task is moved.</summary>
    public bool IsEmpty => _poses.Count == 0;

    /// <summary>Sets where everything a task placed is drawn: turned about the task's built position, then moved.</summary>
    /// <param name="owner">What the task placed, as its placements are keyed.</param>
    /// <param name="pose">The pose. An identity one is kept, not dropped: the drawing only updates a placement it is told
    /// moved, so a task put back where the level built it (an undone move) has to be reported moved, back to its own place.</param>
    public void SetPose(PlacementKey owner, EditorPose pose) => _poses[owner] = pose;

    /// <summary>Removes every pose, as when the level is rebuilt.</summary>
    public void Clear() => _poses.Clear();

    /// <inheritdoc/>
    public bool TryGetTransform(WorldPlacement placement, float tickFraction, out Vector3d positionMetres, out Matrix3f orientation)
    {
        // The title's own motion first, and only when it says it moved the placement: an out parameter is written either way,
        // and a title that leaves it zeroed when it has nothing to say would otherwise put every edited object at the origin.
        positionMetres = placement.Position;
        orientation = placement.Orientation;
        bool moved = false;
        if (Inner is { } inner && inner.TryGetTransform(placement, tickFraction, out Vector3d titlePosition, out Matrix3f titleOrientation))
        {
            positionMetres = titlePosition;
            orientation = titleOrientation;
            moved = true;
        }
        if (!_poses.TryGetValue(PlacementKey.Of(placement), out EditorPose pose))
        {
            return moved;
        }

        // A placement maps local to world through its orientation's transpose, so turning it in the world by W makes
        // its world axes W * Mᵀ, which is the orientation M * Wᵀ; its offset from the pivot turns with W directly.
        Vector3d fromPivot = positionMetres - pose.Pivot;
        positionMetres = pose.Pivot + pose.Offset + pose.Turn.Transform(fromPivot);
        orientation = orientation * pose.Turn.Transposed();
        return true;
    }
}

/// <summary>How far an edited task is drawn from where it was built.</summary>
/// <param name="Pivot">The task's position as built, in metres: what it turns about.</param>
/// <param name="Offset">How far it has moved, in metres.</param>
/// <param name="Turn">
/// The turn in world space: with the task's built orientation O and new orientation N, <c>Nᵀ · O</c>; identity when
/// unturned. Applied to a placement of orientation M it gives <c>M · Turnᵀ</c>, which is N for the task's own.
/// </param>
public readonly record struct EditorPose(Vector3d Pivot, Vector3d Offset, Matrix3f Turn)
{
    /// <summary>Gets whether the pose changes nothing.</summary>
    public bool IsIdentity => Offset == default && Turn == Matrix3f.Identity;
}
