// Each column of froxels accumulated front to back into what a pixel at that depth sees --
// scattered light in the colour channels, surviving transmittance in the alpha.

#include "../Common/FogParams.slang"

// The flashlight's beam in the air, lit here rather than with the rest of the medium in FogScatter: that
// pass blends each froxel with a reprojected history, which is right for light fixed in the world and would
// smear a beam that swings with the view into a comet's tail. Nothing here keeps history, so the beam is
// wherever the torch is this frame.
//
// The air it lights is the medium's own plus a thin dust only the beam sees (control.w), so a torch still
// shows its shaft indoors, where the fog's density is nought, without fogging the room for everything else.
// The phase is the medium's Henyey-Greenstein blended half with an even scatter: a beam from beside the eye
// is seen almost straight down its length, where a forward-scattering phase has nearly nothing to give, and
// real dust in a real beam is not that polite.
float3 FlashlightInScatter(float3 here, float mediumExtinction, out float dust)
{
    dust = 0.0;
    FlashlightParams f = p.flashlight;
    if (!FlashlightOn(f) || f.control.y <= 0.0)
    {
        return float3(0.0, 0.0, 0.0);
    }

    float3 toLight;
    float distance;
    float3 irradiance = FlashlightIrradiance(f, here, false, toLight, distance);
    if (all(irradiance <= float3(0.0, 0.0, 0.0)))
    {
        return float3(0.0, 0.0, 0.0);
    }

    // The air right at the lens is not lit a hundred times brighter than the air a metre out: the lens is a
    // disc, not a point, and a froxel there is tens of centimetres across. Taken at face value, that first
    // metre glowed as a white wall hung in front of the camera. So the falloff stops rising at a metre, and
    // the first few metres fade in -- as in life, where your own beam's haze is seen from beside it, not
    // from inside it.
    irradiance *= saturate(distance * distance);
    float fadeIn = smoothstep(0.75, 4.0, distance);

    // The dust sits only where the beam is actually bright, and only in the first several metres, where a
    // torch's shaft is seen. Spread over the whole spill cone and the whole range it lit nothing there but
    // still dimmed the fog behind it: a dark halo around the beam.
    float colourLuminance = max(dot(f.colour.rgb, float3(0.2126, 0.7152, 0.0722)), 1e-5);
    float share = saturate(dot(irradiance, float3(0.2126, 0.7152, 0.0722)) * max(distance * distance, 1.0) / colourLuminance);
    dust = f.control.w * share * fadeIn * (1.0 - smoothstep(5.0, 12.0, distance));

    float g = p.medium.w;
    float cosTheta = dot(-toLight, normalize(-here));
    float denominator = max(1.0 + (g * g) - (2.0 * g * cosTheta), 1e-4);
    float hg = (1.0 - (g * g)) / (4.0 * kPi * denominator * sqrt(denominator));
    float phase = lerp(hg, 1.0 / (4.0 * kPi), 0.5);

    return irradiance * p.albedo.rgb * (mediumExtinction + dust) * phase * f.control.y * fadeIn;
}

[[vk::binding(0, 0)]] Texture3D<float4> uScattering;
[[vk::binding(2, 0)]] [[vk::image_format("rgba16f")]] WTexture3D<float4> uIntegrated;

[shader("compute")]
[numthreads(8, 8, 1)]
void computeMain(uint3 id : SV_DispatchThreadID)
{
    int2 column = int2(id.xy);
    int3 size = int3(p.grid.xyz + 0.5);
    if (any(column >= size.xy))
    {
        return;
    }

    // Each slice's medium is taken as constant over its slab and integrated analytically against its own
    // extinction (Hillaire 2015), so the light it adds cannot exceed what it could scatter however long
    // the slab. Slab lengths are measured along this column's own ray, so the corners of the frame, whose
    // rays cross each slice obliquely, get the longer path they really have.
    float2 uv = (float2(column) + 0.5) / p.grid.xy;
    float3 previous = ViewPositionAt(uv, SliceViewDepth(0.0));

    float3 accumulated = float3(0.0, 0.0, 0.0);
    float transmittance = 1.0;
    for (int z = 0; z < size.z; z++)
    {
        float4 medium = uScattering.Load(int4(column, z, 0));

        float3 here = ViewPositionAt(uv, SliceViewDepth(float(z) + 0.5));
        float stepLength = length(here - previous);
        previous = here;

        float dust;
        medium.rgb += FlashlightInScatter(here, medium.a, dust);
        medium.a += dust;

        float slabTransmittance = exp(-medium.a * stepLength);
        float3 slab = (medium.rgb - (medium.rgb * slabTransmittance)) / max(medium.a, 1e-5);

        accumulated += slab * transmittance;
        transmittance *= slabTransmittance;

        uIntegrated.Store(int3(column, z), float4(accumulated, transmittance));
    }
}
