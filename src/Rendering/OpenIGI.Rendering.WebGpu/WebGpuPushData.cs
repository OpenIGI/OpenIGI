using OpenIGI.Rendering.Rhi.Push;
using OpenIGI.Shaders;

namespace OpenIGI.Rendering.WebGpu;

/// <summary>
/// Where the shader library's WGSL forms put push data, having no push constants to put it in.
/// </summary>
/// <remarks>
/// This backend's view of <see cref="RhiPushBlocks"/>, which reads it from the compiled modules. The rings that
/// push data is written into are the shared <see cref="RhiPushRing"/> and <see cref="RhiInstancePushRing"/>.
/// </remarks>
public static class WebGpuPushBlocks
{
    /// <summary>Gets the bind group the push block is bound as.</summary>
    public static int Group => RhiPushBlocks.For(ShaderTarget.Wgsl).Group;

    /// <summary>Gets the binding within that group.</summary>
    public static int Binding => RhiPushBlocks.For(ShaderTarget.Wgsl).Binding;

    /// <summary>Gets the largest push block any module declares, which sizes one slice of the ring.</summary>
    public static int LargestBlock => RhiPushBlocks.For(ShaderTarget.Wgsl).LargestBlock;
}
