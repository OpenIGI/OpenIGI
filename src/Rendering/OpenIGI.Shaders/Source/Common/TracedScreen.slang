// Projecting a traced hit back onto the screen, and reading the finished frame there if the camera can
// actually see it.
//
// A reflection pass has two questions to answer once it knows which way a ray goes: what did it hit, and
// can the camera see what it hit. The first is a ray query against the world; the second decides whether
// the hit's colour is read out of the finished frame -- which has this frame's shadows, the soldiers and
// the first-person rig in it -- or has to fall back to the surface cache's static cards. Every pass that
// reflects anything asks both, in that order, for the same reasons, so the answer lives here once. The
// screen-space reflection pass and the glazing pass had a copy each, and they were identical only
// because one was typed out from the other; two hand-kept copies of a depth-agreement tolerance do not
// fail loudly when they drift -- a puddle and a window simply start disagreeing about what is in the
// room, and neither looks wrong on its own.
//
// This needs no ray query, so it compiles into every variant. An untraced build keeps it and simply never
// calls it.
//
// The host must declare before including: Texture2D<float4> uLit with its sampler uLitSampler, the
// finished frame; Texture2D<float4> uSceneDepth, the scene's depth; and the macros kViewToWorld (a
// float3x3, view space to camera-relative world), kUnproject (a float4 of the projection's x scale, y
// scale, near and far) and kResolution (a float2 of the target's width and height in pixels, for reading
// the depth by texel rather than through a sampler).

#ifndef OPENIGI_TRACED_SCREEN_INCLUDED
#define OPENIGI_TRACED_SCREEN_INCLUDED

// The screen UV a view-space point projects to.
float2 ViewToUv(float3 v)
{
    float2 ndc = float2(v.x * kUnproject.x / v.z, -v.y * kUnproject.y / v.z);
    return (ndc * 0.5) + 0.5;
}

// The view-space distance a depth sample stands at, without reprojecting it sideways.
float SceneViewZ(float depth)
{
    float ndcZ = 1.0 - (2.0 * depth);
    return (2.0 * kUnproject.z * kUnproject.w)
        / ((kUnproject.w + kUnproject.z) - (ndcZ * (kUnproject.w - kUnproject.z)));
}

// A traced hit the camera can actually see, resolved out of the lit frame instead of out of a card. This
// is the reference's SampleSceneColorAtHit and it is worth the handful of instructions: a card holds one
// texel for every several centimetres of wall and knows nothing about this frame's shadows, while the lit
// picture is the real thing at full detail. It is also the ONLY path by which anything that moves -- a
// soldier, the weapon, the player's own hands -- can appear in a reflection at all, because the cards are
// a capture of the static level.
//
// Deliberately simpler than the reference in one way: it reprojects into LAST frame's colour and tests
// the history's depth as well, because its trace runs before the frame is lit. Both of the passes here
// run after, so the hit is tested against this frame's own depth and read out of this frame's own colour,
// and there is no history to disagree with.
//
// Taken with a stated slack, for a caller whose hit is not exactly on the surface it means. A
// ray-triangle intersection needs none; a hit found by MARCHING a distance field is good only to a
// fraction of the voxel that found it, which on the outer clipmaps is most of a metre, and judged by the
// proportional test alone such a hit reads as "the camera drew something else here" and is thrown away.
// The ceiling of a hangar went black in the glass below it for exactly that reason.
bool LitAtHitWithin(float3 hitRelative, float slack, out float3 radiance)
{
    radiance = float3(0.0, 0.0, 0.0);

    // Back into view space. The transpose of a rotation is its inverse.
    float3 hitView = mul(transpose(kViewToWorld), hitRelative);
    if (hitView.z <= kUnproject.z)
    {
        // behind the eye, where the frame has nothing to say.
        return false;
    }

    // THE RIG USED TO BE LOOKED FOR HERE, THROUGH ITS OWN LENS, AND MUST NOT BE ANY MORE.
    //
    // The rig is projected with a retail foreground lens of its own rather than with the frame's, so a
    // world-space hit on it lands on the wrong pixels when projected through the frame lens; the branch
    // that used to stand here corrected for that and read the weapon out of the lit frame. It was right
    // for as long as a ray could strike the rig.
    //
    // A ray cannot strike it now -- the trace masks it out, because the player's third-person body
    // carries the same arms and the same weapon and a window was showing both. What the branch would do
    // today is worse than useless: every hit within two metres of the eye would be pushed through the rig
    // lens, and in first person the thing within two metres of the eye is the PLAYER'S OWN BODY. It would
    // land on whatever the weapon happens to cover on screen and paint that onto the reflected body.
    //
    // The rejection below stays. That is a different question -- not "is this hit the rig" but "are these
    // pixels the rig", and the answer still has to be no before they can be read.

    float2 uv = ViewToUv(hitView);
    if (any(uv < float2(0.0, 0.0)) || any(uv > float2(1.0, 1.0)))
    {
        return false;
    }

    // BY TEXEL, never sampled. The pass binds one linear sampler for everything, and a linear read of a
    // DEPTH buffer averages neighbouring depths -- which across a silhouette invents a surface at a
    // distance neither neighbour is at, and on any sloped surface shifts the value by a fraction of a
    // texel. The agreement test below then passes and fails in a beat against the reflection's own
    // sampling rate, so pixels drop out of this route in a regular dotted pattern and are shaded by a
    // different one. On screen that is a grid of dots through an otherwise clean reflection, which reads
    // as noise rather than as a filtering mistake.
    //
    // The reference samples its depth through a point-clamped sampler for the same reason.
    float depth = uSceneDepth.Load(int3(int2(uv * kResolution), 0)).r;
    if (depth <= 0.0)
    {
        // the camera drew sky there, so it did not draw this hit.
        return false;
    }

    // The rig again, reached through the world lens this time -- which means these pixels belong to the
    // rig but this hit does not, because the branch above already claimed every hit that does. Rejected
    // rather than read, or a wall behind the weapon would be painted with it.
    if (depth >= 0.9)
    {
        return false;
    }

    // The hit has to BE what the camera drew, not merely land on the same pixel as it: a ray passing a
    // wall on its way to something further off projects onto that wall's pixel, and reading its colour
    // would put the wall in the mirror instead of what the ray found. Proportional to distance, because
    // the depth buffer's own precision is.
    float sceneZ = SceneViewZ(depth);
    if (abs(hitView.z - sceneZ) > ((0.02 * max(sceneZ, 1.0)) + slack))
    {
        return false;
    }

    // NO world test here, and its absence is deliberate.
    //
    // A screen-space march has one -- the lit alpha is one for a surface and zero for everything drawn
    // over the picture rather than into it -- because the march GUESSES: it finds a depth that agrees and
    // assumes the ray went there, so a weapon drawn over a soldier got mirrored down the length of every
    // puddle in front of him. This does not guess. The ray struck actual geometry, and the projection
    // above only asks whether the camera can see the place it struck; a hit on the weapon is a hit on the
    // weapon, and reading its colour is reading the colour of the thing that is there.
    //
    // Which is exactly what a player looking at a window at arm's length wants. In first person the
    // weapon and the hands are the whole of what the frame holds of the player, and a reflection that
    // excluded them would show an empty room with the glass between.
    radiance = max(uLit.SampleLevel(uLitSampler, uv, 0.0).rgb, float3(0.0, 0.0, 0.0));
    return true;
}

// And the ordinary form, for a caller whose hit is exact.
bool LitAtHit(float3 hitRelative, out float3 radiance)
{
    return LitAtHitWithin(hitRelative, 0.0, radiance);
}

#endif
