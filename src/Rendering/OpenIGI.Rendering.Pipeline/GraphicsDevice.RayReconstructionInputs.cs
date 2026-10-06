using System.Runtime.InteropServices;
using OpenIGI.Rendering.Pipeline.Mesh3D;
using OpenIGI.Rendering.Pipeline.Targets;
using OpenIGI.Rendering.Rhi;
using OpenIGI.Shaders;

namespace OpenIGI.Rendering.Pipeline;

/// <summary>
/// The two ray-reconstruction inputs the G-buffer cannot give: the first-person rig's guides, and the scene's
/// colour before anything transparent was drawn over it.
/// </summary>
/// <remarks>
/// <para>
/// The rig is drawn in the forward pass and never reaches the G-buffer, so the guides built from the G-buffer
/// describe the wall behind the hand. Each of its opaque draws is remembered as it is made -- the call, the
/// ranges, the block it carried -- and drawn again just before the evaluation through the rig's guide shader
/// (World/RigGuides.slang), into a base-colour and a normal image the guide pass reads where the rig stands.
/// Drawn through the same transform and the same first-person depth range, it lands exactly on the depth the
/// rig left, and the depth test keeps only what is visible.
/// </para>
/// <para>
/// The colour before transparency is the noisy opaque scene alone (Streamline's DLSS-RR guide, 4.1.11): it is
/// how the network tells a particle or a pane drawn over a surface from a change in that surface's own light,
/// which is what keeps smoke and sparks from ghosting. It is copied from the lit image at the first
/// transparent forward draw; the reflections, which are the opaque surfaces' own light but are laid over the
/// lit image after the forward pass, are laid over this copy too; and the rig, which is opaque but may be drawn
/// after the first transparent draw, is taken from the finished image by the guide pass. A frame that draws
/// nothing transparent copies the finished image at the evaluation, and the two are then the same.
/// </para>
/// </remarks>
public sealed partial class GraphicsDevice
{
    private readonly List<RigGuideDraw> _rigGuideDraws = [];
    private readonly Dictionary<(CullMode Cull, byte AlphaReference), IRhiRenderPipeline> _rigGuidePipelines = [];
    private ulong _rigGuideFrame = ulong.MaxValue;
    private IRhiShaderModule? _rigGuideModule;
    private IRhiTexture? _rigBaseColour;
    private IRhiTexture? _rigNormal;
    private IRhiTexture? _colourBeforeTransparency;
    private ulong _colourBeforeTransparencyFrame = ulong.MaxValue;

    /// <summary>Gets a value indicating whether this frame's colour before transparency has been taken.</summary>
    private bool ColourBeforeTransparencyTaken => _colourBeforeTransparencyFrame == _frameCounter;

    /// <summary>Remembers one of the rig's opaque draws, to draw again into its guides.</summary>
    /// <param name="call">The draw.</param>
    /// <param name="ranges">Its ranges, or empty for the call's own.</param>
    /// <param name="push">The block it carried.</param>
    /// <param name="environment">The group it bound at index one.</param>
    private void NoteRigGuideDraw(
        in MeshDrawCall call, ReadOnlySpan<MeshDrawRange> ranges, ReadOnlySpan<float> push, IRhiBindGroup environment)
    {
        if (!RawLighting || !_firstPerson || call.Blend != BlendMode.Opaque || _depthState != DepthState.TestAndWrite)
        {
            return;
        }

        if (_rigGuideFrame != _frameCounter)
        {
            _rigGuideDraws.Clear();
            _rigGuideFrame = _frameCounter;
        }

        _rigGuideDraws.Add(new RigGuideDraw(
            call,
            ranges.IsEmpty ? null : ranges.ToArray(),
            MemoryMarshal.AsBytes(push).ToArray(),
            environment));
    }

    /// <summary>Takes the colour before transparency, ahead of the frame's first transparent forward draw.</summary>
    /// <param name="scene">The frame's targets.</param>
    /// <param name="transparent">Whether the draw about to be made is transparent.</param>
    private void TakeColourBeforeTransparency(SceneTargetSet scene, bool transparent)
    {
        if (!RawLighting || !transparent || ColourBeforeTransparencyTaken)
        {
            return;
        }

        CloseScenePass();
        ClosePass();
        CopyColourBeforeTransparency(scene);
        BeginForwardPass(scene);
    }

    /// <summary>Copies the lit image into the colour before transparency, as it stands now.</summary>
    private void CopyColourBeforeTransparency(SceneTargetSet scene)
    {
        EnsureGuides(scene.Width, scene.Height);
        Encoder().CopyTextureToTexture(
            new RhiTextureRegion(scene[SceneTargetId.Lit]),
            new RhiTextureRegion(_colourBeforeTransparency!),
            new RhiExtent3D(scene.Width, scene.Height));
        _colourBeforeTransparencyFrame = _frameCounter;
    }

