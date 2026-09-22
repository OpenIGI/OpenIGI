using OpenIGI.Rendering.Rhi;
using OpenIGI.Shaders;

namespace OpenIGI.Rendering.Pipeline.Draw2D;

/// <summary>
/// One 2D shader module built as a pipeline per blend mode, over a layout that binds a texture and
/// takes the projection and whatever else the module reads as push data.
/// </summary>
/// <remarks>
/// <para>
/// Blend state is baked into a pipeline on both backends rather than set at draw time, so the blend
/// modes the 2D path uses are that many pipelines built from the same module and the same layout. A
/// draw picks one; the vertex layout, the texture binding and the dynamic viewport are identical
/// across them.
/// </para>
/// <para>
/// The projection size travels as push data rather than a uniform, because it is eight bytes that
/// change with the window. Vulkan writes push constants for it, WebGPU a slice of a per-frame uniform
/// ring; the cost differs and the meaning does not, which is the point of the RHI having the notion at
/// all.
/// </para>
/// <para>
/// How large the push block is comes from the module's own reflection rather than being written down a
/// second time here, so a shader that grows a field does not need this file edited.
/// </para>
/// </remarks>
public sealed class SpritePipelineSet : IDisposable
{
    private readonly IRhiRenderPipeline[] _pipelines;
    private bool _disposed;

    /// <summary>Initialises a new instance of the <see cref="SpritePipelineSet"/> class.</summary>
    /// <param name="device">The device the pipelines live on.</param>
    /// <param name="module">The shader module, which carries both stages.</param>
    /// <param name="textureLayout">The layout binding the texture the fragment stage samples.</param>
    /// <param name="targetFormat">The format of the attachment the pipelines draw into.</param>
    /// <param name="blends">The blend modes to build, in the order they will be indexed.</param>
    public SpritePipelineSet(
        IRhiDevice device,
        ShaderModuleId module,
        IRhiBindGroupLayout textureLayout,
        RhiTextureFormat targetFormat,
        IReadOnlyList<BlendMode> blends)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(textureLayout);
        ArgumentNullException.ThrowIfNull(blends);

        ShaderReflection reflection = ShaderCatalog.GetReflection(module);
        PushDataBytes = reflection.PushBlock?.Size ?? 0;

        Module = device.CreateShaderModule(new RhiShaderModuleDescriptor(module, Label: module.ToString()));

        Layout = device.CreatePipelineLayout(new RhiPipelineLayoutDescriptor(
            [textureLayout],
            PushDataBytes,
            RhiShaderStages.Vertex | RhiShaderStages.Fragment,
            module.ToString()));

        RhiShaderStage vertex = new(Module, reflection.EntryPointFor(ShaderStage.Vertex).Name);
        RhiShaderStage fragment = new(Module, reflection.EntryPointFor(ShaderStage.Fragment).Name);

        _pipelines = new IRhiRenderPipeline[blends.Count];
        Blends = blends;

        for (int i = 0; i < blends.Count; i++)
        {
            _pipelines[i] = device.CreateRenderPipeline(new RhiRenderPipelineDescriptor(
                Layout,
                vertex,
                fragment,
                [SpriteVertexLayout.Layout()],
                [new RhiColorTargetState(targetFormat, BlendFor(blends[i]))],
                DepthStencil: null,
                new RhiPrimitiveState(RhiPrimitiveTopology.TriangleList, RhiCullMode.None),
                SampleCount: 1,
                $"{module}-{blends[i]}"));
        }
    }

    /// <summary>Gets the compiled module both stages come from.</summary>
    public IRhiShaderModule Module { get; }

    /// <summary>Gets the layout the texture group and the push data are bound against.</summary>
    public IRhiPipelineLayout Layout { get; }

    /// <summary>Gets how many bytes of push data the module declared.</summary>
    public int PushDataBytes { get; }

    /// <summary>Gets the blend modes that were built, in the order they are indexed.</summary>
    public IReadOnlyList<BlendMode> Blends { get; }

    /// <summary>Gets the pipeline for one blend mode.</summary>
    /// <param name="blend">The blend mode.</param>
    /// <returns>The matching pipeline.</returns>
    /// <exception cref="ArgumentOutOfRangeException">This set was not built with that blend mode.</exception>
    public IRhiRenderPipeline ForBlend(BlendMode blend)
    {
        for (int i = 0; i < Blends.Count; i++)
        {
            if (Blends[i] == blend)
            {
                return _pipelines[i];
            }
        }

        throw new ArgumentOutOfRangeException(
            nameof(blend),
            blend,
            "This pipeline set was not built with that blend mode.");
    }

    /// <summary>How a blend mode is stated to the RHI, or null for one that writes outright.</summary>
    /// <param name="blend">The blend mode.</param>
    /// <returns>The blend state.</returns>
    /// <remarks>
    /// Alpha is blended as one-minus-source-alpha in every mode but additive, which is what the Vulkan
    /// path did and what keeps a target that is composited later holding a sensible coverage value.
    /// </remarks>
    public static RhiBlendState? BlendFor(BlendMode blend) => blend switch
    {
        BlendMode.Opaque => null,
        BlendMode.AlphaBlend => new RhiBlendState(
            RhiBlendComponent.AlphaBlend,
            new RhiBlendComponent(
                RhiBlendFactor.One,
                RhiBlendFactor.OneMinusSourceAlpha,
                RhiBlendOperation.Add)),
        BlendMode.Additive => new RhiBlendState(
            new RhiBlendComponent(RhiBlendFactor.SourceAlpha, RhiBlendFactor.One, RhiBlendOperation.Add),
            new RhiBlendComponent(RhiBlendFactor.One, RhiBlendFactor.One, RhiBlendOperation.Add)),
        _ => throw new ArgumentOutOfRangeException(
            nameof(blend),
            blend,
            "The 2D path does not build a pipeline for that blend mode."),
    };

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        foreach (IRhiRenderPipeline pipeline in _pipelines)
        {
            pipeline.Dispose();
        }

        Layout.Dispose();
        Module.Dispose();
    }
}
