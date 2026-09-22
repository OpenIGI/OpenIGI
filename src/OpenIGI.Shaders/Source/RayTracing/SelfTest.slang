// A sweep of rays across a known quad, to prove an adapter's ray query actually traverses before the
// renderer commits to it.
//
// Everything about it is deliberately the shape the lighting passes will use rather than the simplest
// thing that traces: the structure lives in the bindless resource heap and is reached by index, so a
// structure rebuilt each frame needs no descriptor rebinding; and the results come back through a raw
// device address rather than a descriptor, which is the same mechanism a hit reads the geometry it struck
// through. A self-test that used neither would pass on an adapter the renderer then failed on.
//
// Compiled for SPIR-V alone: it traces, and WGSL has no ray tracing.

// The scene, by index into the bindless heap.
[[vk::binding(0, 1)]] RaytracingAccelerationStructure uScene[];

struct Push
{
    // Where to write the answers, as a device address. Writable, so a plain pointer rather than one of
    // the read-only buffer pointers the hit records are walked through.
    uint* results;

    uint sceneIndex;
    uint rayCount;
};

[[vk::push_constant]] ConstantBuffer<Push> pc;

[shader("compute")]
[numthreads(64, 1, 1)]
void computeMain(uint3 id : SV_DispatchThreadID)
{
    uint index = id.x;
    if (index >= pc.rayCount)
    {
        return;
    }

    // A sweep that runs off both edges of the quad, so the answer contains hits and misses and a
    // traversal that returned a constant would fail.
    float t = float(index) / float(pc.rayCount - 1u);

    RayDesc ray;
    ray.Origin = float3(-0.5 + (2.0 * t), 0.5, -1.0);
    ray.TMin = 0.0;
    ray.Direction = float3(0.0, 0.0, 1.0);
    ray.TMax = 100.0;

    RayQuery<RAY_FLAG_FORCE_OPAQUE> query;
    query.TraceRayInline(
        uScene[NonUniformResourceIndex(pc.sceneIndex)], RAY_FLAG_NONE, 0xFF, ray);

    while (query.Proceed())
    {
    }

    uint result = 0u;
    if (query.CommittedStatus() != COMMITTED_NOTHING)
    {
        // Zero means a miss, so the distance is reported in thousandths and a genuine hit at the origin
        // could never be confused with one.
        result = uint((query.CommittedRayT() * 1000.0) + 0.5);
    }

    pc.results[index] = result;
}
