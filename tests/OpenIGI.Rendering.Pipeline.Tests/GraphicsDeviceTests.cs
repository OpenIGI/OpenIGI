using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Rendering.Mesh3D;
using OpenIGI.Rendering.Rhi;
using OpenIGI.Shaders;
using OpenIGI.TestSupport.Rhi;
using Xunit;

namespace OpenIGI.Rendering.Pipeline.Tests;

/// <summary>
/// That the renderer the game talks to draws the frame it says it draws.
/// </summary>
/// <remarks>
/// Asserted against the recording device, which is what makes the frame's SHAPE checkable: that a
/// clear becomes the next pass's clear rather than a command, that a 2D draw and a 3D draw land in one
/// pass, and that a frame which drew nothing presents nothing.
/// </remarks>
public sealed class GraphicsDeviceTests
{
    private static readonly Bgra32 White = new(255, 255, 255, 255);

    [Fact]
    public void It_offers_the_deferred_frame_and_names_what_it_does_not_do()
    {
        // The question everything above asks before drawing the remaster's frame. The passes that have
        // not moved onto this path are named rather than drawn wrongly.
        using RecordingRhiDevice rhi = new();
        using GraphicsDevice device = Open(rhi);

        Assert.True(device.Capabilities.SupportsSceneCapture);

        // The recording device takes SPIR-V, which every module of the GI stack has.
        Assert.True(device.Capabilities.SupportsCompute);
        Assert.False(device.Capabilities.SupportsRayQuery);

        // What needs an adapter that traces is named on one that does not; what this path draws is not.
        Assert.NotEmpty(device.Capabilities.UnimplementedSettings);
        Assert.Contains("r_rt_shadows", device.Capabilities.UnimplementedSettings);
        Assert.Contains("r_rt_reflections", device.Capabilities.UnimplementedSettings);
        Assert.DoesNotContain("r_motion_blur", device.Capabilities.UnimplementedSettings);
        Assert.DoesNotContain("r_fog_quality", device.Capabilities.UnimplementedSettings);

        // What the Vulkan renderer does not act on either is named here too, so the two options screens
        // agree -- depth of field above all, which neither draws.
        Assert.Contains("r_dof", device.Capabilities.UnimplementedSettings);
        Assert.Contains("r_generate_mipmaps", device.Capabilities.UnimplementedSettings);
        Assert.Contains("r_camera_taps", device.Capabilities.UnimplementedSettings);
    }

    [Fact]
    public void Fog_is_lit_and_integrated_before_the_shading_reads_it()
    {
        using RecordingRhiDevice rhi = new();
        using GraphicsDevice device = Open(rhi);
        device.SetVolumetricFog(new VolumetricFogSettings(Density: 0.014f), Vector3f.Zero, []);

        Assert.True(device.BeginSceneCapture(new SceneTargetDescription(64, 64, Deferred: true)));
        device.BeginPass(ScenePass.Geometry);
        World(device);
        device.EndPass();
        device.EndSceneCapture();
        device.SwapBuffers();

        List<string> order = [.. rhi.Commands.Select(command => command switch
        {
            RecordedRhiCommand.BeginRenderPass pass => pass.Label,
            RecordedRhiCommand.BeginComputePass pass => pass.Label,
            RecordedRhiCommand.SetPipeline pipeline => pipeline.Label,
            _ => string.Empty,
        }).Where(label => label.Length > 0)];

        // Scattered, then integrated, both before the shading samples the result.
        int scatter = order.FindIndex(label => label.Contains("FogScatter", StringComparison.Ordinal));
        int integrate = order.FindIndex(label => label.Contains("FogIntegrate", StringComparison.Ordinal));
        Assert.True(scatter >= 0 && integrate > scatter, string.Join(", ", order));
        Assert.True(integrate < order.IndexOf("shading"), string.Join(", ", order));
    }

    [Fact]
    public void No_fog_means_no_fog_dispatch()
    {
        using RecordingRhiDevice rhi = new();
        using GraphicsDevice device = Open(rhi);
        device.SetVolumetricFog(VolumetricFogSettings.Off, Vector3f.Zero, []);

        Assert.True(device.BeginSceneCapture(new SceneTargetDescription(64, 64, Deferred: true)));
        device.BeginPass(ScenePass.Geometry);
        World(device);
        device.EndPass();
        device.EndSceneCapture();
        device.SwapBuffers();

        Assert.DoesNotContain(rhi.CommandsOf<RecordedRhiCommand.BeginComputePass>(), pass => pass.Label == "fog");
    }

