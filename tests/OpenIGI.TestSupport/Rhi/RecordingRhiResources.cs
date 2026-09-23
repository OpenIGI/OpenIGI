using OpenIGI.Rendering.Rhi;
using OpenIGI.Shaders;

namespace OpenIGI.TestSupport.Rhi;

/// <summary>Something the recording device handed out, which remembers what it was asked for.</summary>
/// <param name="label">Its name.</param>
public abstract class RecordedResource(string label) : IRhiResource
{
    /// <inheritdoc/>
    public string Label { get; } = label;

    /// <summary>Gets whether this has been disposed, so a leak can be asserted on.</summary>
    public bool IsDisposed { get; private set; }

    /// <inheritdoc/>
    public void Dispose()
    {
        IsDisposed = true;
        GC.SuppressFinalize(this);
    }
}

/// <summary>A buffer that holds whatever was written into it.</summary>
/// <remarks>
/// It keeps its bytes because a readback has to give something back, and giving back what was put in is
/// the only answer that lets a test drive an upload and a readback against each other without a GPU.
/// </remarks>
/// <param name="descriptor">What it was created as.</param>
public sealed class RecordedBuffer(in RhiBufferDescriptor descriptor)
    : RecordedResource(descriptor.Label), IRhiBuffer
{
    /// <inheritdoc/>
    public ulong SizeInBytes { get; } = descriptor.SizeInBytes;

    /// <inheritdoc/>
    public RhiBufferUsage Usage { get; } = descriptor.Usage;

    /// <summary>Gets the bytes it holds.</summary>
    public byte[] Contents { get; } = new byte[descriptor.SizeInBytes];
}

/// <summary>A texture, and the views taken of it.</summary>
/// <param name="descriptor">What it was created as.</param>
public sealed class RecordedTexture(in RhiTextureDescriptor descriptor)
    : RecordedResource(descriptor.Label), IRhiTexture
{
    private readonly List<RecordedTextureView> _views = [];
    private readonly RhiTextureDescriptor _descriptor = descriptor;
    private RecordedTextureView? _whole;

    /// <inheritdoc/>
    public int Width => _descriptor.Width;

    /// <inheritdoc/>
    public int Height => _descriptor.Height;

    /// <inheritdoc/>
    public int DepthOrArrayLayers => _descriptor.DepthOrArrayLayers;

    /// <inheritdoc/>
    public int MipLevelCount => _descriptor.MipLevelCount;

    /// <inheritdoc/>
    public int SampleCount => _descriptor.SampleCount;

    /// <inheritdoc/>
    public RhiTextureFormat Format => _descriptor.Format;

    /// <inheritdoc/>
    public RhiTextureUsage Usage => _descriptor.Usage;

    /// <summary>Gets the views taken of this texture.</summary>
    public IReadOnlyList<RecordedTextureView> Views => _views;

    /// <inheritdoc/>
    public IRhiTextureView View => _whole ??= Remember(new RecordedTextureView(this, Format, Label));

    /// <inheritdoc/>
    public IRhiTextureView CreateView(in RhiTextureViewDescriptor descriptor)
    {
        RhiTextureFormat format = descriptor.Format is RhiTextureFormat.Undefined
            ? Format
            : descriptor.Format;

        string label = descriptor.Label.Length > 0 ? descriptor.Label : Label;
        return Remember(new RecordedTextureView(this, format, label));
    }

    private RecordedTextureView Remember(RecordedTextureView view)
    {
        _views.Add(view);
        return view;
    }
}

/// <summary>A view of a recorded texture.</summary>
/// <param name="texture">What it looks at.</param>
/// <param name="format">The format it is read through.</param>
/// <param name="label">Its name.</param>
public sealed class RecordedTextureView(RecordedTexture texture, RhiTextureFormat format, string label)
    : RecordedResource(label), IRhiTextureView
{
    /// <inheritdoc/>
    public IRhiTexture Texture { get; } = texture;

    /// <inheritdoc/>
    public RhiTextureFormat Format { get; } = format;
}

/// <summary>A sampler, kept as the description it was asked for.</summary>
/// <param name="descriptor">What it was created as.</param>
public sealed class RecordedSampler(in RhiSamplerDescriptor descriptor)
    : RecordedResource(descriptor.Label), IRhiSampler
{
    /// <summary>Gets what it was created as.</summary>
    public RhiSamplerDescriptor Descriptor { get; } = descriptor;
}

/// <summary>A shader module, kept as the identifier it was asked for.</summary>
/// <param name="descriptor">Which module and variant.</param>
public sealed class RecordedShaderModule(in RhiShaderModuleDescriptor descriptor)
    : RecordedResource(descriptor.Label.Length > 0 ? descriptor.Label : descriptor.Module.ToString()),
        IRhiShaderModule
{
    /// <summary>Gets which module of the library this is.</summary>
    public ShaderModuleId Module { get; } = descriptor.Module;

    /// <summary>Gets which compiled form was asked for.</summary>
    public ShaderVariant Variant { get; } = descriptor.Variant;
}

