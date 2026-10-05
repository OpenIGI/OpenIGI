using OpenIGI.Core.Numerics;

namespace OpenIGI.Rendering.Temporal;

/// <summary>
/// What every temporal pass needs to know about this frame and the one before it.
/// </summary>
/// <remarks>
/// <para>
/// A temporal filter answers one question over and over: where was this pixel last frame? Answering it
/// takes both frames' view rotations, both projections, how far the camera moved, and both jitters —
/// and every pass in the chain takes the same set, which is why it is one record rather than a dozen
/// parameters repeated.
/// </para>
/// <para>
/// The camera offset is a delta rather than two positions on purpose. A world position far from the
/// origin has no single-precision room left for a frame's worth of movement; the difference has it all,
/// which is why it is taken while the positions are still double.
/// </para>
/// </remarks>
/// <param name="ViewToWorld">This frame's view rotation, camera-relative.</param>
/// <param name="PreviousViewToWorld">The previous frame's.</param>
/// <param name="CameraDelta">This frame's camera position less the previous frame's.</param>
/// <param name="Unprojection">This frame's projection scales and planes.</param>
/// <param name="PreviousUnprojection">The previous frame's.</param>
/// <param name="JitterX">This frame's horizontal projection jitter, in input pixels.</param>
/// <param name="JitterY">Its vertical jitter.</param>
/// <param name="PreviousJitterX">The previous frame's horizontal jitter.</param>
/// <param name="PreviousJitterY">Its vertical jitter.</param>
/// <param name="Width">The render width in pixels.</param>
/// <param name="Height">The render height.</param>
public readonly record struct TemporalFrameState(
    Matrix3f ViewToWorld,
    Matrix3f PreviousViewToWorld,
    Vector3f CameraDelta,
    DepthUnprojection Unprojection,
    DepthUnprojection PreviousUnprojection,
    float JitterX,
    float JitterY,
    float PreviousJitterX,
    float PreviousJitterY,
    int Width,
    int Height)
{
    /// <summary>Gets one over the render width, which every pass turns a pixel into a coordinate with.</summary>
    public float InverseWidth => 1f / Math.Max(1, Width);

    /// <summary>Gets one over the render height.</summary>
    public float InverseHeight => 1f / Math.Max(1, Height);
}

/// <summary>
/// The parameter blocks the temporal passes read, laid out as the shaders declare them.
/// </summary>
/// <remarks>
/// Lifted out for the same reason the forward push block was: it is arithmetic with no graphics in it,
/// every float is positional, and a row written into the wrong slot reprojects plausibly and wrongly.
/// Stated as a function it can be checked without a device.
/// </remarks>
public static class TemporalParameters
{
    /// <summary>How many floats the block the reprojecting passes share holds.</summary>
    /// <remarks>Two four-by-four matrices and five vectors, which is what the parallax test takes.</remarks>
    public const int ReprojectionFloats = 52;

    /// <summary>Fills the block the dilation and the decimation read.</summary>
    /// <param name="block">Room for <see cref="ReprojectionFloats"/> floats.</param>
    /// <param name="state">The frame and the one before it.</param>
    /// <exception cref="ArgumentException">The block is the wrong length.</exception>
    /// <remarks>
    /// The previous rotation goes in TRANSPOSED, because what the shader wants is the way back: a
    /// rotation's transpose is its inverse, and taking it here saves doing it per pixel.
    /// </remarks>
    public static void WriteReprojection(Span<float> block, in TemporalFrameState state)
    {
        if (block.Length != ReprojectionFloats)
        {
            throw new ArgumentException(
                $"The reprojection block is exactly {ReprojectionFloats} floats.",
                nameof(block));
        }

        block.Clear();

        WriteRotation(block, 0, state.ViewToWorld);
        WriteRotation(block, 16, state.PreviousViewToWorld.Transposed());

        block[32] = state.Unprojection.XScale;
        block[33] = state.Unprojection.YScale;
        block[34] = state.Unprojection.Near;
        block[35] = state.Unprojection.Far;

        block[36] = state.Width;
        block[37] = state.Height;
        block[38] = state.InverseWidth;
        block[39] = state.InverseHeight;

        block[40] = state.CameraDelta.X;
        block[41] = state.CameraDelta.Y;
        block[42] = state.CameraDelta.Z;
        block[43] = 0f;

        block[44] = state.PreviousUnprojection.XScale;
        block[45] = state.PreviousUnprojection.YScale;
        block[46] = state.PreviousUnprojection.Near;
        block[47] = state.PreviousUnprojection.Far;

        block[48] = state.JitterX;
        block[49] = state.JitterY;
        block[50] = state.PreviousJitterX;
        block[51] = state.PreviousJitterY;
    }

    /// <summary>How many floats the size-only block holds: the render size and its reciprocal.</summary>
    public const int SizeFloats = 4;

