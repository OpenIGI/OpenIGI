using OpenIGI.Core.Numerics;

namespace OpenIGI.Game.World;

/// <summary>
/// A part that folds on a plane -- a landing gear leg, the nose-gear hatch, the canopy -- placed on the airframe at its
/// attachment and turned about its own X by its angle.
/// </summary>
public abstract class RetailPlanePart
{
    /// <summary>The original's degrees-to-radians, <c>0x1.1df46ap-6f</c>.</summary>
    private protected static readonly float DegreesToRadians = BitConverter.Int32BitsToSingle(0x3C8EFA35);

    /// <summary>Initializes a new instance of the <see cref="RetailPlanePart"/> class.</summary>
    /// <param name="local">The attach point in the plane's frame (<c>+240</c>).</param>
    /// <param name="baseOrientation">The attachment's orientation, row-major (<c>+264</c>).</param>
    private protected RetailPlanePart(Vector3d local, float[] baseOrientation)
    {
        ArgumentNullException.ThrowIfNull(baseOrientation);
        Local = local;
        Array.Copy(baseOrientation, Base, 9);
    }

    /// <summary>Gets the attach point, plane frame (<c>+240</c>).</summary>
    public Vector3d Local { get; }

    /// <summary>Gets the attachment's orientation (<c>+264</c>).</summary>
    public float[] Base { get; } = new float[9];

    /// <summary>Gets or sets the angle it is turned through, radians (<c>+316</c>).</summary>
    public float Angle { get; set; }

    /// <summary>Gets its world position (<c>+32</c>).</summary>
    public double[] World { get; } = new double[3];

    /// <summary>Gets its world orientation (<c>+112</c>).</summary>
    public float[] M { get; } = [1f, 0f, 0f, 0f, 1f, 0f, 0f, 0f, 1f];

    /// <summary>
    /// Gets a value indicating whether its attachment is a mirror image. The original turns the part by the rebuilt
    /// rotation, which is the mirror with its own Z turned over, and draws its model with that Z scaled back by -1.
    /// </summary>
    public bool Mirrored { get; init; }

    /// <summary>Gets how its model is drawn: its orientation, with a mirrored part's Z turned back.</summary>
    public Matrix3f DrawOrientation => Mirrored
        ? new Matrix3f(M[0], M[1], -M[2], M[3], M[4], -M[5], M[6], M[7], -M[8])
        : new Matrix3f(M[0], M[1], M[2], M[3], M[4], M[5], M[6], M[7], M[8]);

