using OpenIGI.Rendering.Rhi;

namespace OpenIGI.Rendering.Pipeline;

/// <summary>Latency: NVIDIA Reflex's pacing and the PC latency markers, where the runtime runs them.</summary>
/// <remarks>
/// <para>
/// A frame here is one that reaches the screen. The host calls <see cref="BeginLatencyFrame"/> at the top of
/// every pass of its loop, and the backend begins a frame only once the previous one has been presented — so
/// the passes that only tick, or that leave early with nothing due, neither pace nor count. Calling the sleep
/// once per loop pass instead once paced several times per presented frame and took the processor's frame
/// time from one millisecond to eight.
/// </para>
/// <para>
/// The markers run whether or not low latency is on: they are how latency is measured at all, and the
/// driver's overlay and NVIDIA's own tools read them.
/// </para>
/// </remarks>
public sealed partial class GraphicsDevice
{
    /// <inheritdoc/>
    public bool PresentsOnRecord => _device.Streamline is not null;

    /// <inheritdoc/>
    public string LowLatencyUnavailable =>
        _device.Streamline is { } streamline
            ? streamline.Support(RhiStreamlineFeature.LowLatency) is { Supported: false } support ? support.Reason : string.Empty
            : NvidiaUnavailable;

    /// <inheritdoc/>
    public bool BeginLatencyFrame()
    {
        ThrowIfDisposed();

        if (_device.Streamline is not { } streamline || !streamline.BeginFrame())
        {
            return false;
        }

        // Before anything of the new frame happens: the sleep is how the frame starts later, with fresher
        // input, and the simulation's start is measured from the moment it wakes.
        streamline.Sleep();
        streamline.Mark(RhiLatencyMarker.SimulationStart);
        return true;
    }

    /// <inheritdoc/>
    public void MarkLatency(LatencyMarker marker)
    {
        if (_device.Streamline is not { } streamline)
        {
            return;
        }

        streamline.Mark(marker switch
        {
            LatencyMarker.SimulationStart => RhiLatencyMarker.SimulationStart,
            LatencyMarker.SimulationEnd => RhiLatencyMarker.SimulationEnd,
            LatencyMarker.RenderSubmitStart => RhiLatencyMarker.RenderSubmitStart,
            LatencyMarker.RenderSubmitEnd => RhiLatencyMarker.RenderSubmitEnd,
            _ => throw new ArgumentOutOfRangeException(nameof(marker), marker, null),
        });
    }

    /// <summary>Gets why NVIDIA's features are absent altogether: the API, or the missing runtime.</summary>
    private string NvidiaUnavailable => _device.ShaderTarget == Shaders.ShaderTarget.Wgsl
        ? "NVIDIA's features run on Vulkan only"
        : "the NVIDIA DLSS runtime is not installed";

    /// <inheritdoc/>
    public void SetLowLatency(LowLatencyMode mode, int frameLimitFramesPerSecond)
    {
        if (_device.Streamline is not { } streamline)
        {
            return;
        }

        int limitMicroseconds = frameLimitFramesPerSecond > 0 ? 1_000_000 / frameLimitFramesPerSecond : 0;
        streamline.SetLatencyOptions(
            mode switch
            {
                LowLatencyMode.On => RhiLowLatencyMode.On,
                LowLatencyMode.Boost => RhiLowLatencyMode.Boost,
                _ => RhiLowLatencyMode.Off,
            },
            limitMicroseconds);
    }
}
