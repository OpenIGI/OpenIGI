using System.Runtime.InteropServices;
using OpenIGI.Core.Numerics;
using OpenIGI.Rendering.Mesh3D;
using OpenIGI.Rendering.Pipeline.Compute;
using OpenIGI.Rendering.Pipeline.Mesh3D;
using OpenIGI.Rendering.Pipeline.Passes;
using OpenIGI.Rendering.Pipeline.Targets;
using OpenIGI.Rendering.Rhi;
using OpenIGI.Shaders;

namespace OpenIGI.Rendering.Pipeline;

/// <summary>The glazing's draws: what a window pane lets through, and the front layer it reflects from.</summary>
/// <remarks>
/// <para>
/// A pane is drawn in the forward pass, after the light, by a pipeline of its own: the transmission is
/// its tint premultiplied by its transparency, and the lit scene behind it shows through by exactly the
/// share it does not cover. See <see cref="GlassPipelineCache"/>.
/// </para>
/// <para>
/// Before that, when the game asks for it, the FRONT LAYER: every pane drawn again into a layer of its
/// own with a depth buffer of its own, recording the nearest pane's world normal and distance. What a
/// pane reflects is traced from that layer after the frame is lit; see the reflections.
/// </para>
/// </remarks>
public sealed partial class GraphicsDevice
{
    private IRhiBindGroupLayout? _glassLayout;
    private GlassPipelineCache? _glassPipelines;
    private GlassPipelineCache? _glassLayerPipelines;
    private ShaderBindings? _glassBindings;
    private StandInTextures? _glassStandIns;
    private ComputeParameters? _glassParameters;
    private IRhiBindGroup? _glassGroup;

    /// <summary>Builds the glazing's pipelines and its own group, once, with the rest of the deferred frame.</summary>
    private void EnsureGlazing()
    {
        if (_glassPipelines is not null)
        {
            return;
        }

        // The front layer's reflection rather than the transmission's, though both declare the same
        // second group: only the layer READS the scene's depth, so only its form says what that slot is --
        // a depth texture, which a layout describing it as a colour one would refuse to bind.
        ShaderReflection reflection = ShaderCatalog.GetReflection(ShaderModuleId.GlassLayer);

        // The second group, as the module declares it: the scene's depth, which only the front layer
        // reads, and the fog the transmission is seen through.
        _glassLayout = _device.CreateBindGroupLayout(new RhiBindGroupLayoutDescriptor(
            ShaderBindingLayout.EntriesFor(reflection, RhiShaderStages.Graphics, group: 1),
            "glass"));

        _glassPipelines = new GlassPipelineCache(
            _device,
            _materialLayout,
            _glassLayout,
            SceneTargetSet.FormatOf(SceneTargetId.Lit),
            SceneTargetSet.FormatOf(SceneTargetId.Depth));

        // The same groups: the two modules share the vertex stage and declare the same second set.
        _glassLayerPipelines = new GlassPipelineCache(
            _device,
            _materialLayout,
            _glassLayout,
            SceneTargetSet.FormatOf(SceneTargetId.GlassLayer),
            SceneTargetSet.FormatOf(SceneTargetId.GlassDepth),
            frontLayer: true);

        _glassBindings = new ShaderBindings(_device, reflection, [_materialLayout, _glassLayout], RhiShaderStages.Graphics, "glass");
        _glassStandIns = new StandInTextures(_device, "glass.standin");
        _glassParameters = new ComputeParameters(
            _device,
            GlassPipelineCache.ParameterFloats * sizeof(float),
            FramesInFlight,
            "glass");
    }

    /// <summary>Settles the glazing's group for the forward pass about to open, once rather than per pane.</summary>
    private void PrepareGlazing() => _glassGroup = GlassGroup(sceneDepth: null);

    /// <summary>Opens the front layer's pass, whose draws record the nearest pane at each pixel.</summary>
    /// <param name="scene">The frame's targets.</param>
    /// <param name="resume">Whether this picks the pass up again after a flush, keeping what it drew.</param>
    /// <remarks>
    /// Its own colour and depth, both cleared: nought alpha is no pane, and a reverse-Z depth of nought is
    /// as far as a pane can be. The scene's depth is READ here, in the shader, which is how a pane behind
    /// a wall is kept out of the layer -- the pass's own depth only settles pane against pane.
    /// </remarks>
    private void BeginGlassLayerPass(SceneTargetSet scene, bool resume = false)
    {
        ClosePass();

        _glassGroup = GlassGroup(scene.View(SceneTargetId.Depth));
        RhiLoadOp load = resume ? RhiLoadOp.Load : RhiLoadOp.Clear;

        _scenePass = Encoder().BeginRenderPass(new RhiRenderPassDescriptor(
            [new RhiColorAttachment(scene.View(SceneTargetId.GlassLayer), load, RhiStoreOp.Store, RhiClearColor.Transparent)],
            new RhiDepthAttachment(scene.View(SceneTargetId.GlassDepth), load, RhiStoreOp.Store, DepthClearValue: 0f),
            Label: "glazing.layer"));

        ApplySceneViewport(_scenePass);
    }

