using System.Runtime.InteropServices;
using OpenIGI.Rendering.Rhi;
using OpenIGI.Shaders;

namespace OpenIGI.TestSupport.Rhi;

/// <summary>
/// A device that writes the frame down instead of drawing it.
/// </summary>
/// <remarks>
/// <para>
/// The pipeline-level twin of <see cref="RecordingGraphicsDevice"/>, and the instrument every pass moved
/// onto the RHI is checked with. A pass is worth asserting about at this level because what it promises
/// is a shape: this pass runs before that one, it draws into these attachments, it clears the first and
/// loads the second, it binds this group at this index with these offsets. All of that is decided on the
/// CPU and none of it needs a GPU to observe.
/// </para>
/// <para>
/// It is not a simulator. Nothing shades, nothing rasterises, and a readback returns what was written
/// rather than what would have been computed. Where a test needs the answer rather than the shape, it
/// runs against a real backend — both of which are already proved to agree.
/// </para>
/// </remarks>
public sealed class RecordingRhiDevice : IRhiDevice
{
    private readonly List<RecordedRhiCommand> _commands = [];
    private readonly List<RecordedResource> _resources = [];
    private RecordedTexture? _frameTarget;
    private int _frameIndex;
    private int _frameSlot;
    private bool _inFrame;
    private int _readbacksReleased;
    private int _readbacksAsked;

    /// <summary>Initialises a new instance of the <see cref="RecordingRhiDevice"/> class.</summary>
    /// <param name="features">What it claims to be able to do.</param>
    /// <param name="limits">The sizes it claims not to exceed, or the WebGPU floor when null.</param>
    /// <param name="width">The width of the frames it hands out.</param>
    /// <param name="height">Their height.</param>
    public RecordingRhiDevice(
        RhiFeatures features = default,
        RhiLimits? limits = null,
        int width = 1280,
        int height = 720)
    {
        Features = features;
        Limits = limits ?? RhiLimits.WebGpuDefaults;
        SurfaceWidth = width;
        SurfaceHeight = height;
    }

    /// <inheritdoc/>
    public string AdapterName => "Recording";

    /// <summary>Gets or sets whether readbacks stay pending until <see cref="ReleaseReadbacks"/>.</summary>
    /// <remarks>
    /// A browser's map comes back on a later task, never inside the call that asked for it; this is how a
    /// test stands in for that.
    /// </remarks>
    public bool HoldsReadbacks { get; set; }

    /// <summary>Lets every readback asked for so far arrive.</summary>
    public void ReleaseReadbacks() => _readbacksReleased = _readbacksAsked;

    /// <inheritdoc/>
    public RhiFeatures Features { get; }

    /// <inheritdoc/>
    /// <remarks>SPIR-V unless a test says otherwise, which is the target every module carries.</remarks>
    public ShaderTarget ShaderTarget { get; init; } = ShaderTarget.Spirv;

    /// <inheritdoc/>
    public RhiLimits Limits { get; }

    /// <inheritdoc/>
    /// <remarks>
    /// The format a real swapchain most often hands out, so a renderer built against this records the
    /// pipelines it would record on a desktop.
    /// </remarks>
    public RhiTextureFormat SurfaceFormat { get; init; } = RhiTextureFormat.Bgra8Unorm;

    /// <inheritdoc/>
    /// <remarks>
    /// Null unless a test supplies one, whatever <see cref="RhiFeatures.RayQuery"/> says: recording a
    /// trace would mean recording an acceleration structure, and a pass that stands down without one is
    /// exactly what wants testing. A test that watches the world's lifetime hands in a stand-in.
    /// </remarks>
    public IRhiRayTracing? RayTracing { get; init; }

    /// <summary>Gets everything the device was asked to do, in order.</summary>
    public IReadOnlyList<RecordedRhiCommand> Commands => _commands;

    /// <summary>Gets everything the device handed out.</summary>
    public IReadOnlyList<RecordedResource> Resources => _resources;

    /// <summary>Gets the width of the frames it hands out.</summary>
    public int SurfaceWidth { get; private set; }

    /// <summary>Gets their height.</summary>
    public int SurfaceHeight { get; private set; }

    /// <summary>Gets how many times <see cref="WaitIdle"/> was called.</summary>
    public int WaitIdleCount { get; private set; }

    /// <summary>Gets or sets what the next <see cref="BeginFrame"/> answers.</summary>
    /// <remarks>
    /// Set false to make the caller take the reconfigure path, which is otherwise reached only by
    /// resizing a real window while a real swapchain goes out of date.
    /// </remarks>
    public bool FrameAvailable { get; set; } = true;

