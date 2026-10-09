using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;
using OpenIGI.Game.Igi2.World;

namespace OpenIGI.Game.Igi2.Paths;

/// <summary>
/// One point of a spline path, I.G.I. 2's <c>SplinePathNodeQTask</c>: where the path passes, which way an object on it faces there,
/// how fast it goes, and the curve's tangent.
/// </summary>
/// <remarks>
/// <para>
/// Reading one (<c>SplinePathNodeQTask_UpdateInternalDataHandler</c>, <c>igi2 0x547EC0</c>) clamps its speed to no less than nought
/// and rebuilds its whole path from the nodes read so far: so a path is built once for every node, and its frames, which each
/// rebuild turns to their tangents, are turned again each time.
/// </para>
/// <para>
/// Its <c>Tangent</c> is used as authored only when <c>UseComputedTangent</c> is off; otherwise the path works it out from its
/// neighbours.
/// </para>
/// </remarks>
public sealed partial class Igi2SplinePathNodeTask : GameTask, Flow.IIgi2CameraTarget
{
    /// <summary>The script name level data instantiates this type under.</summary>
    public const string TypeName = "SplinePathNodeQTask";

    /// <summary>How many lengths it measures its segment in, one per tenth of the way.</summary>
    public const int Tenths = 10;

    private float[] _frame = Igi2Pose.Rotation(0f, 0f, 0f);
    private Vector3d _tangent;
    private readonly float[] _lengthPerTenth = new float[Tenths];

    /// <summary>Gets or sets where the path passes.</summary>
    [ScriptParam(ScriptParamType.ObjectPos, "Position")]
    public Vector3d Position { get; set; }

    /// <summary>Gets or sets which way an object on the path faces here, as level data's three angles.</summary>
    [ScriptParam(ScriptParamType.Real32x9, "Orientation")]
    public Vector3f Orientation { get; set; }

    /// <summary>Gets or sets a value indicating whether the frame is kept as authored rather than turned to the tangent.</summary>
    [ScriptParam(ScriptParamType.Bool8, "Use Free Orientation")]
    public bool IsFreeOrientation { get; set; }

    /// <summary>Gets or sets how fast an object passes here, in kilometres an hour.</summary>
    [ScriptParam(ScriptParamType.Real32, "Speed (KMH)")]
    public float Speed { get; set; }

    /// <summary>Gets or sets the seconds from the path's start, which the path works out again from the lengths and speeds.</summary>
    [ScriptParam(ScriptParamType.Real32, "Time from start (Sec)")]
    public float TimeFromStart { get; set; }

    /// <summary>Gets or sets the curve's tangent here as authored.</summary>
    [ScriptParam(ScriptParamType.Real64x3, "Tangent")]
    public Vector3d AuthoredTangent { get; set; }

    /// <summary>Gets or sets a value indicating whether the path works the tangent out from the neighbours.</summary>
    [ScriptParam(ScriptParamType.Bool8, "UseComputedTangent")]
    public bool IsUseComputedTangent { get; set; } = true;

    /// <summary>Gets the frame an object faces here, row by row, <c>tOrientation</c>.</summary>
    public IReadOnlyList<float> Frame => _frame;

    /// <inheritdoc/>
    /// <remarks><c>SplinePathNodeQTask_GetCameraTargetInfoHandler</c> (<c>igi2 0x547E30</c>): its position and frame.</remarks>
    public Vector3d CameraTargetPosition => Position;

    /// <inheritdoc/>
    public IReadOnlyList<float> CameraTargetOrientation => _frame;

    /// <summary>Gets the frame as a quaternion, <c>tQuatOrientation</c>.</summary>
    public Quaternion32 Rotation { get; private set; } = new(0f, 0f, 0f, 1f);

    /// <summary>Gets the curve's tangent here, <c>tTangent</c>.</summary>
    public Vector3d Tangent => _tangent;

    /// <summary>Gets the length of the segment from here to the next node, <c>vDistToNext</c>; nought at the last.</summary>
    public float DistanceToNext { get; internal set; }

    /// <summary>Gets the length of each tenth of that segment, <c>avDistPerDeltaT</c>.</summary>
    public IReadOnlyList<float> LengthPerTenth => _lengthPerTenth;

    /// <summary>
    /// The share of the segment that one unit of length covers about a point on it,
    /// <c>SplinePathNodeQTask_GetEstimatedTPerMeter</c> (<c>igi2 0x548650</c>): one over ten times the length of the tenth it is in.
    /// </summary>
    /// <param name="t">How far along the segment.</param>
    /// <returns>The share per unit of length.</returns>
    public Fpu TPerUnit(float t)
    {
        int tenth = Fpu.ToInt32((Fpu)t * 10f);
        tenth = Math.Clamp(tenth, 0, Tenths - 1);
        return (Fpu)1f / ((Fpu)_lengthPerTenth[tenth] * 10f);
    }

