using OpenIGI.Core;

namespace OpenIGI.Formats.World;

/// <summary>The chunk tags used by <c>.mef</c> models, spelled as they appear on disk.</summary>
/// <remarks>
/// The original compares against the little-endian dword it reads, so a hexdump shows these spellings
/// while the MSVC multi-character literal in the source is the reverse (<c>HSEM</c> on disk is
/// <c>'MESH'</c> in code). <see cref="FourCc.Reversed"/> moves between the two.
/// </remarks>
public static class MefTags
{
    /// <summary>Chunk <c>HSEM</c> (<c>MESH</c>): the mesh header.</summary>
    public static FourCc Mesh { get; } = FourCc.FromString("HSEM");

    /// <summary>Chunk <c>SEMS</c> (<c>SMES</c>): the header of a shadow-volume mesh.</summary>
    /// <remarks>
    /// <para>
    /// A shadow volume is a model in its own right rather than a broken one: it carries
    /// <c>SMES</c>/<c>SVTX</c>/<c>SFAC</c>/<c>EDGE</c> and no <see cref="Mesh"/> at all, because it is
    /// silhouette geometry to be extruded rather than a surface to be shaded.
    /// </para>
    /// <para>
    /// <b>The original loads it through a different function entirely.</b>
    /// <c>MTP_ShadowVolumeLoad</c> (<c>igi2ed 0x41FA00</c>) opens <c>LOCAL:models/&lt;name&gt;.mef</c>
    /// and asks for exactly these four chunks — the header at 28 bytes a row (its count is the chunk
    /// size over 28), faces at 28, vertices at 12 and edges at 8 — while never touching the mesh
    /// reader. So a shadow volume sitting in a models archive is not something the model reader should
    /// be asked about at all.
    /// </para>
    /// <para>
    /// Sixty-eight of them sit in the sequel's common pack — <c>shadow_1</c>, <c>truckshade</c>,
    /// <c>Ftire_shade</c> and their like — and reading each as a render model reported every one as
    /// malformed, once per file, every load.
    /// </para>
    /// </remarks>
    public static FourCc ShadowMesh { get; } = FourCc.FromString("SEMS");

    /// <summary>Chunk <c>XTVM</c> (<c>MVTX</c>): legacy mesh vertices, empty in most retail models.</summary>
    public static FourCc LegacyVertices { get; } = FourCc.FromString("XTVM");

    /// <summary>Chunk <c>ATTA</c> (<c>ATTA</c>): attachment and bone mount points.</summary>
    public static FourCc Attachments { get; } = FourCc.FromString("ATTA");

    /// <summary>Chunk <c>HIER</c>: an I.G.I. 2 bone mesh's bone hierarchy.</summary>
    public static FourCc BoneHierarchy => Animation.Igi2BoneHierarchy.ChunkTag;

    /// <summary>Chunk <c>BNAM</c>: an I.G.I. 2 bone mesh's bone names, sixteen bytes each.</summary>
    public static FourCc BoneNames { get; } = FourCc.FromString("MANB");

    /// <summary>Chunk <c>TROP</c> (<c>PORT</c>): portals.</summary>
    public static FourCc Portals { get; } = FourCc.FromString("TROP");

    /// <summary>Chunk <c>CFTP</c> (<c>PTFC</c>): portal faces.</summary>
    public static FourCc PortalFaces { get; } = FourCc.FromString("CFTP");

    /// <summary>Chunk <c>XVTP</c> (<c>PTVX</c>): portal vertices.</summary>
    public static FourCc PortalVertices { get; } = FourCc.FromString("XVTP");

    /// <summary>Chunk <c>TXAN</c> (<c>NAXT</c>): optional, repeatable, 20-byte records of unknown purpose.</summary>
    public static FourCc TextureAnimation { get; } = FourCc.FromString("TXAN");

    /// <summary>Chunk <c>D3DR</c> (<c>RD3D</c>): the render header — counts plus sub-chunk pointer slots.</summary>
    public static FourCc RenderHeader { get; } = FourCc.FromString("D3DR");

    /// <summary>Chunk <c>XTRV</c> (<c>VRTX</c>): vertex data.</summary>
    public static FourCc Vertices { get; } = FourCc.FromString("XTRV");

    /// <summary>Chunk <c>DNER</c> (<c>REND</c>): draw records and 16-bit indices.</summary>
    public static FourCc DrawRecords { get; } = FourCc.FromString("DNER");

    /// <summary>Chunk <c>ECAF</c> (<c>FACE</c>): the triangle-list indices, as <c>u16</c> triples.</summary>
    /// <remarks>
    /// I.G.I. 2 only. I.G.I. 1 packs each draw record's indices immediately after its header inside
    /// <see cref="DrawRecords"/>; the sequel keeps one flat array here and has each face group name a
    /// start and a count into it.
    /// </remarks>
    public static FourCc FaceIndices { get; } = FourCc.FromString("ECAF");

    /// <summary>Chunk <c>PMTL</c> (<c>LTMP</c>): one size entry per baked object-lightmap page.</summary>
    public static FourCc LightmapPages { get; } = FourCc.FromString("PMTL");

    /// <summary>Chunk <c>HSMC</c> (<c>CMSH</c>): the collision mesh header.</summary>
    public static FourCc CollisionMesh { get; } = FourCc.FromString("HSMC");

    /// <summary>Chunk <c>XTVC</c> (<c>CVTX</c>): collision vertices.</summary>
    public static FourCc CollisionVertices { get; } = FourCc.FromString("XTVC");

    /// <summary>Chunk <c>ECFC</c> (<c>CFCE</c>): collision faces.</summary>
    public static FourCc CollisionFaces { get; } = FourCc.FromString("ECFC");

    /// <summary>Chunk <c>TAMC</c> (<c>CMAT</c>): collision materials.</summary>
    public static FourCc CollisionMaterials { get; } = FourCc.FromString("TAMC");

    /// <summary>Chunk <c>HPSC</c> (<c>CSPH</c>): collision spheres.</summary>
    public static FourCc CollisionSpheres { get; } = FourCc.FromString("HPSC");
}
