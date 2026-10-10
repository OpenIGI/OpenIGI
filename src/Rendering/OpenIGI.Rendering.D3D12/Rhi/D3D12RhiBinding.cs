using OpenIGI.Rendering.D3D12.Sync;
using OpenIGI.Rendering.Rhi;
using Silk.NET.Direct3D12;

namespace OpenIGI.Rendering.D3D12.Rhi;

/// <summary>Where one bind group entry lives once the group is laid out for Direct3D 12.</summary>
/// <param name="Entry">The entry as the RHI declared it.</param>
/// <param name="Table">
/// Its place in the group's CBV/SRV/UAV table, or in its sampler table for a sampler; -1 for a
/// dynamic-offset buffer, which is a root descriptor rather than a table entry.
/// </param>
/// <param name="Dynamic">Its place among the group's dynamic-offset entries, or -1.</param>
internal readonly record struct D3D12BindingSlot(RhiBindGroupLayoutEntry Entry, int Table, int Dynamic);

/// <summary>A bind group layout: the RHI's entries, split into the parts a root signature binds apart.</summary>
/// <remarks>
/// A group becomes up to three kinds of root parameter: one descriptor table of its constant buffers, shader
/// resources and unordered-access views; one table of its samplers, which Direct3D 12 keeps in a heap of their
/// own; and a root descriptor for each buffer bound with a dynamic offset, because a descriptor in a table
/// cannot be moved per bind. The entries keep their order within each part.
/// </remarks>
internal sealed class D3D12RhiBindGroupLayout : IRhiBindGroupLayout
{
    /// <summary>Initialises a new instance of the <see cref="D3D12RhiBindGroupLayout"/> class.</summary>
    /// <param name="descriptor">The layout.</param>
    public D3D12RhiBindGroupLayout(RhiBindGroupLayoutDescriptor descriptor)
    {
        Label = descriptor.Label;

        int tables = 0;
        int samplers = 0;
        int dynamics = 0;
        List<D3D12BindingSlot> slots = [];

        foreach (RhiBindGroupLayoutEntry entry in descriptor.Entries.OrderBy(entry => entry.Binding))
        {
            if (entry.Type is RhiBindingType.Sampler or RhiBindingType.ComparisonSampler)
            {
                slots.Add(new D3D12BindingSlot(entry, samplers++, -1));
            }
            else if (entry.HasDynamicOffset)
            {
                slots.Add(new D3D12BindingSlot(entry, -1, dynamics++));
            }
            else
            {
                slots.Add(new D3D12BindingSlot(entry, tables++, -1));
            }
        }

        Slots = slots;
        TableCount = tables;
        SamplerCount = samplers;
        DynamicCount = dynamics;
    }

    /// <summary>Gets every entry and where it lives, in binding order.</summary>
    public IReadOnlyList<D3D12BindingSlot> Slots { get; }

    /// <summary>Gets how many descriptors the CBV/SRV/UAV table holds.</summary>
    public int TableCount { get; }

    /// <summary>Gets how many descriptors the sampler table holds.</summary>
    public int SamplerCount { get; }

    /// <summary>Gets how many entries are dynamic-offset root descriptors.</summary>
    public int DynamicCount { get; }

    /// <summary>Finds an entry by its binding number.</summary>
    /// <param name="binding">The binding.</param>
    /// <returns>The slot, or null where the layout has none.</returns>
    public D3D12BindingSlot? Find(int binding)
    {
        foreach (D3D12BindingSlot slot in Slots)
        {
            if (slot.Entry.Binding == binding)
            {
                return slot;
            }
        }

        return null;
    }

    /// <inheritdoc/>
    public string Label { get; }

    /// <inheritdoc/>
    public void Dispose()
    {
    }
}

/// <summary>A bind group: its run of each shader-visible heap, already written, and its dynamic buffers.</summary>
internal sealed class D3D12RhiBindGroup : IRhiBindGroup
{
    private readonly D3D12RhiDevice _device;
    private bool _disposed;

    /// <summary>Initialises a new instance of the <see cref="D3D12RhiBindGroup"/> class.</summary>
    /// <param name="device">The device.</param>
    /// <param name="layout">Its layout.</param>
    /// <param name="table">The first descriptor of its CBV/SRV/UAV run, or -1.</param>
    /// <param name="samplers">Its shared sampler table, or null.</param>
    /// <param name="dynamic">Each dynamic entry's buffer address before its offset, in layout order.</param>
    /// <param name="uses">What a draw or dispatch through it does with each resource it holds.</param>
    /// <param name="label">A name.</param>
    public D3D12RhiBindGroup(
        D3D12RhiDevice device,
        D3D12RhiBindGroupLayout layout,
        int table,
        D3D12SamplerTable? samplers,
        ulong[] dynamic,
        (object Resource, D3D12Use Use)[] uses,
        string label)
    {
        _device = device;
        Uses = uses;
        Layout = layout;
        Table = table;
        Samplers = samplers;
        DynamicAddresses = dynamic;
        Label = label;
    }

