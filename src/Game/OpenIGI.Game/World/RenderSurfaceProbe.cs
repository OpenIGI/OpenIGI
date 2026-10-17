using OpenIGI.Core.Numerics;
using OpenIGI.Formats.World;

namespace OpenIGI.Game.World;

/// <summary>
/// Names the texture on the drawn surface a ray meets.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why the collision trace cannot answer this.</b> A model is two meshes, not one: the collision mesh
/// the world trace walks, and the render mesh the G-buffer draws. They are different geometry with
/// different triangle counts, and nothing indexes one to the other — so a contact from
/// <see cref="ObjectCollisionIndex.TryIdentify(Vector3d, Vector3d, out string, out WorldRayHit)"/> knows the object and the game material row, and cannot
/// know which texture is on the face that was struck. The renderer's material — roughness, metallic,
/// whether the surface reflects — is keyed by TEXTURE NAME in <c>materials.json</c>, so naming a surface
/// there means knowing that name, and there was no way to get it out of a running game.
/// </para>
/// <para>
/// So this traces the render mesh itself, over the same ray, and reports the texture of the nearest draw
/// record it crosses. One placement per probe, on a keypress, so a linear walk of the model's triangles
/// is the right shape: the object has already been chosen by a broad-phase the collision index ran.
/// </para>
/// <para>
/// <b>Triangles are carried to world space rather than the ray to model space.</b> The inverse of a
/// placement's orientation is its transpose, and this codebase has two <see cref="Matrix3f"/>
/// conventions that have already cost a bug apiece; the forward transform is the one
/// <c>LightmapBaker</c> and <see cref="ObjectCollisionIndex"/> both build their world triangles with, so
/// using it here means this agrees with them by construction rather than by argument.
/// </para>
/// </remarks>
public static class RenderSurfaceProbe
{
    /// <summary>Finds the texture on the nearest drawn triangle a ray crosses.</summary>
    /// <param name="placement">The placed object to trace.</param>
    /// <param name="originUnits">Where the ray starts, in world units.</param>
    /// <param name="directionUnits">Which way it goes, in world units; need not be normalised.</param>
    /// <param name="texture">Receives the texture's bare name — the key <c>materials.json</c> uses.</param>
    /// <param name="distanceUnits">Receives how far along the ray the surface is, in world units.</param>
    /// <returns><see langword="true"/> when a drawn triangle was crossed and its texture named.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="placement"/> is <see langword="null"/>.</exception>
    public static bool TryFindTexture(
        WorldPlacement placement,
        Vector3d originUnits,
        Vector3d directionUnits,
        out string texture,
        out double distanceUnits)
    {
        ArgumentNullException.ThrowIfNull(placement);

        texture = string.Empty;
        distanceUnits = 0.0;

        MefModel model = placement.Model;
        ReadOnlySpan<MefVertex> vertices = model.Vertices;

        if (vertices.IsEmpty)
        {
            return false;
        }

        Vector3d offset = new(
            placement.Position.X * WorldScene.UnitsPerMetre,
            placement.Position.Y * WorldScene.UnitsPerMetre,
            placement.Position.Z * WorldScene.UnitsPerMetre);

        double nearest = double.MaxValue;
        int nearestSlot = MefDrawRecord.NoMaterialSlot;

        foreach (MefDrawRecord record in model.DrawRecords)
        {
            if (!record.HasMaterial)
            {
                continue;
            }

            ReadOnlySpan<ushort> indices = record.Indices.Span;

            for (int i = 0; i + 2 < indices.Length; i += 3)
            {
                if (!TryCorner(vertices, record, indices[i], placement, offset, out Vector3d a)
                    || !TryCorner(vertices, record, indices[i + 1], placement, offset, out Vector3d b)
                    || !TryCorner(vertices, record, indices[i + 2], placement, offset, out Vector3d c))
                {
                    continue;
                }

                if (TryHit(originUnits, directionUnits, a, b, c, out double t) && t < nearest)
                {
                    nearest = t;
                    nearestSlot = record.MaterialSlot;
                }
            }
        }

        if (nearestSlot == MefDrawRecord.NoMaterialSlot
            || !placement.Pack.TryGetTextureName(placement.Instance, nearestSlot, out string name))
        {
            return false;
        }

        texture = name;
        distanceUnits = nearest;
        return true;
    }

    private static bool TryCorner(
        ReadOnlySpan<MefVertex> vertices,
        in MefDrawRecord record,
        ushort index,
        WorldPlacement placement,
        Vector3d offset,
        out Vector3d corner)
    {
        int vertex = record.ToVertexIndex(index);

        if ((uint)vertex >= (uint)vertices.Length)
        {
            corner = default;
            return false;
        }

        Vector3f rotated = placement.Orientation.Transform(vertices[vertex].Position);
        corner = new Vector3d(rotated.X + offset.X, rotated.Y + offset.Y, rotated.Z + offset.Z);
        return true;
    }

    // Moller-Trumbore, double-sided: a probe must name the surface it is looking at whichever way the
    // triangle happens to wind, and a cut-out pane is drawn from both.
    private static bool TryHit(
        Vector3d origin,
        Vector3d direction,
        Vector3d a,
        Vector3d b,
        Vector3d c,
        out double distance)
    {
        const double Epsilon = 1e-9;
        distance = 0.0;

        Vector3d e1 = new(b.X - a.X, b.Y - a.Y, b.Z - a.Z);
        Vector3d e2 = new(c.X - a.X, c.Y - a.Y, c.Z - a.Z);

        Vector3d p = Vector3d.Cross(direction, e2);
        double determinant = Vector3d.Dot(e1, p);

        if (Math.Abs(determinant) < Epsilon)
        {
            return false;
        }

        double inverse = 1.0 / determinant;
        Vector3d s = new(origin.X - a.X, origin.Y - a.Y, origin.Z - a.Z);
        double u = Vector3d.Dot(s, p) * inverse;

        if (u is < 0.0 or > 1.0)
        {
            return false;
        }

        Vector3d q = Vector3d.Cross(s, e1);
        double v = Vector3d.Dot(direction, q) * inverse;

        if (v < 0.0 || u + v > 1.0)
        {
            return false;
        }

        double t = Vector3d.Dot(e2, q) * inverse;

        if (t <= Epsilon || t > 1.0)
        {
            return false;
        }

        // The direction carries the ray's whole length, so t is a fraction of it.
        distance = t * Math.Sqrt(Vector3d.Dot(direction, direction));
        return true;
    }

}
