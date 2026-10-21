using OpenIGI.Core.Numerics;

namespace OpenIGI.Game.Igi2.Paths;

/// <summary>An object a spline path's guide moves, a <c>ModelObj</c>: it is set where the curve passes and turned as it says.</summary>
public interface IIgi2PathRider
{
    /// <summary>Gets where it stands now, <c>tQObj.tPos</c>.</summary>
    Vector3d CurrentPosition { get; }

    /// <summary>
    /// Is moved by a guide: turned, then moved with its sphere through the world (<c>World_MoveDynCubeObjRadius</c>,
    /// <c>igi2 0x54C940</c>), and everything attached to it brought along (<c>ModelObj_UpdateAttachments</c>, <c>0x56D7D0</c>).
    /// </summary>
    /// <param name="position">Where it now stands.</param>
    /// <param name="orientation">Which way it now faces, row by row.</param>
    /// <param name="tick">The level's tick it was moved on.</param>
    void Ride(Vector3d position, IReadOnlyList<float> orientation, int tick);

    /// <summary>
    /// Is told the guide that moves it, whose velocity is its parent speed from then on, <c>tAttachContext.ptParentSpeed</c>
    /// (<c>ModelObj_SplinePathConnectObjectHandler</c>, <c>igi2 0x5E6A90</c>).
    /// </summary>
    /// <param name="guide">The guide.</param>
    void Connect(Igi2SplinePathGuideTask guide);
}