    /// <summary>Forgets everything recorded so far, keeping the resources.</summary>
    /// <remarks>Between frames, so one frame's assertions are not read against another's commands.</remarks>
    public void ClearCommands() => _commands.Clear();

    /// <summary>Gets the commands of one kind, in order.</summary>
    /// <typeparam name="T">The kind.</typeparam>
    /// <returns>Those commands.</returns>
    public IReadOnlyList<T> CommandsOf<T>()
        where T : RecordedRhiCommand => [.. _commands.OfType<T>()];

    /// <summary>Gets the labels of the passes that ran, in order.</summary>
    /// <remarks>
    /// The single most useful thing to assert about a frame, and the thing a picture cannot tell you.
    /// </remarks>
    public IReadOnlyList<string> PassOrder() =>
    [
        .. _commands.Select(command => command switch
        {
            RecordedRhiCommand.BeginRenderPass render => render.Label,
            RecordedRhiCommand.BeginComputePass compute => compute.Label,
            _ => null,
        }).OfType<string>(),
    ];

    /// <inheritdoc/>
    public IRhiBuffer CreateBuffer(in RhiBufferDescriptor descriptor) => Keep(new RecordedBuffer(descriptor));

    /// <inheritdoc/>
    public IRhiTexture CreateTexture(in RhiTextureDescriptor descriptor) =>
        Keep(new RecordedTexture(descriptor));

    /// <inheritdoc/>
    public IRhiSampler CreateSampler(in RhiSamplerDescriptor descriptor) =>
        Keep(new RecordedSampler(descriptor));

    /// <inheritdoc/>
    public IRhiShaderModule CreateShaderModule(in RhiShaderModuleDescriptor descriptor) =>
        Keep(new RecordedShaderModule(descriptor));

    /// <inheritdoc/>
    public IRhiBindGroupLayout CreateBindGroupLayout(RhiBindGroupLayoutDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        return Keep(new RecordedBindGroupLayout(descriptor));
    }

    /// <inheritdoc/>
    public IRhiBindGroup CreateBindGroup(RhiBindGroupDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        RequireEverySlotFilled(descriptor);

        return Keep(new RecordedBindGroup(descriptor));
    }

    /// <inheritdoc/>
    public IRhiPipelineLayout CreatePipelineLayout(RhiPipelineLayoutDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);

        if (descriptor.PushDataBytes > Limits.MaxPushDataBytes)
        {
            throw new ArgumentOutOfRangeException(
                nameof(descriptor),
                $"The layout '{descriptor.Label}' declares {descriptor.PushDataBytes} bytes of push data, "
                + $"and this device offers {Limits.MaxPushDataBytes}.");
        }

