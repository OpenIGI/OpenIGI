// Where this frame's history should not be trusted, as one channel an NVIDIA reconstruction reads.
//
// Particles, tracers, muzzle flashes, explosions and rain are drawn over the lit image after the motion
// field was made, and none of them writes motion of its own: a reconstruction that believed the field
// would drag each one along with whatever is behind it and leave a trail. They mark themselves instead,
// as they are drawn, by pushing the lit image's alpha above one -- the same marker the engine's own
// resolve reads to stop accumulating them. This turns that marker into the mask.
//
// One at a full additive marker, nothing where the scene is opaque or unlit; the excess above one is
// scaled so a faint spark biases a little and a muzzle flash biases fully.

[[vk::binding(0, 0)]] Texture2D<float4> uLit;

[[vk::binding(1, 0)]] [[vk::image_format("r32f")]] WTexture2D<float4> uMask;

[shader("compute")]
[numthreads(8, 8, 1)]
void computeMain(uint3 id : SV_DispatchThreadID)
{
    uint width;
    uint height;
    uMask.GetDimensions(width, height);

    if (id.x >= width || id.y >= height)
    {
        return;
    }

    float marker = uLit.Load(int3(id.xy, 0)).a;
    float reactive = saturate((marker - 1.0) * 4.0);
    uMask.Store(int2(id.xy), float4(reactive, 0.0, 0.0, 0.0));
}
