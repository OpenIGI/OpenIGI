using Xunit;

namespace OpenIGI.Shaders.Tests;

/// <summary>
/// The invariants the two reflecting passes share, which nothing but a test states.
/// </summary>
/// <remarks>
/// The glazing and the surface reflection are separate modules that resolve a ray the same way, through
/// the same includes. What the includes cannot guarantee is what each HOST defines for them to read, and
/// the mistakes available there are the quiet kind: a shader that compiles, runs, dispatches and lights up
/// the debug view while every ray starts kilometres from any geometry.
/// </remarks>
public sealed class TracedReflectionTests
{
    private const string Glazing = "Glazing/Reflect.slang";
    private const string Surfaces = "Reflection/Reflect.slang";

    /// <summary>The two modules that reflect the world.</summary>
    public static TheoryData<string> Hosts => [Glazing, Surfaces];

    /// <summary>Both fire in WORLD space and look up cards in the CACHE's space.</summary>
    /// <param name="host">The module's source path.</param>
    /// <remarks>
    /// Two different offsets, and pinned because getting them the wrong way round is invisible to every
    /// signal that normally catches a mistake. It compiles, it runs, it dispatches, the debug view lights
    /// up -- and every ray starts kilometres from any geometry, misses, and falls through to the sky. What
    /// you see is a surface reflecting a plausible sky instead of the room, which reads as a tuning
    /// problem rather than as a bug. The glazing shipped that way once.
    ///
    /// Checked per HOST rather than in the shared include, because the include reaches both through a
    /// macro: the mistake available now is not writing the wrong expression but defining
    /// <c>kWorldOrigin</c> to the wrong thing, and only the host can get that wrong.
    /// </remarks>
    [Theory]
    [MemberData(nameof(Hosts))]
    public void Both_fire_in_world_space_and_look_up_cards_in_the_cache_space(string host)
    {
        string source = ShaderRepository.ReadSource(host);

        // The macro must not be defined as the cache's offset, which is the mistake this guards.
        Assert.Contains("#define kWorldOrigin ", source, StringComparison.Ordinal);
        Assert.DoesNotContain("#define kWorldOrigin p.cacheOrigin", source, StringComparison.Ordinal);

        // And the cards are looked up from the cache's own offset, which the ray never uses.
        Assert.Contains("p.cacheOrigin.xyz", source, StringComparison.Ordinal);
    }

    /// <summary>A ray that leaves the field under a roof has lost a ceiling, not found the sky.</summary>
    /// <param name="host">The module's source path.</param>
    /// <remarks>
    /// The clipmaps are quarter-metre voxels at best and coarser further out; a ceiling is a thin sheet a
    /// few metres up, so the march goes through it and runs out of field on the far side. Handing that
    /// back as sky paints daylight -- or, at night, a deep blue -- across the ceiling of every interior in
    /// the game. Measured in a hangar, a third of every pane.
    ///
    /// Both software halves have to ask, and both must read the SAME roof raster the shading and the water
    /// read, which is why the test is on the shared include rather than on each copy.
    /// </remarks>
    [Theory]
    [MemberData(nameof(Hosts))]
    public void The_software_half_asks_the_roof_map_before_answering_with_the_sky(string host)
    {
        string source = ShaderRepository.ReadSource(host);

        // Directly, or through the weather, which carries the same roofs -- the reflection pass needs the
        // water as well and the glazing has none in it at all.
        Assert.True(
            source.Contains("#include \"../Common/Shelter.slang\"", StringComparison.Ordinal)
                || source.Contains("#include \"../Common/Wetness.slang\"", StringComparison.Ordinal),
            "The module reaches the roof map through neither the shelter nor the weather include.");
        Assert.Contains("LosesCeiling(", source, StringComparison.Ordinal);

        // And it is the SHARED one, not a copy: the water and the glass must not disagree about which
        // floors are indoors.
        Assert.Contains("bool LosesCeiling(", ShaderRepository.ReadSource("Common/Shelter.slang"),
            StringComparison.Ordinal);
    }

    /// <summary>A hit the march cannot identify is estimated, and WITHOUT the sky's colour.</summary>
    /// <param name="host">The module's source path.</param>
    /// <remarks>
    /// The traced path answers an unresolvable hit with black and is right to: it can shade a hit from its
    /// own material, so one that gets that far is a genuine and rare gap. A march has no triangle and no
    /// material, so the case is ordinary and black there is a hole in the glass with a hard edge round it.
    ///
    /// What the estimate must not do is borrow the sky's hue. The thing being estimated is a SURFACE and
    /// its colour is exactly what is not known; the sky's is not a neutral guess but a specific wrong one.
    /// By day it passes, and at night the sky is deep blue and nothing else in the level is -- measured
    /// against the traced answer in the same hangar, the glazing came back at a mean of (61, 68, 75) where
    /// the trace gives (91, 89, 86), and read as tinted glass.
    /// </remarks>
    [Theory]
    [MemberData(nameof(Hosts))]
    public void An_unidentifiable_march_hit_is_estimated_without_the_skys_colour(string host)
    {
        string source = ShaderRepository.ReadSource(host);

        Assert.Contains("kUnknownAlbedo", source, StringComparison.Ordinal);
        Assert.Contains("kLuminance", source, StringComparison.Ordinal);

        // The luminance weights take the brightness and drop the hue, which is the whole point.
        Assert.Contains("float3(0.2126, 0.7152, 0.0722)", source, StringComparison.Ordinal);
    }

