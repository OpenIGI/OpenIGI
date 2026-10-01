using OpenIGI.Rendering.Rhi;

namespace OpenIGI.Rendering.Pipeline.Draw2D;

/// <summary>What a pass needs of a texture to bind it: how it is keyed, its view and its sampler.</summary>
/// <param name="Id">The handle identifier, which the bind-group cache keys by.</param>
/// <param name="View">The view the shader samples.</param>
/// <param name="Sampler">How it is filtered.</param>
public readonly record struct RhiTextureBinding(uint Id, IRhiTextureView View, IRhiSampler Sampler);

/// <summary>
/// Turns a texture handle into something bindable.
/// </summary>
/// <remarks>
/// A seam rather than a direct lookup, because the texture table belongs to the device above these
/// passes while the passes are what need to bind from it. It is also what lets a pass be driven without
/// a device at all, which is how the 2D path is tested.
/// </remarks>
public interface ITextureBindingSource
{
    /// <summary>Resolves a handle, falling back to the white texture when it names nothing.</summary>
    /// <param name="handle">The handle, which may be <see cref="TextureHandle.None"/>.</param>
    /// <returns>Something bindable, always.</returns>
    /// <remarks>
    /// Falling back rather than failing is deliberate and is what the Vulkan path did: a draw whose
    /// texture was destroyed a frame early should lose its picture, not the frame.
    /// </remarks>
    RhiTextureBinding Resolve(TextureHandle handle);
}
