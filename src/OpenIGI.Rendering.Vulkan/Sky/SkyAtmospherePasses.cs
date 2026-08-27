namespace OpenIGI.Rendering.Vulkan.Sky;

/// <summary>The compute shaders that build the atmosphere's lookup tables.</summary>
/// <remarks>
/// Three tables, each built from the one before it. Transmittance and multiple scattering depend only on
/// the air itself, so they are built once. The sky view and the sky's spherical harmonics depend on where
/// the sun is, so they are rebuilt whenever it moves.
/// </remarks>
internal static class SkyAtmospherePasses
{
    /// <summary>How much light survives between a point at some altitude and space, in some direction.</summary>
    public static string Transmittance =>
        $$"""
        #version 450
        layout(local_size_x = 8, local_size_y = 8) in;

        layout(set = 0, binding = 0, rgba16f) uniform writeonly image2D uTransmittance;
        layout(set = 0, binding = 1) uniform Params
        {
            vec4 uSize;       // width, height, 1/width, 1/height
            vec4 uSunDir;     // unused here
            vec4 uSunColour;  // unused here
            vec4 uMisc;       // view height km, _, _, _
        } p;

        {{SkyAtmosphereShaders.Common}}

        // No table to read yet, so transmittance towards the sun is left at one and no multiple
        // scattering is taken; all this march accumulates is the optical depth along its own ray.
        vec3 SampleTransmittanceLut(float viewHeight, float lightZenithCos)
        {
            return vec3(1.0);
        }

        vec3 SampleMultiScattering(vec3 worldPos, float lightZenithCos)
        {
            return vec3(0.0);
        }

        {{SkyAtmosphereShaders.RayMarch}}

        void main()
        {
            ivec2 px = ivec2(gl_GlobalInvocationID.xy);
            if (px.x >= int(p.uSize.x) || px.y >= int(p.uSize.y))
            {
                return;
            }

            vec2 uv = (vec2(px) + 0.5) * p.uSize.zw;
            float viewHeight;
            float viewZenithCos;
            TransmittanceUvToParams(uv, viewHeight, viewZenithCos);

            vec3 worldPos = vec3(0.0, 0.0, viewHeight);
            vec3 worldDir = vec3(0.0, sqrt(max(0.0, 1.0 - (viewZenithCos * viewZenithCos))), viewZenithCos);

            // Ten samples, the reference's transmittance count. Fewer than seem right for an integral
            // over the whole atmosphere, and correct anyway: the closed-form step integration makes a
            // short march behave like a long one, and this table is read by everything downstream, so
            // matching the reference here matters more than being independently more precise.
            ScatteringResult r = IntegrateScatteredLuminance(
                worldPos, worldDir, vec3(0.0, 0.0, 1.0), vec3(0.0),
                false, 10.0, false, false, false);

            imageStore(uTransmittance, px, vec4(EncodeTransmittance(exp(-r.opticalDepth)), 1.0));
        }
        """;

