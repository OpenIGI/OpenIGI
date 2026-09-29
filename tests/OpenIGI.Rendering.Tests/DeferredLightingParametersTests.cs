using OpenIGI.Core.Numerics;
using Xunit;

namespace OpenIGI.Rendering.Tests;

/// <summary>
/// That the deferred shading's two blocks are filled where the shader reads them.
/// </summary>
/// <remarks>
/// Pinned by offset rather than described, because this is the layout two backends now share and the
/// way it fails is not a crash: a value written one slot along shades plausibly and wrongly, and
/// nothing reports it. The offsets here are the shader's own struct, read off
/// <c>Source/Deferred/Shading.slang</c>.
/// </remarks>
public sealed class DeferredLightingParametersTests
{
    [Fact]
    public void The_block_is_the_size_the_shader_declares()
    {
        Assert.Equal(DeferredLightingBlocks.GiBytes / sizeof(float), DeferredLightingParameters.GiFloats);
        Assert.Equal(24, DeferredLightingParameters.LightFloats);
    }

    [Fact]
    public void A_block_of_the_wrong_length_is_refused()
    {
        Assert.Throws<ArgumentException>(() =>
            DeferredLightingParameters.WriteGi(new float[8], new DeferredLightingState(Unprojection()), [], []));

        Assert.Throws<ArgumentException>(() =>
            DeferredLightingParameters.WriteLight(new float[8], Vector3f.UnitZ, 0f, Vector3f.One, 1f, Vector3f.Zero));
    }

    [Fact]
    public void The_unprojection_leads_the_block_because_everything_after_it_needs_a_position()
    {
        float[] block = Gi(new DeferredLightingState(new DepthUnprojection(1.5f, 2.5f, 0.25f, 900f)));

        Assert.Equal(1.5f, block[0]);
        Assert.Equal(2.5f, block[1]);
        Assert.Equal(0.25f, block[2]);
        Assert.Equal(900f, block[3]);
    }

    [Fact]
    public void A_field_that_is_off_says_so_beside_its_own_grid()
    {
        float[] on = Gi(new DeferredLightingState(
            Unprojection(),
            ProbeOrigin: new Vector3f(1f, 2f, 3f),
            ProbeEnabled: true,
            ProbeInverseCount: new Vector3f(0.5f, 0.25f, 0.125f),
            ProbeSpacing: 4f));

        Assert.Equal(1f, on[4]);
        Assert.Equal(3f, on[6]);
        Assert.Equal(1f, on[7]);
        Assert.Equal(0.25f, on[9]);
        Assert.Equal(4f, on[11]);

        // The normal offset is half the spacing, which is what keeps a lookup off the surface it came
        // from without reaching the cell beyond.
        Assert.Equal(2f, on[12]);

        Assert.Equal(0f, Gi(new DeferredLightingState(Unprojection()))[7]);
    }

    [Fact]
    public void The_sky_tint_is_the_colour_times_its_intensity()
    {
        float[] block = Gi(new DeferredLightingState(
            Unprojection(),
            SkyColour: new Vector3f(0.5f, 0.25f, 1f),
            SkyIntensity: 2f));

        Assert.Equal(1f, block[16]);
        Assert.Equal(0.5f, block[17]);
        Assert.Equal(2f, block[18]);

        // And a negative intensity is nought rather than a sky that darkens what it lights.
        Assert.Equal(0f, Gi(new DeferredLightingState(Unprojection(), SkyColour: Vector3f.One, SkyIntensity: -1f))[16]);
    }

    [Fact]
    public void The_rotation_is_written_column_major_with_its_bottom_row_intact()
    {
        // A mat4 in the block, filled from a 3x3: the fourth column and row are the identity's, and a
        // missing one in the corner leaves every world-space lookup scaled by nought.
        float[] block = Gi(new DeferredLightingState(Unprojection(), ViewToWorld: Matrix3f.Identity));

        Assert.Equal(1f, block[28]);
        Assert.Equal(1f, block[33]);
        Assert.Equal(1f, block[38]);
        Assert.Equal(1f, block[43]);
    }

    [Fact]
    public void The_cascades_describe_the_frame_even_before_they_hold_anything()
    {
        // The splits and the matrices say how this frame divided the view either way; only the count the
        // shading reads stands down, so a level's first frames light unshadowed rather than shadowed by
        // an empty map.
        float[] splits = [10f, 30f, 90f, 300f];
        Matrix4f[] matrices = [Matrix4f.Identity, Matrix4f.Identity, Matrix4f.Identity, Matrix4f.Identity];

        float[] waiting = Gi(
            new DeferredLightingState(Unprojection(), CascadeCount: 4, ShadowsReady: false),
            splits,
            matrices);

        Assert.Equal(10f, waiting[44]);
        Assert.Equal(300f, waiting[47]);
        Assert.Equal(0f, waiting[48]);

        float[] ready = Gi(
            new DeferredLightingState(Unprojection(), CascadeCount: 4, ShadowsReady: true),
            splits,
            matrices);

        Assert.Equal(4f, ready[48]);
    }

