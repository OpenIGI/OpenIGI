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

/// <summary>The bomb blast's volume draw (<see cref="BlastVolumePipelineCache"/>): its fireball, its column and its shock front.</summary>
/// <remarks>
/// <para>
/// Each blast breaks the forward pass for two copies -- the depth as it stands, which the march stops each ray at, and the
/// lit image as it stands, which the shock front bends and the smoke covers -- and draws its box over the lit image. A
/// copy per blast rather than per frame, so a second blast in the same frame sees the first one drawn.
/// </para>
/// <para>
/// Only into a scene capture's forward pass. The direct path has neither a depth to copy nor a linear lit image to draw
/// into; there the blast is the original's rocket fire, which every path draws.
/// </para>
/// </remarks>
public sealed partial class GraphicsDevice
{
    private IRhiBindGroupLayout? _blastLayout;
    private BlastVolumePipelineCache? _blastPipelines;
    private ShaderBindings? _blastBindings;
    private ComputeParameters? _blastParameters;
    private IRhiSampler? _blastSampler;

    // Which of the frame's parameter slots the next blast takes, and the frame they were counted in.
    private int _blastSlot;
    private ulong _blastSlotFrame = ulong.MaxValue;

    /// <inheritdoc/>
    public void DrawBlastVolume(MeshHandle mesh, int indexOffset, int indexCount, in Matrix4f modelView, in BlastVolumeLook look)
    {
        ThrowIfDisposed();
        if (indexCount <= 0 || DrawingShadow || CapturingCards || !Capturing || CurrentPass is not ScenePass.Forward
            || _scene is not { } scene)
        {
            return;
        }

        EnsureBlastVolume();
        if (_blastPipelines is not { } pipelines
            || _blastBindings is not { } bindings
            || _blastParameters is not { } parameters
            || _blastSampler is not { } sampler)
        {
            return;
        }

        if (_blastSlotFrame != _frameCounter)
        {
            _blastSlotFrame = _frameCounter;
            _blastSlot = 0;
        }

        if (_blastSlot >= BlastVolumePipelineCache.BlastSlots)
        {
            return;
        }

        int slot = _blastSlot++;

        // What the blast reads: the depth and the lit image as they stand, before it.
        CloseScenePass();
        ClosePass();
        Encoder().CopyTextureToTexture(
            new RhiTextureRegion(scene[SceneTargetId.Depth]),
            new RhiTextureRegion(scene[SceneTargetId.BlastDepth]),
            new RhiExtent3D(scene.Width, scene.Height));
        Encoder().CopyTextureToTexture(
            new RhiTextureRegion(scene[SceneTargetId.Lit]),
            new RhiTextureRegion(scene[SceneTargetId.RainBackdrop]),
            new RhiExtent3D(scene.Width, scene.Height));

        // The frame's parameters, with this blast in its slot. Written whole each blast: a second blast's write carries the
        // first's slot too, so either draw reads the same block.
        Span<float> block = stackalloc float[BlastVolumePipelineCache.ParameterFloats];
        WriteBlastParameters(block, scene, slot, look);
        bindings.Bind("p", parameters.Write<float>(FrameSlot, block));
        bindings.Bind("uBlastDepth", scene.View(SceneTargetId.BlastDepth));
        bindings.Bind("uBackdrop", scene.View(SceneTargetId.RainBackdrop), sampler);
        IRhiBindGroup group = bindings.GroupFor(1);

        BeginForwardPass(scene);
        if (_scenePass is not { } pass)
        {
            return;
        }

        Span<float> push = stackalloc float[BlastVolumePipelineCache.PushDataFloats];
        modelView.CopyColumnMajorTo(push);
        push[16] = slot;
        push[17] = 1f / MathF.Max(_sceneExposure, 1e-3f);
        push[18] = look.Steps;
        push[19] = _frameCounter % 64;

        MeshDrawCall call = Call(mesh, indexOffset, indexCount, TextureHandle.None, TextureHandle.None, BlendMode.AlphaBlend);
        _meshes.Draw(pass, pipelines.Pipeline, call, MemoryMarshal.AsBytes(push), _frameCounter, group);
    }