    [Theory]
    [InlineData(ShaderTarget.Spirv)]
    [InlineData(ShaderTarget.Wgsl)]
    public void It_offers_compute_where_the_whole_GI_stack_can_run(ShaderTarget target)
    {
        // Every module of the stack has both forms in software; the device says so on both backends, and
        // the check behind it is what would say no the day one of them lost its WGSL form.
        using RecordingRhiDevice rhi = new() { ShaderTarget = target };
        using GraphicsDevice device = Open(rhi);

        Assert.True(GraphicsDevice.GiRunsOn(rhi));
        Assert.True(device.Capabilities.SupportsCompute);
        Assert.DoesNotContain("r_surface_cache", device.Capabilities.UnimplementedSettings);
        Assert.DoesNotContain("r_screen_gi", device.Capabilities.UnimplementedSettings);
    }

    [Theory]
    [InlineData(ShaderTarget.Spirv)]
    [InlineData(ShaderTarget.Wgsl)]
    public void The_temporal_resolve_runs_on_every_target(ShaderTarget target)
    {
        // Every module of the chain has a WGSL form, so WebGPU resolves the jitter the game adds as
        // Vulkan does. Without it the jitter reaches the screen, and every pixel moves every frame.
        using RecordingRhiDevice rhi = new() { ShaderTarget = target };
        using GraphicsDevice device = Open(rhi);

        Assert.True(Deferred.TemporalResolve.RunsOn(rhi));
        Assert.DoesNotContain("r_taa", device.Capabilities.UnimplementedSettings);
        Assert.True(device.BeginSceneCapture(new SceneTargetDescription(64, 64, Deferred: true)));
    }

    [Fact]
    public void A_capture_that_is_not_deferred_is_declined()
    {
        // Which is how the game falls back to the direct path rather than being handed a frame this
        // cannot finish: there is no forward HDR path here, only the deferred one.
        using RecordingRhiDevice rhi = new();
        using GraphicsDevice device = Open(rhi);

        Assert.False(device.BeginSceneCapture(default));
        Assert.False(device.BeginSceneCapture(new SceneTargetDescription(64, 64)));
    }

    [Fact]
    public void A_deferred_capture_is_accepted()
    {
        using RecordingRhiDevice rhi = new();
        using GraphicsDevice device = Open(rhi);

        Assert.True(device.BeginSceneCapture(new SceneTargetDescription(64, 64, Deferred: true)));
        device.EndSceneCapture();
    }

    [Fact]
    public void A_frame_that_drew_nothing_presents_nothing()
    {
        // Opening a frame only to close it would be a blank flash rather than the held picture.
        using RecordingRhiDevice rhi = new();
        using GraphicsDevice device = Open(rhi);

        device.SwapBuffers();

        Assert.Empty(rhi.CommandsOf<RecordedRhiCommand.BeginFrame>());
    }

    [Fact]
    public void A_clear_becomes_the_clear_of_the_pass_that_follows_it()
    {
        using RecordingRhiDevice rhi = new();
        using GraphicsDevice device = Open(rhi);

        device.Clear(new Bgra32(0, 0, 0, 255));
        Sprite(device);
        device.SwapBuffers();

        RecordedRhiCommand.BeginRenderPass pass =
            Assert.Single(rhi.CommandsOf<RecordedRhiCommand.BeginRenderPass>());

        Assert.Equal("present", pass.Label);
        Assert.Equal([true], pass.Clears);
    }

    [Fact]
    public void Everything_a_frame_draws_lands_in_one_pass()
    {
        // A pass per draw would be correct and ruinous; the direct path has exactly one.
        using RecordingRhiDevice rhi = new();
        using GraphicsDevice device = Open(rhi);

        device.Clear(new Bgra32(0, 0, 0, 255));
        Sprite(device);
        World(device);
        Sprite(device);
        device.SwapBuffers();

        Assert.Single(rhi.CommandsOf<RecordedRhiCommand.BeginRenderPass>());
        Assert.Equal(3, rhi.CommandsOf<RecordedRhiCommand.Draw>().Count);
    }

