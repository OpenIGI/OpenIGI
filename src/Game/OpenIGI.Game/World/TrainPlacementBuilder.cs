using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;
using OpenIGI.Formats.World;

namespace OpenIGI.Game.World;

/// <summary>
/// The scene walk's rail and road builder: it couples each <c>Train</c> car onto its consist at the
/// railroad's initial position, measures the car and wheel models the consist solver reads, and lays
/// every <c>SplineObj</c> chain as bent segment geometry.
/// </summary>
/// <remarks>
/// One instance serves one <see cref="WorldScene.Build"/> walk, because a child <c>Train</c> couples
/// behind the cars its lead has already placed: the builder remembers each car it placed so the next
/// one in the consist can find it.
/// </remarks>
internal sealed class TrainPlacementBuilder
{
    private readonly Dictionary<string, (MtpInstance Instance, LoadedPack Pack, MefModel Model)> _byName;
    private readonly Dictionary<GameTask, TrainPlacementState> _trainPlacements = [];

    /// <summary>Creates a builder over the walk's bound pack instances.</summary>
    /// <param name="byName">The pack instances by model name, which supply each car's geometry.</param>
    internal TrainPlacementBuilder(Dictionary<string, (MtpInstance Instance, LoadedPack Pack, MefModel Model)> byName)
    {
        _byName = byName;
    }

    /// <summary>Couples a <c>Train</c> task's car onto its consist and places it at the railroad's start.</summary>
    /// <param name="declared">The <c>Train</c> task.</param>
    /// <param name="placedNames">The model names the task places, in walk order.</param>
    /// <returns>The car's placement, or <see langword="null"/> when it has no railroad or cannot be placed.</returns>
    internal TrainPlacementState? Place(DeclaredTask declared, string[] placedNames)
    {
        (float ownLength, float ownPivot) = TrainGeometry(placedNames);

        // A child Train couples behind the cars already on its lead's consist; a lead starts one.
        RailroadConsist? consist =
            declared.Parent is DeclaredTask parent
            && string.Equals(parent.Type.Name, "Train", StringComparison.Ordinal)
            && _trainPlacements.TryGetValue(parent, out TrainPlacementState? parentTrain)
                ? parentTrain.Consist
                : RailroadPath.FromTrain(declared) is { } railroad ? new RailroadConsist(railroad) : null;
        if (consist is not null)
        {
            int car = consist.Add(declared, ownLength, ownPivot);
            if (consist.TryPlace(consist.Railroad.InitialPosition, car, out Vector3d carPosition, out Matrix3f carOrientation))
            {
                TrainPlacementState trainPlacement = new(consist, carPosition, carOrientation);
                _trainPlacements[declared] = trainPlacement;
                return trainPlacement;
            }
        }

        return null;
    }

    private (float Length, float Pivot) TrainGeometry(IEnumerable<string> names)
    {
        foreach (string name in names)
        {
            if (!_byName.TryGetValue(name, out var bound))
            {
                continue;
            }

            return (TrainCouplingDistance(bound.Model), TrainCouplingPivotFraction(bound.Model));
        }
        return default;
    }

    /// <inheritdoc cref="WorldScene.TrainCouplingDistance(MefModel)"/>
    internal static float TrainCouplingDistance(MefModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        return model.TryGetMagicVertex(0, out Vector3f front)
            && model.TryGetMagicVertex(1, out Vector3f rear)
                ? MathF.Abs(front.X - rear.X)
                : 0f;
    }

    /// <inheritdoc cref="WorldScene.WheelRadius(MefModel)"/>
    internal static float WheelRadius(MefModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        ReadOnlySpan<MefVertex> vertices = model.Vertices;
        if (vertices.Length == 0)
        {
            return 0f;
        }

        Vector3f least = vertices[0].Position;
        Vector3f most = least;
        for (int i = 1; i < vertices.Length; i++)
        {
            Vector3f position = vertices[i].Position;
            least = new Vector3f(
                MathF.Min(least.X, position.X),
                MathF.Min(least.Y, position.Y),
                MathF.Min(least.Z, position.Z));
            most = new Vector3f(
                MathF.Max(most.X, position.X),
                MathF.Max(most.Y, position.Y),
                MathF.Max(most.Z, position.Z));
        }

        float widest = MathF.Max(most.X - least.X, MathF.Max(most.Y - least.Y, most.Z - least.Z));
        return widest * 0.5f;
    }

