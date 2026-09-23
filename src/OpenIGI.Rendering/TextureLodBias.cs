namespace OpenIGI.Rendering;

/// <summary>How far a texture's mip selection is pushed towards the sharper levels.</summary>
/// <remarks>
/// <para>
/// A frame rendered below the output size picks its mips for the smaller image, and the upscale then
/// shows them too soft; a temporal resolve with any feedback averages detail away on top. Both are paid
/// back by biasing every mipmapped texture's selection by the log of the render scale, and a little more
/// under temporal feedback -- which is what the reference upscalers ask of the engines that feed them.
/// </para>
/// <para>
/// It reaches further than sharpness. A cut-out's coverage is its alpha tested against a threshold, and a
/// thin one -- a chain-link fence, a wire -- loses its strands the moment a softer level averages them
/// under the threshold: in the picture and, drawn from the light, in the shadow it casts.
/// </para>
/// </remarks>
public static class TextureLodBias
{
    /// <summary>The bias the current settings ask for, to be applied to every mipmapped texture's sampler.</summary>
    /// <returns>The bias, in mip levels; negative is sharper.</returns>
    /// <remarks>
    /// Clamped to one at the top of the scale so supersampling never biases coarser -- it already samples
    /// below its own mip rate, which is the whole point of it.
    /// </remarks>
    public static float FromSettings()
    {
        float scale = Math.Clamp(RenderCVars.RenderScale.Value, 0.25f, 1f);
        float resolution = MathF.Log2(scale);
        float temporal = RenderCVars.TemporalFeedback.Value < 1f ? -0.3f : 0f;
        return Math.Clamp(MathF.Max(resolution + temporal, -2f), -4f, 4f);
    }
}
