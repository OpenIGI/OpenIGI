using OpenIGI.Core.Numerics;
using OpenIGI.Rendering.Pipeline.Draw2D;
using OpenIGI.Rendering.Pipeline.Mesh3D;
using OpenIGI.Rendering.Rhi;
using OpenIGI.TestSupport.Rhi;
using Xunit;

namespace OpenIGI.Rendering.Pipeline.Tests;

/// <summary>
/// That the world pipeline builds the variants it is asked for, and no more.
/// </summary>
public sealed class Mesh3DPipelineCacheTests
{
    [Fact]
    public void A_variant_is_built_once_and_reused()
    {
        using RecordingRhiDevice device = new();
        using Mesh3DPipelineCache cache = Open(device);

        IRhiRenderPipeline first = cache.ForState(DepthState.TestAndWrite, CullMode.CounterClockwise, BlendMode.Opaque);
        IRhiRenderPipeline again = cache.ForState(DepthState.TestAndWrite, CullMode.CounterClockwise, BlendMode.Opaque);

        Assert.Same(first, again);
        Assert.Equal(1, cache.Count);
    }

    [Fact]
    public void Each_distinct_state_is_its_own_variant()
    {
        using RecordingRhiDevice device = new();
        using Mesh3DPipelineCache cache = Open(device);

        cache.ForState(DepthState.TestAndWrite, CullMode.CounterClockwise, BlendMode.Opaque);
        cache.ForState(DepthState.TestOnly, CullMode.CounterClockwise, BlendMode.Opaque);
        cache.ForState(DepthState.TestAndWrite, CullMode.None, BlendMode.Opaque);
        cache.ForState(DepthState.TestAndWrite, CullMode.CounterClockwise, BlendMode.AlphaBlend);
        cache.ForState(DepthState.TestAndWrite, CullMode.CounterClockwise, BlendMode.Opaque, alphaReference: 200);
        cache.ForState(DepthState.TestAndWrite, CullMode.CounterClockwise, BlendMode.Opaque, reflective: true);

        Assert.Equal(6, cache.Count);
    }

    [Fact]
    public void The_cut_out_threshold_and_the_reflection_switch_reach_the_shader_as_constants()
    {
        using RecordingRhiDevice device = new();
        using Mesh3DPipelineCache cache = Open(device);

        cache.ForState(DepthState.TestAndWrite, CullMode.None, BlendMode.Opaque, alphaReference: 200, reflective: true);

        RecordedRenderPipeline pipeline = Assert.Single(device.Resources.OfType<RecordedRenderPipeline>());
        IReadOnlyDictionary<int, RhiConstant> constants = pipeline.Descriptor.Fragment!.Constants!;

        Assert.Equal(RhiConstant.Of(200f / 255f), constants[Mesh3DConstants.AlphaReference]);
        Assert.Equal(RhiConstant.Of(1), constants[Mesh3DConstants.Reflective]);

        // Both stages, or the two disagree about whether the varying between them means anything.
        Assert.Equal(constants, pipeline.Descriptor.Vertex.Constants);
    }

    [Fact]
    public void A_variant_set_dropped_mid_frame_outlives_the_frames_that_may_have_bound_it()
    {
        // The menu loads its backdrop's mission, and with it the sun, while it goes on drawing: freed at
        // once, a pipeline the frame being recorded had bound invalidated that frame's commands, and
        // submitting them lost the device.
        using RecordingRhiDevice device = new();
        using RetiredResources retired = new(framesInFlight: 2) { Frame = 10 };
        using Mesh3DPipelineCache cache = new(
            device,
            Mesh3DPipelineCache.CreateMaterialLayout(device),
            Mesh3DPipelineCache.CreateCubeLayout(device),
            RhiTextureFormat.Rgba16Float,
            RhiTextureFormat.Depth32Float,
            retired);

        IRhiRenderPipeline bound = cache.ForState(DepthState.TestAndWrite, CullMode.None, BlendMode.Opaque);

        // Enough other suns that the first set is the one dropped.
        for (int sun = 1; sun <= 8; sun++)
        {
            cache.SetSun(new Vector3f(sun * 0.1f, 0f, 0f), Vector3f.One);
        }

        Assert.False(((RecordedRenderPipeline)bound).IsDisposed);

        retired.ReleaseBefore(12);

        Assert.True(((RecordedRenderPipeline)bound).IsDisposed);
    }

