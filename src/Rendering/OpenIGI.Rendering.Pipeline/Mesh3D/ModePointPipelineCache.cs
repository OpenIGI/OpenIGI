using OpenIGI.Rendering.Rhi;
using OpenIGI.Shaders;

namespace OpenIGI.Rendering.Pipeline.Mesh3D;

/// <summary>The mode points' draw (<c>World/ModePoint.slang</c>): one pipeline over one target.</summary>
/// <remarks>
/// Premultiplied: the disc's fill darkens what lies under it by its coverage and its rings add their light; the wall adds light
/// alone. The coverage is added to the target's alpha, the marker the temporal resolve reads to keep an animated overlay out
/// of its history. Depth tested and never written, both sides drawn.
/// </remarks>
public sealed class ModePointPipelineCache : IDisposable
{
    /// <summary>How many floats of push data the shader reads: its transform and three vectors.</summary>
    public const int PushDataFloats = 28;

    private readonly IRhiShaderModule _module;
    private bool _disposed;

    /// <summary>Initialises a new instance of the <see cref="ModePointPipelineCache"/> class.</summary>
    /// <param name="device">The device the pipeline lives on.</param>
    /// <param name="materialLayout">The material group every mesh path shares; the shader samples none of it.</param>
    /// <param name="targetFormat">The format of the image drawn into.</param>
    /// <param name="depthFormat">The format of the depth tested against.</param>
    public ModePointPipelineCache(IRhiDevice device, IRhiBindGroupLayout materialLayout, RhiTextureFormat targetFormat, RhiTextureFormat depthFormat)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(materialLayout);

        ShaderReflection reflection = ShaderCatalog.GetReflection(ShaderModuleId.ModePoint);
        _module = device.CreateShaderModule(new RhiShaderModuleDescriptor(ShaderModuleId.ModePoint, Label: nameof(ShaderModuleId.ModePoint)));
        Layout = device.CreatePipelineLayout(new RhiPipelineLayoutDescriptor(
            [materialLayout],
            PushDataFloats * sizeof(float),
            RhiShaderStages.Vertex | RhiShaderStages.Fragment,
            nameof(ShaderModuleId.ModePoint)));

        IReadOnlyDictionary<int, RhiConstant> vertex = ShaderConstants.Declared(reflection, Mesh3DConstants.TargetConstants);
        IReadOnlyDictionary<int, RhiConstant> fragment = ShaderConstants.Declared(
            reflection,
            new Dictionary<int, RhiConstant>
            {
                [Mesh3DConstants.ClipYDown] = RhiConstant.Of(0),
                [Mesh3DConstants.TargetEncodes] = RhiConstant.Of(targetFormat.IsSrgb() ? 1 : 0),
            });
        RhiBlendState blend = new(
            new RhiBlendComponent(RhiBlendFactor.One, RhiBlendFactor.OneMinusSourceAlpha, RhiBlendOperation.Add),
            new RhiBlendComponent(RhiBlendFactor.One, RhiBlendFactor.One, RhiBlendOperation.Add));
        Pipeline = device.CreateRenderPipeline(new RhiRenderPipelineDescriptor(
            Layout,
            new RhiShaderStage(_module, reflection.EntryPointFor(ShaderStage.Vertex).Name, vertex),
            new RhiShaderStage(_module, reflection.EntryPointFor(ShaderStage.Fragment).Name, fragment),
            GlassPipelineCache.VertexLayouts(),
            [new RhiColorTargetState(targetFormat, blend)],
            new RhiDepthStencilState(depthFormat, DepthWrite: false, RhiCompare.GreaterEqual),
            new RhiPrimitiveState(RhiPrimitiveTopology.TriangleList, RhiCullMode.None),
            SampleCount: 1,
            nameof(ShaderModuleId.ModePoint)));
    }

    /// <summary>Gets the pipeline layout.</summary>
    public IRhiPipelineLayout Layout { get; }

    /// <summary>Gets the pipeline.</summary>
    public IRhiRenderPipeline Pipeline { get; }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Pipeline.Dispose();
        Layout.Dispose();
        _module.Dispose();
    }
}
