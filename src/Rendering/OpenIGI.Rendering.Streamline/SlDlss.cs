using System.Runtime.InteropServices;

namespace OpenIGI.Rendering.Streamline;

/// <summary>Which DLSS mode to run, from cheapest to sharpest.</summary>
/// <remarks>
/// <see cref="Dlaa"/> is the reason this integration exists at native resolution: it is the same
/// reconstruction as super resolution, run without the upscale, so it replaces the anti-aliasing
/// rather than the resolution.
/// </remarks>
internal enum SlDlssMode : uint
{
    /// <summary>Off. Note that this does not release the feature's resources.</summary>
    Off = 0,

    /// <summary>Performance.</summary>
    MaxPerformance = 1,

    /// <summary>Balanced.</summary>
    Balanced = 2,

    /// <summary>Quality.</summary>
    MaxQuality = 3,

    /// <summary>Ultra performance.</summary>
    UltraPerformance = 4,

    /// <summary>Ultra quality.</summary>
    UltraQuality = 5,

    /// <summary>Native resolution, anti-aliasing only.</summary>
    Dlaa = 6,
}

/// <summary>Which trained model a DLSS mode should use.</summary>
/// <remarks>
/// Presets A to D were removed and E to I are deprecated or revert to the default, so only the
/// transformer-generation ones are named here. K is the best image quality and the default for
/// DLAA, quality and balanced; M is the performance default; L is the ultra-performance default.
/// </remarks>
internal enum SlDlssPreset : uint
{
    /// <summary>Whatever NVIDIA currently considers best for the mode.</summary>
    Default = 0,

    /// <summary>Slightly less ghosting than K at the cost of more flicker. K is generally preferred.</summary>
    PresetJ = 10,

    /// <summary>Transformer-based, the best image quality, at a higher cost.</summary>
    PresetK = 11,

    /// <summary>Sharper and more stable than J or K, and more expensive. The ultra-performance default.</summary>
    PresetL = 12,

    /// <summary>The quality gains of L closer to the speed of J and K. The performance default.</summary>
    PresetM = 13,
}

/// <summary>How DLSS should behave, set whenever the player changes a setting.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct SlDlssOptions
{
    /// <summary>The structure header.</summary>
    public SlStructHeader Header;

    /// <summary>Which mode to run.</summary>
    public SlDlssMode Mode;

    /// <summary>The final target width.</summary>
    public uint OutputWidth;

    /// <summary>The final target height.</summary>
    public uint OutputHeight;

    /// <summary>Deprecated and ignored by the plugin. Sharpening is the host's job.</summary>
    public float Sharpness;

    /// <summary>Any exposure already folded into the colour buffer. One when there is none.</summary>
    public float PreExposure;

    /// <summary>A scale applied to the tagged exposure value.</summary>
    public float ExposureScale;

    /// <summary>Whether the colour buffers hold high dynamic range. They do here.</summary>
    public SlBoolean ColorBuffersHdr;

    /// <summary>Whether the on-screen indicator's horizontal axis is inverted.</summary>
    public SlBoolean IndicatorInvertAxisX;

    /// <summary>Whether the on-screen indicator's vertical axis is inverted.</summary>
    public SlBoolean IndicatorInvertAxisY;

    /// <summary>The model to use at native resolution.</summary>
    public SlDlssPreset DlaaPreset;

    /// <summary>The model to use for quality.</summary>
    public SlDlssPreset QualityPreset;

    /// <summary>The model to use for balanced.</summary>
    public SlDlssPreset BalancedPreset;

    /// <summary>The model to use for performance.</summary>
    public SlDlssPreset PerformancePreset;

    /// <summary>The model to use for ultra performance.</summary>
    public SlDlssPreset UltraPerformancePreset;

    /// <summary>The model to use for ultra quality.</summary>
    public SlDlssPreset UltraQualityPreset;

    /// <summary>
    /// Whether DLSS should meter the frame itself. Setting this, or tagging no exposure buffer, puts
    /// it into auto-exposure mode.
    /// </summary>
    public SlBoolean UseAutoExposure;

    /// <summary>Whether the alpha channel is upscaled too. Experimental, and it costs performance.</summary>
    public SlBoolean AlphaUpscalingEnabled;

    /// <summary>The structure's identity, from <c>sl_dlss.h</c>.</summary>
    public static SlStructType TypeId { get; } = new(
        0x6ac826e4,
        0x4c61,
        0x4101,
        SlStructType.Pack(0xa9, 0x2d, 0x63, 0x8d, 0x42, 0x10, 0x57, 0xb8));

    /// <summary>Creates options carrying NVIDIA's recommended model for every mode.</summary>
    /// <returns>The options.</returns>
    /// <remarks>
    /// The presets are set explicitly rather than left at <see cref="SlDlssPreset.Default"/> so that
    /// an over-the-air model change cannot silently move the picture out from under a calibration
    /// that was measured against a known one.
    /// </remarks>
    public static SlDlssOptions Create() =>
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
            DlaaPreset = SlDlssPreset.PresetK,
            QualityPreset = SlDlssPreset.PresetK,
            BalancedPreset = SlDlssPreset.PresetK,
            PerformancePreset = SlDlssPreset.PresetM,
            UltraPerformancePreset = SlDlssPreset.PresetL,
            UltraQualityPreset = SlDlssPreset.PresetK,
            UseAutoExposure = SlBoolean.False,
            AlphaUpscalingEnabled = SlBoolean.False,
        };
}

