using OpenIGI.Shaders;

namespace OpenIGI.Rendering.Rhi;

/// <summary>
/// A graphics device, as everything above it needs one: resources, pipelines, a command encoder and a
/// frame to present.
/// </summary>
/// <remarks>
/// <para>
/// WebGPU's model, because it is the narrower of the two and a narrower interface can be implemented
/// over a wider one. The reverse cannot: a browser cannot be asked for a subpass, a barrier, a
/// descriptor pool or a memory type, while Vulkan can be asked to do without all four.
/// </para>
/// <para>
/// Nothing here knows what a frame of this game looks like. Which passes run in what order, what the
/// G-buffer holds and when the surface cache is relit belong above, in the shared pipeline, so that both
/// backends draw the same frame rather than two frames that resemble each other.
/// </para>
/// </remarks>
public interface IRhiDevice : IDisposable
{
    /// <summary>Gets a description of the adapter, for logs and the options screen.</summary>
    string AdapterName { get; }

    /// <summary>Gets the graphics API underneath, by its usual name: "Vulkan", "WebGPU", "Direct3D 12".</summary>
    /// <remarks>
    /// For logs and the options screen only. Nothing above the backend branches on it: what a device can
    /// do is asked through <see cref="Features"/>, <see cref="Limits"/> and <see cref="ShaderTarget"/>.
    /// </remarks>
    string ApiName { get; }

    /// <summary>Gets how many nanoseconds one tick of a timestamp query is.</summary>
    /// <remarks>
    /// Vulkan reports it as the adapter's timestamp period; Direct3D 12 as the queue's tick frequency;
    /// WebGPU resolves its timestamps to nanoseconds already, so it is one there.
    /// </remarks>
    double TimestampPeriodNanoseconds { get; }

    /// <summary>Gets why <see cref="Streamline"/> is null, in words the options screen can show.</summary>
    /// <remarks>
    /// Empty while Streamline runs. Each backend knows its own reason -- the API has no Streamline at all,
    /// the runtime pack is not installed, or the runtime refused to start -- where the pipeline above would
    /// otherwise have to guess it from which backend it is talking to.
    /// </remarks>
    string StreamlineUnavailableReason { get; }

    /// <summary>Gets the optional things this device can do.</summary>
    RhiFeatures Features { get; }

    /// <summary>Gets the code this device's shader modules are made from.</summary>
    /// <remarks>
    /// A few of the library's modules are carried for one target only, where the other language cannot
    /// express them. A pass built from such a module asks the catalogue with this before building.
    /// </remarks>
    ShaderTarget ShaderTarget { get; }

    /// <summary>Gets the sizes it will not exceed.</summary>
    RhiLimits Limits { get; }

    /// <summary>Gets the format the frames it hands out are in.</summary>
    /// <remarks>
    /// Asked rather than assumed. A surface's format is the window system's to choose — a desktop
    /// swapchain may hand out an unsigned-normalised image where a browser canvas hands out an sRGB one
    /// — and every pipeline drawing into a frame is built for its format before any frame exists. A
    /// pipeline built for the wrong one is refused on one backend and silently mis-encoded on the other.
    /// </remarks>
    RhiTextureFormat SurfaceFormat { get; }

    /// <summary>Gets hardware acceleration structures, where this device offers them.</summary>
    /// <remarks>
    /// A capability interface rather than members on this one: only a device reporting
    /// <see cref="RhiFeatures.RayQuery"/> has it, and the renderer builds its traced world over it only when
    /// the answer is not null -- exactly as the traced passes already stand down on a Mac.
    /// </remarks>
    IRhiAccelerationStructures? AccelerationStructures { get; }

    /// <summary>Gets NVIDIA's Streamline features, where this device started the optional runtime.</summary>
    /// <remarks>
    /// Null everywhere else — on WebGPU, off Windows, on a machine without the runtime pack — and every
    /// caller falls back to the engine's own path when it is. See <see cref="IRhiStreamline"/>.
    /// </remarks>
    IRhiStreamline? Streamline => null;

    /// <summary>Describes what the device holds in memory, for a memory report.</summary>
    /// <returns>Lines of text, or empty where the backend has nothing to say.</returns>
    string DescribeMemory() => string.Empty;

