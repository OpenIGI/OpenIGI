using OpenIGI.Core.Numerics;
using OpenIGI.Rendering.Mesh3D;
using Xunit;

namespace OpenIGI.Rendering.Tests;

/// <summary>
/// That every slot of the forward push block carries what it is supposed to.
/// </summary>
/// <remarks>
/// The block is 128 bytes with nothing spare and several slots that mean different things depending on
/// what the draw is. A slot written with the wrong one shades plausibly and incorrectly, which is the
/// worst kind of defect to have in a renderer: nothing fails, the picture is merely wrong somewhere.
/// </remarks>
public sealed class ForwardPushBlockTests
{
    /// <summary>Material fog is explicitly weighted without activating the IGI1 terrain morph or the rig light.</summary>
    [Theory]
    [InlineData(BlendMode.Opaque)]
    [InlineData(BlendMode.Additive)]
    public void Material_fog_preserves_the_weight_after_fogging(BlendMode blend)
    {
        Span<float> block = stackalloc float[ForwardPushBlock.Floats];
        SceneFog fog = new(new Vector3f(0.2f, 0.4f, 0.6f), 0.001f);
        ForwardPushBlock.Write(block, World with { Fog = fog, FogTarget = FogTarget.MaterialWeightedHalfScene },
            Matrix4f.Identity, blend, hasLightmap: true);
        Assert.Equal(-1f, block[23]);
        Assert.Equal(fog.Colour.X * SceneFog.HalfTarget, block[28]);
        Assert.Equal(fog.Colour.Y * SceneFog.HalfTarget, block[29]);
        Assert.Equal(fog.Colour.Z * SceneFog.HalfTarget, block[30]);
        Assert.Equal(fog.DensityPerMetre, block[31]);
    }

    [Fact]
    public void The_first_sixteen_slots_are_the_model_view_projection()
    {
        Matrix4f modelView = Matrix4f.CreateTranslation(1f, 2f, 3f);
        ForwardDrawState state = World with { Projection = Matrix4f.Identity };

        Span<float> block = stackalloc float[ForwardPushBlock.Floats];
        ForwardPushBlock.Write(block, state, modelView, BlendMode.Opaque, hasLightmap: false);

        Span<float> expected = stackalloc float[16];
        modelView.CopyColumnMajorTo(expected);

        Assert.Equal(expected.ToArray(), block[..16].ToArray());
    }

    [Fact]
    public void A_world_draw_is_marked_as_the_world_and_carries_the_lightmap_scale()
    {
        Span<float> block = stackalloc float[ForwardPushBlock.Floats];
        ForwardPushBlock.Write(
            block,
            World with { LightmapScale = 2f },
            Matrix4f.Identity,
            BlendMode.Opaque,
            hasLightmap: true);

        Assert.Equal(ForwardDrawMarker.World, block[16]);
        Assert.Equal(2f, block[17]);
    }

    [Fact]
    public void Anything_blended_is_marked_translucent_whatever_it_is()
    {
        Span<float> block = stackalloc float[ForwardPushBlock.Floats];

        ForwardPushBlock.Write(block, World, Matrix4f.Identity, BlendMode.Additive, hasLightmap: false);
        Assert.Equal(ForwardDrawMarker.Translucent, block[16]);

        ForwardPushBlock.Write(
            block,
            World with { FirstPerson = true },
            Matrix4f.Identity,
            BlendMode.AlphaBlend,
            hasLightmap: false);

        Assert.Equal(ForwardDrawMarker.Translucent, block[16]);
    }

    [Fact]
    public void The_lit_rig_carries_the_inverse_exposure_rather_than_the_lightmap_scale()
    {
        // Written raw, the eye adaptation of a dim interior multiplies the arms into pure white.
        Span<float> block = stackalloc float[ForwardPushBlock.Floats];

        ForwardPushBlock.Write(
            block,
            Rig with { Exposure = 4f, LightmapScale = 2f },
            Matrix4f.Identity,
            BlendMode.Opaque,
            hasLightmap: false);

        Assert.Equal(ForwardDrawMarker.LitFirstPerson, block[16]);
        Assert.Equal(0.25f, block[17]);
    }

    [Fact]
    public void An_exposure_of_nothing_does_not_divide_by_nothing()
    {
        Span<float> block = stackalloc float[ForwardPushBlock.Floats];

        ForwardPushBlock.Write(
            block,
            Rig with { Exposure = 0f },
            Matrix4f.Identity,
            BlendMode.Opaque,
            hasLightmap: false);

        Assert.True(float.IsFinite(block[17]));
    }

    [Fact]
    public void The_rig_takes_the_key_colour_and_the_lamps_standing_around_it()
    {
        // Without the lamps the arms are lit by the sun and nothing else: at noon that is right, and at
        // night it is a black key over a nearly black ambient, so the weapon stays a silhouette while
        // the player walks under a lit fitting.
        Span<float> block = stackalloc float[ForwardPushBlock.Floats];

        ForwardDrawState state = Rig with { LampLight = new Vector3f(0.1f, 0.2f, 0.3f) };

        ForwardPushBlock.Write(block, state, Matrix4f.Identity, BlendMode.Opaque, hasLightmap: false);

        Assert.Equal(state.Lighting.SunColour.X + 0.1f, block[23], 5);
        Assert.Equal(state.Lighting.SunColour.Y + 0.2f, block[27], 5);
        Assert.Equal(state.Lighting.SunColour.Z + 0.3f, block[31], 5);
    }

