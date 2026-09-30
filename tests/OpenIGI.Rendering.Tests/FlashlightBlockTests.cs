using OpenIGI.Core.Numerics;
using Xunit;

namespace OpenIGI.Rendering.Tests;

/// <summary>
/// That the flashlight reaches every shader that lights with it in the one layout they all declare.
/// </summary>
/// <remarks>
/// The shading and the fog both carry <c>FlashlightParams</c> (<c>Common/Flashlight.slang</c>). Pinned by
/// offset, because the way this fails is a beam on the wall and a beam in the air pointing different ways.
/// </remarks>
public sealed class FlashlightBlockTests
{
    [Fact]
    public void A_light_that_is_off_writes_nothing_every_shader_reads_as_on()
    {
        float[] block = Write(FlashlightLight.Off);

        Assert.All(block, value => Assert.Equal(0f, value));
        Assert.False(FlashlightLight.Off.IsOn);
    }

    [Fact]
    public void The_six_vectors_land_where_the_shader_reads_them()
    {
        FlashlightLight light = Torch();
        float[] block = Write(light with { Throw = 0.75f }, shadowMask: true);

        Expect(block, 0, 0.16f, -0.14f, 0.1f, 40f);
        Expect(block, 4, 0f, 0f, 1f);
        Assert.Equal(MathF.Cos(40f * MathF.PI / 180f), block[7], 5);
        Expect(block, 8, 0f, 1f, 0f, 0.018f);
        Expect(block, 12, 60f, 60f, 60f);
        Assert.Equal(0.3f * 0.3f, block[15], 5);

        float tanOuter = MathF.Tan(22f * MathF.PI / 180f);
        Assert.Equal(tanOuter, block[16], 5);
        Assert.Equal(MathF.Tan(9f * MathF.PI / 180f) / tanOuter, block[17], 5);
        Assert.Equal(MathF.Tan(40f * MathF.PI / 180f) / tanOuter, block[18], 5);
        Assert.Equal(0.06f, block[19], 5);

        Expect(block, 20, 1f, 1f, 0.75f, 0.015f);
    }

    [Fact]
    public void The_cones_are_kept_in_order_so_the_cookie_never_divides_by_nothing()
    {
        float[] block = Write(Torch() with { InnerDegrees = 50f, OuterDegrees = 10f, SpillDegrees = 5f });

        // The inner cone is never wider than the outer, nor the spill narrower.
        Assert.True(block[17] <= 1f);
        Assert.True(block[18] >= 1f);
        Assert.True(block[16] > 0f);
    }

    [Fact]
    public void The_fog_carries_the_same_flashlight_as_the_shading()
    {
        FlashlightLight light = Torch();
        float[] fog = new float[VolumetricFogParameters.Floats];
        VolumetricFogParameters.Write(
            fog,
            new VolumetricFogFrame(
                default,
                ReadOnlyMemory<LocalFogVolume>.Empty,
                ReadOnlyMemory<DeferredLocalLight>.Empty,
                0,
                Vector3f.UnitZ,
                Vector3f.One,
                Vector3f.Zero,
                SkyValid: false,
                BaseHeight: 0f,
                Flashlight: light),
            new VolumetricFogCamera(
                Matrix3f.Identity,
                Matrix3f.Identity,
                Vector3f.Zero,
                new DepthUnprojection(1f, 1f, 0.1f, 1000f),
                new DepthUnprojection(1f, 1f, 0.1f, 1000f),
                Vector3f.Zero),
            (8, 8, 16),
            [],
            [],
            0,
            ShelterMapTransform.None,
            frameIndex: 3,
            hasHistory: false);

        float[] expected = new float[FlashlightBlock.Floats];
        FlashlightBlock.Write(expected, light, shadowMask: false);

        Assert.Equal(expected, fog[^FlashlightBlock.Floats..]);
    }

    /// <summary>The flashlight the remastered presets draw, straight ahead from the chest.</summary>
    internal static FlashlightLight Torch() => new(
        new Vector3f(0.16f, -0.14f, 0.1f),
        new Vector3f(0f, 0f, 1f),
        new Vector3f(0f, 1f, 0f),
        Vector3f.One,
        Intensity: 60f,
        Range: 40f,
        InnerDegrees: 9f,
        OuterDegrees: 22f,
        SpillDegrees: 40f,
        SpillLevel: 0.06f,
        LensRadius: 0.018f,
        NearCore: 0.3f,
        Clearance: 0.12f,
        Shadows: true,
        Volumetric: 1f,
        Dust: 0.015f,
        RigFill: Vector3f.Zero);

    private static float[] Write(in FlashlightLight light, bool shadowMask = false)
    {
        float[] block = new float[FlashlightBlock.Floats];
        FlashlightBlock.Write(block, light, shadowMask);
        return block;
    }

    private static void Expect(float[] actual, int at, params ReadOnlySpan<float> expected)
    {
        for (int i = 0; i < expected.Length; i++)
        {
            Assert.Equal(expected[i], actual[at + i], 6);
        }
    }
}
