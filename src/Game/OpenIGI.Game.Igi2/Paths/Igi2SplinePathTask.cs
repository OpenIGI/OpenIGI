using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;
using OpenIGI.Game.Igi2.World;

namespace OpenIGI.Game.Igi2.Paths;

/// <summary>
/// A spline path an object is guided along, I.G.I. 2's <c>SplinePathDynCubeObj</c>: its child nodes joined by cubic Hermite
/// segments.
/// </summary>
/// <remarks>
/// <para>
/// A path of fewer than three nodes is never measured nor ridden. Its nodes are its <c>SplinePathNodeQTask</c> children, the
/// first hundred, in order (<c>SplinePathDynCubeObj_ResetPath</c>, <c>igi2 0x5486F0</c>).
/// </para>
/// <para>
/// Measuring it (<c>SplinePathDynCubeObj_InitPath</c>, <c>0x548DA0</c>): a node that computes its tangent takes the step to the
/// next for the first, from the one before for the last, and half the step across its neighbours between. Each segment's length
/// is summed over a hundred steps of a hundredth, and each tenth of it over twenty steps of a two-hundredth; and each node's time
/// from the start is the last one's plus the segment's length over the mean of the two speeds.
/// </para>
/// </remarks>
public sealed partial class Igi2SplinePathTask : GameTask
{
    /// <summary>The script name level data instantiates this type under.</summary>
    public const string TypeName = "SplinePathDynCubeObj";

    /// <summary>The most nodes a path holds, <c>aptNode[100]</c>.</summary>
    public const int MaxNodes = 100;

    /// <summary>The fewest nodes a path is measured and ridden with.</summary>
    public const int MinNodes = 3;

    // The steps a segment's length is summed over, a hundredth, and a tenth's over, a two-hundredth: 0x3C23D70A and 0x3BA3D70A.
    private const float LengthStep = 0.0099999998f;
    private const float TenthStep = 0.0049999999f;

    // A km/h as world units a second, 1 / 3.6, and half of one over 4096: the mean of two speeds in metres a second.
    private const float KilometresAnHour = 0.27777779f;
    private const float HalfMetre = 0.00048828125f;

    private readonly List<Igi2SplinePathNodeTask> _nodes = [];

    /// <summary>Gets or sets a value indicating whether the editor draws it.</summary>
    [ScriptParam(ScriptParamType.Bool8, "Show in editor")]
    public bool IsShowInEditor { get; set; } = true;

    /// <summary>Gets its nodes, <c>aptNode</c>.</summary>
    public IReadOnlyList<Igi2SplinePathNodeTask> Nodes => _nodes;

    /// <summary>
    /// Rebuilds it from the nodes it holds now, as each node's read does: collects them, measures it, and turns every node's
    /// frame to its tangent (<c>SplinePathDynCubeObj_UpdateOrientations</c>, <c>igi2 0x5491A0</c>).
    /// </summary>
    public void Rebuild()
    {
        _nodes.Clear();
        foreach (GameTask child in Children)
        {
            if (child is Igi2SplinePathNodeTask node && _nodes.Count < MaxNodes)
            {
                _nodes.Add(node);
            }
        }

        Measure();
        foreach (Igi2SplinePathNodeTask node in _nodes)
        {
            node.LimitOrientation();
        }
    }

    /// <summary>
    /// Where a segment passes, <c>SplinePathDynCubeObj_GetInterpolatedPosition</c> (<c>igi2 0x5487B0</c>): the cubic Hermite of its
    /// two nodes' positions and tangents.
    /// </summary>
    /// <param name="segment">The segment, from its first node.</param>
    /// <param name="t">How far along it.</param>
    /// <returns>The point.</returns>
    /// <remarks>
    /// Each part is summed as the end's position, the end's tangent, the start's position, then the start's tangent, the basis
    /// weights stored to floats but the end's position weight.
    /// </remarks>
    public Vector3d Position(int segment, float t)
    {
        Igi2SplinePathNodeTask from = _nodes[segment], to = _nodes[segment + 1];
        Fpu t1 = t;
        Fpu t2 = t1 * t1;
        Fpu t3 = t1 * t2;
        Fpu startTangent = ((t3 - (t2 + t2)) + t1).F;
        Fpu startWeight = (((t3 + t3) - (t2 * 3f)) + 1f).F;
        Fpu endTangent = (t3 - t2).F;
        Fpu endWeight = (t3 * -2f) + (t2 * 3f);
        Vector3d a = from.Position, b = to.Position, ta = from.Tangent, tb = to.Tangent;
        return new Vector3d(
            ((((endWeight * b.X) + (endTangent * tb.X)) + (startWeight * a.X)) + (startTangent * ta.X)).Value,
            ((((endWeight * b.Y) + (endTangent * tb.Y)) + (startWeight * a.Y)) + (startTangent * ta.Y)).Value,
            ((((endWeight * b.Z) + (endTangent * tb.Z)) + (startWeight * a.Z)) + (startTangent * ta.Z)).Value);
    }