/// <summary>A bind group layout, kept as the shape it was asked for.</summary>
/// <param name="descriptor">Its shape.</param>
public sealed class RecordedBindGroupLayout(RhiBindGroupLayoutDescriptor descriptor)
    : RecordedResource(descriptor.Label), IRhiBindGroupLayout
{
    /// <summary>Gets its slots.</summary>
    public IReadOnlyList<RhiBindGroupLayoutEntry> Entries { get; } = descriptor.Entries;
}

/// <summary>A bind group, kept as what was bound into it.</summary>
/// <param name="descriptor">Its layout and contents.</param>
public sealed class RecordedBindGroup(RhiBindGroupDescriptor descriptor)
    : RecordedResource(descriptor.Label), IRhiBindGroup
{
    /// <summary>Gets the shape it was bound against.</summary>
    public IRhiBindGroupLayout Layout { get; } = descriptor.Layout;

    /// <summary>Gets what fills each slot.</summary>
    public IReadOnlyList<RhiBindGroupEntry> Entries { get; } = descriptor.Entries;
}

/// <summary>A pipeline layout, kept as the groups and push data it declared.</summary>
/// <param name="descriptor">What it was created as.</param>
public sealed class RecordedPipelineLayout(RhiPipelineLayoutDescriptor descriptor)
    : RecordedResource(descriptor.Label), IRhiPipelineLayout
{
    /// <summary>Gets the groups it takes.</summary>
    public IReadOnlyList<IRhiBindGroupLayout> BindGroupLayouts { get; } = descriptor.BindGroupLayouts;

    /// <summary>Gets how many bytes of push data it declared.</summary>
    public int PushDataBytes { get; } = descriptor.PushDataBytes;
}

/// <summary>A graphics pipeline, kept as what it was created from.</summary>
/// <param name="descriptor">What it was created as.</param>
public sealed class RecordedRenderPipeline(RhiRenderPipelineDescriptor descriptor)
    : RecordedResource(descriptor.Label), IRhiRenderPipeline
{
    /// <inheritdoc/>
    public IRhiPipelineLayout Layout { get; } = descriptor.Layout;

    /// <summary>Gets what it was created from.</summary>
    public RhiRenderPipelineDescriptor Descriptor { get; } = descriptor;
}

/// <summary>A compute pipeline, kept as what it was created from.</summary>
/// <param name="descriptor">What it was created as.</param>
public sealed class RecordedComputePipeline(RhiComputePipelineDescriptor descriptor)
    : RecordedResource(descriptor.Label), IRhiComputePipeline
{
    /// <inheritdoc/>
    public IRhiPipelineLayout Layout { get; } = descriptor.Layout;

    /// <summary>Gets what it was created from.</summary>
    public RhiComputePipelineDescriptor Descriptor { get; } = descriptor;
}

/// <summary>A query set, which counts nothing.</summary>
/// <param name="descriptor">What it was created as.</param>
public sealed class RecordedQuerySet(in RhiQuerySetDescriptor descriptor)
    : RecordedResource(descriptor.Label), IRhiQuerySet
{
    /// <inheritdoc/>
    public RhiQueryType Type { get; } = descriptor.Type;

    /// <inheritdoc/>
    public int Count { get; } = descriptor.Count;
}

/// <summary>Recorded work, ready to be handed back to the device.</summary>
/// <param name="label">Its name.</param>
/// <param name="commands">What was recorded into it.</param>
public sealed class RecordedCommandBuffer(string label, IReadOnlyList<RecordedRhiCommand> commands)
    : RecordedResource(label), IRhiCommandBuffer
{
    /// <summary>Gets what was recorded into it.</summary>
    public IReadOnlyList<RecordedRhiCommand> Commands { get; } = commands;
}

/// <summary>
/// A readback that is complete at once, because nothing had to run -- or, where the device holds them
/// back, once it lets them go, which is how a browser's map behaves.
/// </summary>
/// <param name="data">What comes back.</param>
/// <param name="arrived">Whether it has arrived; complete at once where none is given.</param>
public sealed class RecordedReadback(byte[] data, Func<bool>? arrived = null) : IRhiReadback
{
    /// <inheritdoc/>
    public bool IsComplete => arrived?.Invoke() ?? true;

    /// <inheritdoc/>
    public ulong SizeInBytes => (ulong)data.Length;

    /// <inheritdoc/>
    public bool Wait(TimeSpan timeout) => IsComplete;

    /// <inheritdoc/>
    public ReadOnlySpan<byte> GetData() => data;

    /// <inheritdoc/>
    public void Dispose()
    {
        // Nothing was mapped, so nothing has to be unmapped.
    }
}
