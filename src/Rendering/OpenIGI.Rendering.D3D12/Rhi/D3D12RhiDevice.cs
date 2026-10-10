using OpenIGI.Core.Diagnostics;
using OpenIGI.Rendering.D3D12.Descriptors;
using OpenIGI.Rendering.D3D12.Interop;
using OpenIGI.Rendering.Rhi;
using OpenIGI.Shaders;
using Silk.NET.Direct3D12;

namespace OpenIGI.Rendering.D3D12.Rhi;

/// <summary>The RHI over Direct3D 12.</summary>
/// <remarks>
/// <para>
/// A translation of the interface and nothing more. One direct queue, one fence, enhanced barriers decided
/// by the encoders, one shader-visible heap of each kind for the life of the device, and root signatures
/// built from the shaders' own register placements. Everything that decides what a frame is lives above the
/// RHI and is shared with the other backends.
/// </para>
/// <para>
/// Nothing is freed while the GPU may still read it: a disposed resource, a bind group's descriptors and a
/// command list's allocator are each retired against the next fence value and released once the queue has
/// passed it.
/// </para>
/// </remarks>
internal sealed unsafe partial class D3D12RhiDevice : IRhiDevice
{
    /// <summary>How many frames may be in flight, which is how many sets of per-frame resources the renderer keeps.</summary>
    public const int FramesInFlight = 2;

    private readonly IOpenIgiLogger _logger;
    private readonly List<(ulong Ticket, Action Release)> _retired = [];
    private readonly Dictionary<string, D3D12RootSignature> _rootSignatures = new(StringComparer.Ordinal);
    private readonly Dictionary<string, D3D12SamplerTable> _samplerTables = new(StringComparer.Ordinal);
    private ID3D12CommandSignature* _draw;
    private ID3D12CommandSignature* _drawIndexed;
    private ID3D12CommandSignature* _dispatch;
    private readonly D3D12RhiAccelerationStructures? _accelerationStructures;
    private readonly Memory.D3D12BufferPool _defaultPool;
    private readonly Memory.D3D12BufferPool _uploadPool;
    private readonly Memory.D3D12BufferPool? _gpuUploadPool;
    private D3D12RhiBuffer? _zeroes;
    private bool _disposed;

    /// <summary>Initialises a new instance of the <see cref="D3D12RhiDevice"/> class.</summary>
    /// <param name="context">The device and queue, which this owns.</param>
    /// <param name="logger">Where it reports.</param>
    public D3D12RhiDevice(D3D12Context context, IOpenIgiLogger logger)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(logger);

        Context = context;
        _logger = logger;
        Api = Silk.NET.Direct3D12.D3D12.GetApi();

        _defaultPool = new Memory.D3D12BufferPool(Native, HeapType.Default, "device buffers");
        _uploadPool = new Memory.D3D12BufferPool(Native, HeapType.Upload, "upload buffers");
        _gpuUploadPool = context.Capabilities.GpuUploadHeap ? new Memory.D3D12BufferPool(Native, GpuUploadHeap, "GPU upload buffers") : null;

        // Tier 3 binding allows a million and more; the traced world's bindless views live here too.
        ShaderHeap = new D3D12DescriptorHeap(Native, DescriptorHeapType.CbvSrvUav, 1_000_000, shaderVisible: true, "shader heap");
        SamplerHeap = new D3D12DescriptorHeap(Native, DescriptorHeapType.Sampler, 2048, shaderVisible: true, "sampler heap");
        RtvHeap = new D3D12DescriptorHeap(Native, DescriptorHeapType.Rtv, 4096, shaderVisible: false, "render targets");
        DsvHeap = new D3D12DescriptorHeap(Native, DescriptorHeapType.Dsv, 1024, shaderVisible: false, "depth targets");

        _draw = CreateSignature(IndirectArgumentType.Draw, 16, "draw indirect");
        _drawIndexed = CreateSignature(IndirectArgumentType.DrawIndexed, 20, "draw indexed indirect");
        _dispatch = CreateSignature(IndirectArgumentType.Dispatch, 12, "dispatch indirect");

        Features = new RhiFeatures(
            RayQuery: context.Capabilities.RaytracingTier >= 11,
            TimestampQuery: true,
            Float32Filterable: true,
            DepthClamp: true,
            IndirectFirstInstance: true,
            TextureCompressionBc: true,
            StrictTextureSampleTypes: false);

