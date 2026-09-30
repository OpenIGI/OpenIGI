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

/// <summary>The raindrops' draw: clear water that bends the scene behind it rather than a white streak.</summary>
/// <remarks>
/// <para>
/// A drop drawn as light of its own reads as a white scratch, whatever colour it is given: real rain is
/// transparent, and what a streak shows is the scene behind it, refracted. So the first drop of a frame
/// breaks the forward pass for one copy of the lit image as it stands -- every opaque surface lit, the
/// translucency before the storm drawn -- and every drop after samples that copy through its own lens. See
/// <see cref="RainPipelineCache"/>.
/// </para>
/// <para>
/// A copy rather than the lit image itself because the drops are drawn INTO the lit image: a pass cannot
/// read what it is writing.
/// </para>
/// </remarks>
public sealed partial class GraphicsDevice
{
    // How many streaks the drop sheet lays side by side; see the storm's sheet.
    private const float RainSheetCells = 10f;

    // How far the lens bends the view, in streak widths either way: a cylinder of water flips what is
    // behind it across its axis, which is a little over the streak's own width.
    private const float RainBend = 1.4f;

    // How much of the sky the lens gathers into a drop, against the scene straight behind it, and how much
    // the rims concentrate the light behind them.
    private const float RainSkyGather = 0.8f;
    private const float RainRimGather = 0.5f;

    private IRhiBindGroupLayout? _rainLayout;
    private RainPipelineCache? _rainPipelines;
    private ShaderBindings? _rainBindings;
    private ComputeParameters? _rainParameters;
    private IRhiSampler? _rainSampler;
    private IRhiBindGroup? _rainGroup;
    private ulong _rainBackdropFrame = ulong.MaxValue;

    /// <summary>Builds the rain's pipelines and its own group, once, with the rest of the deferred frame.</summary>
    private void EnsureRain()
    {
        if (_rainPipelines is not null)
        {
            return;
        }

        ShaderReflection reflection = ShaderCatalog.GetReflection(ShaderModuleId.Rain);

        _rainLayout = _device.CreateBindGroupLayout(new RhiBindGroupLayoutDescriptor(
            ShaderBindingLayout.EntriesFor(reflection, RhiShaderStages.Graphics, group: 1),
            "rain"));

        _rainPipelines = new RainPipelineCache(
            _device,
            _materialLayout,
            _rainLayout,
            SceneTargetSet.FormatOf(SceneTargetId.Lit),
            SceneTargetSet.FormatOf(SceneTargetId.Depth));

        _rainBindings = new ShaderBindings(_device, reflection, [_materialLayout, _rainLayout], RhiShaderStages.Graphics, "rain");
        _rainParameters = new ComputeParameters(_device, RainPipelineCache.ParameterFloats * sizeof(float), FramesInFlight, "rain");
        _rainSampler = _device.CreateSampler(new RhiSamplerDescriptor(Label: "rain.backdrop"));
    }

    /// <summary>Records one batch of drops over the lit image, refracting the scene behind them.</summary>
    /// <param name="call">What to draw.</param>
    /// <param name="ranges">Which parts of it, or empty for the call's own range.</param>
    /// <param name="modelView">Its transform into view space.</param>
    private void DrawRain(in MeshDrawCall call, ReadOnlySpan<MeshDrawRange> ranges, in Matrix4f modelView)
    {
        if (_scene is not { } scene
            || _rainPipelines is not { } pipelines
            || _rainBindings is not { } bindings
            || _rainParameters is not { } parameters
            || _rainSampler is not { } sampler)
        {
            return;
        }

        // Once a frame: what is behind the rain, before any of it is drawn.
        if (_rainBackdropFrame != _frameCounter || _rainGroup is null)
        {
            CloseScenePass();
            ClosePass();

            Encoder().CopyTextureToTexture(
                new RhiTextureRegion(scene[SceneTargetId.Lit]),
                new RhiTextureRegion(scene[SceneTargetId.RainBackdrop]),
                new RhiExtent3D(scene.Width, scene.Height));

            Vector3f fog = _fog.ColourFor(BlendMode.AlphaBlend);
            Span<float> block =
            [
                fog.X, fog.Y, fog.Z, _fog.DensityPerMetre,
                scene.Width, scene.Height, 1f / Math.Max(1, scene.Width), 1f / Math.Max(1, scene.Height),
                RainBend, RainSkyGather, RainSheetCells, RainRimGather,
            ];

            bindings.Bind("p", parameters.Write<float>(FrameSlot, block));
            bindings.Bind("uBackdrop", scene.View(SceneTargetId.RainBackdrop), sampler);
            _rainGroup = bindings.GroupFor(1);
            _rainBackdropFrame = _frameCounter;

            BeginForwardPass(scene);
        }

        if (_scenePass is not { } pass)
        {
            return;
        }

        Span<float> push = stackalloc float[RainPipelineCache.PushDataBytes / sizeof(float)];
        (_projection * modelView).CopyColumnMajorTo(push);

        IRhiRenderPipeline pipeline = pipelines.ForCull(_cullMode);

        if (ranges.IsEmpty)
        {
            _meshes.Draw(pass, pipeline, call, MemoryMarshal.AsBytes(push), _frameCounter, _rainGroup);
        }
        else
        {
            _meshes.DrawRanges(pass, pipeline, call, ranges, MemoryMarshal.AsBytes(push), _frameCounter, _rainGroup);
        }
    }

    private void DisposeRain()
    {
        _rainGroup = null;
        _rainSampler?.Dispose();
        _rainSampler = null;
        _rainParameters?.Dispose();
        _rainParameters = null;
        _rainBindings?.Dispose();
        _rainBindings = null;
        _rainPipelines?.Dispose();
        _rainPipelines = null;
        _rainLayout?.Dispose();
        _rainLayout = null;
    }
}
