using OpenIGI.Core.Numerics;

namespace OpenIGI.Rendering.Sky;

/// <summary>How much of the sun's light gets through the air to the ground, by how high the sun stands.</summary>
/// <remarks>
/// <para>
/// The same atmosphere the sky is marched through (Common/SkyAtmosphere.slang: the planet, the Rayleigh and
/// Mie layers and the ozone band, constant for constant), integrated once along the path to the sun. This
/// is what colours the sun at sunset: the blue is scattered out of the long, low path first, then the
/// green, and what reaches the ground is orange, then red, and much dimmer -- while the light scattered out
/// of it is what makes the sky blue overhead. Tinting the sun light by this transmittance keeps the direct
/// light and the sky it sits in in agreement.
/// </para>
/// <para>
/// It replaces a hand-made warm tint that was multiplied into the sun AND fed to the sky, which the sky
/// then reddened a second time by its own scattering: at sunset every direction of the sky came out a
/// uniform orange-brown and every wall facing the sun a saturated red.
/// </para>
/// </remarks>
public static class AtmosphereTransmittance
{
    private const float BottomRadiusKm = 6360f;
    private const float TopRadiusKm = 6420f;

    // The viewer's height above the ground: a couple of metres, as the sky takes it.
    private const float ViewHeightKm = 0.002f;

    private static readonly Vector3f RayleighScattering = new(0.005802f, 0.013558f, 0.033100f);
    private const float RayleighDensityExpScale = -1f / 8f;
    private const float MieExtinction = 0.004440f;
    private const float MieDensityExpScale = -1f / 1.2f;
    private static readonly Vector3f AbsorptionExtinction = new(0.000650f, 0.001881f, 0.000085f);

    private const int Steps = 48;

    /// <summary>The fraction of each channel of sunlight that reaches the ground.</summary>
    /// <param name="sinElevation">The sine of the sun's elevation: its direction's vertical component.</param>
    /// <returns>Per channel, 0 to 1. Nought once the sun is below the horizon.</returns>
    public static Vector3f At(float sinElevation)
    {
        float s = Math.Clamp(sinElevation, -1f, 1f);
        float cos = MathF.Sqrt(Math.Max(0f, 1f - (s * s)));

        // From the viewer, straight along the direction to the sun.
        double originZ = BottomRadiusKm + ViewHeightKm;
        double dx = cos;
        double dz = s;

        // Blocked by the planet: the sun has set.
        double b = originZ * dz;
        double groundDisc = (b * b) - ((originZ * originZ) - ((double)BottomRadiusKm * BottomRadiusKm));
        if (dz < 0.0 && groundDisc >= 0.0 && (-b - Math.Sqrt(groundDisc)) > 0.0)
        {
            return default;
        }

        // Out to the top of the air.
        double topDisc = (b * b) - ((originZ * originZ) - ((double)TopRadiusKm * TopRadiusKm));
        double length = -b + Math.Sqrt(Math.Max(topDisc, 0.0));
        if (length <= 0.0)
        {
            return new Vector3f(1f, 1f, 1f);
        }

        double depthR = 0.0;
        double depthG = 0.0;
        double depthB = 0.0;
        double step = length / Steps;
        for (int i = 0; i < Steps; i++)
        {
            double t = (i + 0.5) * step;
            double x = dx * t;
            double z = originZ + (dz * t);
            double height = Math.Max(0.0, Math.Sqrt((x * x) + (z * z)) - BottomRadiusKm);

            double rayleigh = Math.Exp(RayleighDensityExpScale * height);
            double mie = Math.Exp(MieDensityExpScale * height);
            double ozone = height < 25.0 ? (height / 15.0) + (1.0 - (25.0 / 15.0)) : (-height / 15.0) + (1.0 + (25.0 / 15.0));
            ozone = Math.Clamp(ozone, 0.0, 1.0);

            depthR += ((RayleighScattering.X * rayleigh) + (MieExtinction * mie) + (AbsorptionExtinction.X * ozone)) * step;
            depthG += ((RayleighScattering.Y * rayleigh) + (MieExtinction * mie) + (AbsorptionExtinction.Y * ozone)) * step;
            depthB += ((RayleighScattering.Z * rayleigh) + (MieExtinction * mie) + (AbsorptionExtinction.Z * ozone)) * step;
        }

        return new Vector3f((float)Math.Exp(-depthR), (float)Math.Exp(-depthG), (float)Math.Exp(-depthB));
    }
}
