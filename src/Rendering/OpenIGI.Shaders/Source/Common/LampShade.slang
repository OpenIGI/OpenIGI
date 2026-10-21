// One lamp's light on a surface in the lighting pass, before its shadow: the reach and the cone the lamp
// trace weighs it by, the tube or the small sphere it is lit as, and the highlight widened by the source.
//
// The host must include this after declaring its Lamps block as `lamps` and its Gi block as `gi` (for the
// view-to-world rotation).

#ifndef OPENIGI_LAMP_SHADE_INCLUDED
#define OPENIGI_LAMP_SHADE_INCLUDED

#include "LampIrradiance.slang"
#include "ShadingBrdf.slang"

// One lamp's light on this surface towards the eye, before anything stands in its way, and the surface's
// diffuse share of it for the ranking. Nought for a lamp out of reach, behind its own glass, or below the
// surface's horizon.
//
// A fitting with a measured face is lit as the tube it is: the irradiance integrated along its length (see
// LineIrradiance), so a long batten throws a long pool rather than a round one and the wall beside it
// takes the spread-out glow a tube gives; and the highlight is taken at the point along it nearest the
// reflected view ray, with the lobe widened by the tube's own thickness at that distance, so a wet floor
// shows a stretched streak rather than a pinpoint. A point lamp is a small sphere: the inverse square stops
// rising inside its own radius, rather than being softened by a metre everywhere -- which flattened every
// pool into a disc with a window-shaped edge and took the hot centre out of it.
float3 LampShade(
    int i,
    float3 viewPos,
    float3 normal,
    float3 toEye,
    float3 albedo,
    float metallic,
    float specularLevel,
    float roughness,
    out float diffuseWeight)
{
    diffuseWeight = 0.0;

    // Its reach and its cone, as the lamp trace weighs them: see Common/LampIrradiance.slang.
    float4 positionRadius = lamps.positionRadius[i];
    float3 toLightView = positionRadius.xyz - viewPos;
    float distanceSq = dot(toLightView, toLightView);
    float window = LampReachWindow(distanceSq, positionRadius.w);
    if (window <= 0.0)
    {
        return float3(0.0, 0.0, 0.0);
    }

    float3 toCentre = mul(gi.viewToWorld, float4(toLightView, 0.0)).xyz;
    float3 centreDir = toCentre * rsqrt(max(distanceSq, 1e-8));

    float4 facing = lamps.direction[i];
    if (LampFaces(facing))
    {
        window *= LampCone(mul(gi.viewToWorld, float4(facing.xyz, 0.0)).xyz, centreDir);
    }

    if (window <= 0.0)
    {
        return float3(0.0, 0.0, 0.0);
    }

    float3 colour = lamps.colourIntensity[i].rgb * lamps.colourIntensity[i].a * window;
    float source = LampSource(facing);

    float4 rect = lamps.rect[i];
    float3 halfAxis = mul(gi.viewToWorld, float4(rect.xyz, 0.0)).xyz;
    bool tube = dot(halfAxis, halfAxis) > 1e-4;

    // The diffuse irradiance, per unit of the lamp's intensity.
    float irradiance;
    float3 toSpecular = toCentre;
    float thickness = source;
    if (tube)
    {
        float3 lineStart = toCentre - halfAxis;
        float3 lineDelta = halfAxis * 2.0;
        irradiance = LineIrradiance(normal, lineStart, lineDelta);

        // Where along the tube the reflected view ray passes closest: the point whose light the highlight
        // is made of.
        float3 reflected = reflect(-toEye, normal);
        float along = dot(reflected, lineDelta);
        float denominator = dot(lineDelta, lineDelta) - (along * along);
        float t = denominator > 1e-6
            ? ((dot(reflected, lineStart) * along) - dot(lineStart, lineDelta)) / denominator
            : 0.5;
        toSpecular = lineStart + (lineDelta * saturate(t));
        thickness = clamp(rect.w, 0.02, source);
    }
    else
    {
        irradiance = LampPointIrradiance(normal, centreDir, distanceSq, source);
    }

    float specularDistanceSq = max(dot(toSpecular, toSpecular), 1e-6);
    float3 specularDir = toSpecular * rsqrt(specularDistanceSq);

    diffuseWeight = dot(colour, float3(0.2126, 0.7152, 0.0722)) * irradiance;
    if (irradiance <= 0.0 && dot(normal, specularDir) <= 0.0)
    {
        return float3(0.0, 0.0, 0.0);
    }

    // The highlight: the lobe widened by the angle the source spans from here, and renormalised so the
    // wider lobe carries the same energy rather than more.
    float alpha = max(roughness, 0.02) * max(roughness, 0.02);
    float widened = saturate(alpha + (thickness / (2.0 * sqrt(specularDistanceSq))));
    float normalisation = (alpha / widened) * (alpha / widened);

    float3 lobeDiffuse;
    float3 lobeSpecular;
    EvaluateSurface(
        albedo,
        metallic,
        specularLevel,
        sqrt(widened),
        normal,
        toEye,
        specularDir,
        lobeDiffuse,
        lobeSpecular);

    float3 diffuseColour = albedo * (1.0 - metallic);
    float specularIrradiance = 1.0 / max(specularDistanceSq, thickness * thickness);
    return colour * ((diffuseColour * irradiance) + (lobeSpecular * normalisation * specularIrradiance * kPi));
}

#endif
