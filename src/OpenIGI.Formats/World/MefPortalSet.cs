using System.Buffers.Binary;
using System.Collections.ObjectModel;
using OpenIGI.Core;
using OpenIGI.Core.Numerics;
using OpenIGI.Formats.Ilff;

namespace OpenIGI.Formats.World;

/// <summary>One opening in a sector's shell, as the mesh stores it.</summary>
/// <param name="FirstVertex">The first of this portal's vertices in the model's portal-vertex list.</param>
/// <param name="VertexCount">How many vertices it has.</param>
/// <param name="FirstFace">The first of its triangles in the model's portal-face list.</param>
/// <param name="FaceCount">How many triangles it has.</param>
/// <param name="Slot">
/// The portal's slot in its own model, which is what an object's open/closed state is indexed by, and
/// what pairs two sectors together — see <see cref="MefPortalSet"/>.
/// </param>
/// <remarks>
/// <c>Mesh3D_Portal_t</c>, 20 bytes, five <c>int</c>s in this order.
/// </remarks>
public readonly record struct MefPortal(int FirstVertex, int VertexCount, int FirstFace, int FaceCount, int Slot)
{
    /// <summary>The size in bytes of one portal record.</summary>
    public const int Size = 20;
}

/// <summary>One triangle of a portal's outline, as three indices into the portal-vertex list.</summary>
/// <param name="Vertex0">The first corner.</param>
/// <param name="Vertex1">The second corner.</param>
/// <param name="Vertex2">The third corner.</param>
/// <remarks>
/// <c>Mesh3D_PortalFace_t</c>, 12 bytes. The indices are into the model's whole portal-vertex list, not
/// into the portal's own slice — <c>Sector_IsPortalVisible</c> subtracts
/// <see cref="MefPortal.FirstVertex"/> from each before indexing its scratch array, which only makes
/// sense if they are absolute.
/// </remarks>
public readonly record struct MefPortalFace(int Vertex0, int Vertex1, int Vertex2)
{
    /// <summary>The size in bytes of one portal face.</summary>
    public const int Size = 12;
}

/// <summary>
/// A model's portals: the openings through which one sector can see into another.
/// </summary>
/// <remarks>
/// <para>
/// Three chunks, whose layouts the sequel's symbols name in full — <c>Mesh3D_Portal_t</c> (20 bytes),
/// <c>Mesh3D_PortalFace_t</c> (12) and <c>Mesh3D_PortalVertex_t</c> (12, a bare
/// <c>real32x3_t</c>). The same chunks exist in Project I.G.I., which is why this lives in the shared
/// format layer rather than in either title.
/// </para>
/// <para>
/// <b>The first face's winding is the portal's facing.</b> <c>Sector_IsPortalVisible</c> takes the cross
/// product of that one triangle's edges as the plane normal and flips it toward the camera; the
/// remaining faces only widen the outline.
/// </para>
/// <para>
/// <b>A portal is not necessarily a quad, and not necessarily a triangle fan.</b> Across I.G.I. 2's 794
/// placed portals the commonest shape is the quad (602 of them, four vertices and two triangles) but they
/// run to 28 vertices and 22 triangles, and <b>41 have a face count that is not
/// <c>vertices - 2</c></b> — <c>v28 f14</c> is seven separate quads, not one fan. So a reader that
/// triangulated the vertex list itself, or assumed four corners, would be wrong on a quarter of them.
/// Both counts are taken from the record.
/// </para>
/// <para>
/// <see cref="MefPortal.Slot"/> is the portal's index within its own model, and it does two jobs. It
/// indexes the per-object array of open/closed flags a door drives (<c>PortalStatus_s</c>, one byte),
/// and it is what <c>SectorObject_TraverseSector</c> compares across objects: stepping through a portal
/// admits every sector in the same sector container that has a portal with <b>the same slot number</b>.
/// That is a coarser rule than a true portal graph — it pairs by authored slot rather than by geometry —
/// but it is the rule the original follows.
/// </para>
/// </remarks>
public sealed class MefPortalSet
{
    private MefPortalSet(
        IReadOnlyList<MefPortal> portals,
        IReadOnlyList<MefPortalFace> faces,
        IReadOnlyList<Vector3f> vertices)
    {
        Portals = portals;
        Faces = faces;
        Vertices = vertices;
    }

    /// <summary>An empty set, for the models that enclose no space.</summary>
    public static MefPortalSet Empty { get; } =
        new(ReadOnlyCollection<MefPortal>.Empty, ReadOnlyCollection<MefPortalFace>.Empty, ReadOnlyCollection<Vector3f>.Empty);

    /// <summary>Gets the portals, in file order.</summary>
    public IReadOnlyList<MefPortal> Portals { get; }

    /// <summary>Gets every portal triangle in the model, indexed by <see cref="MefPortal.FirstFace"/>.</summary>
    public IReadOnlyList<MefPortalFace> Faces { get; }

    /// <summary>Gets every portal vertex in the model, in the model's own space.</summary>
    public IReadOnlyList<Vector3f> Vertices { get; }

    /// <summary>Gets a value indicating whether the model encloses space.</summary>
    public bool IsSector => Portals.Count > 0;

