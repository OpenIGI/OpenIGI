// This frame's depth, kept for the next one.
//
// The occlusion's history test and the temporal resolve's history decimation both ask where a surface
// stood LAST frame. The Vulkan original keeps a whole second set of scene targets for that; this path
// keeps one set, and so copies the one field that is read back into a target of its own. A copy rather
// than a texture-to-texture transfer because a depth image cannot be copied into a colour one, and the
// storage it lands in has to be a colour format for the passes that read it as a plain float.

[[vk::binding(0, 0)]] Texture2D<float4> uDepth;

[[vk::binding(1, 0)]] [[vk::image_format("r32f")]] WTexture2D<float4> uPreviousDepth;

[shader("compute")]
[numthreads(8, 8, 1)]
void computeMain(uint3 id : SV_DispatchThreadID)
{
    uint width;
    uint height;
    uPreviousDepth.GetDimensions(width, height);

    if (id.x >= width || id.y >= height)
    {
        return;
    }

    uPreviousDepth.Store(int2(id.xy), float4(uDepth.Load(int3(id.xy, 0)).r, 0.0, 0.0, 0.0));
}
