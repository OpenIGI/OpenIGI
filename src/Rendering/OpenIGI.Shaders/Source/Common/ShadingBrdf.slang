// How a surface answers light: the microfacet response to one light, and the closed-form stand-ins the
// lighting pass takes for the environment's specular and for occlusion's bounce inside a crevice.
//
// Nothing in it reads a binding; it is functions of a material and a few directions, and the lighting pass
// and the lamp shading both ask it the same question.

#ifndef OPENIGI_SHADING_BRDF_INCLUDED
#define OPENIGI_SHADING_BRDF_INCLUDED

#include "SkyAtmosphere.slang"

// The response of a surface to a single light. Both lobes come out already weighted by the clamped
// cosine between the normal and the light, and carry neither the light's colour nor its strength.
// Diffuse is Lambertian. Specular is a microfacet lobe: the Trowbridge-Reitz (GGX) normal distribution
// as Walter et al. 2007 state it, the height-correlated Smith visibility in the closed-form
// approximation Hammon gave at GDC 2017, and Schlick's 1994 Fresnel. Materials are described by
// metalness plus a specular level, following Karis 2013.
void EvaluateSurface(
    float3 baseColour,
    float metallic,
    float specular,
    float roughness,
    float3 n,
    float3 v,
    float3 l,
    out float3 diffuse,
    out float3 specularOut)
{
    // Metals tint their reflection with the base colour and scatter nothing diffusely. For everything
    // else the specular level maps linearly onto reflectance at normal incidence, so 0.5 gives 4%.
    float3 diffuseColour = baseColour * (1.0 - metallic);
    float3 f0 = lerp((0.08 * specular).xxx, baseColour, metallic);

    // Roughness never reaches zero, so a perfectly polished surface still has a lobe wide enough for a
    // light to fall into. Perceptual roughness is squared to get the distribution width.
    float perceptualRoughness = max(roughness, 0.02);
    float a = perceptualRoughness * perceptualRoughness;
    float aSquared = a * a;

    // Half vector between the view and light directions. When the two point exactly away from each
    // other their sum vanishes; the length is floored so h shrinks towards zero instead of dividing by
    // zero, which leaves both half-vector cosines at (or near) zero.
    float3 halfSum = v + l;
    float3 h = halfSum * rsqrt(max(dot(halfSum, halfSum), 1e-8));
    float cosNH = saturate(dot(n, h));
    float cosVH = saturate(dot(v, h));

    // Clamp the light and view cosines only after the half vector is built. The view cosine is nudged
    // off zero so silhouettes facing away from the camera still get a finite visibility term.
    float cosNL = saturate(dot(n, l));
    float cosNV = saturate(abs(dot(n, v)) + 1e-5);

    // Trowbridge-Reitz / GGX:  D = a^2 / (pi * ((n.h)^2 (a^2 - 1) + 1)^2)
    float ggxDenom = (cosNH * cosNH * (aSquared - 1.0)) + 1.0;
    float ndf = aSquared / (kPi * ggxDenom * ggxDenom);

    // Height-correlated Smith masking-shadowing folded with the 1 / (4 n.l n.v) microfacet factor:
    //   V = 0.5 / lerp(2 (n.l)(n.v), (n.l) + (n.v), a)
    float smithDenom = lerp(2.0 * cosNL * cosNV, cosNL + cosNV, a);
    float vis = 0.5 / max(smithDenom, 1e-6);

    // Schlick:  F = F0 + (F90 - F0) (1 - v.h)^5. F90 is not fixed at one: it scales with the green
    // channel of F0, so anything reflecting less than about 2% is read as cavity occlusion and loses
    // its grazing highlight.
    float f90 = saturate(50.0 * f0.g);
    float schlick = pow(1.0 - cosVH, 5.0);
    float3 fresnel = f0 + ((f90 - f0) * schlick);

    diffuse = diffuseColour * (cosNL / kPi);
    specularOut = ndf * vis * fresnel * cosNL;
}

// Analytic stand-in for the pre-integrated split-sum environment BRDF (Karis 2014, "Physically Based
// Shading on Mobile"). Returns the scale (x) and bias (y) applied to F0 for ambient specular, avoiding
// a lookup-table fetch.
float2 SplitSumApprox(float roughness, float cosNV)
{
    float smoothness = 1.0 - roughness;
    float grazing = min(smoothness * smoothness, exp2(-9.28 * cosNV));
    float t = (grazing * smoothness) + 0.0425 - (0.0275 * roughness);
    float scale = 1.04 - (0.572 * roughness) - (1.04 * t);
    float bias = (0.022 * roughness) - 0.04 + (1.04 * t);
    return float2(scale, bias);
}

float SpecularOcclusion(float noV, float alpha, float visibility)
{
    return clamp(pow(noV + visibility, alpha) - 1.0 + visibility, 0.0, 1.0);
}

// A surface does not absorb its own occlusion once: light bounces inside a crevice before it leaves, so a
// bright material stays brighter under occlusion than a dark one. Fit from Jimenez et al. 2016, GTAO
// multi-bounce.
float3 AoMultiBounce(float3 albedo, float visibility)
{
    float3 a = (2.0404 * albedo) - float3(0.3324, 0.3324, 0.3324);
    float3 b = float3(0.6417, 0.6417, 0.6417) - (4.7951 * albedo);
    float3 c = (2.7552 * albedo) + float3(0.6903, 0.6903, 0.6903);
    return max(float3(visibility, visibility, visibility), ((((visibility * a) + b) * visibility) + c) * visibility);
}

#endif
