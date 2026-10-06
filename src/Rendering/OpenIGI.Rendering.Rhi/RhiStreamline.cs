using System.Numerics;

namespace OpenIGI.Rendering.Rhi;

/// <summary>The NVIDIA features reached through Streamline.</summary>
public enum RhiStreamlineFeature
{
    /// <summary>DLSS super resolution, and DLAA at native resolution.</summary>
    SuperResolution,

    /// <summary>DLSS ray reconstruction.</summary>
    RayReconstruction,

    /// <summary>DLSS frame generation, including multi-frame generation.</summary>
    FrameGeneration,

    /// <summary>Reflex low-latency pacing.</summary>
    LowLatency,

    /// <summary>The PC latency markers, which measure latency whether or not Reflex paces anything.</summary>
    LatencyMarkers,

    /// <summary>RTX Dynamic Vibrance.</summary>
    Vibrance,
}

/// <summary>The role a tagged texture plays for a feature.</summary>
public enum RhiStreamlineBuffer
{
    /// <summary>The hardware depth buffer, reverse-Z in this renderer.</summary>
    Depth,

    /// <summary>View-space depth, one channel.</summary>
    LinearDepth,

    /// <summary>Per-pixel motion, in the convention the constants describe.</summary>
    MotionVectors,

    /// <summary>The colour to reconstruct, at render resolution, before tone mapping.</summary>
    ScalingInputColor,

    /// <summary>Where the reconstruction is written, at output resolution.</summary>
    ScalingOutputColor,

    /// <summary>A 1x1 texture holding the exposure the tone mapper will multiply the colour by.</summary>
    Exposure,

    /// <summary>A mask of pixels whose history should not be trusted: additive effects with no motion.</summary>
    BiasCurrentColorHint,

    /// <summary>Diffuse albedo, linear.</summary>
    DiffuseAlbedo,

    /// <summary>Specular albedo: the directional reflectance the specular lighting was scaled by.</summary>
    SpecularAlbedo,

    /// <summary>World normal in <c>xyz</c>, linear roughness in <c>w</c>.</summary>
    NormalRoughness,

    /// <summary>World-space distance from the primary surface to the specular ray's hit.</summary>
    SpecularHitDistance,

    /// <summary>The scene colour before transparent surfaces were composited over it.</summary>
    ColorBeforeTransparency,

    /// <summary>The final colour before the interface was drawn: what frame generation interpolates.</summary>
    HudLessColor,

    /// <summary>The interface alone, premultiplied, with its coverage in alpha.</summary>
    UiColorAndAlpha,
}

/// <summary>How long a tagged texture stays as it was when tagged.</summary>
public enum RhiStreamlineLifecycle
{
    /// <summary>Until the evaluation that reads it returns. No copy is taken.</summary>
    UntilEvaluate,

    /// <summary>Until the frame is presented. What frame generation needs, because it reads at present time.</summary>
    UntilPresent,

    /// <summary>Only now; the feature copies it.</summary>
    OnlyNow,
}

/// <summary>A texture and the role it plays this frame.</summary>
/// <param name="Buffer">The role.</param>
/// <param name="Texture">The texture, or <see langword="null"/> to clear the role.</param>
/// <param name="Lifecycle">How long it stays as tagged.</param>
/// <param name="Width">The width in use, or zero for the whole texture.</param>
/// <param name="Height">The height in use, or zero for the whole texture.</param>
public readonly record struct RhiStreamlineTag(
    RhiStreamlineBuffer Buffer,
    IRhiTexture? Texture,
    RhiStreamlineLifecycle Lifecycle,
    int Width = 0,
    int Height = 0);

/// <summary>The camera state every feature reads, once per frame.</summary>
/// <remarks>
/// Matrices are <see cref="Matrix4x4"/> in its own convention — row-major, acting on row vectors — which
/// is exactly Streamline's, so they cross over as they are. The engine's own matrices act on column
/// vectors and must be transposed into this. Every matrix is free of jitter; the jitter travels
/// separately, in render pixels.
/// </remarks>
public readonly record struct RhiStreamlineConstants
{
    /// <summary>Gets camera view to clip, unjittered.</summary>
    public Matrix4x4 CameraViewToClip { get; init; }

    /// <summary>Gets clip to camera view.</summary>
    public Matrix4x4 ClipToCameraView { get; init; }

    /// <summary>Gets this frame's clip space to the previous frame's.</summary>
    public Matrix4x4 ClipToPrevClip { get; init; }

    /// <summary>Gets the previous frame's clip space to this frame's.</summary>
    public Matrix4x4 PrevClipToClip { get; init; }

    /// <summary>Gets the sub-pixel jitter the frame was rendered with, in render pixels.</summary>
    public Vector2 Jitter { get; init; }

    /// <summary>Gets what the motion texture is multiplied by to give previous-minus-current in ±1 units.</summary>
    public Vector2 MotionVectorScale { get; init; }

    /// <summary>Gets the camera position in world space.</summary>
    public Vector3 CameraPosition { get; init; }

    /// <summary>Gets the camera's up vector in world space.</summary>
    public Vector3 CameraUp { get; init; }

    /// <summary>Gets the camera's right vector in world space.</summary>
    public Vector3 CameraRight { get; init; }

    /// <summary>Gets the camera's forward vector in world space.</summary>
    public Vector3 CameraForward { get; init; }

    /// <summary>Gets the near plane distance.</summary>
    public float Near { get; init; }

    /// <summary>Gets the far plane distance.</summary>
    public float Far { get; init; }

    /// <summary>Gets the vertical field of view in radians.</summary>
    public float VerticalFieldOfView { get; init; }

    /// <summary>Gets width over height.</summary>
    public float AspectRatio { get; init; }

    /// <summary>Gets a value indicating whether nearer surfaces have the higher depth.</summary>
    public bool DepthInverted { get; init; }

    /// <summary>Gets a value indicating whether the motion texture includes the camera's own motion.</summary>
    public bool CameraMotionIncluded { get; init; }

    /// <summary>Gets a value indicating whether the motion texture includes the jitter.</summary>
    public bool MotionVectorsJittered { get; init; }

    /// <summary>Gets a value indicating whether the previous frame bears no relation to this one.</summary>
    public bool Reset { get; init; }
}

