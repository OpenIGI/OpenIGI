using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi2.World;
using OpenIGI.Game.World;

namespace OpenIGI.Game.Igi2.Humans;

/// <content>
/// Riding something: a human held to a ladder or a mounted gun goes where that thing carries it and faces as it turns
/// (<c>human.c Human_AttachToTask</c>, <c>Human_Attach</c>, <c>Human_DetachFromTask</c>).
/// </content>
public abstract partial class Igi2Human
{
    private readonly float[] _attachOrientation = EngineEuler.Rotation(0f, 0f, 0f);

    /// <summary>Gets the body's frame, row by row, as the original reads <c>tOrientation</c>.</summary>
    public float[] OrientationRows
    {
        get
        {
            Matrix3f o = Orientation;
            return [o.M11, o.M12, o.M13, o.M21, o.M22, o.M23, o.M31, o.M32, o.M33];
        }
    }

    /// <summary>Gets what the human rides, <c>Human_s.ptAttachQTask</c>, or <see langword="null"/>.</summary>
    public IAttachParent? AttachParent { get; private set; }

    /// <summary>Gets where the human stands in the frame of what it rides, <c>Human_s.tAttachPos</c>.</summary>
    public Vector3d AttachPosition { get; private set; }

    /// <summary>Gets how the human is turned in the frame of what it rides, <c>Human_s.tAttachOrientation</c>, row by row.</summary>
    public IReadOnlyList<float> AttachOrientation => _attachOrientation;

    /// <summary>
    /// Holds the human to something it rides, <c>Human_AttachToTask</c> (<c>igi2 0x503A60</c>): a position and an orientation in the
    /// world, taken into the parent's frame.
    /// </summary>
    /// <param name="parent">What it rides.</param>
    /// <param name="position">Where it stands, in the world.</param>
    /// <param name="orientation">How it is turned, in the world, row by row.</param>
    public void AttachTo(IAttachParent parent, Vector3d position, IReadOnlyList<float> orientation)
    {
        ArgumentNullException.ThrowIfNull(parent);
        ArgumentNullException.ThrowIfNull(orientation);
        AttachParent = parent;
        IReadOnlyList<float> a = parent.AttachOrientation;
        Vector3d at = parent.AttachPosition;
        Fpu dx = (Fpu)position.X - at.X;
        Fpu dy = (Fpu)position.Y - at.Y;
        Fpu dz = (Fpu)position.Z - at.Z;
        AttachPosition = new Vector3d(
            (((dy * a[3]) + (dz * a[6])) + (dx * a[0])).Value,
            (((dy * a[4]) + (dz * a[7])) + (dx * a[1])).Value,
            (((dy * a[5]) + (dz * a[8])) + (dx * a[2])).Value);

        // The parent's frame transposed, times the orientation: tX.vX = (Zx·O.Zx + Yx·O.Yx) + Xx·O.Xx, and so on.
        IReadOnlyList<float> o = orientation;
        _attachOrientation[0] = ((((Fpu)a[6] * o[6]) + ((Fpu)a[3] * o[3])) + ((Fpu)a[0] * o[0])).F;
        _attachOrientation[1] = ((((Fpu)a[0] * o[1]) + ((Fpu)a[6] * o[7])) + ((Fpu)a[3] * o[4])).F;
        _attachOrientation[2] = ((((Fpu)a[6] * o[8]) + ((Fpu)a[3] * o[5])) + ((Fpu)a[0] * o[2])).F;
        _attachOrientation[3] = ((((Fpu)a[7] * o[6]) + ((Fpu)a[4] * o[3])) + ((Fpu)a[1] * o[0])).F;
        _attachOrientation[4] = ((((Fpu)a[1] * o[1]) + ((Fpu)a[7] * o[7])) + ((Fpu)a[4] * o[4])).F;
        _attachOrientation[5] = ((((Fpu)a[7] * o[8]) + ((Fpu)a[4] * o[5])) + ((Fpu)a[1] * o[2])).F;
        _attachOrientation[6] = ((((Fpu)a[8] * o[6]) + ((Fpu)a[5] * o[3])) + ((Fpu)a[2] * o[0])).F;
        _attachOrientation[7] = ((((Fpu)a[2] * o[1]) + ((Fpu)a[8] * o[7])) + ((Fpu)a[5] * o[4])).F;
        _attachOrientation[8] = ((((Fpu)a[8] * o[8]) + ((Fpu)a[5] * o[5])) + ((Fpu)a[2] * o[2])).F;
    }