    /// <summary>A marched hit is resolved with its own accuracy allowed for; a traced one is not.</summary>
    /// <remarks>
    /// The two shared resolves -- the lit frame and the surface cache -- each ask whether a hit is ON the
    /// surface being described, and both were written for a ray-triangle intersection, which is. A hit
    /// found by marching a distance field is good only to a fraction of the voxel that found it, which on
    /// the outer clipmaps is most of a metre; held to the traced tolerances it reads as "the camera drew
    /// something else here" and "no card contains this", and the software glazing came back as a hangar
    /// full of black holes with the ceiling missing from every pane.
    ///
    /// So the widening is asserted here, and so is its absence on the traced path: a wider tolerance there
    /// would start letting a floor resolve as the ceiling above it.
    /// </remarks>
    [Fact]
    public void The_marched_resolve_widens_its_tolerance_by_the_voxel_and_the_traced_one_does_not()
    {
        string glazing = ShaderRepository.ReadSource(Glazing);

        // The software half takes the voxel as slack, in both resolves.
        Assert.Contains("LitAtHitWithin(hit, voxel, radiance)", glazing, StringComparison.Ordinal);
        Assert.Contains("kCardSkin + (voxel * 2.0)", glazing, StringComparison.Ordinal);

        // The traced half resolves through the plain forms, which is the same thing as taking no slack:
        // the cards at exactly the skin, which is the bias the reference gives a triangle hit.
        Assert.Contains("LitAtHit(hitRelative, radiance)", glazing, StringComparison.Ordinal);
        Assert.Contains(
            "SampleCardsWithin(hitRelative + p.cacheOrigin.xyz, hitNormal, kCardSkin, cardRadiance)",
            glazing,
            StringComparison.Ordinal);
    }

    /// <summary>The glazing declares the hit shading before the trace that calls it.</summary>
    /// <remarks>
    /// The trace shades its hit on the way out, because the ray query is local to it -- so <c>ShadeHit</c>
    /// has to be a declared function by the time <c>TraceWorld</c> is included. Slang resolves a call
    /// against what has been declared above it, so the wrong order is a compile error rather than a silent
    /// fault; the test is here so that the error arrives with its reason attached rather than as a wall of
    /// parser output.
    /// </remarks>
    [Fact]
    public void The_glazing_includes_the_hit_shading_before_the_trace_that_calls_it()
    {
        string source = ShaderRepository.ReadSource(Glazing);

        int shade = source.IndexOf("TracedMaterial.slang", StringComparison.Ordinal);
        int trace = source.IndexOf("TracedWorld.slang", StringComparison.Ordinal);

        Assert.True(shade >= 0, "The glazing includes no hit shading.");
        Assert.True(trace >= 0, "The glazing includes no world trace.");
        Assert.True(shade < trace, "The hit shading is included after the trace that calls it.");
        Assert.Contains("#define kShadeHits", source, StringComparison.Ordinal);
    }

    /// <summary>The hit shading reads the albedo array in NORMALISED coordinates, through its sampler.</summary>
    /// <remarks>
    /// It used to fetch by texel, which ignores the bound sampler and takes the nearest one -- and the
    /// pass binds a linear sampler, so the filtering was there and being thrown away. A reflected body at
    /// arm's length in a window is magnified many times over, so each stored texel became a hard block and
    /// the whole reflection read as low-resolution rather than as point-sampled.
    ///
    /// The test is on the READ, not on a resolution: normalised coordinates are resolution-independent,
    /// which is the second reason to prefer them -- the array's size stopped being a constant that C# and
    /// the shader had to agree about, so there is no longer a number here that can drift.
    /// </remarks>
    [Fact]
    public void The_hit_shading_reads_the_albedo_array_through_its_sampler()
    {
        string source = ShaderRepository.ReadSource("Common/TracedMaterial.slang");

        Assert.Contains("uTracedAlbedo.SampleLevel(", source, StringComparison.Ordinal);
        Assert.Contains("float3(uv, float(layer))", source, StringComparison.Ordinal);

        // AND THE UV GOES IN WHOLE. Wrapping a tiling world texture with a frac looks like the obvious way
        // to do it through a sampler that clamps, and it is wrong: a frac moves the sample but it cannot
        // move the FILTER, so at every repeat the bilinear tap set straddles the jump and clamps to the
        // edge texel instead of reaching round. One texel of a layer stored at this resolution is a wide
        // stripe, so every repeat drew a hard line -- vertical marks up the side of every storage tank and
        // along every facade in the reflections. The array carries a wrapping sampler instead.
        Assert.DoesNotContain("float3(frac(uv)", source, StringComparison.Ordinal);
    }
}
