namespace OpenIGI.Rendering;

/// <summary>What turns the jittered, render-resolution frame into the anti-aliased output-resolution one.</summary>
/// <remarks>
/// One choice, not three switches: these are mutually exclusive ways of resolving the same frame, and
/// running one after another is never right. Anything other than <see cref="Engine"/> needs NVIDIA's
/// optional runtime and an adapter it supports; when either is missing the engine's own resolve runs
/// instead and the log says why.
/// </remarks>
public enum ReconstructionKind
{
    /// <summary>The engine's own temporal upscaler.</summary>
    Engine,

    /// <summary>DLSS super resolution, or DLAA at native resolution.</summary>
    Dlss,

    /// <summary>
    /// DLSS ray reconstruction: upscaling and denoising in one network, fed the traced lighting before the
    /// engine's own denoisers. Needs hardware ray tracing.
    /// </summary>
    RayReconstruction,
}
