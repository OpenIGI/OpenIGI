namespace OpenIGI.Rendering;

/// <summary>
/// The level's global distance field, as the GLSL every pass that marches it shares.
/// </summary>
/// <remarks>
/// <para>
/// The software answer to "what does this ray hit". Where there is no ray-tracing hardware there is
/// still a signed distance to the nearest surface, resampled into a stack of clipmaps around the camera,
/// and a ray can be walked through it by stepping exactly as far as the nearest surface — which by
/// definition cannot pass through one. It is the approximation a machine without a structure to trace
/// gets, and it is a good one: a ray crossing open ground clears it in a handful of steps however much
/// geometry stands nearby.
/// </para>
/// <para>
/// <b>This lives here, below the renderer seam, for the reason <see cref="SurfaceCacheShaders"/> does:
/// there must be exactly one copy.</b> It began in the engine, beside the passes that used it, which was
/// right while those passes were all the engine's own. A backend-owned pass cannot see them — a graphics
/// backend does not reference the game — so a second copy would have had to be kept in step by hand, and
/// two marches that disagreed by a step size or a hit threshold would not look like a bug. They would
/// look like the window and the wall beside it disagreeing about where the room is.
/// </para>
/// <para>
/// <b>A snippet, not a shader.</b> It declares no version, no bindings and no uniforms: every function
/// reads globals the host is expected to have declared. Concatenated into a host that declares them, it
/// compiles.
/// </para>
/// <para>
/// Globals the host must declare before including this: <c>sampler3D uClipmaps</c>, <c>int
/// uShadowLevels</c>, <c>int uShadowResolution</c>, <c>vec4 uClipmapOrigin[4]</c> — each an origin and
/// that level's voxel size — and <c>float uClipmapRange[4]</c>. Everything is measured in the surface
/// cache's own space, which is the space the origins were written in.
/// </para>
/// </remarks>
public static class DistanceFieldShaders
{
    /// <summary>How far the nearest surface is, from the finest clipmap that contains the point.</summary>
    /// <remarks>
    /// Reports "not known" where no level does, rather than a conservative small number. Outside every
    /// clipmap there is no information at all, and answering "very close" would shadow the whole far side
    /// of the level.
    /// </remarks>
    public const string Distance =
        """
        float DistanceAt(vec3 world, out bool known, out float raw, out float resolved)
        {
            known = false;
            raw = 0.0;
            resolved = uClipmapOrigin[0].w;

            for (int level = 0; level < uShadowLevels; level++)
            {
                vec4 corner = uClipmapOrigin[level];
                vec3 voxel = (world - corner.xyz) / corner.w;

                // Half a voxel inside, so a trilinear fetch at the boundary cannot reach the level's
                // clamped edge and read a distance belonging to nothing.
                if (any(lessThan(voxel, vec3(0.5)))
                    || any(greaterThan(voxel, vec3(float(uShadowResolution) - 0.5))))
                {
                    continue;
                }

                float slice = (float(level) * float(uShadowResolution)) + voxel.z;

                vec3 uvw = vec3(
                    voxel.x / float(uShadowResolution),
                    voxel.y / float(uShadowResolution),
                    slice / float(uShadowResolution * uShadowLevels));

                known = true;
                raw = texture(uClipmaps, uvw).r;
                resolved = corner.w;
                return ((raw * 2.0) - 1.0) * uClipmapRange[level];
            }

            return 1e6;
        }
        """;

