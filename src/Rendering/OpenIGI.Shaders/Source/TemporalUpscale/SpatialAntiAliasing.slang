#include "../Common/Unroll.slang"

[[vk::binding(0, 0)]] Texture2D<float4> uInput;
[[vk::binding(1, 0)]] SamplerState uInputSampler;
[[vk::binding(2, 0)]] Texture2D<float4> uRejection;
[[vk::binding(4, 0)]] [[vk::image_format("rg16f")]] WTexture2D<float4> uOffset;

struct Params
{
    float4 size;
    float4 control;
};

[[vk::binding(5, 0)]] ConstantBuffer<Params> p;

// A sub-pixel offset toward the nearest edge, for the history update to nudge its reconstruction by.
//
// This is the edge search of Lottes (2009) without the final blend: find whether the local luma edge
// runs horizontally or vertically, walk along it in both directions until the luma leaves the band
// between the two sides, and from where the span ends work out how far this pixel sits from the point
// the edge crosses. The result is that distance, pointing across the edge, in input pixels.
// uRejection stays declared and unread; the C# side binds it by name.

static const float kContrastThreshold = 0.05;
static const int kWalkSteps = 8;

float LumaOf(float4 c)
{
    float l = dot(max(c.rgb, 0.0) * p.control.x, float3(0.299, 0.587, 0.114));
    return l / (0.5 + l);
}

float LumaTexel(int2 at, int2 size)
{
    return LumaOf(uInput.Load(int3(clamp(at, int2(0, 0), size - 1), 0)));
}

float LumaAt(float2 uv)
{
    float2 halfTexel = 0.5 * p.size.zw;
    return LumaOf(uInput.SampleLevel(uInputSampler, clamp(uv, halfTexel, 1.0 - halfTexel), 0.0));
}

[numthreads(8, 8, 1)]
void computeMain(uint3 id : SV_DispatchThreadID)
{
    int2 px = int2(id.xy);
    int2 size = int2(p.size.xy);
    if (px.x >= size.x || px.y >= size.y)
    {
        return;
    }

    float C = LumaTexel(px, size);
    float N = LumaTexel(px + int2(0, -1), size);
    float S = LumaTexel(px + int2(0, 1), size);
    float E = LumaTexel(px + int2(1, 0), size);
    float W = LumaTexel(px + int2(-1, 0), size);
    float NE = LumaTexel(px + int2(1, -1), size);
    float NW = LumaTexel(px + int2(-1, -1), size);
    float SE = LumaTexel(px + int2(1, 1), size);
    float SW = LumaTexel(px + int2(-1, 1), size);

    float lo = min(min(min(C, N), min(S, E)), min(min(W, NE), min(NW, min(SE, SW))));
    float hi = max(max(max(C, N), max(S, E)), max(max(W, NE), max(NW, max(SE, SW))));
    if ((hi - lo) <= kContrastThreshold)
    {
        uOffset.Store(px, float4(0.0, 0.0, 0.0, 0.0));
        return;
    }

    // How strongly the luma changes across rows versus across columns.
    float hv = abs(((NE + NW) * 0.5) - N) + abs(((E + W) * 0.5) - C) + abs(((SE + SW) * 0.5) - S);
    float vv = abs(((NE + SE) * 0.5) - E) + abs(((N + S) * 0.5) - C) + abs(((NW + SW) * 0.5) - W);

    int2 walkDir;
    int2 side;
    float edgeL;
    if (hv > vv)
    {
        walkDir = int2(0, 1);
        if (abs(W - C) > abs(E - C))
        {
            side = int2(-1, 0);
            edgeL = W;
        }
        else
        {
            side = int2(1, 0);
            edgeL = E;
        }
    }
    else
    {
        walkDir = int2(1, 0);
        if (abs(N - C) > abs(S - C))
        {
            side = int2(0, -1);
            edgeL = N;
        }
        else
        {
            side = int2(0, 1);
            edgeL = S;
        }
    }

    float delta = abs(edgeL - C) * 0.5;
    if (delta <= kContrastThreshold)
    {
        uOffset.Store(px, float4(0.0, 0.0, 0.0, 0.0));
        return;
    }

    // Walk from the boundary between this pixel and its edge-side neighbour, both ways along the edge,
    // until the luma leaves the band between the two sides.
    float2 start = (float2(px) + 0.5 + (float2(side) * 0.5)) * p.size.zw;
    float2 step = float2(walkDir) * p.size.zw;
    float mid = (edgeL + C) * 0.5;
    float halfBand = abs(edgeL - C) * 0.25;
    bool brighter = edgeL > C;

    bool doneP = false;
    bool doneN = false;
    bool highP = false;
    bool lowP = false;
    bool highN = false;
    bool lowN = false;
    float lenP = float(kWalkSteps);
    float lenN = float(kWalkSteps);
    OPENIGI_UNROLL for (int i = 1; i <= kWalkSteps; i++)
    {
        if (!doneP)
        {
            float l = LumaAt(start + (float(i) * step));
            if (l < (mid - halfBand))
            {
                lowP = true;
                doneP = true;
                lenP = float(i - 1);
            }
            else if (l > (mid + halfBand))
            {
                highP = true;
                doneP = true;
                lenP = float(i - 1);
            }
        }

        if (!doneN)
        {
            float l = LumaAt(start - (float(i) * step));
            if (l < (mid - halfBand))
            {
                lowN = true;
                doneN = true;
                lenN = float(i - 1);
            }
            else if (l > (mid + halfBand))
            {
                highN = true;
                doneN = true;
                lenN = float(i - 1);
            }
        }
    }

    // A direction whose span ends on the bright side of the band "increments": the edge bends toward
    // this pixel's side there. Where only one end does, the offset grows the nearer that end is.
    bool incP = brighter ? highP : lowP;
    bool decP = brighter ? lowP : highP;
    bool incN = brighter ? highN : lowN;
    bool decN = brighter ? lowN : highN;
    float inv = 1.0 / (1.0 + lenP + lenN);

    float dist = 0.0;
    if (!doneP && !doneN)
    {
        dist = 0.0;
    }
    else if (incP && incN)
    {
        dist = 0.5;
    }
    else if (decP && decN)
    {
        dist = 0.0;
    }
    else if (incN)
    {
        dist = 0.5 - ((lenN + 0.5) * inv);
    }
    else if (incP)
    {
        dist = 0.5 - ((lenP + 0.5) * inv);
    }

    uOffset.Store(px, float4(float(side.x) * dist, float(side.y) * dist, 0.0, 0.0));
}
