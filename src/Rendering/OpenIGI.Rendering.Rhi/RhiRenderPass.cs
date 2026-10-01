namespace OpenIGI.Rendering.Rhi;

/// <summary>What happens to an attachment's existing contents when a pass begins.</summary>
public enum RhiLoadOp
{
    /// <summary>They are kept.</summary>
    Load,

    /// <summary>They are replaced with the clear value.</summary>
    Clear,
}

/// <summary>What happens to an attachment's contents when a pass ends.</summary>
public enum RhiStoreOp
{
    /// <summary>They are kept.</summary>
    Store,

    /// <summary>They are thrown away, which a tiler is entitled to take at its word.</summary>
    Discard,
}

/// <summary>A colour to clear an attachment with.</summary>
/// <param name="R">Red.</param>
/// <param name="G">Green.</param>
/// <param name="B">Blue.</param>
/// <param name="A">Alpha.</param>
public readonly record struct RhiClearColor(float R, float G, float B, float A)
{
    /// <summary>Gets transparent black, which every target this renderer clears is cleared to.</summary>
    public static RhiClearColor Transparent => new(0f, 0f, 0f, 0f);
}

/// <summary>One colour target of a render pass.</summary>
/// <param name="View">What is written.</param>
/// <param name="Load">What happens to its contents when the pass begins.</param>
/// <param name="Store">What happens to them when it ends.</param>
/// <param name="ClearValue">What it is cleared to, when it is cleared.</param>
/// <param name="ResolveTarget">
/// Where a multisampled attachment is resolved to, or <see langword="null"/> when it is not.
/// </param>
public readonly record struct RhiColorAttachment(
    IRhiTextureView View,
    RhiLoadOp Load = RhiLoadOp.Clear,
    RhiStoreOp Store = RhiStoreOp.Store,
    RhiClearColor ClearValue = default,
    IRhiTextureView? ResolveTarget = null);

/// <summary>The depth target of a render pass.</summary>
/// <param name="View">What is written.</param>
/// <param name="DepthLoad">What happens to its contents when the pass begins.</param>
/// <param name="DepthStore">What happens to them when it ends.</param>
/// <param name="DepthClearValue">
/// What it is cleared to. This renderer draws reverse-Z, so the far plane is nought and a pass that
/// clears to one starts with everything already nearer than anything it will draw.
/// </param>
/// <param name="DepthReadOnly">Whether the pass only reads depth, which lets it be sampled at the same time.</param>
public readonly record struct RhiDepthAttachment(
    IRhiTextureView View,
    RhiLoadOp DepthLoad = RhiLoadOp.Clear,
    RhiStoreOp DepthStore = RhiStoreOp.Store,
    float DepthClearValue = 0f,
    bool DepthReadOnly = false);

/// <summary>Where a pass writes the times it began and ended.</summary>
/// <param name="QuerySet">The set to write into.</param>
/// <param name="BeginIndex">The slot for the time the pass began, or -1 for none.</param>
/// <param name="EndIndex">The slot for the time it ended, or -1 for none.</param>
/// <remarks>
/// Written by the pass rather than by commands around it, because that is the only shape WebGPU has and
/// because it is the honest one: a timestamp recorded outside a pass measures the gap between passes as
/// well as the pass.
/// </remarks>
public readonly record struct RhiTimestampWrites(IRhiQuerySet QuerySet, int BeginIndex, int EndIndex);

/// <summary>What a render pass draws into.</summary>
/// <param name="ColorAttachments">The colour targets, in the order the pipeline's own targets are declared.</param>
/// <param name="DepthAttachment">The depth target, or <see langword="null"/> for a pass that writes colour alone.</param>
/// <param name="Timestamps">Where to record when the pass began and ended, or <see langword="null"/>.</param>
/// <param name="OcclusionQuerySet">The set an occlusion query within the pass writes into.</param>
/// <param name="Label">A name for captures and diagnostics.</param>
/// <remarks>
/// Attachments are stated here rather than baked into a render-pass object, which is the WebGPU model
/// and the reason this interface can carry both backends: Vulkan builds a render pass and framebuffer
/// from this (and caches them), while a browser takes it as it stands. Subpasses have no place here at
/// all -- the one thing in this renderer that used them, the deferred shading pass reading the G-buffer
/// as input attachments, becomes a second pass that samples the first's output.
/// </remarks>
public sealed record RhiRenderPassDescriptor(
    IReadOnlyList<RhiColorAttachment> ColorAttachments,
    RhiDepthAttachment? DepthAttachment = null,
    RhiTimestampWrites? Timestamps = null,
    IRhiQuerySet? OcclusionQuerySet = null,
    string Label = "");

/// <summary>What a compute pass is recorded as.</summary>
/// <param name="Timestamps">Where to record when the pass began and ended, or <see langword="null"/>.</param>
/// <param name="Label">A name for captures and diagnostics.</param>
public readonly record struct RhiComputePassDescriptor(
    RhiTimestampWrites? Timestamps = null,
    string Label = "");
