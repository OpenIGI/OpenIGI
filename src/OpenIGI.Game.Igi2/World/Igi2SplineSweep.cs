using OpenIGI.Core.Numerics;
using OpenIGI.Formats;
using OpenIGI.Formats.World;
using OpenIGI.Game.World;
using OpenIGI.Rendering;

namespace OpenIGI.Game.Igi2.World;

/// <summary>One sampled frame of a spline segment: where the curve is, and how it is turned there.</summary>
/// <param name="Orientation">
/// The frame's rotation. Its first COLUMN is the curve's tangent; the other two span the cross-section
/// the model is carried in.
/// </param>
/// <param name="Position">The point on the curve, in engine units, relative to the segment's midpoint.</param>
public readonly record struct Igi2SplineFrame(Matrix3f Orientation, Vector3f Position);

/// <summary>
/// A model bent along the curve between two spline waypoints — the sequel's <c>SplineObjSegment</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>The segment is not a placement.</b> Nothing in the level data declares one. A
/// <c>SplineObjWaypoint</c> declares a <c>Segment Model</c>, and
/// <c>SplineObjWaypoint_UpdateInternalDataHandler</c> (<c>igi2ed 0x6417C0</c>) hands it to
/// <c>SplineObjSegment_Update</c> (<c>0x611FB0</c>) together with the PREVIOUS waypoint — so the segment
/// spans from the waypoint before it to the waypoint that owns it, and the first waypoint of a spline
/// owns none.
/// </para>
/// <para>
/// <b>How the curve is built.</b> Three cubic Hermite channels, one per axis, through the two endpoints,
/// with each end's tangent taken from the first column of that waypoint's orientation and scaled by the
/// straight-line distance between them (<c>Hermite64_Init</c>, <c>0x66FDF0</c>). The segment is then
/// sampled at <c>areas + 1</c> evenly spaced parameters. At the ends the waypoints' own orientations are
/// used verbatim; in between, the frame is <c>Real32x9_Create(alpha, beta, gamma)</c> where beta and
/// gamma come from the curve's tangent there (<c>Real32x3_GetAngles</c>, <c>0x604FA0</c>) and alpha is
/// interpolated between the two ends' own alphas, each unwrapped so the roll takes the short way round.
/// </para>
/// <para>
/// <b>How the model is bent.</b> <c>Direct3DRender_ProcessVerticesSplineRigid</c> (<c>0x568380</c>)
/// consumes the model's local X as position ALONG the curve: it is rescaled from the model's own X
/// extent onto <c>[0, areas]</c>, which picks a pair of neighbouring frames and a fraction between them.
/// The vertex is then placed by each of the two frames — its Y and Z read as offsets in that frame's
/// cross-section — and the two results are mixed by the fraction. Normals take the lower frame's
/// rotation whole. So the model tiles once along the segment and rolls with it.
/// </para>
/// <para>
/// <b>A linear segment</b> skips all of that: one frame, and a single matrix whose first column is the
/// chord itself divided by the model's X extent, so the model is simply stretched from end to end.
/// </para>
/// </remarks>
public sealed class Igi2SplineSweep : IIgi2PlacementGeometry
{
    private readonly Igi2SplineFrame[] _frames;
    private readonly float[] _areaLengths;
    private readonly Vector3f _chord;
    private readonly bool _linear;
    private readonly bool _flipped;

    private Igi2SplineSweep(Igi2SplineFrame[] frames, Vector3d midpoint, Vector3f chord, bool linear, bool flipped)
    {
        _frames = frames;
        _chord = chord;
        _linear = linear;
        _flipped = flipped;
        Midpoint = midpoint;

        _areaLengths = new float[frames.Length - 1];
        float total = 0f;
        for (int i = 0; i < _areaLengths.Length; i++)
        {
            Vector3f step = new(
                frames[i + 1].Position.X - frames[i].Position.X,
                frames[i + 1].Position.Y - frames[i].Position.Y,
                frames[i + 1].Position.Z - frames[i].Position.Z);
            _areaLengths[i] = MathF.Sqrt((step.X * step.X) + (step.Y * step.Y) + (step.Z * step.Z));
            total += _areaLengths[i];
        }

        Length = total;
    }

