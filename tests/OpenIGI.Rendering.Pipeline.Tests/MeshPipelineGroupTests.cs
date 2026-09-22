using OpenIGI.Rendering.Pipeline.Mesh3D;
using OpenIGI.Rendering.Rhi;
using OpenIGI.TestSupport.Rhi;
using Xunit;

namespace OpenIGI.Rendering.Pipeline.Tests;

/// <summary>
/// That the four mesh pipelines agree about the things they have to agree about.
/// </summary>
/// <remarks>
/// They move together because they share a bind group layout: a group is built per texture triple and
/// used by whichever pipeline draws it, so one of them wanting a different shape would mean a second
/// group per triple. Sharing it means they can also disagree, which is what these check.
/// </remarks>
public sealed class MeshPipelineGroupTests
{
    private static readonly RhiTextureFormat[] GBufferFormats =
    [
        RhiTextureFormat.Rgba8Unorm,
        RhiTextureFormat.Rgba16Float,
        RhiTextureFormat.Rgba8Unorm,
        RhiTextureFormat.Rgba16Float,
    ];

    [Fact]
    public void All_four_build_against_one_material_layout()
    {
        using RecordingRhiDevice device = new();
        using Group group = new(device);

        // One layout, four pipeline layouts built from it: the count is the assertion, because a second
        // material layout would mean a second group per texture triple in every frame.
        Assert.Single(device.Resources.OfType<RecordedBindGroupLayout>(), l => l.Label == "mesh3d-material");
    }

    [Fact]
    public void The_shadow_pass_can_be_asked_for_the_same_cut_out_as_the_geometry_pass()
    {
        // A pass that cuts on less than the G-buffer keeps casters the frame never draws; one that cuts
        // on more drops shadows from things you can see.
        using RecordingRhiDevice device = new();
        using Group group = new(device);

        const byte Reference = 200;

        group.GBuffer.ForState(DepthState.TestAndWrite, CullMode.CounterClockwise, alphaReference: Reference);
        group.Shadows.ForAlphaReference(Reference);

        uint expected = Mesh3DConstants.Bits(Reference / 255f);

        foreach (RecordedRenderPipeline pipeline in device.Resources
            .OfType<RecordedRenderPipeline>()
            .Where(p => p.Label.StartsWith("GBuffer", StringComparison.Ordinal)
                || p.Label.StartsWith("Shadow", StringComparison.Ordinal)))
        {
            Assert.Equal(expected, pipeline.Descriptor.Fragment!.Constants![Mesh3DConstants.AlphaReference]);
        }
    }

    [Fact]
    public void A_geometry_draw_writes_every_target_outright()
    {
        IReadOnlyList<RhiColorTargetState> targets =
            GBufferPipelineCache.TargetsFor(BlendMode.Opaque, GBufferFormats);

        Assert.Equal(4, targets.Count);
        Assert.All(targets, target =>
        {
            Assert.Null(target.Blend);
            Assert.Equal(RhiColorWrite.All, target.Write);
        });
    }

    [Fact]
    public void An_additive_material_pass_adds_into_the_base_colour_and_touches_nothing_else()
    {
        IReadOnlyList<RhiColorTargetState> targets =
            GBufferPipelineCache.TargetsFor(BlendMode.Additive, GBufferFormats);

        RhiColorTargetState baseColour = targets[GBufferPipelineCache.BaseColourTarget];

        Assert.Equal(RhiBlendFactor.One, baseColour.Blend!.Value.Color.Source);
        Assert.Equal(RhiBlendFactor.One, baseColour.Blend.Value.Color.Destination);

        // Its colour and nothing else: not its own alpha, which the pass beneath it wrote.
        Assert.Equal(RhiColorWrite.Red | RhiColorWrite.Green | RhiColorWrite.Blue, baseColour.Write);

        for (int i = 0; i < targets.Count; i++)
        {
            if (i != GBufferPipelineCache.BaseColourTarget)
            {
                Assert.Equal(RhiColorWrite.None, targets[i].Write);
            }
        }
    }

    [Fact]
    public void A_modulated_bake_goes_to_the_baked_target_rather_than_the_albedo()
    {
        // The base colour attachment holds an albedo, and a bake multiplied into an albedo is lit a
        // second time by whatever lights the frame.
        IReadOnlyList<RhiColorTargetState> targets =
            GBufferPipelineCache.TargetsFor(BlendMode.Modulate2X, GBufferFormats);

        Assert.Equal(RhiColorWrite.None, targets[GBufferPipelineCache.BaseColourTarget].Write);

        RhiColorTargetState baked = targets[GBufferPipelineCache.BakedTarget];

        // src * dst + dst * src, which over a neutral destination is twice the bake -- the doubling
        // MODULATE2X means, falling out of the blend for free.
        Assert.Equal(RhiBlendFactor.Destination, baked.Blend!.Value.Color.Source);
        Assert.Equal(RhiBlendFactor.Source, baked.Blend.Value.Color.Destination);
    }

    [Fact]
    public void The_shadow_map_and_the_cards_are_drawn_the_ordinary_way_round_not_reverse_z()
    {
        using RecordingRhiDevice device = new();
        using Group group = new(device);

        group.Shadows.ForAlphaReference();

        RecordedRenderPipeline shadow = device.Resources
            .OfType<RecordedRenderPipeline>()
            .Single(p => p.Label.StartsWith("Shadow", StringComparison.Ordinal));

        Assert.Equal(RhiCompare.LessEqual, shadow.Descriptor.DepthStencil!.Value.DepthCompare);

        RecordedRenderPipeline card = device.Resources
            .OfType<RecordedRenderPipeline>()
            .Single(p => p.Label == "Card");

        Assert.Equal(RhiCompare.LessEqual, card.Descriptor.DepthStencil!.Value.DepthCompare);
    }

