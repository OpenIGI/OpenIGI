using OpenIGI.Core.Graphics;
using OpenIGI.Rendering.Rhi;
using OpenIGI.Shaders;

namespace OpenIGI.Rendering.Pipeline.Passes;

/// <summary>
/// One texel of each shape a shader can ask for, to fill a slot nothing has produced yet.
/// </summary>
/// <remarks>
/// <para>
/// A shader that names a resource makes it statically used, and a descriptor nothing has written is
/// undefined to read however carefully the branch avoids it — so every slot a module declares has to be
/// filled whether or not this frame has anything to put there. The renderer being built over the RHI
/// reaches the deferred shading before it has a sky, a probe field or a shadow map, and this is what
/// stands in until it does.
/// </para>
/// <para>
/// One per shape rather than one per slot, because the shape is the whole of what a binding is
/// validated against: a volume bound where a flat texture was declared is refused by one backend and
/// undefined on the other, and an array of slices and a depth texture are each their own shape again.
/// The right one is chosen from the module's own description of the slot rather than from a list kept
/// here.
/// </para>
/// </remarks>
public sealed class StandInTextures : IDisposable
{
    private readonly IRhiTexture _flat;
    private readonly IRhiTexture _volume;
    private readonly IRhiTexture _slices;
    private readonly IRhiTexture _depth;
    private readonly IRhiTexture _depthSlices;
    private bool _disposed;

    /// <summary>Initialises a new instance of the <see cref="StandInTextures"/> class.</summary>
    /// <param name="device">The device the textures live on.</param>
    /// <param name="label">A name for captures and diagnostics.</param>
    public StandInTextures(IRhiDevice device, string label = "standin")
    {
        ArgumentNullException.ThrowIfNull(device);

        _flat = device.CreateTexture(new RhiTextureDescriptor(
            1, 1, RhiTextureFormat.Rgba8Unorm, RhiTextureUsage.Sampled | RhiTextureUsage.CopyDestination,
            Label: $"{label}.flat"));

        _volume = device.CreateTexture(new RhiTextureDescriptor(
            1, 1, RhiTextureFormat.Rgba8Unorm, RhiTextureUsage.Sampled | RhiTextureUsage.CopyDestination,
            DepthOrArrayLayers: 1, Dimension: RhiTextureViewDimension.Texture3D,
            Label: $"{label}.volume"));

        _slices = device.CreateTexture(new RhiTextureDescriptor(
            1, 1, RhiTextureFormat.Rgba8Unorm, RhiTextureUsage.Sampled | RhiTextureUsage.CopyDestination,
            DepthOrArrayLayers: 1, Dimension: RhiTextureViewDimension.Texture2DArray,
            Label: $"{label}.slices"));

        // A depth stand-in is never written: a depth format is not a copy destination, and what a
        // comparison lookup reads out of an untouched one — nought, so nothing is in shadow — is the
        // answer a frame with no shadow map wants anyway. Read plainly, nought is the far plane under
        // reverse Z: a scene depth that has nothing in front of it.
        _depth = device.CreateTexture(new RhiTextureDescriptor(
            1, 1, RhiTextureFormat.Depth32Float, RhiTextureUsage.Sampled | RhiTextureUsage.RenderTarget,
            Label: $"{label}.depth"));

        _depthSlices = device.CreateTexture(new RhiTextureDescriptor(
            1, 1, RhiTextureFormat.Depth32Float, RhiTextureUsage.Sampled | RhiTextureUsage.RenderTarget,
            DepthOrArrayLayers: 1, Dimension: RhiTextureViewDimension.Texture2DArray,
            Label: $"{label}.depthslices"));

        // White, because white is the identity of every multiply a shader does with one of these.
        Bgra32[] white = [new Bgra32(255, 255, 255, 255)];

        foreach (IRhiTexture texture in (IRhiTexture[])[_flat, _volume, _slices])
        {
            device.WriteTexture<Bgra32>(
                new RhiTextureRegion(texture),
                white,
                bytesPerRow: 4,
                new RhiExtent3D(1, 1));
        }
    }

    /// <summary>The view to bind where a module declares a slot this renderer does not fill yet.</summary>
    /// <param name="binding">What the module says the slot is.</param>
    /// <returns>A one-texel view of the right shape.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="binding"/> is <see langword="null"/>.</exception>
    public IRhiTextureView For(ShaderBinding binding)
    {
        ArgumentNullException.ThrowIfNull(binding);
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (binding.Dimensions == 3)
        {
            return _volume.View;
        }

        bool depth = binding.SampleType is ShaderSampleType.Depth;

        if (!binding.Arrayed)
        {
            return depth ? _depth.View : _flat.View;
        }

        return depth ? _depthSlices.View : _slices.View;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        _depthSlices.Dispose();
        _depth.Dispose();
        _slices.Dispose();
        _volume.Dispose();
        _flat.Dispose();
    }
}