    /// <summary>
    /// How much light arrives at a point having bounced more than once, which is what fills the sky away
    /// from the sun and stops the horizon going black.
    /// </summary>
    /// <remarks>
    /// Estimated by firing two rays — straight up and straight down — from each altitude and light angle,
    /// measuring both the light that arrives and how readily the air would scatter it onwards, then
    /// summing the resulting series of ever-weaker bounces. Cheap, and close enough that the reference
    /// ships it as the default rather than its own more careful version.
    /// </remarks>
    public static string MultiScatter =>
        $$"""
        #version 450
        layout(local_size_x = 8, local_size_y = 8) in;

        layout(set = 0, binding = 0) uniform sampler2D uTransmittanceLut;
        layout(set = 0, binding = 1, rgba16f) uniform writeonly image2D uMultiScatter;
        layout(set = 0, binding = 2) uniform Params
        {
            vec4 uSize;
            vec4 uSunDir;
            vec4 uSunColour;
            vec4 uMisc;
        } p;

        {{SkyAtmosphereShaders.Common}}

        vec3 SampleTransmittanceLut(float viewHeight, float lightZenithCos)
        {
            return DecodeTransmittance(
                texture(uTransmittanceLut, TransmittanceParamsToUv(viewHeight, lightZenithCos)).rgb);
        }

        // The table being built here is the first estimate of it, so there is nothing to read yet.
        vec3 SampleMultiScattering(vec3 worldPos, float lightZenithCos)
        {
            return vec3(0.0);
        }

        {{SkyAtmosphereShaders.RayMarch}}

        void main()
        {
            ivec2 px = ivec2(gl_GlobalInvocationID.xy);
            if (px.x >= int(p.uSize.x) || px.y >= int(p.uSize.y))
            {
                return;
            }

            vec2 uv = (vec2(px) + 0.5) * p.uSize.zw;

            // Across the table: the sun's angle from straight up. Down it: altitude.
            float sunZenithCos = (uv.x * 2.0) - 1.0;
            vec3 sunDir = vec3(0.0, sqrt(clamp(1.0 - (sunZenithCos * sunZenithCos), 0.0, 1.0)), sunZenithCos);
            float viewHeight = kBottomRadiusKm + (uv.y * (kTopRadiusKm - kBottomRadiusKm));

            vec3 worldPos = vec3(0.0, 0.0, viewHeight);
            const vec3 up = vec3(0.0, 0.0, 1.0);

            // A pure white sun, so the table is a transfer function rather than a lit result — the real
            // sun's colour and brightness are applied wherever it is read.
            const vec3 white = vec3(1.0);
            // The reference's multiple-scattering count.
            const float samples = 15.0;

            ScatteringResult r0 = IntegrateScatteredLuminance(
                worldPos, up, sunDir, white, true, samples, false, false, false);
            ScatteringResult r1 = IntegrateScatteredLuminance(
                worldPos, -up, sunDir, white, true, samples, false, false, false);

            const float sphereSolidAngle = 4.0 * kPi;
            const float isotropicPhase = 1.0 / sphereSolidAngle;

            vec3 integrated = (sphereSolidAngle / 2.0) * (r0.luminance + r1.luminance);
            vec3 scatteredAsOne = 0.5 * (r0.multiScatteringAs1 + r1.multiScatteringAs1);
            vec3 inScattered = integrated * isotropicPhase;

            // Each bounce scatters a fraction of what the last one delivered, so the total is a geometric
            // series. Five terms rather than the closed form, because an extreme atmosphere can push the
            // ratio past one and the closed form would then diverge.
            vec3 rSqr = scatteredAsOne * scatteredAsOne;
            vec3 total = inScattered
                * (1.0 + scatteredAsOne + rSqr + (scatteredAsOne * rSqr) + (rSqr * rSqr));

            imageStore(uMultiScatter, px, vec4(total * kMultiScatteringFactor, 1.0));
        }
        """;