    [Fact]
    public void The_scene_is_drawn_reverse_z_while_they_are_not()
    {
        using RecordingRhiDevice device = new();
        using Group group = new(device);

        group.GBuffer.ForState(DepthState.TestAndWrite, CullMode.CounterClockwise);

        RecordedRenderPipeline geometry = device.Resources
            .OfType<RecordedRenderPipeline>()
            .Single(p => p.Label.StartsWith("GBuffer", StringComparison.Ordinal));

        Assert.Equal(RhiCompare.GreaterEqual, geometry.Descriptor.DepthStencil!.Value.DepthCompare);
    }

    [Fact]
    public void The_shadow_pass_biases_depth_rather_than_culling_a_facing()
    {
        // The world's meshes are not reliably wound one way, so culling cannot be relied on to keep a
        // lit surface from reading as its own shadow; the slope-scaled bias is what does.
        using RecordingRhiDevice device = new();
        using Group group = new(device);

        group.Shadows.ForAlphaReference();

        RecordedRenderPipeline shadow = device.Resources
            .OfType<RecordedRenderPipeline>()
            .Single(p => p.Label.StartsWith("Shadow", StringComparison.Ordinal));

        Assert.Equal(RhiCullMode.None, shadow.Descriptor.Primitive.CullMode);
        Assert.Equal(ShadowPipelineCache.DepthBiasConstant, shadow.Descriptor.DepthStencil!.Value.DepthBiasConstant);
        Assert.Equal(ShadowPipelineCache.DepthBiasSlopeScale, shadow.Descriptor.DepthStencil.Value.DepthBiasSlopeScale);
    }

    [Fact]
    public void A_shadow_draw_has_no_colour_target_at_all()
    {
        using RecordingRhiDevice device = new();
        using Group group = new(device);

        group.Shadows.ForAlphaReference();

        RecordedRenderPipeline shadow = device.Resources
            .OfType<RecordedRenderPipeline>()
            .Single(p => p.Label.StartsWith("Shadow", StringComparison.Ordinal));

        Assert.Empty(shadow.Descriptor.ColorTargets);
    }

    [Fact]
    public void Only_the_paths_that_can_move_read_a_previous_pose()
    {
        // A card is a photograph of a model where it stands, so there is nothing to form a delta with.
        Assert.Equal(2, Mesh3DPipelineCache.VertexLayouts().Count);
        Assert.Equal(2, GBufferPipelineCache.VertexLayouts().Count);
        Assert.Equal(2, ShadowPipelineCache.VertexLayouts().Count);
        Assert.Single(CardPipeline.VertexLayouts());
    }

    [Fact]
    public void Only_the_geometry_pass_reads_the_surface_normal()
    {
        // It is the pass that writes it; nothing else has a target to put it in.
        Assert.Contains(
            GBufferPipelineCache.VertexLayouts()[0].Attributes,
            a => a.Offset == Vertex3D.SurfaceNormalOffset);

        Assert.DoesNotContain(
            Mesh3DPipelineCache.VertexLayouts()[0].Attributes,
            a => a.Offset == Vertex3D.SurfaceNormalOffset);
    }

    [Fact]
    public void A_geometry_pass_that_is_given_the_wrong_number_of_targets_is_refused()
    {
        using RecordingRhiDevice device = new();
        using IRhiBindGroupLayout material = Mesh3DPipelineCache.CreateMaterialLayout(device);
        using IRhiBindGroupLayout surface = SurfaceLayout(device);

        Assert.Throws<ArgumentException>(() => new GBufferPipelineCache(
            device,
            material,
            surface,
            [RhiTextureFormat.Rgba8Unorm],
            RhiTextureFormat.Depth32Float));
    }

    private static IRhiBindGroupLayout SurfaceLayout(RecordingRhiDevice device) =>
        device.CreateBindGroupLayout(new RhiBindGroupLayoutDescriptor(
            [new RhiBindGroupLayoutEntry(0, RhiBindingType.UniformBuffer, RhiShaderStages.Graphics)],
            "surface"));

    /// <summary>The four mesh pipelines, built the way a frame builds them: over one material layout.</summary>
    private sealed class Group : IDisposable
    {
        private readonly IRhiBindGroupLayout _material;
        private readonly IRhiBindGroupLayout _surface;
        private readonly IRhiBindGroupLayout _cube;

        public Group(RecordingRhiDevice device)
        {
            _material = Mesh3DPipelineCache.CreateMaterialLayout(device);
            _surface = SurfaceLayout(device);

            _cube = Mesh3DPipelineCache.CreateCubeLayout(device);

            Forward = new Mesh3DPipelineCache(
                device,
                _material,
                _cube,
                RhiTextureFormat.Rgba16Float,
                RhiTextureFormat.Depth32Float);
            GBuffer = new GBufferPipelineCache(device, _material, _surface, GBufferFormats, RhiTextureFormat.Depth32Float);
            Shadows = new ShadowPipelineCache(device, _material, RhiTextureFormat.Depth32Float);
            Cards = new CardPipeline(device, _material, GBufferFormats, RhiTextureFormat.Depth32Float);
        }

        public Mesh3DPipelineCache Forward { get; }

        public GBufferPipelineCache GBuffer { get; }

        public ShadowPipelineCache Shadows { get; }

        public CardPipeline Cards { get; }

        public void Dispose()
        {
            Cards.Dispose();
            Shadows.Dispose();
            GBuffer.Dispose();
            Forward.Dispose();
            _cube.Dispose();
            _surface.Dispose();
            _material.Dispose();
        }
    }
}
