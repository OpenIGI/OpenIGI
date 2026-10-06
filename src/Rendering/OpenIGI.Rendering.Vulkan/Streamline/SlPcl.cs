using System.Runtime.InteropServices;

namespace OpenIGI.Rendering.Vulkan.Streamline;

/// <summary>The latency markers a frame passes through, from <c>sl_pcl.h</c>.</summary>
/// <remarks>
/// Only the markers this game can place honestly are named. Input sampling had a marker of its own
/// (6) until the header deprecated it; the driver now dates input from the simulation start.
/// </remarks>
internal enum SlPclMarker : uint
{
    /// <summary>Simulation for the frame starts.</summary>
    SimulationStart = 0,

    /// <summary>Simulation for the frame ends.</summary>
    SimulationEnd = 1,

    /// <summary>Recording of the frame's commands starts.</summary>
    RenderSubmitStart = 2,

    /// <summary>The frame's commands have been submitted.</summary>
    RenderSubmitEnd = 3,

    /// <summary>Immediately before the present call.</summary>
    PresentStart = 4,

    /// <summary>Immediately after the present call returns.</summary>
    PresentEnd = 5,

    /// <summary>A click that should flash the latency indicator.</summary>
    TriggerFlash = 7,

    /// <summary>The latency ping the driver's statistics message asked for.</summary>
    PcLatencyPing = 8,
}

/// <summary>How the latency markers should be delivered.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct SlPclOptions
{
    /// <summary>The structure header.</summary>
    public SlStructHeader Header;

    /// <summary>A function key to send pings with instead of a window message. Zero for the message.</summary>
    public short VirtualKey;

    /// <summary>The thread the statistics messages go to. Zero for the default.</summary>
    public uint IdThread;

    /// <summary>The structure's identity, from <c>sl_pcl.h</c>.</summary>
    public static SlStructType TypeId { get; } = new(
        0xcfa32f9b,
        0x023c,
        0x420e,
        SlStructType.Pack(0x90, 0x56, 0x68, 0x32, 0xb7, 0x4f, 0x89, 0xb4));

    /// <summary>Creates options that use the window message.</summary>
    /// <returns>The options.</returns>
    public static SlPclOptions Create() => new() { Header = SlStructHeader.For(TypeId, 1) };
}

/// <summary>What the latency-marker feature reports.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct SlPclState
{
    /// <summary>The structure header.</summary>
    public SlStructHeader Header;

    /// <summary>The window message latency pings arrive as.</summary>
    public uint StatsWindowMessage;

    /// <summary>The structure's identity, from <c>sl_pcl.h</c>.</summary>
    public static SlStructType TypeId { get; } = new(
        0xcfa32f9b,
        0x023c,
        0x420e,
        SlStructType.Pack(0x90, 0x56, 0x68, 0x32, 0xb7, 0x4f, 0x89, 0xb5));

    /// <summary>Creates an empty structure for Streamline to fill.</summary>
    /// <returns>The state.</returns>
    public static SlPclState Create() => new() { Header = SlStructHeader.For(TypeId, 1) };
}
