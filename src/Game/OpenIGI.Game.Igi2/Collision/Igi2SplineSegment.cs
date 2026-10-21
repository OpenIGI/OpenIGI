using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi2.World;

namespace OpenIGI.Game.Igi2.Collision;

/// <summary>
/// A straight spline segment, <c>SplineObjSegment</c> with its linear flag set, as the collision sees it: the
/// segment model stretched from one waypoint to the next.
/// </summary>
/// <remarks>
/// <para>
/// <c>SplineObjSegment_Update</c> (<c>igi2 0x5BAB10</c>) places the segment at the midpoint of its two waypoints and
/// builds one matrix that takes the model's own frame to the world's: its first column is the chord between the
/// waypoints over the model's X extent, its other two the chord's turn about the vertical. The model's X is measured
/// from the middle of that extent. The handler traces the model's own collision mesh, unbent, by taking each line
/// into that frame with the matrix's inverse.
/// </para>
/// <para>
/// Every value is formed as the client's 24-bit truncating FPU forms it — the midpoint too, so a waypoint far from
/// the origin puts the segment a quarter unit or so off the true middle.
/// </para>
/// </remarks>
public sealed partial class Igi2SplineSegment : IIgi2Collider
{
    /// <summary>How many level-of-detail models a name may chain to, <c>MTP_CreateLODVirModel</c> (<c>igi2 0x4F4D20</c>).</summary>
    public const int MaximumLevelsOfDetail = 5;

    private readonly IReadOnlyList<Igi2CollisionMesh?> _meshes;

    private Igi2SplineSegment(
        object owner,
        IReadOnlyList<Igi2CollisionMesh?> meshes,
        Vector3d position,
        float[] orientation,
        float[] toMesh,
        float middle,
        float radius,
        bool isFlipped)
    {
        Owner = owner;
        _meshes = meshes;
        Position = position;
        Orientation = orientation;
        ToMesh = toMesh;
        Middle = middle;
        Radius = radius;
        IsFlipped = isFlipped;
    }

    /// <inheritdoc/>
    public object Owner { get; }

    /// <summary>Gets the segment's position, <c>tPos</c>: the midpoint of its waypoints, at 24 bits.</summary>
    public Vector3d Position { get; }

    /// <summary>
    /// Gets the matrix from the model's frame to the world's, <c>tOrientation</c> (<c>+0x7C</c>): nine floats, row by
    /// row, its first column the chord over the model's extent.
    /// </summary>
    public IReadOnlyList<float> Orientation { get; }

    /// <summary>Gets the matrix from the world's frame to the model's, the inverse kept at <c>+0x578</c>.</summary>
    public IReadOnlyList<float> ToMesh { get; }

    /// <summary>Gets the middle of the model's X extent, which the stretch is measured from.</summary>
    public float Middle { get; }

    /// <summary>Gets the radius the bounds tests use, <c>+0x14C</c>.</summary>
    public float Radius { get; }

    /// <summary>Gets a value indicating whether the model runs backwards along the segment, the waypoint's <c>Flip</c>.</summary>
    public bool IsFlipped { get; }

    /// <summary>Gets the collision group spline segments are in, <c>0x10000000</c>.</summary>
    public uint Group { get; } = Igi2CollisionWorld.SplineGroup;

    /// <summary>Gets or sets a value indicating whether the model draws as a building.</summary>
    public bool IsBuildingMesh { get; init; }

    /// <summary>Gets or sets a value indicating whether the model has baked lightmaps (<c>+0x134</c> set and non-empty).</summary>
    public bool IsLightmapped { get; init; }

    /// <summary>Gets one collision mesh level.</summary>
    /// <param name="level">The level, <c>iMesh</c>.</param>
    /// <returns>The level, or <see langword="null"/> when the model has none there.</returns>
    public Igi2CollisionMesh? Mesh(int level) => (uint)level < (uint)_meshes.Count ? _meshes[level] : null;

    /// <summary>Builds a straight segment between two waypoints, as <c>SplineObjSegment_Update</c> (<c>igi2 0x5BAB10</c>) does.</summary>
    /// <param name="owner">What a hit reports as struck.</param>
    /// <param name="start">The earlier waypoint's position.</param>
    /// <param name="stop">The later waypoint's, the one that owns the segment.</param>
    /// <param name="isFlipped">Whether the model runs backwards, the owning waypoint's <c>Flip</c>.</param>
    /// <param name="meshes">The segment model's collision mesh levels, by <c>iMesh</c>.</param>
    /// <param name="modelRadius">
    /// The largest radius of the model's levels of detail, <c>Mesh3D_Mesh_t.vRadius</c>; see
    /// <see cref="LevelsOfDetail"/>.
    /// </param>
    /// <returns>The segment, or <see langword="null"/> when the waypoints coincide or the model has no first mesh level.</returns>
    /// <remarks>
    /// <para>
    /// The model's X extent is the lowest and highest X among the first mesh level's vertices, both starting from zero.
    /// Coincident waypoints leave the stretch matrix and its inverse as they were before the update, which a new
    /// segment does not define; such a segment is not built.
    /// </para>
    /// <para>
    /// The radius is the largest level-of-detail radius times the longest row of the stretch matrix, times 1.1.
    /// </para>
    /// </remarks>
    public static Igi2SplineSegment? Straight(
        object owner,
        Vector3d start,
        Vector3d stop,
        bool isFlipped,
        IReadOnlyList<Igi2CollisionMesh?> meshes,
        float modelRadius)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(meshes);
        if (meshes.Count == 0 || meshes[0] is not { } first || start == stop)
        {
            return null;
        }

