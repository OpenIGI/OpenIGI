namespace OpenIGI.Rendering;

/// <summary>
/// How a surface responds to light, beyond the colour its texture gives it.
/// </summary>
/// <remarks>
/// <para>
/// The original's data has nothing of this kind: a surface is a diffuse texture and a lightmap, and
/// how shiny it is was the lighting artist's problem, not the renderer's. So these values are
/// <em>authored for the port</em>, matched onto textures by name in <c>materials.json</c> — snow is a
/// rough dielectric, a gantry is metal, a window is smooth and barely diffuse.
/// </para>
/// <para>
/// That makes them a deliberate divergence rather than a reconstruction, and they only ever apply on
/// the modern path: under the vanilla preset nothing reads them.
/// </para>
/// </remarks>
/// <param name="Roughness">
/// How wide the surface scatters a reflection: 0 is a mirror, 1 is chalk. Drives both the specular
/// highlight and, from stage R8, how sharp a screen-space reflection is allowed to be.
/// </param>
/// <param name="Metallic">
/// Whether the surface reflects its own colour rather than the light's. Physically this is nearly
/// always 0 or 1; values between exist only for surfaces whose texture mixes both.
/// </param>
/// <param name="Emissive">
/// How much light the surface gives off on its own, as a multiple of its albedo. Lamps and screens.
/// One is the surface's own colour unlit — the sky dome, a tracer, a muzzle flash. A lamp wants more
/// than one: a fluorescent tube painted at three quarters grey is not a light until it is scaled past
/// what a lit surface could ever reach, which is what makes the bloom find it and the eye read it as
/// the source rather than as a pale panel. See <see cref="MaximumEmissive"/> for how far that goes.
/// </param>
/// <param name="CastsOcclusion">
/// Whether this surface is a solid occluder of light. False for the thin and cut-out surfaces neither
/// a depth buffer nor an opaque ray can describe honestly: a depth buffer records where a surface
/// starts and never where it ends, so the horizon search treats anything thin as reaching to infinity
/// and darkens whatever stands behind it; an opaque triangle in a ray-tracing structure blocks a
/// shadow or gather ray whole, with no per-texel alpha test to let the light through a pane of glass or
/// between the wires of a fence. So the flag gates both the ambient occlusion and the traceable world.
/// Glass, wire fences, foliage and soldiers are the ones that matter.
/// </param>
/// <param name="NoHistory">
/// Whether the temporal resolve must not blend history onto this surface this frame. For the draws
/// where the engine <em>knows</em> the previous frame shows something unrelated — a body's first
/// appearance, anything the simulation teleports — where reprojection would confidently fetch the
/// wrong pixels. Per draw and per frame: set it for the one frame the discontinuity happens, then
/// stop, or the surface never converges.
/// </param>
public readonly record struct SurfaceMaterial(
    float Roughness = 0.8f,
    float Metallic = 0.0f,
    float Emissive = 0.0f,
    bool CastsOcclusion = true,
    bool NoHistory = false)
{
    /// <summary>The emissive channel's value for a surface excluded from ambient occlusion.</summary>
    /// <remarks>
    /// <para>
    /// The G-buffer has no channel to spare — every one of its four targets already carries the
    /// surface's own alpha, because GL blends each attachment by that attachment's alpha. So the
    /// exclusion rides in the emissive channel, which in practice only ever holds nought or one: a
    /// surface either glows or it does not. A third value at a quarter is unambiguous against both,
    /// and leaves the <c>emissive &gt; 0.5</c> test every other pass already uses working untouched.
    /// </para>
    /// <para>
    /// It is a compromise and worth replacing with a proper flags channel if the G-buffer ever gains
    /// one.
    /// </para>
    /// </remarks>
    public const float ExcludedFromOcclusion = 0.25f;

    /// <summary>The brightest a surface may declare itself, as a multiple of its albedo.</summary>
    /// <remarks>
    /// Set by what the channel can carry rather than by taste: the packing below spends the band above
    /// a half on the strength in steps of a sixteenth, and eight bits leave a hundred and twenty-seven
    /// of them. Nothing in the game asks for more — a tube at eight times its albedo is already the
    /// brightest thing in any interior — and a value past it is clamped rather than wrapped.
    /// </remarks>
    public const float MaximumEmissive = 127f / EmissiveStepsPerUnit;

    /// <summary>How many packed steps one unit of <see cref="Emissive"/> spans.</summary>
    /// <remarks>
    /// Sixteen, so that a strength of exactly one — the sky dome, a tracer, every emissive draw that
    /// existed before lamps did — lands on exactly one code and comes back exactly one. A round step
    /// is what keeps this packing from putting a fraction of a percent onto the sky, which is inside
    /// the tolerance the reference comparisons are read at.
    /// </remarks>
    private const float EmissiveStepsPerUnit = 16f;

    /// <summary>Gets the value written into the G-buffer's emissive channel.</summary>
    /// <remarks>
    /// <para>
    /// Emission wins where a surface asks for both: a glowing surface is already ignored by the
    /// occlusion pass, so there is nothing to say.
    /// </para>
    /// <para>
    /// The channel carries the STRENGTH and not merely the flag. It is one byte, and the three things
    /// it has to say do not overlap: nought is an ordinary surface, a quarter is the occlusion
    /// exclusion above, and every code past a half is emissive with the multiple recovered from how
    /// far past. A byte at 128 + 16x is that multiple x, so one is 144 and comes back exact; the
    /// smallest emissive code is 129, a sixteenth, which reads as a glow rather than as nothing.
    /// Every <c>emissive &gt; 0.5</c> test in the passes downstream is untouched by this.
    /// </para>
    /// </remarks>
    public float PackedEmissive =>
        Emissive > 0f ? PackEmissive(Emissive) : CastsOcclusion ? 0f : ExcludedFromOcclusion;

    /// <summary>Packs an emissive strength into the G-buffer's channel.</summary>
    /// <param name="strength">The multiple of albedo the surface emits; clamped into range.</param>
    /// <returns>The channel value, always above a half so the flag tests downstream still see it.</returns>
    public static float PackEmissive(float strength)
    {
        float clamped = Math.Clamp(strength, 1f / EmissiveStepsPerUnit, MaximumEmissive);

        return MathF.Round(128f + (clamped * EmissiveStepsPerUnit)) / 255f;
    }

    /// <summary>
    /// Gets the material a surface with no rule matching it gets.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Rough and non-metallic: the reading that makes an unclassified surface behave like painted
    /// concrete, which is what most of the game is. A shiny default would put highlights on every
    /// wall in the game the first time the sun moved.
    /// </para>
    /// <para>
    /// Spelled out rather than written <c>new()</c>: for a struct that means the <em>zero</em>
    /// instance, not the primary constructor's defaults, so it would have produced a mirror-smooth
    /// surface with roughness nought — the opposite of what is wanted.
    /// </para>
    /// </remarks>
    public static SurfaceMaterial Default { get; } = new(Roughness: 0.8f, Metallic: 0.0f, Emissive: 0.0f);

    /// <summary>Gets the material the sky dome is drawn with.</summary>
    /// <remarks>
    /// <para>
    /// Light rather than a lit surface, which the emissive strength of one says, and NOT a solid
    /// occluder, which is the part that matters to the ray-traced world: built into the acceleration
    /// structure the dome is a closed shell around the level, and every ray fired at the sun from
    /// anywhere inside it hits — the traced mask came back shadowed for every sun-facing pixel in the
    /// frame, indoors and out, which reads as a level with no sunlight rather than as a bug in the sky.
    /// </para>
    /// <para>
    /// Spelled out as a material rather than left as a bare emissive override because emission alone can
    /// no longer mean it. A lamp is emissive too, and a lamp is ordinary solid geometry that belongs in
    /// the traced world: it has to shadow the sun like the ceiling it hangs from. Saying "emits, and is
    /// not an occluder" names the sky and nothing else.
    /// </para>
    /// </remarks>
    public static SurfaceMaterial Sky { get; } = new(Emissive: 1f, CastsOcclusion: false);

    /// <summary>Gets a value indicating whether this material is the default one.</summary>
    public bool IsDefault => this == Default;
}