    /// <summary>Gives back memory the device holds for reuse but is not using, once a load is over.</summary>
    /// <remarks>Upload staging and the like: kept between bursts of uploads, and RAM held for nothing after them.</remarks>
    void TrimMemory()
    {
    }

    /// <summary>Creates a buffer.</summary>
    /// <param name="descriptor">What it is created as.</param>
    /// <returns>The buffer.</returns>
    IRhiBuffer CreateBuffer(in RhiBufferDescriptor descriptor);

    /// <summary>Creates a texture.</summary>
    /// <param name="descriptor">What it is created as.</param>
    /// <returns>The texture.</returns>
    IRhiTexture CreateTexture(in RhiTextureDescriptor descriptor);

    /// <summary>Creates a sampler.</summary>
    /// <param name="descriptor">What it is created as.</param>
    /// <returns>The sampler.</returns>
    IRhiSampler CreateSampler(in RhiSamplerDescriptor descriptor);

    /// <summary>Loads one module of the shader library in the form this backend runs.</summary>
    /// <param name="descriptor">Which module and variant.</param>
    /// <returns>The module.</returns>
    IRhiShaderModule CreateShaderModule(in RhiShaderModuleDescriptor descriptor);

    /// <summary>Creates a bind group layout.</summary>
    /// <param name="descriptor">Its shape.</param>
    /// <returns>The layout.</returns>
    IRhiBindGroupLayout CreateBindGroupLayout(RhiBindGroupLayoutDescriptor descriptor);

    /// <summary>Creates a bind group.</summary>
    /// <param name="descriptor">Its layout and what fills each slot.</param>
    /// <returns>The group.</returns>
    IRhiBindGroup CreateBindGroup(RhiBindGroupDescriptor descriptor);

    /// <summary>Creates a pipeline layout.</summary>
    /// <param name="descriptor">Its bind group layouts and push data.</param>
    /// <returns>The layout.</returns>
    IRhiPipelineLayout CreatePipelineLayout(RhiPipelineLayoutDescriptor descriptor);

    /// <summary>Creates a graphics pipeline.</summary>
    /// <param name="descriptor">What it is created as.</param>
    /// <returns>The pipeline.</returns>
    IRhiRenderPipeline CreateRenderPipeline(RhiRenderPipelineDescriptor descriptor);

    /// <summary>Creates a compute pipeline.</summary>
    /// <param name="descriptor">What it is created as.</param>
    /// <returns>The pipeline.</returns>
    IRhiComputePipeline CreateComputePipeline(RhiComputePipelineDescriptor descriptor);

    /// <summary>Creates a query set.</summary>
    /// <param name="descriptor">What it counts and how many slots it has.</param>
    /// <returns>The set.</returns>
    IRhiQuerySet CreateQuerySet(in RhiQuerySetDescriptor descriptor);

    /// <summary>Begins recording work.</summary>
    /// <param name="label">A name for it.</param>
    /// <returns>The encoder.</returns>
    IRhiCommandEncoder CreateCommandEncoder(string label = "");

    /// <summary>Submits recorded work.</summary>
    /// <param name="commands">What to submit, in order.</param>
    void Submit(ReadOnlySpan<IRhiCommandBuffer> commands);

    /// <summary>Sends everything submitted so far to the GPU now, and waits until it has finished.</summary>
    /// <remarks>
    /// <para>
    /// A backend may hold what is submitted during a frame and send it when the frame ends -- the Vulkan
    /// one does, so that the image acquired for the frame is waited on once and presented once. That
    /// leaves nothing a readback made in the middle of a frame can wait for. This sends the held work
    /// ahead of the frame's end, so a readback of what it wrote completes; the frame then goes on and
    /// ends as usual.
    /// </para>
    /// <para>
    /// A diagnostic's tool, not a frame's: it stalls the host on the GPU. Where the platform cannot
    /// block, it sends what it holds and returns, and a readback polls across frames instead.
    /// </para>
    /// </remarks>
    void Flush();

