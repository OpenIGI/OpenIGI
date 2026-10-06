using System.Runtime.InteropServices;

namespace OpenIGI.Rendering.Vulkan.Streamline;

/// <summary>Whether frame generation runs.</summary>
internal enum SlDlssGMode : uint
{
    /// <summary>Off. With <see cref="SlDlssGFlags.RetainResourcesWhenOff"/> the feature keeps its memory.</summary>
    Off = 0,

    /// <summary>On, generating the configured number of frames between rendered ones.</summary>
    On = 1,

    /// <summary>Let the plugin decide per frame. Not offered: it is a Direct3D 12 behaviour.</summary>
    Auto = 2,

    /// <summary>Dynamic multi-frame generation. Direct3D 12 only; never requested on Vulkan.</summary>
    Dynamic = 3,
}

/// <summary>Behaviour switches for frame generation.</summary>
[Flags]
internal enum SlDlssGFlags : uint
{
    /// <summary>None.</summary>
    None = 0,

    /// <summary>A debugging view that shows only the generated frames.</summary>
    ShowOnlyInterpolatedFrame = 1 << 0,

    /// <summary>The inputs change size from frame to frame. Not set: the render size is fixed per mode.</summary>
    DynamicResolutionEnabled = 1 << 1,

    /// <summary>Asks the next state query to estimate the memory the feature will need.</summary>
    RequestVramEstimate = 1 << 2,

    /// <summary>
    /// Keep the feature's resources while it is switched off. What lets a menu or a loading screen
    /// pause frame generation and resume it a second later without rebuilding anything.
    /// </summary>
    RetainResourcesWhenOff = 1 << 3,

    /// <summary>Let the plugin notice a full-screen menu on its own and stop interpolating it.</summary>
    EnableFullscreenMenuDetection = 1 << 4,
}

/// <summary>How much of the host's queue work may overlap frame generation's.</summary>
internal enum SlDlssGQueueParallelismMode : uint
{
    /// <summary>The presenting queue waits for frame generation. The default, and the safe one.</summary>
    BlockPresentingClientQueue = 0,

    /// <summary>Nothing waits, and the host must instead wait on the inputs fence before reusing an input.</summary>
    BlockNoClientQueues = 1,
}

/// <summary>Why frame generation is not producing frames, as a set of reasons.</summary>
[Flags]
internal enum SlDlssGStatus : uint
{
    /// <summary>Working.</summary>
    Ok = 0,

    /// <summary>The back buffer is smaller than the feature accepts.</summary>
    FailResolutionTooLow = 1 << 0,

    /// <summary>Reflex is not running, and frame generation requires it.</summary>
    FailReflexNotDetectedAtRuntime = 1 << 1,

    /// <summary>The swapchain's HDR format is not one the feature supports.</summary>
    FailHdrFormatNotSupported = 1 << 2,

    /// <summary>The per-frame constants are invalid.</summary>
    FailCommonConstantsInvalid = 1 << 3,

    /// <summary>A Direct3D-only requirement; never reported on Vulkan.</summary>
    FailGetCurrentBackBufferIndexNotCalled = 1 << 4,
}

/// <summary>How frame generation should behave.</summary>
/// <remarks>
/// Set only when something changes. The guide is explicit that this is not a per-frame call, and the
/// plugin logs a warning for every redundant one.
/// </remarks>
[StructLayout(LayoutKind.Sequential)]
internal struct SlDlssGOptions
{
    /// <summary>The structure header.</summary>
    public SlStructHeader Header;

    /// <summary>Whether frame generation runs.</summary>
    public SlDlssGMode Mode;

    /// <summary>How many frames to generate between rendered ones: one for 2x, three for 4x.</summary>
    public uint NumFramesToGenerate;

    /// <summary>Behaviour switches.</summary>
    public SlDlssGFlags Flags;

    /// <summary>The dynamic-resolution target width. Unused here.</summary>
    public uint DynamicResWidth;

    /// <summary>The dynamic-resolution target height. Unused here.</summary>
    public uint DynamicResHeight;

    /// <summary>How many images the swapchain holds. A hint for sizing the feature's own queue.</summary>
    public uint NumBackBuffers;

    /// <summary>The width depth and motion are rendered at.</summary>
    public uint MvecDepthWidth;

    /// <summary>The height depth and motion are rendered at.</summary>
    public uint MvecDepthHeight;

