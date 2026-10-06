namespace OpenIGI.Rendering;

/// <summary>How far below the output resolution DLSS renders.</summary>
/// <remarks>
/// The ratios are not a table kept here: the runtime is asked for the render size of each mode, because
/// they belong to the trained model and have moved between model generations.
/// </remarks>
public enum DlssQuality
{
    /// <summary>Native resolution, anti-aliasing only (DLAA).</summary>
    Dlaa,

    /// <summary>About two thirds of the output size on each side.</summary>
    Quality,

    /// <summary>About 58% of the output size on each side.</summary>
    Balanced,

    /// <summary>Half the output size on each side.</summary>
    Performance,

    /// <summary>A third of the output size on each side.</summary>
    UltraPerformance,
}
