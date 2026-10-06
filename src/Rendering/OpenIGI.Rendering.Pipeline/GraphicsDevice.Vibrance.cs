using OpenIGI.Rendering.Rhi;

namespace OpenIGI.Rendering.Pipeline;

/// <summary>RTX Dynamic Vibrance: NVIDIA's network raising the finished picture's vibrance and saturation.</summary>
/// <remarks>
/// It runs on the tone-mapped picture — NVIDIA's guide is explicit that in linear light it shifts colours it
/// should not — and before any interface is drawn, so a frame that wants it resolves into the picture's own
/// image (see GraphicsDevice.Layer) and the interface is laid over it afterwards with its colours untouched.
/// </remarks>
public sealed partial class GraphicsDevice
{
    private bool _vibranceRequested;
    private float _vibranceIntensity = 0.5f;
    private float _vibranceSaturation = 0.25f;

    /// <inheritdoc/>
    public string VibranceUnavailable =>
        _device.Streamline is { } streamline
            ? streamline.Support(RhiStreamlineFeature.Vibrance) is { Supported: false } support ? support.Reason : string.Empty
            : NvidiaUnavailable;

    /// <inheritdoc/>
    public void SetVibrance(bool enabled, float intensity, float saturationBoost)
    {
        ThrowIfDisposed();

        _vibranceRequested = enabled && VibranceUnavailable.Length == 0;
        _vibranceIntensity = Math.Clamp(intensity, 0f, 1f);
        _vibranceSaturation = Math.Clamp(saturationBoost, 0f, 1f);

        if (enabled)
        {
            ReportOnChange(
                "Dynamic Vibrance",
                _vibranceRequested
                    ? $"on, intensity {_vibranceIntensity:0.00}, saturation {_vibranceSaturation:0.00}"
                    : $"off ({VibranceUnavailable})");
        }
        else
        {
            ReportOnChange("Dynamic Vibrance", "off");
        }
    }

    /// <summary>Runs Dynamic Vibrance over the picture's own image, in place, where it is asked for.</summary>
    private void RecordVibrance()
    {
        if (_device.Streamline is not { } streamline || _display is not { } display || !_layered)
        {
            return;
        }

        streamline.SetVibranceOptions(ReconstructionViewport, _vibranceRequested, _vibranceIntensity, _vibranceSaturation);
        if (!_vibranceRequested)
        {
            return;
        }

        _ = streamline.BeginFrame();
        ClosePass();
        IRhiCommandEncoder encoder = Encoder();
        EnsureFrameConstants(streamline);

        // The final colour, read and written in place: the plugin's only input.
        streamline.Tag(
            ReconstructionViewport,
            [new RhiStreamlineTag(RhiStreamlineBuffer.ScalingOutputColor, display, RhiStreamlineLifecycle.UntilEvaluate)],
            encoder);

        ReportOnChange(
            "Dynamic Vibrance evaluate",
            EvaluateTimed(streamline, RhiStreamlineFeature.Vibrance, encoder, RenderStage.CameraEffects, "dvc", out string? failure)
                ? "ok"
                : $"did not run ({failure})");
    }

    /// <summary>Gets why NVIDIA's features are absent altogether: the API, or the missing runtime.</summary>
    private string NvidiaUnavailable => _device.ShaderTarget == Shaders.ShaderTarget.Wgsl
        ? "NVIDIA's features run on Vulkan only"
        : "the NVIDIA DLSS runtime is not installed";
}
