using OpenIGI.Rendering.Rhi;

namespace OpenIGI.Rendering.Pipeline.Draw2D;

/// <summary>
/// Hands out the bind group that binds a texture and its sampler, one per texture, kept for the
/// texture's whole life.
/// </summary>
/// <remarks>
/// <para>
/// A draw does not bind a texture; it binds a group that points at one. What such a group holds never
/// changes — a texture's view and sampler are fixed — so it is built the first time the texture is drawn
/// and then reused. A run of draws of the same texture costs a lookup rather than a rebuild.
/// </para>
/// <para>
/// The image and the sampler occupy separate slots rather than one combined sampler. That is not a
/// preference: Slang's combined sampler expands into two binding slots on the WGSL target and collides
/// with whatever is declared next, so every module of the library declares the pair apart and both
/// targets expect it.
/// </para>
/// <para>
/// This replaces the Vulkan descriptor-set cache exactly, including the part that matters most: a
/// destroyed texture must give its group back, or a long session accumulates groups pointing at views
/// that no longer exist.
/// </para>
/// </remarks>
/// <param name="device">The device the groups are made on.</param>
/// <param name="layout">The shape they are built against.</param>
public sealed class TextureBindGroupCache(IRhiDevice device, IRhiBindGroupLayout layout) : IDisposable
{
    private readonly IRhiDevice _device = device ?? throw new ArgumentNullException(nameof(device));
    private readonly IRhiBindGroupLayout _layout = layout ?? throw new ArgumentNullException(nameof(layout));
    private readonly Dictionary<uint, IRhiBindGroup> _groups = [];
    private bool _disposed;

    /// <summary>Gets how many groups are being kept.</summary>
    public int Count => _groups.Count;

    /// <summary>Gets the group binding a texture, building it on first use.</summary>
    /// <param name="textureId">The texture's handle identifier, which is what it is keyed by.</param>
    /// <param name="view">The view to bind.</param>
    /// <param name="sampler">The sampler to bind alongside it.</param>
    /// <returns>The group: the view at zero, the sampler at one.</returns>
    public IRhiBindGroup Get(uint textureId, IRhiTextureView view, IRhiSampler sampler)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(sampler);
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_groups.TryGetValue(textureId, out IRhiBindGroup? existing))
        {
            return existing;
        }

        IRhiBindGroup group = _device.CreateBindGroup(new RhiBindGroupDescriptor(
            _layout,
            [RhiBindGroupEntry.Of(0, view), RhiBindGroupEntry.Of(1, sampler)],
            $"texture-{textureId}"));

        _groups[textureId] = group;
        return group;
    }

    /// <summary>Releases the group of a destroyed texture, if one was built.</summary>
    /// <param name="textureId">The texture's handle identifier.</param>
    public void Forget(uint textureId)
    {
        if (_groups.Remove(textureId, out IRhiBindGroup? group))
        {
            group.Dispose();
        }
    }

    /// <summary>The layout every group here is built against.</summary>
    /// <param name="device">The device to create it on.</param>
    /// <param name="label">A name for captures and diagnostics.</param>
    /// <returns>The layout: a sampled texture at zero and a sampler at one, read by the fragment stage.</returns>
    public static IRhiBindGroupLayout CreateLayout(IRhiDevice device, string label = "texture")
    {
        ArgumentNullException.ThrowIfNull(device);

        return device.CreateBindGroupLayout(new RhiBindGroupLayoutDescriptor(
            [
                new RhiBindGroupLayoutEntry(0, RhiBindingType.SampledTexture, RhiShaderStages.Fragment),
                new RhiBindGroupLayoutEntry(1, RhiBindingType.Sampler, RhiShaderStages.Fragment),
            ],
            label));
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        foreach (IRhiBindGroup group in _groups.Values)
        {
            group.Dispose();
        }

        _groups.Clear();
    }
}
