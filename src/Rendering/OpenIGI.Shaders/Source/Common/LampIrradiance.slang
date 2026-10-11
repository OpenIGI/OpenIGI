// What one lamp brings to a surface before anything stands in its way: whether it reaches, its fitting's cone,
// a point lamp's irradiance, a tube's irradiance integrated along its length, and a cheap bound on a tube's
// light for choosing lamps by.
//
// Shared by the lighting pass, which adds the light, and the lamp trace, which weighs every lamp by it to choose
// which to shadow and to decide whether a pixel has any lamp light to shadow at all. The two used to answer
// apart: the trace weighed a tube by its centre alone, so a wall facing only part of a batten -- one on the
// soffit of mission 1's security-building canopy, its middle just behind the wall -- was "reached by no lamp"
// there, while the shading integrated the part in front of it and lit the wall. A pixel no lamp reaches is never
// traced, so that light went through the canopy unshadowed, as a bright band. With the reach, the cone and the
// point term shared, and the trace's tube bound nought exactly where the shading's tube light is, the trace
// shadows every light the shading adds.
//
// Small steps rather than one function that answers everything, and none of them takes a matrix: both callers
// run these for every lamp at every pixel, and a structure of every term, or the view-to-world matrix passed by
// value, held registers across the whole lamp loop -- milliseconds a frame between the two passes, for occupancy
// the arithmetic itself never needed.

#ifndef OPENIGI_LAMP_IRRADIANCE_INCLUDED
#define OPENIGI_LAMP_IRRADIANCE_INCLUDED

// The irradiance a uniform straight emitter puts on a surface, per unit of the emitter's whole intensity:
// the integral along it of n.L / |L|^3, where L runs from the surface to each point of the emitter, from
// L0 at one end to L0 + D at the other. Closed form, since |L|^2 is a quadratic in the parameter t:
//
//   integral dt / Q^(3/2)   = (a t + b) / (det sqrt(Q))
//   integral t dt / Q^(3/2) = -(b t + c) / (det sqrt(Q))
//
// with Q = a t^2 + 2 b t + c, a = D.D, b = L0.D, c = L0.L0 and det = a c - b^2. The part of the emitter
// below the surface's horizon gives it nothing, so the interval is first cut to where n.L is positive.
// A surface the emitter lies along (det near nought) takes it as one point at the middle of what is left.
float LineIrradiance(float3 n, float3 lineStart, float3 lineDelta)
{
    float startFacing = dot(n, lineStart);
    float deltaFacing = dot(n, lineDelta);
    float t0 = 0.0;
    float t1 = 1.0;
    if (abs(deltaFacing) > 1e-6)
    {
        float crossing = -startFacing / deltaFacing;
        if (deltaFacing > 0.0)
        {
            t0 = max(t0, crossing);
        }
        else
        {
            t1 = min(t1, crossing);
        }
    }
    else if (startFacing <= 0.0)
    {
        return 0.0;
    }

    if (t1 <= t0)
    {
        return 0.0;
    }

    float a = dot(lineDelta, lineDelta);
    float b = dot(lineStart, lineDelta);
    float c = dot(lineStart, lineStart);
    float det = (a * c) - (b * b);
    if (det <= 1e-5 * a * c)
    {
        float3 middle = lineStart + (lineDelta * (0.5 * (t0 + t1)));
        float distanceSq = max(dot(middle, middle), 1e-4);
        return saturate(dot(n, middle) * rsqrt(distanceSq)) / distanceSq * (t1 - t0);
    }

    float q0 = sqrt(max((a * t0 * t0) + (2.0 * b * t0) + c, 1e-8));
    float q1 = sqrt(max((a * t1 * t1) + (2.0 * b * t1) + c, 1e-8));
    float at1 = ((startFacing * ((a * t1) + b)) - (deltaFacing * ((b * t1) + c))) / (det * q1);
    float at0 = ((startFacing * ((a * t0) + b)) - (deltaFacing * ((b * t0) + c))) / (det * q0);
    return max(at1 - at0, 0.0);
}

/// The reach window: one at the lamp, falling to exactly nought at its reach, and nought beyond.
/// Takes no matrix, and no shared function here does: handed the view-to-world matrix by value, a caller's lamp
/// loop held all sixteen of its floats in registers for the whole loop, where reading the constant buffer at the
/// call site loads only what is used -- and the occupancy that cost was half a millisecond of the lighting pass
/// and as much of the lamp trace. Each caller turns its vectors into the world itself.
float LampReachWindow(float distanceSq, float reach)
{
    if (distanceSq >= reach * reach)
    {
        return 0.0;
    }

    float ratio = distanceSq / max(reach * reach, 1e-4);
    float window = clamp(1.0 - (ratio * ratio), 0.0, 1.0);
    return window * window;
}

/// The fitting's cone: how squarely it faces back down the way to it. axis is the fitting's facing in world
/// space, unnormalised, and centreDir the unit direction from the surface to its centre.
float LampCone(float3 axis, float3 centreDir)
{
    return max(dot(normalize(axis), -centreDir), 0.0);
}

/// Whether a lamp's direction entry says it faces a way at all.
bool LampFaces(float4 direction)
{
    return dot(direction.xyz, direction.xyz) > 0.25;
}

/// A point lamp's diffuse irradiance per unit of its intensity: the inverse square, softened inside its fitting.
float LampPointIrradiance(float3 normal, float3 centreDir, float distanceSq, float source)
{
    return saturate(dot(normal, centreDir)) / max(distanceSq, source * source);
}

/// A cheap bound on a tube's light at a surface, per unit of its intensity, for choosing lamps rather than
/// lighting with them: the largest of the point irradiances at its two ends and its middle, softened inside its
/// thickness. Nought exactly where the tube's diffuse light and highlight are -- when the whole tube lies behind
/// the surface, which a straight emitter does when both its ends do -- so a lamp the shading lights a surface
/// with is always counted as reaching it; otherwise a magnitude in the same units, at a third of the cost of
/// the integral and the highlight's point. The lamp trace weighs every lamp in reach of every pixel, and the
/// exact terms cost it two milliseconds a frame where the shading's choice of lamp needed none of their precision.
float LampTubeBound(float3 normal, float3 toCentre, float3 halfAxis, float thickness)
{
    float3 start = toCentre - halfAxis;
    float3 end = toCentre + halfAxis;
    float startSq = max(dot(start, start), 1e-8);
    float endSq = max(dot(end, end), 1e-8);
    float centreSq = max(dot(toCentre, toCentre), 1e-8);
    float startFacing = dot(normal, start) * rsqrt(startSq);
    float endFacing = dot(normal, end) * rsqrt(endSq);
    if (max(startFacing, endFacing) <= 0.0)
    {
        return 0.0;
    }

    float soft = thickness * thickness;
    float centreFacing = dot(normal, toCentre) * rsqrt(centreSq);
    return max(
        max(saturate(startFacing) / max(startSq, soft), saturate(endFacing) / max(endSq, soft)),
        saturate(centreFacing) / max(centreSq, soft));
}

/// The fitting's size, which softens a point lamp's inverse square inside it.
float LampSource(float4 direction)
{
    return clamp(direction.w, 0.05, 1.0);
}

#endif