    /// <summary>The sky's luminance in every direction from where the camera stands.</summary>
    /// <remarks>
    /// <para>
    /// Parameterised as a latitude and longitude around the viewer, but with the latitude packed towards
    /// the horizon, where the sky changes fastest and where a low sun does most of its work. The two
    /// halves of the vertical axis map above and below the horizon separately, so the horizon itself lands
    /// exactly on a texel boundary rather than being smeared across one.
    /// </para>
    /// <para>
    /// Built directly in world space with the vertical as the up axis. The reference builds it in a frame
    /// that follows the planet's local up, which matters when the camera can travel far enough for that to
    /// change; this game is played on a patch of ground where up is up, so using world directions removes
    /// a transform that could only be got wrong.
    /// </para>
    /// </remarks>
    public static string SkyView =>
        $$"""
        #version 450
        layout(local_size_x = 8, local_size_y = 8) in;

        layout(set = 0, binding = 0) uniform sampler2D uTransmittanceLut;
        layout(set = 0, binding = 1) uniform sampler2D uMultiScatterLut;
        layout(set = 0, binding = 2, rgba16f) uniform writeonly image2D uSkyView;
        layout(set = 0, binding = 3) uniform Params
        {
            vec4 uSize;
            vec4 uSunDir;     // world direction towards the sun
            vec4 uSunColour;  // illuminance outside the atmosphere
            vec4 uMisc;       // view height km, _, _, _
        } p;

        {{SkyAtmosphereShaders.Common}}
        {{SkyAtmosphereShaders.Lookup}}
        {{SkyAtmosphereShaders.RayMarch}}

        void main()
        {
            ivec2 px = ivec2(gl_GlobalInvocationID.xy);
            if (px.x >= int(p.uSize.x) || px.y >= int(p.uSize.y))
            {
                return;
            }

            // Pulled in by half a texel at each edge so the extremes of the range land on texel centres
            // and bilinear filtering never reaches past them.
            vec2 uv = (vec2(px) + 0.5) * p.uSize.zw;
            uv = (uv - (0.5 * p.uSize.zw)) * (p.uSize.xy / (p.uSize.xy - 1.0));

            float viewHeight = p.uMisc.x;

            float horizonLength = sqrt(max(0.0, (viewHeight * viewHeight) - (kBottomRadiusKm * kBottomRadiusKm)));
            float beta = acos(clamp(horizonLength / viewHeight, -1.0, 1.0));
            float zenithHorizonAngle = kPi - beta;

            float viewZenithAngle;
            if (uv.y < 0.5)
            {
                float coord = 1.0 - (2.0 * uv.y);
                viewZenithAngle = zenithHorizonAngle * (1.0 - (coord * coord));
            }
            else
            {
                float coord = (uv.y * 2.0) - 1.0;
                viewZenithAngle = zenithHorizonAngle + (beta * coord * coord);
            }

            float cosZenith = cos(viewZenithAngle);
            float sinZenith = sqrt(max(0.0, 1.0 - (cosZenith * cosZenith)));
            float azimuth = uv.x * 2.0 * kPi;

            vec3 worldDir = vec3(sinZenith * cos(azimuth), sinZenith * sin(azimuth), cosZenith);
            vec3 worldPos = vec3(0.0, 0.0, viewHeight);

            // Variable rate: the count and the step spacing both come from how far this ray runs, which
            // is what the fixed thirty-two was standing in for. A horizon ray crosses hundreds of
            // kilometres of which only the first few contain any air, so equal steps put almost every
            // sample in vacuum -- the count below is ignored when the rate varies.
            ScatteringResult r = IntegrateScatteredLuminance(
                worldPos,
                worldDir,
                p.uSunDir.xyz,
                p.uSunColour.rgb,
                false,
                32.0,
                true,
                true,
                true);

            float greyTransmittance = dot(r.transmittance, vec3(1.0 / 3.0));
            imageStore(uSkyView, px, vec4(r.luminance, greyTransmittance));
        }
        """;