    [Fact]
    public void A_world_draw_leaves_the_key_slot_at_zero()
    {
        // That slot is the terrain flag the vertex stage reads; a sun tint there would morph the world.
        Span<float> block = stackalloc float[ForwardPushBlock.Floats];

        ForwardPushBlock.Write(block, World, Matrix4f.Identity, BlendMode.Opaque, hasLightmap: false);

        Assert.Equal(0f, block[23]);
    }

    [Fact]
    public void A_sub_mesh_with_a_lightmap_does_not_also_take_the_flat_model_light()
    {
        Span<float> block = stackalloc float[ForwardPushBlock.Floats];
        ForwardDrawState state = World with { ModelAmbient = new Vector3f(0.5f, 0.6f, 0.7f) };

        ForwardPushBlock.Write(block, state, Matrix4f.Identity, BlendMode.Opaque, hasLightmap: true);
        Assert.Equal([1f, 1f, 1f], block[24..27].ToArray());

        ForwardPushBlock.Write(block, state, Matrix4f.Identity, BlendMode.Opaque, hasLightmap: false);
        Assert.Equal([0.5f, 0.6f, 0.7f], block[24..27].ToArray());
    }

    [Fact]
    public void Terrain_takes_the_fade_ramp_and_the_offset_from_the_eye()
    {
        Span<float> block = stackalloc float[ForwardPushBlock.Floats];

        ForwardDrawState state = World with { DetailFade = new DetailFade(2f, 3f, 4f, 1f) };
        Matrix4f modelView = Matrix4f.CreateTranslation(10f, 20f, 30f);

        ForwardPushBlock.Write(block, state, modelView, BlendMode.Opaque, hasLightmap: false);

        Assert.Equal([2f, 3f, 4f, 1f], block[20..24].ToArray());
        Assert.Equal([10f, 20f, 30f], block[24..27].ToArray());
    }

    [Fact]
    public void The_rig_is_never_drawn_with_the_terrain_slide_even_when_one_is_set()
    {
        Span<float> block = stackalloc float[ForwardPushBlock.Floats];

        ForwardDrawState state = Rig with { DetailFade = new DetailFade(2f, 3f, 4f, 1f) };

        ForwardPushBlock.Write(block, state, Matrix4f.Identity, BlendMode.Opaque, hasLightmap: false);

        // The rotation rows, not the ramp: an identity model-view leaves the first at one.
        Assert.Equal(1f, block[20]);
    }

    [Fact]
    public void The_fog_rides_in_the_last_row_of_every_draw_that_is_not_the_lit_rig()
    {
        Span<float> block = stackalloc float[ForwardPushBlock.Floats];

        SceneFog fog = new(new Vector3f(0.4f, 0.5f, 0.6f), 0.002f);
        ForwardDrawState state = World with { Fog = fog };

        ForwardPushBlock.Write(block, state, Matrix4f.Identity, BlendMode.Opaque, hasLightmap: false);

        Assert.Equal([0.4f, 0.5f, 0.6f], block[28..31].ToArray());
        Assert.Equal(0.002f, block[31]);
    }

    [Fact]
    public void An_additive_pass_fogs_to_black_so_a_surface_is_fogged_once()
    {
        // sub_5144E0 sets FOGCOLOR per blend mode: a surface drawn as several passes must be fogged
        // once, not once per pass.
        Span<float> block = stackalloc float[ForwardPushBlock.Floats];

        ForwardDrawState state = World with { Fog = new SceneFog(new Vector3f(0.4f, 0.5f, 0.6f), 0.002f) };

        ForwardPushBlock.Write(block, state, Matrix4f.Identity, BlendMode.Additive, hasLightmap: false);

        Assert.Equal([0f, 0f, 0f], block[28..31].ToArray());
    }

    [Fact]
    public void A_block_of_the_wrong_length_is_refused()
    {
        Assert.Throws<ArgumentException>(() =>
        {
            float[] block = new float[16];
            ForwardPushBlock.Write(block, World, Matrix4f.Identity, BlendMode.Opaque, hasLightmap: false);
        });
    }

    private static ForwardDrawState World => new(
        Matrix4f.Identity,
        SceneLighting.Baked,
        SceneFog.None,
        FogTarget.Scene,
        DetailFade.None,
        Vector3f.One,
        Vector3f.Zero,
        LightmapScale: 1f,
        PoseBlend: 0f,
        Exposure: 1f,
        LampLight: Vector3f.Zero,
        FirstPerson: false);

    private static ForwardDrawState Rig => World with
    {
        Lighting = SceneLighting.Baked with
        {
            Model = LightingModel.Dynamic,
            SunColour = new Vector3f(1f, 0.9f, 0.8f),
        },
        FirstPerson = true,
    };
}