    /// <summary>
    /// Draws the rig's remembered draws into its guide images, and makes sure the colour before transparency
    /// exists for this frame.
    /// </summary>
    /// <param name="scene">The frame's targets.</param>
    /// <param name="encoder">The frame's encoder.</param>
    private void RecordRigGuides(SceneTargetSet scene, IRhiCommandEncoder encoder)
    {
        EnsureGuides(scene.Width, scene.Height);

        if (!ColourBeforeTransparencyTaken)
        {
            CopyColourBeforeTransparency(scene);
        }

        bool drawn = _rigGuideFrame == _frameCounter && _rigGuideDraws.Count > 0 && _forwardPipelines is not null;
        ReportOnChange("Ray reconstruction rig guides", drawn ? $"{_rigGuideDraws.Count} draw(s)" : "no rig in view");

        // Cleared either way: a frame with no rig must not read last frame's.
        using (IRhiRenderPassEncoder pass = encoder.BeginRenderPass(new RhiRenderPassDescriptor(
            [
                new RhiColorAttachment(_rigBaseColour!.View, RhiLoadOp.Clear, RhiStoreOp.Store, RhiClearColor.Transparent),
                new RhiColorAttachment(_rigNormal!.View, RhiLoadOp.Clear, RhiStoreOp.Store, RhiClearColor.Transparent),
            ],
            new RhiDepthAttachment(scene.View(SceneTargetId.Depth), RhiLoadOp.Load, RhiStoreOp.Store, DepthClearValue: 0f),
            _profiler.Time(RenderStage.TemporalResolve, "rig-guides"),
            Label: "rig-guides")))
        {
            if (drawn)
            {
                // The first-person layer's sliver of the depth range, as the rig was drawn into.
                pass.SetViewport(0, 0, scene.Width, scene.Height, Viewport.ForegroundDepthFloor, 1f);
                pass.SetScissor(0, 0, scene.Width, scene.Height);

                foreach (RigGuideDraw draw in _rigGuideDraws)
                {
                    IRhiRenderPipeline pipeline = RigGuidePipeline(draw.Call.Cull, draw.Call.AlphaReference);
                    if (draw.Ranges is { } ranges)
                    {
                        _meshes.DrawRanges(pass, pipeline, draw.Call, ranges, draw.Push, _frameCounter, draw.Environment);
                    }
                    else
                    {
                        _meshes.Draw(pass, pipeline, draw.Call, draw.Push, _frameCounter, draw.Environment);
                    }
                }
            }
        }

        _rigGuideDraws.Clear();
    }

    /// <summary>The rig guide pipeline for a draw's culling and cut-out.</summary>
    private IRhiRenderPipeline RigGuidePipeline(CullMode cull, byte alphaReference)
    {
        if (_rigGuidePipelines.TryGetValue((cull, alphaReference), out IRhiRenderPipeline? existing))
        {
            return existing;
        }

        ShaderReflection reflection = ShaderCatalog.GetReflection(ShaderModuleId.RigGuides);
        _rigGuideModule ??= _device.CreateShaderModule(new RhiShaderModuleDescriptor(ShaderModuleId.RigGuides, Label: "RigGuides"));

        // The rig's constants as the forward pass bakes them for the lit image: linear, decoded on sampling.
        IReadOnlyDictionary<int, RhiConstant> constants = ShaderConstants.Declared(
            reflection,
            Mesh3DConstants.Describe(
                alphaReference,
                reflective: false,
                default,
                0f,
                default,
                default,
                targetEncodes: false,
                linearScene: true));

        IRhiRenderPipeline pipeline = _device.CreateRenderPipeline(new RhiRenderPipelineDescriptor(
            _forwardPipelines!.Layout,
            new RhiShaderStage(_rigGuideModule, reflection.EntryPointFor(ShaderStage.Vertex).Name, constants),
            new RhiShaderStage(_rigGuideModule, reflection.EntryPointFor(ShaderStage.Fragment).Name, constants),
            Mesh3DPipelineCache.VertexLayouts(),
            [new RhiColorTargetState(RhiTextureFormat.Rgba16Float, null), new RhiColorTargetState(RhiTextureFormat.Rgba16Float, null)],
            Mesh3DPipelineCache.DepthStateFor(DepthState.TestOnly, SceneTargetSet.FormatOf(SceneTargetId.Depth)),
            new RhiPrimitiveState(RhiPrimitiveTopology.TriangleList, Mesh3DPipelineCache.CullFor(cull)),
            SampleCount: 1,
            $"RigGuides-{cull}-{alphaReference}"));

        _rigGuidePipelines[(cull, alphaReference)] = pipeline;
        return pipeline;
    }

    private void DisposeRayReconstructionInputs()
    {
        foreach (IRhiRenderPipeline pipeline in _rigGuidePipelines.Values)
        {
            pipeline.Dispose();
        }

        _rigGuidePipelines.Clear();
        _rigGuideModule?.Dispose();
        _rigGuideModule = null;
        _rigGuideDraws.Clear();
    }

    /// <summary>One of the rig's opaque draws, as it was made.</summary>
    /// <param name="Call">The draw.</param>
    /// <param name="Ranges">Its ranges, or null for the call's own.</param>
    /// <param name="Push">The block it carried.</param>
    /// <param name="Environment">The group it bound at index one.</param>
    private readonly record struct RigGuideDraw(MeshDrawCall Call, MeshDrawRange[]? Ranges, byte[] Push, IRhiBindGroup Environment);
}