        return Keep(new RecordedPipelineLayout(descriptor));
    }

    /// <inheritdoc/>
    public IRhiRenderPipeline CreateRenderPipeline(RhiRenderPipelineDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        return Keep(new RecordedRenderPipeline(descriptor));
    }

    /// <inheritdoc/>
    public IRhiComputePipeline CreateComputePipeline(RhiComputePipelineDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        return Keep(new RecordedComputePipeline(descriptor));
    }

    /// <inheritdoc/>
    public IRhiQuerySet CreateQuerySet(in RhiQuerySetDescriptor descriptor) =>
        Keep(new RecordedQuerySet(descriptor));

    /// <inheritdoc/>
    public IRhiCommandEncoder CreateCommandEncoder(string label = "") => new RecordingRhiEncoder(label);

    /// <inheritdoc/>
    public void Submit(ReadOnlySpan<IRhiCommandBuffer> commands)
    {
        List<string> labels = [];

        foreach (IRhiCommandBuffer command in commands)
        {
            labels.Add(command.Label);

            // The encoder's own record is folded into the device's, so the frame reads as one sequence
            // whether it was recorded on one encoder or five.
            if (command is RecordedCommandBuffer recorded)
            {
                _commands.AddRange(recorded.Commands);
            }
        }

        _commands.Add(new RecordedRhiCommand.Submit(labels));
    }

    /// <inheritdoc/>
    public void Flush() => _commands.Add(new RecordedRhiCommand.Flush());

    /// <inheritdoc/>
    public void WriteBuffer<T>(IRhiBuffer buffer, ulong offset, ReadOnlySpan<T> data)
        where T : unmanaged
    {
        ArgumentNullException.ThrowIfNull(buffer);

        ReadOnlySpan<byte> bytes = MemoryMarshal.AsBytes(data);

        if (buffer is RecordedBuffer recorded)
        {
            bytes.CopyTo(recorded.Contents.AsSpan((int)offset));
        }

        _commands.Add(new RecordedRhiCommand.WriteBuffer(buffer.Label, offset, bytes.Length));
    }

    /// <inheritdoc/>
    public void WriteTexture<T>(
        in RhiTextureRegion destination,
        ReadOnlySpan<T> data,
        int bytesPerRow,
        in RhiExtent3D size)
        where T : unmanaged =>
        _commands.Add(new RecordedRhiCommand.WriteTexture(
            destination.Texture.Label,
            MemoryMarshal.AsBytes(data).Length));

    /// <inheritdoc/>
    public IRhiReadback ReadBuffer(IRhiBuffer buffer, ulong offset = 0, ulong sizeInBytes = 0)
    {
        ArgumentNullException.ThrowIfNull(buffer);

        ulong length = sizeInBytes == 0 ? buffer.SizeInBytes - offset : sizeInBytes;

        byte[] data = buffer is RecordedBuffer recorded
            ? recorded.Contents.AsSpan((int)offset, (int)length).ToArray()
            : new byte[length];

        if (!HoldsReadbacks)
        {
            return new RecordedReadback(data);
        }

        int ticket = ++_readbacksAsked;
        return new RecordedReadback(data, () => _readbacksReleased >= ticket);
    }

    /// <inheritdoc/>
    public bool BeginFrame(out RhiFrame frame)
    {
        frame = default;

        if (_inFrame)
        {
            throw new InvalidOperationException("A frame is already begun; end it before beginning another.");
        }

        if (!FrameAvailable)
        {
            return false;
        }

        _frameTarget = new RecordedTexture(new RhiTextureDescriptor(
            SurfaceWidth,
            SurfaceHeight,
            RhiTextureFormat.Bgra8UnormSrgb,
            RhiTextureUsage.RenderTarget,
            Label: "swapchain"));

        _inFrame = true;
        _commands.Add(new RecordedRhiCommand.BeginFrame(_frameIndex, _frameSlot));

        frame = new RhiFrame(_frameTarget.View, _frameIndex, _frameSlot);
        return true;
    }

    /// <inheritdoc/>
    public bool EndFrame(in RhiFrame frame)
    {
        if (!_inFrame)
        {
            throw new InvalidOperationException("No frame is begun, so there is none to end.");
        }

        _commands.Add(new RecordedRhiCommand.EndFrame(frame.Index));
        _inFrame = false;
        _frameIndex++;

        // Two frames in flight, which is what the Vulkan side rotates through and therefore the number a
        // pass that indexes per-frame resources has to cope with.
        _frameSlot = (_frameSlot + 1) % 2;
        return true;
    }

    /// <inheritdoc/>
    public void ConfigureSurface(int width, int height, bool vsync)
    {
        SurfaceWidth = width;
        SurfaceHeight = height;
    }

    /// <inheritdoc/>
    public void WaitIdle() => WaitIdleCount++;

    /// <inheritdoc/>
    public void Dispose()
    {
        foreach (RecordedResource resource in _resources)
        {
            resource.Dispose();
        }

        GC.SuppressFinalize(this);
    }

    /// <summary>Checks that a group fills every slot its layout declares, with the right kind of thing.</summary>
    /// <remarks>
    /// The one piece of validation worth doing here, because it is the mistake that costs the most: both
    /// backends reject a mismatched group, but one of them does it inside a driver with a message about
    /// descriptor sets and the other inside a browser. Said here, it names the binding.
    /// </remarks>
    private static void RequireEverySlotFilled(RhiBindGroupDescriptor descriptor)
    {
        if (descriptor.Layout is not RecordedBindGroupLayout layout)
        {
            return;
        }

        foreach (RhiBindGroupLayoutEntry slot in layout.Entries)
        {
            RhiBindGroupEntry? filled = null;

            foreach (RhiBindGroupEntry entry in descriptor.Entries)
            {
                if (entry.Binding == slot.Binding)
                {
                    filled = entry;
                    break;
                }
            }

            if (filled is not { } value)
            {
                throw new ArgumentException(
                    $"The group '{descriptor.Label}' leaves binding {slot.Binding} of its layout empty.",
                    nameof(descriptor));
            }

            bool matches = slot.Type switch
            {
                RhiBindingType.Sampler or RhiBindingType.ComparisonSampler => value.Sampler is not null,
                RhiBindingType.SampledTexture or RhiBindingType.StorageTexture => value.TextureView is not null,
                _ => value.Buffer is not null,
            };

            if (!matches)
            {
                throw new ArgumentException(
                    $"Binding {slot.Binding} of the group '{descriptor.Label}' holds a {slot.Type} in its "
                    + "layout, and something else was bound to it.",
                    nameof(descriptor));
            }
        }
    }

    private T Keep<T>(T resource)
        where T : RecordedResource
    {
        _resources.Add(resource);
        return resource;
    }
}
