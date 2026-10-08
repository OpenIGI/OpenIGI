// The glazing transmission draw: how much of the scene behind a clear pane lets through.
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

/// The fragment stage: the share of the room behind that the pane lets through.
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

    if (alpha < GlassHoleAlpha)
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

    // CLEAR GLASS ADDS NO LIGHT OF ITS OWN. The texture's colour is the original's painted stand-in for
    // a reflection -- grey-white streaks -- and it was drawn here at a quarter strength, lit only by the
    // lightmap: full bright on anything without one, so a truck's windscreen glowed in a dark garage and
    // every pane looked the same by day and by night. The reflection pass now supplies the real
    // reflection, lit by the scene, so this draw only decides how much of the room behind gets through.
    //
    // That is the pane's transmittance, which depends on the angle it is seen at and nothing else: what
    // the two faces do not reflect (GlassPaneReflectance), less a little absorbed along the path through
    // the sheet, which lengthens as the refracted ray leans (Snell, n = 1.5). Head-on that lets through
    // about ninety per cent; at a glancing angle the pane closes into the mirror the reflection paints.
    float3 normalW = normalize(input.worldNormal);
    float cosine = abs(dot(normalW, normalize(input.fromEye)));
    float sinRefracted = sqrt(saturate(1.0 - (cosine * cosine))) / 1.5;
    float cosRefracted = sqrt(saturate(1.0 - (sinRefracted * sinRefracted)));
    float through = (1.0 - GlassPaneReflectance(cosine)) * exp(-0.02 / max(cosRefracted, 0.05));

    // Premultiplied over the scene: nothing added, the scene behind kept at the share that gets through.
    // The scene there is already lit and fogged for its own distance, so the pane needs neither.
    return float4(0.0, 0.0, 0.0, 1.0 - through);
}
