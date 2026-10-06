using System.Runtime.InteropServices;

namespace OpenIGI.Rendering.Vulkan.Streamline;

/// <summary>Whether roughness travels in the normal buffer or in one of its own.</summary>
internal enum SlDlssDNormalRoughnessMode : uint
{
    /// <summary>Two textures: normals, and roughness separately.</summary>
    Unpacked = 0,

    /// <summary>One texture: the world normal in <c>xyz</c> and roughness in <c>w</c>.</summary>
    Packed = 1,
}

/// <summary>Which trained model ray reconstruction should use.</summary>
/// <remarks>
/// A separate ladder from super resolution's: the two features have their own model families, and the
/// numbers do not line up. Only the ones NVIDIA currently recommends are named.
/// </remarks>
internal enum SlDlssDPreset : uint
{
    /// <summary>Whatever NVIDIA currently considers best for the mode.</summary>
    Default = 0,

    /// <summary>A transformer model.</summary>
    PresetD = 4,

    /// <summary>A transformer model.</summary>
    PresetE = 5,

    /// <summary>The newest transformer model, and the default since Streamline 2.14.</summary>
    PresetF = 6,
}

/// <summary>How ray reconstruction should behave.</summary>
/// <remarks>
/// It carries the camera transform itself, unlike super resolution — the denoiser works in world
/// space, so it has to be able to get there from a pixel rather than only reproject between frames.
/// </remarks>
[StructLayout(LayoutKind.Sequential)]
internal struct SlDlssDOptions
{
    /// <summary>The structure header.</summary>
    public SlStructHeader Header;

    /// <summary>Which mode to run. Shares super resolution's ladder.</summary>
    public SlDlssMode Mode;

    /// <summary>The final target width.</summary>
    public uint OutputWidth;

    /// <summary>The final target height.</summary>
    public uint OutputHeight;

    /// <summary>Deprecated, and ignored.</summary>
    public float Sharpness;

    /// <summary>Any exposure already folded into the colour buffer.</summary>
    public float PreExposure;

    /// <summary>A scale applied to the tagged exposure value.</summary>
    public float ExposureScale;

    /// <summary>Whether the colour buffers hold high dynamic range.</summary>
    public SlBoolean ColorBuffersHdr;

    /// <summary>Whether the on-screen indicator's horizontal axis is inverted.</summary>
    public SlBoolean IndicatorInvertAxisX;

    /// <summary>Whether the on-screen indicator's vertical axis is inverted.</summary>
    public SlBoolean IndicatorInvertAxisY;

    /// <summary>Whether roughness rides in the normal buffer's alpha.</summary>
    public SlDlssDNormalRoughnessMode NormalRoughnessMode;

    /// <summary>World space to camera view space, row-major.</summary>
    public SlFloat4x4 WorldToCameraView;

    /// <summary>Camera view space to world space, row-major.</summary>
    public SlFloat4x4 CameraViewToWorld;

    /// <summary>Whether the alpha channel is upscaled too.</summary>
    public SlBoolean AlphaUpscalingEnabled;

    /// <summary>The model to use at native resolution.</summary>
    public SlDlssDPreset DlaaPreset;

    /// <summary>The model to use for quality.</summary>
    public SlDlssDPreset QualityPreset;

    /// <summary>The model to use for balanced.</summary>
    public SlDlssDPreset BalancedPreset;

    /// <summary>The model to use for performance.</summary>
    public SlDlssDPreset PerformancePreset;

    /// <summary>The model to use for ultra performance.</summary>
    public SlDlssDPreset UltraPerformancePreset;

    /// <summary>The model to use for ultra quality.</summary>
    public SlDlssDPreset UltraQualityPreset;

    /// <summary>The structure's identity, from <c>sl_dlss_d.h</c>.</summary>
    public static SlStructType TypeId { get; } = new(
        0x0ad87504,
        0x774e,
        0x4bf3,
        SlStructType.Pack(0x96, 0x33, 0xa4, 0x4d, 0x1f, 0x7f, 0x9c, 0xb8));

    /// <summary>Creates options with ray reconstruction off.</summary>
    /// <returns>The options.</returns>
    public static SlDlssDOptions Create() =>
        new()
        {
            Header = SlStructHeader.For(TypeId, 3),
            Mode = SlDlssMode.Off,
            OutputWidth = SlAbi.InvalidUint,
            OutputHeight = SlAbi.InvalidUint,
            Sharpness = 0f,
            PreExposure = 1f,
            ExposureScale = 1f,
            ColorBuffersHdr = SlBoolean.True,
            IndicatorInvertAxisX = SlBoolean.False,
            IndicatorInvertAxisY = SlBoolean.False,

            // One texture rather than two: the guide pass has to write the normal anyway, and roughness
            // costs it a channel it already has.
            NormalRoughnessMode = SlDlssDNormalRoughnessMode.Packed,
            AlphaUpscalingEnabled = SlBoolean.False,
            DlaaPreset = SlDlssDPreset.PresetF,
            QualityPreset = SlDlssDPreset.PresetF,
            BalancedPreset = SlDlssDPreset.PresetF,
            PerformancePreset = SlDlssDPreset.PresetF,
            UltraPerformancePreset = SlDlssDPreset.PresetF,
            UltraQualityPreset = SlDlssDPreset.PresetF,
        };
}

/// <summary>What ray reconstruction says the render resolution should be.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct SlDlssDOptimalSettings
{
    /// <summary>The structure header.</summary>
    public SlStructHeader Header;

    /// <summary>The render width to use.</summary>
    public uint OptimalRenderWidth;

    /// <summary>The render height to use.</summary>
    public uint OptimalRenderHeight;

    /// <summary>The sharpness it would pick. Deprecated along with the option it feeds.</summary>
    public float OptimalSharpness;

    /// <summary>The smallest render width dynamic resolution may fall to.</summary>
    public uint RenderWidthMin;

    /// <summary>The smallest render height dynamic resolution may fall to.</summary>
    public uint RenderHeightMin;

    /// <summary>The largest render width dynamic resolution may rise to.</summary>
    public uint RenderWidthMax;

    /// <summary>The largest render height dynamic resolution may rise to.</summary>
    public uint RenderHeightMax;

    /// <summary>The structure's identity, from <c>sl_dlss_d.h</c>.</summary>
    public static SlStructType TypeId { get; } = new(
        0xfbd0c637,
        0xa28f,
        0x41f2,
        SlStructType.Pack(0xbc, 0x91, 0xb4, 0x21, 0xfa, 0xee, 0x8e, 0x1e));

    /// <summary>Creates an empty structure for Streamline to fill.</summary>
    /// <returns>The settings.</returns>
    public static SlDlssDOptimalSettings Create() =>
        new() { Header = SlStructHeader.For(TypeId, 1) };
}

/// <summary>What ray reconstruction reports about itself, chiefly its memory.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct SlDlssDState
{
    /// <summary>The structure header.</summary>
    public SlStructHeader Header;

    /// <summary>How much video memory the feature expects to use.</summary>
    public ulong EstimatedVramUsageInBytes;

    /// <summary>The structure's identity, from <c>sl_dlss_d.h</c>.</summary>
    public static SlStructType TypeId { get; } = new(
        0x71873c14,
        0xf8ca,
        0x4767,
        SlStructType.Pack(0x9e, 0xaf, 0x3b, 0x43, 0x93, 0xea, 0x98, 0xfa));

    /// <summary>Creates an empty structure for Streamline to fill.</summary>
    /// <returns>The state.</returns>
    public static SlDlssDState Create() =>
        new() { Header = SlStructHeader.For(TypeId, 1) };
}