    /// <summary>
    /// Projects the whole sky onto nine spherical-harmonic coefficients per colour, which is all a matte
    /// surface needs to know about it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A diffuse surface does not care where in the sky its light came from, only about the cosine-weighted
    /// average over the hemisphere it faces. Nine numbers per channel reproduce that to well within what
    /// the eye can tell, and reduce lighting a pixel from the sky to a handful of dot products.
    /// </para>
    /// <para>
    /// Sixty-four directions spread evenly over the sphere, each carrying an equal share of its solid
    /// angle. The reference captures a cube first and projects that; this projects the sky-view table
    /// directly, which is the same integral without the cube's resampling in the middle of it.
    /// </para>
    /// <para>
    /// The coefficients are written already folded with the cosine convolution and the division by pi, so
    /// evaluating them is three dot products and reading them costs a surface nothing but a multiply by
    /// its own colour.
    /// </para>
    /// </remarks>
    public static string SkyIrradiance =>
        $$"""
        #version 450
        layout(local_size_x = 8, local_size_y = 8) in;

        layout(set = 0, binding = 0) uniform sampler2D uSkyView;
        layout(set = 0, binding = 1, rgba32f) uniform writeonly image2D uIrradiance;
        layout(set = 0, binding = 2) uniform Params
        {
            vec4 uSize;
            vec4 uSunDir;
            vec4 uSunColour;
            vec4 uMisc;       // view height km, sky light intensity, _, _
        } p;

        {{SkyAtmosphereShaders.Common}}
        {{SkyAtmosphereShaders.SkyViewLookup}}

        shared vec3 sCoefficients[64][9];

        void main()
        {
            uint index = (gl_LocalInvocationID.y * 8u) + gl_LocalInvocationID.x;

            // An even spread over the sphere: the vertical coordinate is the cosine rather than the angle,
            // which is what makes equal steps cover equal solid angle.
            vec2 e = (vec2(gl_LocalInvocationID.xy) + 0.5) / 8.0;
            float phi = 2.0 * kPi * e.x;
            float cosTheta = 1.0 - (2.0 * e.y);
            float sinTheta = sqrt(max(0.0, 1.0 - (cosTheta * cosTheta)));
            vec3 dir = vec3(sinTheta * cos(phi), sinTheta * sin(phi), cosTheta);

            const float solidAnglePerSample = (4.0 * kPi) / 64.0;
            // The sky's own brightness, times how strongly the scene wants to be lit by it. The reference
            // treats that as a property of the sky light rather than of the atmosphere, and so does this:
            // the table itself stays physical and only what reads it is scaled.
            vec3 radiance = SampleSkyView(uSkyView, dir, p.uMisc.x) * solidAnglePerSample * p.uMisc.y;

            // The real spherical-harmonic basis up to the second band.
            float basis[9];
            basis[0] = 0.282095;
            basis[1] = -0.488603 * dir.y;
            basis[2] = 0.488603 * dir.z;
            basis[3] = -0.488603 * dir.x;
            basis[4] = 1.092548 * dir.x * dir.y;
            basis[5] = -1.092548 * dir.y * dir.z;
            basis[6] = 0.315392 * ((3.0 * dir.z * dir.z) - 1.0);
            basis[7] = -1.092548 * dir.x * dir.z;
            basis[8] = 0.546274 * ((dir.x * dir.x) - (dir.y * dir.y));

            for (int i = 0; i < 9; i++)
            {
                sCoefficients[index][i] = radiance * basis[i];
            }

            barrier();

            // Halving tree: sixty-four partial sums become one in six steps.
            for (uint stride = 32u; stride > 0u; stride >>= 1u)
            {
                if (index < stride)
                {
                    for (int i = 0; i < 9; i++)
                    {
                        sCoefficients[index][i] += sCoefficients[index + stride][i];
                    }
                }

                barrier();
            }

            if (index != 0u)
            {
                return;
            }

            vec3 c[9];
            for (int i = 0; i < 9; i++)
            {
                c[i] = sCoefficients[0][i];
            }

            // The cosine convolution and the division by pi, folded in here so that evaluating the result
            // is only dot products and a surface never divides by pi again.
            const float k0 = 0.28209479177387814;
            const float k1 = 0.32573500793527993;
            const float k2 = 0.27313710764801974;
            const float k3 = 0.078847843075366648;
            const float k4 = 0.13656855382400987;

            imageStore(uIrradiance, ivec2(0, 0), vec4(-k1 * c[3].r, -k1 * c[1].r, k1 * c[2].r, (k0 * c[0].r) - (k3 * c[6].r)));
            imageStore(uIrradiance, ivec2(1, 0), vec4(-k1 * c[3].g, -k1 * c[1].g, k1 * c[2].g, (k0 * c[0].g) - (k3 * c[6].g)));
            imageStore(uIrradiance, ivec2(2, 0), vec4(-k1 * c[3].b, -k1 * c[1].b, k1 * c[2].b, (k0 * c[0].b) - (k3 * c[6].b)));
            imageStore(uIrradiance, ivec2(3, 0), vec4(k2 * c[4].r, -k2 * c[5].r, 3.0 * k3 * c[6].r, -k2 * c[7].r));
            imageStore(uIrradiance, ivec2(4, 0), vec4(k2 * c[4].g, -k2 * c[5].g, 3.0 * k3 * c[6].g, -k2 * c[7].g));
            imageStore(uIrradiance, ivec2(5, 0), vec4(k2 * c[4].b, -k2 * c[5].b, 3.0 * k3 * c[6].b, -k2 * c[7].b));
            imageStore(uIrradiance, ivec2(6, 0), vec4(k4 * c[8].r, k4 * c[8].g, k4 * c[8].b, 1.0));

            // The sky's plain average, for anything that wants one number rather than nine.
            vec3 average = c[0] * (1.0 / (0.282095 * 4.0 * kPi));
            imageStore(uIrradiance, ivec2(7, 0), vec4(average, 1.0));
        }
        """;
}