    /// <inheritdoc cref="WorldScene.TrainCouplingPivotFraction(MefModel)"/>
    internal static float TrainCouplingPivotFraction(MefModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        if (!model.TryGetMagicVertex(0, out Vector3f front)
            || !model.TryGetMagicVertex(1, out Vector3f rear))
        {
            return 0f;
        }

        float length = MathF.Abs(front.X - rear.X);
        return length > float.Epsilon ? MathF.Abs(front.X) / length : 0f;
    }

    /// <summary>Lays every <c>SplineObj</c> chain: one bent segment per consecutive waypoint pair.</summary>
    /// <remarks>
    /// <para>
    /// A segment spans from the <em>previous</em> waypoint to the waypoint that names its model, which
    /// is <c>sub_500EF0</c>'s arrangement — a chain of N waypoints lays N−1 segments and the first
    /// waypoint's own <c>Segment Model</c> goes unused. The curve is the one <c>sub_4E4530</c> builds:
    /// a Hermite cubic between the two waypoint positions whose tangents are the waypoints' local X
    /// axes scaled by the chord length, evaluated here per vertex rather than at the original's
    /// <c>NumAreas</c> sampled matrices — the same curve, continuously.
    /// </para>
    /// <para>
    /// A vertex's local X picks its place along the curve (the original measures the model's X extent
    /// the same way, <c>sub_4E4530</c>'s tail); its local Y and Z ride the curve's zero-roll frame.
    /// <b>Divergence:</b> the original also interpolates a twist angle between the endpoint
    /// orientations; every level-1 waypoint is authored yaw-only, so the twist is dropped until a
    /// level needs it.
    /// </para>
    /// </remarks>
    internal static int AddSplineSegments(
        LoadedMission mission,
        GameTask levelRoot,
        Dictionary<string, (MtpInstance Instance, LoadedPack Pack, MefModel Model)> byName,
        List<WorldPlacement> placements,
        IOpenIgiLogger log)
    {
        int added = 0;
        int chain = 0;
        foreach (GameTask task in levelRoot.Descendants())
        {
            if (task is not DeclaredTask spline
                || !string.Equals(spline.Type.Name, WorldScene.SplineObjType, StringComparison.Ordinal))
            {
                continue;
            }

            chain++;
            List<(DeclaredTask Task, Vector3d Position)> waypoints = [];
            foreach (GameTask child in spline.Children)
            {
                if (child is DeclaredTask waypoint
                    && string.Equals(waypoint.Type.Name, WorldScene.SplineWaypointType, StringComparison.Ordinal)
                    && WorldScene.TryGetPosition(waypoint, out Vector3d position))
                {
                    waypoints.Add((waypoint, position));
                }
            }

            for (int i = 1; i < waypoints.Count; i++)
            {
                (DeclaredTask waypoint, Vector3d to) = waypoints[i];
                (DeclaredTask previous, Vector3d from) = waypoints[i - 1];

                if (!waypoint.TryGetValue(WorldScene.SegmentModelParameter, out object? raw)
                    || raw is not string name
                    || name.Length == 0
                    || !byName.TryGetValue(name, out (MtpInstance Instance, LoadedPack Pack, MefModel Model) bound))
                {
                    continue;
                }

                MeshGeometry? bent = BendSegment(bound.Model, from, to, Tangent(previous), Tangent(waypoint));
                if (bent is null)
                {
                    continue;
                }

                placements.Add(new WorldPlacement(
                    from,
                    Matrix3f.Identity,
                    bound.Instance,
                    bound.Pack,
                    bound.Model,
                    $"spline:{chain}:{i}",
                    waypoint,
                    string.Empty,
                    -1)
                {
                    BentGeometry = bent,
                });
                WorldSceneDiagnostics.LogDerivedPlacement(
                    log,
                    mission,
                    waypoint,
                    name,
                    from,
                    Matrix3f.Identity,
                    "SPLINE");
                added++;
            }
        }

        return added;

        // The tangent direction is the waypoint orientation's local X axis in world space — the
        // matrix's first column, exactly the elements [0], [3], [6] that sub_4E4530 reads.
        // An automatically oriented spline (0x500EF0) runs on the frame rebuilt from its neighbours instead.
        static Vector3f Tangent(DeclaredTask waypoint)
        {
            if (RetailWorldObjects.IsAutomaticallyOriented(waypoint))
            {
                float[] automatic = RetailWorldObjects.WaypointOrientation(waypoint);
                return new Vector3f(automatic[0], automatic[3], automatic[6]);
            }

            waypoint.TryGetValue(WorldScene.OrientationParameter, out object? raw);
            Matrix3f m = raw is Vector3f angles ? WorldScene.ToOrientation(angles) : Matrix3f.Identity;
            return new Vector3f(m.M11, m.M21, m.M31);
        }
    }

