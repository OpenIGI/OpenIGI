// Projects the whole sky onto nine spherical-harmonic coefficients per colour, which is all a matte
// surface needs to know about it.
//
// A diffuse surface does not care where in the sky its light came from, only about the cosine-weighted
// average over the hemisphere it faces. Nine numbers per channel reproduce that to well within what the
// eye can tell, and reduce lighting a pixel from the sky to a handful of dot products.
//
// Sixty-four directions spread evenly over the sphere, each carrying an equal share of its solid angle.
// The sky-view table is projected directly, with no intermediate cube capture, which is the same
// integral without a resampling step in the middle of it.
//
// The coefficients are written already folded with the cosine convolution and the division by pi, so
// evaluating them is three dot products and reading them costs a surface nothing but a multiply by its
// own colour.

#include "../Common/SkyView.slang"

[[vk::binding(0, 0)]] Texture2D<float4> uSkyView;
[[vk::binding(1, 0)]] SamplerState uSkyViewSampler;

[[vk::binding(2, 0)]] [[vk::image_format("rgba32f")]] WTexture2D<float4> uIrradiance;

struct Params
{
    float4 size;
    float4 sunDir;
    float4 sunColour;

    // view height km, sky light intensity, _, _.
    float4 misc;
    // world direction towards the moon and the share of it the air scatters; the moon's illuminance
    // outside the atmosphere, nought by day.
    float4 moonDir;
    float4 moonColour;
};

[[vk::binding(3, 0)]] ConstantBuffer<Params> p;

groupshared float3 sCoefficients[64][9];

[shader("compute")]
[numthreads(8, 8, 1)]
void computeMain(uint3 localId : SV_GroupThreadID)
{
    uint index = (localId.y * 8u) + localId.x;

    // An even spread over the sphere: the vertical coordinate is the cosine rather than the angle, which
    // is what makes equal steps cover equal solid angle.
    float2 e = (float2(localId.xy) + 0.5) / 8.0;
    float phi = 2.0 * kPi * e.x;
    float cosTheta = 1.0 - (2.0 * e.y);
    float sinTheta = sqrt(max(0.0, 1.0 - (cosTheta * cosTheta)));
    float3 dir = float3(sinTheta * cos(phi), sinTheta * sin(phi), cosTheta);

    const float solidAnglePerSample = (4.0 * kPi) / 64.0;

    // The sky's own brightness, times how strongly the scene wants to be lit by it. That scale belongs to
    // the sky light rather than to the atmosphere: the table itself stays physical and only what reads it
    // is scaled.
    float3 radiance = SampleSkyView(uSkyView, uSkyViewSampler, dir, p.misc.x)
        * solidAnglePerSample * p.misc.y;

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

    GroupMemoryBarrierWithGroupSync();

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

        GroupMemoryBarrierWithGroupSync();
    }

    if (index != 0u)
    {
        return;
    }

    float3 c[9];
    for (int i = 0; i < 9; i++)
    {
        c[i] = sCoefficients[0][i];
    }

    // The cosine convolution and the division by pi, folded in here so that evaluating the result is
    // only dot products and a surface never divides by pi again.
    const float k0 = 0.28209479177387814;
    const float k1 = 0.32573500793527993;
    const float k2 = 0.27313710764801974;
    const float k3 = 0.078847843075366648;
    const float k4 = 0.13656855382400987;

    uIrradiance.Store(
        int2(0, 0), float4(-k1 * c[3].r, -k1 * c[1].r, k1 * c[2].r, (k0 * c[0].r) - (k3 * c[6].r)));
    uIrradiance.Store(
        int2(1, 0), float4(-k1 * c[3].g, -k1 * c[1].g, k1 * c[2].g, (k0 * c[0].g) - (k3 * c[6].g)));
    uIrradiance.Store(
        int2(2, 0), float4(-k1 * c[3].b, -k1 * c[1].b, k1 * c[2].b, (k0 * c[0].b) - (k3 * c[6].b)));
    uIrradiance.Store(
        int2(3, 0), float4(k2 * c[4].r, -k2 * c[5].r, 3.0 * k3 * c[6].r, -k2 * c[7].r));
    uIrradiance.Store(
        int2(4, 0), float4(k2 * c[4].g, -k2 * c[5].g, 3.0 * k3 * c[6].g, -k2 * c[7].g));
    uIrradiance.Store(
        int2(5, 0), float4(k2 * c[4].b, -k2 * c[5].b, 3.0 * k3 * c[6].b, -k2 * c[7].b));
    uIrradiance.Store(int2(6, 0), float4(k4 * c[8].r, k4 * c[8].g, k4 * c[8].b, 1.0));

    // The sky's plain average, for anything that wants one number rather than nine.
    float3 average = c[0] * (1.0 / (0.282095 * 4.0 * kPi));
    uIrradiance.Store(int2(7, 0), float4(average, 1.0));
}
