using OpenIGI.Core.Numerics;
using OpenIGI.Rendering.Temporal;

namespace OpenIGI.Rendering;

/// <summary>
/// The parameter blocks of the flashlight's two shadow dispatches, the same for every backend.
/// </summary>
/// <remarks>
/// <para>
/// FlashlightShadow fires one ray per pixel from the surface to a point on the lens -- against the world
/// where the adapter traces, through the depth buffer where it does not -- and stores whether it got there
/// and, if not, how wide a penumbra the occluder implies. FlashlightShadowFilter spreads each answer over that
/// penumbra, weighted by depth so one surface's shadow does not bleed onto the next. The shading reads the
/// result at its own pixel.
/// </para>
/// <para>
/// There is no temporal half, on purpose. The light hangs off the player, so a shadow it cast last frame
/// moved with the player: reprojecting by the world would drag it, and keeping it at the same pixel would
/// smear it through every turn. A torch's shadow is nearly hard -- the emitter is a couple of centimetres
/// across -- so one ray and a small filter is both the cheap answer and the right one.
/// </para>
/// </remarks>
public static class FlashlightShadowParameters
{
    /// <summary>The number of floats in the trace's block: a rotation and seven vectors.</summary>
    public const int TraceFloats = 44;

    /// <summary>The number of floats in the filter's block: three vectors.</summary>
    public const int FilterFloats = 12;

    /// <summary>How many steps the depth-buffer march takes where nothing can trace.</summary>
    public const int ScreenSteps = 24;

    /// <summary>Fills the trace's block.</summary>
    /// <param name="data">The block, <see cref="TraceFloats"/> long.</param>
    /// <param name="width">The render width.</param>
    /// <param name="height">The render height.</param>
    /// <param name="viewToWorld">This frame's view-to-world rotation.</param>
    /// <param name="camera">Where the camera is, in absolute world space.</param>
    /// <param name="unprojection">How a depth sample becomes a view position.</param>
    /// <param name="light">The flashlight, in view space.</param>
    /// <param name="frameIndex">Which frame this is, so the point on the lens moves between them.</param>
    /// <param name="cutoutRecords">
    /// The device address of the traced world's hit records, for the rays' cut-out test; nought where there
    /// are none, and every candidate is then taken as solid.
    /// </param>
    /// <param name="rawNoise">
    /// Whether the trace feeds ray reconstruction raw: white noise keyed on the frame count, which then wraps
    /// at 2^24 rather than 2^16 so consecutive frames never repeat within a session's reach.
    /// </param>
    public static void WriteTrace(
        Span<float> data,
        int width,
        int height,
        in Matrix3f viewToWorld,
        Vector3f camera,
        in DepthUnprojection unprojection,
        in FlashlightLight light,
        long frameIndex,
        ulong cutoutRecords = 0,
        bool rawNoise = false)
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
        data[27] = rawNoise ? frameIndex & 0xFFFFFF : frameIndex % 65536;

        data[28] = light.ViewPosition.X;
        data[29] = light.ViewPosition.Y;
        data[30] = light.ViewPosition.Z;
        data[31] = MathF.Max(light.LensRadius, 0f);

        Vector3f axis = light.ViewAxis;
        float length = MathF.Sqrt((axis.X * axis.X) + (axis.Y * axis.Y) + (axis.Z * axis.Z));
        axis = length > 1e-6f ? new Vector3f(axis.X / length, axis.Y / length, axis.Z / length) : new Vector3f(0f, 0f, 1f);
        data[32] = axis.X;
        data[33] = axis.Y;
        data[34] = axis.Z;
        float spill = Math.Clamp(MathF.Max(light.SpillDegrees, light.OuterDegrees), 0.5f, 85f);
        data[35] = MathF.Cos(spill * (MathF.PI / 180f));

        data[36] = light.Range;
        data[37] = MathF.Max(light.Clearance, 0.02f);
        data[38] = ScreenSteps;
        data[39] = rawNoise ? 1f : 0f;

        // The records' address as a bit pattern, low word then high, as every other trace carries it.
        data[40] = BitConverter.UInt32BitsToSingle((uint)cutoutRecords);
        data[41] = BitConverter.UInt32BitsToSingle((uint)(cutoutRecords >> 32));
    }

    /// <summary>Fills the filter's block.</summary>
    /// <param name="data">The block, <see cref="FilterFloats"/> long.</param>
    /// <param name="width">The render width.</param>
    /// <param name="height">The render height.</param>
    /// <param name="unprojection">How a depth sample becomes a view position.</param>
    /// <param name="lensRadius">How wide the emitter is, in metres.</param>
    public static void WriteFilter(
        Span<float> data,
        int width,
        int height,
        in DepthUnprojection unprojection,
        float lensRadius)
    {
        Require(data, FilterFloats);
        data.Clear();

        WriteSize(data, 0, width, height);

        data[4] = unprojection.XScale;
        data[5] = unprojection.YScale;
        data[6] = unprojection.Near;
        data[7] = unprojection.Far;

        data[8] = MathF.Max(lensRadius, 0f);
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
