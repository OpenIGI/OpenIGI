using OpenIGI.Rendering.Vulkan.Shaders;
using Xunit;

namespace OpenIGI.Rendering.Vulkan.Tests;

/// <summary>What the lighting pass does with a ground whose bake is whole.</summary>
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
/// The G-buffer half of this moved to <c>OpenIGI.Shaders.Tests</c> with the shader. What is left
/// guards the lighting pass, which is still GLSL.
/// </para>
/// </remarks>
public sealed class GroundBakeLightingTests
{
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