    /// <summary>Gets its layout.</summary>
    public D3D12RhiBindGroupLayout Layout { get; }

    /// <summary>Gets the first descriptor of its CBV/SRV/UAV run in the shader-visible heap, or -1.</summary>
    public int Table { get; }

    /// <summary>Gets its sampler table, or null.</summary>
    public D3D12SamplerTable? Samplers { get; }

    /// <summary>Gets what a draw or dispatch through it does with each resource it holds.</summary>
    public (object Resource, D3D12Use Use)[] Uses { get; }

    /// <summary>Gets each dynamic entry's GPU address before the offset a bind supplies.</summary>
    public ulong[] DynamicAddresses { get; }

    /// <inheritdoc/>
    public string Label { get; }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        int table = Table;
        int count = Layout.TableCount;
        D3D12SamplerTable? samplers = Samplers;
        _device.Retire(() =>
        {
            if (table >= 0)
            {
                _device.ShaderHeap.Free(table, count);
            }

            samplers?.Release();
        });
    }
}

/// <summary>A run of the sampler heap holding one sequence of samplers, shared by every group that binds it.</summary>
/// <remarks>
/// The shader-visible sampler heap holds 2048 descriptors at most, and a level has thousands of material
/// groups -- but only a handful of distinct sampler sets among them. So a table is keyed by its samplers and
/// counted, and a group that binds the same set binds the same table.
/// </remarks>
internal sealed class D3D12SamplerTable
{
    private readonly D3D12RhiDevice _device;
    private int _references;

    /// <summary>Initialises a new instance of the <see cref="D3D12SamplerTable"/> class.</summary>
    /// <param name="device">The device.</param>
    /// <param name="key">The samplers' identities, in table order.</param>
    /// <param name="start">The first descriptor.</param>
    /// <param name="count">How many.</param>
    public D3D12SamplerTable(D3D12RhiDevice device, string key, int start, int count)
    {
        _device = device;
        Key = key;
        Start = start;
        Count = count;
    }

    /// <summary>Gets the key it is shared under.</summary>
    public string Key { get; }

    /// <summary>Gets the first descriptor.</summary>
    public int Start { get; }

    /// <summary>Gets how many.</summary>
    public int Count { get; }

    /// <summary>Takes a reference.</summary>
    public void Acquire() => _references++;

    /// <summary>Gives a reference back, freeing the table with the last.</summary>
    public void Release()
    {
        if (--_references == 0)
        {
            _device.ForgetSamplerTable(this);
        }
    }
}

/// <summary>A pipeline layout: the groups and the push data a root signature is built over.</summary>
/// <param name="descriptor">The layout.</param>
internal sealed class D3D12RhiPipelineLayout(RhiPipelineLayoutDescriptor descriptor) : IRhiPipelineLayout
{
    /// <summary>Gets the group layouts, by group index.</summary>
    public IReadOnlyList<D3D12RhiBindGroupLayout> Groups { get; } =
        [.. descriptor.BindGroupLayouts.Select(layout => (D3D12RhiBindGroupLayout)layout)];

    /// <summary>Gets how many bytes of push data a pass may set.</summary>
    public int PushDataBytes { get; } = descriptor.PushDataBytes;

    /// <summary>Gets an identity for root-signature caching.</summary>
    public int Identity { get; } = Interlocked.Increment(ref _next);

    private static int _next;

    /// <inheritdoc/>
    public string Label { get; } = descriptor.Label;

    /// <inheritdoc/>
    public void Dispose()
    {
    }
}

/// <summary>Where a root signature put each group's parameters, so a bind sets the right ones.</summary>
/// <param name="Table">The CBV/SRV/UAV table's root parameter, or -1 where the shaders read none of it.</param>
/// <param name="Samplers">The sampler table's root parameter, or -1.</param>
/// <param name="Dynamic">
/// Each dynamic entry's root parameter and kind, in layout order; a parameter of -1 is an entry no shader
/// reads.
/// </param>
internal readonly record struct D3D12GroupParameters(
    int Table,
    int Samplers,
    (int Parameter, RootParameterType Type)[] Dynamic);
