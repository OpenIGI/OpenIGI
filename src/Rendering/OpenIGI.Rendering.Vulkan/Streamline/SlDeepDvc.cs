using System.Runtime.InteropServices;

namespace OpenIGI.Rendering.Vulkan.Streamline;

/// <summary>Whether RTX Dynamic Vibrance runs.</summary>
internal enum SlDeepDvcMode : uint
{
    /// <summary>Off.</summary>
    Off = 0,

    /// <summary>On.</summary>
    On = 1,
}

/// <summary>How RTX Dynamic Vibrance should behave.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct SlDeepDvcOptions
{
    /// <summary>The structure header.</summary>
    public SlStructHeader Header;

    /// <summary>Whether it runs.</summary>
    public SlDeepDvcMode Mode;

    /// <summary>How strongly vibrance is raised, from zero to one.</summary>
    public float Intensity;

    /// <summary>How much extra saturation is added, from zero to one.</summary>
    public float SaturationBoost;

    /// <summary>The structure's identity, from <c>sl_deepdvc.h</c>.</summary>
    public static SlStructType TypeId { get; } = new(
        0x23288aad,
        0x7e7e,
        0xbe2a,
        SlStructType.Pack(0x91, 0x67, 0x27, 0xda, 0x30, 0xa3, 0x04, 0x6b));

    /// <summary>Creates options with the header's defaults: off, half intensity, a quarter saturation.</summary>
    /// <returns>The options.</returns>
    public static SlDeepDvcOptions Create() =>
        new()
        {
            Header = SlStructHeader.For(TypeId, 1),
            Mode = SlDeepDvcMode.Off,
            Intensity = 0.5f,
            SaturationBoost = 0.25f,
        };
}

/// <summary>What RTX Dynamic Vibrance reports about itself.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct SlDeepDvcState
{
    /// <summary>The structure header.</summary>
    public SlStructHeader Header;

    /// <summary>How much video memory it expects to use.</summary>
    public ulong EstimatedVramUsageInBytes;

    /// <summary>The structure's identity, from <c>sl_deepdvc.h</c>.</summary>
    public static SlStructType TypeId { get; } = new(
        0x934fd3d3,
        0xb34c,
        0x70a7,
        SlStructType.Pack(0xa1, 0x39, 0xf1, 0x9f, 0xe0, 0x4d, 0x91, 0xd3));

    /// <summary>Creates an empty structure for Streamline to fill.</summary>
    /// <returns>The state.</returns>
    public static SlDeepDvcState Create() => new() { Header = SlStructHeader.For(TypeId, 1) };
}
