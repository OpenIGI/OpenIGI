namespace OpenIGI.Desktop;

/// <summary>Which renderer F10 moves to from the one running, named as <c>OPENIGI_RENDERER</c> names them.</summary>
/// <remarks>
/// The two being compared are the legacy Vulkan renderer and the RHI over Vulkan, so the key toggles
/// between them. A game started on WebGPU goes to legacy, and from there the toggle is the usual one.
/// </remarks>
internal static class RendererSwitchOrder
{
    /// <summary>The renderer to switch to.</summary>
    /// <param name="current">The running one: <c>rhi</c>, <c>webgpu</c>, or anything else for legacy.</param>
    /// <returns><c>rhi</c> from legacy, and null -- legacy -- from either RHI backend.</returns>
    public static string? Next(string? current) => IsRhi(current) ? null : "rhi";

    /// <summary>A renderer's name for the log.</summary>
    /// <param name="renderer">As <see cref="Next"/> returns it.</param>
    /// <returns>A few words.</returns>
    public static string Describe(string? renderer) =>
        string.Equals(renderer, "rhi", StringComparison.OrdinalIgnoreCase) ? "RHI (Vulkan)"
        : string.Equals(renderer, "webgpu", StringComparison.OrdinalIgnoreCase) ? "RHI (WebGPU)"
        : "legacy Vulkan";

    private static bool IsRhi(string? renderer) =>
        string.Equals(renderer, "rhi", StringComparison.OrdinalIgnoreCase)
        || string.Equals(renderer, "webgpu", StringComparison.OrdinalIgnoreCase);
}