    /// <summary>The march that finds where a ray stops. Needs <see cref="Distance"/> before it.</summary>
    /// <remarks>
    /// Sphere tracing: step by exactly the distance to the nearest surface. Returns one when nothing was
    /// hit and zero when something was, with where it stopped in <c>hit</c>.
    /// </remarks>
    public const string Visibility =
        """
        float VisibilityAt(vec3 world, vec3 normal, vec3 direction, out vec3 hit)
        {
            hit = world;
            if (uShadowLevels <= 0)
            {
                return 1.0;
            }

            // Started off the surface along its own normal. A ray beginning exactly on a surface begins
            // at distance zero, which reads as an immediate hit and shadows everything -- and the offset
            // has to clear a voxel of the FINEST level, because that is the resolution the field holds.
            float finest = uClipmapOrigin[0].w;
            vec3 at = world + (normal * finest * 1.5);

            // Taken from the clipmaps rather than written down. The outermost clipmap is what the trace
            // can see, so it is also how far it is worth marching.
            float travelled = 0.0;
            float reach = uClipmapOrigin[0].w * float(uShadowResolution) * float(1 << (uShadowLevels - 1));

            for (int step = 0; step < 64 && travelled < reach; step++)
            {
                bool known;
                float raw;
                float resolved;
                float distance = DistanceAt(at + (direction * travelled), known, raw, resolved);

                // Off the end of every clipmap. Nothing further along this ray can be described, so the
                // honest answer is that nothing was found to block it.
                if (!known)
                {
                    return 1.0;
                }

                // Everything here scales with the voxel of the level that ANSWERED, not the finest. The
                // fields are unsigned, so near a wall the trilinear read bottoms out around half the local
                // voxel rather than crossing zero: a hit threshold fixed at the finest level's half-voxel
                // can never trigger in an outer level, which silently limited every shadow to the
                // innermost box. And the same guard that stops a texel's own wall from reading as a
                // blocker has to widen with the voxel, because a coarse level cannot tell the ray's own
                // surface from one a few metres past it.
                if (travelled > resolved * 2.0 && distance < resolved * 0.5)
                {
                    // Where it stopped, nudged back along the ray. The trace halts about half a voxel
                    // short of the surface, and the index wants a point on the surface rather than one
                    // hanging in the air in front of it.
                    hit = at + (direction * (travelled + distance));
                    return 0.0;
                }

                travelled += max(distance, resolved * 0.25);
            }

            return 1.0;
        }
        """;

