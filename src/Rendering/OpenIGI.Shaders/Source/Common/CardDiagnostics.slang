// The surface cache lighting's diagnostics, written in place of light when the dump asks for one: the trace's
// own post-mortem, the card index sending a texel back to itself, the reconstructed position and normal, and
// what the distance field reads at the trace's start.
//
// The host must include this after declaring the cache's Globals (uProbe, the clipmaps, the sun's direction
// and the position probe's box) and the card albedo as uAlbedo, and after SoftwareTrace.slang and
// SurfaceCache.slang.

#ifndef OPENIGI_CARD_DIAGNOSTICS_INCLUDED
#define OPENIGI_CARD_DIAGNOSTICS_INCLUDED

// The trace's own post-mortem: how the ray ended (red: hit zero, expired half, escaped one), the voxel
// size of the level that had the last word (green, against the coarsest), and how far it got (blue,
// against its reach). An over-shadowed atlas with green high says the coarse levels are calling hits the
// fine ones would not.
float4 ProbedTrace(float3 world, float3 normal)
{
    float finest = uClipmapOrigin[0].w;
    float coarsest = uClipmapOrigin[max(uShadowLevels - 1, 0)].w;
    float3 at = world + (normal * finest * 1.5);

    float travelled = 0.0;
    float reach = finest * float(uShadowResolution) * float(1 << (uShadowLevels - 1));

    for (int step = 0; step < 64 && travelled < reach; step++)
    {
        bool known;
        float raw;
        float resolved;
        float distance = DistanceAt(at + (uSunDirection * travelled), known, raw, resolved);

        if (!known)
        {
            return float4(1.0, 0.0, travelled / reach, 1.0);
        }

        // The same touch test as the visibility march, so the probe shows what the bounce sees.
        float touchedAt;
        if (FieldStepHits(distance, travelled, resolved, touchedAt))
        {
            return float4(0.0, resolved / coarsest, travelled / reach, 1.0);
        }

        travelled += max(distance, resolved * 0.25);
    }

    return float4(0.5, 0.0, clamp(travelled / reach, 0.0, 1.0), 1.0);
}

// A diagnostic in place of light. Every fault in the shadow trace presents as the same black atlas; each
// probe writes one of the trace's own intermediates so the dump can tell a wrong position from a field it
// cannot read.
float4 Probed(float3 world, float3 normal, float2 cardUv, int2 texel)
{
    // Every texel looks up its OWN position and is told whether the index sent it back to itself. Green
    // found itself, yellow found a different surface, red found nothing.
    //
    // Comparing card UVs rather than the colours behind them, because the card atlas and the lighting
    // atlas have different layouts -- the same surface is at different coordinates in each -- so a colour
    // comparison would be measuring the packing, not the index.
    if (uProbe == 5)
    {
        // One texel in sixty-four. The index is walked per candidate per texel, and a crowded cell holds
        // nearly two hundred pages, so testing every texel of a million-texel atlas outruns the frame the
        // pass is dispatched in and the dump never happens -- which reads exactly like the probe finding
        // nothing. A sample answers the same question: whether the index sends a point back to the
        // surface it came from.
        if (((texel.x | texel.y) & 7) != 0)
        {
            return float4(0.0, 0.0, 0.0, 0.0);
        }

        float2 found;
        float2 litFound;
        float agreement;

        if (!LookupCard(world, found, litFound, agreement))
        {
            return float4(1.0, 0.0, 0.0, 1.0);
        }

        uint albedoWidth;
        uint albedoHeight;
        uAlbedo.GetDimensions(albedoWidth, albedoHeight);
        float2 apart = abs(found - cardUv) * float2(float(albedoWidth), float(albedoHeight));

        return max(apart.x, apart.y) < 1.5
            ? float4(0.0, 1.0, 0.0, 1.0)
            : float4(1.0, 1.0, 0.0, 1.0);
    }

    if (uProbe == 4)
    {
        return ProbedTrace(world, normal);
    }

    // Where the texel believes it stands, mapped through the outermost clipmap's box. A correct
    // reconstruction fills the interior; one in the wrong space clamps to the walls.
    if (uProbe == 2)
    {
        return float4(
            clamp(
                (world - uProbeMin) * uProbeInvExtent,
                float3(0.0, 0.0, 0.0),
                float3(1.0, 1.0, 1.0)),
            1.0);
    }

    if (uProbe == 3)
    {
        return float4((normal * 0.5) + 0.5, 1.0);
    }

    // What the trace sees at its own start point, one question per channel: did the range parameters
    // arrive (red is exactly one when they did); what byte the texture actually returned (green, half
    // meaning a surface at zero); the decoded clearance against four finest voxels (blue); and whether
    // any level contained the point at all (alpha, half meaning none did).
    float finest = uClipmapOrigin[0].w;
    float3 at = world + (normal * finest * 1.5);

    bool known;
    float raw;
    float resolved;
    float start = DistanceAt(at, known, raw, resolved);

    return float4(
        uShadowLevels > 0 ? clamp(uClipmapRange[0] / (finest * 4.0), 0.0, 1.0) : 0.0,
        raw,
        known ? clamp(start / (finest * 4.0), 0.0, 1.0) : 1.0,
        known ? 1.0 : 0.5);
}

#endif
