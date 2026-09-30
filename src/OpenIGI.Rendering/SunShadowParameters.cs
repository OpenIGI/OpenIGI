using OpenIGI.Core.Numerics;
using OpenIGI.Rendering.Temporal;

namespace OpenIGI.Rendering;

/// <summary>
/// The parameter blocks of the traced sun shadow's three dispatches, the same for every backend.
/// </summary>
/// <remarks>
/// <para>
/// The trace fires one ray a pixel at a point on the sun's disc and records whether it got there and how
/// far the occluder was (the SunShadow module); the denoise spreads each answer over the penumbra that
/// distance implies (ShadowDenoise); the accumulation averages the result over frames before it is turned
/// into light (ShadowAccumulate). Written once so the renderers cannot drift apart: the blocks are the
/// shaders' own layouts.
/// </para>
/// </remarks>
public static class SunShadowParameters
{
    /// <summary>The number of floats in the trace's block: a rotation and six vectors.</summary>
    public const int TraceFloats = 40;

    /// <summary>The number of floats in the denoise's block: three vectors.</summary>
    public const int DenoiseFloats = 12;

    /// <summary>The number of floats in the accumulation's block: two rotations and six vectors.</summary>
    public const int AccumulateFloats = 56;

    /// <summary>How far a shadow ray looks for an occluder, in metres.</summary>
    public const float RayLength = 4000f;

    /// <summary>How far, relatively, a reprojected depth may differ before the history is thrown away.</summary>
    public const float DepthTolerance = 0.02f;

    /// <summary>Fills the trace's block.</summary>
    /// <param name="data">The block, <see cref="TraceFloats"/> long.</param>
    /// <param name="width">The render width.</param>
    /// <param name="height">The render height.</param>
    /// <param name="viewToWorld">This frame's view-to-world rotation.</param>
    /// <param name="cameraPosition">Where the camera is, in absolute world space.</param>
    /// <param name="sunDirection">The world direction towards the sun.</param>
    /// <param name="unprojection">The projection's scales and planes.</param>
    /// <param name="sunAngleDegrees">How wide the sun's disc is, which gives a shadow its penumbra.</param>
    /// <param name="frameIndex">A number that changes every frame, so the one ray lands somewhere new.</param>
    /// <param name="recordAddress">Where the hit records live, or zero to take every hit at face value.</param>
    public static void WriteTrace(
        Span<float> data,
        int width,
        int height,
        in Matrix3f viewToWorld,
        Vector3f cameraPosition,
        Vector3f sunDirection,
        in DepthUnprojection unprojection,
        float sunAngleDegrees,
        float frameIndex,
        ulong recordAddress)
    {
        Require(data, TraceFloats);
        data.Clear();

        TemporalParameters.WriteRotation(data, 0, viewToWorld);

        data[16] = unprojection.XScale;
        data[17] = unprojection.YScale;
        data[18] = unprojection.Near;
        data[19] = unprojection.Far;

        WriteSize(data, 20, width, height);

        data[24] = sunDirection.X;
        data[25] = sunDirection.Y;
        data[26] = sunDirection.Z;
        data[27] = RayLength;

        data[28] = cameraPosition.X;
        data[29] = cameraPosition.Y;
        data[30] = cameraPosition.Z;

        // The sine of the sun's half-angle, which is the form the cone sampler wants. Zero collapses the
        // disc to a point and the trace to a hard edge.
        data[31] = MathF.Sin(0.5f * float.DegreesToRadians(Math.Max(sunAngleDegrees, 0f)));

        data[32] = frameIndex;

        // The record buffer's address, as the two words the shader reassembles it from: the block is
        // declared uint4 there, so the bits are what matter and not the type they travel in.
        data[36] = BitConverter.UInt32BitsToSingle((uint)recordAddress);
        data[37] = BitConverter.UInt32BitsToSingle((uint)(recordAddress >> 32));
    }