    [Fact]
    public void A_change_of_fog_keeps_the_variants_it_does_not_reach()
    {
        // The fog is push data. The retail path sets the terrain's and the objects' alternately within a
        // frame, and each of those rebuilt every variant while it was a reason to.
        using RecordingRhiDevice device = new();
        using Mesh3DPipelineCache cache = Open(device);

        IRhiRenderPipeline before = cache.ForState(DepthState.TestAndWrite, CullMode.None, BlendMode.Opaque);

        cache.SetFog(new Vector3f(0.4f, 0.4f, 0.5f), 0.002f);
        cache.SetFog(new Vector3f(0.2f, 0.3f, 0.3f), 0.004f);

        Assert.Same(before, cache.ForState(DepthState.TestAndWrite, CullMode.None, BlendMode.Opaque));
        Assert.Single(device.Resources.OfType<RecordedRenderPipeline>());
    }

    [Fact]
    public void A_new_sun_has_variants_of_its_own_and_going_back_reuses_the_first()
    {
        using RecordingRhiDevice device = new();
        using Mesh3DPipelineCache cache = Open(device);

        IRhiRenderPipeline day = cache.ForState(DepthState.TestAndWrite, CullMode.None, BlendMode.Opaque);

        cache.SetSun(new Vector3f(0.1f, 0.1f, 0.2f), new Vector3f(0.2f, 0.2f, 0.3f));
        Assert.Equal(0, cache.Count);
        IRhiRenderPipeline night = cache.ForState(DepthState.TestAndWrite, CullMode.None, BlendMode.Opaque);
        Assert.NotSame(day, night);

        cache.SetSun(Vector3f.Zero, Vector3f.Zero);

        Assert.Same(day, cache.ForState(DepthState.TestAndWrite, CullMode.None, BlendMode.Opaque));
        Assert.False(((RecordedRenderPipeline)day).IsDisposed);
    }

    [Fact]
    public void A_constant_the_module_no_longer_declares_is_not_supplied()
    {
        // The fog colour and density became push data and their constant identifiers stayed behind. An
        // identifier a module does not declare is ignored in silence by one backend and refused outright
        // by the other, so what is sent is kept to what the module says it takes.
        using RecordingRhiDevice device = new();
        using Mesh3DPipelineCache cache = Open(device);

        cache.SetFog(new Vector3f(0.4f, 0.4f, 0.5f), 0.002f);
        cache.ForState(DepthState.TestAndWrite, CullMode.None, BlendMode.Opaque);

        IReadOnlyDictionary<int, RhiConstant> constants =
            Assert.Single(device.Resources.OfType<RecordedRenderPipeline>()).Descriptor.Fragment!.Constants!;

        Assert.DoesNotContain(Mesh3DConstants.FogDensity, constants.Keys);
        Assert.DoesNotContain(Mesh3DConstants.FogColourRed, constants.Keys);

        // What it does declare still arrives.
        Assert.Contains(Mesh3DConstants.AlphaReference, constants.Keys);
        Assert.Contains(Mesh3DConstants.SunColourRed, constants.Keys);
    }

    [Fact]
    public void Setting_the_fog_to_what_it_already_is_keeps_the_variants()
    {
        using RecordingRhiDevice device = new();
        using Mesh3DPipelineCache cache = Open(device);

        cache.SetFog(new Vector3f(0.4f, 0.4f, 0.5f), 0.002f);
        IRhiRenderPipeline built = cache.ForState(DepthState.TestAndWrite, CullMode.None, BlendMode.Opaque);

        cache.SetFog(new Vector3f(0.4f, 0.4f, 0.5f), 0.002f);

        Assert.Same(built, cache.ForState(DepthState.TestAndWrite, CullMode.None, BlendMode.Opaque));
    }