    [Fact]
    public void A_clear_partway_through_a_frame_starts_a_second_pass()
    {
        // The only shape the interface has: a pass states what happens to its attachments as it begins.
        using RecordingRhiDevice rhi = new();
        using GraphicsDevice device = Open(rhi);

        Sprite(device);
        device.Clear(new Bgra32(0, 0, 0, 255));
        Sprite(device);
        device.SwapBuffers();

        IReadOnlyList<RecordedRhiCommand.BeginRenderPass> passes =
            rhi.CommandsOf<RecordedRhiCommand.BeginRenderPass>();

        Assert.Equal(2, passes.Count);
        Assert.Equal([false], passes[0].Clears);
        Assert.Equal([true], passes[1].Clears);
    }

    [Fact]
    public void A_frame_begins_and_ends_around_what_it_drew()
    {
        using RecordingRhiDevice rhi = new();
        using GraphicsDevice device = Open(rhi);

        Sprite(device);
        device.SwapBuffers();

        Assert.Single(rhi.CommandsOf<RecordedRhiCommand.BeginFrame>());
        Assert.Single(rhi.CommandsOf<RecordedRhiCommand.EndFrame>());
        Assert.Single(rhi.CommandsOf<RecordedRhiCommand.Submit>());
    }

    [Fact]
    public void Successive_frames_alternate_the_resources_they_write()
    {
        using RecordingRhiDevice rhi = new();
        using GraphicsDevice device = Open(rhi);

        List<int> slots = [];

        for (int i = 0; i < 4; i++)
        {
            Sprite(device);
            device.SwapBuffers();
        }

        foreach (RecordedRhiCommand.BeginFrame frame in rhi.CommandsOf<RecordedRhiCommand.BeginFrame>())
        {
            slots.Add(frame.FrameInFlight);
        }

        Assert.Equal([0, 1, 0, 1], slots);
    }

    [Fact]
    public void A_destroyed_texture_takes_the_groups_that_named_it()
    {
        // Or the next frame binds a view that is gone.
        using RecordingRhiDevice rhi = new();
        using GraphicsDevice device = Open(rhi);

        TextureHandle texture = device.CreateTexture(new TextureDescription(2, 2), Pixels(4));

        Sprite(device, texture);
        device.SwapBuffers();

        RecordedBindGroup group = rhi.Resources
            .OfType<RecordedBindGroup>()
            .Single(g => g.Label == $"texture-{texture.Id}");

        device.DestroyTexture(texture);

        // Not yet: the frame that named it may still be in flight, and destroying a resource a recorded
        // command buffer holds invalidates that buffer and silently voids everything recorded after it.
        Assert.False(group.IsDisposed);

        for (int frame = 0; frame < GraphicsDevice.FramesInFlight; frame++)
        {
            Sprite(device);
            device.SwapBuffers();
        }

        Assert.True(group.IsDisposed);
    }

    [Fact]
    public void A_mesh_is_drawn_through_the_world_pipeline()
    {
        using RecordingRhiDevice rhi = new();
        using GraphicsDevice device = Open(rhi);

        World(device);
        device.SwapBuffers();

        RecordedRhiCommand.SetPipeline pipeline =
            Assert.Single(rhi.CommandsOf<RecordedRhiCommand.SetPipeline>());

        Assert.StartsWith("Mesh3D-", pipeline.Label, StringComparison.Ordinal);
    }

    [Fact]
    public void The_depth_buffer_follows_the_mode()
    {
        using RecordingRhiDevice rhi = new();
        using GraphicsDevice device = Open(rhi);

        World(device);
        device.SwapBuffers();

        RecordedTexture depth = rhi.Resources.OfType<RecordedTexture>().Single(t => t.Label == "present.depth");
        Assert.Equal(640, depth.Width);

        device.SetMode(new DisplayMode(1280, 720, 32, 60));
        World(device);
        device.SwapBuffers();

        Assert.Equal(1280, rhi.Resources.OfType<RecordedTexture>().Last(t => t.Label == "present.depth").Width);

        // The outgrown one is held back until the frames that drew into it have finished, for the same
        // reason a destroyed texture is.
        Assert.False(depth.IsDisposed);

        for (int frame = 0; frame < GraphicsDevice.FramesInFlight; frame++)
        {
            World(device);
            device.SwapBuffers();
        }

        Assert.True(depth.IsDisposed);
    }

