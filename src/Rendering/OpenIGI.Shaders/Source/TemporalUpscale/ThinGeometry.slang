// Finds geometry too fine for the rasteriser to resolve -- a wire, a railing, chain-link -- and
// relaxes the parallax verdict over it while the camera is still.
//
// The dilation goes through shared memory rather than evaluating the detection nine times over:
// every pixel of the group needs the score of its eight neighbours, and every one of those is a
// neighbour of eight others, so computing it once per pixel and sharing it does the work once
// instead of nine times for the same answer.

// The depth and the rejection are read by texel; the velocity is filtered, so only it needs a
// sampler. The others' sampler slots are reserved because the compute pass gives every sampled
// binding an image slot and a sampler slot.
#include "../Common/DepthSlot.slang"
#include "../Common/Unroll.slang"

OPENIGI_DEPTH_SLOT(0) uDepth;

[[vk::binding(2, 0)]] Texture2D<float4> uRejection;

// .r = the relaxed history rejection the update reads. .g = the raw line strength, which the
// shading rejection reads to widen its box -- see the note on the store at the end.
[[vk::binding(4, 0)]] [[vk::image_format("rg8")]] WTexture2D<float4> uRelaxed;

[[vk::binding(5, 0)]] Texture2D<float4> uVelocity;
[[vk::binding(6, 0)]] SamplerState uVelocitySampler;

struct Params
{
    // width, height, 1/width, 1/height.
    float4 size;

    // xScale, yScale, near, far.
    float4 unproject;

    // relative depth tolerance, relaxation, hasRejection, hasVelocity.
    float4 control;
};

[[vk::binding(7, 0)]] ConstantBuffer<Params> p;

// An eight-by-eight group plus the one-pixel apron the three-by-three dilation reaches into.
static const int kApron = 10;
groupshared float sLine[kApron * kApron];

float ViewDepth(float depth)
{
    float near = p.unproject.z;
    float far = p.unproject.w;
    float ndcZ = 1.0 - (2.0 * depth);
    return (2.0 * near * far) / ((far + near) - (ndcZ * (far - near)));
}

float DepthAt(int2 px, int2 size)
{
    float d = OPENIGI_DEPTH_LOAD(uDepth, clamp(px, int2(0, 0), size - 1));

    // The sky is infinitely far, and a wire against it is the case this exists for -- so it has to
    // read as "very distant" rather than as "no surface", or every silhouette against the sky would
    // fail the test that its neighbours are further away.
    return d <= 0.0 ? p.unproject.w : ViewDepth(d);
}

// The detection at one pixel. Factored out so it can be evaluated across a neighbourhood: the score
// has to be DILATED before it is used, because the pixels a wire mesh makes unstable are not only
// the ones the test fires on. A gap the wire crosses on some frames and misses on others is a pixel
// whose own neighbourhood looks, on the frame it was missed, like plain background -- there is
// nothing there to detect, and yet it is precisely the pixel that flickers.
float LineAt(int2 c, int2 size)
{
    if (OPENIGI_DEPTH_LOAD(uDepth, clamp(c, int2(0, 0), size - 1)) <= 0.0)
    {
        // Sky. There is no geometry here to be thin.
        return 0.0;
    }

    // The nine depths this needs, fetched once.
    float d[9];
    OPENIGI_UNROLL for (int y = -1; y <= 1; y++)
    {
        OPENIGI_UNROLL for (int x = -1; x <= 1; x++)
        {
            d[((y + 1) * 3) + (x + 1)] = DepthAt(c + int2(x, y), size);
        }
    }

    // How much nearer than a neighbour a pixel has to be before it counts as standing proud rather
    // than merely sloping away. Relative, because a fixed distance that separates a wire from its
    // background up close would separate nothing at all far away -- and far away is exactly where
    // thin geometry is lost.
    float tolerance = d[4] * p.control.x;

    // A one-pixel line: the middle row (or column) stands proud of the rows (columns) on either side
    // of it. The centre has to stand proud on both sides, and the score grows with how many of the side
    // pixels along the line do too, so a lone bump is not mistaken for a wire.
    float hL = 0.0;
    float hC = 0.0;
    float hR = 0.0;
    float vT = 0.0;
    float vM = 0.0;
    float vB = 0.0;
    OPENIGI_UNROLL for (int k = 0; k < 3; k++)
    {
        // Column k: its middle-row pixel against its top and bottom pixels.
        float h = ((d[k] - d[3 + k]) > tolerance ? 1.0 : 0.0)
            + ((d[6 + k] - d[3 + k]) > tolerance ? 1.0 : 0.0);

        // Row k: its middle-column pixel against its left and right pixels.
        float v = ((d[k * 3] - d[(k * 3) + 1]) > tolerance ? 1.0 : 0.0)
            + ((d[(k * 3) + 2] - d[(k * 3) + 1]) > tolerance ? 1.0 : 0.0);

        if (k == 0) { hL = h; vT = v; }
        else if (k == 1) { hC = h; vM = v; }
        else { hR = h; vB = v; }
    }

    float horizontal = hC < 2.0 ? 0.0 : saturate((hL + hR - 2.0) * 0.5);
    float vertical = vM < 2.0 ? 0.0 : saturate((vT + vB - 2.0) * 0.5);
    float ridge = saturate(horizontal + vertical);

    // The ridge test above finds an ISOLATED wire -- one pixel standing proud of a background on
    // both sides. It finds nothing on chain-link seen from any distance, because once the mesh is
    // dense enough that neighbouring pixels are themselves on wires, no pixel stands proud of
    // anything: the whole neighbourhood is wire and gap interleaved.
    //
    // So detect the interleaving itself. Walking the eight neighbours as a ring and classifying each
    // as differing or not from the centre, a clean silhouette divides the ring into one near arc and
    // one far arc, giving exactly two transitions. A wire mesh alternates, giving four or more.
    //
    // Classified by whether the neighbour lies at a DIFFERENT depth from the centre, in either
    // direction, rather than by which of the two is nearer. Asking "is the centre in front of this
    // one" only ever fires on the wire itself: a gap pixel between two wires is behind all of them,
    // scores no transitions at all, and is left to flicker.
    const int kRing[8] = { 0, 1, 2, 5, 8, 7, 6, 3 };
    int transitions = 0;
    bool previousDiffers = abs(d[4] - d[kRing[7]]) > tolerance;
    OPENIGI_UNROLL for (int i = 0; i < 8; i++)
    {
        bool differs = abs(d[4] - d[kRing[i]]) > tolerance;
        if (differs != previousDiffers)
        {
            transitions++;
        }

        previousDiffers = differs;
    }

    // Two transitions is a clean edge and scores nothing; four or more is fragmented and scores
    // full. Anything between is the ambiguous case and is graded rather than decided.
    float fragmented = clamp((float(transitions) - 2.0) * 0.5, 0.0, 1.0);

    return max(ridge, fragmented);
}

