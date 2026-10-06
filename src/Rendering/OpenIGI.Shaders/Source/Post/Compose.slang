// The interface laid over the finished picture, when the two were drawn apart.
//
// Ordinarily the interface is drawn straight over the resolved picture in the back buffer. Two of NVIDIA's
// features need them apart instead: frame generation interpolates the picture WITHOUT the interface and the
// interface separately, and Dynamic Vibrance must change the picture and leave the interface alone. So the
// picture is resolved into an image of its own, the interface is drawn into a transparent layer, and this
// puts the two together in the back buffer.
//
// The layer holds premultiplied colour and coverage: every sprite blends into it over transparent black,
// which is exactly the over operator accumulated, so laying it over the picture gives the same pixel the
// direct path would have drawn -- ui.rgb + (1 - ui.a) * picture -- and that is also the blend frame
// generation's own recomposition assumes.
//
// Both are fetched texel for texel rather than sampled: the three images are the same size, and a filter
// here could only blur.

#include "../Common/Fullscreen.slang"
#include "../Common/TargetEncoding.slang"

[[vk::binding(0, 0)]] Texture2D<float4> uDisplay;

[[vk::binding(1, 0)]] Texture2D<float4> uInterface;

typedef FullscreenVaryings Varyings;

[shader("vertex")]
Varyings vertexMain(uint index : SV_VertexID)
{
    return FullscreenTriangle(index);
}

[shader("fragment")]
float4 fragmentMain(Varyings input) : SV_Target
{
    int3 texel = int3(int2(input.position.xy), 0);
    float3 picture = uDisplay.Load(texel).rgb;
    float4 overlay = uInterface.Load(texel);
    return ToTarget(float4(overlay.rgb + ((1.0 - overlay.a) * picture), 1.0));
}
