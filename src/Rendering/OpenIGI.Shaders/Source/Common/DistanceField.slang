// The level's global distance field, as every pass that walks it without an acceleration structure reads
// it: one lookup, one visibility march, one surface march, and the gradient that gives a hit its normal.
//
// One copy, because a pass that marches a field differently from the pass that built it does not fail --
// it finds a surface a little way from where the surface is, and everything downstream of that is a
// little wrong everywhere.
//
// The host must declare before including: Texture3D<float4> uClipmaps with its sampler, and the macros
// uShadowLevels, uShadowResolution, uClipmapOrigin (an array of float4: each level's low corner and its
// voxel size in w) and uClipmapRange (an array of float: each level's encoded range).

#ifndef OPENIGI_DISTANCE_FIELD_INCLUDED
#define OPENIGI_DISTANCE_FIELD_INCLUDED

// The finest level that covers a point, and what it says the distance to the nearest surface is. Levels
// are searched from the finest outwards, and a point outside them all is unknown rather than far: the
// difference matters to every caller, because "no field here" and "nothing near here" lead to opposite
// answers.
float DistanceAt(float3 world, out bool known, out float raw, out float resolved)
{
    known = false;
    raw = 0.0;
    resolved = uClipmapOrigin[0].w;

    for (int level = 0; level < uShadowLevels; level++)
    {
        float4 corner = uClipmapOrigin[level];
        float3 voxel = (world - corner.xyz) / corner.w;
        if (any(voxel < float3(0.5, 0.5, 0.5))
            || any(voxel > float3(float(uShadowResolution) - 0.5,
                                  float(uShadowResolution) - 0.5,
                                  float(uShadowResolution) - 0.5)))
        {
            continue;
        }

        float slice = (float(level) * float(uShadowResolution)) + voxel.z;
        float3 uvw = float3(
            voxel.x / float(uShadowResolution),
            voxel.y / float(uShadowResolution),
            slice / float(uShadowResolution * uShadowLevels));

        known = true;
        raw = uClipmaps.SampleLevel(uClipmapsSampler, uvw, 0.0).r;
        resolved = corner.w;
        return ((raw * 2.0) - 1.0) * uClipmapRange[level];
    }

    return 1e6;
}

// Whether one step of a march has touched a surface, and where to report the touch. A sphere tracer
// (Hart 1996) that waits for the field to cross nought takes many tiny steps along a grazing surface and
// then reports a point already inside it. Instead a step counts as a hit once the field is within a small
// radius of the ray, and that radius grows with the distance travelled: nought at the start, so the
// surface the ray leaves cannot stop it, and half a voxel of the answering level once two voxels have
// been covered. The reported distance is pulled back by the same radius so a lookup made at the hit
// lands on the surface's skin rather than behind it.
bool FieldStepHits(float field, float travelled, float resolved, out float hitDistance)
{
    float radius = 0.5 * resolved * saturate(travelled / max(2.0 * resolved, 1e-6));
    hitDistance = max((travelled + field) - radius, 0.0);
    return field < radius;
}

// Whether anything stands between a surface and a direction. Sphere tracing, stepped by whatever the
// field says is safe and floored at a quarter of the voxel that answered so a grazing ray cannot crawl.
//
// Left out where the host has an acceleration structure and defines OPENIGI_TRACED_VISIBILITY: the traced
// answer takes this name, so that everything which gathers indirect light asks one question and never has
// to know which answer it is getting. See SoftwareTrace.slang.
#ifndef OPENIGI_TRACED_VISIBILITY
float VisibilityAt(float3 world, float3 normal, float3 direction, out float3 hit)
{
    hit = world;
    if (uShadowLevels <= 0)
    {
        return 1.0;
    }

    float finest = uClipmapOrigin[0].w;
    float3 at = world + (normal * finest * 1.5);
    float travelled = 0.0;
    float reach = uClipmapOrigin[0].w * float(uShadowResolution) * float(1 << (uShadowLevels - 1));

    for (int step = 0; step < 64 && travelled < reach; step++)
    {
        bool known;
        float raw;
        float resolved;
        float distance = DistanceAt(at + (direction * travelled), known, raw, resolved);
        if (!known)
        {
            return 1.0;
        }

        float hitDistance;
        if (FieldStepHits(distance, travelled, resolved, hitDistance))
        {
            hit = at + (direction * hitDistance);
            return 0.0;
        }

        travelled += max(distance, resolved * 0.25);
    }

    return 1.0;
}
#endif

