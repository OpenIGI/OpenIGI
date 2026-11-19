using OpenIGI.Rendering.Rhi;
using OpenIGI.Shaders;

namespace OpenIGI.Rendering.Pipeline.Mesh3D;

/// <summary>The infrared view's two body draws, one pipeline per <see cref="InfraredDraw"/>, over one target.</summary>
/// <remarks>
/// <para>
/// <c>Direct3DRender_DrawBoneMesh</c> under <c>isDrawInfra</c>: the body where it stands with blending off, its colour the ramp's;
/// then drawn nearer the eye with <c>SRCALPHA</c> and <c>INVSRCALPHA</c>, the ramp's alpha times the texture factor's the coverage.
/// The first leaves the target's alpha -- the frame's mark of world geometry -- as the body left it; the second ADDS its
/// coverage, as a tracer does, the marker that keeps an image with no motion of its own out of the temporal history.
/// </para>
/// <para>
/// Both are tested against the scene's depth and write none of it (reverse-Z here, so greater-or-equal): the body itself is
/// already in it, and what the see-through draw is nearer than is what it shows through. Both sides are drawn, as the world
/// draws a body.
/// </para>
/// </remarks>
public sealed class InfraredPipelineCache : IDisposable
{
    /// <summary>How many floats of push data the infrared shader reads: its transform, the view rows and its control vector.</summary>
    public const int PushDataFloats = 16 + 12 + 4;

    /// <summary>How many bytes that is.</summary>
    public const int PushDataBytes = PushDataFloats * sizeof(float);

    private readonly IRhiDevice _device;
    private readonly RhiTextureFormat _targetFormat;
    private readonly RhiTextureFormat _depthFormat;
    private readonly IRhiShaderModule _module;
    private readonly ShaderReflection _reflection;
    private readonly IRhiPipelineLayout _layout;
    private readonly Dictionary<InfraredDraw, IRhiRenderPipeline> _pipelines = [];
    private bool _disposed;

    /// <summary>Initialises a new instance of the <see cref="InfraredPipelineCache"/> class.</summary>
    /// <param name="device">The device the pipelines live on.</param>
    /// <param name="materialLayout">The material group every mesh path shares; the ramp is bound as its diffuse.</param>
    /// <param name="targetFormat">The format of the image the bodies are drawn into.</param>
    /// <param name="depthFormat">The format of the depth they are tested against.</param>
    public InfraredPipelineCache(
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
        _reflection = ShaderCatalog.GetReflection(ShaderModuleId.Infrared);
        _module = device.CreateShaderModule(new RhiShaderModuleDescriptor(ShaderModuleId.Infrared, Label: nameof(ShaderModuleId.Infrared)));
        _layout = device.CreatePipelineLayout(new RhiPipelineLayoutDescriptor(
            [materialLayout],
            PushDataBytes,
            RhiShaderStages.Vertex | RhiShaderStages.Fragment,
            nameof(ShaderModuleId.Infrared)));
    }

    /// <summary>Gets the pipeline for one of the draws, building it on first use.</summary>
    /// <param name="draw">The draw.</param>
    /// <returns>The pipeline.</returns>
    public IRhiRenderPipeline For(InfraredDraw draw)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!_pipelines.TryGetValue(draw, out IRhiRenderPipeline? pipeline))
        {
            pipeline = Create(draw);
            _pipelines[draw] = pipeline;
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
        _layout.Dispose();
        _module.Dispose();
    }

    private IRhiRenderPipeline Create(InfraredDraw draw)
    {
        IReadOnlyDictionary<int, RhiConstant> vertex = ShaderConstants.Declared(_reflection, Mesh3DConstants.TargetConstants);
        IReadOnlyDictionary<int, RhiConstant> fragment = ShaderConstants.Declared(_reflection, Mesh3DConstants.DisplayComposite(_targetFormat));

        RhiBlendState blend = draw == InfraredDraw.Body
            ? new RhiBlendState(
                new RhiBlendComponent(RhiBlendFactor.One, RhiBlendFactor.Zero, RhiBlendOperation.Add),
                new RhiBlendComponent(RhiBlendFactor.Zero, RhiBlendFactor.One, RhiBlendOperation.Add))
            : new RhiBlendState(
                new RhiBlendComponent(RhiBlendFactor.SourceAlpha, RhiBlendFactor.OneMinusSourceAlpha, RhiBlendOperation.Add),
                new RhiBlendComponent(RhiBlendFactor.One, RhiBlendFactor.One, RhiBlendOperation.Add));

        return _device.CreateRenderPipeline(new RhiRenderPipelineDescriptor(
            _layout,
            new RhiShaderStage(_module, _reflection.EntryPointFor(ShaderStage.Vertex).Name, vertex),
            new RhiShaderStage(_module, _reflection.EntryPointFor(ShaderStage.Fragment).Name, fragment),
            GlassPipelineCache.VertexLayouts(),
            [new RhiColorTargetState(_targetFormat, blend)],
            new RhiDepthStencilState(_depthFormat, DepthWrite: false, RhiCompare.GreaterEqual),
            new RhiPrimitiveState(RhiPrimitiveTopology.TriangleList, RhiCullMode.None),
            SampleCount: 1,
            $"Infrared-{draw}"));
    }
}
