using OpenIGI.Rendering.Rhi;
using OpenIGI.Shaders;

namespace OpenIGI.Rendering.Pipeline.Compute;

/// <summary>
/// One compute module, its resources bound by name, ready to dispatch.
/// </summary>
/// <remarks>
/// <para>
/// The shape every compute pass in this renderer has. What differs between them is which module and
/// which textures; everything else — deriving the layout, building the group, sizing the dispatch from
/// the target, rebuilding the group when the frame is resized — is the same, so it is written once.
/// </para>
/// <para>
/// Resources are named rather than numbered. A pass that binds by index is a second copy of the
/// shader's binding table, and the version of that mistake worth worrying about is not a missing
/// binding — that is refused — but a transposed pair, which binds the wrong texture to the wrong slot
/// and shades something plausible.
/// </para>
/// <para>
/// The group is rebuilt only when what is bound changes. A frame that binds the same textures every
/// time it runs — which is most of them, until the window is resized — builds one group and keeps it.
/// </para>
/// </remarks>
public sealed class ComputePass : IDisposable
{
    private readonly IRhiDevice _device;
    private readonly Dictionary<int, Dictionary<int, RhiBindGroupEntry>> _bound = [];
    private readonly IRhiBindGroup?[] _groups;
    private readonly bool[] _stale;
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

        _device = device;
        Label = label.Length > 0 ? label : module.ToString();
        Program = new ComputeProgram(device, module, variant, constants);

        _groups = new IRhiBindGroup?[Program.GroupLayouts.Count];
        _stale = new bool[Program.GroupLayouts.Count];
    }

    /// <summary>Gets the name it was created with.</summary>
    public string Label { get; }

    /// <summary>Gets the module as a pipeline, and what it declares.</summary>
    public ComputeProgram Program { get; }

    /// <summary>Gets how many bind groups have been built, which a test can watch for churn.</summary>
    public int GroupsBuilt { get; private set; }

    /// <summary>Binds a texture view to the slot the module gave that name.</summary>
    /// <param name="name">The name the Slang source declares.</param>
    /// <param name="view">The view.</param>
    public void Bind(string name, IRhiTextureView view)
    {
        ArgumentNullException.ThrowIfNull(view);
        Set(name, binding => RhiBindGroupEntry.Of(binding, view));
    }

    /// <summary>Binds a buffer, or a range of one.</summary>
    /// <param name="name">The name the Slang source declares.</param>
    /// <param name="buffer">The buffer.</param>
    /// <param name="offset">Where the binding starts.</param>
    /// <param name="size">How much it covers, or zero for the rest.</param>
    public void Bind(string name, IRhiBuffer buffer, ulong offset = 0, ulong size = 0)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        Set(name, binding => RhiBindGroupEntry.Of(binding, buffer, offset, size));
    }

    /// <summary>Binds a sampler.</summary>
    /// <param name="name">The name the Slang source declares.</param>
    /// <param name="sampler">The sampler.</param>
    public void Bind(string name, IRhiSampler sampler)
    {
        ArgumentNullException.ThrowIfNull(sampler);
        Set(name, binding => RhiBindGroupEntry.Of(binding, sampler));
    }

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

        for (int group = 0; group < _groups.Length; group++)
        {
            pass.SetBindGroup(group, GroupFor(group));
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

        foreach (IRhiBindGroup? group in _groups)
        {
            group?.Dispose();
        }

        Program.Dispose();
    }

    private void Set(string name, Func<int, RhiBindGroupEntry> entry)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        (int group, int binding) = Program.Slot(name);

        if (!_bound.TryGetValue(group, out Dictionary<int, RhiBindGroupEntry>? entries))
        {
            entries = [];
            _bound[group] = entries;
        }

        RhiBindGroupEntry built = entry(binding);

        if (entries.TryGetValue(binding, out RhiBindGroupEntry existing) && existing == built)
        {
            return;
        }

        entries[binding] = built;
        _stale[group] = true;
    }

    private IRhiBindGroup GroupFor(int group)
    {
        if (!_stale[group] && _groups[group] is { } kept)
        {
            return kept;
        }

        List<RhiBindGroupLayoutEntry> wanted = Expected(group);
        Dictionary<int, RhiBindGroupEntry> entries = _bound.TryGetValue(group, out Dictionary<int, RhiBindGroupEntry>? bound)
            ? bound
            : [];

        List<RhiBindGroupEntry> filled = [];

        foreach (RhiBindGroupLayoutEntry slot in wanted)
        {
            if (!entries.TryGetValue(slot.Binding, out RhiBindGroupEntry value))
            {
                throw new InvalidOperationException(
                    $"The pass '{Label}' dispatches with nothing bound to slot {slot.Binding} of group "
                    + $"{group}, which its module declares as a {slot.Type}.");
            }

            filled.Add(value);
        }

        _groups[group]?.Dispose();

        IRhiBindGroup built = _device.CreateBindGroup(new RhiBindGroupDescriptor(
            Program.GroupLayouts[group],
            filled,
            $"{Label}-{group}"));

        _groups[group] = built;
        _stale[group] = false;
        GroupsBuilt++;

        return built;
    }

    /// <summary>What the module declares in one group, in binding order.</summary>
    private List<RhiBindGroupLayoutEntry> Expected(int group)
    {
        List<RhiBindGroupLayoutEntry> entries = [];

        foreach (ShaderBinding binding in Program.Reflection.Bindings)
        {
            if (binding.Group != group)
            {
                continue;
            }

            if (Program.Reflection.PushBlock is { } push
                && binding.Group == push.Group
                && binding.Binding == push.Binding)
            {
                continue;
            }

            entries.Add(ComputeProgram.EntryFor(binding));
        }

        return entries;
    }
}
