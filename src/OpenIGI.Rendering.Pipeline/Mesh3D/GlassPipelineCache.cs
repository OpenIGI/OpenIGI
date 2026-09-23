using OpenIGI.Rendering.Mesh3D;
using OpenIGI.Rendering.Rhi;
using OpenIGI.Shaders;

namespace OpenIGI.Rendering.Pipeline.Mesh3D;

/// <summary>
/// The glazing's transmission: a window pane's tint laid over the lit scene behind it, one pipeline per
/// culling mode.
/// </summary>
/// <remarks>
/// <para>
/// The Vulkan original's glass pipeline in its transmission form. What comes through a pane is its
/// colour, lightmap and vertex colour multiplied, premultiplied by how transparent it is and fogged by its
/// distance, and laid over the lit image with one-and-one-minus-source-alpha — so the tint darkens what
/// is behind it by exactly the share the pane covers. Tested against the scene's depth and never
/// writing it: a pane that wrote depth would hide the tracers, the flame and the smoke behind it.
/// </para>
/// <para>
/// What a pane reflects is a pass of its own, over the surface cache, and has not moved onto this path.
/// </para>
/// </remarks>
public sealed class GlassPipelineCache : IDisposable
{
    /// <summary>How many bytes of push data the glass shader reads.</summary>
    public const int PushDataBytes = sizeof(float) * 32;

    /// <summary>How many floats its per-frame block holds: one vector of fog.</summary>
    public const int ParameterFloats = 4;

    private readonly IRhiDevice _device;
    private readonly RhiTextureFormat _targetFormat;
    private readonly RhiTextureFormat _depthFormat;
    private readonly IRhiShaderModule _module;
    private readonly ShaderReflection _reflection;
    private readonly Dictionary<CullMode, IRhiRenderPipeline> _pipelines = [];
    private bool _disposed;

    /// <summary>Initialises a new instance of the <see cref="GlassPipelineCache"/> class.</summary>
    /// <param name="device">The device the pipelines live on.</param>
    /// <param name="materialLayout">The material group, shared with the other mesh paths.</param>
    /// <param name="glassLayout">The glazing's own group: the scene's depth and the fog block.</param>
    /// <param name="targetFormat">The format of the lit image it draws over.</param>
    /// <param name="depthFormat">The format of the scene's depth it tests against.</param>
    public GlassPipelineCache(
        IRhiDevice device,
        IRhiBindGroupLayout materialLayout,
        IRhiBindGroupLayout glassLayout,
        RhiTextureFormat targetFormat,
        RhiTextureFormat depthFormat)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(materialLayout);
        ArgumentNullException.ThrowIfNull(glassLayout);

        _device = device;
        _targetFormat = targetFormat;
        _depthFormat = depthFormat;
        _reflection = ShaderCatalog.GetReflection(ShaderModuleId.Glass);
        _module = device.CreateShaderModule(new RhiShaderModuleDescriptor(ShaderModuleId.Glass, Label: "Glass"));

        Layout = device.CreatePipelineLayout(new RhiPipelineLayoutDescriptor(
            [materialLayout, glassLayout],
            PushDataBytes,
            RhiShaderStages.Vertex | RhiShaderStages.Fragment,
            "Glass"));
    }

    /// <summary>Gets the pipeline layout every variant shares.</summary>
    public IRhiPipelineLayout Layout { get; }

    /// <summary>Gets the reflection of the module, whose second group the caller builds.</summary>
    public ShaderReflection Reflection => _reflection;

    /// <summary>Gets the pipeline for a culling mode, building it on first use.</summary>
    /// <param name="cull">Which facing the pane culls.</param>
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

    /// <summary>The vertex streams the glass stage reads: the mesh, and the pose it moved from.</summary>
    /// <returns>The current pose at slot zero, the previous pose at slot one.</returns>
    public static IReadOnlyList<RhiVertexBufferLayout> VertexLayouts() =>
    [
        new RhiVertexBufferLayout(
            Vertex3D.Size,
            RhiVertexStepMode.Vertex,
            [
                new RhiVertexElement(0, RhiVertexFormat.Float32x3, 0),
                new RhiVertexElement(1, RhiVertexFormat.Float32x3, 12),
                new RhiVertexElement(2, RhiVertexFormat.Float32x2, 24),
                new RhiVertexElement(3, RhiVertexFormat.Float32x2, 32),
                new RhiVertexElement(4, RhiVertexFormat.Unorm8x4, Vertex3D.ColourOffset),
            ]),
        new RhiVertexBufferLayout(
            Vertex3D.Size,
            RhiVertexStepMode.Vertex,
            [
                new RhiVertexElement(5, RhiVertexFormat.Float32x3, 0),
                new RhiVertexElement(6, RhiVertexFormat.Float32x3, 12),
            ]),
    ];

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
        IReadOnlyDictionary<int, RhiConstant> vertex = ShaderConstants.Declared(_reflection, Mesh3DConstants.TargetConstants);

        // Every texture on this path is an sRGB image, so the pane's colour and lightmap arrive linear.
        IReadOnlyDictionary<int, RhiConstant> fragment = ShaderConstants.Declared(
            _reflection,
            new Dictionary<int, RhiConstant>
            {
                [Mesh3DConstants.ClipYDown] = RhiConstant.Of(0),
                [Mesh3DConstants.SamplerDecodes] = RhiConstant.Of(1),
            });

        // Premultiplied over what is there; the target's own alpha is left as the scene wrote it.
        RhiBlendState over = new(
            new RhiBlendComponent(RhiBlendFactor.One, RhiBlendFactor.OneMinusSourceAlpha, RhiBlendOperation.Add),
            new RhiBlendComponent(RhiBlendFactor.Zero, RhiBlendFactor.One, RhiBlendOperation.Add));

        return _device.CreateRenderPipeline(new RhiRenderPipelineDescriptor(
            Layout,
            new RhiShaderStage(_module, _reflection.EntryPointFor(ShaderStage.Vertex).Name, vertex),
            new RhiShaderStage(_module, _reflection.EntryPointFor(ShaderStage.Fragment).Name, fragment),
            VertexLayouts(),
            [new RhiColorTargetState(_targetFormat, over)],

            // Reverse-Z, tested and never written.
            new RhiDepthStencilState(_depthFormat, DepthWrite: false, RhiCompare.GreaterEqual),
            new RhiPrimitiveState(RhiPrimitiveTopology.TriangleList, Mesh3DPipelineCache.CullFor(cull)),
            SampleCount: 1,
            $"Glass-{cull}"));
    }
}
