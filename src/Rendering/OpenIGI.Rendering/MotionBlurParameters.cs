namespace OpenIGI.Rendering;

/// <summary>
/// The motion blur's four parameter blocks and its shutter, the same for every backend.
/// </summary>
/// <remarks>
/// The blur is a tile-based reconstruction filter in the manner of McGuire et al. 2012: the screen is
/// cut into tiles, each tile records the slowest and fastest motion inside it, the fastest is spread to
/// the neighbours it could smear into, and every pixel then gathers along its tile's dominant motion.
/// </remarks>
public static class MotionBlurParameters
{
    /// <summary>The square of pixels one tile covers, which is the flatten stage's group size.</summary>
    public const int TileSize = 16;

    /// <summary>The number of floats in the half-resolution, dilate and apply blocks: two vectors.</summary>
    public const int SmallFloats = 8;

    /// <summary>The number of floats in the flatten block: three vectors.</summary>
    public const int FlattenFloats = 12;

    /// <summary>The longest smear, end to end, as a percentage of the screen's width.</summary>
    /// <remarks>
    /// Half of it goes either side of a pixel. Past this a fast turn would read as a wash rather than as
    /// motion, and the gather's sample count could no longer cover the distance without gaps.
    /// </remarks>
    public const float SmearCapPercent = 5f;

    // The frame rate a velocity's smear is measured against: a frame that took longer than this smears
    // proportionally less, so the streak's length tracks speed rather than frame time.
    private const float ReferenceFrameRate = 30f;

    // The fraction of the reference frame the shutter is open for.
    private const float ShutterFraction = 0.5f;

    // The most samples the gather takes along a pixel's motion.
    private const int MaxGatherSamples = 16;

    // The shortest frame time the shutter scale trusts, so a zero or garbage delta cannot blow it up.
    private const float MinFrameSeconds = 1e-4f;

    // How the dilate scales a tile's velocity before testing its reach: not at all.
    private const float DilateVelocityScale = 1f;

    /// <summary>How many tiles cover a length of pixels.</summary>
    /// <param name="pixels">The width or height.</param>
    /// <returns>The tile count along it.</returns>
    public static int TilesFor(int pixels) => (pixels + TileSize - 1) / TileSize;

    /// <summary>Fills the half-resolution colour's block.</summary>
    /// <param name="data">The block, <see cref="SmallFloats"/> long.</param>
    /// <param name="halfWidth">The half-resolution width.</param>
    /// <param name="halfHeight">The half-resolution height.</param>
    public static void WriteHalf(Span<float> data, int halfWidth, int halfHeight)
    {
        Begin(data, SmallFloats);
        WriteSize(data, halfWidth, halfHeight);

        // Slot four is the downsample's threshold, and nought means none: the blur wants every colour.
    }

    /// <summary>Fills the flatten block, the shutter included.</summary>
    /// <param name="data">The block, <see cref="FlattenFloats"/> long.</param>
    /// <param name="width">The width the chain runs at.</param>
    /// <param name="height">Its height.</param>
    /// <param name="near">The near plane, for linearising depth.</param>
    /// <param name="far">The far plane.</param>
    /// <param name="amount">How much of a frame's travel to smear.</param>
    /// <param name="maxPercent">The longest blur, as a percentage of screen width.</param>
    /// <param name="frameSeconds">How long this frame took.</param>
    public static void WriteFlatten(
        Span<float> data,
        int width,
        int height,
        float near,
        float far,
        float amount,
        float maxPercent,
        float frameSeconds)
    {
        Begin(data, FlattenFloats);
        WriteSize(data, width, height);

        data[6] = near;
        data[7] = far;

        // The velocity image is a UV delta for one frame; this turns it into pixels of smear for the
        // reference frame's open shutter.
        float shutter = (1f / ReferenceFrameRate / MathF.Max(frameSeconds, MinFrameSeconds)) * amount * ShutterFraction;
        data[8] = width * shutter;
        data[9] = height * shutter;

        // The cap is per side, so half of the end-to-end length.
        data[10] = width * 0.5f * (maxPercent / 100f);
    }

    /// <summary>Fills the dilate block.</summary>
    /// <param name="data">The block, <see cref="SmallFloats"/> long.</param>
    /// <param name="tilesX">The tile grid's width.</param>
    /// <param name="tilesY">Its height.</param>
    public static void WriteDilate(Span<float> data, int tilesX, int tilesY)
    {
        Begin(data, SmallFloats);
        WriteSize(data, tilesX, tilesY);
        data[4] = DilateVelocityScale;
    }

    /// <summary>Fills the gather's block.</summary>
    /// <param name="data">The block, <see cref="SmallFloats"/> long.</param>
    /// <param name="width">The width the chain runs at.</param>
    /// <param name="height">Its height.</param>
    /// <param name="tilesX">The tile grid's width.</param>
    /// <param name="tilesY">Its height.</param>
    public static void WriteApply(Span<float> data, int width, int height, int tilesX, int tilesY)
    {
        Begin(data, SmallFloats);
        WriteSize(data, width, height);
        data[4] = tilesX;
        data[5] = tilesY;
        data[6] = MaxGatherSamples;
    }

    private static void Begin(Span<float> data, int floats)
    {
        if (data.Length != floats)
        {
            throw new ArgumentException($"The block is {floats} floats, not {data.Length}.", nameof(data));
        }

        data.Clear();
    }

    private static void WriteSize(Span<float> data, int width, int height)
    {
        data[0] = width;
        data[1] = height;
        data[2] = 1f / width;
        data[3] = 1f / height;
    }
}