    // The blasts' block: the projection, the view's axes, the depth's unprojection, the screen, the sun and the sky, and the
    // blasts. The slots other than this one keep what the last blast of the frame wrote there.
    private void WriteBlastParameters(Span<float> block, SceneTargetSet scene, int slot, in BlastVolumeLook look)
    {
        _blastBlock ??= new float[BlastVolumePipelineCache.ParameterFloats];
        float[] kept = _blastBlock;

        _projection.CopyColumnMajorTo(kept.AsSpan(0, 16));
        int at = 16;
        Row(kept, ref at, _viewToWorld.M11, _viewToWorld.M12, _viewToWorld.M13, 0f);
        Row(kept, ref at, _viewToWorld.M21, _viewToWorld.M22, _viewToWorld.M23, 0f);
        Row(kept, ref at, _viewToWorld.M31, _viewToWorld.M32, _viewToWorld.M33, 0f);
        Row(kept, ref at, _unprojection.XScale, _unprojection.YScale, _unprojection.Near, _unprojection.Far);
        Row(kept, ref at, scene.Width, scene.Height, 1f / Math.Max(1, scene.Width), 1f / Math.Max(1, scene.Height));

        Vector3f sun = _lighting.SunDirection;
        Vector3f sunColour = _lighting.SunColour * _lighting.Scale;
        Vector3f ambient = _lighting.AmbientColour * _lighting.Scale;
        Row(kept, ref at, sun.X, sun.Y, sun.Z, 1f);
        Row(kept, ref at, sunColour.X, sunColour.Y, sunColour.Z, 0f);
        Row(kept, ref at, ambient.X, ambient.Y, ambient.Z, 0f);

        at += slot * 16;
        float half = look.BoxHalfSize;
        Row(kept, ref at, look.CentreView.X, look.CentreView.Y, look.CentreView.Z, half);
        Row(kept, ref at, look.AgeSeconds, look.Seed % 65521u, look.FireballRadiusMetres, look.Heat);
        Row(kept, ref at, look.ColumnHeightMetres, look.SmokeDensity, look.ShockRadiusMetres, look.ShockStrength);
        Row(kept, ref at, look.FireLight.X, look.FireLight.Y, look.FireLight.Z, look.FireGlow);

        kept.AsSpan().CopyTo(block);
    }

    private static void Row(float[] block, ref int at, float x, float y, float z, float w)
    {
        block[at] = x;
        block[at + 1] = y;
        block[at + 2] = z;
        block[at + 3] = w;
        at += 4;
    }

    private float[]? _blastBlock;

    /// <summary>Builds the blast's pipeline and its own group, once.</summary>
    private void EnsureBlastVolume()
    {
        if (_blastPipelines is not null)
        {
            return;
        }

        ShaderReflection reflection = ShaderCatalog.GetReflection(ShaderModuleId.BlastVolume);
        _blastLayout = _device.CreateBindGroupLayout(new RhiBindGroupLayoutDescriptor(
            ShaderBindingLayout.EntriesFor(reflection, RhiShaderStages.Graphics, group: 1),
            "blast"));
        _blastPipelines = new BlastVolumePipelineCache(
            _device,
            _materialLayout,
            _blastLayout,
            SceneTargetSet.FormatOf(SceneTargetId.Lit),
            SceneTargetSet.FormatOf(SceneTargetId.Depth));
        _blastBindings = new ShaderBindings(_device, reflection, [_materialLayout, _blastLayout], RhiShaderStages.Graphics, "blast");
        _blastParameters = new ComputeParameters(_device, BlastVolumePipelineCache.ParameterFloats * sizeof(float), FramesInFlight, "blast");
        _blastSampler = _device.CreateSampler(new RhiSamplerDescriptor(Label: "blast.backdrop"));
    }

    private void DisposeBlastVolume()
    {
        _blastSampler?.Dispose();
        _blastSampler = null;
        _blastParameters?.Dispose();
        _blastParameters = null;
        _blastBindings?.Dispose();
        _blastBindings = null;
        _blastPipelines?.Dispose();
        _blastPipelines = null;
        _blastLayout?.Dispose();
        _blastLayout = null;
    }
}
