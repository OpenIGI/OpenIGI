// The player's flashlight, as every pass that lights with it reads it: the shading, and the fog.
//
// Not retail -- the original has no flashlight. One beam, shaped like a torch's rather than a cone's: a hot
// core, a body that falls away towards the reflector's edge, a faint ring where the reflector ends, and a dim
// spill past it. All of it analytic and without a loop, so it costs the same on every target and WGSL has
// nothing to refuse. The block is FlashlightBlock's, six vectors, and a range of nought is a light that is off.

#ifndef OPENIGI_FLASHLIGHT_INCLUDED
#define OPENIGI_FLASHLIGHT_INCLUDED

struct FlashlightParams
{
    // xyz the lens in view space, w the range (nought: off).
    float4 position;

    // xyz the beam's axis in view space, w the cosine of the spill's half-angle.
    float4 axis;

    // xyz the reflector's up in view space, w the lens radius.
    float4 up;

    // rgb colour times intensity, w the near core squared.
    float4 colour;

    // tan of the outer half-angle; the inner's, then the spill's tangent over it; the spill's level.
    float4 cone;

    // shadow mask made, volumetric scale, frame, dust per metre.
    float4 control;
};

bool FlashlightOn(FlashlightParams f)
{
    return f.position.w > 0.0;
}

// How bright the beam is along a direction leaving the lens, against its centre: one on the axis, nought
// past the spill. The radius is measured in tangent space -- where a flat wall square to the beam shows it --
// so the shape stays round and its rings stay rings however the light is aimed.
float FlashlightCookie(FlashlightParams f, float3 direction)
{
    float c = dot(f.axis.xyz, direction);
    if (c <= f.axis.w || c <= 0.0)
    {
        return 0.0;
    }

    float sine = sqrt(max(1.0 - (c * c), 0.0));
    float r = (sine / c) / max(f.cone.x, 1e-4);

    float inner = f.cone.y;
    float spillEdge = f.cone.z;

    // The hot spot, a gaussian a little narrower than the inner cone: a reflector concentrates its light
    // there, and it is what reads as "a torch" rather than as a stage light.
    float hot = exp(-((r * r) / max(0.3 * inner * inner, 1e-4)));

    // The beam's body, flat to the inner cone and falling smoothly to the reflector's edge.
    float body = 1.0 - smoothstep(inner, 1.0, r);

    // The reflector's rim: a slightly brighter band just inside the edge, where the facets stop.
    float ring = exp(-(((r - 0.86) * (r - 0.86)) / 0.004));

    // Past the edge, the light that left the emitter without touching the reflector.
    float spill = (1.0 - smoothstep(1.0, spillEdge, r)) * f.cone.w;

    // A real lens is not perfect: a faint, irregular ripple around the beam and across it, turned with the
    // reflector so it stays put on the wall while the torch does.
    float3 side = cross(f.up.xyz, f.axis.xyz);
    float sideLength = length(side);
    float3 across = sideLength > 1e-4 ? side / sideLength : float3(1.0, 0.0, 0.0);
    float3 upward = cross(f.axis.xyz, across);
    float around = atan2(dot(direction, upward), dot(direction, across) + 1e-6);
    float flaw = 1.0 + (0.045 * r * sin((5.0 * around) + 1.3)) + (0.03 * sin((33.0 * r) + (2.0 * around)));

    float beam = (0.55 * hot) + (0.45 * body) + (0.07 * ring);
    return max((beam * flaw) + spill, 0.0);
}

// The light the beam puts on a point, before its surface or its shadow: the cookie, the windowed inverse
// square and the colour. View-space in and out; toLight is the unit direction from the point to the lens.
float3 FlashlightIrradiance(FlashlightParams f, float3 viewPos, out float3 toLight, out float distance)
{
    float3 fromLens = viewPos - f.position.xyz;
    float distanceSq = dot(fromLens, fromLens);
    distance = sqrt(max(distanceSq, 1e-8));
    toLight = -fromLens / distance;

    float range = f.position.w;
    if (range <= 0.0 || distance >= range)
    {
        return float3(0.0, 0.0, 0.0);
    }

    float cookie = FlashlightCookie(f, -toLight);
    if (cookie <= 0.0)
    {
        return float3(0.0, 0.0, 0.0);
    }

    // The lamps' window, so the beam ends where it says it does rather than at a hard line.
    float ratio = distanceSq / (range * range);
    float window = saturate(1.0 - (ratio * ratio));
    window *= window;

    return f.colour.rgb * (cookie * window / max(distanceSq, f.colour.w));
}

#endif