[shader("compute")]
[numthreads(8, 8, 1)]
void computeMain(
    uint3 id : SV_DispatchThreadID,
    uint3 groupId : SV_GroupID,
    uint3 localId : SV_GroupThreadID,
    uint localIndex : SV_GroupIndex)
{
    int2 px = int2(id.xy);
    int2 size = int2(p.size.xy);

    // No early exit for a pixel off the edge of the frame: it still has to reach the barrier below,
    // and a barrier some of the group skipped is undefined. Every thread fills its share and waits;
    // only the store at the end is guarded. LineAt clamps its own reads.
    int2 group = int2(groupId.xy) * 8;
    int2 apron = group - int2(1, 1);
    for (uint i = localIndex; i < uint(kApron * kApron); i += 64u)
    {
        int2 cell = int2(int(i) % kApron, int(i) / kApron);
        sLine[i] = LineAt(apron + cell, size);
    }

    GroupMemoryBarrierWithGroupSync();

    if (px.x >= size.x || px.y >= size.y)
    {
        return;
    }

    float believable = p.control.z > 0.5 ? uRejection.Load(int3(px, 0)).r : 1.0;

    int2 local = int2(localId.xy) + int2(1, 1);
    float line = 0.0;
    OPENIGI_UNROLL for (int y = -1; y <= 1; y++)
    {
        OPENIGI_UNROLL for (int x = -1; x <= 1; x++)
        {
            line = max(line, sLine[((local.y + y) * kApron) + local.x + x]);
        }
    }

    // Trust the history further where a line was found -- but ONLY while the pixel is still.
    //
    // The verdict being relaxed here is the PARALLAX one, which answers "was this surface in front
    // at its previous position". A false answer at rest is what makes a fence crawl: the wire's
    // depth flickers as the jitter moves under it, the test reads that as the surface having been
    // occluded, and the history is thrown away every frame. But under motion that same verdict is
    // doing its real job. Behind a fence is where disocclusion genuinely happens -- every wire
    // uncovers new background as the camera moves -- so relaxing it there hands the newly revealed
    // pixels whatever the wire was covering, which is a ghost of the fence smeared across the scene.
    //
    // Disocclusion is a consequence of motion; it cannot occur at a pixel that has not moved. So the
    // relaxation is a still-camera correction and falls off over a single pixel of velocity.
    float speedPx = 0.0;
    if (p.control.w > 0.5)
    {
        float2 uv = (float2(px) + 0.5) * p.size.zw;
        speedPx = length(uVelocity.SampleLevel(uVelocitySampler, uv, 0.0).xy * p.size.xy);
    }

    float stillness = clamp(1.0 - speedPx, 0.0, 1.0);
    float relaxed = lerp(believable, 1.0, line * p.control.y * stillness);

    // The line strength goes out alongside it, because relaxing the PARALLAX rejection is only half
    // of what a wire needs. That verdict answers "was this surface here last frame"; the shading
    // rejection separately answers "does it still look the same", and on a wire narrower than a
    // pixel the answer is no every single frame -- the pixel alternates between wire and sky as the
    // jitter moves under it. The shading rejection reads this strength to widen its comparison box.
    uRelaxed.Store(px, float4(relaxed, line, 0.0, 0.0));
}