        Vector3d position = new(
            (((Fpu)start.X + stop.X) * 0.5).Value,
            (((Fpu)stop.Y + start.Y) * 0.5).Value,
            (((Fpu)stop.Z + start.Z) * 0.5).Value);

        // The one area's frame: the chord's turn about the vertical, stored straight off fsin and fcos.
        Vector3f chord = new(((Fpu)stop.X - start.X).F, ((Fpu)stop.Y - start.Y).F, ((Fpu)stop.Z - start.Z).F);
        bool none = chord.X == 0f && chord.Y == 0f && chord.Z == 0f;
        float gamma = none ? 0f : Igi2Pose.DirectionGamma(chord);
        Fpu sin = Fpu.Sin(gamma);
        float cos = Fpu.Cos(gamma).F, minusSin = (-sin).F;
        if (none)
        {
            chord = new Vector3f(1f, 0f, 0f);
        }

        float low = 0f, high = 0f;
        float[] vertices = first.Arrays.Vertices;
        for (int i = 0; i < vertices.Length; i += 3)
        {
            float x = vertices[i];
            if (x < low)
            {
                low = x;
            }

            if (x > high)
            {
                high = x;
            }
        }

        // The frame's transpose with its first row replaced by the chord over the extent, transposed back: the
        // chord in the first column, the frame's own second and third columns beside it.
        Fpu extent = (Fpu)1f / ((Fpu)high - low);
        float ax = (extent * chord.X).F, ay = (extent * chord.Y).F, az = (extent * chord.Z).F;
        if (isFlipped)
        {
            ax = -ax;
            ay = -ay;
            az = -az;
        }

        float[] orientation = [ax, minusSin, 0f, ay, cos, 0f, az, 0f, 1f];
        float longest = Row(orientation, 0).F;
        float second = Row(orientation, 3).F, third = Row(orientation, 6).F;
        if (longest < second)
        {
            longest = second;
        }

        if (longest < third)
        {
            longest = third;
        }

        float[] transpose =
        [
            orientation[0], orientation[3], orientation[6],
            orientation[1], orientation[4], orientation[7],
            orientation[2], orientation[5], orientation[8],
        ];

        return new Igi2SplineSegment(
            owner,
            meshes,
            position,
            orientation,
            Igi2Pose.InverseTranspose(transpose),
            (((Fpu)high + low) * 0.5f).F,
            ((Fpu)modelRadius * longest * 1.1f).F,
            isFlipped);

        static Fpu Row(float[] m, int at) =>
            Fpu.Sqrt(((Fpu)m[at] * m[at]) + ((Fpu)m[at + 1] * m[at + 1]) + ((Fpu)m[at + 2] * m[at + 2]));
    }

    /// <summary>
    /// The names a segment model's levels of detail go by: the name itself, then its last character advanced one at a
    /// time, as long as the name resolves: <c>MTP_CreateLODVirModel</c> (<c>igi2 0x4F4D20</c>).
    /// </summary>
    /// <param name="model">The segment model's name.</param>
    /// <param name="exists">Whether a name resolves to a virtual model.</param>
    /// <returns>The names, at most <see cref="MaximumLevelsOfDetail"/>.</returns>
    public static IEnumerable<string> LevelsOfDetail(string model, Func<string, bool> exists)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(exists);
        if (model.Length == 0)
        {
            yield break;
        }

        char[] name = model.ToCharArray();
        for (int level = 0; level < MaximumLevelsOfDetail && exists(new string(name)); level++)
        {
            yield return new string(name);
            name[^1]++;
        }
    }

    /// <inheritdoc/>
    /// <remarks>
    /// <c>SplineObjSegment_TraceLinesHandler</c> answers both the line and the ground events, so this is
    /// <see cref="Igi2SplineSegmentTrace"/>.
    /// </remarks>
    public void NextZ(Igi2LineTrace trace) => Igi2SplineSegmentTrace.Trace(this, trace);
}
