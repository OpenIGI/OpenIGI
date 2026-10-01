namespace OpenIGI.Rendering;

/// <summary>
/// An optional device capability: drawing signed-distance-field text with the distance-field shader.
/// </summary>
/// <remarks>
/// <para>
/// Kept off the main <see cref="IGraphicsDevice"/> because it is a specialised path, not something every
/// backend must offer and not something the 2D pipeline needs in general. A caller checks
/// <c>device is ISdfTextDevice</c>; when the capability is absent — as it is for the recording device
/// used in tests — the sprite batch falls back to drawing the atlas as ordinary coverage, which the
/// coverage atlas is built to render correctly.
/// </para>
/// <para>
/// The geometry is the same two buffers a textured draw uses; the difference is the program, which reads
/// the atlas as a distance field and thresholds it into a crisp, optionally outlined and glowing, edge.
/// </para>
/// </remarks>
public interface ISdfTextDevice
{
    /// <summary>Draws indexed screen-space triangles sampling a distance-field atlas.</summary>
    /// <param name="vertices">The vertex buffer; texture coordinates address the SDF atlas.</param>
    /// <param name="indices">Indices into <paramref name="vertices"/>, three per triangle.</param>
    /// <param name="atlas">The signed-distance-field atlas texture.</param>
    /// <param name="parameters">How to shade the run — spread, outline and glow.</param>
    /// <param name="blend">How fragments combine with the render target.</param>
    /// <exception cref="ArgumentException"><paramref name="indices"/> is not a multiple of three, or an index is out of range.</exception>
    void DrawText2D(
        ReadOnlySpan<Vertex2D> vertices,
        ReadOnlySpan<ushort> indices,
        TextureHandle atlas,
        in SdfTextParams parameters,
        BlendMode blend);
}