    /// <summary>Fills the denoise's block.</summary>
    /// <param name="data">The block, <see cref="DenoiseFloats"/> long.</param>
    /// <param name="width">The render width.</param>
    /// <param name="height">The render height.</param>
    /// <param name="unprojection">The projection's scales and planes.</param>
    /// <param name="sunAngleDegrees">How wide the sun's disc is.</param>
    /// <param name="accumulationCeiling">How many frames the accumulation counts to.</param>
    public static void WriteDenoise(
        Span<float> data,
        int width,
        int height,
        in DepthUnprojection unprojection,
        float sunAngleDegrees,
        float accumulationCeiling)
    {
        Require(data, DenoiseFloats);
        data.Clear();

        WriteSize(data, 0, width, height);

        data[4] = unprojection.XScale;
        data[5] = unprojection.YScale;
        data[6] = unprojection.Near;
        data[7] = unprojection.Far;

        // The reference's penumbra formula for a directional light, its trigonometry folded onto the host:
        // the tangent of the half-angle, times the distance to the occluder, is the penumbra's radius.
        data[8] = MathF.Tan(0.5f * float.DegreesToRadians(Math.Max(sunAngleDegrees, 0f)));

        // One over the ceiling the accumulation counts to, so a stored frame count reads as a fraction
        // of converged.
        data[9] = 1f / MathF.Max(accumulationCeiling, 1f);
    }

    /// <summary>Fills the accumulation's block.</summary>
    /// <param name="data">The block, <see cref="AccumulateFloats"/> long.</param>
    /// <param name="state">This frame and the last, as the temporal passes measure them, at the render size.</param>
    /// <param name="frames">How many frames the accumulation counts to.</param>
    /// <param name="hasHistory">Whether the previous frame's accumulation is worth reading.</param>
    /// <param name="velocityValid">Whether this frame's motion field, moving objects included, is ready to read.</param>
    public static void WriteAccumulate(
        Span<float> data,
        in TemporalFrameState state,
        float frames,
        bool hasHistory,
        bool velocityValid = false)
    {
        Require(data, AccumulateFloats);
        data.Clear();

        // The same pair the velocity pass is handed: a rotation's transpose is its inverse, so this is the
        // previous frame's world-to-view. The mask and the colour have to reproject to the same place or
        // the shadow slides across the surface it belongs to.
        TemporalParameters.WriteRotation(data, 0, state.ViewToWorld);
        TemporalParameters.WriteRotation(data, 16, state.PreviousViewToWorld.Transposed());

        data[32] = state.Unprojection.XScale;
        data[33] = state.Unprojection.YScale;
        data[34] = state.Unprojection.Near;
        data[35] = state.Unprojection.Far;

        WriteSize(data, 36, state.Width, state.Height);

        data[40] = state.CameraDelta.X;
        data[41] = state.CameraDelta.Y;
        data[42] = state.CameraDelta.Z;

        data[44] = state.PreviousUnprojection.XScale;
        data[45] = state.PreviousUnprojection.YScale;
        data[46] = state.PreviousUnprojection.Near;
        data[47] = state.PreviousUnprojection.Far;

        data[48] = Math.Max(1f, frames);
        data[49] = hasHistory ? 1f : 0f;
        data[50] = DepthTolerance;

        // Whether the frame's motion field -- moving objects' own motion included -- is ready to read.
        data[51] = velocityValid ? 1f : 0f;

        data[52] = state.JitterX;
        data[53] = state.JitterY;
        data[54] = state.PreviousJitterX;
        data[55] = state.PreviousJitterY;
    }

    private static void WriteSize(Span<float> data, int at, int width, int height)
    {
        data[at + 0] = width;
        data[at + 1] = height;
        data[at + 2] = 1f / Math.Max(1, width);
        data[at + 3] = 1f / Math.Max(1, height);
    }

    private static void Require(Span<float> data, int floats)
    {
        if (data.Length != floats)
        {
            throw new ArgumentException($"The block is exactly {floats} floats.", nameof(data));
        }
    }
}
