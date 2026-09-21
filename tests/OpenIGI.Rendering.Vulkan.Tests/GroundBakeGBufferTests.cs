using OpenIGI.Rendering.Vulkan.Shaders;
using Xunit;

namespace OpenIGI.Rendering.Vulkan.Tests;

/// <summary>Where I.G.I. 2's ground puts its bake in the G-buffer.</summary>
/// <remarks>
/// <para>
/// The sequel draws its ground in three passes and the third is the tile's bake, MODULATE2X over the
/// two under it. A forward path takes that straight into the picture. A deferred one cannot: the base
/// colour attachment holds an ALBEDO, and a bake multiplied into an albedo is lit a second time by
/// whatever lights the frame.
/// </para>
/// <para>
/// Mission 1 is the measurement behind these. Its bake is a night one, so the ground's albedo came
/// back at about 0.01 where its texture is a tan nearer 0.35, and the dynamic model lit THAT with a
/// sun the level paints (0.10, 0.15, 0.25). The hillsides rendered black, flat and blue. Every preset
/// but vanilla lights dynamically, which is why vanilla was the only one that looked right.
/// </para>
/// </remarks>
public sealed class GroundBakeGBufferTests
{
    /// <summary>The bake pass writes the baked attachment and nothing else.</summary>
    [Fact]
    public void The_grounds_bake_goes_to_the_baked_attachment()
    {
        Assert.Contains("if (push.uNormal0.w > 3.5)", DeferredShaderSource.GBufferFragment, StringComparison.Ordinal);
        Assert.Contains(
            "gBaked = vec4(DecodeSrgb(diffuse.rgb) * DecodeSrgb(vColor.rgb), 1.0);",
            DeferredShaderSource.GBufferFragment,
            StringComparison.Ordinal);
    }

    /// <summary>And it returns before the albedo the rest of the shader would build.</summary>
    /// <remarks>
    /// The whole point is that this pass contributes NO colour. Falling through would multiply the bake
    /// into the albedo again — which is the fault, restored.
    /// </remarks>
    [Fact]
    public void The_bake_pass_contributes_no_colour()
    {
        string source = DeferredShaderSource.GBufferFragment;
        int bake = source.IndexOf("if (push.uNormal0.w > 3.5)", StringComparison.Ordinal);
        int detail = source.IndexOf("if (push.uNormal0.w > 2.5)", StringComparison.Ordinal);

        Assert.True(bake >= 0 && detail > bake, "the bake branch has to be tested before the wider detail one.");
        Assert.Contains(
            "gBaseColour = vec4(0.0);",
            source[bake..detail],
            StringComparison.Ordinal);
    }

    /// <summary>The detail map still multiplies the ground's colour, which is a different pass.</summary>
    /// <remarks>
    /// Three says the lightmap slot is the material's detail map; four says the draw is the bake. Both
    /// ride the same component, so the narrower test has to come first and the wider one has to survive.
    /// </remarks>
    [Fact]
    public void The_detail_map_still_multiplies_the_colour()
    {
        Assert.Contains(
            "albedo *= DecodeSrgb(texture(uLightmap, vLightmapUv).rgb);",
            DeferredShaderSource.GBufferFragment,
            StringComparison.Ordinal);
    }
    /// <summary>The ground asks for no specular, because the original's ground has none.</summary>
    /// <remarks>
    /// Retail draws the ground as three texture passes with no lighting in them, so a specular lobe on it
    /// is the port's invention. Given one it laid a sheen down every sun-facing slope -- worst at distance,
    /// where a coarse tile is one flat triangle and the highlight is uniform over a whole hillside -- and
    /// fed the screen-space reflections besides. Only the dynamic model reads these channels, which is why
    /// the ground was shiny on that model alone.
    /// </remarks>
    [Fact]
    public void The_ground_asks_for_no_specular()
    {
        string source = DeferredShaderSource.GBufferFragment;
        int gate = source.LastIndexOf("if (push.uNormal0.w > 2.5)", StringComparison.Ordinal);
        int encode = source.IndexOf("gMaterial = EncodeGBufferMaterial(", StringComparison.Ordinal);

        Assert.True(gate >= 0 && encode > gate, "the ground has to be made matte before the material is packed.");

        string block = source[gate..encode];
        Assert.Contains("specular = 0.0;", block, StringComparison.Ordinal);
        Assert.Contains("roughness = 1.0;", block, StringComparison.Ordinal);
    }

    /// <summary>A whole bake gets no sun added over it, which is what hybrid otherwise does.</summary>
    /// <remarks>
    /// The hybrid premise is that a bake is the AMBIENT term with the key light still to be computed. For
    /// a game whose bakes already carry the key light that counts it twice, and on I.G.I. 2's night
    /// missions it does so at a scale that swamps the bake: the directional there is the level's own night
    /// sun, some thirty times the moon the cycle invents for a level that authors none. Measured on
    /// mission 1's ground against vanilla: 3.5 times with the night exposure on, 6.9 with it off.
    /// </remarks>
    [Fact]
    public void A_whole_bake_takes_no_added_sun()
    {
        string source = DeferredShaderSource.LightingFragment(rayQuery: false);

        // The gate, and the nothing it substitutes for the sun.
        Assert.Contains("light.uSky.z > 0.5", source, StringComparison.Ordinal);
        Assert.Contains("? vec3(0.0)", source, StringComparison.Ordinal);

        // And it gates the HYBRID branch, which is the one that adds a sun to a bake. Anchored on that
        // branch's own composite rather than on its trailing "// Hybrid" label, which the file also
        // carries further up and IndexOf finds first.
        int gate = source.IndexOf("light.uSky.z > 0.5", StringComparison.Ordinal);
        int composite = source.IndexOf("albedo * (baked + sun)", StringComparison.Ordinal);
        Assert.True(gate >= 0 && composite > gate, "the gate has to stand in the hybrid branch.");
    }

    /// <summary>And the flag rides the sky vector's spare slot rather than growing the push block.</summary>
    /// <remarks>
    /// The lighting push is four vec4s against Vulkan's guaranteed 128 bytes, so there is no room to grow.
    /// </remarks>
    [Fact]
    public void The_whole_bake_flag_is_declared_in_the_push_block()
    {
        Assert.Contains(
            "vec4 uSky;                 // x: the title draws its own sky, z: its bakes are whole, yw unused",
            DeferredShaderSource.LightingFragment(rayQuery: false),
            StringComparison.Ordinal);
    }
}