    /// <summary>Writes host data into a buffer.</summary>
    /// <typeparam name="T">The element type.</typeparam>
    /// <param name="buffer">The buffer.</param>
    /// <param name="offset">Where the write starts.</param>
    /// <param name="data">The data.</param>
    /// <remarks>
    /// The queue's own write, not a mapping: both backends have one, and it is the only upload path that
    /// does not need the caller to know whether the memory is visible to the host.
    /// </remarks>
    void WriteBuffer<T>(IRhiBuffer buffer, ulong offset, ReadOnlySpan<T> data)
        where T : unmanaged;

    /// <summary>Writes host data into a texture.</summary>
    /// <typeparam name="T">The element type.</typeparam>
    /// <param name="destination">The region written.</param>
    /// <param name="data">The texels.</param>
    /// <param name="bytesPerRow">How many bytes one row of the data occupies.</param>
    /// <param name="size">How much to write.</param>
    void WriteTexture<T>(
        in RhiTextureRegion destination,
        ReadOnlySpan<T> data,
        int bytesPerRow,
        in RhiExtent3D size)
        where T : unmanaged;

    /// <summary>Asks for a buffer's contents to come back to the host.</summary>
    /// <param name="buffer">The buffer, which must have been created with <see cref="RhiBufferUsage.MapRead"/>.</param>
    /// <param name="offset">Where the read starts.</param>
    /// <param name="sizeInBytes">How many bytes, or zero for the rest of it.</param>
    /// <returns>The readback, which completes once the work that wrote the buffer has finished.</returns>
    /// <remarks>
    /// A future rather than a value, because a browser cannot block for one and a desktop should not. The
    /// shared pipeline waits where the platform allows it and reports the last completed result where it
    /// does not — see <c>GraphicsCapabilities.SynchronousReadback</c>.
    /// </remarks>
    IRhiReadback ReadBuffer(IRhiBuffer buffer, ulong offset = 0, ulong sizeInBytes = 0);

    /// <summary>Begins the frame, acquiring the image that will be presented.</summary>
    /// <param name="frame">The frame, when one was acquired.</param>
    /// <returns><see langword="false"/> when the surface must be reconfigured first and nothing was acquired.</returns>
    bool BeginFrame(out RhiFrame frame);

    /// <summary>Ends the frame and presents it.</summary>
    /// <param name="frame">The frame that was begun.</param>
    /// <returns><see langword="false"/> when the surface must be reconfigured before the next frame.</returns>
    bool EndFrame(in RhiFrame frame);

    /// <summary>Reconfigures the surface for a new size.</summary>
    /// <param name="width">The new width in pixels.</param>
    /// <param name="height">The new height in pixels.</param>
    /// <param name="vsync">Whether to wait for the display.</param>
    void ConfigureSurface(int width, int height, bool vsync);

    /// <summary>Waits until every submission has completed.</summary>
    /// <remarks>
    /// For shutdown and for a resize, where a resource about to be destroyed may still be in flight. Not
    /// something a frame should ever do, and not something a browser offers at all: the web
    /// implementation completes what it can and returns.
    /// </remarks>
    void WaitIdle();

    /// <summary>Marks the queue where it stands, to learn when the GPU has caught up with it.</summary>
    /// <returns>A marker that completes once every submission made before this call has finished.</returns>
    /// <remarks>
    /// <para>
    /// Only what has been SUBMITTED. Work a backend is holding for the frame's end -- see <see cref="Flush"/>
    /// -- goes out after the marker and is not covered by it, so a caller asking about the work it recorded
    /// in a frame takes the marker in the next one.
    /// </para>
    /// <para>
    /// For pacing, not for correctness: it lets a caller that spreads work across frames keep no more than
    /// the GPU can finish in flight. Nothing waits on it, which is what makes it usable in a browser.
    /// </para>
    /// </remarks>
    IRhiWorkMarker MarkSubmittedWork();
}

/// <summary>The image a frame draws into, and where it stands in the rotation.</summary>
/// <param name="Target">The view to attach as the frame's colour target.</param>
/// <param name="Index">Which image of the swapchain it is.</param>
/// <param name="FrameInFlight">Which set of per-frame resources this frame may write.</param>
public readonly record struct RhiFrame(IRhiTextureView Target, int Index, int FrameInFlight);
