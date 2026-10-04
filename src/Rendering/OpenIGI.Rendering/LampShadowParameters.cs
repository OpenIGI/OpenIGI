using OpenIGI.Core.Numerics;
using OpenIGI.Rendering.Temporal;

namespace OpenIGI.Rendering;

/// <summary>
/// The parameter blocks of the traced lamp visibility's three dispatches, the same for every backend.
/// </summary>
/// <remarks>
/// <para>
/// The trace fires one ray per half-resolution pixel at the lamp its reservoir picks (the LampShadow module);
/// the temporal half accumulates that onto a reprojected, neighbourhood-clamped history
/// (LampDenoiseTemporal); the spatial half filters what is left where its variance says there is still noise
/// (LampDenoiseSpatial). The shading reads the result. Written once so the renderers cannot drift apart: the
/// blocks are the shaders' own layouts, and the trace's carries the shading's lamp block byte for byte, so
/// the two cannot disagree about which lamp is which.
/// </para>
/// </remarks>
public static class LampShadowParameters
{
    /// <summary>How many frame pixels each side of one lamp-visibility texel covers.</summary>
    /// <remarks>
    /// One: the visibility is traced and denoised at the frame's own resolution. It was two, and at half
    /// resolution one sample stood for four pixels -- which is harmless on an open wall and wrong wherever
    /// a thin occluder crosses the view. Seen through a chain-link fence, nearly every two-by-two block held
    /// a wire and the building behind it, the building's pixels inherited the wire's clear line to the
    /// lamps, and the building glowed through the fence in the fence's own diamond pattern. Every pixel
    /// asking its own question is the only answer that does not depend on what stands in front of it.
    /// </remarks>
    public const int ResolutionDivisor = 1;

    /// <summary>The traced size along one axis, for a frame this many pixels along it.</summary>
    /// <param name="frame">The frame's size along the axis.</param>
    /// <returns>The visibility's size along it, never nought.</returns>
    public static int TracedSize(int frame) => Math.Max(1, frame / ResolutionDivisor);

    /// <summary>How a frame-pixel jitter or offset scales into the visibility's pixels.</summary>
    public const float JitterScale = 1f / ResolutionDivisor;

    /// <summary>The number of floats in the trace's header: a rotation and four vectors.</summary>
    /// <remarks>
    /// Exactly right or nothing works: the lamp arrays are read at a fixed offset after it, so a header even
    /// one vector longer than the shader believes shifts every lamp and the trace reads a count of nought.
    /// </remarks>
    public const int TraceHeaderFloats = 32;

    /// <summary>The number of floats in the trace's block: the header, then the shading's lamp block.</summary>
    public const int TraceFloats = TraceHeaderFloats + DeferredLampBlock.Floats;

    /// <summary>The number of floats in the temporal half's block: two rotations and six vectors.</summary>
    public const int TemporalFloats = 56;

    /// <summary>The number of floats in the spatial half's block: a rotation and three vectors.</summary>
    public const int SpatialFloats = 28;

    /// <summary>Fills the trace's block.</summary>
    /// <param name="data">The block, <see cref="TraceFloats"/> long.</param>
    /// <param name="width">The half-resolution width.</param>
    /// <param name="height">The half-resolution height.</param>
    /// <param name="viewToWorld">This frame's view-to-world rotation.</param>
    /// <param name="camera">Where the camera is, in absolute world space.</param>
    /// <param name="unprojection">How a depth sample becomes a view position.</param>
    /// <param name="frameIndex">Which frame this is, so the reservoir and the ray move between them.</param>
    /// <param name="lamps">The lamps, packed exactly as the shading reads them (<see cref="DeferredLampBlock"/>).</param>
    /// <param name="cutoutRecords">
    /// The device address of the traced world's hit records, which the rays' cut-out test reads each
    /// surface's mask layer from; nought where there are none, and every candidate is taken as solid.
    /// </param>
    /// <param name="sunDirection">The world direction towards the sun.</param>
    /// <param name="sunLuminance">
    /// The sun's luminance as the lighting pass applies it, where the sun was traced this frame; nought
    /// otherwise, which keeps every lamp shadow the sun's share would have skipped.
    /// </param>
    /// <param name="minimumVisible">
    /// The summed lamp light below which a pixel is left lit without a ray, because no shadow there could
    /// be seen; nought traces every pixel a lamp reaches.
    /// </param>
    public static void WriteTrace(
        Span<float> data,
        int width,
        int height,
        in Matrix3f viewToWorld,
        Vector3f camera,
        in DepthUnprojection unprojection,
        int frameIndex,
        ReadOnlySpan<float> lamps,
        ulong cutoutRecords = 0,
        float minimumVisible = 0f,
        Vector3f sunDirection = default,
        float sunLuminance = 0f)
    {
        Require(data, TraceFloats);
        data.Clear();

        TemporalParameters.WriteRotation(data, 0, viewToWorld);

        data[16] = unprojection.XScale;
        data[17] = unprojection.YScale;
        data[18] = unprojection.Near;
        data[19] = unprojection.Far;

        WriteSize(data, 20, width, height);

        data[24] = camera.X;
        data[25] = camera.Y;
        data[26] = camera.Z;
        data[27] = frameIndex;

        data[28] = sunDirection.X;
        data[29] = sunDirection.Y;
        data[30] = sunDirection.Z;
        data[31] = MathF.Max(sunLuminance, 0f);

        lamps[..Math.Min(lamps.Length, data.Length - TraceHeaderFloats)].CopyTo(data[TraceHeaderFloats..]);

        // Into the lamp block's source vector, whose last three slots the shading does not read: the
        // records' address as a bit pattern, low word then high, as every other trace carries it.
        data[TraceHeaderFloats + CutoutRecordsLow] = BitConverter.UInt32BitsToSingle((uint)cutoutRecords);

        // The lamp light below which nothing a shadow could take away would show; see the trace's skip.
        data[TraceHeaderFloats + MinimumVisibleSlot] = MathF.Max(minimumVisible, 0f);
        data[TraceHeaderFloats + CutoutRecordsLow + 1] = BitConverter.UInt32BitsToSingle((uint)(cutoutRecords >> 32));
    }