    /// <summary>Reads the three portal chunks of a model.</summary>
    /// <param name="container">The model's container.</param>
    /// <returns>The portals, or <see cref="Empty"/> when the model has none.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="container"/> is <see langword="null"/>.</exception>
    /// <exception cref="PackFormatException">A chunk's length is not a whole number of records, or an index is out of range.</exception>
    public static MefPortalSet Read(IlffContainer container)
    {
        ArgumentNullException.ThrowIfNull(container);

        ReadOnlySpan<byte> portalBytes = Payload(container, MefTags.Portals);
        if (portalBytes.Length == 0)
        {
            return Empty;
        }

        ReadOnlySpan<byte> faceBytes = Payload(container, MefTags.PortalFaces);
        ReadOnlySpan<byte> vertexBytes = Payload(container, MefTags.PortalVertices);

        MefPortal[] portals = new MefPortal[Count(portalBytes, MefPortal.Size, MefTags.Portals)];
        MefPortalFace[] faces = new MefPortalFace[Count(faceBytes, MefPortalFace.Size, MefTags.PortalFaces)];
        Vector3f[] vertices = new Vector3f[Count(vertexBytes, 12, MefTags.PortalVertices)];

        for (int i = 0; i < vertices.Length; i++)
        {
            ReadOnlySpan<byte> entry = vertexBytes.Slice(i * 12, 12);
            vertices[i] = new Vector3f(
                BinaryPrimitives.ReadSingleLittleEndian(entry),
                BinaryPrimitives.ReadSingleLittleEndian(entry[4..]),
                BinaryPrimitives.ReadSingleLittleEndian(entry[8..]));
        }

        for (int i = 0; i < faces.Length; i++)
        {
            ReadOnlySpan<byte> entry = faceBytes.Slice(i * MefPortalFace.Size, MefPortalFace.Size);
            faces[i] = new MefPortalFace(
                BinaryPrimitives.ReadInt32LittleEndian(entry),
                BinaryPrimitives.ReadInt32LittleEndian(entry[4..]),
                BinaryPrimitives.ReadInt32LittleEndian(entry[8..]));

            if (Outside(faces[i].Vertex0, vertices.Length)
                || Outside(faces[i].Vertex1, vertices.Length)
                || Outside(faces[i].Vertex2, vertices.Length))
            {
                throw new PackFormatException(
                    $"Portal face {i} names a vertex outside the {vertices.Length} the model holds.");
            }
        }

        for (int i = 0; i < portals.Length; i++)
        {
            ReadOnlySpan<byte> entry = portalBytes.Slice(i * MefPortal.Size, MefPortal.Size);
            MefPortal portal = new(
                BinaryPrimitives.ReadInt32LittleEndian(entry),
                BinaryPrimitives.ReadInt32LittleEndian(entry[4..]),
                BinaryPrimitives.ReadInt32LittleEndian(entry[8..]),
                BinaryPrimitives.ReadInt32LittleEndian(entry[12..]),
                BinaryPrimitives.ReadInt32LittleEndian(entry[16..]));

            if (Spans(portal.FirstVertex, portal.VertexCount, vertices.Length))
            {
                throw new PackFormatException(
                    $"Portal {i} claims vertices {portal.FirstVertex}..{portal.FirstVertex + portal.VertexCount - 1} of {vertices.Length}.");
            }

            if (Spans(portal.FirstFace, portal.FaceCount, faces.Length))
            {
                throw new PackFormatException(
                    $"Portal {i} claims faces {portal.FirstFace}..{portal.FirstFace + portal.FaceCount - 1} of {faces.Length}.");
            }

            portals[i] = portal;
        }

        return new MefPortalSet(
            new ReadOnlyCollection<MefPortal>(portals),
            new ReadOnlyCollection<MefPortalFace>(faces),
            new ReadOnlyCollection<Vector3f>(vertices));
    }

    /// <summary>Gets the outward normal of a portal, in the model's own space.</summary>
    /// <param name="portal">The portal.</param>
    /// <returns>The unnormalised normal, or zero when the portal has no faces or is degenerate.</returns>
    /// <remarks>
    /// Taken from the <em>first</em> face alone, which is what <c>Sector_IsPortalVisible</c> does. The
    /// sign is whatever the winding gives; the original flips it toward the camera at use, so a caller
    /// that needs a facing has to do the same rather than trust this one.
    /// </remarks>
    public Vector3f NormalOf(MefPortal portal)
    {
        if (portal.FaceCount <= 0)
        {
            return default;
        }

        MefPortalFace face = Faces[portal.FirstFace];
        Vector3f origin = Vertices[face.Vertex1];
        Vector3f a = Vertices[face.Vertex0] - origin;
        Vector3f b = Vertices[face.Vertex2] - origin;

        return new Vector3f(
            (a.Y * b.Z) - (a.Z * b.Y),
            (a.Z * b.X) - (a.X * b.Z),
            (a.X * b.Y) - (a.Y * b.X));
    }

    /// <inheritdoc/>
    public override string ToString() =>
        $"{Portals.Count} portals, {Faces.Count} faces, {Vertices.Count} vertices";

    private static bool Outside(int index, int count) => index < 0 || index >= count;

    private static bool Spans(int first, int count, int total) =>
        first < 0 || count < 0 || (long)first + count > total;

    private static ReadOnlySpan<byte> Payload(IlffContainer container, FourCc tag) =>
        container.TryFindChunk(tag, out IlffChunk chunk) ? chunk.Payload.Span : default;

    private static int Count(ReadOnlySpan<byte> payload, int stride, FourCc tag)
    {
        if (payload.Length % stride != 0)
        {
            throw new PackFormatException(
                $"The '{tag}' chunk is {payload.Length} bytes, which is not a whole number of {stride}-byte records.");
        }

        return payload.Length / stride;
    }
}