    /// <summary>Where a ray actually STOPS, for a pass that needs the point and not just a yes or no.</summary>
    /// <remarks>
    /// <para>
    /// <b>Why <see cref="Visibility"/> cannot be used for this, although it marches the same field.</b> A
    /// shadow ray only has to establish that something is in the way, so it stops the moment the field
    /// reads less than half a voxel and never asks where the surface was. On the outer clipmaps half a
    /// voxel is a metre, which makes that test wrong for a reflection in both directions at once: a ray
    /// that merely PASSES a metre from a façade reports a hit, so every grazing reflection down a street
    /// becomes a wall; and a hit it does report can be a metre off the surface, which is further than the
    /// surface cache's own tolerance, so nothing downstream can say which wall was struck. Asked to
    /// resolve reflections this way, a level came back as a sheet of "struck something nothing can
    /// shade".
    /// </para>
    /// <para>
    /// <b>What this does instead: find the crossing, not the proximity.</b> The field is signed — the
    /// build stores <c>clamp(nearest / range, -1, 1)</c> about a half — so a ray that enters solid
    /// geometry has a step outside it and a step inside it with a zero between them, and one secant step
    /// lands within a fraction of a voxel of the surface whatever size that voxel is. A ray that only
    /// passes nearby never changes sign and is correctly not a hit.
    /// </para>
    /// <para>
    /// <b>And the case a sign cannot catch.</b> Plenty of this level is a single sheet of triangles with
    /// no inside — a fence, a roof, a wall panel modelled one-sided — and a field built around one never
    /// goes negative however squarely it is struck. So a local minimum that comes within a quarter of a
    /// voxel counts too, taken at the closest point rather than interpolated. The two rules cover
    /// solid and sheet geometry respectively, and neither fires on a ray going past.
    /// </para>
    /// <para>
    /// The voxel that answered comes back with the hit because every tolerance downstream has to widen by
    /// it. A point found in the coarsest clipmap is good to a couple of metres and a surface cache asked
    /// to match it within a quarter of one will refuse every time.
    /// </para>
    /// </remarks>
    public const string Surface =
        """
        // Carries a stopped ray the rest of the way ONTO the surface, and a shade past it.
        //
        // The march stops short, and by a distance that matters. The field is built by sampling each
        // object's own field at voxel centres and resampling that into a clipmap at voxel centres, and
        // neither step can represent a zero that falls between two samples -- so near a wall the value
        // bottoms out around a third of a voxel and climbs again without ever crossing. Measured on a
        // level, the point the march stopped at was floating between a third and a half of a voxel in
        // front of the wall it had struck: at the coarse clipmaps that is the better part of a metre, and
        // every consumer of a hit asks which surface it is ON. Four fifths of them found no surface at
        // all, and the fifth that did disagreed with it by a metre and a half.
        //
        // So the residual is walked off along the RAY -- the one direction that cannot carry the point
        // onto some other surface -- twice, because one step of it is exact only for a ray meeting the
        // wall square on. The last nudge puts the point a quarter of a voxel INSIDE rather than on the
        // boundary, deliberately: a point a little inside a wall still resolves to that wall, and a point
        // a little in front of it resolves to nothing.
        vec3 Settle(vec3 at, vec3 direction, float travelled, out float voxel)
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

        bool SurfaceAt(vec3 world, vec3 normal, vec3 direction, float reach, out vec3 hit, out float voxel)
        {
            hit = world;
            voxel = uClipmapOrigin[0].w;
            if (uShadowLevels <= 0)
            {
                return false;
            }

            // Off the surface along its own normal, as the shadow march starts: a ray beginning exactly on
            // a surface begins at distance nought, and the offset has to clear a voxel of the finest level
            // because that is the resolution the field holds.
            float finest = uClipmapOrigin[0].w;
            vec3 at = world + (normal * finest * 1.5);

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

                // Off the end of every clipmap. Nothing further along this ray can be described at all, so
                // the honest answer is that the ray left the described world -- which is a miss, and the
                // caller will give it the sky.
                if (!known)
                {
                    return false;
                }

                if (started)
                {
                    // INSIDE. The surface is between the last step and this one, and the field is linear
                    // enough across one step for a secant to find it.
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

                    // Or a sheet, which has no inside to enter. The field bottomed out close to nothing
                    // and is climbing again, so the previous step was the closest approach -- and close
                    // enough to be a strike rather than a pass.
                    //
                    // A TWENTIETH of a voxel, which is very nearly touching, and the tightness is
                    // measured rather than cautious. Loosened to half a voxel this rule fires wherever a
                    // ray passes anywhere near a façade, which on the outer clipmaps is a metre: against
                    // a traced reference at the same viewpoint it turned thirty points of open sky into
                    // hits, and bought one and a half points of resolved surface for them. A reported hit
                    // that nothing can identify is worse than no hit at all -- the sky is a real answer
                    // and an invented surface is not -- so the rule only fires where it is sure.
                    if (distance > previousDistance && previousDistance < resolved * 0.05)
                    {
                        hit = Settle(at, direction, previous, voxel);
                        return true;
                    }
                }

                previous = travelled;
                previousDistance = distance;
                started = true;

                // Sphere tracing: exactly as far as the nearest surface, which by definition cannot pass
                // through one. The floor is what stops a ray running alongside a wall from taking a
                // thousand vanishing steps; it is a quarter of the voxel that answered, which is small
                // enough not to step over anything the field can describe.
                travelled += max(distance, resolved * 0.25);
            }

            return false;
        }

        // The surface normal at a marched hit: the field's gradient, by central differences one voxel
        // apart. A distance field has no triangle to take a normal from, and the cards are sampled by
        // normal, so this is what a march hands them -- the same answer the reference takes from its own
        // field. Nought where the field is unknown around the point, which a caller treats as "face the
        // ray".
        vec3 FieldNormal(vec3 world, float voxel)
        {
            bool known;
            float raw;
            float resolved;
            float step = max(voxel, 1e-3);
            vec3 gradient = vec3(
                DistanceAt(world + vec3(step, 0.0, 0.0), known, raw, resolved)
                    - DistanceAt(world - vec3(step, 0.0, 0.0), known, raw, resolved),
                DistanceAt(world + vec3(0.0, step, 0.0), known, raw, resolved)
                    - DistanceAt(world - vec3(0.0, step, 0.0), known, raw, resolved),
                DistanceAt(world + vec3(0.0, 0.0, step), known, raw, resolved)
                    - DistanceAt(world - vec3(0.0, 0.0, step), known, raw, resolved));

            // An unknown sample answers with a huge distance, and a difference of two of those is a
            // gradient of nothing worth believing.
            float length2 = dot(gradient, gradient);
            return (length2 > 1e-12 && length2 < 1e6) ? gradient * inversesqrt(length2) : vec3(0.0);
        }
        """;

    /// <summary>Both halves, in the order a host must declare them.</summary>
    public static string March => Distance + Environment.NewLine + Visibility;

    /// <summary>The march a reflection wants: the same field, asked where the ray stopped.</summary>
    public static string Trace => Distance + Environment.NewLine + Surface;
}