    /// <summary>The back buffer's width.</summary>
    public uint ColorWidth;

    /// <summary>The back buffer's height.</summary>
    public uint ColorHeight;

    /// <summary>The back buffer's <c>VkFormat</c>.</summary>
    public uint ColorBufferFormat;

    /// <summary>The motion-vector buffer's <c>VkFormat</c>.</summary>
    public uint MvecBufferFormat;

    /// <summary>The depth buffer's <c>VkFormat</c>.</summary>
    public uint DepthBufferFormat;

    /// <summary>The interface-free colour's <c>VkFormat</c>.</summary>
    public uint HudLessBufferFormat;

    /// <summary>The interface layer's <c>VkFormat</c>.</summary>
    public uint UiBufferFormat;

    /// <summary>An optional callback for errors from the presents frame generation makes on the host's behalf.</summary>
    public nint OnErrorCallback;

    /// <summary>Reserved by Streamline.</summary>
    public SlBoolean Reserved15;

    /// <summary>How much queue work may overlap. Must match across viewports.</summary>
    public SlDlssGQueueParallelismMode QueueParallelismMode;

    /// <summary>Interpolate the interface-free colour and the interface separately, then recompose them.</summary>
    public SlBoolean EnableUserInterfaceRecomposition;

    /// <summary>The dynamic mode's target rate. Unused here.</summary>
    public float DynamicTargetFrameRate;

    /// <summary>The structure's identity, from <c>sl_dlss_g.h</c>.</summary>
    public static SlStructType TypeId { get; } = new(
        0xfac5f1cb,
        0x2dfd,
        0x4f36,
        SlStructType.Pack(0xa1, 0xe6, 0x3a, 0x9e, 0x86, 0x52, 0x56, 0xc5));

    /// <summary>Creates options with frame generation off and the C++ header's defaults.</summary>
    /// <returns>The options.</returns>
    public static SlDlssGOptions Create() =>
        new()
        {
            Header = SlStructHeader.For(TypeId, 5),
            Mode = SlDlssGMode.Off,
            NumFramesToGenerate = 1,
            Reserved15 = SlBoolean.Invalid,
            EnableUserInterfaceRecomposition = SlBoolean.False,
        };
}

/// <summary>What frame generation reports about itself.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct SlDlssGState
{
    /// <summary>The structure header.</summary>
    public SlStructHeader Header;

    /// <summary>How much video memory the feature expects to use, when an estimate was asked for.</summary>
    public ulong EstimatedVramUsageInBytes;

    /// <summary>Why it is not running, if it is not.</summary>
    public SlDlssGStatus Status;

    /// <summary>The smallest back-buffer side it accepts.</summary>
    public uint MinWidthOrHeight;

    /// <summary>How many frames reached the screen since the last query, generated ones included.</summary>
    public uint NumFramesActuallyPresented;

    /// <summary>The most frames this system can generate per rendered one: one means 2x at most.</summary>
    public uint NumFramesToGenerateMax;

    /// <summary>Reserved by Streamline.</summary>
    public SlBoolean Reserved4;

    /// <summary>Whether vertical sync may stay on while frame generation runs.</summary>
    public SlBoolean IsVsyncSupportAvailable;

    /// <summary>The <c>VkSemaphore</c> (timeline) signalled once the feature has finished with a frame's inputs.</summary>
    public nint InputsProcessingCompletionFence;

    /// <summary>The value <see cref="InputsProcessingCompletionFence"/> reaches for the last presented frame.</summary>
    public ulong LastPresentInputsProcessingCompletionFenceValue;

    /// <summary>Whether dynamic multi-frame generation is available. Never on Vulkan today.</summary>
    public SlBoolean IsDynamicMfgSupported;

    /// <summary>The structure's identity, from <c>sl_dlss_g.h</c>.</summary>
    public static SlStructType TypeId { get; } = new(
        0xcc8ac8e1,
        0xa179,
        0x44f5,
        SlStructType.Pack(0x97, 0xfa, 0xe7, 0x41, 0x12, 0xf9, 0xbc, 0x61));

    /// <summary>Creates an empty structure for Streamline to fill.</summary>
    /// <returns>The state.</returns>
    public static SlDlssGState Create() =>
        new() { Header = SlStructHeader.For(TypeId, 4) };
}
