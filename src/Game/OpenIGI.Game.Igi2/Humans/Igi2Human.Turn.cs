using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi2.World;

namespace OpenIGI.Game.Igi2.Humans;

/// <summary>The body's turn: its angles, and the orientation built from them.</summary>
public abstract partial class Igi2Human
{
    /// <summary>π as the float <c>0x40490FDB</c>, which scales the turn input into radians.</summary>
    private const float Pi = 3.1415927f;

    /// <summary>Gets or sets the body's pitch, <c>vAlpha</c>; zero for a body that is not riding something.</summary>
    public float Alpha { get; set; }

    /// <summary>Gets or sets the body's roll, <c>vBeta</c>; zero for a body that is not riding something.</summary>
    public float Beta { get; set; }

    /// <summary>Gets or sets the ground's normal averaged under a lying or dead body, <c>tLastAveragedGroundNormal</c>.</summary>
    /// <remarks>Starts straight up, (0, 0, 1) (<c>Human_CreateHandler</c>).</remarks>
    public Vector3f AveragedGroundNormal { get; set; } = new(0f, 0f, 1f);

    /// <summary>
    /// Turns the body by the turn input and rebuilds its orientation: <c>Human_UpdateBody</c> (<c>igi2 0x500F30</c>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// The horizontal look input times π times the view's field of view is this tick's turn; a locked body does not
    /// turn. A body that turns on a rigid platform first takes the platform's turn
    /// (<see cref="UpdatePlatformMovement"/>) and notes its new orientation on the platform afterwards. The turn
    /// is added to the ideal heading, which is not wrapped, and becomes the heading and the turning speed.
    /// </para>
    /// <para>
    /// The orientation is <c>Real32x9_Create</c> of the angles. A body lying flat, or dead, is then tilted onto the
    /// ground under it: the averaged ground normal is taken into the body's frame, two <c>fpatan</c>s give the pitch
    /// and the roll that stand it on that normal, and the orientation is multiplied by the tilt they make.
    /// </para>
    /// <para>
    /// A lying body's turn is first checked for room (<see cref="CheckTurnRoom"/>) and refused whole when it has none.
    /// A body riding something (<see cref="AttachParent"/>) reads no turn input: its angles are those of what it rides times its
    /// own orientation there.
    /// </para>
    /// </remarks>
    public void UpdateBody()
    {
        if (AttachParent is { } parent)
        {
            // Riding: the angles of the parent's frame times the body's own in it; no turn input is read.
            (float alpha, float beta, float gamma) = Igi2Pose.Angles(RiddenOrientation(parent));
            Alpha = alpha;
            Beta = beta;
            Gamma = gamma;
            IdealGamma = gamma;
            BuildOrientation(turnsOnPlatform: false);
            return;
        }

        float input = IsLockOrientation ? 0f : Channels[Igi2HumanChannel.LookX];
        bool turnsOnPlatform = false;
        if (input != 0f && !float.IsNaN(input))
        {
            turnsOnPlatform = true;
            Vector3d unused = default;
            UpdatePlatformMovement(ref unused, updatePosition: false, updateOrientation: true);
        }

        Alpha = 0f;
        Beta = 0f;
        Fpu turn = (-((Fpu)input * Pi)) * View.FieldOfView;
        if (turn.Value != 0.0 && Pose == Igi2HumanPose.LyingFlat)
        {
            turn = CheckTurnRoom(turn);
        }

        Fpu heading = turn + IdealGamma;
        IdealGamma = heading.F;
        TurningSpeed = turn.F;
        Gamma = IdealGamma;
        BuildOrientation(turnsOnPlatform);
    }

    /// <summary>The orientation from the angles, tilted onto the ground when lying or dead (<c>Human_UpdateBody</c>'s end).</summary>
    private void BuildOrientation(bool turnsOnPlatform)
    {
        float[] orientation = Igi2Pose.Rotation(Alpha, Beta, Gamma);
        if (IsDead || Pose == Igi2HumanPose.LyingFlat)
        {
            TiltOntoGround(orientation);
        }

        Orientation = new Matrix3f(
            orientation[0], orientation[1], orientation[2],
            orientation[3], orientation[4], orientation[5],
            orientation[6], orientation[7], orientation[8]);
        if (turnsOnPlatform)
        {
            UpdatePlatformPosition(forcePosition: false, forceOrientation: true);
        }
    }

    /// <summary>Tilts an orientation so its up axis stands on the averaged ground normal.</summary>
    private void TiltOntoGround(float[] o)
    {
        Vector3f n = AveragedGroundNormal;
        float across = (((Fpu)n.Z * o[6]) + ((Fpu)n.Y * o[3]) + ((Fpu)o[0] * n.X)).F;
        Fpu ahead = ((Fpu)n.Z * o[7]) + ((Fpu)n.Y * o[4]) + ((Fpu)o[1] * n.X);
        Fpu up = ((Fpu)n.Z * o[8]) + ((Fpu)n.Y * o[5]) + ((Fpu)n.X * o[2]);
        if (up.Value == 0.0)
        {
            return;
        }

        Fpu inverse = (Fpu)1f / up;
        float roll = Fpu.Atan2((Fpu)across * inverse, 1.0).F;
        float pitch = (-Fpu.Atan2(ahead * inverse, 1.0)).F;
        float[] t = Igi2Pose.Rotation(pitch, roll, 0f);
        float[] r = new float[9];
        for (int c = 0; c < 3; c++)
        {
            r[c] = (((Fpu)t[6 + c] * o[2]) + ((Fpu)t[3 + c] * o[1]) + ((Fpu)t[c] * o[0])).F;
            r[3 + c] = (((Fpu)t[3 + c] * o[4]) + ((Fpu)t[c] * o[3]) + ((Fpu)t[6 + c] * o[5])).F;
            r[6 + c] = (((Fpu)t[6 + c] * o[8]) + ((Fpu)t[c] * o[6]) + ((Fpu)t[3 + c] * o[7])).F;
        }

        r.CopyTo(o, 0);
    }
}
