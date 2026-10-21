using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;

namespace OpenIGI.Game.World;

/// <summary>What one attached task rides: its parent and the frame it was parented in (<c>Human_SetParent</c>, <c>0x463310</c>).</summary>
internal sealed class CutsceneAttachmentState(
    GameTask parentTask,
    CutsceneTransformState parent,
    int parentRevisionAtCapture,
    Vector3d parentOriginPosition,
    Matrix3f parentOriginOrientation,
    Vector3d position,
    Matrix3f orientation)
{
    public GameTask ParentTask { get; } = parentTask;
    public CutsceneTransformState Parent { get; } = parent;
    public int ParentRevisionAtCapture { get; } = parentRevisionAtCapture;
    public Vector3d ParentOriginPosition { get; } = parentOriginPosition;
    public Matrix3f ParentOriginOrientation { get; } = parentOriginOrientation;
    public Vector3d Position { get; } = position;
    public Matrix3f Orientation { get; } = orientation;
    public Vector3d RelativePosition { get; set; } =
        parentOriginOrientation.Transform(position - parentOriginPosition);
}
