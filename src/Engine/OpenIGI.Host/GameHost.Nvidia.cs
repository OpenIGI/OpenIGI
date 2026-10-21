using OpenIGI.Rendering;

namespace OpenIGI.Host;

/// <summary>The host's side of NVIDIA's optional features: what it asks of Reflex, and of frame generation.</summary>
/// <remarks>
/// The device decides what can run and says why when something cannot; the host only turns the settings into
/// requests and tells the device the things only the game knows — whether this frame is gameplay at all.
/// </remarks>
public sealed partial class GameHost
{
    // What reconstructed the last frame, for the settings page: the render scale has no say under DLSS.
    private ReconstructionKind _reconstructionActive = ReconstructionKind.Engine;

    /// <summary>Why a setting cannot take effect on this device, for the settings page; empty when it can.</summary>
    /// <param name="variable">The console variable a settings row drives.</param>
    /// <returns>The reason, or empty.</returns>
    /// <remarks>
    /// Asked as the page is built, so it reflects the device of the moment: switching renderer rebuilds the
    /// page on its next visit. The reconstruction row stays usable while either NVIDIA reconstruction can run,
    /// and says which cannot.
    /// </remarks>
    private string SettingUnavailable(string variable)
    {
        IReadOnlyDictionary<ReconstructionKind, string> support = _device.ReconstructionSupport;
        string dlss = support.GetValueOrDefault(ReconstructionKind.Dlss, string.Empty);
        string rayReconstruction = support.GetValueOrDefault(ReconstructionKind.RayReconstruction, string.Empty);
        string nvidia = dlss.Length > 0 && rayReconstruction.Length > 0 ? dlss : string.Empty;

        return variable switch
        {
            "r_reconstruction" or "r_dlss_quality" => nvidia,
            "r_frame_generation" => _device.FrameGenerationUnavailable,
            "r_low_latency" => _device.LowLatencyUnavailable,
            "r_scale" when _reconstructionActive != ReconstructionKind.Engine =>
                "DLSS chooses the render size while it runs; set it with DLSS Quality",
            _ => DisplaySettingUnavailable(variable),
        };
    }

    /// <summary>The Reflex mode this frame runs with: the setting, raised to On when frame generation wants it.</summary>
    /// <returns>The mode.</returns>
    /// <remarks>
    /// Frame generation requires Reflex: without it the generated frames queue up behind the rendered ones and
    /// the latency the multiplier costs is never paid back. It is raised rather than refused, as NVIDIA's own
    /// integrations do, and the setting itself is left as the player set it.
    /// </remarks>
    private static LowLatencyMode EffectiveLowLatency()
    {
        LowLatencyMode mode = RenderCVars.LowLatency.Value;
        return RenderCVars.FrameGeneration.Value != FrameGenerationMode.Off && mode == LowLatencyMode.Off
            ? LowLatencyMode.On
            : mode;
    }
}