    /// <summary>The glazing's group for this frame: the air it is fogged by, and the depth the front layer tests.</summary>
    /// <param name="sceneDepth">
    /// The scene's depth for the front layer, which reads it; null for the transmission, which does not
    /// and cannot -- its pass is testing against that very depth, and a texture cannot be both.
    /// </param>
    private IRhiBindGroup? GlassGroup(IRhiTextureView? sceneDepth)
    {
        if (_glassBindings is not { } bindings || _glassStandIns is not { } standIns || _glassParameters is not { } parameters)
        {
            return null;
        }

        Vector3f fog = _fog.ColourFor(BlendMode.AlphaBlend);
        Span<float> block = [fog.X, fog.Y, fog.Z, _fog.DensityPerMetre];

        ShaderBinding depthSlot = _glassLayerPipelines!.Reflection.Bindings.First(
            binding => string.Equals(binding.Name, "uSceneDepth", StringComparison.Ordinal));

        bindings.Bind("p", parameters.Write<float>(FrameSlot, block));
        bindings.Bind("uSceneDepth", sceneDepth ?? standIns.For(depthSlot));

        return bindings.GroupFor(1);
    }

    /// <summary>Records one pane over the lit image.</summary>
    /// <param name="pass">The forward pass.</param>
    /// <param name="call">What to draw.</param>
    /// <param name="ranges">Which parts of it, or empty for the call's own range.</param>
    /// <param name="modelView">Its transform into view space.</param>
    private void DrawGlass(IRhiRenderPassEncoder pass, in MeshDrawCall call, ReadOnlySpan<MeshDrawRange> ranges, in Matrix4f modelView)
    {
        if (_glassPipelines is not { } pipelines || _glassGroup is not { } group)
        {
            return;
        }

        Span<float> push = stackalloc float[GlassPipelineCache.PushDataBytes / sizeof(float)];
        WriteGlassPush(push, modelView);

        IRhiRenderPipeline pipeline = pipelines.ForCull(_cullMode);
        DrawGlassWith(pass, pipeline, group, call, ranges, push);
    }

    /// <summary>Records a draw into the front layer, if it is a pane; the layer holds nothing else.</summary>
    private void DrawIntoGlassLayer(in MeshDrawCall call, ReadOnlySpan<MeshDrawRange> ranges, in Matrix4f modelView)
    {
        if (_material.Glass)
        {
            DrawGlassLayer(call, ranges, modelView);
        }
    }

    /// <summary>Records one pane into the front layer.</summary>
    /// <param name="call">What to draw.</param>
    /// <param name="ranges">Which parts of it, or empty for the call's own range.</param>
    /// <param name="modelView">Its transform into view space.</param>
    private void DrawGlassLayer(in MeshDrawCall call, ReadOnlySpan<MeshDrawRange> ranges, in Matrix4f modelView)
    {
        if (_scenePass is not { } pass || _glassLayerPipelines is not { } pipelines || _glassGroup is not { } group)
        {
            return;
        }

        // Counted here, where every pane the transmission will draw is seen once.
        _glassPanes++;

        Span<float> push = stackalloc float[GlassPipelineCache.PushDataBytes / sizeof(float)];
        WriteGlassPush(push, modelView);
        DrawGlassWith(pass, pipelines.ForCull(_cullMode), group, call, ranges, push);
    }

    /// <summary>Records a pane through one of the glazing's pipelines.</summary>
    private void DrawGlassWith(
        IRhiRenderPassEncoder pass,
        IRhiRenderPipeline pipeline,
        IRhiBindGroup group,
        in MeshDrawCall call,
        ReadOnlySpan<MeshDrawRange> ranges,
        ReadOnlySpan<float> push)
    {
        if (ranges.IsEmpty)
        {
            _meshes.Draw(pass, pipeline, call, MemoryMarshal.AsBytes(push), _frameCounter, group);
        }
        else
        {
            _meshes.DrawRanges(pass, pipeline, call, ranges, MemoryMarshal.AsBytes(push), _frameCounter, group);
        }
    }

    /// <summary>The glazing's block: its transform, and the rotation that turns its normal into the world.</summary>
    private void WriteGlassPush(Span<float> push, in Matrix4f modelView)
    {
        push.Clear();
        (_projection * modelView).CopyColumnMajorTo(push[..16]);

        Matrix3f modelViewRotation = new(
            modelView.M11, modelView.M12, modelView.M13,
            modelView.M21, modelView.M22, modelView.M23,
            modelView.M31, modelView.M32, modelView.M33);

        Matrix3f normal = _viewToWorld * modelViewRotation;

        push[16] = normal.M11;
        push[17] = normal.M21;
        push[18] = normal.M31;
        push[20] = normal.M12;
        push[21] = normal.M22;
        push[22] = normal.M32;
        push[24] = normal.M13;
        push[25] = normal.M23;
        push[26] = normal.M33;
    }

    private void DisposeGlazing()
    {
        _glassGroup = null;
        _glassParameters?.Dispose();
        _glassParameters = null;
        _glassStandIns?.Dispose();
        _glassStandIns = null;
        _glassBindings?.Dispose();
        _glassBindings = null;
        _glassPipelines?.Dispose();
        _glassPipelines = null;
        _glassLayerPipelines?.Dispose();
        _glassLayerPipelines = null;
        _glassLayout?.Dispose();
        _glassLayout = null;
    }
}
