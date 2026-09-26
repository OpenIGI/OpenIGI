using OpenIGI.Rendering.Rhi;

namespace OpenIGI.Rendering.Pipeline;

/// <summary>The mission's authored colour grade: a cube the resolve looks every displayed colour up in.</summary>
public sealed partial class GraphicsDevice
{
    private IRhiTexture? _colourLut;
    private int _colourLutSize;

    /// <inheritdoc/>
    /// <remarks>
    /// The file's triples are uploaded widened into half-float voxels with red varying fastest, which is also
    /// the order a volume's own memory runs. The cube it replaces is retired rather than freed, because a
    /// grade can be chosen while frames that sample the old one are in flight.
    /// </remarks>
    public void SetColourLookup(ColourLookupTable? table)
    {
        ThrowIfDisposed();

        if (_colourLut is { } previous)
        {
            _retired.Retire(previous);
            _colourLut = null;
            _colourLutSize = 0;
        }

        if (table is null)
        {
            return;
        }

        int size = table.Size;
        int voxels = size * size * size;
        ReadOnlySpan<float> entries = table.Entries;
        Half[] texels = new Half[voxels * 4];

        for (int i = 0; i < voxels; i++)
        {
            texels[(i * 4) + 0] = (Half)entries[(i * 3) + 0];
            texels[(i * 4) + 1] = (Half)entries[(i * 3) + 1];
            texels[(i * 4) + 2] = (Half)entries[(i * 3) + 2];
            texels[(i * 4) + 3] = (Half)1f;
        }

        _colourLut = _device.CreateTexture(new RhiTextureDescriptor(
            size,
            size,
            RhiTextureFormat.Rgba16Float,
            RhiTextureUsage.Sampled | RhiTextureUsage.CopyDestination,
            DepthOrArrayLayers: size,
            Dimension: RhiTextureViewDimension.Texture3D,
            Label: $"grade.{table.Name}"));

        _device.WriteTexture<Half>(
            new RhiTextureRegion(_colourLut),
            texels,
            bytesPerRow: size * 4 * 2,
            new RhiExtent3D(size, size, size));

        _colourLutSize = size;
    }

    /// <summary>The grade cube's edge for this frame, or nought where the frame is not graded.</summary>
    /// <remarks>A cube loaded, the strength above nought, and not a mask view.</remarks>
    private float GradeSize(in PostProcessSettings post) =>
        _colourLut is not null && post.ColourLookup > 0f && !_debugView.IsMask() ? _colourLutSize : 0f;

    private void DisposeGrade()
    {
        _colourLut?.Dispose();
        _colourLut = null;
    }
}
