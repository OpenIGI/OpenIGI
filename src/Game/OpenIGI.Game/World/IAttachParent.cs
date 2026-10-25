using OpenIGI.Core.Numerics;

namespace OpenIGI.Game.World;

/// <summary>
/// A parent its children attach to, as a lift's are: <c>Attach_eAttachEvent</c> gives a child the parent's position and
/// orientation to hold itself against (<c>Lift_AttachHandler</c>, <c>igi2 0x46EBB0</c>).
/// </summary>
/// <remarks>
/// The engine's own frame of a carried thing: a position in doubles and an orientation of nine floats, row by row. Project
/// I.G.I. reads a nav graph on a train in it as I.G.I. 2 does (<c>0x4F9770</c>), and holds a human to what it rides with it
/// (<c>Human_SetParent</c>, <c>0x463310</c>).
/// </remarks>
public interface IAttachParent
{
    /// <summary>Gets where the parent stands, <c>AttachContext_t.ptParentPos</c>.</summary>
    Vector3d AttachPosition { get; }

    /// <summary>Gets which way the parent faces, <c>AttachContext_t.ptParentOrientation</c>, row by row.</summary>
    IReadOnlyList<float> AttachOrientation { get; }
}
