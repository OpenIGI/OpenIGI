// The glazing front-layer draw: which pane is nearest, and which way it faces.
//
// Shares the vertex stage and the descriptor sets with the transmission draw, so a pane goes through
// the same alpha cut and arrives with the same world normal. What it does not share is the target:
// this writes a colour attachment in a pass that owns a DEPTH BUFFER, so where two panes cover one
// pixel the depth test picks one of them and the answer is the same every frame.
//
// It has to reject panes the camera cannot see, which the transmission draw gets for free by testing
// against the scene depth. This pass tests against a depth buffer of its own -- that is the whole
// reason it exists -- so the occlusion question is asked here in the shader instead. Without it a
// window deep inside a building would record a layer through the wall in front of it, and the
// reflection pass would paint a window reflection onto that wall.

#include "../Common/GlassVertex.slang"

/// The vertex stage: the shared glazing transform.
[shader("vertex")]
GlassVaryings vertexMain(GlassVertexInput input)
{
    return GlassTransform(input);
}

/// The fragment stage: the nearest pane's world normal and its distance.
[shader("fragment")]
float4 fragmentMain(GlassVaryings input) : SV_Target
{
    float alpha = uDiffuse.Sample(uDiffuseSampler, input.diffuseUv).a * input.color.a;

    // The same cut the transmission draw makes, so the two agree about what a pane is.
    if (alpha >= 0.5)
    {
        discard;
    }

    // Behind something the camera already drew, so it is the front layer of nothing. REVERSE-Z, so a
    // nearer surface holds the GREATER value and a pane loses when its own is smaller. Fetched by
    // texel rather than sampled: a depth is a value at a pixel, and interpolating two of them across a
    // silhouette invents a surface that is at neither distance.
    float scene = uSceneDepth.Load(int3(int2(input.position.xy), 0));
    if (input.position.z < scene)
    {
        discard;
    }

    // The alpha carries the view depth AND doubles as the mask -- a pane is always at a positive
    // distance, so nought means no pane covered this pixel, which is what the clear leaves.
    //
    // The normal goes over as the geometry authored it. Making it face the eye is the REFLECTION
    // pass's job: a pane is a single sheet with no thickness and a player walks around it, so which
    // way it should face depends on where the camera is -- and that pass has already rebuilt the
    // surface position to answer exactly that.
    return float4(normalize(input.worldNormal), max(input.viewDepth, 1e-4));
}