    /// <summary>
    /// Follows the plane (<c>0x435A80</c>): <c>M·l + pos</c> in doubles, then its orientation from its angle, a steering
    /// turn and its attachment turn (<c>0x4355A0</c>; the hatch's and the canopy's <c>0x434F50</c> is the same with no steering).
    /// </summary>
    /// <param name="position">The plane's position.</param>
    /// <param name="m">The plane's orientation.</param>
    /// <param name="steer">The steering angle (yaw control × π/4 on a steering leg with the gear down, else 0).</param>
    public void Follow(double[] position, float[] m, float steer)
    {
        Vector3d l = Local;
        double wx = ((X87)m[2] * l.Z + (X87)m[1] * l.Y + (X87)m[0] * l.X).Value;
        double wy = ((X87)m[5] * l.Z + (X87)m[4] * l.Y + (X87)m[3] * l.X).Value;
        double wz = ((X87)m[8] * l.Z + (X87)m[7] * l.Y).Value;
        wz = ((X87)wz + (X87)m[6] * l.X).Value;
        World[0] = X87.Add(wx, position[0]);
        World[1] = X87.Add(wy, position[1]);
        World[2] = X87.Add(wz, position[2]);

        Span<float> r = stackalloc float[9];
        RetailPlaneBody.Euler(r, Angle, 0.0f, steer);
        float[] b = Base;
        X87 v3 = (X87)r[0] * b[0] + (X87)r[6] * b[2] + (X87)r[3] * b[1];
        float v11 = ((X87)r[1] * b[0] + (X87)r[7] * b[2] + (X87)r[4] * b[1]).F;
        float v12 = ((X87)r[2] * b[0] + (X87)r[8] * b[2] + (X87)r[5] * b[1]).F;
        X87 v4 = (X87)r[0] * b[3] + (X87)r[6] * b[5] + (X87)r[3] * b[4];
        float v13 = ((X87)r[1] * b[3] + (X87)r[7] * b[5] + (X87)r[4] * b[4]).F;
        float v14 = ((X87)r[2] * b[3] + (X87)r[8] * b[5] + (X87)r[5] * b[4]).F;
        X87 v5 = (X87)r[6] * b[8] + (X87)r[3] * b[7] + (X87)r[0] * b[6];
        float v15 = ((X87)r[7] * b[8] + (X87)r[4] * b[7] + (X87)r[1] * b[6]).F;
        float v16 = ((X87)r[8] * b[8] + (X87)r[5] * b[7] + (X87)r[2] * b[6]).F;
        float[] g = M;
        g[0] = (v5 * m[2] + v4 * m[1] + v3 * m[0]).F;
        g[1] = ((X87)v15 * m[2] + (X87)v13 * m[1] + (X87)v11 * m[0]).F;
        g[2] = ((X87)v16 * m[2] + (X87)v14 * m[1] + (X87)v12 * m[0]).F;
        g[3] = (v3 * m[3] + v5 * m[5] + v4 * m[4]).F;
        g[4] = ((X87)v11 * m[3] + (X87)v15 * m[5] + (X87)v13 * m[4]).F;
        g[5] = ((X87)v12 * m[3] + (X87)v16 * m[5] + (X87)v14 * m[4]).F;
        g[6] = (v3 * m[6] + v5 * m[8] + v4 * m[7]).F;
        g[7] = ((X87)v11 * m[6] + (X87)v15 * m[8] + (X87)v13 * m[7]).F;
        g[8] = ((X87)v12 * m[6] + (X87)v16 * m[8] + (X87)v14 * m[7]).F;
    }
}

/// <summary>One landing gear leg: a <c>Gear</c> attachment (<c>0x435460</c>), 320 bytes.</summary>
public sealed class RetailPlaneGear : RetailPlanePart
{
    private static readonly float Retention = BitConverter.Int32BitsToSingle(0x3F7AE148);
    private static readonly float Approach = BitConverter.Int32BitsToSingle(0x3CA3D700);
    private static readonly float Snap = BitConverter.Int32BitsToSingle(0x3C23D70A);
    private static readonly float Home = BitConverter.Int32BitsToSingle(0x3DCCCCCD);

    /// <summary>Initializes a new instance of the <see cref="RetailPlaneGear"/> class.</summary>
    /// <param name="local">The attach point in the plane's frame (<c>+240</c>).</param>
    /// <param name="baseOrientation">The attachment's orientation, row-major (<c>+264</c>).</param>
    /// <param name="record">The leg's record (<c>+312</c>).</param>
    /// <param name="vertex">The leg model's magic vertex 0: where its wheel touches.</param>
    public RetailPlaneGear(Vector3d local, float[] baseOrientation, MagicGear record, Vector3f vertex)
        : base(local, baseOrientation)
    {
        Record = record;
        Vertex = vertex;
    }

    /// <summary>Gets the leg's record.</summary>
    public MagicGear Record { get; }

    /// <summary>Gets the wheel's contact vertex, model frame.</summary>
    public Vector3f Vertex { get; }

    /// <summary>The fold angle the leg starts at (<c>0x435460</c>): none with the gear down, the full fold with it up.</summary>
    /// <param name="gearDown">The plane's gear byte (<c>+2093</c>).</param>
    public void Seat(bool gearDown) =>
        Angle = gearDown ? 0.0f : ((X87)Record.FoldDegrees * DegreesToRadians).F;