    /// <summary>Bends one segment model along its span's Hermite curve.</summary>
    /// <param name="model">The segment model, authored along its local X.</param>
    /// <param name="from">The span's start, in metres — also the placement's position.</param>
    /// <param name="to">The span's end, in metres.</param>
    /// <param name="fromTangent">The start waypoint's unit tangent.</param>
    /// <param name="toTangent">The end waypoint's unit tangent.</param>
    /// <returns>The geometry, positioned relative to <paramref name="from"/>, or <see langword="null"/>.</returns>
    private static MeshGeometry? BendSegment(
        MefModel model,
        Vector3d from,
        Vector3d to,
        Vector3f fromTangent,
        Vector3f toTangent)
    {
        if (model.IsEmpty || model.DrawRecords.Count == 0)
        {
            return null;
        }

        ReadOnlySpan<MefVertex> source = model.Vertices;
        float minX = float.MaxValue;
        float maxX = float.MinValue;
        foreach (MefVertex vertex in source)
        {
            minX = Math.Min(minX, vertex.Position.X);
            maxX = Math.Max(maxX, vertex.Position.X);
        }

        float length = maxX - minX;
        if (length <= 0)
        {
            return null;
        }

        // The Hermite endpoints relative to the span's start, with tangents scaled by the chord — the
        // v79 factor sub_4E4530 multiplies both endpoint axes by.
        double dx = to.X - from.X;
        double dy = to.Y - from.Y;
        double dz = to.Z - from.Z;
        double chord = Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz));
        if (chord < 1e-3)
        {
            return null;
        }

        Span<double> t0 = [fromTangent.X * chord, fromTangent.Y * chord, fromTangent.Z * chord];
        Span<double> t1 = [toTangent.X * chord, toTangent.Y * chord, toTangent.Z * chord];
        Span<double> p1 = [dx, dy, dz];

        Vertex3D[] vertices = new Vertex3D[source.Length];
        for (int i = 0; i < source.Length; i++)
        {
            MefVertex v = source[i];
            double t = Math.Clamp((v.Position.X - minX) / length, 0.0, 1.0);

            // Hermite basis and its derivative, once per vertex.
            double t2 = t * t;
            double t3 = t2 * t;
            double h00 = (2 * t3) - (3 * t2) + 1;
            double h10 = t3 - (2 * t2) + t;
            double h01 = (-2 * t3) + (3 * t2);
            double h11 = t3 - t2;
            double d00 = (6 * t2) - (6 * t);
            double d10 = (3 * t2) - (4 * t) + 1;
            double d01 = (6 * t) - (6 * t2);
            double d11 = (3 * t2) - (2 * t);

            double px = (h10 * t0[0]) + (h01 * p1[0]) + (h11 * t1[0]);
            double py = (h10 * t0[1]) + (h01 * p1[1]) + (h11 * t1[1]);
            double pz = (h10 * t0[2]) + (h01 * p1[2]) + (h11 * t1[2]);
            _ = h00; // the start point is the origin, so its term vanishes

            double gx = (d00 * 0) + (d10 * t0[0]) + (d01 * p1[0]) + (d11 * t1[0]);
            double gy = (d10 * t0[1]) + (d01 * p1[1]) + (d11 * t1[1]);
            double gz = (d10 * t0[2]) + (d01 * p1[2]) + (d11 * t1[2]);

            // The curve's zero-roll frame: X down the tangent, Y level, Z perpendicular up — what
            // sub_4E4530 builds through sub_4B38E0 from the direction angles with no twist.
            double glen = Math.Sqrt((gx * gx) + (gy * gy) + (gz * gz));
            if (glen < 1e-9)
            {
                gx = dx / chord;
                gy = dy / chord;
                gz = dz / chord;
                glen = 1.0;
            }

            double fx = gx / glen;
            double fy = gy / glen;
            double fz = gz / glen;

            double sx = -fy;
            double sy = fx;
            double slen = Math.Sqrt((sx * sx) + (sy * sy));
            if (slen < 1e-9)
            {
                sx = 0.0;
                sy = 1.0;
                slen = 1.0;
            }

            sx /= slen;
            sy /= slen;

            double ux = -(fz * sy);
            double uy = fz * sx;
            double uz = (fx * sy) - (fy * sx);

            double offY = v.Position.Y / WorldScene.UnitsPerMetre;
            double offZ = v.Position.Z / WorldScene.UnitsPerMetre;

            vertices[i] = new Vertex3D(
                new Vector3f(
                    (float)(px + (sx * offY) + (ux * offZ)),
                    (float)(py + (sy * offY) + (uy * offZ)),
                    (float)(pz + (uz * offZ))),
                new Vector3f(
                    (float)((fx * v.Normal.X) + (sx * v.Normal.Y) + (ux * v.Normal.Z)),
                    (float)((fy * v.Normal.X) + (sy * v.Normal.Y) + (uy * v.Normal.Z)),
                    (float)((fz * v.Normal.X) + (uz * v.Normal.Z))),
                v.DiffuseU,
                v.DiffuseV,
                v.LightmapU,
                v.LightmapV);
        }

        int total = 0;
        foreach (MefDrawRecord record in model.DrawRecords)
        {
            total += record.Indices.Length;
        }

        ushort[] indices = new ushort[total];
        List<MeshSubset> subsets = new(model.DrawRecords.Count);
        int cursor = 0;
        foreach (MefDrawRecord record in model.DrawRecords)
        {
            ReadOnlySpan<ushort> run = record.Indices.Span;
            int start = cursor;
            foreach (ushort index in run)
            {
                indices[cursor++] = (ushort)record.ToVertexIndex(index);
            }

            subsets.Add(new MeshSubset(start, run.Length, record.MaterialSlot, record.LightmapPage));
        }

        // The same repair the ordinary mesh builder makes, and for the same reason: a model's flattened
        // draw order does not wind uniformly, and the world pass culls back faces. Without it a bent
        // segment keeps whichever facing it was authored with and half of it is removed -- which is what
        // hid the railway at the one preset that culls, while every other preset drew it.
        MefMeshBuilder.RepairWinding(vertices, indices);

        return new MeshGeometry(vertices, indices, [.. subsets]);
    }
}

/// <summary>A <c>Train</c> car's place on its consist, as <see cref="TrainPlacementBuilder"/> laid it.</summary>
/// <param name="Consist">The consist the car is coupled into.</param>
/// <param name="Position">The car's position, in engine units.</param>
/// <param name="Orientation">The car's orientation on the railroad.</param>
internal sealed record TrainPlacementState(
    RailroadConsist Consist,
    Vector3d Position,
    Matrix3f Orientation);
