// A picture in linear light laid into the back buffer texel for texel.
//
// The direct path draws straight into the back buffer and keeps no picture of its own, so a pass that works over its
// finished frame -- a bomb's after-blast -- reads a copy of it and leaves what it made in an image of its own. This puts
// that image back where the frame was, at the display encoding the back buffer holds.
//
// Fetched rather than sampled: the two are the same size, and a filter here could only blur.

#include "../Common/Fullscreen.slang"
#include "../Common/TargetEncoding.slang"

[[vk::binding(0, 0)]] Texture2D<float4> uPicture;

typedef FullscreenVaryings Varyings;

[shader("vertex")]
Varyings vertexMain(uint index : SV_VertexID)
{
    return FullscreenTriangle(index);
}

[shader("fragment")]
float4 fragmentMain(Varyings input) : SV_Target
{
    float3 picture = uPicture.Load(int3(int2(input.position.xy), 0)).rgb;
    return ToTarget(float4(EncodeSrgb(saturate(picture)), 1.0));
}
