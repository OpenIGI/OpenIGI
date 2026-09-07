namespace OpenIGI.Rendering;

/// <summary>
/// What the loading backdrop pass draws: a held picture of the level taken out of focus and out of
/// colour under a title card, or a procedural surface where there is no picture yet.
/// </summary>
/// <remarks>
/// Every amount runs from zero to one and is eased by the caller over the sequence, so the pass itself
/// is stateless: the same settings draw the same frame. That is what lets the blocking part of a load
/// present frames from outside the render loop and have them match the ones the loop draws after.
/// </remarks>
/// <param name="TimeSeconds">Real seconds, for the grain, the drift and the light sweep.</param>
/// <param name="Blur">How far out of focus the picture is, one for the widest disc.</param>
/// <param name="Desaturation">How far towards a cool monochrome grade the picture is taken.</param>
/// <param name="Vignette">How strongly the edges darken.</param>
/// <param name="Zoom">The slow push in on the picture, one for none; a few per cent is the most it wants.</param>
/// <param name="Grain">The film grain's amplitude.</param>
/// <param name="Scanlines">The scanlines' depth, zero for none.</param>
/// <param name="Opacity">How much of the whole pass shows over the live frame; the reveal fades it off.</param>
/// <param name="Letterbox">The fraction of the height each cinematic bar takes.</param>
/// <param name="HasPicture">Whether a held picture is bound, or the procedural surface should be drawn.</param>
/// <param name="AspectRatio">The output's width over its height, so the blur disc is round.</param>
/// <param name="Sweep">Where the diagonal light sweep is, in screen widths; it drifts across over time.</param>
public readonly record struct LoadingBackdropSettings(
    float TimeSeconds,
    float Blur,
    float Desaturation,
    float Vignette,
    float Zoom,
    float Grain,
    float Scanlines,
    float Opacity,
    float Letterbox,
    bool HasPicture,
    float AspectRatio,
    float Sweep);
