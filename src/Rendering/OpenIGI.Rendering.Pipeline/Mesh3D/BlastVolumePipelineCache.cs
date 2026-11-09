using OpenIGI.Rendering.Mesh3D;
using OpenIGI.Rendering.Rhi;
using OpenIGI.Shaders;

namespace OpenIGI.Rendering.Pipeline.Mesh3D;

/// <summary>The bomb blast's draw (<c>World/BlastVolume.slang</c>): one pipeline over the lit image.</summary>
/// <remarks>
/// <para>
/// The colour is <b>replaced</b>, not blended: the shader writes the scene behind the blast -- bent by its shock front, seen
/// through its smoke -- with the blast's own light added, out of a copy of the lit image taken just before, so what it
/// writes already holds what it covers. Its coverage is ADDED to the target's alpha, the marker the temporal resolve reads
/// to keep a moving overlay out of its history.
/// </para>
/// <para>
/// Both faces drawn and no depth test: the shader keeps only the box's far face, by where the ray leaves the box, so each
/// pixel is marched once whether the eye is outside the box or in it, and it stops each ray at the scene's own depth.
/// </para>
/// </remarks>
public sealed class BlastVolumePipelineCache : IDisposable
{
    /// <summary>How many floats of push data the shader reads: the box's transform and the draw's vector.</summary>
    public const int PushDataFloats = 20;

    /// <summary>How many floats the frame's parameters hold: see <c>BlastVolumeParams</c>.</summary>
    public const int ParameterFloats = 16 + (8 * 4) + (BlastSlots * 16);

    /// <summary>How many blasts one frame's parameters carry.</summary>
    public const int BlastSlots = 2;

    private readonly IRhiShaderModule _module;
    private bool _disposed;

    /// <summary>Initialises a new instance of the <see cref="BlastVolumePipelineCache"/> class.</summary>
    /// <param name="device">The device the pipeline lives on.</param>
    /// <param name="materialLayout">The material group every mesh path shares; the shader samples none of it.</param>
    /// <param name="blastLayout">The blast's own group: the depth and lit image it reads, and its parameters.</param>
    /// <param name="targetFormat">The lit image's format.</param>
    /// <param name="depthFormat">The scene depth's format, which the pass is opened with.</param>
    public BlastVolumePipelineCache(
        IRhiDevice device, IRhiBindGroupLayout materialLayout, IRhiBindGroupLayout blastLayout, RhiTextureFormat targetFormat, RhiTextureFormat depthFormat)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(materialLayout);
        ArgumentNullException.ThrowIfNull(blastLayout);

        Reflection = ShaderCatalog.GetReflection(ShaderModuleId.BlastVolume);
        _module = device.CreateShaderModule(new RhiShaderModuleDescriptor(ShaderModuleId.BlastVolume, Label: nameof(ShaderModuleId.BlastVolume)));
        Layout = device.CreatePipelineLayout(new RhiPipelineLayoutDescriptor(
            [materialLayout, blastLayout],
            PushDataFloats * sizeof(float),
            RhiShaderStages.Vertex | RhiShaderStages.Fragment,
            nameof(ShaderModuleId.BlastVolume)));

        IReadOnlyDictionary<int, RhiConstant> vertex = ShaderConstants.Declared(Reflection, Mesh3DConstants.TargetConstants);
        IReadOnlyDictionary<int, RhiConstant> fragment = ShaderConstants.Declared(
            Reflection,
            new Dictionary<int, RhiConstant>
            {
                [Mesh3DConstants.ClipYDown] = RhiConstant.Of(0),
            });

        RhiBlendState replace = new(
            new RhiBlendComponent(RhiBlendFactor.One, RhiBlendFactor.Zero, RhiBlendOperation.Add),
            new RhiBlendComponent(RhiBlendFactor.One, RhiBlendFactor.One, RhiBlendOperation.Add));
        Pipeline = device.CreateRenderPipeline(new RhiRenderPipelineDescriptor(
            Layout,
            new RhiShaderStage(_module, Reflection.EntryPointFor(ShaderStage.Vertex).Name, vertex),
            new RhiShaderStage(_module, Reflection.EntryPointFor(ShaderStage.Fragment).Name, fragment),
            GlassPipelineCache.VertexLayouts(),
            [new RhiColorTargetState(targetFormat, replace)],
            new RhiDepthStencilState(depthFormat, DepthWrite: false, RhiCompare.Always),
            new RhiPrimitiveState(RhiPrimitiveTopology.TriangleList, RhiCullMode.None),
            SampleCount: 1,
            nameof(ShaderModuleId.BlastVolume)));
    }

    /// <summary>Gets the module's reflection, whose second group the caller builds.</summary>
    public ShaderReflection Reflection { get; }

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
