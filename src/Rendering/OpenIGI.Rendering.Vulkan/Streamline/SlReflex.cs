using System.Runtime.InteropServices;

namespace OpenIGI.Rendering.Vulkan.Streamline;

/// <summary>Which Reflex mode the driver should run.</summary>
internal enum SlReflexMode : uint
{
    /// <summary>Off. The sleep and the markers still run; they are how latency is measured at all.</summary>
    Off = 0,

    /// <summary>Starts each frame as late as the measured render time allows.</summary>
    LowLatency = 1,

    /// <summary>As low latency, and also keeps the GPU's clocks up when the frame is processor-bound.</summary>
    LowLatencyWithBoost = 2,
}

/// <summary>How Reflex should behave.</summary>
/// <remarks>Set at start-up even when off, and again whenever the player changes it.</remarks>
[StructLayout(LayoutKind.Sequential)]
internal struct SlReflexOptions
{
    /// <summary>The structure header.</summary>
    public SlStructHeader Header;

    /// <summary>Which mode to run.</summary>
    public SlReflexMode Mode;

    /// <summary>A frame-rate cap in microseconds per frame, or zero for none. Independent of the mode.</summary>
    public uint FrameLimitUs;

    /// <summary>A C++ <c>bool</c>. Left false: NVIDIA reserves it for integrations it has advised.</summary>
    public byte UseMarkersToOptimize;

    /// <summary>A function key to send latency pings with instead of a window message. Zero for the message.</summary>
    public ushort VirtualKey;

    /// <summary>The thread the latency statistics messages go to. Zero for the default.</summary>
    public uint IdThread;

    /// <summary>The structure's identity, from <c>sl_reflex.h</c>.</summary>
    public static SlStructType TypeId { get; } = new(
        0xf03af81a,
        0x6d0b,
        0x4902,
        SlStructType.Pack(0xa6, 0x51, 0xc4, 0x96, 0x5e, 0x21, 0x54, 0x34));

    /// <summary>Creates options with Reflex off and no cap.</summary>
    /// <returns>The options.</returns>
    public static SlReflexOptions Create() =>
        new() { Header = SlStructHeader.For(TypeId, 1), Mode = SlReflexMode.Off };
}

/// <summary>One frame's latency timings, as the driver measured them.</summary>
/// <remarks>Times are in microseconds on the driver's own clock; only the differences mean anything.</remarks>
[StructLayout(LayoutKind.Sequential)]
internal struct SlReflexReport
{
    /// <summary>The structure header.</summary>
    public SlStructHeader Header;

    /// <summary>Which frame this describes.</summary>
    public ulong FrameId;

    /// <summary>When input was sampled.</summary>
    public ulong InputSampleTime;

    /// <summary>When simulation started.</summary>
    public ulong SimStartTime;

    /// <summary>When simulation ended.</summary>
    public ulong SimEndTime;

    /// <summary>When the frame's commands started being recorded.</summary>
    public ulong RenderSubmitStartTime;

    /// <summary>When they were submitted.</summary>
    public ulong RenderSubmitEndTime;

    /// <summary>When the present call was made.</summary>
    public ulong PresentStartTime;

    /// <summary>When it returned.</summary>
    public ulong PresentEndTime;

    /// <summary>When the driver started on the frame.</summary>
    public ulong DriverStartTime;

    /// <summary>When the driver finished with it.</summary>
    public ulong DriverEndTime;

    /// <summary>When it entered the operating system's render queue.</summary>
    public ulong OsRenderQueueStartTime;

    /// <summary>When it left.</summary>
    public ulong OsRenderQueueEndTime;

    /// <summary>When the GPU started on it.</summary>
    public ulong GpuRenderStartTime;

    /// <summary>When the GPU finished it.</summary>
    public ulong GpuRenderEndTime;

    /// <summary>How long the GPU was actually busy with it.</summary>
    public uint GpuActiveRenderTimeUs;

    /// <summary>The GPU's frame time.</summary>
    public uint GpuFrameTimeUs;
}

/// <summary>Whether Reflex can run here, and the last frames' latency timings.</summary>
/// <remarks>
/// Laid out explicitly: the header's two arrays of 64 reports are padded in ways a sequential mirror
/// of fixed byte buffers would get wrong, and the offsets below are the compiler's.
/// </remarks>
[StructLayout(LayoutKind.Explicit, Size = 12848)]
internal unsafe struct SlReflexState
{
    /// <summary>How many frames of timings the state carries.</summary>
    public const int ReportCount = 64;

    /// <summary>The structure header.</summary>
    [FieldOffset(0)]
    public SlStructHeader Header;

    /// <summary>A C++ <c>bool</c>: whether low-latency mode can run on this system.</summary>
    [FieldOffset(32)]
    public byte LowLatencyAvailable;

    /// <summary>A C++ <c>bool</c>: whether the reports below hold anything.</summary>
    [FieldOffset(33)]
    public byte LatencyReportAvailable;

    /// <summary>The window message the driver's latency statistics arrive as.</summary>
    [FieldOffset(36)]
    public uint StatsWindowMessage;

    /// <summary>The last frames' timings, <see cref="ReportCount"/> of them, oldest first.</summary>
    [FieldOffset(40)]
    public fixed byte FrameReports[ReportCount * 152];

    /// <summary>A C++ <c>bool</c>: whether the driver owns the latency flash indicator.</summary>
    [FieldOffset(9768)]
    public byte FlashIndicatorDriverControlled;

    /// <summary>A second set of per-frame timings added in version 2. Not read here.</summary>
    [FieldOffset(9776)]
    public fixed byte FrameReports2[ReportCount * 48];

    /// <summary>The structure's identity, from <c>sl_reflex.h</c>.</summary>
    public static SlStructType TypeId { get; } = new(
        0xf0bb5985,
        0xdaf9,
        0x4728,
        SlStructType.Pack(0xb2, 0xfd, 0xae, 0x80, 0xa2, 0xbd, 0x79, 0x89));

    /// <summary>Creates an empty structure for Streamline to fill.</summary>
    /// <returns>The state.</returns>
    public static SlReflexState Create()
    {
        SlReflexState state = default;
        state.Header = SlStructHeader.For(TypeId, 2);
        return state;
    }

    /// <summary>Reads one frame's timings.</summary>
    /// <param name="index">Which report, from zero to <see cref="ReportCount"/> minus one.</param>
    /// <returns>The report.</returns>
    public readonly SlReflexReport Report(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, ReportCount);
        fixed (byte* reports = FrameReports)
        {
            return ((SlReflexReport*)reports)[index];
        }
    }
}
