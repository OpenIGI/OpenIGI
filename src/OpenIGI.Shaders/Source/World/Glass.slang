// The glazing transmission draw: what a pane lets through, tinted by the pane and fogged by the air.
//
// No ray query, no acceleration structure and no surface cache: this carried all three once and they
// have moved to the reflection pass, where the lit frame is readable. What is left needs nothing the
// world pipeline does not already bind.

#include "../Common/GlassVertex.slang"

/// The vertex stage: the shared glazing transform.
[shader("vertex")]
GlassVaryings vertexMain(GlassVertexInput input)
{
    return GlassTransform(input);
}

float3 DecodeSrgb(float3 c)
{
    return lerp(c / 12.92, pow((c + 0.055) / 1.055, float3(2.4, 2.4, 2.4)), step(0.04045, c));
}

/// The fragment stage: the transmission the room behind is seen through.
[shader("fragment")]
float4 fragmentMain(GlassVaryings input) : SV_Target
{
    float4 diffuse = uDiffuse.Sample(uDiffuseSampler, input.diffuseUv);
    float alpha = diffuse.a * input.color.a;

    // THE INVERSE of the geometry pass's cut, and it must stay the exact inverse. That pass keeps a
    // glass texture's opaque texels -- the frame, the mullions, the putty -- and lights them properly
    // in the G-buffer; this keeps the ones it dropped. Written as the same expression on both sides so
    // the two partition the texture between them: a texel drawn by neither is a hole in a window
    // frame, and one drawn by both is a frame shaded twice, once without shadows.
    if (alpha >= 0.5)
    {
        discard;
    }

    // NOTHING IS RECORDED HERE, and that is the point of the front-layer pass.
    //
    // This used to write the pane's normal and distance into a layer with an image store, from inside
    // the translucency subpass. Blending is ordered by the rasteriser; an image store is not ordered
    // at all, between triangles or between draws. So wherever two panes covered one pixel -- a window
    // seen through a window, which a building glazed on two faces is full of -- whichever fragment
    // happened to land last won, and it was not the same one from frame to frame. The reflection
    // fizzed with per-pixel noise. The layer is written by a pass that owns a depth buffer now.

    // What the pane itself contributes: its own colour, lit by whatever the level baked onto it.
    // Glass is nearly all transmission, so this is a tint on the room behind rather than a surface
    // colour, and the blend below is what applies it.
    float3 tint = DecodeSrgb(diffuse.rgb) * DecodeSrgb(input.color.rgb)
        * DecodeSrgb(uLightmap.Sample(uLightmapSampler, input.lightmapUv).rgb);

    // The frame's fog, over the pane's own distance. Glazing is ordinary world geometry as far as the
    // atmosphere is concerned -- a window across a courtyard has as much air in front of it as the
    // wall it is set into, and leaving it out makes distant panes read as nearer than their own
    // frames.
    float fog = exp(-p.fog.w * max(input.viewDepth, 0.0));

    // Transmission only, at the texture's own alpha. The reflection is added later and takes its own
    // share of the pixel then; weighting this by a Fresnel term as well would have the two passes
    // dividing one pixel between them by a number each had computed separately.
    return float4(lerp(p.fog.rgb * alpha, tint * alpha, fog), alpha);
}
