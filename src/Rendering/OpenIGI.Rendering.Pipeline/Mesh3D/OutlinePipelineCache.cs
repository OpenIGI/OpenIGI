using OpenIGI.Rendering.Rhi;
using OpenIGI.Shaders;

namespace OpenIGI.Rendering.Pipeline.Mesh3D;

/// <summary>The three pipelines the selection glow is drawn with: a depth mask and two shells.</summary>
public enum OutlinePass
{
    /// <summary>The model's own silhouette, laid into depth just inside the near plane, no colour.</summary>
    Mask,

    /// <summary>A shell drawn with back faces culled.</summary>
    ShellCullBack,

    /// <summary>A shell drawn with front faces culled.</summary>
    ShellCullFront,
}

/// <summary>
/// The selection glow's pipelines, built per target the glow is drawn into.
/// </summary>
/// <remarks>
/// <para>
/// An inverted hull. The mask writes the model's silhouette into depth at a depth just inside the near plane,
/// writing no colour; each shell draws the model pushed outwards in screen space and tests <em>greater</em>
/// in reverse-Z, so it loses against the mask -- the glow stops at the model -- and wins against the world
/// behind. A shell blends its colour over the picture and forces the target's alpha to nought, because that
/// alpha is the frame's marker of world geometry and a glow is not world.
/// </para>
/// <para>
/// The glow goes into whatever pass is open: the forward pass over the lit image, or the frame itself on
/// the direct path. The two have different formats, so the pipelines are keyed by them.
/// </para>
/// </remarks>
public sealed class OutlinePipelineCache : IDisposable
{
    /// <summary>How many bytes of push data the outline shader reads.</summary>
    public const int PushDataBytes = (16 + 4 + 4 + 4) * sizeof(float);

    private readonly IRhiDevice _device;
    private readonly IRhiShaderModule _module;
    private readonly IRhiPipelineLayout _layout;
    private readonly ShaderReflection _reflection;
    private readonly Dictionary<(RhiTextureFormat Colour, RhiTextureFormat Depth, OutlinePass Pass), IRhiRenderPipeline> _pipelines = [];
    private bool _disposed;

    /// <summary>Initialises a new instance of the <see cref="OutlinePipelineCache"/> class.</summary>
    /// <param name="device">The device.</param>
    public OutlinePipelineCache(IRhiDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);

        _device = device;
        _reflection = ShaderCatalog.GetReflection(ShaderModuleId.Outline);
        _module = device.CreateShaderModule(new RhiShaderModuleDescriptor(ShaderModuleId.Outline, Label: "Outline"));
        _layout = device.CreatePipelineLayout(new RhiPipelineLayoutDescriptor(
            [],
            PushDataBytes,
            RhiShaderStages.Vertex | RhiShaderStages.Fragment,
            "Outline"));
    }

    /// <summary>Gets the pipeline for one pass of the glow into a target.</summary>
    /// <param name="colour">The colour target's format.</param>
    /// <param name="depth">The depth target's format.</param>
    /// <param name="pass">Which of the three.</param>
    /// <returns>The pipeline, built on first use.</returns>
    public IRhiRenderPipeline For(RhiTextureFormat colour, RhiTextureFormat depth, OutlinePass pass)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!_pipelines.TryGetValue((colour, depth, pass), out IRhiRenderPipeline? pipeline))
        {
            pipeline = Create(colour, depth, pass);
            _pipelines[(colour, depth, pass)] = pipeline;
        }

        return pipeline;
    }

    /// <summary>The two vertex streams the outline reads: this pose and the last, position and normal.</summary>
    /// <returns>The current pose at slot zero, the previous at slot one, at the mesh stages' locations.</returns>
    public static IReadOnlyList<RhiVertexBufferLayout> VertexLayouts() =>
    [
        new RhiVertexBufferLayout(
            Vertex3D.Size,
            RhiVertexStepMode.Vertex,
            [
                new RhiVertexElement(0, RhiVertexFormat.Float32x3, 0),
                new RhiVertexElement(1, RhiVertexFormat.Float32x3, 12),
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
        _layout.Dispose();
        _module.Dispose();
    }

    private IRhiRenderPipeline Create(RhiTextureFormat colour, RhiTextureFormat depth, OutlinePass pass)
    {
        bool mask = pass == OutlinePass.Mask;

        // Colour over the picture; the target's alpha forced to nought, since it marks world geometry.
        RhiBlendState blend = new(
            RhiBlendComponent.AlphaBlend,
            new RhiBlendComponent(RhiBlendFactor.Zero, RhiBlendFactor.Zero, RhiBlendOperation.Add));

        return _device.CreateRenderPipeline(new RhiRenderPipelineDescriptor(
            _layout,
            new RhiShaderStage(
                _module,
                _reflection.EntryPointFor(ShaderStage.Vertex).Name,
                ShaderConstants.Declared(_reflection, Mesh3DConstants.TargetConstants)),
            new RhiShaderStage(_module, _reflection.EntryPointFor(ShaderStage.Fragment).Name),
            VertexLayouts(),

            // The mask writes no colour at all: that is the whole of what makes it a mask.
            [new RhiColorTargetState(colour, mask ? null : blend, mask ? RhiColorWrite.None : RhiColorWrite.All)],
            new RhiDepthStencilState(depth, DepthWrite: mask, mask ? RhiCompare.Always : RhiCompare.Greater),
            new RhiPrimitiveState(
                RhiPrimitiveTopology.TriangleList,
                pass switch
                {
                    OutlinePass.ShellCullBack => RhiCullMode.Back,
                    OutlinePass.ShellCullFront => RhiCullMode.Front,
                    _ => RhiCullMode.None,
                }),
            SampleCount: 1,
            $"Outline-{pass}-{colour}"));
    }
}
