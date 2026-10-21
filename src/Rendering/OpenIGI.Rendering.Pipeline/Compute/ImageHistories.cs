using OpenIGI.Rendering.Rhi;
using OpenIGI.Shaders;

namespace OpenIGI.Rendering.Pipeline.Compute;

/// <summary>
/// What a storage image held before a dispatch, for a module form that cannot read the image back itself.
/// </summary>
/// <remarks>
/// <para>
/// A few modules blend this frame's answer onto what a texel already held, reading an image and writing it
/// in the same dispatch. SPIR-V does that on one read-write image. WGSL allows read-write storage on three
/// single-channel formats and these images are rgba16float, so its form writes the image only and reads
/// what it held from a copy taken just before — the same values, because one invocation reads and writes
/// each texel and nothing else touches it.
/// </para>
/// <para>
/// A module says which images need it by declaring, beside the image, a texture of the same name with
/// <see cref="Suffix"/> appended (see <c>Common/ImageHistory.slang</c>). Both forms declare it, so they share
/// one binding table; only the WGSL one reads it, and only there is the copy taken.
/// </para>
/// <para>
/// One copy per image, kept for its life and remade only if the image's shape changes: the images that
/// need it — the surface cache's lighting atlases, the radiance cache's volumes — are made once a level.
/// </para>
/// </remarks>
internal sealed class ImageHistories : IDisposable
{
    /// <summary>What a history texture's name ends in, after the image it is the history of.</summary>
    public const string Suffix = "History";

    /// <summary>How far past its image's slot a history's texture slot is (<c>OPENIGI_HISTORY_SLOT</c>).</summary>
    public const int SlotOffset = 12;

    private readonly IRhiDevice _device;
    private readonly RetiredResources _retired;
    private readonly Dictionary<IRhiTexture, IRhiTexture> _copies = [];
    private bool _disposed;

    /// <summary>Initializes a new instance of the <see cref="ImageHistories"/> class.</summary>
    /// <param name="device">The device the copies are made on.</param>
    /// <param name="retired">Where a copy goes when its image does, to outlive the frames still reading it.</param>
    public ImageHistories(IRhiDevice device, RetiredResources retired)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(retired);

        _device = device;
        _retired = retired;
    }

    /// <summary>Gets whether a device's module forms read histories at all.</summary>
    /// <param name="device">The device.</param>
    /// <returns><see langword="true"/> where the device's form reads them; see <see cref="ShaderTargetForms.ReadsImageHistories"/>.</returns>
    public static bool AreRead(IRhiDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);
        return device.ShaderTarget.ReadsImageHistories();
    }

    /// <summary>The image a history texture stands for, if the binding is one.</summary>
    /// <param name="reflection">The module.</param>
    /// <param name="texture">A texture it binds.</param>
    /// <returns>The storage image of the same name less <see cref="Suffix"/>, or null when it is an ordinary texture.</returns>
    public static ShaderBinding? ImageOf(ShaderReflection reflection, ShaderBinding texture)
    {
        ArgumentNullException.ThrowIfNull(reflection);
        ArgumentNullException.ThrowIfNull(texture);

        if (texture.Kind != ShaderBindingKind.Texture || !texture.Name.EndsWith(Suffix, StringComparison.Ordinal))
        {
            return null;
        }

        // The slot as well as the name, because the name alone is a coincidence waiting to happen: the
        // screen gather writes an image called uPosition and reads last frame's positions as a texture
        // called uPositionHistory -- a real history the game binds, not a copy this takes. Matched by
        // name, it was handed a stand-in on SPIR-V, every reprojection compared against nothing, and the
        // screen GI never accumulated past a single frame's noise. The macro places a copy at its image's
        // slot plus the offset; a texture anywhere else is the module's own.
        int textureSlot = ComputeBindingSpace.SlotOf(texture.Binding);
        string image = texture.Name[..^Suffix.Length];
        return reflection.Bindings.FirstOrDefault(binding =>
            binding.Kind == ShaderBindingKind.StorageTexture
            && string.Equals(binding.Name, image, StringComparison.Ordinal)
            && ComputeBindingSpace.SlotOf(binding.Binding) + SlotOffset == textureSlot);
    }

    /// <summary>Records the copy of an image into its history, making the history first if it has none.</summary>
    /// <param name="encoder">The encoder, outside any pass.</param>
    /// <param name="image">The image about to be dispatched over.</param>
    /// <param name="dimension">How the module addresses it, which a texture does not say of itself.</param>
    /// <returns>The history's view, to bind to the module's history slot.</returns>
    public IRhiTextureView Record(IRhiCommandEncoder encoder, IRhiTexture image, RhiTextureViewDimension dimension)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(encoder);
        ArgumentNullException.ThrowIfNull(image);

        IRhiTexture copy = CopyOf(image, dimension);

        encoder.CopyTextureToTexture(
            new RhiTextureRegion(image),
            new RhiTextureRegion(copy),
            new RhiExtent3D(image.Width, image.Height, image.DepthOrArrayLayers));

        return copy.View;
    }

    /// <summary>Lets an image's history go with it.</summary>
    /// <param name="image">The image being destroyed.</param>
    public void Forget(IRhiTexture image)
    {
        ArgumentNullException.ThrowIfNull(image);

        if (_copies.Remove(image, out IRhiTexture? copy))
        {
            _retired.Retire(copy);
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        foreach (IRhiTexture copy in _copies.Values)
        {
            copy.Dispose();
        }

        _copies.Clear();
    }

    /// <summary>The image's history, made to its shape.</summary>
    private IRhiTexture CopyOf(IRhiTexture image, RhiTextureViewDimension dimension)
    {
        if (_copies.TryGetValue(image, out IRhiTexture? copy)
            && copy.Width == image.Width
            && copy.Height == image.Height
            && copy.DepthOrArrayLayers == image.DepthOrArrayLayers
            && copy.Format == image.Format)
        {
            return copy;
        }

        if (copy is not null)
        {
            _retired.Retire(copy);
        }

        copy = _device.CreateTexture(new RhiTextureDescriptor(
            image.Width,
            image.Height,
            image.Format,
            RhiTextureUsage.Sampled | RhiTextureUsage.CopyDestination,
            DepthOrArrayLayers: image.DepthOrArrayLayers,
            Dimension: dimension,
            Label: $"{image.Label}.history"));

        _copies[image] = copy;
        return copy;
    }
}
