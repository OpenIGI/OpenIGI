using OpenIGI.Core.Numerics;

namespace OpenIGI.Rendering.Sky;

/// <summary>What the atmosphere's tables are built for: where the sun is, and how high the eye is.</summary>
/// <param name="SunDirection">The world direction towards the sun.</param>
/// <param name="SunIlluminance">The sun's colour and brightness outside the atmosphere.</param>
/// <param name="ViewHeightKm">The camera's height from the planet's centre, in kilometres.</param>
/// <param name="SkyLightIntensity">How brightly the sky lights the world; one is its own brightness.</param>
public readonly record struct SkyConditions(
    Vector3f SunDirection,
    Vector3f SunIlluminance,
    float ViewHeightKm,
    float SkyLightIntensity)
{
    /// <summary>
    /// The camera's height above the planet's centre, in the kilometres the atmosphere works in.
    /// </summary>
    /// <param name="altitudeMetres">How far the camera is above the ground.</param>
    /// <returns>The height to march from.</returns>
    /// <remarks>
    /// A metre above the ground and a hundred metres above it give skies that differ by far less than
    /// the eye can tell, so this being approximate costs nothing while the game is played on foot. A
    /// mission fought from an aircraft would want the real altitude.
    /// </remarks>
    public static float ViewHeightFor(float altitudeMetres) =>
        6360.0f + Math.Max(0.0005f, altitudeMetres * 0.001f);

    /// <summary>The conditions a frame's own lighting describes, as the atmosphere wants them.</summary>
    /// <param name="lighting">The frame's lighting, whose sun is in view space.</param>
    /// <param name="viewToWorld">The camera's rotation from view space into world space.</param>
    /// <param name="altitudeMetres">How far the camera is above the ground.</param>
    /// <returns>The sun turned into the world, its illuminance, the eye's height and the sky's weight.</returns>
    /// <remarks>
    /// The atmosphere's source sun is deliberately its own: scaling how surfaces answer the direct light
    /// must not also scale the visible sky and the harmonics projected from it. A caller that does not
    /// name one gets the direct sun, which is how the two used to be shared.
    /// </remarks>
    public static SkyConditions ForScene(in SceneLighting lighting, in Matrix3f viewToWorld, float altitudeMetres)
    {
        Vector3f source = lighting.AtmosphereSunColour ?? lighting.SunColour;

        return new SkyConditions(
            lighting.WorldSunDirection(viewToWorld),
            new Vector3f(source.X * lighting.Scale, source.Y * lighting.Scale, source.Z * lighting.Scale),
            ViewHeightFor(altitudeMetres),
            Math.Max(0f, lighting.SkyLightIntensity));
    }

    /// <summary>Whether a sky built for these conditions would look the same built for another.</summary>
    /// <param name="other">The conditions to compare against.</param>
    /// <returns><see langword="true"/> when the difference could not be seen.</returns>
    /// <remarks>
    /// The whole atmospheric march for a sky that has not changed is waste: this game's sun is fixed for
    /// a mission and the view height barely moves on foot, so the tables hold their answer until an
    /// input actually moves — a day cycle scrubbed, or more than a metre of altitude. The tolerances are
    /// far tighter than any change the eye could read, so the skip never holds a stale table through a
    /// visible one.
    /// </remarks>
    public bool LooksLike(in SkyConditions other) =>
        Near(SunDirection, other.SunDirection)
        && Near(SunIlluminance, other.SunIlluminance)
        && MathF.Abs(ViewHeightKm - other.ViewHeightKm) <= 1e-3f
        && MathF.Abs(SkyLightIntensity - other.SkyLightIntensity) <= 1e-4f;

    private static bool Near(Vector3f left, Vector3f right) =>
        MathF.Abs(left.X - right.X) <= 1e-5f
        && MathF.Abs(left.Y - right.Y) <= 1e-5f
        && MathF.Abs(left.Z - right.Z) <= 1e-5f;
}

/// <summary>The block every one of the atmosphere's four passes reads.</summary>
/// <remarks>
/// One block for all four, because they march the same air: what differs between them is which table
/// they are filling, which is the size in the first vector. Backend-neutral because neither backend
/// decides any of it.
/// </remarks>
public static class SkyParameters
{
    /// <summary>How many floats the block holds.</summary>
    public const int Floats = 16;

    /// <summary>How many bytes it occupies.</summary>
    public const int Bytes = Floats * sizeof(float);

    /// <summary>Writes the block for one pass.</summary>
    /// <param name="block">Room for <see cref="Floats"/> floats.</param>
    /// <param name="width">The table being filled, in texels.</param>
    /// <param name="height">Its height.</param>
    /// <param name="conditions">Where the sun is and how high the eye is.</param>
    /// <exception cref="ArgumentException">The block is the wrong length.</exception>
    public static void Write(Span<float> block, int width, int height, in SkyConditions conditions)
    {
        if (block.Length != Floats)
        {
            throw new ArgumentException($"The sky block is {Floats} floats, not {block.Length}.", nameof(block));
        }

        block[0] = width;
        block[1] = height;
        block[2] = 1f / width;
        block[3] = 1f / height;

        block[4] = conditions.SunDirection.X;
        block[5] = conditions.SunDirection.Y;
        block[6] = conditions.SunDirection.Z;
        block[7] = 0f;

        block[8] = conditions.SunIlluminance.X;
        block[9] = conditions.SunIlluminance.Y;
        block[10] = conditions.SunIlluminance.Z;
        block[11] = 0f;

        block[12] = conditions.ViewHeightKm;
        block[13] = conditions.SkyLightIntensity;
        block[14] = 0f;
        block[15] = 0f;
    }
}