    /// <summary>How far two depths may differ, as a fraction of the nearer, and still be one surface.</summary>
    /// <remarks>
    /// Relative rather than absolute: what counts as a step in depth at arm's length is nothing at all
    /// across a valley, and a fixed threshold picks one of the two.
    /// </remarks>
    public const float RelativeDepthTolerance = 0.02f;

    /// <summary>How many floats the thin-geometry block holds.</summary>
    public const int ThinGeometryFloats = 12;

    /// <summary>How many floats the shading-rejection block holds.</summary>
    public const int RejectShadingFloats = 8;

    /// <summary>How many floats the spatial anti-aliasing block holds.</summary>
    public const int SpatialAntiAliasingFloats = 8;

    /// <summary>How many floats the history-update block holds.</summary>
    public const int UpdateHistoryFloats = 20;

    /// <summary>How many floats the resolve block holds.</summary>
    public const int ResolveHistoryFloats = 16;

    /// <summary>Fills a block that needs nothing but the size of what it is walking over.</summary>
    /// <param name="block">Room for four floats.</param>
    /// <param name="width">The width in pixels.</param>
    /// <param name="height">The height.</param>
    /// <remarks>
    /// The reciprocal is passed rather than computed per invocation. It is the same for every one of
    /// them, and a divide is not free on every part this has to run on.
    /// </remarks>
    public static void WriteSize(Span<float> block, int width, int height)
    {
        block[0] = width;
        block[1] = height;
        block[2] = 1f / Math.Max(1, width);
        block[3] = 1f / Math.Max(1, height);
    }

    /// <summary>Fills the block the thin-geometry pass reads.</summary>
    /// <param name="block">Room for <see cref="ThinGeometryFloats"/> floats.</param>
    /// <param name="state">The frame.</param>
    /// <param name="settings">What the resolve is asked to do.</param>
    /// <param name="hasRejection">Whether a rejection field is bound for it to relax.</param>
    /// <param name="hasVelocity">Whether a motion field is bound.</param>
    public static void WriteThinGeometry(
        Span<float> block,
        in TemporalFrameState state,
        in TemporalResolveSettings settings,
        bool hasRejection,
        bool hasVelocity)
    {
        WriteSize(block, state.Width, state.Height);

        block[4] = state.Unprojection.XScale;
        block[5] = state.Unprojection.YScale;
        block[6] = state.Unprojection.Near;
        block[7] = state.Unprojection.Far;

        block[8] = RelativeDepthTolerance;
        block[9] = Math.Clamp(settings.ThinRelaxation, 0f, 1f);
        block[10] = hasRejection ? 1f : 0f;
        block[11] = hasVelocity ? 1f : 0f;
    }

    /// <summary>Fills the block the shading rejection reads.</summary>
    /// <param name="block">Room for <see cref="RejectShadingFloats"/> floats.</param>
    /// <param name="state">The frame.</param>
    /// <param name="settings">What the resolve is asked to do.</param>
    /// <param name="hasParallax">Whether the parallax rejection is bound.</param>
    /// <remarks>
    /// The exposure is here because the rejection is judged in display terms: two colours that differ by
    /// less after exposure than an eye could tell apart should not reject a texel's whole history.
    /// </remarks>
    public static void WriteRejectShading(
        Span<float> block,
        in TemporalFrameState state,
        in TemporalResolveSettings settings,
        bool hasParallax)
    {
        WriteSize(block, state.Width, state.Height);

        block[4] = MathF.Max(settings.Exposure, 1e-4f);
        block[5] = settings.ShadingBlend;
        block[6] = hasParallax ? 1f : 0f;
        block[7] = Math.Clamp(settings.ThinRelaxation, 0f, 1f);
    }

    /// <summary>Fills the block the spatial filter reads.</summary>
    /// <param name="block">Room for <see cref="SpatialAntiAliasingFloats"/> floats.</param>
    /// <param name="state">The frame.</param>
    /// <param name="settings">What the resolve is asked to do.</param>
    /// <param name="hasRejection">Whether a rejection field is bound.</param>
    public static void WriteSpatialAntiAliasing(
        Span<float> block,
        in TemporalFrameState state,
        in TemporalResolveSettings settings,
        bool hasRejection)
    {
        WriteSize(block, state.Width, state.Height);

        block[4] = MathF.Max(settings.Exposure, 1e-4f);
        block[5] = hasRejection ? 1f : 0f;
        block[6] = 0f;
        block[7] = 0f;
    }