        Limits = new RhiLimits(
            MaxTextureDimension2D: 16384,
            MaxTextureDimension3D: 2048,
            MaxTextureArrayLayers: 2048,
            MaxBindGroups: 8,
            MaxSamplersPerStage: 2048,
            MaxSampledTexturesPerStage: 1_000_000,
            MaxStorageBuffersPerStage: 1_000_000,
            MaxStorageTexturesPerStage: 1_000_000,
            MaxUniformBufferBindingSize: 65536,
            MaxStorageBufferBindingSize: 1UL << 31,
            MaxPushDataBytes: 128,
            MaxComputeWorkgroupStorageBytes: 32768,
            MaxComputeInvocationsPerWorkgroup: 1024,
            MaxComputeWorkgroupsPerDimension: 65535,
            MaxColorAttachments: 8,
            MaxSampleCount: 8);

        CreateUploads();
        Pipelines = new Pipelines.D3D12PipelineCache(Native, context.AdapterName, logger);
        _accelerationStructures = Features.RayQuery ? new D3D12RhiAccelerationStructures(this) : null;
    }

    /// <summary>Gets the device, the queue and the fence.</summary>
    public D3D12Context Context { get; }

    /// <summary>Gets the Direct3D 12 entry points outside any interface: root signature serialisation.</summary>
    public Silk.NET.Direct3D12.D3D12 Api { get; }

    /// <summary>Gets the device.</summary>
    public ID3D12Device10* Native => Context.Device;

    /// <summary>The pool small buffers of a heap type share, or null where that heap is not pooled.</summary>
    /// <param name="heap">The heap type.</param>
    /// <returns>The pool.</returns>
    public Memory.D3D12BufferPool? PoolFor(HeapType heap) => heap switch
    {
        HeapType.Default => _defaultPool,
        HeapType.Upload => _uploadPool,
        GpuUploadHeap => _gpuUploadPool,
        _ => null,
    };

    /// <summary>Gets the pipeline library every pipeline is compiled through.</summary>
    public Pipelines.D3D12PipelineCache Pipelines { get; }

    /// <summary>Gets the shader-visible CBV/SRV/UAV heap.</summary>
    public D3D12DescriptorHeap ShaderHeap { get; }

    /// <summary>Gets the shader-visible sampler heap.</summary>
    public D3D12DescriptorHeap SamplerHeap { get; }

    /// <summary>Gets the render-target descriptor heap.</summary>
    public D3D12DescriptorHeap RtvHeap { get; }

    /// <summary>Gets the depth-stencil descriptor heap.</summary>
    public D3D12DescriptorHeap DsvHeap { get; }

    /// <summary>Gets the command signature an indirect draw executes through.</summary>
    public ID3D12CommandSignature* DrawSignature => _draw;

    /// <summary>Gets the command signature an indirect indexed draw executes through.</summary>
    public ID3D12CommandSignature* DrawIndexedSignature => _drawIndexed;

    /// <summary>Gets the command signature an indirect dispatch executes through.</summary>
    public ID3D12CommandSignature* DispatchSignature => _dispatch;

    /// <inheritdoc/>
    public string AdapterName => Context.AdapterName;

    /// <inheritdoc/>
    public string ApiName => "Direct3D 12";

    /// <inheritdoc/>
    /// <remarks>The direct queue's tick frequency, as a period.</remarks>
    public double TimestampPeriodNanoseconds => 1e9 / Context.Capabilities.TimestampFrequency;

    /// <inheritdoc/>
    public RhiFeatures Features { get; }

    /// <inheritdoc/>
    public ShaderTarget ShaderTarget => ShaderTarget.Dxil;

    /// <inheritdoc/>
    public RhiLimits Limits { get; }

    /// <inheritdoc/>
    /// <remarks>Where the adapter offers DXR 1.1, the tier inline ray queries need.</remarks>
    public IRhiAccelerationStructures? AccelerationStructures => _accelerationStructures;

    /// <summary>Releases something once no work recorded before this call can still reach it.</summary>
    /// <param name="release">What to run then.</param>
    /// <remarks>
    /// <para>
    /// Not dated yet: an encoder still recording may name what is released, and it may be submitted after
    /// any number of other submissions -- a level adopted in the middle of a frame sends its uploads early,
    /// several times over, while the frame's own encoder is still open. Dating a release by the next signal
    /// freed memory that frame went on to read: the device was removed with an access fault.
    /// </para>
    /// <para>
    /// So a release is dated by the first submission made once no encoder is open -- everything recorded
    /// before it has then been submitted, at or before that signal.
    /// </para>
    /// </remarks>
    public void Retire(Action release)
    {
        ArgumentNullException.ThrowIfNull(release);
        if (_disposed)
        {
            release();
            return;
        }

        _retired.Add((Undated, release));
    }

    /// <summary>The ticket of a release no submission has dated yet.</summary>
    private const ulong Undated = ulong.MaxValue;

    // How many encoders the RHI's callers hold open: created and neither finished nor dropped.
    private int _openEncoders;

    /// <summary>Notes an encoder handed out.</summary>
    internal void EncoderOpened() => _openEncoders++;

    /// <summary>Notes an encoder finished or dropped.</summary>
    internal void EncoderClosed() => _openEncoders--;

    /// <summary>Dates every undated release by a submission's signal, if nothing recorded before it is still unsubmitted.</summary>
    /// <param name="ticket">The submission's fence value.</param>
    private void DateReleases(ulong ticket)
    {
        if (_openEncoders > 0)
        {
            return;
        }

        for (int i = 0; i < _retired.Count; i++)
        {
            if (_retired[i].Ticket == Undated)
            {
                _retired[i] = (ticket, _retired[i].Release);
            }
        }
    }

    /// <summary>Runs every release whose fence value the queue has passed.</summary>
    public void ReleaseRetired()
    {
        if (_retired.Count == 0)
        {
            return;
        }

        ulong completed = Context.Completed;
        for (int i = 0; i < _retired.Count; i++)
        {
            if (_retired[i].Ticket > completed)
            {
                continue;
            }

            _retired[i].Release();
            _retired.RemoveAt(i);
            i--;
        }
    }

    /// <summary>The buffer of zeroes a clear is copied from, in pieces as long as it is.</summary>
    /// <remarks>
    /// Committed without <c>CREATE_NOT_ZEROED</c>, so the runtime hands it over zeroed; it is never
    /// written, so it stays that way.
    /// </remarks>
    public D3D12RhiBuffer Zeroes => _zeroes ??= CreateZeroes();

    private D3D12RhiBuffer CreateZeroes()
    {
        const ulong Size = 4UL * 1024 * 1024;
        ResourceDesc1 description = new()
        {
            Dimension = ResourceDimension.Buffer,
            Width = Size,
            Height = 1,
            DepthOrArraySize = 1,
            MipLevels = 1,
            SampleDesc = new Silk.NET.DXGI.SampleDesc(1, 0),
            Layout = TextureLayout.LayoutRowMajor,
        };

        HeapProperties properties = new() { Type = HeapType.Default };
        ID3D12Resource* resource = null;
        Guid iid = ID3D12Resource.Guid;
        D3D12Check.Ok(
            Native->CreateCommittedResource3(&properties, HeapFlags.None, &description, BarrierLayout.Undefined, null, null, 0, null, &iid, (void**)&resource),
            "CreateCommittedResource3 (zeroes)");
        D3D12Names.Set((ID3D12Object*)resource, "zeroes");
        return new D3D12RhiBuffer(this, resource, new RhiBufferDescriptor(Size, RhiBufferUsage.CopySource, "zeroes"), HeapType.Default, Size);
    }

    /// <summary>The root signature for a layout and the shaders bound through it, made once and shared.</summary>
    /// <param name="layout">The pipeline layout.</param>
    /// <param name="reflections">The stages' reflections.</param>
    /// <param name="graphics">Whether it serves a graphics pipeline.</param>
    /// <param name="label">A name for a new one.</param>
    /// <returns>The root signature.</returns>
    public D3D12RootSignature RootSignatureFor(
        D3D12RhiPipelineLayout layout,
        IReadOnlyList<ShaderReflection> reflections,
        bool graphics,
        string label)
    {
        string key = $"{layout.Identity}|{graphics}|{string.Join(",", reflections.Select(r => $"{r.Module}.{r.Variant}"))}";
        if (!_rootSignatures.TryGetValue(key, out D3D12RootSignature? signature))
        {
            signature = D3D12RootSignature.Build(this, layout, reflections, graphics, label);
            _rootSignatures[key] = signature;
        }

        return signature;
    }

    /// <summary>The sampler table holding a sequence of samplers, shared with every group that binds the same.</summary>
    /// <param name="samplers">The samplers, in table order.</param>
    /// <returns>The table, its reference taken for the caller.</returns>
    public D3D12SamplerTable SamplerTableFor(IReadOnlyList<D3D12RhiSampler> samplers)
    {
        // By what the samplers do, not which objects they are: a level makes the same sampler many times.
        string key = string.Join("|", samplers.Select(sampler => (sampler.Descriptor with { Label = string.Empty }).ToString()));
        if (!_samplerTables.TryGetValue(key, out D3D12SamplerTable? table))
        {
            int start = SamplerHeap.Allocate(samplers.Count);
            for (int i = 0; i < samplers.Count; i++)
            {
                WriteSampler(samplers[i].Descriptor, SamplerHeap.Cpu(start + i));
            }

            table = new D3D12SamplerTable(this, key, start, samplers.Count);
            _samplerTables[key] = table;
        }

        table.Acquire();
        return table;
    }

    /// <summary>Forgets a sampler table its last group has let go of.</summary>
    /// <param name="table">The table.</param>
    public void ForgetSamplerTable(D3D12SamplerTable table)
    {
        _samplerTables.Remove(table.Key);
        SamplerHeap.Free(table.Start, table.Count);
    }

    /// <inheritdoc/>
    public string DescribeMemory()
    {
        (ulong budget, ulong usage) = Context.LocalMemory();
        return string.Join(
            '\n',
            "Direct3D 12:",
            FormattableString.Invariant($"  local memory: {usage / (1024 * 1024)} MiB of a {budget / (1024 * 1024)} MiB budget"),
            FormattableString.Invariant($"  shader heap: {ShaderHeap.InUse}/{ShaderHeap.Capacity}, samplers {SamplerHeap.InUse}/{SamplerHeap.Capacity}"),
            FormattableString.Invariant($"  render targets: {RtvHeap.InUse}, depth targets: {DsvHeap.InUse}"),
            FormattableString.Invariant($"  upload staging: {_freeStaging.Count} spare chunk(s), {_usedStaging.Count} in flight; {_retired.Count} release(s) waiting"));
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        Context.WaitIdle();
        ReleaseSwapchain();
        _zeroes?.Dispose();
        DisposeUploads();
        DisposeLists();

        _disposed = true;
        foreach ((_, Action release) in _retired)
        {
            release();
        }

        _retired.Clear();
        _defaultPool.Dispose();
        _uploadPool.Dispose();
        _gpuUploadPool?.Dispose();

        foreach (D3D12RootSignature signature in _rootSignatures.Values)
        {
            signature.Dispose();
        }

        _rootSignatures.Clear();
        Pipelines.Dispose();
        D3D12Check.Release(ref _draw);
        D3D12Check.Release(ref _drawIndexed);
        D3D12Check.Release(ref _dispatch);
        ShaderHeap.Dispose();
        SamplerHeap.Dispose();
        RtvHeap.Dispose();
        DsvHeap.Dispose();
        Context.Dispose();
    }

    private void WriteSampler(in RhiSamplerDescriptor descriptor, CpuDescriptorHandle destination)
    {
        SamplerDesc description = new()
        {
            Filter = D3D12Translation.SamplerFilter(descriptor),
            AddressU = D3D12Translation.AddressMode(descriptor.AddressU),
            AddressV = D3D12Translation.AddressMode(descriptor.AddressV),
            AddressW = D3D12Translation.AddressMode(descriptor.AddressW),
            MipLODBias = descriptor.MipLodBias,
            MaxAnisotropy = (uint)Math.Clamp(descriptor.MaxAnisotropy, 1, 16),
            ComparisonFunc = descriptor.Compare is { } compare ? D3D12Translation.Compare(compare) : Silk.NET.Direct3D12.ComparisonFunc.Never,
            MinLOD = descriptor.LodMin,
            MaxLOD = descriptor.LodMax,
        };

        Native->CreateSampler(&description, destination);
    }

    private ID3D12CommandSignature* CreateSignature(IndirectArgumentType type, uint stride, string label)
    {
        IndirectArgumentDesc argument = new() { Type = type };
        CommandSignatureDesc description = new()
        {
            ByteStride = stride,
            NumArgumentDescs = 1,
            PArgumentDescs = &argument,
        };

        ID3D12CommandSignature* signature = null;
        Guid iid = ID3D12CommandSignature.Guid;
        D3D12Check.Ok(Native->CreateCommandSignature(&description, null, &iid, (void**)&signature), $"CreateCommandSignature ({label})");
        return signature;
    }
}
