// The finished picture, as the mods' passes over it left it, put where the tone map would have drawn it.
//
// While a mod runs a pass over the finished picture, the tone map draws into an image of its own rather than the back buffer;
// the passes read that and write their own, and this lays the last of them where the picture was going. The image holds the
// display encoding, as the tone map wrote it, so a target that encodes as it stores is handed the colour decoded.
//
// Fetched texel for texel rather than sampled: the images are the screen's size, and a filter here could only blur.

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
    return ToTarget(float4(uPicture.Load(int3(int2(input.position.xy), 0)).rgb, 1.0));
}