    [Fact]
    public void A_cascade_the_frame_did_not_draw_is_the_identity_rather_than_stale()
    {
        float[] block = Gi(
            new DeferredLightingState(Unprojection(), CascadeCount: 1, ShadowsReady: true),
            [10f],
            [Matrix4f.Identity]);

        // The second cascade's matrix: the identity, not whatever was in the block before.
        Assert.Equal(1f, block[52 + 16]);
        Assert.Equal(1f, block[52 + 16 + 5]);
        Assert.Equal(0f, block[45]);
    }

    [Fact]
    public void A_level_with_no_roof_map_writes_the_transform_that_says_so()
    {
        float[] block = Gi(new DeferredLightingState(
            Unprojection(),
            HasShelterMap: false,
            Shelter: new ShelterMapTransform(100f, 200f, 0.5f, 8, 8, 1f, 2f, 3f, 4f)));

        Assert.Equal(0f, block[123]);

        // And the transform itself is the none one, not the one that was handed in: a shader told there
        // is no map must not be able to read a plausible one out of the block anyway.
        Assert.Equal(0f, block[124]);
        Assert.Equal(0f, block[125]);
    }

    [Fact]
    public void A_frame_with_no_fog_leaves_its_scale_at_nought()
    {
        // Which is how the shader knows to leave the colour alone rather than sample whatever was bound
        // in the volume's place.
        Assert.Equal(0f, Gi(new DeferredLightingState(Unprojection()))[138]);

        float[] fogged = Gi(new DeferredLightingState(
            Unprojection(),
            Fog: new FogSliceDistribution(1f, 2f, 3f, 0.25f)));

        Assert.Equal(1f, fogged[136]);
        Assert.Equal(3f, fogged[138]);
        Assert.Equal(0.25f, fogged[139]);
    }

    [Fact]
    public void The_light_block_carries_the_sun_the_model_and_the_ambient()
    {
        float[] block = new float[DeferredLightingParameters.LightFloats];

        DeferredLightingParameters.WriteLight(
            block,
            new Vector3f(0f, 0f, 1f),
            model: 2f,
            new Vector3f(1f, 0.9f, 0.8f),
            lightScale: 1.5f,
            new Vector3f(0.1f, 0.2f, 0.3f),
            debugView: 7f,
            paintedSky: true,
            bakesWhole: false);

        Assert.Equal(1f, block[2]);
        Assert.Equal(2f, block[3]);
        Assert.Equal(0.9f, block[5]);
        Assert.Equal(1.5f, block[7]);
        Assert.Equal(0.3f, block[10]);
        Assert.Equal(7f, block[11]);
        Assert.Equal(1f, block[12]);
        Assert.Equal(0f, block[14]);
    }

    [Fact]
    public void The_night_sky_follows_the_four_lighting_vectors()
    {
        float[] block = new float[DeferredLightingParameters.LightFloats];
        NightSky night = new(
            Airglow: 0.004f,
            CloudCover: 0.6f,
            StarRadiance: 0.12f,
            MoonDirection: new Vector3f(0.1f, 0.2f, 0.9f),
            MoonRadiance: 2.5f,
            Seconds: 3605f);

        DeferredLightingParameters.WriteLight(
            block, Vector3f.UnitZ, 2f, Vector3f.One, 1f, Vector3f.Zero, night: night);

        Assert.Equal(0.1f, block[16]);
        Assert.Equal(0.2f, block[17]);
        Assert.Equal(0.9f, block[18]);
        Assert.Equal(2.5f, block[19]);
        Assert.Equal(0.12f, block[20]);
        Assert.Equal(0.6f, block[21]);

        // Wrapped at the hour, so the twinkle's phase keeps its fraction however long a session runs.
        Assert.Equal(5f, block[22], 3);
    }

    [Fact]
    public void A_day_draws_no_stars_and_no_moon()
    {
        float[] block = new float[DeferredLightingParameters.LightFloats];

        DeferredLightingParameters.WriteLight(block, Vector3f.UnitZ, 2f, Vector3f.One, 1f, Vector3f.Zero);

        Assert.Equal(0f, block[19]);
        Assert.Equal(0f, block[20]);
    }

    [Fact]
    public void The_flashlight_follows_the_fog_and_is_all_zero_when_off()
    {
        Assert.Equal(DeferredLightingParameters.FlashlightOffset + FlashlightBlock.Floats, DeferredLightingParameters.GiFloats);

        float[] off = Gi(new DeferredLightingState(Unprojection()));
        Assert.All(off.AsSpan(DeferredLightingParameters.FlashlightOffset).ToArray(), value => Assert.Equal(0f, value));

        FlashlightLight light = FlashlightBlockTests.Torch();
        float[] on = Gi(new DeferredLightingState(Unprojection(), Flashlight: light, FlashlightShadowed: true));

        float[] expected = new float[FlashlightBlock.Floats];
        FlashlightBlock.Write(expected, light, shadowMask: true, frameIndex: 0);
        Assert.Equal(expected, on.AsSpan(DeferredLightingParameters.FlashlightOffset, FlashlightBlock.Floats).ToArray());
    }

    private static DepthUnprojection Unprojection() => new(1f, 1f, 0.1f, 1000f);

    private static float[] Gi(
        in DeferredLightingState state,
        ReadOnlySpan<float> splits = default,
        ReadOnlySpan<Matrix4f> matrices = default)
    {
        float[] block = new float[DeferredLightingParameters.GiFloats];
        DeferredLightingParameters.WriteGi(block, state, splits, matrices);
        return block;
    }
}
