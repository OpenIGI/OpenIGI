namespace OpenIGI.Rendering;

/// <summary>The flashlight as the shaders read it: six vectors, the same in every block that carries it.</summary>
/// <remarks>
/// <para>
/// The deferred shading's GI block and the fog's block both carry the flashlight, and both are written
/// through here, so the lit wall and the lit air cannot disagree about where the beam is. The layout is
/// <c>FlashlightParams</c> in <c>Common/Flashlight.slang</c>:
/// </para>
/// <list type="table">
/// <item><term>position</term><description>view xyz, the range.</description></item>
/// <item><term>axis</term><description>view xyz, the cosine of the spill's half-angle -- the hard cull.</description></item>
/// <item><term>up</term><description>view xyz, the lens radius.</description></item>
/// <item><term>colour</term><description>linear rgb times the intensity, the near core squared.</description></item>
/// <item><term>cone</term><description>tan of the outer half-angle, then the inner's and the spill's tangents over it, and the spill's level.</description></item>
/// <item><term>control</term><description>whether a shadow mask was made, the volumetric scale, the frame, the dust.</description></item>
/// </list>
/// <para>
/// A flashlight that is off writes zeros, and a range of nought is what every shader tests.
/// </para>
/// </remarks>
public static class FlashlightBlock
{
    /// <summary>How many floats the flashlight occupies in a block.</summary>
    public const int Floats = 24;

    /// <summary>The narrowest cone the beam is allowed, in degrees, so the tangents never divide by nothing.</summary>
    private const float MinimumDegrees = 0.5f;

    /// <summary>Writes the flashlight.</summary>
    /// <param name="block">Room for <see cref="Floats"/> floats.</param>
    /// <param name="light">The flashlight.</param>
    /// <param name="shadowMask">Whether this frame made a shadow mask for it to be read through.</param>
    /// <param name="frameIndex">Which frame this is, for anything that jitters.</param>
    /// <exception cref="ArgumentException">The block is the wrong length.</exception>
    public static void Write(Span<float> block, in FlashlightLight light, bool shadowMask, long frameIndex)
    {
        if (block.Length != Floats)
        {
            throw new ArgumentException($"The flashlight is {Floats} floats, not {block.Length}.", nameof(block));
        }

        block.Clear();

        if (!light.IsOn)
        {
            return;
        }

        float outer = Math.Clamp(light.OuterDegrees, MinimumDegrees, 80f);
        float inner = Math.Clamp(light.InnerDegrees, MinimumDegrees * 0.5f, outer);
        float spill = Math.Clamp(light.SpillDegrees, outer, 85f);

        float tanOuter = MathF.Tan(outer * (MathF.PI / 180f));
        float tanInner = MathF.Tan(inner * (MathF.PI / 180f));
        float tanSpill = MathF.Tan(spill * (MathF.PI / 180f));

        block[0] = light.ViewPosition.X;
        block[1] = light.ViewPosition.Y;
        block[2] = light.ViewPosition.Z;
        block[3] = light.Range;

        (float ax, float ay, float az) = Normalise(light.ViewAxis.X, light.ViewAxis.Y, light.ViewAxis.Z, 0f, 0f, 1f);
        block[4] = ax;
        block[5] = ay;
        block[6] = az;
        block[7] = MathF.Cos(spill * (MathF.PI / 180f));

        (float ux, float uy, float uz) = Normalise(light.ViewUp.X, light.ViewUp.Y, light.ViewUp.Z, 0f, 1f, 0f);
        block[8] = ux;
        block[9] = uy;
        block[10] = uz;
        block[11] = MathF.Max(light.LensRadius, 0f);

        float intensity = MathF.Max(light.Intensity, 0f);
        block[12] = light.Colour.X * intensity;
        block[13] = light.Colour.Y * intensity;
        block[14] = light.Colour.Z * intensity;
        float near = MathF.Max(light.NearCore, 0.01f);
        block[15] = near * near;

        block[16] = tanOuter;
        block[17] = tanInner / tanOuter;
        block[18] = tanSpill / tanOuter;
        block[19] = Math.Clamp(light.SpillLevel, 0f, 1f);

        block[20] = shadowMask ? 1f : 0f;
        block[21] = MathF.Max(light.Volumetric, 0f);
        block[22] = frameIndex % 64;
        block[23] = MathF.Max(light.Dust, 0f);
    }

    private static (float X, float Y, float Z) Normalise(float x, float y, float z, float fx, float fy, float fz)
    {
        float length = MathF.Sqrt((x * x) + (y * y) + (z * z));
        return length > 1e-6f ? (x / length, y / length, z / length) : (fx, fy, fz);
    }
}