    /// <summary>Gets one frame's point, in engine units, in the world rather than in the segment.</summary>
    /// <param name="index">The frame.</param>
    /// <returns>The world point.</returns>
    /// <exception cref="ArgumentOutOfRangeException">There is no such frame.</exception>
    public Vector3d PointAt(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, _frames.Length);

        Igi2SplineFrame frame = _frames[index];
        return new Vector3d(
            Midpoint.X + frame.Position.X,
            Midpoint.Y + frame.Position.Y,
            Midpoint.Z + frame.Position.Z);
    }

    /// <summary>The most frames a segment is sampled at, which is the cap the editor applies.</summary>
    /// <remarks>
    /// <c>SplineObjWaypoint_UpdateInternalDataHandler</c> clamps the waypoint's <c>NumAreas</c> to its
    /// parent's <c>Number of Matrices / Segment</c> and then to twenty. Every waypoint in the shipped
    /// corpus asks for exactly twenty, so the clamp never bites, but it is what bounds the array.
    /// </remarks>
    public const int MaximumAreas = 20;

    /// <summary>Gets where the segment's object stands: the midpoint of the two waypoints, in engine units.</summary>
    /// <remarks>
    /// Every frame position is stored relative to this, because that is what the original stores and
    /// what the object's own world position then adds back.
    /// </remarks>
    public Vector3d Midpoint { get; }

    /// <summary>Gets the sampled frames, from the earlier waypoint to the later one.</summary>
    public IReadOnlyList<Igi2SplineFrame> Frames => _frames;

    /// <summary>Gets whether the model is stretched straight from end to end rather than bent.</summary>
    public bool IsLinear => _linear;

    /// <summary>Gets the straight-line length of each area, in engine units.</summary>
    /// <remarks>
    /// <c>SplineObjSegment_Update</c> (<c>igi2ed 0x611FB0</c>) measures each area as it samples it and
    /// keeps the array at <c>+1456</c>. It is what a train walks when it steps along the track, so it is
    /// the curve's length as the engine reckons it: a chain of chords, not an integral.
    /// </remarks>
    public IReadOnlyList<float> AreaLengths => _areaLengths;

    /// <summary>Gets the whole segment's length, in engine units: the sum of its areas.</summary>
    public float Length { get; }

    /// <summary>Builds the curve between two waypoints.</summary>
    /// <param name="start">The earlier waypoint's position, in engine units.</param>
    /// <param name="stop">The later waypoint's position, in engine units.</param>
    /// <param name="startOrientation">The earlier waypoint's orientation.</param>
    /// <param name="stopOrientation">The later waypoint's orientation.</param>
    /// <param name="areas">How many spans the curve is sampled in; forced to one when linear.</param>
    /// <param name="linear">Whether the segment is straight.</param>
    /// <param name="flipped">Whether the model runs backwards along the curve.</param>
    /// <returns>The sweep.</returns>
    public static Igi2SplineSweep Create(
        Vector3d start,
        Vector3d stop,
        Matrix3f startOrientation,
        Matrix3f stopOrientation,
        int areas,
        bool linear,
        bool flipped)
    {
        int count = linear ? 1 : Math.Clamp(areas, 1, MaximumAreas);
        Vector3d span = stop - start;
        double length = span.Length;
        Vector3d midpoint = new((start.X + stop.X) * 0.5, (start.Y + stop.Y) * 0.5, (start.Z + stop.Z) * 0.5);
        Vector3f chord = new((float)span.X, (float)span.Y, (float)span.Z);
        Igi2SplineFrame[] frames = new Igi2SplineFrame[count + 1];

        if (linear)
        {
            // A straight segment's frames are the chord's own turn about the up axis and nothing else --
            // Real32x9_CreateGamma of the direction's gamma. Only the second and third columns are read
            // when the model is drawn, the first being replaced by the chord itself.
            Vector3f direction = chord;
            float gamma = 0f;
            if (direction.X != 0f || direction.Y != 0f || direction.Z != 0f)
            {
                (_, gamma) = DirectionAngles(direction);
            }

            Matrix3f turn = Matrix3f.RotationZ(gamma);
            for (int i = 0; i <= count; i++)
            {
                double t = (double)i / count;
                frames[i] = new Igi2SplineFrame(
                    turn,
                    new Vector3f(
                        (float)(((stop.X - start.X) * t) + start.X - midpoint.X),
                        (float)(((stop.Y - start.Y) * t) + start.Y - midpoint.Y),
                        (float)(((stop.Z - start.Z) * t) + start.Z - midpoint.Z)));
            }

            return new Igi2SplineSweep(frames, midpoint, chord, linear: true, flipped);
        }

        // The tangent at each end is that waypoint's own first column, lengthened by the straight-line
        // distance between the two. A Hermite with those endpoints and those tangents is the curve.
        Vector3f startTangent = startOrientation.Column1;
        Vector3f stopTangent = stopOrientation.Column1;
        Hermite x = new(start.X, stop.X, startTangent.X * length, stopTangent.X * length);
        Hermite y = new(start.Y, stop.Y, startTangent.Y * length, stopTangent.Y * length);
        Hermite z = new(start.Z, stop.Z, startTangent.Z * length, stopTangent.Z * length);

        // The roll at each end, unwrapped so the segment turns the short way rather than most of the way
        // round. Real32x9_GetAngles reports every angle in [0, 2pi), which is what makes the unwrapping
        // necessary at all.
        float startRoll = RollOf(startOrientation);
        float stopRoll = RollOf(stopOrientation);
        while (stopRoll - startRoll > MathF.PI)
        {
            startRoll += MathF.Tau;
        }

        while (stopRoll - startRoll < -MathF.PI)
        {
            stopRoll += MathF.Tau;
        }

        for (int i = 0; i <= count; i++)
        {
            double t = (double)i / count;
            Vector3f position = new(
                (float)(x.At(t) - midpoint.X),
                (float)(y.At(t) - midpoint.Y),
                (float)(z.At(t) - midpoint.Z));

            Matrix3f orientation;
            if (i == 0)
            {
                orientation = startOrientation;
            }
            else if (i == count)
            {
                orientation = stopOrientation;
            }
            else
            {
                Vector3f tangent = new((float)x.Slope(t), (float)y.Slope(t), (float)z.Slope(t));
                float beta = 0f;
                float gamma = 0f;
                if (tangent.X != 0f || tangent.Y != 0f || tangent.Z != 0f)
                {
                    (beta, gamma) = DirectionAngles(tangent);
                }

                orientation = Matrix3f.FromEngineEulerAngles(
                    (float)(((stopRoll - startRoll) * t) + startRoll),
                    beta,
                    gamma);
            }

            frames[i] = new Igi2SplineFrame(orientation, position);
        }

        return new Igi2SplineSweep(frames, midpoint, chord, linear: false, flipped);
    }

    /// <summary>
    /// Gets the beta and gamma <c>Real32x3_GetAngles</c> (<c>igi2ed 0x604FA0</c>) reports for a direction.
    /// </summary>
    /// <param name="direction">The direction; need not be normalised.</param>
    /// <returns>The tilt off the horizontal and the turn about the up axis, in radians.</returns>
    /// <remarks>
    /// The zero-X case is the original's, not a shortcut: it reports a gamma of zero rather than the
    /// quarter turn <c>atan2</c> would give, and a direction straight up the Z axis therefore keeps the
    /// world's own facing instead of picking one arbitrarily.
    /// </remarks>
    public static (float Beta, float Gamma) DirectionAngles(Vector3f direction)
    {
        float gamma = direction.X == 0f ? 0f : MathF.Atan2(direction.Y, direction.X);
        float length = MathF.Sqrt(
            (direction.X * direction.X) + (direction.Y * direction.Y) + (direction.Z * direction.Z));
        float beta = length == 0f ? 0f : -MathF.Asin(direction.Z / length);
        return (beta, gamma);
    }

    /// <summary>Gets the roll <c>Real32x9_GetAngles</c> (<c>igi2ed 0x606260</c>) reports for a rotation.</summary>
    /// <param name="orientation">The rotation.</param>
    /// <returns>The alpha angle, in radians, in <c>[0, 2pi)</c>.</returns>
    /// <remarks>
    /// The original decomposes all three angles; only this one is read back when a segment interpolates
    /// its roll, so only this one is computed. The gimbal branch — the first column lying along the up
    /// axis, where the roll and the turn are the same rotation — is the original's second case.
    /// </remarks>
    public static float RollOf(Matrix3f orientation)
    {
        float horizontal = MathF.Sqrt((orientation.M21 * orientation.M21) + (orientation.M11 * orientation.M11));
        float roll = horizontal <= 0.0001f
            ? MathF.Atan2(-orientation.M23, orientation.M22)
            : MathF.Atan2(orientation.M32, orientation.M33);
        return roll < 0f ? roll + MathF.Tau : roll;
    }

    /// <inheritdoc/>
    public MeshGeometry? Build(MefModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        MeshGeometry? source = MefMeshBuilder.Build(model, (float)(1.0 / WorldScene.UnitsPerMetre));
        if (source is null)
        {
            return null;
        }

        if (!TryExtent(model, out float low, out float high))
        {
            return null;
        }

        Vertex3D[] vertices = new Vertex3D[source.Vertices.Length];
        if (_linear)
        {
            BuildStraight(source, vertices, low, high);
        }
        else
        {
            BuildBent(source, vertices, low, high);
        }

        return new MeshGeometry(vertices, [.. source.Indices], source.Subsets);
    }

    /// <inheritdoc/>
    public override string ToString() =>
        $"I.G.I. 2 spline segment, {_frames.Length - 1} area(s){(_linear ? ", straight" : string.Empty)}";

    /// <summary>The model's extent along the axis the curve consumes, in metres.</summary>
    /// <remarks>
    /// Read from the COLLISION mesh, which is what <c>SplineObjSegment_Update</c> measures — it walks the
    /// collision vertex list for the lowest and highest X and refuses a model whose collision mesh is the
    /// wrong kind outright. A model with no collision mesh at all falls back to the drawn one, because a
    /// segment with no extent has nothing to map its vertices onto.
    /// </remarks>
    private static bool TryExtent(MefModel model, out float low, out float high)
    {
        low = float.MaxValue;
        high = float.MinValue;

        if (MefCollisionMesh.TryRead(model.Container, out MefCollisionMesh? collision, AssetFormatProfile.Igi2.Collision)
            && collision is not null
            && collision.Vertices.Count > 0)
        {
            foreach (Vector3f vertex in collision.Vertices)
            {
                low = MathF.Min(low, vertex.X);
                high = MathF.Max(high, vertex.X);
            }
        }
        else
        {
            foreach (MefVertex vertex in model.Vertices)
            {
                low = MathF.Min(low, vertex.Position.X);
                high = MathF.Max(high, vertex.Position.X);
            }
        }

        low = (float)(low / WorldScene.UnitsPerMetre);
        high = (float)(high / WorldScene.UnitsPerMetre);
        return high > low;
    }

    /// <summary>Bends the model along the frames.</summary>
    private void BuildBent(MeshGeometry source, Vertex3D[] vertices, float low, float high)
    {
        int areas = _frames.Length - 1;
        float scale = 1f / (high - low);
        float origin = _flipped ? -high : -low;
        float step = _flipped ? -(areas * scale) : areas * scale;
        float metres = (float)(1.0 / WorldScene.UnitsPerMetre);

        for (int i = 0; i < vertices.Length; i++)
        {
            Vertex3D vertex = source.Vertices[i];

            // Where along the curve this vertex sits, as a whole area and a fraction of the next. The
            // last area keeps its closing frame rather than running off the end of the array, which is
            // the original's `if (index == areas) --index`.
            float along = (origin + vertex.Position.X) * step;
            int area = Math.Clamp((int)along, 0, areas - 1);
            float fraction = Math.Clamp(along - area, 0f, 1f);

            Igi2SplineFrame near = _frames[area];
            Igi2SplineFrame far = _frames[area + 1];
            Vector3f section = new(0f, vertex.Position.Y, vertex.Position.Z);
            Vector3f a = Add(RowsDot(near.Orientation, section), near.Position, metres);
            Vector3f b = Add(RowsDot(far.Orientation, section), far.Position, metres);

            vertices[i] = new Vertex3D(
                new Vector3f(
                    ((b.X - a.X) * fraction) + a.X,
                    ((b.Y - a.Y) * fraction) + a.Y,
                    ((b.Z - a.Z) * fraction) + a.Z),
                RowsDot(near.Orientation, vertex.Normal),
                vertex.DiffuseU,
                vertex.DiffuseV,
                vertex.LightmapU,
                vertex.LightmapV,
                vertex.Colour);
        }
    }

    /// <summary>Stretches the model straight from end to end.</summary>
    private void BuildStraight(MeshGeometry source, Vertex3D[] vertices, float low, float high)
    {
        // One matrix, whose first column is the chord over the model's own extent and whose other two are
        // the frame's second and third ROWS -- which is the transpose the original applies before it
        // draws. The model's X is measured from the middle of its extent rather than from its low end.
        // The chord in METRES, because the extent it is divided by is in metres and so is every vertex
        // the result is applied to. The two came from engine units and had to meet somewhere.
        Matrix3f frame = _frames[0].Orientation;
        float metres = (float)(1.0 / WorldScene.UnitsPerMetre);
        float scale = (_flipped ? -1f : 1f) / (high - low);
        Vector3f along = new(
            _chord.X * metres * scale,
            _chord.Y * metres * scale,
            _chord.Z * metres * scale);
        if (_chord.X == 0f && _chord.Y == 0f && _chord.Z == 0f)
        {
            along = new Vector3f(scale, 0f, 0f);
        }

        Vector3f across = frame.Row2;
        Vector3f up = frame.Row3;
        float middle = (low + high) * 0.5f;

        for (int i = 0; i < vertices.Length; i++)
        {
            Vertex3D vertex = source.Vertices[i];
            float x = vertex.Position.X - middle;
            vertices[i] = new Vertex3D(
                new Vector3f(
                    (along.X * x) + (across.X * vertex.Position.Y) + (up.X * vertex.Position.Z),
                    (along.Y * x) + (across.Y * vertex.Position.Y) + (up.Y * vertex.Position.Z),
                    (along.Z * x) + (across.Z * vertex.Position.Y) + (up.Z * vertex.Position.Z)),
                vertex.Normal,
                vertex.DiffuseU,
                vertex.DiffuseV,
                vertex.LightmapU,
                vertex.LightmapV,
                vertex.Colour);
        }
    }

    /// <summary>Applies a rotation the engine's way for this path: the matrix's ROWS dotted with a vector.</summary>
    /// <remarks>
    /// The spline path is one of the two conventions the engine uses. <c>Matrix3f.Transform</c> is
    /// the other, <c>v * M</c>, which is why this is written out rather than called.
    /// </remarks>
    private static Vector3f RowsDot(Matrix3f m, Vector3f v) => new(
        (m.M11 * v.X) + (m.M12 * v.Y) + (m.M13 * v.Z),
        (m.M21 * v.X) + (m.M22 * v.Y) + (m.M23 * v.Z),
        (m.M31 * v.X) + (m.M32 * v.Y) + (m.M33 * v.Z));

    private static Vector3f Add(Vector3f rotated, Vector3f unitsOffset, float metres) => new(
        rotated.X + (unitsOffset.X * metres),
        rotated.Y + (unitsOffset.Y * metres),
        rotated.Z + (unitsOffset.Z * metres));

    /// <summary>One axis of the cubic the segment's curve is, as <c>Hermite64_Init</c> stores it.</summary>
    /// <remarks>
    /// <c>Hermite64_Init</c> (<c>igi2ed 0x66FDF0</c>) keeps the four coefficients rather than the two
    /// endpoints and two tangents, and <c>Hermite64_Evaluate</c> (<c>0x66FE40</c>) and
    /// <c>Hermite64_EvaluateTangent</c> (<c>0x66FE70</c>) read them straight back as a cubic and its
    /// derivative. Kept in double, as the original does, because the endpoints are world positions in
    /// engine units and a float loses metres at the far side of a level.
    /// </remarks>
    private readonly struct Hermite(double start, double stop, double startSlope, double stopSlope)
    {
        private readonly double _cubic = (start + start) - (stop + stop) + startSlope + stopSlope;
        private readonly double _square = (stop * 3.0) - (start * 3.0) - (startSlope + startSlope) - stopSlope;
        private readonly double _slope = startSlope;
        private readonly double _origin = start;

        public double At(double t) => (t * t * t * _cubic) + (t * t * _square) + (t * _slope) + _origin;

        public double Slope(double t) => (3.0 * t * t * _cubic) + (2.0 * t * _square) + _slope;
    }
}