    // Where in the lamp block the trace finds the records' address: the source vector's y and z.
    private const int CutoutRecordsLow = 5;

    /// <summary>The lamp block's otherwise unused <c>source.w</c>, read only by the trace.</summary>
    private const int MinimumVisibleSlot = 7;

    /// <summary>Fills the temporal half's block.</summary>
    /// <param name="data">The block, <see cref="TemporalFloats"/> long.</param>
    /// <param name="state">
    /// This frame and the last at the HALF resolution the trace runs at, jitters included: half a pixel of
    /// full-resolution jitter is a quarter of one here.
    /// </param>
    /// <param name="hasHistory">Whether the history holds the previous frame at this size.</param>
    /// <param name="accumulate">Whether to blend onto that history at all; off is for measurement.</param>
    /// <param name="velocityValid">Whether this frame's motion field, moving objects included, is ready to read.</param>
    public static void WriteTemporal(
        Span<float> data, in TemporalFrameState state, bool hasHistory, bool accumulate, bool velocityValid = false)
    {
        Require(data, TemporalFloats);
        data.Clear();

        // A rotation's transpose is its inverse, so the previous view-to-world's transpose is the previous
        // world-to-view: the same pair the sun's accumulation and the velocity pass are handed.
        TemporalParameters.WriteRotation(data, 0, state.ViewToWorld);
        TemporalParameters.WriteRotation(data, 16, state.PreviousViewToWorld.Transposed());

        data[32] = state.Unprojection.XScale;
        data[33] = state.Unprojection.YScale;
        data[34] = state.Unprojection.Near;
        data[35] = state.Unprojection.Far;

        data[36] = state.PreviousUnprojection.XScale;
        data[37] = state.PreviousUnprojection.YScale;
        data[38] = state.PreviousUnprojection.Near;
        data[39] = state.PreviousUnprojection.Far;

        WriteSize(data, 40, state.Width, state.Height);

        data[44] = state.CameraDelta.X;
        data[45] = state.CameraDelta.Y;
        data[46] = state.CameraDelta.Z;
        data[47] = hasHistory ? 1f : 0f;

        data[48] = state.JitterX;
        data[49] = state.JitterY;
        data[50] = state.PreviousJitterX;
        data[51] = state.PreviousJitterY;

        data[52] = accumulate ? 1f : 0f;
        data[53] = velocityValid ? 1f : 0f;
    }

    /// <summary>Fills the spatial half's block.</summary>
    /// <param name="data">The block, <see cref="SpatialFloats"/> long.</param>
    /// <param name="width">The half-resolution width.</param>
    /// <param name="height">The half-resolution height.</param>
    /// <param name="viewToWorld">The view-to-world rotation.</param>
    /// <param name="unprojection">How a depth sample becomes a view position.</param>
    /// <param name="jitterX">This frame's horizontal jitter, in half-resolution pixels.</param>
    /// <param name="jitterY">This frame's vertical jitter, in half-resolution pixels.</param>
    /// <param name="frameIndex">Which frame this is, so the disk's rotation moves between them.</param>
    /// <param name="filter">Whether to filter at all; off is for measurement.</param>
    public static void WriteSpatial(
        Span<float> data,
        int width,
        int height,
        in Matrix3f viewToWorld,
        in DepthUnprojection unprojection,
        float jitterX,
        float jitterY,
        int frameIndex,
        bool filter)
    {
        Require(data, SpatialFloats);
        data.Clear();

        TemporalParameters.WriteRotation(data, 0, viewToWorld);

        data[16] = unprojection.XScale;
        data[17] = unprojection.YScale;
        data[18] = unprojection.Near;
        data[19] = unprojection.Far;

        WriteSize(data, 20, width, height);

        data[24] = jitterX;
        data[25] = jitterY;
        data[26] = frameIndex;
        data[27] = filter ? 1f : 0f;
    }

    private static void WriteSize(Span<float> data, int at, int width, int height)
    {
        data[at + 0] = width;
        data[at + 1] = height;
        data[at + 2] = width > 0 ? 1f / width : 0f;
        data[at + 3] = height > 0 ? 1f / height : 0f;
    }

    private static void Require(Span<float> data, int floats)
    {
        if (data.Length != floats)
        {
            throw new ArgumentException($"The block is exactly {floats} floats.", nameof(data));
        }
    }
}
