using OpenIGI.Rendering.Rhi;
using OpenIGI.Shaders;

namespace OpenIGI.Rendering.Pipeline.Passes;

/// <summary>
/// What one module has bound, named the way the module names it, built into groups on demand.
/// </summary>
/// <remarks>
/// <para>
/// Resources are named rather than numbered. A pass that binds by index is a second copy of the
/// shader's binding table, and the version of that mistake worth worrying about is not a missing
/// binding — that is refused — but a transposed pair, which binds the wrong texture to the wrong slot
/// and shades something plausible.
/// </para>
/// <para>
/// A group is rebuilt only when what it holds changes, and one it has built before is taken back rather
/// than built again. That second half is not an economy: most passes bind a parameter buffer that
/// alternates with the frame slot, so what they hold changes EVERY frame and flips back the next. Built
/// afresh each time, that was a new group per pass per frame, and a Vulkan descriptor set is never
/// handed back while any frame could still be reading it -- so the pools grew for as long as the game
/// ran. Kept, a pass builds its two groups once.
/// </para>
/// <para>
/// Shared by the compute passes and the raster ones, because the difference between them is the
/// pipeline and the recorded command, not how a shader's resources are named and gathered.
/// </para>
/// </remarks>
public sealed class ShaderBindings : IDisposable
{
    /// <summary>What the library suffixes a texture's name with to name its sampler.</summary>
    public const string SamplerSuffix = "Sampler";

    private readonly IRhiDevice _device;
    private readonly ShaderReflection _reflection;
    private readonly IReadOnlyList<IRhiBindGroupLayout> _layouts;
    private readonly RhiShaderStages _stages;
    private readonly Dictionary<string, ShaderBinding> _byName = [];
    private readonly Dictionary<int, Dictionary<int, RhiBindGroupEntry>> _bound = [];
    private readonly IRhiBindGroup?[] _groups;
    private readonly bool[] _stale;
    private readonly List<(RhiBindGroupEntry[] Entries, IRhiBindGroup Group)>[] _recent;
    private bool _disposed;

    /// <summary>Initialises a new instance of the <see cref="ShaderBindings"/> class.</summary>
    /// <param name="device">The device the groups are created on.</param>
    /// <param name="reflection">What the module declares.</param>
    /// <param name="layouts">The group layouts, in group order, which the caller owns.</param>
    /// <param name="stages">Which stages read the groups, for the completeness check's diagnostics.</param>
    /// <param name="label">A name for captures and diagnostics.</param>
    public ShaderBindings(
        IRhiDevice device,
        ShaderReflection reflection,
        IReadOnlyList<IRhiBindGroupLayout> layouts,
        RhiShaderStages stages,
        string label)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(reflection);
        ArgumentNullException.ThrowIfNull(layouts);

        _device = device;
        _reflection = reflection;
        _layouts = layouts;
        _stages = stages;
        Label = label;

        foreach (ShaderBinding binding in reflection.Bindings)
        {
            if (!ShaderBindingLayout.IsPushBlock(reflection, binding))
            {
                _byName[binding.Name] = binding;
            }
        }

        _groups = new IRhiBindGroup?[layouts.Count];
        _stale = new bool[layouts.Count];
        _recent = new List<(RhiBindGroupEntry[], IRhiBindGroup)>[layouts.Count];

