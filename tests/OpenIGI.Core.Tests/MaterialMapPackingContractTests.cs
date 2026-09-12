using OpenIGI.Core.Graphics;
using Xunit;

namespace OpenIGI.Core.Tests;

/// <summary>
/// Pins the detail map's byte layout, so the two implementations of it cannot drift apart.
/// </summary>
/// <remarks>
/// <para>
/// There are two packers now. <see cref="MaterialMapGenerator.PackDetail"/> derives maps at runtime from
/// the player's own installation; <c>tools/texture-forge</c> derives better ones offline from an upscaled
/// texture. Both write the same PNG, into the same directory, for the same sampler to read — so the layout
/// is a contract between two languages rather than an implementation detail of either.
/// </para>
/// <para>
/// <b>Why a table of literal bytes rather than a comparison between the two.</b> Running the Python side
/// from a test would need Python, a virtual environment and a gigabyte of weights in CI, which is not a
/// test so much as a second build. Instead the CONTRACT is written out here as expected bytes, and the
/// identical table is asserted on the Python side in <c>tools/texture-forge/tests/test_pack.py</c>. Neither
/// can move without failing, and neither needs the other to be installed.
/// </para>
/// <para>
/// <b>What this is actually defending against.</b> <see cref="Bgra32"/>'s constructor takes its channels in
/// MEMORY order, so its first positional argument is blue, while a sampler — and the Python side, and PNG
/// itself — calls the first channel red. Writing that positionally once put roughness where the normal's x
/// belonged, and the level still rendered: every value was in range and the result looked like a plausible
/// picture. That is the worst way for it to be wrong, and a literal table is the only thing that catches it.
/// </para>
/// </remarks>
public sealed class MaterialMapPackingContractTests
{
    /// <summary>
    /// The contract: what each set of inputs must produce, channel by channel, as the sampler reads them.
    /// </summary>
    /// <remarks>
    /// Derived from the layout the G-buffer shader undoes, and nothing else:
    /// <list type="bullet">
    /// <item><description>r = (normal.x * 0.5) + 0.5</description></item>
    /// <item><description>g = (normal.y * 0.5) + 0.5, OpenGL handedness — positive is up</description></item>
    /// <item><description>b = roughness * 0.5, so 0.5 is the identity multiplier</description></item>
    /// <item><description>a = occlusion, white for open and dark for a crease</description></item>
    /// </list>
    /// </remarks>
    public static TheoryData<float, float, float, float, byte, byte, byte, byte> Contract =>
        new()
        {
            // A flat, ordinary, non-metallic surface: the identity of this whole system. Every channel
            // here is the value that means "change nothing", which makes it the one row whose bytes can be
            // reasoned about without arithmetic.
            { 0f, 0f, 1f, 1f, 128, 128, 128, 255 },

            // The normal's extremes, which is where a swapped channel shows.
            { 1f, 0f, 1f, 1f, 255, 128, 128, 255 },
            { -1f, 0f, 1f, 1f, 0, 128, 128, 255 },
            { 0f, 1f, 1f, 1f, 128, 255, 128, 255 },
            { 0f, -1f, 1f, 1f, 128, 0, 128, 255 },

            // Roughness at the ends of the range the generator may produce. It is a MULTIPLIER stored
            // halved, so zero is "make this mirror-smooth" and two is the most it may roughen a material.
            { 0f, 0f, 0f, 1f, 128, 128, 0, 255 },
            { 0f, 0f, 2f, 1f, 128, 128, 255, 255 },

            // Occlusion, fully closed. Alpha used to carry the complement of METALLIC and now carries
            // this; the byte is unchanged because the two agree at both ends, which is exactly why no
            // pack already on disk needs rewriting.
            { 0f, 0f, 1f, 0f, 128, 128, 128, 0 },

            // An arbitrary interior point, to catch a rounding rule that only agrees at the ends.
            { 0.5f, -0.25f, 1.5f, 0.75f, 191, 96, 191, 191 },
        };

    /// <summary>Every packed texel must match the contract byte for byte.</summary>
    /// <param name="normalX">The tangent normal's x.</param>
    /// <param name="normalY">The tangent normal's y.</param>
    /// <param name="roughness">The roughness multiplier.</param>
    /// <param name="occlusion">How much of its surroundings the texel can see.</param>
    /// <param name="red">The byte the sampler must read as red.</param>
    /// <param name="green">The byte the sampler must read as green.</param>
    /// <param name="blue">The byte the sampler must read as blue.</param>
    /// <param name="alpha">The byte the sampler must read as alpha.</param>
    [Theory]
    [MemberData(nameof(Contract))]
    public void PackDetailMatchesTheContract(
        float normalX,
        float normalY,
        float roughness,
        float occlusion,
        byte red,
        byte green,
        byte blue,
        byte alpha)
    {
        MaterialMapGenerator.Maps maps = new(
            Width: 1,
            Height: 1,
            Elevation: [0.5f],
            NormalX: [normalX],
            NormalY: [normalY],
            Roughness: [roughness],
            Occlusion: [occlusion]);

        Bgra32[] packed = new Bgra32[1];
        MaterialMapGenerator.PackDetail(maps, packed);

        Assert.Equal(red, packed[0].R);
        Assert.Equal(green, packed[0].G);
        Assert.Equal(blue, packed[0].B);
        Assert.Equal(alpha, packed[0].A);
    }

    /// <summary>
    /// A neutral map must be the neutral texture the device substitutes when there is no pack at all.
    /// </summary>
    /// <remarks>
    /// <c>VulkanGraphicsDevice.CreateNeutralDetailTexture</c> uploads a single texel of (128, 128, 128, 255)
    /// for every surface with no map, and the shader multiplies rather than branches — so "no pack" and "a
    /// pack that says change nothing" have to be the same four bytes or turning a pack on would shift every
    /// surface in the level slightly, with nothing to say why.
    /// </remarks>
    [Fact]
    public void NeutralMapMatchesTheDeviceFallback()
    {
        MaterialMapGenerator.Maps flat = new(
            Width: 1,
            Height: 1,
            Elevation: [0.5f],
            NormalX: [0f],
            NormalY: [0f],
            Roughness: [1f],
            Occlusion: [1f]);

        Bgra32[] packed = new Bgra32[1];
        MaterialMapGenerator.PackDetail(flat, packed);

        Assert.Equal(new Bgra32(128, 128, 128, 255), packed[0]);
    }
}
