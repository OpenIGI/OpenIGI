// Screen motion, derived by reprojecting each pixel's depth through the previous frame's camera.

#include "../Common/DepthSlot.slang"
#include "../Common/ViewDepth.slang"

OPENIGI_DEPTH_SLOT(0) uDepth;
[[vk::binding(1, 0)]] SamplerState uDepthSampler;

[[vk::binding(2, 0)]] [[vk::image_format("rg16f")]] WTexture2D<float4> uVelocity;

struct Params
{
    // this frame's view rotation, camera-relative.
    float4x4 viewToWorld;

    // the previous frame's inverse view rotation.
    float4x4 prevWorldToView;

    // xScale, yScale, near, far.
    float4 unproject;

    // width, height, 1/width, 1/height.
    float4 resolution;

    // this frame's camera minus the previous frame's, w unused.
    float4 cameraDelta;

    // xScale, yScale, near, far of the previous unjittered projection.
    float4 prevProjection;

    // current raster jitter in input pixels, xy; zw unused.
    float4 jitter;
};

[[vk::binding(3, 0)]] ConstantBuffer<Params> p;

[shader("compute")]
[numthreads(8, 8, 1)]
void computeMain(uint3 id : SV_DispatchThreadID)
{
    int2 px = int2(id.xy);
    int2 size = int2(p.resolution.xy);
    if (px.x >= size.x || px.y >= size.y)
    {
        return;
    }

    float2 rasterUv = (float2(px) + 0.5) * p.resolution.zw;
    float depth = OPENIGI_DEPTH_SAMPLE(uDepth, uDepthSampler, rasterUv);

    // Reverse-Z clears to zero, so a pixel the geometry never covered reads exactly zero. That is the
    // sky, and the sky is not motionless: it sits at infinity, so it turns with the camera exactly as
    // the ground does and only the camera's TRANSLATION leaves it where it was. It used to be reported
    // as still, and then every pixel the sky had just been revealed at -- behind a strut, a wire, a
    // soldier against the horizon -- read its history from the same place rather than from where that
    // sky came from, and kept a copy of whatever thin thing had stood there: a ghost one frame behind,
    // on every pan. Reprojecting the pixel's view direction alone, with no parallax, is the sky's true
    // motion.
    if (depth <= 0.0)
    {
        float2 skyUv = rasterUv - (p.jitter.xy * p.resolution.zw);
        float2 ndc = (skyUv * 2.0) - 1.0;
        float3 direction = float3(ndc.x / p.unproject.x, -ndc.y / p.unproject.y, 1.0);
        float3 worldDirection = mul(p.viewToWorld, float4(direction, 0.0)).xyz;
        float3 prevDirection = mul(p.prevWorldToView, float4(worldDirection, 0.0)).xyz;
        if (prevDirection.z <= 1e-4)
        {
            uVelocity.Store(px, float4(0.0, 0.0, 0.0, 0.0));
            return;
        }

        float2 prevSkyNdc = float2(
            prevDirection.x * p.prevProjection.x / prevDirection.z,
            -prevDirection.y * p.prevProjection.y / prevDirection.z);
        uVelocity.Store(px, float4(skyUv - ((prevSkyNdc * 0.5) + 0.5), 0.0, 0.0));
        return;
    }

    // The first-person rig lives in the foreground depth sliver (reverse-Z 0.9..1, nearer than any world
    // surface can be) and is locked to the camera: it does not move on screen when the camera turns.
    // This pass derives motion from depth reprojection, which knows nothing of that rigging -- it read
    // the rig's near depth as world geometry a hand's length away and handed it an enormous velocity
    // every time the view rotated. The motion blur then smeared that velocity across the whole weapon,
    // and the temporal pass chased the same false motion into a ghosting weapon. Its true screen motion
    // is very nearly zero, which is both what stills the blur and what lets the temporal pass hold the
    // weapon steady.
    if (depth >= 0.9)
    {
        uVelocity.Store(px, float4(0.0, 0.0, 0.0, 0.0));
        return;
    }

    // The depth belongs to the ray displaced by this frame's projection jitter. Reconstruct that surface
    // through its UNJITTERED screen position, just as the reference's no-AA clip transform does. Using
    // rasterUv here happens to cancel for a held camera and for much of a rotation, but it reconstructs
    // the wrong world point under translation; the parallax error then grows with walking speed and
    // makes every history lookup land between the right samples.
    float2 currentUv = rasterUv - (p.jitter.xy * p.resolution.zw);
    float3 viewPos = ViewPositionAt(currentUv, depth, p.unproject);

    // Camera-relative throughout: the world position is never formed, only the offset from this frame's
    // camera, and then the offset from the previous one.
    float3 relativeToCamera = mul(p.viewToWorld, float4(viewPos, 0.0)).xyz;
    float3 relativeToPrevCamera = relativeToCamera + p.cameraDelta.xyz;
    float3 prevView = mul(p.prevWorldToView, float4(relativeToPrevCamera, 0.0)).xyz;

    // Behind the previous camera, so it had no screen position at all -- nothing to reproject onto.
    if (prevView.z <= p.prevProjection.z)
    {
        uVelocity.Store(px, float4(0.0, 0.0, 0.0, 0.0));
        return;
    }

    // The projection applied by hand rather than as a matrix, from the same four numbers the
    // reconstruction above used, so the two are exact inverses of one another.
    float2 prevNdc = float2(
        prevView.x * p.prevProjection.x / prevView.z,
        -prevView.y * p.prevProjection.y / prevView.z);
    float2 prevUv = (prevNdc * 0.5) + 0.5;

    // Motion is always unjittered-current minus unjittered-previous. Consumers that read a raw frame
    // texture add that frame's jitter themselves; the super-sampled history is already in unjittered
    // output space and uses this vector directly.
    uVelocity.Store(px, float4(currentUv - prevUv, 0.0, 0.0));
}
