using OpenIGI.Rendering.Rhi;
using OpenIGI.Shaders;

namespace OpenIGI.Rendering.Pipeline.Passes;

/// <summary>
/// A pipeline that draws one full-screen triangle: one bind group, a push block, no vertex input and
/// no depth.
/// </summary>
/// <remarks>
/// The triangle is generated from the vertex index, so there is nothing to bind and the draw is three
/// vertices. It is the shape of nearly every screen-space pass in this renderer — the deferred
/// lighting, the resolve, the loading backdrop — which differ only in the group they read and the
/// module they shade with.
/// </remarks>
public sealed class FullscreenPipeline : IDisposable
{
    private bool _disposed;

    /// <summary>Initialises a new instance of the <see cref="FullscreenPipeline"/> class.</summary>
    /// <param name="device">The device the pipeline lives on.</param>
    /// <param name="module">The shader module, which carries both stages.</param>
    /// <param name="bindGroupLayout">The single group the shader reads.</param>
    /// <param name="targetFormat">The format of the attachment it draws into.</param>
    /// <param name="blend">How it is blended over that attachment, or null to replace it.</param>
    /// <param name="variant">Which compiled form of the module to load.</param>
    /// <param name="depthFormat">
    /// The depth attachment of the pass it draws into, or <see cref="RhiTextureFormat.Undefined"/> for
    /// a pass with none. Declared although nothing here tests depth, because a pipeline whose targets
    /// do not match its pass is refused.
    /// </param>
    public FullscreenPipeline(
        IRhiDevice device,
        ShaderModuleId module,
        IRhiBindGroupLayout bindGroupLayout,
        RhiTextureFormat targetFormat,
        RhiBlendState? blend = null,
        ShaderVariant variant = default,
        RhiTextureFormat depthFormat = RhiTextureFormat.Undefined)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(bindGroupLayout);

        ShaderReflection reflection = ShaderCatalog.GetReflection(module);
        PushDataBytes = reflection.PushBlock?.Size ?? 0;

        Module = device.CreateShaderModule(
            new RhiShaderModuleDescriptor(module, variant, module.ToString()));

        Layout = device.CreatePipelineLayout(new RhiPipelineLayoutDescriptor(
            [bindGroupLayout],
            PushDataBytes,
            RhiShaderStages.Vertex | RhiShaderStages.Fragment,
            module.ToString()));

        Pipeline = device.CreateRenderPipeline(new RhiRenderPipelineDescriptor(
            Layout,
            new RhiShaderStage(Module, reflection.EntryPointFor(ShaderStage.Vertex).Name),
            new RhiShaderStage(Module, reflection.EntryPointFor(ShaderStage.Fragment).Name),
            [],
            [new RhiColorTargetState(targetFormat, blend)],
            depthFormat is RhiTextureFormat.Undefined
                ? null
                : new RhiDepthStencilState(depthFormat, DepthWrite: false, RhiCompare.Always),
            new RhiPrimitiveState(RhiPrimitiveTopology.TriangleList, RhiCullMode.None),
            SampleCount: 1,
            module.ToString()));
    }

    /// <summary>Gets the compiled module both stages come from.</summary>
    public IRhiShaderModule Module { get; }

    /// <summary>Gets the layout the group and the push data are bound against.</summary>
    public IRhiPipelineLayout Layout { get; }

    /// <summary>Gets the pipeline.</summary>
    public IRhiRenderPipeline Pipeline { get; }

    /// <summary>Gets how many bytes of push data the module declared.</summary>
    public int PushDataBytes { get; }

    /// <summary>Draws the triangle, with a group and push data already decided.</summary>
    /// <param name="pass">The pass to record into.</param>
    /// <param name="group">The group the shader reads.</param>
    /// <param name="push">The push data, which must not exceed what the module declared.</param>
    public void Draw(IRhiRenderPassEncoder pass, IRhiBindGroup group, ReadOnlySpan<byte> push)
    {
        ArgumentNullException.ThrowIfNull(pass);
        ArgumentNullException.ThrowIfNull(group);

        pass.SetPipeline(Pipeline);
        pass.SetBindGroup(0, group);

        if (!push.IsEmpty)
        {
            pass.SetPushData(push);
        }

        pass.Draw(3);
    }

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
        Module.Dispose();
    }
}