/// <summary>What DLSS says the render resolution should be for a mode and output size.</summary>
/// <remarks>
/// The render size is asked for rather than computed. The ratios are not a fixed table — they move
/// between model generations — and a host that hard-codes them ends up feeding the network something
/// it was not trained on.
/// </remarks>
[StructLayout(LayoutKind.Sequential)]
internal struct SlDlssOptimalSettings
{
    /// <summary>The structure header.</summary>
    public SlStructHeader Header;

    /// <summary>The render width to use.</summary>
    public uint OptimalRenderWidth;

    /// <summary>The render height to use.</summary>
    public uint OptimalRenderHeight;

    /// <summary>The sharpness DLSS would pick. Deprecated along with the option it feeds.</summary>
    public float OptimalSharpness;

    /// <summary>The smallest render width dynamic resolution may fall to.</summary>
    public uint RenderWidthMin;

    /// <summary>The smallest render height dynamic resolution may fall to.</summary>
    public uint RenderHeightMin;

    /// <summary>The largest render width dynamic resolution may rise to.</summary>
    public uint RenderWidthMax;

    /// <summary>The largest render height dynamic resolution may rise to.</summary>
    public uint RenderHeightMax;

    /// <summary>The structure's identity, from <c>sl_dlss.h</c>.</summary>
    public static SlStructType TypeId { get; } = new(
        0xef1d0957,
        0xfd58,
        0x4df7,
        SlStructType.Pack(0xb5, 0x04, 0x8b, 0x69, 0xd8, 0xaa, 0x6b, 0x76));

    /// <summary>Creates an empty structure for Streamline to fill.</summary>
    /// <returns>The settings.</returns>
    public static SlDlssOptimalSettings Create() =>
        new() { Header = SlStructHeader.For(TypeId, 1) };
}

/// <summary>What DLSS reports about a running viewport.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct SlDlssState
{
    /// <summary>The structure header.</summary>
    public SlStructHeader Header;

    /// <summary>How much video memory DLSS expects to occupy.</summary>
    public ulong EstimatedVramUsageInBytes;

    /// <summary>The structure's identity, from <c>sl_dlss.h</c>.</summary>
    public static SlStructType TypeId { get; } = new(
        0x9366b056,
        0x8c01,
        0x463c,
        SlStructType.Pack(0xbb, 0x91, 0xe6, 0x87, 0x82, 0x63, 0x6c, 0xe9));

    /// <summary>Creates an empty structure for Streamline to fill.</summary>
    /// <returns>The state.</returns>
    public static SlDlssState Create() =>
        new() { Header = SlStructHeader.For(TypeId, 1) };
}
