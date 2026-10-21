namespace OpenIGI.Rendering.Pipeline.RayTracing;

/// <summary>One run of a mesh's indices drawn with one material, as the traceable world records it.</summary>
/// <remarks>
/// <para>
/// A mesh is not a material. Measured over mission one, 164 of 197 meshes are drawn as more than one
/// index range and 143 with more than one texture, so "this mesh is a cut-out" is not a question that
/// has an answer — only "this run of its triangles is". That is why the traceable world is built one
/// geometry per range rather than one per mesh: a hit reports which geometry it landed on, and the
/// material is looked up from that.
/// </para>
/// <para>
/// It also fixes something the per-mesh form got wrong in the other direction. A mesh with both solid
/// and cut-out ranges was admitted whole the moment any solid range of it was drawn, which built its
/// cut-out ranges into the structure as solid geometry — so the exclusion meant to keep wire and glass
/// from casting a wall's shadow only worked on meshes that were nothing but wire or glass.
/// </para>
/// </remarks>
/// <param name="IndexOffset">Where in the mesh's index buffer the run starts.</param>
/// <param name="IndexCount">How many indices the run covers.</param>
/// <param name="Opaque">
/// Whether traversal may take a hit on this run without asking. False for a run whose texture is a
/// cut-out, which is what makes the ray shader test the alpha before accepting the hit.
/// </param>
/// <param name="MaskLayer">
/// Which layer of <see cref="OpacityMasks"/> holds this run's cut-out shape, or <c>-1</c> when it
/// is solid and no mask is needed.
/// </param>
/// <param name="AlbedoLayer">
/// Which layer of <see cref="TracedAlbedo"/> holds this run's colour, or <c>-1</c> when it has
/// none. Read where a reflection struck something the camera cannot see and the surface cache never
/// baked — which is every body in the level.
/// </param>
/// <param name="Dynamic">
/// Whether this run belongs to geometry that MOVES — a soldier, the player, anything skinned. The
/// surface cache is a bake of the static level, so no card can describe it, and a reflection must not
/// let one try: a card's box is a slab, so a body standing near a wall sits inside that wall's box and
/// would be resolved to the wall's baked lighting.
/// </param>
/// <param name="Emissive">
/// How strongly this run glows, as a multiple of its albedo -- the strength the G-buffer gives it; nought
/// for a surface that does not. A reflection that stops on a lamp shows it lit rather than as the dark
/// fitting the surface cache photographed round it.
/// </param>
internal readonly record struct RayTracedSurface(
    int IndexOffset,
    int IndexCount,
    bool Opaque,
    int MaskLayer,
    int AlbedoLayer = -1,
    bool Dynamic = false,
    float Emissive = 0f)
{
    /// <summary>Gets the record's flag word: the dynamic bit, and the glow in sixteenths in bits 8-15.</summary>
    public uint FlagWord =>
        (Dynamic ? 1u : 0u)
        | ((Emissive > 0f ? (uint)Math.Clamp((int)MathF.Round(Emissive * 16f), 1, 255) : 0u) << 8);
}