    /// <summary>Fills the block the history update reads.</summary>
    /// <param name="block">Room for <see cref="UpdateHistoryFloats"/> floats.</param>
    /// <param name="state">The frame.</param>
    /// <param name="settings">What the resolve is asked to do.</param>
    /// <param name="outputWidth">The width the resolve will present at.</param>
    /// <param name="outputHeight">Its height.</param>
    /// <param name="bound">Which of the optional fields mean anything this frame.</param>
    public static void WriteUpdateHistory(
        Span<float> block,
        in TemporalFrameState state,
        in TemporalResolveSettings settings,
        int outputWidth,
        int outputHeight,
        TemporalHistoryInputs bound)
    {
        int historyWidth = state.Width * settings.EffectiveHistoryScale;
        int historyHeight = state.Height * settings.EffectiveHistoryScale;

        WriteSize(block, state.Width, state.Height);
        WriteSize(block[4..], historyWidth, historyHeight);

        block[8] = settings.Enabled ? 1f : 0f;
        block[9] = settings.MaxSamples;
        block[10] = settings.EffectiveHistoryScale;
        block[11] = state.JitterX;

        block[12] = state.JitterY;
        block[13] = bound.HasFlag(TemporalHistoryInputs.Rejection) ? 1f : 0f;
        block[14] = bound.HasFlag(TemporalHistoryInputs.SpatialAntiAliasing) ? 1f : 0f;
        block[15] = bound.HasFlag(TemporalHistoryInputs.ShadingRejection) ? 1f : 0f;

        // The exposure the resolve will apply, so the history's brightness weights are judged on what will be
        // seen: on raw values a night, exposed many times over, sat far below one and the weights flattened
        // into a plain average that a single lamp texel dominated -- halos and trails round every lamp.
        block[16] = MathF.Max(settings.Exposure, 1e-4f);
        block[17] = 0f;
        block[18] = 0f;
        block[19] = historyWidth / (float)Math.Max(1, outputWidth);
    }

    /// <summary>Fills the block the resolve reads.</summary>
    /// <param name="block">Room for <see cref="ResolveHistoryFloats"/> floats.</param>
    /// <param name="settings">What the resolve is asked to do.</param>
    /// <param name="historyWidth">The history's width in texels.</param>
    /// <param name="historyHeight">Its height.</param>
    /// <param name="outputWidth">The width the resolve presents at.</param>
    /// <param name="outputHeight">Its height.</param>
    public static void WriteResolveHistory(
        Span<float> block,
        in TemporalResolveSettings settings,
        int historyWidth,
        int historyHeight,
        int outputWidth,
        int outputHeight)
    {
        WriteSize(block, outputWidth, outputHeight);
        WriteSize(block[4..], historyWidth, historyHeight);

        block[8] = historyWidth / (float)Math.Max(1, outputWidth);
        block[9] = historyHeight / (float)Math.Max(1, outputHeight);
        block[10] = settings.DebugView;
        block[11] = Math.Clamp(settings.Sharpen, 0f, 1f);

        // The exposure, for the same reason the update has it.
        block[12] = MathF.Max(settings.Exposure, 1e-4f);
        block[13] = 0f;
        block[14] = 0f;
        block[15] = 0f;
    }

    /// <summary>Writes a rotation as four columns of four, which is how the shaders declare a matrix.</summary>
    /// <param name="block">The block being filled.</param>
    /// <param name="offset">Where the matrix starts.</param>
    /// <param name="m">The rotation.</param>
    /// <remarks>
    /// Column-major with a fourth row of nought, nought, nought, one: a three-by-three rotation in the
    /// space a four-by-four takes, because that is what the shader declares and a tighter packing would
    /// straddle the alignment a uniform block has to keep.
    /// </remarks>
    public static void WriteRotation(Span<float> block, int offset, in Matrix3f m)
    {
        block[offset + 0] = m.M11;
        block[offset + 1] = m.M21;
        block[offset + 2] = m.M31;
        block[offset + 3] = 0f;

        block[offset + 4] = m.M12;
        block[offset + 5] = m.M22;
        block[offset + 6] = m.M32;
        block[offset + 7] = 0f;

        block[offset + 8] = m.M13;
        block[offset + 9] = m.M23;
        block[offset + 10] = m.M33;
        block[offset + 11] = 0f;

        block[offset + 12] = 0f;
        block[offset + 13] = 0f;
        block[offset + 14] = 0f;
        block[offset + 15] = 1f;
    }
}

/// <summary>Which of the history update's optional inputs a frame actually bound.</summary>
/// <remarks>
/// Every one of them has a texture bound whatever happens — a layout with a hole in it binds nothing —
/// so what the shader needs is to be told which of them mean anything this frame.
/// </remarks>
[Flags]
public enum TemporalHistoryInputs
{
    /// <summary>None of them.</summary>
    None = 0,

    /// <summary>The reprojection rejection.</summary>
    Rejection = 1 << 0,

    /// <summary>The spatial filter's offsets.</summary>
    SpatialAntiAliasing = 1 << 1,

    /// <summary>The shading rejection.</summary>
    ShadingRejection = 1 << 2,

    /// <summary>All of them, which is what a complete frame binds.</summary>
    All = Rejection | SpatialAntiAliasing | ShadingRejection,
}