    /// <summary>Lets go of what the human rides, <c>Human_DetachFromTask</c> (<c>igi2 0x503E00</c>).</summary>
    public void Detach() => AttachParent = null;

    /// <summary>
    /// Carries a riding human with what it rides, <c>Human_Attach</c> (<c>igi2 0x502F00</c>), in place of <see cref="Move"/>: its
    /// speed and this tick's step (unless the step is disabled) are turned into the parent's frame and added to where it stands
    /// there, and it is put where the parent now holds that point. The step is used up.
    /// </summary>
    public void FollowAttachment()
    {
        if (AttachParent is not { } parent)
        {
            return;
        }

        IReadOnlyList<float> a = parent.AttachOrientation;
        Vector3f speed = Speed;
        Fpu sx = (Fpu)speed.X;
        Fpu sy = (Fpu)speed.Y;
        Fpu sz = (Fpu)speed.Z;
        if (!IsDisableDeltaTranslation)
        {
            sx += DeltaTranslation.X;
            sy += DeltaTranslation.Y;
            sz += DeltaTranslation.Z;
        }

        sx = (Fpu)sx.F;
        sy = (Fpu)sy.F;
        sz = (Fpu)sz.F;
        Fpu localX = ((sx * a[0]) + (sz * a[6])) + (sy * a[3]);
        float localY = (((sz * a[7]) + (sy * a[4])) + (sx * a[1])).F;
        float localZ = (((sx * a[2]) + (sz * a[8])) + (sy * a[5])).F;
        Fpu x = localX + AttachPosition.X;
        Fpu y = (Fpu)localY + AttachPosition.Y;
        Fpu z = (Fpu)localZ + AttachPosition.Z;
        AttachPosition = new Vector3d(x.Value, y.Value, z.Value);

        Vector3d at = parent.AttachPosition;
        Fpu worldX = ((((Fpu)a[2] * z) + ((Fpu)a[0] * x)) + ((Fpu)a[1] * y)) + at.X;
        Fpu worldY = ((((Fpu)a[4] * y) + ((Fpu)a[5] * z)) + ((Fpu)a[3] * x)) + at.Y;
        Fpu worldZ = ((((Fpu)a[7] * y) + ((Fpu)a[8] * z)) + ((Fpu)a[6] * x)) + at.Z;
        Position = new Vector3d(worldX.Value, worldY.Value, worldZ.Value);
        DeltaTranslation = default;
    }

    /// <summary>The orientation a riding human faces: the parent's frame times its own in that frame (<c>Human_UpdateBody</c>).</summary>
    private float[] RiddenOrientation(IAttachParent parent)
    {
        IReadOnlyList<float> a = parent.AttachOrientation;
        float[] l = _attachOrientation;
        float[] r = new float[9];
        for (int c = 0; c < 3; c++)
        {
            r[c] = ((((Fpu)a[0] * l[c]) + ((Fpu)l[6 + c] * a[2])) + ((Fpu)a[1] * l[3 + c])).F;
            r[3 + c] = ((((Fpu)l[3 + c] * a[4]) + ((Fpu)l[c] * a[3])) + ((Fpu)l[6 + c] * a[5])).F;
            r[6 + c] = ((((Fpu)l[6 + c] * a[8]) + ((Fpu)l[3 + c] * a[7])) + ((Fpu)l[c] * a[6])).F;
        }

        return r;
    }
}
