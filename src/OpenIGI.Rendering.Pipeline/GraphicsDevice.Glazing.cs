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

/// <summary>The glazing's transmission: what a window pane lets through, laid over the lit scene.</summary>
/// <remarks>
/// <para>
/// A pane is drawn in the forward pass, after the light, by a pipeline of its own: the transmission is
/// its tint premultiplied by its transparency, and the lit scene behind it shows through by exactly the
/// share it does not cover. See <see cref="GlassPipelineCache"/>.
/// </para>
/// <para>
/// What a pane REFLECTS is not here. That is its own pass over the front layer of the glazing and the
/// surface cache, and it arrives with the surface cache; until then a window is the room behind it,
/// tinted, which is what the Vulkan original draws with reflections switched off.
/// </para>
/// </remarks>
public sealed partial class GraphicsDevice
{
    private IRhiBindGroupLayout? _glassLayout;
    private GlassPipelineCache? _glassPipelines;
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

        ShaderReflection reflection = ShaderCatalog.GetReflection(ShaderModuleId.Glass);

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

        _glassBindings = new ShaderBindings(_device, reflection, [_materialLayout, _glassLayout], RhiShaderStages.Graphics, "glass");
        _glassStandIns = new StandInTextures(_device, "glass.standin");
        _glassParameters = new ComputeParameters(
            _device,
            GlassPipelineCache.ParameterFloats * sizeof(float),
            FramesInFlight,
            "glass");
    }

    /// <summary>Settles the glazing's group for the forward pass about to open, once rather than per pane.</summary>
    private void PrepareGlazing() => _glassGroup = GlassGroup();

    /// <summary>The glazing's group for this frame: the air it is fogged by.</summary>
    /// <remarks>
    /// The fog the level blends its translucency towards, and its density. The scene depth the group
    /// also declares is read only by the front layer, which has not moved onto this path, so it holds a
    /// stand-in: the forward pass is testing against that very depth, and a texture cannot be both.
    /// </remarks>
    private IRhiBindGroup? GlassGroup()
    {
        if (_glassBindings is not { } bindings || _glassStandIns is not { } standIns || _glassParameters is not { } parameters)
        {
            return null;
        }

        Vector3f fog = _fog.ColourFor(BlendMode.AlphaBlend);
        Span<float> block = [fog.X, fog.Y, fog.Z, _fog.DensityPerMetre];

        bindings.Bind("p", parameters.Write<float>(FrameSlot, block));
        bindings.BindRemainingTextures(standIns.For);

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
        _glassLayout?.Dispose();
        _glassLayout = null;
    }
}
