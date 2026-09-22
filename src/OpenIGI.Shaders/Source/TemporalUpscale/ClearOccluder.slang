// The closest-occluder scatter, emptied for this frame's dilation.
//
// The dilation builds it with an atomic minimum, whose identity is the largest value there is: a texel
// nothing reprojected onto has to read as "nothing in front", not as whatever last frame left. Not
// cleared, every frame's minimum takes in every frame before it, and the decimation that reads it finds
// an occluder in front of every history texel and rejects the whole of it -- the resolve then passes
// the raw frame through and looks like it is working. The Vulkan original clears it with a transfer;
// WebGPU has no command that clears a texture, so this is a pass.

[[vk::binding(0, 0)]] [[vk::image_format("r32ui")]] RWTexture2D<uint> uOccluder;

[shader("compute")]
[numthreads(8, 8, 1)]
void computeMain(uint3 id : SV_DispatchThreadID)
{
    uint width;
    uint height;
    uOccluder.GetDimensions(width, height);

    if (id.x >= width || id.y >= height)
    {
        return;
    }

    uOccluder[id.xy] = 0xFFFFFFFFu;
}
