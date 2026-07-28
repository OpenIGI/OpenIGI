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

    /// <summary>Chunk <c>XTVM</c> (<c>MVTX</c>): legacy mesh vertices, empty in most retail models.</summary>
    public static FourCc LegacyVertices { get; } = FourCc.FromString("XTVM");

    /// <summary>Chunk <c>ATTA</c> (<c>ATTA</c>): attachment and bone mount points.</summary>
    public static FourCc Attachments { get; } = FourCc.FromString("ATTA");

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
