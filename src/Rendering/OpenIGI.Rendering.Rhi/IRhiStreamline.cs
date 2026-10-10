using System.Numerics;

namespace OpenIGI.Rendering.Rhi;

/// <summary>NVIDIA's Streamline features, where the device runs them: DLSS, ray reconstruction, frame generation, Reflex.</summary>
/// <remarks>
/// <para>
/// A capability interface, like <see cref="IRhiAccelerationStructures"/>: only a Vulkan device that started the
/// optional Streamline runtime has one, and everything that uses it asks and falls back to the engine's
/// own path when the answer is null. It speaks RHI types only — the handles, the formats, the layouts and
/// the command buffer stay inside the backend — so the shared pipeline drives it without knowing which
/// API is underneath.
/// </para>
/// <para>
/// Everything a frame hands over carries the same frame: the token <see cref="BeginFrame"/> mints is the
/// one the constants, the tags, the evaluations, the Reflex sleep and the latency markers all name, and
/// the backend's present closes it. A frame is one that reaches the screen, never a pass of the host loop.
/// </para>
/// </remarks>
public interface IRhiStreamline
{
    /// <summary>Gets a one-line description of what is running, for the log.</summary>
    string Description { get; }

    /// <summary>Asks whether a feature can run here.</summary>
    /// <param name="feature">The feature.</param>
    /// <returns>The answer, with the reason when it is no.</returns>
    RhiStreamlineSupport Support(RhiStreamlineFeature feature);

    /// <summary>Gets the index of the current frame, which the present carries as its id.</summary>
    ulong FrameIndex { get; }

    /// <summary>Gets a value indicating whether a frame has been begun and not yet presented.</summary>
    bool FrameOpen { get; }

    /// <summary>Begins a frame, if the last one has been presented.</summary>
    /// <returns><see langword="true"/> when this call began a new frame.</returns>
    bool BeginFrame();

    /// <summary>Paces the frame's start, when low latency is on. Call once per frame, before input is sampled.</summary>
    void Sleep();

    /// <summary>Marks a boundary in the current frame.</summary>
    /// <param name="marker">Which boundary.</param>
    void Mark(RhiLatencyMarker marker);

    /// <summary>Sets the low-latency mode and an optional frame-rate cap.</summary>
    /// <param name="mode">The mode.</param>
    /// <param name="frameLimitMicroseconds">The cap in microseconds per frame, or zero for none.</param>
    void SetLatencyOptions(RhiLowLatencyMode mode, int frameLimitMicroseconds);

    /// <summary>Reads what Reflex reports.</summary>
    /// <returns>The state.</returns>
    RhiLatencyState LatencyState();

    /// <summary>Sets the camera constants for the current frame and a viewport.</summary>
    /// <param name="viewport">The viewport the features are evaluated for.</param>
    /// <param name="constants">The constants.</param>
    void SetConstants(int viewport, in RhiStreamlineConstants constants);

    /// <summary>Tags the textures a viewport's features read and write this frame.</summary>
    /// <param name="viewport">The viewport.</param>
    /// <param name="tags">The textures and their roles.</param>
    /// <param name="encoder">The encoder the evaluation will be recorded into.</param>
    void Tag(int viewport, ReadOnlySpan<RhiStreamlineTag> tags, IRhiCommandEncoder encoder);

    /// <summary>Asks what a DLSS mode renders at for an output size.</summary>
    /// <param name="feature"><see cref="RhiStreamlineFeature.SuperResolution"/> or <see cref="RhiStreamlineFeature.RayReconstruction"/>.</param>
    /// <param name="mode">The mode.</param>
    /// <param name="outputWidth">The output width.</param>
    /// <param name="outputHeight">The output height.</param>
    /// <param name="size">The answer.</param>
    /// <returns><see langword="true"/> when the feature answered.</returns>
    bool TryGetRenderSize(RhiStreamlineFeature feature, RhiDlssMode mode, int outputWidth, int outputHeight, out RhiDlssRenderSize size);

    /// <summary>Sets the super-resolution options for a viewport. Cheap to repeat; sent on change only.</summary>
    /// <param name="viewport">The viewport.</param>
    /// <param name="mode">The mode.</param>
    /// <param name="outputWidth">The output width.</param>
    /// <param name="outputHeight">The output height.</param>
    void SetSuperResolutionOptions(int viewport, RhiDlssMode mode, int outputWidth, int outputHeight);

    /// <summary>Sets the ray-reconstruction options for a viewport. Sent every frame: they carry the camera.</summary>
    /// <param name="viewport">The viewport.</param>
    /// <param name="mode">The mode.</param>
    /// <param name="outputWidth">The output width.</param>
    /// <param name="outputHeight">The output height.</param>
    /// <param name="worldToCameraView">World to camera view, in <see cref="Matrix4x4"/>'s convention.</param>
    /// <param name="cameraViewToWorld">Its inverse.</param>
    void SetRayReconstructionOptions(
        int viewport,
        RhiDlssMode mode,
        int outputWidth,
        int outputHeight,
        in Matrix4x4 worldToCameraView,
        in Matrix4x4 cameraViewToWorld);

    /// <summary>Gets a value indicating whether the frame-generation plugin is loaded.</summary>
    bool FrameGenerationLoaded { get; }

    /// <summary>Loads or unloads frame generation, rebuilding the swapchain around the change.</summary>
    /// <param name="loaded">Whether it should be loaded.</param>
    /// <returns><see langword="true"/> when the plugin is now in the requested state.</returns>
    /// <remarks>
    /// While loaded, the host renders off-screen and pays a copy each frame even with frame generation
    /// off, so it is loaded only while someone has asked for it. Must be called outside a frame.
    /// </remarks>
    bool SetFrameGenerationLoaded(bool loaded);

    /// <summary>Sets the frame-generation options for a viewport. Sent on change only.</summary>
    /// <param name="viewport">The viewport.</param>
    /// <param name="options">The options.</param>
    void SetFrameGenerationOptions(int viewport, in RhiFrameGenerationOptions options);

    /// <summary>Reads frame generation's state. Call after the present, on the presenting thread.</summary>
    /// <param name="viewport">The viewport.</param>
    /// <returns>The state.</returns>
    RhiFrameGenerationState FrameGenerationState(int viewport);

    /// <summary>Records a feature's evaluation into an encoder, between passes.</summary>
    /// <param name="feature">The feature.</param>
    /// <param name="viewport">The viewport.</param>
    /// <param name="encoder">The encoder, with no pass open.</param>
    /// <param name="failure">Why it did not run, when it did not.</param>
    /// <returns><see langword="true"/> when the work was recorded.</returns>
    bool Evaluate(RhiStreamlineFeature feature, int viewport, IRhiCommandEncoder encoder, out string? failure);

    /// <summary>Releases a feature's resources for a viewport.</summary>
    /// <param name="feature">The feature.</param>
    /// <param name="viewport">The viewport.</param>
    /// <remarks>Waits for the device first: any evaluation still in flight names those resources.</remarks>
    void FreeResources(RhiStreamlineFeature feature, int viewport);
}