// A couple of refinement steps once a crossing is bracketed, plus a quarter-voxel nudge past it so the
// hit sits on the surface rather than a hair in front of it.
float3 Settle(float3 at, float3 direction, float travelled, out float voxel)
{
    voxel = uClipmapOrigin[0].w;

    for (int pass = 0; pass < 2; pass++)
    {
        bool known;
        float raw;
        float resolved;
        float distance = DistanceAt(at + (direction * travelled), known, raw, resolved);
        if (!known)
        {
            break;
        }

        voxel = resolved;
        travelled += max(distance, 0.0);
    }

    return at + (direction * (travelled + (voxel * 0.25)));
}

// Where a ray meets a surface, which is a harder question than whether it meets one: a visibility march
// may stop anywhere inside the geometry, and a reflection needs the point it entered at. Two endings
// count -- the field going negative, which brackets a crossing to interpolate, and the distance turning
// upward from very near nought, which is a grazing ray that touched and is leaving again.
bool SurfaceAt(
    float3 world,
    float3 normal,
    float3 direction,
    float reach,
    out float3 hit,
    out float voxel)
{
    hit = world;
    voxel = uClipmapOrigin[0].w;
    if (uShadowLevels <= 0)
    {
        return false;
    }

    float finest = uClipmapOrigin[0].w;
    float3 at = world + (normal * finest * 1.5);
    float travelled = 0.0;
    float previous = 0.0;
    float previousDistance = 0.0;
    bool started = false;

    for (int step = 0; step < 96 && travelled < reach; step++)
    {
        bool known;
        float raw;
        float resolved;
        float distance = DistanceAt(at + (direction * travelled), known, raw, resolved);
        if (!known)
        {
            return false;
        }

        if (started)
        {
            if (distance <= 0.0)
            {
                float span = travelled - previous;
                float fall = previousDistance - distance;
                float crossing = fall > 1e-5
                    ? previous + (span * (previousDistance / fall))
                    : travelled;
                hit = Settle(at, direction, clamp(crossing, previous, travelled), voxel);
                return true;
            }

            if (distance > previousDistance && previousDistance < resolved * 0.05)
            {
                hit = Settle(at, direction, previous, voxel);
                return true;
            }
        }

        previous = travelled;
        previousDistance = distance;
        started = true;
        travelled += max(distance, resolved * 0.25);
    }

    return false;
}

// The field's gradient, which is the surface normal wherever the field is a distance. Central
// differences at the voxel that answered, and nought where the gradient is degenerate -- at a point the
// field knows nothing about, the differences are between two sentinels and mean nothing.
float3 FieldNormal(float3 world, float voxel)
{
    bool known;
    float raw;
    float resolved;
    float step = max(voxel, 1e-3);
    float3 gradient = float3(
        DistanceAt(world + float3(step, 0.0, 0.0), known, raw, resolved)
            - DistanceAt(world - float3(step, 0.0, 0.0), known, raw, resolved),
        DistanceAt(world + float3(0.0, step, 0.0), known, raw, resolved)
            - DistanceAt(world - float3(0.0, step, 0.0), known, raw, resolved),
        DistanceAt(world + float3(0.0, 0.0, step), known, raw, resolved)
            - DistanceAt(world - float3(0.0, 0.0, step), known, raw, resolved));

    float length2 = dot(gradient, gradient);
    return (length2 > 1e-12 && length2 < 1e6) ? gradient * rsqrt(length2) : float3(0.0, 0.0, 0.0);
}

#endif
