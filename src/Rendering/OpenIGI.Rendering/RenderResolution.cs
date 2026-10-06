namespace OpenIGI.Rendering;

/// <summary>What the game asks the frame to be reconstructed with.</summary>
/// <param name="Kind">The reconstruction asked for.</param>
/// <param name="Quality">The DLSS quality mode, for the NVIDIA kinds.</param>
/// <param name="OutputWidth">The width the frame is presented at.</param>
/// <param name="OutputHeight">The height it is presented at.</param>
/// <param name="Temporal">Whether temporal reconstruction is on at all (<c>r_taa</c>, the deferred path).</param>
public readonly record struct ReconstructionRequest(
    ReconstructionKind Kind,
    DlssQuality Quality,
    int OutputWidth,
    int OutputHeight,
    bool Temporal);

/// <summary>What the device will actually reconstruct with this frame, and at what size.</summary>
/// <param name="Active">The reconstruction that will run.</param>
/// <param name="RenderWidth">The width it wants rendered, or zero to leave it to the render scale.</param>
/// <param name="RenderHeight">The height it wants rendered, or zero.</param>
/// <param name="Reason">Why it differs from the request, or empty when it does not.</param>
public readonly record struct ReconstructionPlan(
    ReconstructionKind Active,
    int RenderWidth,
    int RenderHeight,
    string Reason)
{
    /// <summary>Gets the plan for the engine's own reconstruction.</summary>
    /// <param name="reason">Why, when something else was asked for.</param>
    /// <returns>The plan.</returns>
    public static ReconstructionPlan Engine(string reason = "") =>
        new(ReconstructionKind.Engine, 0, 0, reason);
}

/// <summary>The frame's sizes, its jitter sequence and its mip bias, decided once from one plan.</summary>
/// <param name="RenderWidth">The width the scene is rendered at.</param>
/// <param name="RenderHeight">The height the scene is rendered at.</param>
/// <param name="OutputWidth">The width it is reconstructed to, or zero when it is not upscaled.</param>
/// <param name="OutputHeight">The height it is reconstructed to, or zero.</param>
/// <param name="JitterPhases">How many jitter offsets the sequence cycles through before repeating.</param>
/// <param name="MipBias">The bias every mipmapped texture is sampled with.</param>
public readonly record struct RenderResolution(
    int RenderWidth,
    int RenderHeight,
    int OutputWidth,
    int OutputHeight,
    int JitterPhases,
    float MipBias);

/// <summary>Decides the render size, the output size, the jitter length and the mip bias together.</summary>
/// <remarks>
/// <para>
/// One decision because they are one fact. The jitter is a fraction of a <em>render</em> pixel, so a jitter
/// worked out against one render size and a scene target built at another moves every sample by the wrong
/// amount; the jitter has to cycle through more phases the further the render is below the output, so that
/// each output pixel is still visited often enough; and the textures must pick their mips for the output,
/// not the render, or the upscale shows them soft. Working each out in its own place is how they drift apart.
/// </para>
/// <para>
/// Under DLSS the numbers are NVIDIA's (DLSS Programming Guide §3.5 and §3.7.1.1): the render size is the
/// one the runtime asks for — the render scale is set aside, there is no dynamic resolution — the phase
/// count is eight times the area ratio, and the mip bias is the log of the ratio less one. Under ray
/// reconstruction the phases never fall below thirty-two, which its integration guide asks of a noisy input.
/// </para>
/// </remarks>
public static class RenderResolutionPolicy
{
    /// <summary>How many phases the engine's own resolve cycles through.</summary>
    public const int EnginePhases = 16;

    /// <summary>The base phase count NVIDIA scales by the area ratio.</summary>
    public const int DlssBasePhases = 8;

    /// <summary>The fewest phases ray reconstruction is given.</summary>
    public const int RayReconstructionMinimumPhases = 32;

    /// <summary>The most phases any sequence is given: ultra performance's own figure.</summary>
    public const int MaximumPhases = 72;

    /// <summary>Decides the frame's sizes, jitter length and mip bias.</summary>
    /// <param name="windowWidth">The window's width, which is the output.</param>
    /// <param name="windowHeight">The window's height.</param>
    /// <param name="renderScale">The render scale, which only the engine's own resolve follows.</param>
    /// <param name="temporal">Whether temporal reconstruction is on at all.</param>
    /// <param name="plan">What the device will reconstruct with.</param>
    /// <returns>The frame's resolution.</returns>
    public static RenderResolution Choose(
        int windowWidth,
        int windowHeight,
        float renderScale,
        bool temporal,
        in ReconstructionPlan plan)
    {
        int outputWidth = Math.Max(1, windowWidth);
        int outputHeight = Math.Max(1, windowHeight);

        if (temporal && plan.Active != ReconstructionKind.Engine && plan.RenderWidth > 0 && plan.RenderHeight > 0)
        {
            int renderWidth = Math.Min(plan.RenderWidth, outputWidth);
            int renderHeight = Math.Min(plan.RenderHeight, outputHeight);
            float ratio = outputWidth / (float)renderWidth;
            int phases = Math.Clamp((int)MathF.Ceiling(DlssBasePhases * ratio * ratio), DlssBasePhases, MaximumPhases);
            if (plan.Active == ReconstructionKind.RayReconstruction)
            {
                phases = Math.Max(phases, RayReconstructionMinimumPhases);
            }

            return new RenderResolution(
                renderWidth,
                renderHeight,
                outputWidth,
                outputHeight,
                phases,
                DlssMipBias(renderWidth, outputWidth));
        }

        int width = Math.Max(1, (int)(outputWidth * renderScale));
        int height = Math.Max(1, (int)(outputHeight * renderScale));

        // Asked for only when the accumulation is running, because it is the accumulation that does the
        // upsampling: without it these are two buffers holding a stretched copy.
        bool upscales = temporal && renderScale < 1f;
        return new RenderResolution(
            width,
            height,
            upscales ? outputWidth : 0,
            upscales ? outputHeight : 0,
            EnginePhases,
            TextureLodBias.FromSettings());
    }

    /// <summary>NVIDIA's recommended mip bias for a render width under an output width.</summary>
    /// <param name="renderWidth">The render width.</param>
    /// <param name="outputWidth">The output width.</param>
    /// <returns><c>log2(render / output) - 1</c>: minus one at DLAA, minus two at performance.</returns>
    public static float DlssMipBias(int renderWidth, int outputWidth) =>
        MathF.Log2(Math.Max(1, renderWidth) / (float)Math.Max(1, outputWidth)) - 1f;
}
