using OpenIGI.Rendering.Mesh3D;
using OpenIGI.Rendering.Rhi;
using OpenIGI.Shaders;

namespace OpenIGI.Rendering.Pipeline.Mesh3D;

/// <summary>The tracers' draw: one pipeline per <see cref="TracerStyle"/>, over one target.</summary>
/// <remarks>
/// <para>
/// The retail line blends its colour source-alpha over the scene, as the original's translucent primitive does
/// (<c>0x50E000</c>: <c>SRCALPHA</c>, <c>INVSRCALPHA</c>); the remastered streak adds its light. Both ADD their alpha:
/// the lit image's alpha is one wherever the frame was shaded, and what a tracer adds is the marker the temporal
/// resolve reads to keep an effect that lives for one tick out of its history.
/// </para>
/// <para>
/// Tested against the scene's depth and never writing it (<c>0x50DCC0</c>: <c>ZFUNC LESSEQUAL</c>, no z-write; reverse-Z
/// here, so greater-or-equal), and drawn from both sides: a strip is built to face the eye.
/// </para>
/// </remarks>
public sealed class TracerPipelineCache : IDisposable
{
    /// <summary>How many floats of push data the tracer shader reads: its transform and its style vector.</summary>
    public const int PushDataFloats = 20;

    /// <summary>How many bytes that is.</summary>
    public const int PushDataBytes = PushDataFloats * sizeof(float);

    private readonly IRhiDevice _device;
    private readonly RhiTextureFormat _targetFormat;
    private readonly RhiTextureFormat _depthFormat;
    private readonly IRhiShaderModule _module;
    private readonly Dictionary<TracerStyle, IRhiRenderPipeline> _pipelines = [];
    private bool _disposed;

    /// <summary>Initialises a new instance of the <see cref="TracerPipelineCache"/> class.</summary>
    /// <param name="device">The device the pipelines live on.</param>
    /// <param name="materialLayout">The material group every mesh path shares; a tracer binds it and samples none of it.</param>
    /// <param name="targetFormat">The format of the image the tracers are drawn into.</param>
    /// <param name="depthFormat">The format of the depth they are tested against.</param>
    public TracerPipelineCache(
        IRhiDevice device,
        IRhiBindGroupLayout materialLayout,
        RhiTextureFormat targetFormat,
        RhiTextureFormat depthFormat)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(materialLayout);

        _device = device;
        _targetFormat = targetFormat;
        _depthFormat = depthFormat;
        Reflection = ShaderCatalog.GetReflection(ShaderModuleId.Tracer);
        _module = device.CreateShaderModule(new RhiShaderModuleDescriptor(ShaderModuleId.Tracer, Label: nameof(ShaderModuleId.Tracer)));

        Layout = device.CreatePipelineLayout(new RhiPipelineLayoutDescriptor(
            [materialLayout],
            PushDataBytes,
            RhiShaderStages.Vertex | RhiShaderStages.Fragment,
            nameof(ShaderModuleId.Tracer)));
    }

    /// <summary>Gets the pipeline layout both styles share.</summary>
    public IRhiPipelineLayout Layout { get; }

    /// <summary>Gets the reflection of the module.</summary>
    public ShaderReflection Reflection { get; }

    /// <summary>Gets the pipeline for a style, building it on first use.</summary>
    /// <param name="style">The style.</param>
    /// <returns>The pipeline.</returns>
    public IRhiRenderPipeline For(TracerStyle style)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!_pipelines.TryGetValue(style, out IRhiRenderPipeline? pipeline))
        {
            pipeline = Create(style);
            _pipelines[style] = pipeline;
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

    private IRhiRenderPipeline Create(TracerStyle style)
    {
        IReadOnlyDictionary<int, RhiConstant> vertex = ShaderConstants.Declared(Reflection, Mesh3DConstants.TargetConstants);

        // A target that encodes as it stores is handed its colour decoded, so it is encoded once.
        IReadOnlyDictionary<int, RhiConstant> fragment = ShaderConstants.Declared(
            Reflection,
            new Dictionary<int, RhiConstant>
            {
                [Mesh3DConstants.ClipYDown] = RhiConstant.Of(0),
                [Mesh3DConstants.TargetEncodes] = RhiConstant.Of(_targetFormat.IsSrgb() ? 1 : 0),
            });

        RhiBlendComponent colour = style == TracerStyle.Retail
            ? new RhiBlendComponent(RhiBlendFactor.SourceAlpha, RhiBlendFactor.OneMinusSourceAlpha, RhiBlendOperation.Add)
            : new RhiBlendComponent(RhiBlendFactor.One, RhiBlendFactor.One, RhiBlendOperation.Add);
        RhiBlendState blend = new(colour, new RhiBlendComponent(RhiBlendFactor.One, RhiBlendFactor.One, RhiBlendOperation.Add));

        RhiDepthStencilState depth = new(_depthFormat, DepthWrite: false, RhiCompare.GreaterEqual);

        return _device.CreateRenderPipeline(new RhiRenderPipelineDescriptor(
            Layout,
            new RhiShaderStage(_module, Reflection.EntryPointFor(ShaderStage.Vertex).Name, vertex),
            new RhiShaderStage(_module, Reflection.EntryPointFor(ShaderStage.Fragment).Name, fragment),
            GlassPipelineCache.VertexLayouts(),
            [new RhiColorTargetState(_targetFormat, blend)],
            depth,
            new RhiPrimitiveState(RhiPrimitiveTopology.TriangleList, RhiCullMode.None),
            SampleCount: 1,
            $"Tracer-{style}"));
    }
}
