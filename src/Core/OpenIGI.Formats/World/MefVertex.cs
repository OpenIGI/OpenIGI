using System.Runtime.InteropServices;
using OpenIGI.Core.Numerics;

namespace OpenIGI.Formats.World;

/// <summary>One vertex of a MEF model, widened to the 40-byte layout.</summary>
/// <remarks>
/// <para>
/// The on-disk layouts come from the D3D flexible vertex format the original creates its vertex buffer
/// with (<c>dword_543CB8</c>, read in <c>0x49B4B0</c>): <c>0x112</c> = <c>XYZ|NORMAL|TEX1</c> at a 32-byte
/// stride for <see cref="MefMeshFormat.Diffuse"/>, and <c>0x212</c> = <c>XYZ|NORMAL|TEX2</c> at a 40-byte
/// stride for the other two formats.
/// </para>
/// <para>
/// A 32-byte vertex has no lightmap channel, so <see cref="LightmapU"/> and <see cref="LightmapV"/> read
/// as zero for those models.
/// </para>
/// </remarks>
[StructLayout(LayoutKind.Sequential)]
public readonly struct MefVertex
{
    /// <summary>The stride in bytes of a <see cref="MefMeshFormat.Diffuse"/> vertex on disk.</summary>
    public const int DiffuseStride = 32;

    /// <summary>The stride in bytes of an <see cref="MefMeshFormat.Skinned"/> or <see cref="MefMeshFormat.Lightmapped"/> vertex on disk.</summary>
    public const int LightmappedStride = 40;

    /// <summary>Initializes a new instance of the <see cref="MefVertex"/> struct.</summary>
    /// <param name="position">The model-space position.</param>
    /// <param name="normal">The vertex normal.</param>
    /// <param name="diffuseU">The diffuse texture U coordinate.</param>
    /// <param name="diffuseV">The diffuse texture V coordinate.</param>
    /// <param name="lightmapU">The lightmap U coordinate; zero when the model has no lightmap channel.</param>
    /// <param name="lightmapV">The lightmap V coordinate; zero when the model has no lightmap channel.</param>
    public MefVertex(Vector3f position, Vector3f normal, float diffuseU, float diffuseV, float lightmapU, float lightmapV)
    {
        Position = position;
        Normal = normal;
        DiffuseU = diffuseU;
        DiffuseV = diffuseV;
        LightmapU = lightmapU;
        LightmapV = lightmapV;
    }

    /// <summary>Gets the model-space position.</summary>
    public Vector3f Position { get; }

    /// <summary>Gets the vertex normal.</summary>
    public Vector3f Normal { get; }

    /// <summary>Gets the diffuse texture U coordinate.</summary>
    public float DiffuseU { get; }

    /// <summary>Gets the diffuse texture V coordinate.</summary>
    public float DiffuseV { get; }

    /// <summary>Gets the lightmap U coordinate, or zero when the model has no lightmap channel.</summary>
    public float LightmapU { get; }

    /// <summary>Gets the lightmap V coordinate, or zero when the model has no lightmap channel.</summary>
    public float LightmapV { get; }

    /// <summary>Returns the on-disk stride in bytes for a mesh format.</summary>
    /// <param name="format">The mesh format.</param>
    /// <returns>32 for <see cref="MefMeshFormat.Diffuse"/>, otherwise 40.</returns>
    public static int StrideOf(MefMeshFormat format) =>
        format == MefMeshFormat.Diffuse ? DiffuseStride : LightmappedStride;

    /// <inheritdoc/>
    public override string ToString() => $"{Position} n={Normal} uv=({DiffuseU}, {DiffuseV})";
}
