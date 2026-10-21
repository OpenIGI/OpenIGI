namespace OpenIGI.Core.Configuration.Presets;

/// <summary>
/// A named group of graphics settings.
/// </summary>
/// <remarks>
/// Presets exist so that a player never has to understand a froxel to get a good picture, and so that
/// the remaster track always has a way back to the original look. Changing any preset-managed setting
/// by hand moves the reported preset to <see cref="Custom"/>; nothing is silently overridden.
/// </remarks>
public enum GraphicsPreset
{
    /// <summary>
    /// The game as it shipped: no offscreen pipeline, no realtime lighting, the original's own
    /// per-vertex exponential fog, and the baked lightmaps.
    /// </summary>
    /// <remarks>This is the regression gate for the whole remaster track — it must be pixel-identical.</remarks>
    Vanilla,

    /// <summary>A deliberate, cheap improvement: the modern pipeline with the expensive effects off.</summary>
    Enhanced,

    /// <summary>Everything on at sensible quality, targeting mid-range hardware at 1080p.</summary>
    High,

    /// <summary>Everything on at maximum quality.</summary>
    Ultra,

    /// <summary>Not a preset: the settings do not match any of them.</summary>
    Custom,
}
