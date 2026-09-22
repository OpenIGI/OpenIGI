using Xunit;

namespace OpenIGI.Shaders.Tests;

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
/// <para>
/// These moved here with the shader. The companions that guard the lighting pass stayed in
/// <c>OpenIGI.Rendering.Vulkan.Tests</c>, because the lighting pass is still GLSL.
/// </para>
/// </remarks>
public sealed class GroundBakeGBufferTests
{
    private static string Source => ShaderRepository.ReadSource("World/GBuffer.slang");

    /// <summary>The bake pass writes the baked attachment and nothing else.</summary>
    [Fact]
    public void The_grounds_bake_goes_to_the_baked_attachment()
    {
        Assert.Contains("if (push.normal0.w > 3.5)", Source, StringComparison.Ordinal);
        Assert.Contains(
            "output.baked = float4(DecodeSrgb(diffuse.rgb) * DecodeSrgb(input.color.rgb), 1.0);",
            Source,
            StringComparison.Ordinal);
    }

    /// <summary>And it returns before the albedo the rest of the shader would build.</summary>
    /// <remarks>
    /// The whole point is that this pass contributes NO colour. Falling through would multiply the
    /// bake into the albedo again — which is the fault, restored.
    /// </remarks>
    [Fact]
    public void The_bake_pass_contributes_no_colour()
    {
        string source = Source;
        int bake = source.IndexOf("if (push.normal0.w > 3.5)", StringComparison.Ordinal);
        int detail = source.IndexOf("if (push.normal0.w > 2.5)", StringComparison.Ordinal);

        Assert.True(bake >= 0 && detail > bake, "the bake branch has to be tested before the wider detail one.");

        string block = source[bake..detail];
        Assert.Contains("output.baseColour = float4(0.0, 0.0, 0.0, 0.0);", block, StringComparison.Ordinal);
        Assert.Contains("return output;", block, StringComparison.Ordinal);
    }

    /// <summary>The detail map still multiplies the ground's colour, which is a different pass.</summary>
    /// <remarks>
    /// Three says the lightmap slot is the material's detail map; four says the draw is the bake. Both
    /// ride the same component, so the narrower test has to come first and the wider one has to
    /// survive.
    /// </remarks>
    [Fact]
    public void The_detail_map_still_multiplies_the_colour()
    {
        Assert.Contains(
            "albedo *= DecodeSrgb(uLightmap.Sample(uLightmapSampler, input.lightmapUv).rgb);",
            Source,
            StringComparison.Ordinal);
    }

    /// <summary>The ground asks for no specular, because the original's ground has none.</summary>
    /// <remarks>
    /// Retail draws the ground as three texture passes with no lighting in them, so a specular lobe on
    /// it is the port's invention. Given one it laid a sheen down every sun-facing slope -- worst at
    /// distance, where a coarse tile is one flat triangle and the highlight is uniform over a whole
    /// hillside -- and fed the screen-space reflections besides. Only the dynamic model reads these
    /// channels, which is why the ground was shiny on that model alone.
    /// </remarks>
    [Fact]
    public void The_ground_asks_for_no_specular()
    {
        string source = Source;
        int gate = source.LastIndexOf("if (push.normal0.w > 0.5)", StringComparison.Ordinal);
        int encode = source.IndexOf("output.material = EncodeGBufferMaterial(", StringComparison.Ordinal);

        Assert.True(gate >= 0 && encode > gate, "the ground has to be made matte before the material is packed.");

        string block = source[gate..encode];
        Assert.Contains("specular = 0.0;", block, StringComparison.Ordinal);
        Assert.Contains("roughness = 1.0;", block, StringComparison.Ordinal);
    }
}
