using OpenIGI.Rendering.Mesh3D;
using OpenIGI.Rendering.Rhi;
using OpenIGI.Shaders;

namespace OpenIGI.Rendering.Pipeline.Mesh3D;

/// <summary>The raindrops' draw: the scene behind each streak, refracted through it. One pipeline per culling mode.</summary>
/// <remarks>
/// <para>
/// Premultiplied over the lit image -- one and one-minus-source-alpha -- so a streak replaces the scene behind it
/// by exactly its coverage with that same scene, bent. Its ALPHA is added rather than blended: the lit image's
/// alpha is one wherever the frame was shaded, and what a drop adds on top is the reactive mask the temporal
/// resolve reads to keep the rain from trailing.
/// </para>
/// <para>
/// Tested against the scene's depth and never writing it, as every translucency is: a drop that wrote depth
/// would hide the smoke and the tracers behind it.
/// </para>
/// </remarks>
public sealed class RainPipelineCache : IDisposable
{
    /// <summary>How many bytes of push data the rain shader reads: its transform.</summary>
    public const int PushDataBytes = sizeof(float) * 16;

    /// <summary>How many floats its per-frame block holds: fog, screen and lens, one vector each.</summary>
    public const int ParameterFloats = 12;

    private readonly IRhiDevice _device;
    private readonly RhiTextureFormat _targetFormat;
    private readonly RhiTextureFormat _depthFormat;
    private readonly IRhiShaderModule _module;
    private readonly Dictionary<CullMode, IRhiRenderPipeline> _pipelines = [];
    private bool _disposed;

    /// <summary>Initialises a new instance of the <see cref="RainPipelineCache"/> class.</summary>
    /// <param name="device">The device the pipelines live on.</param>
    /// <param name="materialLayout">The material group, shared with the other mesh paths.</param>
    /// <param name="rainLayout">The rain's own group: the scene behind and the block.</param>
    /// <param name="targetFormat">The lit image's format.</param>
    /// <param name="depthFormat">The scene depth's format.</param>
    public RainPipelineCache(
        IRhiDevice device,
        IRhiBindGroupLayout materialLayout,
        IRhiBindGroupLayout rainLayout,
        RhiTextureFormat targetFormat,
        RhiTextureFormat depthFormat)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(materialLayout);
        ArgumentNullException.ThrowIfNull(rainLayout);

        _device = device;
        _targetFormat = targetFormat;
        _depthFormat = depthFormat;
        Reflection = ShaderCatalog.GetReflection(ShaderModuleId.Rain);
        _module = device.CreateShaderModule(new RhiShaderModuleDescriptor(ShaderModuleId.Rain, Label: nameof(ShaderModuleId.Rain)));

        Layout = device.CreatePipelineLayout(new RhiPipelineLayoutDescriptor(
            [materialLayout, rainLayout],
            PushDataBytes,
            RhiShaderStages.Vertex | RhiShaderStages.Fragment,
            nameof(ShaderModuleId.Rain)));
    }

    /// <summary>Gets the pipeline layout every variant shares.</summary>
    public IRhiPipelineLayout Layout { get; }

    /// <summary>Gets the reflection of the module, whose second group the caller builds.</summary>
    public ShaderReflection Reflection { get; }

    /// <summary>Gets the pipeline for a culling mode, building it on first use.</summary>
    /// <param name="cull">Which facing the draw culls.</param>
    /// <returns>The pipeline.</returns>
    public IRhiRenderPipeline ForCull(CullMode cull)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!_pipelines.TryGetValue(cull, out IRhiRenderPipeline? pipeline))
        {
            pipeline = Create(cull);
            _pipelines[cull] = pipeline;
        }

        return pipeline;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        foreach (IRhiRenderPipeline pipeline in _pipelines.Values)
        {
            pipeline.Dispose();
        }

        _pipelines.Clear();
        Layout.Dispose();
        _module.Dispose();
    }

    private IRhiRenderPipeline Create(CullMode cull)
    {
        IReadOnlyDictionary<int, RhiConstant> vertex = ShaderConstants.Declared(Reflection, Mesh3DConstants.TargetConstants);

        // The sheet is an sRGB image; only its alpha is read, which no decode touches.
        IReadOnlyDictionary<int, RhiConstant> fragment = ShaderConstants.Declared(
            Reflection,
            new Dictionary<int, RhiConstant>
            {
                [Mesh3DConstants.ClipYDown] = RhiConstant.Of(0),
                [Mesh3DConstants.SamplerDecodes] = RhiConstant.Of(1),
            });

        // Premultiplied colour over the scene; alpha added, for the reactive mask.
        RhiBlendState over = new(
            new RhiBlendComponent(RhiBlendFactor.One, RhiBlendFactor.OneMinusSourceAlpha, RhiBlendOperation.Add),
            new RhiBlendComponent(RhiBlendFactor.One, RhiBlendFactor.One, RhiBlendOperation.Add));

        // Reverse-Z: tested with greater-or-equal against the scene's depth, never written.
        RhiDepthStencilState depth = new(_depthFormat, DepthWrite: false, RhiCompare.GreaterEqual);

        return _device.CreateRenderPipeline(new RhiRenderPipelineDescriptor(
            Layout,
            new RhiShaderStage(_module, Reflection.EntryPointFor(ShaderStage.Vertex).Name, vertex),
            new RhiShaderStage(_module, Reflection.EntryPointFor(ShaderStage.Fragment).Name, fragment),
            GlassPipelineCache.VertexLayouts(),
            [new RhiColorTargetState(_targetFormat, over)],
            depth,
            new RhiPrimitiveState(RhiPrimitiveTopology.TriangleList, Mesh3DPipelineCache.CullFor(cull)),
            SampleCount: 1,
            $"Rain-{cull}"));
    }
}
