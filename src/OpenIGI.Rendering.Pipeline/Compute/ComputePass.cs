using OpenIGI.Rendering.Pipeline.Passes;
using OpenIGI.Rendering.Rhi;
using OpenIGI.Shaders;

namespace OpenIGI.Rendering.Pipeline.Compute;

/// <summary>
/// One compute module, its resources bound by name, ready to dispatch.
/// </summary>
/// <remarks>
/// The shape every compute pass in this renderer has. What differs between them is which module and
/// which textures; everything else — deriving the layout, building the group, sizing the dispatch from
/// the target, rebuilding the group when the frame is resized — is the same, so it is written once.
/// The binding half of that is shared with the raster passes, which name and gather their resources
/// identically and differ only in what they record.
/// </remarks>
public sealed class ComputePass : IDisposable
{
    private readonly ShaderBindings _bindings;
    private bool _disposed;

    /// <summary>Initialises a new instance of the <see cref="ComputePass"/> class.</summary>
    /// <param name="device">The device the pass runs on.</param>
    /// <param name="module">Which module of the library.</param>
    /// <param name="label">A name for captures and diagnostics; the module's own when empty.</param>
    /// <param name="variant">Which compiled form of the module.</param>
    /// <param name="constants">Constants to fix at creation, or null for the module's own defaults.</param>
    public ComputePass(
        IRhiDevice device,
        ShaderModuleId module,
        string label = "",
        ShaderVariant variant = default,
        IReadOnlyDictionary<int, RhiConstant>? constants = null)
    {
        ArgumentNullException.ThrowIfNull(device);

        Label = label.Length > 0 ? label : module.ToString();
        Program = new ComputeProgram(device, module, variant, constants);

        _bindings = new ShaderBindings(
            device,
            Program.Reflection,
            Program.GroupLayouts,
            RhiShaderStages.Compute,
            Label);
    }

    /// <summary>What the library suffixes a texture's name with to name its sampler.</summary>
    public const string SamplerSuffix = ShaderBindings.SamplerSuffix;

    /// <summary>Gets the name it was created with.</summary>
    public string Label { get; }

    /// <summary>Gets the module as a pipeline, and what it declares.</summary>
    public ComputeProgram Program { get; }

    /// <summary>Gets how many bind groups have been built, which a test can watch for churn.</summary>
    public int GroupsBuilt => _bindings.GroupsBuilt;

    /// <summary>Binds a texture view to the slot the module gave that name.</summary>
    /// <param name="name">The name the Slang source declares.</param>
    /// <param name="view">The view.</param>
    public void Bind(string name, IRhiTextureView view) => _bindings.Bind(name, view);

    /// <summary>Binds a texture and the sampler the module reads it through.</summary>
    /// <param name="name">The name the Slang source declares the texture under.</param>
    /// <param name="view">The view.</param>
    /// <param name="sampler">How it is filtered.</param>
    /// <remarks>
    /// The library pairs a texture with a sampler of the same name and the suffix <c>Sampler</c>, because
    /// a combined sampler takes two binding slots on the WGSL target and collides with whatever is
    /// declared next. A module that fetches rather than samples declares no sampler, and this leaves it
    /// alone rather than refusing.
    /// </remarks>
    public void Bind(string name, IRhiTextureView view, IRhiSampler sampler) =>
        _bindings.Bind(name, view, sampler);

    /// <summary>Binds a buffer, or a range of one.</summary>
    /// <param name="name">The name the Slang source declares.</param>
    /// <param name="buffer">The buffer.</param>
    /// <param name="offset">Where the binding starts.</param>
    /// <param name="size">How much it covers, or zero for the rest.</param>
    public void Bind(string name, IRhiBuffer buffer, ulong offset = 0, ulong size = 0) =>
        _bindings.Bind(name, buffer, offset, size);

    /// <summary>Binds a sampler.</summary>
    /// <param name="name">The name the Slang source declares.</param>
    /// <param name="sampler">The sampler.</param>
    public void Bind(string name, IRhiSampler sampler) => _bindings.Bind(name, sampler);

    /// <summary>Binds a sampler to every slot the module declares that nothing has bound.</summary>
    /// <param name="sampler">The sampler.</param>
    /// <returns>How many slots it filled.</returns>
    /// <remarks>
    /// For a pass whose textures are all read the same way, which is most of them here. It fills only
    /// what is still empty, so a slot bound deliberately beforehand keeps what it was given.
    /// </remarks>
    public int BindRemainingSamplers(IRhiSampler sampler) => _bindings.BindRemainingSamplers(sampler);

    /// <summary>Records the dispatch that covers a region.</summary>
    /// <param name="pass">The pass to record into.</param>
    /// <param name="width">How many invocations are wanted along x.</param>
    /// <param name="height">Along y.</param>
    /// <param name="depth">Along z.</param>
    /// <param name="push">The push block, for a module that reads one.</param>
    /// <exception cref="InvalidOperationException">A slot the module declares was never bound.</exception>
    public void Dispatch(
        IRhiComputePassEncoder pass,
        int width,
        int height = 1,
        int depth = 1,
        ReadOnlySpan<byte> push = default)
    {
        ArgumentNullException.ThrowIfNull(pass);
        ObjectDisposedException.ThrowIf(_disposed, this);

        pass.SetPipeline(Program.Pipeline);

        for (int group = 0; group < _bindings.GroupCount; group++)
        {
            pass.SetBindGroup(group, _bindings.GroupFor(group));
        }

        if (!push.IsEmpty)
        {
            pass.SetPushData(push);
        }

        (int x, int y, int z) = Program.GroupsFor(width, height, depth);
        pass.Dispatch(x, y, z);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        _bindings.Dispose();
        Program.Dispose();
    }
}