    [Fact]
    public void Depth_is_tested_the_reverse_way_round()
    {
        // Reverse-Z: nearer geometry has the greater depth value, so the test keeps the greater one.
        RhiDepthStencilState? state =
            Mesh3DPipelineCache.DepthStateFor(DepthState.TestAndWrite, RhiTextureFormat.Depth32Float);

        Assert.Equal(RhiCompare.GreaterEqual, state!.Value.DepthCompare);
        Assert.True(state.Value.DepthWrite);

        Assert.False(Mesh3DPipelineCache
            .DepthStateFor(DepthState.TestOnly, RhiTextureFormat.Depth32Float)!.Value.DepthWrite);

        Assert.Equal(
            RhiCompare.Always,
            Mesh3DPipelineCache.DepthStateFor(DepthState.Disabled, RhiTextureFormat.Depth32Float)!.Value.DepthCompare);
    }

    [Fact]
    public void The_front_face_is_counter_clockwise_as_it_is_in_the_original()
    {
        Assert.Equal(RhiCullMode.Front, Mesh3DPipelineCache.CullFor(CullMode.Clockwise));
        Assert.Equal(RhiCullMode.Back, Mesh3DPipelineCache.CullFor(CullMode.CounterClockwise));
        Assert.Equal(RhiCullMode.None, Mesh3DPipelineCache.CullFor(CullMode.None));
    }

    [Fact]
    public void The_previous_pose_stream_carries_only_what_differs_between_poses()
    {
        IReadOnlyList<RhiVertexBufferLayout> layouts = Mesh3DPipelineCache.VertexLayouts();

        Assert.Equal(2, layouts.Count);
        Assert.Equal(5, layouts[0].Attributes.Count);
        Assert.Equal(2, layouts[1].Attributes.Count);

        // Its locations continue the first stream's rather than restarting.
        Assert.Equal([5, 6], layouts[1].Attributes.Select(a => a.Location));
    }

    [Fact]
    public void A_material_group_is_built_once_per_triple_and_released_with_any_of_them()
    {
        using RecordingRhiDevice device = new();
        using IRhiBindGroupLayout layout = Mesh3DPipelineCache.CreateMaterialLayout(device);
        using MaterialBindGroupCache materials = new(device, layout);

        FakeTextureBindingSource textures = new(device);
        RhiTextureBinding diffuse = textures.Resolve(textures.Add(1));
        RhiTextureBinding lightmap = textures.Resolve(textures.Add(2));
        RhiTextureBinding other = textures.Resolve(textures.Add(3));
        RhiTextureBinding detail = textures.Resolve(TextureHandle.None);

        IRhiBindGroup first = materials.Get(diffuse, lightmap, detail);

        Assert.Same(first, materials.Get(diffuse, lightmap, detail));
        Assert.NotSame(first, materials.Get(diffuse, other, detail));
        Assert.Equal(2, materials.Count);

        // The diffuse is in both triples, so both go.
        Assert.Equal(2, materials.Forget(1));
        Assert.Equal(0, materials.Count);
    }

    /// <summary>The forward cache with the layouts a frame would hand it.</summary>
    /// <remarks>
    /// The layouts leak for the length of a test, deliberately: what is under test is the variants, and
    /// a frame owns those layouts rather than any one cache.
    /// </remarks>
    private static Mesh3DPipelineCache Open(IRhiDevice device) => new(
        device,
        Mesh3DPipelineCache.CreateMaterialLayout(device),
        Mesh3DPipelineCache.CreateCubeLayout(device),
        RhiTextureFormat.Rgba16Float,
        RhiTextureFormat.Depth32Float);
}
