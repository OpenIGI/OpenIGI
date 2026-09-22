// Adds the reflection back into the lit scene, in place.
//
// Its own dispatch, because the reflection was read from the lit scene and is now written into it: one
// pixel's reflection is another pixel's source, so the read and the write cannot share a pass. A pixel is
// only ever added to itself here, which is the one access pattern that is safe to do to an image in
// place.
//
// Compiled for SPIR-V alone. The scene image is read and written by the same dispatch, and WGSL does not
// allow read_write access to an rgba16float storage texture at all -- a WebGPU form has to ping-pong
// between two images, which is a change to what the host allocates and binds rather than to this shader,
// so it belongs with the web backend rather than ahead of it.

[[vk::binding(0, 0)]] [[vk::image_format("rgba16f")]] RWTexture2D<float4> uReflection;
[[vk::binding(1, 0)]] [[vk::image_format("rgba16f")]] RWTexture2D<float4> uScene;

struct Params
{
    // width, height, whether to replace the scene rather than add to it, _.
    float4 resolution;
};

[[vk::binding(2, 0)]] ConstantBuffer<Params> p;

[shader("compute")]
[numthreads(8, 8, 1)]
void computeMain(uint3 id : SV_DispatchThreadID)
{
    int2 px = int2(id.xy);
    if (px.x >= int(p.resolution.x) || px.y >= int(p.resolution.y))
    {
        return;
    }

    float3 reflection = uReflection.Load(px).rgb;

    // The reflection on its own, for the debug view that isolates it. Replacing rather than adding is the
    // whole of what that view is: what a surface reflects is otherwise a small addition to a lit picture,
    // and a term you cannot see on its own is a term you cannot tell is missing.
    if (p.resolution.z > 0.5)
    {
        uScene[px] = float4(max(reflection, float3(0.0, 0.0, 0.0)), 1.0);
        return;
    }

    float3 scene = uScene[px].rgb;

    // Clamped at nothing, because a water pixel's reflection is SIGNED: it carries the sky the shading
    // laid in that pixel back out again so the marched scene can stand in its place. The two cancel to
    // within the fog and aerial perspective the lit colour has been through and this has not, which is a
    // rounding error at the few tens of metres a marched ray reaches -- but a rounding error that went
    // negative would be a black speck, and this costs one instruction.
    uScene[px] = float4(max(scene + reflection, float3(0.0, 0.0, 0.0)), 1.0);
}