    /// <summary>
    /// Turns the frame to the tangent, <c>SplinePathNodeQTask_LimitOrientation</c> (<c>igi2 0x5481E0</c>), and takes its
    /// quaternion as <c>SplinePathDynCubeObj_UpdateOrientations</c> (<c>0x5491A0</c>) does.
    /// </summary>
    /// <remarks>
    /// <para>
    /// With no tangent the frame is the identity. A free-oriented node keeps its frame. Otherwise its Y becomes the tangent, its X
    /// the tangent across the old Z, and its Z that X across the tangent, each made a unit; a tangent along the old Z takes its Z
    /// across the world's X instead, from the tangent stored to floats.
    /// </para>
    /// <para>The tangent's doubles go into the products whole: only the results are chopped.</para>
    /// </remarks>
    internal void LimitOrientation()
    {
        Vector3d tangent = _tangent;
        if (tangent.X == 0.0 && tangent.Y == 0.0 && tangent.Z == 0.0)
        {
            _frame = [1f, 0f, 0f, 0f, 1f, 0f, 0f, 0f, 1f];
        }
        else if (!IsFreeOrientation)
        {
            Fpu tx = tangent.X, ty = tangent.Y, tz = tangent.Z;
            float storedY = ty.F, storedZ = tz.F;
            float[] m = _frame;
            float zx = m[2], zy = m[5], zz = m[8];

            // X = T x Z.
            float xx = ((ty * zz) - (tz * zy)).F;
            float xy = (-((tx * zz) - (tz * zx))).F;
            float xz = ((tx * zy) - (ty * zx)).F;
            // Any part not nought, NaN among them, as the parity flag of each comparison says.
            if (xx != 0f || xy != 0f || xz != 0f)
            {
                // Z = X x T.
                zx = (((Fpu)xy * tz) - ((Fpu)xz * ty)).F;
                zy = (-(((Fpu)xx * tz) - ((Fpu)xz * tx))).F;
                zz = (((Fpu)xx * ty) - ((Fpu)xy * tx)).F;
            }
            else
            {
                // Z = (1, 0, 0) x T, then X = T x Z, from the tangent's floats but its X whole.
                Fpu sy = storedY, sz = storedZ;
                Fpu along = tx * 0f;
                zx = (((Fpu)0f * sz) - (sy * 0f)).F;
                zy = (-(sz - along)).F;
                zz = (sy - along).F;
                xx = ((sy * zz) - (sz * zy)).F;
                xy = (-((tx * zz) - (sz * zx))).F;
                xz = ((tx * zy) - (sy * zx)).F;
            }

            Unit(ref zx, ref zy, ref zz);
            Unit(ref xx, ref xy, ref xz);

            // Y is the tangent itself made a unit: its X whole, its Y and Z as floats.
            Fpu yx = tx, yy = storedY, yz = storedZ;
            if (tx.Value != 0.0 || storedY != 0f || storedZ != 0f)
            {
                Fpu inverse = (Fpu)1.0 / Fpu.Sqrt(((tx * tx) + ((Fpu)storedY * storedY)) + ((Fpu)storedZ * storedZ));
                yx = tx * inverse;
                yy = (Fpu)storedY * inverse;
                yz = (Fpu)storedZ * inverse;
            }

            _frame = [xx, yx.F, zx, xy, yy.F, zy, xz, yz.F, zz];
        }

        Rotation = Igi2Pose.ToQuaternion(_frame);
    }

    /// <summary>Sets the curve's tangent here, as the path works it out.</summary>
    /// <param name="tangent">The tangent.</param>
    internal void SetTangent(Vector3d tangent) => _tangent = tangent;

    /// <summary>Sets the length of one tenth of the segment.</summary>
    /// <param name="tenth">Which tenth.</param>
    /// <param name="length">Its length.</param>
    internal void SetLengthPerTenth(int tenth, float length) => _lengthPerTenth[tenth] = length;

    /// <inheritdoc/>
    /// <remarks>
    /// <c>SplinePathNodeQTask_CreateHandler</c> (<c>igi2 0x547F10</c>) and the parameters read: its frame from its angles, its
    /// tangent as authored, its speed no less than nought; then its path is rebuilt with it.
    /// </remarks>
    protected override void OnParsed()
    {
        Vector3f angles = Orientation;
        _frame = Igi2Pose.Rotation(angles.X, angles.Y, angles.Z);
        _tangent = AuthoredTangent;
        if (Speed <= 0f)
        {
            Speed = 0f;
        }

        Array.Clear(_lengthPerTenth);
        DistanceToNext = 0f;
        (Parent as Igi2SplinePathTask)?.Rebuild();
    }

    // Real32x3_Normalize inline: each part times one over the length, when any part is not nought.
    private static void Unit(ref float x, ref float y, ref float z)
    {
        if (x == 0f && y == 0f && z == 0f)
        {
            return;
        }

        Fpu inverse = (Fpu)1.0 / Fpu.Sqrt((((Fpu)x * x) + ((Fpu)y * y)) + ((Fpu)z * z));
        x = ((Fpu)x * inverse).F;
        y = ((Fpu)y * inverse).F;
        z = ((Fpu)z * inverse).F;
    }
}
