using OpenIGI.Core.Numerics;
using OpenIGI.Formats.World;

namespace OpenIGI.Game.World;

/// <summary>The box a model's vertices fill, in its own frame: what a fence offers itself along and is touched within.</summary>
public static class MefModelBounds
{
    /// <summary>The least and greatest of a model's vertices on each axis.</summary>
    /// <param name="model">The model.</param>
    /// <returns>The box, and whether the model has any vertex to make one of.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="model"/> is <see langword="null"/>.</exception>
    public static (Vector3d Minimum, Vector3d Maximum, bool HasBounds) Of(MefModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        ReadOnlySpan<MefVertex> vertices = model.Vertices;
        if (vertices.IsEmpty)
        {
            return default;
        }

        Vector3f first = vertices[0].Position;
        Vector3d minimum = new(first.X, first.Y, first.Z);
        Vector3d maximum = minimum;
        for (int i = 1; i < vertices.Length; i++)
        {
            Vector3f point = vertices[i].Position;
            minimum = new Vector3d(
                Math.Min(minimum.X, point.X),
                Math.Min(minimum.Y, point.Y),
                Math.Min(minimum.Z, point.Z));
            maximum = new Vector3d(
                Math.Max(maximum.X, point.X),
                Math.Max(maximum.Y, point.Y),
                Math.Max(maximum.Z, point.Z));
        }

        return (minimum, maximum, true);
    }
}