    /// <summary>
    /// Which way an object on a segment faces, <c>SplinePathDynCubeObj_GetInterpolatedOrientation</c> (<c>igi2 0x5489D0</c>): its
    /// nodes' quaternions slerped and made a frame.
    /// </summary>
    /// <param name="segment">The segment, from its first node.</param>
    /// <param name="t">How far along it.</param>
    /// <returns>The frame, row by row.</returns>
    /// <remarks>
    /// Its test for a node that turns with the curve is made on the address of the node's flag, never nought, so the frame is
    /// always the slerp; the branch that would build one from the curve's tangent is never taken.
    /// </remarks>
    public float[] Orientation(int segment, float t) =>
        Igi2Pose.FromQuaternion(Igi2Pose.Slerp(_nodes[segment].Rotation, _nodes[segment + 1].Rotation, t));

    // SplinePathDynCubeObj_InitPath (igi2 0x548DA0).
    private void Measure()
    {
        int count = _nodes.Count;
        if (count < MinNodes)
        {
            return;
        }

        Igi2SplinePathNodeTask first = _nodes[0], last = _nodes[count - 1];
        if (first.IsUseComputedTangent)
        {
            first.SetTangent(Step(_nodes[1].Position, first.Position));
        }

        if (last.IsUseComputedTangent)
        {
            last.SetTangent(Step(last.Position, _nodes[count - 2].Position));
        }

        for (int i = 1; i < count - 1; i++)
        {
            Igi2SplinePathNodeTask node = _nodes[i];
            if (node.IsUseComputedTangent)
            {
                Vector3d step = Step(_nodes[i + 1].Position, _nodes[i - 1].Position);
                node.SetTangent(new Vector3d(((Fpu)step.X * 0.5).Value, ((Fpu)step.Y * 0.5).Value, ((Fpu)step.Z * 0.5).Value));
            }
        }

        last.DistanceToNext = 0f;
        first.TimeFromStart = 0f;
        for (int i = 0; i < count - 1; i++)
        {
            Igi2SplinePathNodeTask node = _nodes[i], next = _nodes[i + 1];
            node.DistanceToNext = Length(i, 0f, LengthStep, 100, node.Position, 0f);

            Fpu meanSpeed = ((Fpu)node.Speed * KilometresAnHour) + ((Fpu)next.Speed * KilometresAnHour);
            next.TimeFromStart = meanSpeed.Value > 0.0
                ? ((((Fpu)node.DistanceToNext * HalfMetre) / meanSpeed) + node.TimeFromStart).F
                : 0f;

            for (int tenth = 0; tenth < Igi2SplinePathNodeTask.Tenths; tenth++)
            {
                float start = ((Fpu)(double)tenth * 0.1f).F;
                node.SetLengthPerTenth(tenth, Length(i, start, TenthStep, 20, Position(i, start), 0f));
            }
        }
    }

    // A walk along a segment from start in so many steps, its chords summed into a float: the two points held alternately, as
    // the original keeps them in a pair it writes by the step's parity.
    private float Length(int segment, float start, float step, int steps, Vector3d from, float length)
    {
        Span<Vector3d> points = [from, from];
        float t = start;
        for (int k = 0; k < steps; k++)
        {
            t = ((Fpu)t + step).F;
            points[k & 1] = Position(segment, t);
            Fpu dx = (Fpu)points[0].X - points[1].X;
            Fpu dy = (Fpu)points[0].Y - points[1].Y;
            Fpu dz = (Fpu)points[0].Z - points[1].Z;
            length = (Fpu.Sqrt(((dx * dx) + (dy * dy)) + (dz * dz)) + length).F;
        }

        return length;
    }

    // One position less another, each part chopped and stored whole.
    private static Vector3d Step(Vector3d to, Vector3d from) =>
        new(((Fpu)to.X - from.X).Value, ((Fpu)to.Y - from.Y).Value, ((Fpu)to.Z - from.Z).Value);
}
