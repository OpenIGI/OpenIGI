using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;
using OpenIGI.Formats.World;
using OpenIGI.Rendering;

namespace OpenIGI.Game.World;

/// <summary>The dynamic model transform a retail <c>Wire</c> builds from its two authored endpoints.</summary>
/// <remarks>
/// <c>Wire_UpdateInternalDataHandler</c> places the model at the endpoints' midpoint, points its local
/// Y axis down the span and scales that axis by <c>length / WORLD_METER</c>. Traversal then uses the
/// transformed magic vertices 0 and 1, not the untransformed task parameters. Keeping this in one value
/// makes the visible cable and the player's mount line share the exact same transform.
/// </remarks>
public readonly record struct WireTransform(
    Vector3d PositionMetres,
    Matrix3f Orientation,
    float ScaleY,
    Vector3d CableStart,
    Vector3d CableEnd)
{
    /// <summary>Gets the midpoint the model is drawn at, in engine units.</summary>
    /// <remarks>
    /// A world walk reads and compares in engine units and converts once, at the point it hands a
    /// placement to the renderer. This is the same midpoint <see cref="PositionMetres"/> reports.
    /// </remarks>
    public Vector3d PositionUnits => new(
        PositionMetres.X * WorldScene.UnitsPerMetre,
        PositionMetres.Y * WorldScene.UnitsPerMetre,
        PositionMetres.Z * WorldScene.UnitsPerMetre);

    /// <summary>Builds the runtime transform from a wire task alone.</summary>
    /// <remarks>
    /// The model is needed only for <see cref="CableStart"/> and <see cref="CableEnd"/>, which is the
    /// line the player traverses. A caller that only wants to DRAW the cable does not have one to hand
    /// at the point it reads the task, and does not need one; the endpoints then report the authored
    /// positions, which is what the magic vertices sit at either end of anyway.
    /// </remarks>
    public static bool TryCreate(DeclaredTask task, out WireTransform transform)
    {
        ArgumentNullException.ThrowIfNull(task);
        return Build(task, null, out transform);
    }

    /// <summary>Builds the runtime transform from a wire task and its model.</summary>
    public static bool TryCreate(DeclaredTask task, MefModel model, out WireTransform transform)
    {
        ArgumentNullException.ThrowIfNull(task);
        ArgumentNullException.ThrowIfNull(model);
        return Build(task, model, out transform);
    }

    /// <summary>Builds the runtime transform from a wire's two endpoints and, when at hand, its model.</summary>
    /// <param name="start">The start position, in engine units.</param>
    /// <param name="stop">The stop position, in engine units.</param>
    /// <param name="model">The wire's model, or <see langword="null"/>.</param>
    /// <param name="transform">The transform.</param>
    /// <returns><see langword="false"/> for two endpoints at one point.</returns>
    public static bool TryCreate(Vector3d start, Vector3d stop, MefModel? model, out WireTransform transform) =>
        Build(start, stop, model, out transform);

    private static bool Build(DeclaredTask task, MefModel? model, out WireTransform transform)
    {
        if (!TryPosition(task, "Start position", out Vector3d start)
            || !TryPosition(task, "Stop position", out Vector3d stop))
        {
            transform = default;
            return false;
        }

        return Build(start, stop, model, out transform);
    }

    private static bool Build(Vector3d start, Vector3d stop, MefModel? model, out WireTransform transform)
    {

        Vector3d span = stop - start;
        double length = span.Length;
        if (length <= 0.0)
        {
            transform = default;
            return false;
        }

        Vector3d direction = span / length;
        Vector3d up = CableUp(direction);
        Vector3d side = Vector3d.Cross(direction, up).Normalized();
        Matrix3f orientation = new(
            (float)side.X, (float)direction.X, (float)up.X,
            (float)side.Y, (float)direction.Y, (float)up.Y,
            (float)side.Z, (float)direction.Z, (float)up.Z);
        float scaleY = (float)(length / WorldScene.UnitsPerMetre);
        Vector3d midpoint = Vector3d.Lerp(start, stop, 0.5);

        Vector3d cableStart = start;
        Vector3d cableEnd = stop;
        if (model is not null
            && model.TryGetMagicVertex(0, out Vector3f localStart)
            && model.TryGetMagicVertex(1, out Vector3f localEnd))
        {
            cableStart = TransformMagic(midpoint, orientation, scaleY, localStart);
            cableEnd = TransformMagic(midpoint, orientation, scaleY, localEnd);
        }

        transform = new WireTransform(
            new Vector3d(
                midpoint.X / WorldScene.UnitsPerMetre,
                midpoint.Y / WorldScene.UnitsPerMetre,
                midpoint.Z / WorldScene.UnitsPerMetre),
            orientation,
            scaleY,
            cableStart,
            cableEnd);
        return true;
    }

    /// <summary>Builds the wire's model geometry with its retail local-Y stretch applied.</summary>
    public MeshGeometry? Stretch(MefModel model) => Stretch(model, ScaleY);

    /// <summary>Builds a model's geometry with a local-Y stretch applied.</summary>
    /// <param name="model">The model to stretch.</param>
    /// <param name="scaleY">The factor its local Y axis is scaled by.</param>
    /// <returns>The stretched geometry, or <see langword="null"/> when the model has none.</returns>
    public static MeshGeometry? Stretch(MefModel model, float scaleY)
    {
        ArgumentNullException.ThrowIfNull(model);
        MeshGeometry? source = MefMeshBuilder.Build(model, (float)(1.0 / WorldScene.UnitsPerMetre));
        if (source is null)
        {
            return null;
        }

        Vertex3D[] vertices = new Vertex3D[source.Vertices.Length];
        for (int i = 0; i < vertices.Length; i++)
        {
            Vertex3D vertex = source.Vertices[i];
            Vector3f normal = Normalize(new Vector3f(
                vertex.Normal.X,
                scaleY != 0f ? vertex.Normal.Y / scaleY : vertex.Normal.Y,
                vertex.Normal.Z));
            vertices[i] = new Vertex3D(
                new Vector3f(vertex.Position.X, vertex.Position.Y * scaleY, vertex.Position.Z),
                normal,
                vertex.DiffuseU,
                vertex.DiffuseV,
                vertex.LightmapU,
                vertex.LightmapV,
                vertex.Colour);
        }

        return new MeshGeometry(vertices, [.. source.Indices], source.Subsets);
    }

    /// <summary>Gets column 2 of the retail cable frame: cable-relative up.</summary>
    public static Vector3d CableUp(Vector3d direction)
    {
        if (Math.Abs(direction.Z) <= 0.94999999)
        {
            double horizontal = Math.Sqrt((direction.X * direction.X) + (direction.Y * direction.Y));
            return new Vector3d(
                -direction.Z * direction.X / horizontal,
                -direction.Z * direction.Y / horizontal,
                horizontal);
        }

        return new Vector3d(0.0, -direction.Z, direction.Y).Normalized();
    }

    private static Vector3d TransformMagic(
        Vector3d midpoint,
        Matrix3f orientation,
        float scaleY,
        Vector3f local)
    {
        Vector3f scaled = new(local.X, local.Y * scaleY, local.Z);
        Vector3f worldOffset = orientation.Transposed().Transform(scaled);
        return midpoint + new Vector3d(worldOffset.X, worldOffset.Y, worldOffset.Z);
    }

    private static bool TryPosition(DeclaredTask task, string name, out Vector3d position)
    {
        if (task.TryGetValue(name, out object? raw) && raw is Vector3d value)
        {
            position = value;
            return true;
        }

        position = default;
        return false;
    }

    private static Vector3f Normalize(Vector3f value)
    {
        float length = MathF.Sqrt((value.X * value.X) + (value.Y * value.Y) + (value.Z * value.Z));
        return length > 0f
            ? new Vector3f(value.X / length, value.Y / length, value.Z / length)
            : value;
    }
}