        for (int i = 0; i < _recent.Length; i++)
        {
            _recent[i] = [];
        }
    }

    /// <summary>How many groups of one index are kept for reuse.</summary>
    /// <remarks>
    /// Two frame slots, with room for a pass whose other contents also alternate. The least recently
    /// used goes first; it is released rather than retired, which is safe on both backends because
    /// neither frees a group a submitted command still names -- WebGPU counts its references, and this
    /// Vulkan implementation never hands a descriptor set back at all.
    /// </remarks>
    private const int RecentCapacity = 8;

    /// <summary>Gets the name these were created with.</summary>
    public string Label { get; }

    /// <summary>Gets how many groups there are.</summary>
    public int GroupCount => _groups.Length;

    /// <summary>Gets how many groups have been built, which a test can watch for churn.</summary>
    public int GroupsBuilt { get; private set; }

    /// <summary>Whether the module binds anything of a name.</summary>
    /// <param name="name">The name the Slang source would declare.</param>
    /// <returns><see langword="true"/> when it does.</returns>
    /// <remarks>
    /// Asked rather than caught, for the places where a name is optional: a texture's sampler exists
    /// only where the module samples rather than fetches, and a traced variant binds what a software one
    /// does not.
    /// </remarks>
    public bool Declares(string name) => _byName.ContainsKey(name);

    /// <summary>Where the module binds one of its resources.</summary>
    /// <param name="name">The name the Slang source declares.</param>
    /// <returns>Its group and binding.</returns>
    /// <exception cref="ArgumentException">The module binds nothing of that name.</exception>
    public (int Group, int Binding) Slot(string name)
    {
        if (_byName.TryGetValue(name, out ShaderBinding? binding))
        {
            return (binding.Group, binding.Binding);
        }

        throw new ArgumentException(
            $"The module {_reflection.Module} binds nothing called '{name}'. It binds: "
            + $"{string.Join(", ", _byName.Keys.Order())}.",
            nameof(name));
    }

    /// <summary>Binds a texture view to the slot the module gave that name.</summary>
    /// <param name="name">The name the Slang source declares.</param>
    /// <param name="view">The view.</param>
    public void Bind(string name, IRhiTextureView view)
    {
        ArgumentNullException.ThrowIfNull(view);
        Set(name, binding => RhiBindGroupEntry.Of(binding, view));
    }

    /// <summary>Binds a texture and the sampler the module reads it through.</summary>
    /// <param name="name">The name the Slang source declares the texture under.</param>
    /// <param name="view">The view.</param>
    /// <param name="sampler">How it is filtered.</param>
    /// <remarks>
    /// The library pairs a texture with a sampler of the same name and the suffix <c>Sampler</c>, because
    /// a combined sampler takes two binding slots on the WGSL target and collides with whatever is
    /// declared next. Binding them together here is that convention stated once: a module that fetches
    /// rather than samples declares no sampler, and this leaves it alone rather than refusing.
    /// </remarks>
    public void Bind(string name, IRhiTextureView view, IRhiSampler sampler)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(sampler);

        Bind(name, view);

        if (Declares(name + SamplerSuffix))
        {
            Bind(name + SamplerSuffix, sampler);
        }
    }

    /// <summary>Binds a sampler.</summary>
    /// <param name="name">The name the Slang source declares.</param>
    /// <param name="sampler">The sampler.</param>
    public void Bind(string name, IRhiSampler sampler)
    {
        ArgumentNullException.ThrowIfNull(sampler);
        Set(name, binding => RhiBindGroupEntry.Of(binding, sampler));
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

    /// <summary>Binds an acceleration structure, for a module that traces rays.</summary>
    /// <param name="name">The name the Slang source declares.</param>
    /// <param name="structure">The structure.</param>
    public void Bind(string name, IRhiAccelerationStructure structure)
    {
        ArgumentNullException.ThrowIfNull(structure);
        Set(name, binding => RhiBindGroupEntry.Of(binding, structure));
    }

    /// <summary>Binds a sampler to every slot the module declares that nothing has bound.</summary>
    /// <param name="sampler">The sampler.</param>
    /// <returns>How many slots it filled.</returns>
    /// <remarks>
    /// For a pass whose textures are all read the same way, which is most of them here. It fills only
    /// what is still empty, so a slot bound deliberately beforehand keeps what it was given — which is
    /// how a comparison sampler survives a call that would otherwise overwrite it with a filtering one.
    /// </remarks>
    public int BindRemainingSamplers(IRhiSampler sampler)
    {
        ArgumentNullException.ThrowIfNull(sampler);
        ObjectDisposedException.ThrowIf(_disposed, this);

        int filled = 0;

        foreach (ShaderBinding binding in _reflection.Bindings)
        {
            if (binding.Kind is not ShaderBindingKind.Sampler || IsBound(binding))
            {
                continue;
            }

            Bind(binding.Name, sampler);
            filled++;
        }

        return filled;
    }

    /// <summary>Binds a texture to every slot the module declares that nothing has bound.</summary>
    /// <param name="choose">What to bind, given the binding's own description of what it holds.</param>
    /// <returns>How many slots it filled.</returns>
    /// <remarks>
    /// For a pass whose shader reads more than the renderer yet produces: a stand-in goes into each
    /// remaining slot, chosen by what the module says the slot is — a volume, an array of slices, a
    /// depth texture — because binding a view of the wrong shape is refused by one backend and
    /// undefined on the other.
    /// </remarks>
    public int BindRemainingTextures(Func<ShaderBinding, IRhiTextureView> choose)
    {
        ArgumentNullException.ThrowIfNull(choose);
        ObjectDisposedException.ThrowIf(_disposed, this);

        int filled = 0;

        foreach (ShaderBinding binding in _reflection.Bindings)
        {
            if (binding.Kind is not ShaderBindingKind.Texture
                || ShaderBindingLayout.IsPushBlock(_reflection, binding)
                || IsBound(binding))
            {
                continue;
            }

            Bind(binding.Name, choose(binding));
            filled++;
        }

        return filled;
    }

    /// <summary>Whether something has been bound to a name.</summary>
    /// <param name="name">The name the Slang source declares.</param>
    /// <returns><see langword="true"/> when a resource is waiting in that slot.</returns>
    public bool IsBound(string name) =>
        _byName.TryGetValue(name, out ShaderBinding? binding) && IsBound(binding);

    /// <summary>The group for a set, built if what it holds has changed.</summary>
    /// <param name="group">Which group.</param>
    /// <returns>The group, ready to bind.</returns>
    /// <exception cref="InvalidOperationException">A slot the module declares was never bound.</exception>
    public IRhiBindGroup GroupFor(int group)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!_stale[group] && _groups[group] is { } kept)
        {
            return kept;
        }

        Dictionary<int, RhiBindGroupEntry> entries =
            _bound.TryGetValue(group, out Dictionary<int, RhiBindGroupEntry>? bound) ? bound : [];

        List<RhiBindGroupEntry> filled = [];

        foreach (RhiBindGroupLayoutEntry slot in ShaderBindingLayout.EntriesFor(_reflection, _stages, group))
        {
            if (!entries.TryGetValue(slot.Binding, out RhiBindGroupEntry value))
            {
                throw new InvalidOperationException(
                    $"The pass '{Label}' runs with nothing bound to slot {slot.Binding} of group "
                    + $"{group}, which its module declares as a {slot.Type}.");
            }

            filled.Add(value);
        }

        List<(RhiBindGroupEntry[] Entries, IRhiBindGroup Group)> recent = _recent[group];

        for (int i = 0; i < recent.Count; i++)
        {
            if (recent[i].Entries.AsSpan().SequenceEqual(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(filled)))
            {
                (RhiBindGroupEntry[] Entries, IRhiBindGroup Group) hit = recent[i];

                // Most recently used last, so the one evicted is the one longest unused.
                recent.RemoveAt(i);
                recent.Add(hit);

                _groups[group] = hit.Group;
                _stale[group] = false;
                return hit.Group;
            }
        }

        IRhiBindGroup built = _device.CreateBindGroup(new RhiBindGroupDescriptor(
            _layouts[group],
            filled,
            $"{Label}-{group}"));

        recent.Add(([.. filled], built));

        if (recent.Count > RecentCapacity)
        {
            recent[0].Group.Dispose();
            recent.RemoveAt(0);
        }

        _groups[group] = built;
        _stale[group] = false;
        GroupsBuilt++;

        return built;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        foreach (List<(RhiBindGroupEntry[] Entries, IRhiBindGroup Group)> recent in _recent)
        {
            foreach ((RhiBindGroupEntry[] _, IRhiBindGroup group) in recent)
            {
                group.Dispose();
            }

            recent.Clear();
        }
    }

    private bool IsBound(ShaderBinding binding) =>
        _bound.TryGetValue(binding.Group, out Dictionary<int, RhiBindGroupEntry>? entries)
        && entries.ContainsKey(binding.Binding);

    private void Set(string name, Func<int, RhiBindGroupEntry> entry)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        (int group, int binding) = Slot(name);

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
}
