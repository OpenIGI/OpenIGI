using OpenIGI.Core.Numerics;

namespace OpenIGI.Game.Igi2.Humans;

/// <content>Putting a body somewhere at once, rather than moving it there.</content>
public abstract partial class Igi2Human
{
    /// <summary>How far below the feet the ground still counts when a body is snapped to it, <c>4096000.0</c>.</summary>
    public const float SnapTolerance = 4096000f;

    /// <summary>
    /// Puts the body at a spot at once: <c>Human_PopPosition</c> (<c>igi2 0x501FA0</c>). A landing's push is forgotten, the next
    /// move is told the body was popped, and a rigid platform underfoot notes where the body now stands on it.
    /// </summary>
    /// <param name="position">Where the body stands.</param>
    /// <remarks>
    /// The original also moves the body's and its view's places in the world's object tree and carries what hangs off them,
    /// and keeps the spot as where the body's shadow falls; nothing here is kept in such a tree, and shadows are drawn
    /// elsewhere.
    /// </remarks>
    public void PopPosition(Vector3d position)
    {
        Position = position;
        ImpactSpeed = default;
        IsPopped = true;
        UpdatePlatformPosition(forcePosition: true, forceOrientation: false);
    }

    /// <summary>Lets go of the ground and puts the body at a spot, as a script moves a human (<c>Human_SetGroundTask</c> with none, then <c>Human_PopPosition</c>).</summary>
    /// <param name="position">Where the body stands.</param>
    public void PopPositionDroppingGround(Vector3d position)
    {
        DropGround();
        PopPosition(position);
    }

    /// <summary>
    /// Stands the body upright, turned to a heading at once: <c>Human_PopOrientation</c> (<c>igi2 0x502330</c>). The heading and
    /// the ideal heading are both set, and a rigid platform underfoot notes the body's new orientation on it.
    /// </summary>
    /// <param name="gamma">The heading, in radians.</param>
    public void PopOrientation(float gamma)
    {
        Fpu sin = Fpu.Sin(gamma);
        IdealGamma = gamma;
        Gamma = gamma;
        float cos = Fpu.Cos(gamma).F;
        Orientation = new Matrix3f(cos, (-sin).F, 0f, sin.F, cos, 0f, 0f, 0f, 1f);
        IsPopped = true;
        UpdatePlatformPosition(forcePosition: false, forceOrientation: true);
    }

    /// <summary>
    /// Settles the body on the ground under where it stands: <c>Human_SnapToGround</c> (<c>igi2 0x5023C0</c>), the ground found
    /// from the body's own spot with a tolerance of a thousand metres, and the body popped there.
    /// </summary>
    public void SnapToGround()
    {
        Vector3d spot = Position;
        GroundCollision(spot, ref spot, SnapTolerance);
        PopPosition(spot);
    }
}