    [Fact]
    public void Reaching_for_the_remaster_stack_says_which_member_and_why()
    {
        // What has not moved onto this device refuses by name rather than handing back a handle nothing
        // could ever honour.
        using RecordingRhiDevice rhi = new();
        using GraphicsDevice device = Open(rhi);

        GraphicsDeviceException refused =
            Assert.Throws<GraphicsDeviceException>(() => device.CreateCubeTexture(64, "c"));

        Assert.Contains(nameof(IGraphicsDevice.CreateCubeTexture), refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_occlusion_query_nothing_answers_reads_as_visible()
    {
        // A false would hide geometry that is there; a true costs a draw that would have happened.
        using RecordingRhiDevice rhi = new();
        using GraphicsDevice device = Open(rhi);

        Assert.True(device.OcclusionVisible(device.CreateOcclusionQuery()));
    }

    [Fact]
    public void The_ground_draws_into_the_G_buffer_with_its_slide_and_its_detail()
    {
        // The terrain's passes are weighted by a level-of-detail slide the vertex stage computes, and its
        // detail pass multiplies its map in rather than writing it as light. Both arrive as device state,
        // and the geometry pass drew every page without either: each additive pass at full weight, so the
        // ground came out as the SUM of its materials.
        using RecordingRhiDevice rhi = new();
        using GraphicsDevice device = Open(rhi);

        Assert.True(device.BeginSceneCapture(new SceneTargetDescription(64, 64, Deferred: true)));
        device.BeginPass(ScenePass.Geometry);

        device.SetDetailFade(0.25f, 0.5f, 2f, fadeAlpha: true);
        World(device);

        device.ClearDetailFade();
        device.SetLightmapModulates(true);
        World(device);

        device.EndPass();
        device.EndSceneCapture();
        device.SwapBuffers();

        float[][] pushed = GeometryPushes(rhi);
        Assert.Equal(2, pushed.Length);

        float[] slide = pushed[0];
        float[] detail = pushed[1];

        // The slide's scale, offset, slope and mode, in the slots the vertex stage reads them from.
        Assert.Equal([0.25f, 0.5f, 2f, 2f], slide[16..20]);

        // Three: the lightmap slot is the ground's detail map.
        Assert.Equal(GBufferPushBlock.GroundDetailMode, detail[19]);
    }

    [Fact]
    public void The_forward_pass_draws_over_the_lit_image_after_the_light()
    {
        // The view model, the muzzle flash and every translucent draw: after the shading, over the lit
        // image it produced, against the depth the geometry wrote. They were dropped outright, which is a
        // game with no weapon in hand.
        using RecordingRhiDevice rhi = new();
        using GraphicsDevice device = Open(rhi);

        Assert.True(device.BeginSceneCapture(new SceneTargetDescription(64, 64, Deferred: true)));

        device.BeginPass(ScenePass.Geometry);
        World(device);
        device.EndPass();

        device.BeginPass(ScenePass.Forward);
        World(device);

        // A pane, which goes to the glazing's own pipeline: drawn as ordinary translucency it would be a
        // window that looks finished and is not.
        device.SetMaterialOverride(new SurfaceMaterial(Glass: true));
        World(device);
        device.SetMaterialOverride(SurfaceMaterial.Default);

        device.EndPass();
        device.EndSceneCapture();
        device.SwapBuffers();

        List<string> passes = [.. rhi.CommandsOf<RecordedRhiCommand.BeginRenderPass>().Select(pass => pass.Label)];
        Assert.True(passes.IndexOf("shading") < passes.IndexOf("forward"), string.Join(", ", passes));

        RecordedRhiCommand.BeginRenderPass forward = rhi.CommandsOf<RecordedRhiCommand.BeginRenderPass>()
            .Single(pass => pass.Label == "forward");

        // Loaded, not cleared: the translucency goes over the shaded scene.
        Assert.Equal(["Lit"], forward.ColorAttachments);
        Assert.Equal([false], forward.Clears);
        Assert.Equal("Depth", forward.DepthAttachment);

        Assert.Equal(2, DrawsIn(rhi, "forward"));
        Assert.Contains(
            rhi.CommandsOf<RecordedRhiCommand.SetPipeline>(),
            set => set.Label.StartsWith("Glass-", StringComparison.Ordinal));
    }

    [Fact]
    public void Panes_draw_a_front_layer_whose_reflection_is_traced_and_added_after_the_translucency()
    {
        // The game's sequence: geometry, the panes into their own layer, then the forward pass. What a
        // pane reflects is traced from that layer once the frame is lit and its translucency drawn, and
        // added to the lit image before anything temporal reads it.
        using RecordingRhiDevice rhi = new();
        using GraphicsDevice device = Open(rhi);

        Assert.True(device.BeginSceneCapture(new SceneTargetDescription(64, 64, Deferred: true)));

        device.BeginPass(ScenePass.Geometry);
        World(device);
        device.EndPass();

        device.BeginPass(ScenePass.GlassLayer);
        World(device);
        device.SetMaterialOverride(new SurfaceMaterial(Glass: true));
        World(device);
        device.SetMaterialOverride(SurfaceMaterial.Default);
        device.EndPass();

        device.BeginPass(ScenePass.Forward);
        device.SetMaterialOverride(new SurfaceMaterial(Glass: true));
        World(device);
        device.SetMaterialOverride(SurfaceMaterial.Default);
        device.EndPass();

        device.EndSceneCapture();
        device.SwapBuffers();

        RecordedRhiCommand.BeginRenderPass layer = rhi.CommandsOf<RecordedRhiCommand.BeginRenderPass>()
            .Single(pass => pass.Label == "glazing.layer");

        // Its own colour and depth, both cleared; and only the pane was drawn into it.
        Assert.Equal(["GlassLayer"], layer.ColorAttachments);
        Assert.Equal([true], layer.Clears);
        Assert.Equal("GlassDepth", layer.DepthAttachment);
        Assert.Equal(1, DrawsIn(rhi, "glazing.layer"));

        List<string> order = [.. rhi.Commands.Select(command => command switch
        {
            RecordedRhiCommand.BeginRenderPass pass => pass.Label,
            RecordedRhiCommand.BeginComputePass pass => pass.Label,
            _ => string.Empty,
        }).Where(label => label.Length > 0)];

        Assert.True(order.IndexOf("glazing.layer") < order.IndexOf("shading"), string.Join(", ", order));
        Assert.True(order.IndexOf("forward") < order.IndexOf("glazing.reflect"), string.Join(", ", order));
        Assert.True(order.IndexOf("glazing.reflect") < order.IndexOf("glazing.composite"), string.Join(", ", order));

        RecordedRhiCommand.BeginRenderPass composite = rhi.CommandsOf<RecordedRhiCommand.BeginRenderPass>()
            .Single(pass => pass.Label == "glazing.composite");

        // Added over the lit image rather than clearing it.
        Assert.Equal(["Lit"], composite.ColorAttachments);
        Assert.Equal([false], composite.Clears);
    }

    [Fact]
    public void A_frame_with_no_panes_traces_nothing()
    {
        using RecordingRhiDevice rhi = new();
        using GraphicsDevice device = Open(rhi);

        Assert.True(device.BeginSceneCapture(new SceneTargetDescription(64, 64, Deferred: true)));

        device.BeginPass(ScenePass.Geometry);
        World(device);
        device.EndPass();

        device.BeginPass(ScenePass.GlassLayer);
        World(device);
        device.EndPass();

        device.EndSceneCapture();
        device.SwapBuffers();

        Assert.DoesNotContain(rhi.CommandsOf<RecordedRhiCommand.BeginComputePass>(), pass => pass.Label == "glazing.reflect");
        Assert.DoesNotContain(rhi.CommandsOf<RecordedRhiCommand.BeginRenderPass>(), pass => pass.Label == "glazing.composite");
    }

    [Fact]
    public void A_cascade_draws_what_is_solid_into_its_own_layer()
    {
        // One layer of a depth array per cascade, cleared, and only what is solid casts: a blended draw
        // through the cut-out test casts the shadow of a chequerboard.
        using RecordingRhiDevice rhi = new();
        using GraphicsDevice device = Open(rhi);

        Assert.DoesNotContain("r_shadows", device.Capabilities.UnimplementedSettings);

        device.SetShadows(
            new ShadowSettings(CascadeCount: 2, Resolution: 256),
            [Cascade(10f), Cascade(40f)],
            new DepthUnprojection(1f, 1f, 0.1f, 1000f),
            Matrix3f.Identity);

        Assert.True(device.BeginShadowCascade(1));
        World(device);
        WorldBlended(device);
        device.EndShadowCascade();

        Assert.False(device.BeginShadowCascade(2), "A cascade past the count was begun.");

        device.SwapBuffers();

        RecordedRhiCommand.BeginRenderPass cascade = rhi.CommandsOf<RecordedRhiCommand.BeginRenderPass>()
            .Single(pass => pass.Label == "cascade1");

        Assert.Empty(cascade.ColorAttachments);
        Assert.Equal("shadow-map.1", cascade.DepthAttachment);
        Assert.Equal(1, DrawsIn(rhi, "cascade1"));
    }

    private static ShadowCascade Cascade(float split) =>
        new(Matrix3f.Identity, Matrix4f.Identity, Vector3f.Zero, split, split, 0.1f);

    private static void WorldBlended(GraphicsDevice device)
    {
        MeshHandle mesh = device.CreateMesh(
            [
                new Vertex3D(new Vector3f(0f, 0f, 0f), Vector3f.UnitY, 0f, 0f),
                new Vertex3D(new Vector3f(1f, 0f, 0f), Vector3f.UnitY, 1f, 0f),
                new Vertex3D(new Vector3f(1f, 1f, 0f), Vector3f.UnitY, 1f, 1f),
            ],
            [0, 1, 2]);

        device.DrawMesh(mesh, 0, 3, Matrix4f.Identity, TextureHandle.None, TextureHandle.None, BlendMode.AlphaBlend);
    }

    /// <summary>How many draws were recorded inside the named render pass.</summary>
    private static int DrawsIn(RecordingRhiDevice rhi, string label)
    {
        int draws = 0;
        bool inside = false;

        foreach (RecordedRhiCommand command in rhi.Commands)
        {
            switch (command)
            {
                case RecordedRhiCommand.BeginRenderPass begun:
                    inside = begun.Label == label;
                    break;

                case RecordedRhiCommand.EndPass:
                    inside = false;
                    break;

                case RecordedRhiCommand.Draw when inside:
                    draws++;
                    break;
            }
        }

        return draws;
    }

    /// <summary>The blocks the frame's geometry draws pushed, in order, as floats.</summary>
    private static float[][] GeometryPushes(RecordingRhiDevice rhi) =>
    [
        .. rhi.CommandsOf<RecordedRhiCommand.SetPushData>()
            .Where(command => command.Bytes.Count == GBufferPushBlock.Floats * sizeof(float))
            .Select(command => System.Runtime.InteropServices.MemoryMarshal
                .Cast<byte, float>(command.Bytes.ToArray())
                .ToArray()),
    ];

    private static GraphicsDevice Open(RecordingRhiDevice rhi) =>
        new(rhi, new DisplayMode(640, 480, 32, 60));

    private static Bgra32[] Pixels(int count)
    {
        Bgra32[] pixels = new Bgra32[count];
        Array.Fill(pixels, White);
        return pixels;
    }

    private static void Sprite(GraphicsDevice device, TextureHandle texture = default)
    {
        Vertex2D[] quad =
        [
            new(0f, 0f, 0f, 0f, White),
            new(8f, 0f, 1f, 0f, White),
            new(8f, 8f, 1f, 1f, White),
            new(0f, 8f, 0f, 1f, White),
        ];

        device.DrawTriangles2D(quad, [0, 1, 2, 0, 2, 3], texture, BlendMode.AlphaBlend);
    }

    private static void World(GraphicsDevice device)
    {
        MeshHandle mesh = device.CreateMesh(
            [
                new Vertex3D(new Vector3f(0f, 0f, 0f), Vector3f.UnitY, 0f, 0f),
                new Vertex3D(new Vector3f(1f, 0f, 0f), Vector3f.UnitY, 1f, 0f),
                new Vertex3D(new Vector3f(1f, 1f, 0f), Vector3f.UnitY, 1f, 1f),
            ],
            [0, 1, 2]);

        device.SetDepthState(DepthState.TestAndWrite);
        device.DrawMesh(mesh, 0, 3, Matrix4f.Identity, TextureHandle.None, TextureHandle.None, BlendMode.Opaque);
    }
}
