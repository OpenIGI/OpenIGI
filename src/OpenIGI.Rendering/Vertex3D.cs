using System.Runtime.InteropServices;
using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;

namespace OpenIGI.Rendering;

/// <summary>
/// A world-space vertex: position, normal, a diffuse texture coordinate, a lightmap coordinate and a
/// modulating colour.
/// </summary>
/// <remarks>
/// <para>
/// The first 40 bytes are deliberately the original's model vertex — the D3D flexible vertex format
/// <c>XYZ|NORMAL|TEX2</c> that the retail engine builds its vertex buffers with — so a
/// <c>.mef</c> model's vertex data maps across without rearrangement. Models that carry only one texture
/// coordinate set leave the lightmap channel at zero.
/// </para>
/// <para>
/// <see cref="Colour"/> is the one addition, and the terrain path is what needs it: the original packs
/// its exponential fog into a vertex alpha byte, and cross-fades one ground material into another by
/// weighting each material's pass per vertex. Models pass <see cref="Bgra32.White"/> and are unaffected.
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
    public const int Size = 48;

    /// <summary>The byte offset of <see cref="Colour"/> within the vertex.</summary>
    public const int ColourOffset = 40;

    /// <summary>The byte offset of <see cref="SurfaceNormal"/> within the vertex.</summary>
    public const int SurfaceNormalOffset = 44;

    /// <summary>Initializes a new instance of the <see cref="Vertex3D"/> struct.</summary>
    /// <param name="position">The position.</param>
    /// <param name="normal">The normal.</param>
    /// <param name="diffuseU">The diffuse texture U coordinate.</param>
    /// <param name="diffuseV">The diffuse texture V coordinate.</param>
    /// <param name="lightmapU">The lightmap U coordinate.</param>
    /// <param name="lightmapV">The lightmap V coordinate.</param>
    /// <param name="colour">The modulating colour; white leaves the sampled texture alone.</param>
    /// <param name="surfaceNormal">A packed shading normal for a mesh whose normal channel is spoken for.</param>
    public Vertex3D(
        Vector3f position,
        Vector3f normal,
        float diffuseU,
        float diffuseV,
        float lightmapU = 0f,
        float lightmapV = 0f,
        Bgra32 colour = default,
        Bgra32 surfaceNormal = default)
    {
        Position = position;
        Normal = normal;
        DiffuseU = diffuseU;
        DiffuseV = diffuseV;
        LightmapU = lightmapU;
        LightmapV = lightmapV;
        Colour = colour == default ? Bgra32.White : colour;
        SurfaceNormal = surfaceNormal;
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

    /// <summary>Gets the colour the sampled texture is modulated by.</summary>
    /// <remarks>
    /// A default-constructed <see cref="Vertex3D"/> has a transparent black colour, which is why the
    /// constructor promotes <see langword="default"/> to <see cref="Bgra32.White"/>: a caller that does
    /// not care about the channel should not have to name it.
    /// </remarks>
    public Bgra32 Colour { get; }

    /// <summary>Gets a surface normal carried beside <see cref="Normal"/>, or all zero when there is none.</summary>
    /// <remarks>
    /// <para>
    /// Almost every mesh puts its shading normal in <see cref="Normal"/> and leaves this alone. The terrain
    /// cannot: its normal channel carries the offset each vertex slides along as the ground morphs between
    /// levels of detail, and there is nowhere else for that to live. So the terrain's real normal lives
    /// here, and a renderer prefers this channel whenever it carries anything.
    /// </para>
    /// <para>
    /// All zero means absent rather than a normal pointing nowhere, which is what lets every other mesh —
    /// and every vertex built before this existed — go on saying nothing and be understood.
    /// </para>
    /// </remarks>
    public Bgra32 SurfaceNormal { get; }

    /// <summary>Packs a unit normal into the byte channels this vertex carries it in.</summary>
    /// <param name="normal">The normal; need not be normalised.</param>
    /// <returns>The packed normal, never all zero for a usable direction.</returns>
    public static Bgra32 PackSurfaceNormal(Vector3f normal)
    {
        float length = MathF.Sqrt((normal.X * normal.X) + (normal.Y * normal.Y) + (normal.Z * normal.Z));
        if (length < 1e-6f)
        {
            return default;
        }

        static byte Encode(float v) => (byte)Math.Clamp((int)MathF.Round((v * 0.5f + 0.5f) * 255f), 0, 255);

        // The fourth channel is the "there is one here" mark, so a normal that happens to encode to zeros
        // in the other three is still told apart from a vertex that carries none.
        return new Bgra32(
            Encode(normal.X / length),
            Encode(normal.Y / length),
            Encode(normal.Z / length),
            255);
    }

    /// <inheritdoc/>
    public override string ToString() => $"{Position} uv=({DiffuseU}, {DiffuseV})";
}