    /// <summary>The leg's update (<c>0x435930</c>): the fold eases toward its target; returns the gear state byte.</summary>
    /// <param name="gearDown">The plane's gear byte (<c>+2093</c>).</param>
    /// <param name="gearArgument">The plane's <c>+2099</c>.</param>
    /// <returns>The value the update writes to the plane's <c>+2098</c>.</returns>
    public sbyte Update(bool gearDown, sbyte gearArgument)
    {
        X87 fold = (X87)Record.FoldDegrees * DegreesToRadians;
        X87 target = gearDown && gearArgument == 1 ? (X87)0.0f : fold;
        if (target.Value != Angle)
        {
            Angle = ((X87)Angle * Retention + target * Approach).F;
        }

        if (X87.Abs((X87)Angle - target).Value < Snap)
        {
            Angle = target.F;
        }

        return PartState(fold, Angle);
    }

    /// <summary>
    /// What a folding part reports of where it is: 1 within a tenth of a radian of rest, 0 within a tenth of its full
    /// angle, -1 between.
    /// </summary>
    /// <param name="full">Its full angle.</param>
    /// <param name="angle">Its angle now.</param>
    /// <returns>Its state byte.</returns>
    internal static sbyte PartState(X87 full, float angle)
    {
        float a = Math.Abs(angle);
        if (a < Home)
        {
            return 1;
        }

        return X87.Abs(full - a).Value < 0.1 ? (sbyte)0 : (sbyte)-1;
    }

    /// <summary>The wheel vertex turned by the leg (<c>0x4D0A50</c>, unit scale): world offset from the leg's origin.</summary>
    /// <returns>The offset.</returns>
    public Vector3d WheelOffset()
    {
        float[] g = M;
        X87 x = Vertex.X;
        float y = Vertex.Y;
        float z = Vertex.Z;
        double ox = X87.ToFloat(((X87)z * g[2] + (X87)y * g[1] + x * g[0]).Value);
        double oy = ((X87)z * g[5] + (X87)y * g[4] + x * g[3]).Value;
        double oz = X87.ToFloat(((X87)z * g[8] + (X87)y * g[7] + x * g[6]).Value);
        return new Vector3d(ox, oy, oz);
    }
}

/// <summary>
/// The hatch over a plane's nose gear: a <c>Hatch</c> attachment (<c>0x435C50</c>). It shuts once the gear is ordered up
/// and has folded away, and opens again whenever not; the legs come down only while it reports itself open.
/// </summary>
public sealed class RetailPlaneHatch : RetailPlanePart
{
    private static readonly float Retention = BitConverter.Int32BitsToSingle(0x3F7851EC);
    private static readonly float Approach = BitConverter.Int32BitsToSingle(0x3CF5C280);

    /// <summary>Initializes a new instance of the <see cref="RetailPlaneHatch"/> class.</summary>
    /// <param name="local">The attach point in the plane's frame.</param>
    /// <param name="baseOrientation">The attachment's orientation, row-major.</param>
    /// <param name="degrees">The angle it shuts through, degrees (<c>0x436090</c>).</param>
    /// <param name="order">How many of the plane's legs come before it in its attachments, which run before it does.</param>
    public RetailPlaneHatch(Vector3d local, float[] baseOrientation, float degrees, int order)
        : base(local, baseOrientation)
    {
        Degrees = degrees;
        Order = order;
    }

    /// <summary>Gets the angle it shuts through, degrees.</summary>
    public float Degrees { get; }

    /// <summary>Gets how many legs run before it.</summary>
    public int Order { get; }

    /// <summary>Makes it: open with the gear down, shut with it up (<c>0x435C50</c>).</summary>
    /// <param name="gearDown">The plane's gear byte (<c>+2093</c>).</param>
    public void Seat(bool gearDown) => Angle = gearDown ? 0.0f : ((X87)Degrees * DegreesToRadians).F;

    /// <summary>Its update (<c>0x435E50</c>); returns what it writes to the plane's <c>+2099</c>.</summary>
    /// <param name="gearDown">The plane's gear byte (<c>+2093</c>).</param>
    /// <param name="gearState">The plane's gear state (<c>+2098</c>) as the legs before it left it.</param>
    /// <returns>Its state byte.</returns>
    public sbyte Update(bool gearDown, sbyte gearState)
    {
        X87 full = (X87)Degrees * DegreesToRadians;
        Angle = !gearDown && gearState == 0
            ? ((X87)Angle * Retention + full * Approach).F
            : ((X87)Angle * Retention).F;
        return RetailPlaneGear.PartState(full, Angle);
    }
}

