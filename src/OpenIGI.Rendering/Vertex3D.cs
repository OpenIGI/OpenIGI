using System.Runtime.InteropServices;
using OpenIGI.Core.Numerics;

namespace OpenIGI.Rendering;

/// <summary>
/// A world-space vertex: position, normal, a diffuse texture coordinate and a lightmap coordinate.
/// </summary>
/// <remarks>
/// <para>
/// The layout is deliberately the original's 40-byte model vertex — the D3D flexible vertex format
/// <c>XYZ|NORMAL|TEX2</c> that the retail engine builds its vertex buffers with — so a
/// <c>.mef</c> model's vertex data maps across without rearrangement. Models that carry only one texture
/// coordinate set leave the lightmap channel at zero.
/// </para>
/// <para>
/// Positions are in whatever space the caller's world matrix expects. World positions themselves are
/// <see cref="Vector3d"/> elsewhere in the engine; the conversion to single precision happens when a
/// mesh is built, relative to its own origin, so that distant geometry does not lose precision.
/// </para>
/// </remarks>
[StructLayout(LayoutKind.Sequential)]
public readonly struct Vertex3D
{
    /// <summary>The size of one vertex in bytes.</summary>
    public const int Size = 40;

    /// <summary>Initializes a new instance of the <see cref="Vertex3D"/> struct.</summary>
    /// <param name="position">The position.</param>
    /// <param name="normal">The normal.</param>
    /// <param name="diffuseU">The diffuse texture U coordinate.</param>
    /// <param name="diffuseV">The diffuse texture V coordinate.</param>
    /// <param name="lightmapU">The lightmap U coordinate.</param>
    /// <param name="lightmapV">The lightmap V coordinate.</param>
    public Vertex3D(
        Vector3f position,
        Vector3f normal,
        float diffuseU,
        float diffuseV,
        float lightmapU = 0f,
        float lightmapV = 0f)
    {
        Position = position;
        Normal = normal;
        DiffuseU = diffuseU;
        DiffuseV = diffuseV;
        LightmapU = lightmapU;
        LightmapV = lightmapV;
    }

    /// <summary>Gets the position.</summary>
    public Vector3f Position { get; }

    /// <summary>Gets the normal.</summary>
    public Vector3f Normal { get; }

    /// <summary>Gets the diffuse texture U coordinate.</summary>
    public float DiffuseU { get; }

    /// <summary>Gets the diffuse texture V coordinate.</summary>
    public float DiffuseV { get; }

    /// <summary>Gets the lightmap U coordinate.</summary>
    public float LightmapU { get; }

    /// <summary>Gets the lightmap V coordinate.</summary>
    public float LightmapV { get; }

    /// <inheritdoc/>
    public override string ToString() => $"{Position} uv=({DiffuseU}, {DiffuseV})";
}
