namespace OpenIGI.Rendering;

/// <summary>The colour a run of draws fogs towards: the scene's own, half of it, or white.</summary>
/// <remarks>
/// <para>
/// I.G.I. 2's ground is three passes multiplied together, and its fog is divided between them so the
/// product still lands on the scene's fog colour at distance. <c>TerrainRender_SetBlendMode</c>
/// (<c>igi2ed 0x666B90</c>) writes <c>D3DRS_FOGCOLOR</c> per mode: the base pass (mode 1,
/// <c>SRCALPHA/ZERO</c>) and the overlay passes (mode 2, <c>SRCALPHA/ONE</c>) fog to the fog colour
/// times 127 out of 255 — half of it — and the lightmap pass (mode 3, <c>DESTCOLOR/SRCCOLOR</c>) fogs to
/// <c>0xFFFFFFFF</c>, white. The lightmap pass doubles what is under it, so far away the frame holds
/// <c>2 × (fog / 2) × 1 = fog</c>; nearer in, the bake is fogged away towards white before the texture
/// is fogged away towards the haze, which is why distant rock in the original keeps its texture at
/// full brightness under a thin blue veil instead of sinking into fog squared.
/// </para>
/// <para>
/// Every other draw fogs to the scene's colour. A title that never sets this leaves it at
/// <see cref="Scene"/>, which is the fog the forward shader always applied.
/// </para>
/// </remarks>
public enum FogTarget
{
    /// <summary>The scene's fog colour, as set by <see cref="IGraphicsDevice.SetFog"/>.</summary>
    Scene,

    /// <summary>Half the scene's fog colour, 127 out of 255 of it: a pass that will be doubled by the next.</summary>
    HalfScene,

    /// <summary>White: a multiplying pass whose contribution fades to the identity with distance.</summary>
    White,

    /// <summary>Half the scene fog, multiplied by the material share stored in the vertex's grey colour.</summary>
    MaterialWeightedHalfScene,
}