/// <summary>Which DLSS mode to run.</summary>
public enum RhiDlssMode
{
    /// <summary>Off.</summary>
    Off,

    /// <summary>Native resolution, anti-aliasing only.</summary>
    Dlaa,

    /// <summary>Quality.</summary>
    Quality,

    /// <summary>Balanced.</summary>
    Balanced,

    /// <summary>Performance.</summary>
    Performance,

    /// <summary>Ultra performance.</summary>
    UltraPerformance,
}

/// <summary>What a DLSS mode renders at for an output size.</summary>
/// <param name="Width">The render width to use.</param>
/// <param name="Height">The render height to use.</param>
/// <param name="MinWidth">The smallest width the mode accepts.</param>
/// <param name="MinHeight">The smallest height the mode accepts.</param>
/// <param name="MaxWidth">The largest width the mode accepts.</param>
/// <param name="MaxHeight">The largest height the mode accepts.</param>
public readonly record struct RhiDlssRenderSize(int Width, int Height, int MinWidth, int MinHeight, int MaxWidth, int MaxHeight);

/// <summary>Whether a feature can run here, and if not, why.</summary>
/// <param name="Supported">Whether it can.</param>
/// <param name="Reason">Why it cannot, or a description of what is running when it can.</param>
public readonly record struct RhiStreamlineSupport(bool Supported, string Reason);

/// <summary>The low-latency mode.</summary>
public enum RhiLowLatencyMode
{
    /// <summary>No pacing; latency is still measured.</summary>
    Off,

    /// <summary>Start each frame as late as the measured render time allows.</summary>
    On,

    /// <summary>As <see cref="On"/>, with the GPU's clocks kept up.</summary>
    Boost,
}

/// <summary>The boundaries in a frame the application marks for latency measurement.</summary>
public enum RhiLatencyMarker
{
    /// <summary>Simulation starts.</summary>
    SimulationStart,

    /// <summary>Simulation ends.</summary>
    SimulationEnd,

    /// <summary>Recording of the frame's commands starts.</summary>
    RenderSubmitStart,

    /// <summary>The frame's commands have all been recorded.</summary>
    RenderSubmitEnd,

    /// <summary>A click that should flash the latency indicator.</summary>
    TriggerFlash,
}

/// <summary>What Reflex reports.</summary>
/// <param name="LowLatencyAvailable">Whether low-latency pacing can run here.</param>
/// <param name="LatencyReportAvailable">Whether the timings below are real.</param>
/// <param name="LastFrameLatencyMicroseconds">Simulation start to GPU finish for the last reported frame.</param>
/// <param name="LastFrameGpuMicroseconds">The GPU's frame time for it.</param>
public readonly record struct RhiLatencyState(
    bool LowLatencyAvailable,
    bool LatencyReportAvailable,
    long LastFrameLatencyMicroseconds,
    long LastFrameGpuMicroseconds);

/// <summary>How frame generation should run.</summary>
/// <param name="Enabled">Whether it runs this frame. Off keeps its resources.</param>
/// <param name="FramesToGenerate">Generated frames per rendered one, already clamped to what the system offers.</param>
/// <param name="UserInterfaceRecomposition">Interpolate the interface-free colour and the interface separately.</param>
/// <param name="RenderWidth">The width depth and motion are rendered at.</param>
/// <param name="RenderHeight">Their height.</param>
/// <param name="BlockNoClientQueues">Let the host's queues run on while frame generation works.</param>
public readonly record struct RhiFrameGenerationOptions(
    bool Enabled,
    int FramesToGenerate,
    bool UserInterfaceRecomposition,
    int RenderWidth,
    int RenderHeight,
    bool BlockNoClientQueues = false);

/// <summary>What frame generation reports.</summary>
/// <param name="Ok">Whether it reports no failure.</param>
/// <param name="Status">Its failure reasons, named, or empty.</param>
/// <param name="MinWidthOrHeight">The smallest back-buffer side it accepts.</param>
/// <param name="FramesToGenerateMax">The most frames this system can generate per rendered one.</param>
/// <param name="FramesActuallyPresented">How many frames reached the screen since the last query.</param>
/// <param name="VsyncSupported">Whether vertical sync may stay on while it runs.</param>
public readonly record struct RhiFrameGenerationState(
    bool Ok,
    string Status,
    int MinWidthOrHeight,
    int FramesToGenerateMax,
    int FramesActuallyPresented,
    bool VsyncSupported);