/// <summary>A plane's canopy: a <c>CockpitShield</c> attachment (<c>0x434E10</c>), which swings open while the canopy byte is set.</summary>
public sealed class RetailPlaneCanopy : RetailPlanePart
{
    private static readonly float Retention = BitConverter.Int32BitsToSingle(0x3F7AE148);
    private static readonly float Approach = BitConverter.Int32BitsToSingle(0x3CA3D700);

    /// <summary>Initializes a new instance of the <see cref="RetailPlaneCanopy"/> class.</summary>
    /// <param name="local">The attach point in the plane's frame.</param>
    /// <param name="baseOrientation">The attachment's orientation, row-major.</param>
    /// <param name="degrees">The angle it swings open through, degrees (<c>0x436090</c>).</param>
    public RetailPlaneCanopy(Vector3d local, float[] baseOrientation, float degrees)
        : base(local, baseOrientation) => Degrees = degrees;

    /// <summary>Gets the angle it swings open through, degrees.</summary>
    public float Degrees { get; }

    /// <summary>Makes it: open with the canopy byte set, shut without (<c>0x434E10</c>).</summary>
    /// <param name="open">The plane's canopy byte, which channel 5 toggles.</param>
    public void Seat(bool open) => Angle = open ? ((X87)Degrees * DegreesToRadians).F : 0.0f;

    /// <summary>Its update (<c>0x4352A0</c>): it eases toward open or shut.</summary>
    /// <param name="open">The plane's canopy byte.</param>
    public void Update(bool open)
    {
        X87 target = open ? (X87)Degrees * DegreesToRadians : (X87)0.0f;
        if (target.Value != Angle)
        {
            Angle = ((X87)Angle * Retention + target * Approach).F;
        }
    }
}

/// <summary>
/// One of a plane's control surfaces: a <c>Rudder</c> attachment (<c>0x4360C0</c>), turned about its own X by the plane's
/// smoothed controls as it follows the airframe (<c>0x4362C0</c>).
/// </summary>
public sealed class RetailPlaneRudder : RetailPlanePart
{
    /// <summary>Initializes a new instance of the <see cref="RetailPlaneRudder"/> class.</summary>
    /// <param name="local">The attach point in the plane's frame.</param>
    /// <param name="baseOrientation">The attachment's orientation, row-major.</param>
    /// <param name="record">Its kind and throw.</param>
    public RetailPlaneRudder(Vector3d local, float[] baseOrientation, MagicRudder record)
        : base(local, baseOrientation) => Record = record;

    /// <summary>Gets its kind and throw.</summary>
    public MagicRudder Record { get; }

    /// <summary>Turns it for the plane's controls, as its move does.</summary>
    /// <param name="controls">The plane's smoothed pitch, roll and yaw.</param>
    /// <param name="limit">The type's first axis rate, which caps a taileron's throw.</param>
    /// <remarks>
    /// An aileron turns against the roll; a taileron by the pitch less (left) or plus (right) the roll, never past the
    /// limit; the rudder against the yaw. A surface of any other kind keeps the model's largest scale, which the
    /// original's angle shared a stack slot with -- one, for every model the game ships.
    /// </remarks>
    public void Aim(float[] controls, float limit)
    {
        ArgumentNullException.ThrowIfNull(controls);
        X87 k = (X87)Record.Degrees * DegreesToRadians;
        X87 v;
        switch (Record.Kind)
        {
            case 0:
                Angle = (-(k * controls[0])).F;
                return;
            case 1:
                v = (k * controls[1]) - (k * controls[0]);
                Angle = v.Value >= limit ? limit : v.F;
                return;
            case 2:
                v = (k * controls[1]) + (k * controls[0]);
                Angle = v.Value >= limit ? limit : v.F;
                return;
            case 3:
                Angle = (-(k * controls[2])).F;
                return;
            default:
                Angle = 1.0f;
                return;
        }
    }
}

