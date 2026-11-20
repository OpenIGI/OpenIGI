// A bomb's after-blast: its front sweeping out over the world, drawn over the lit scene before the bloom and the tone map.
//
// The front is a sphere about the bomb, held a second after the boom and then swelling out (BombWave). Where it passes over
// a surface the surface catches it: a hard leading edge and a wake fading behind it, inside which the picture is streaked
// towards the bomb, as if the air itself were thrown outward from it, and its brightest parts flare. Nothing of it shows at
// the eye or far off. Once the front has passed the eye the whole picture is hazed with the dust it raised, the more the
// farther the front has gone on. The pass fades out over its last seconds.
//
// The streak is a handful of taps along the line from the pixel to the bomb's place on the screen -- or, when the bomb is
// behind the eye, to its mirror through the eye, so the streak still runs away from it -- each tap jittered by a hash so
// the steps never band. A pixel the front is not on reads only itself.
//
// Bound by name, in the shape of the mods' passes (Mod/PostPass.slang), which every backend already runs.

#include "../Common/ViewDepth.slang"

// The lit scene, linear and unexposed.
[[vk::binding(0, 0)]] Texture2D<float4> uSource;

// The scene's depth, reverse-Z, nought for the sky, at the scene's own resolution.
[[vk::binding(1, 0)]] DepthTexture2D uDepth;

// Where the picture goes, every texel of it.
[[vk::binding(2, 0)]] [[vk::image_format("rgba16f")]] WTexture2D<float4> uTarget;

struct AfterBlastParams
{
    // the target's width, height, 1/width, 1/height.
    float4 size;

    // the depth's width and height, and the depth's size over the target's.
    float4 depth;

    // the projection's x scale, y scale, near and far, in metres.
    float4 unproject;

    // the bomb in view space, metres; the front's radius, metres.
    float4 blast;

    // the leading edge's thickness, the wake's length, the streak's length on the screen, how strongly it shows.
    float4 shell;

    // the near fade's start and end, the far fade's start and end, metres from the eye.
    float4 reach;

    // the dust haze's colour, linear and divided by the exposure; how much of it.
    float4 haze;

    // the exposure the resolve applies, the frame's number, the tap count, unused.
    float4 tuning;
};

[[vk::binding(3, 0)]] ConstantBuffer<AfterBlastParams> p;

static const float3 Luma = float3(0.2126, 0.7152, 0.0722);

float3 SourceAt(int2 pixel)
{
    int2 last = int2(p.size.xy) - 1;
    return uSource.Load(int3(clamp(pixel, int2(0, 0), last), 0)).rgb;
}

float DepthAt(int2 pixel)
{
    int2 last = int2(p.depth.xy) - 1;
    int2 at = int2((float2(pixel) + 0.5) * p.depth.zw);
    return uDepth.Load(int3(clamp(at, int2(0, 0), last), 0));
}

float Hash(float2 seed)
{
    float3 q = frac(float3(seed.xyx) * 0.1031);
    q += dot(q, q.yzx + 33.33);
    return frac((q.x + q.y) * q.z);
}

// Where a view-space point falls on the screen, nought to one; a point behind the eye is mirrored through it first.
float2 ScreenOf(float3 view)
{
    float3 v = view.z > 0.01 ? view : -view;
    float z = max(v.z, 0.01);
    float2 ndc = float2((v.x * p.unproject.x) / z, (-v.y * p.unproject.y) / z);
    return (ndc * 0.5) + 0.5;
}

[shader("compute")]
[numthreads(8, 8, 1)]
void main(uint3 id : SV_DispatchThreadID)
{
    if (any(id.xy >= uint2(p.size.xy)))
    {
        return;
    }

    int2 pixel = int2(id.xy);
    float2 uv = (float2(pixel) + 0.5) * p.size.zw;
    float3 scene = SourceAt(pixel);
    float front = p.blast.w;

    // The surface under the pixel, and how far it is from the bomb and from the eye; the sky stands at the far plane.
    float depth = DepthAt(pixel);
    float3 surface = depth > 0.0 ? ViewPositionAt(uv, depth, p.unproject) : float3(0.0, 0.0, p.unproject.w * 4.0);
    float fromBomb = distance(surface, p.blast.xyz);
    float fromEye = length(surface);

    // The shell: a hard edge where the front is, a wake falling off behind it.
    float edge = smoothstep(front + p.shell.x, front, fromBomb);
    float wake = smoothstep(front - p.shell.y, front, fromBomb);
    float shell = edge * wake * wake * smoothstep(p.reach.x, p.reach.y, fromEye) * smoothstep(p.reach.w, p.reach.z, fromEye);

    float3 shown = scene;
    if (shell > 1e-3)
    {
        // Away from the bomb's place on the screen, the farther the pixel is from it the longer, up to a cap.
        float2 toBomb = ScreenOf(p.blast.xyz) - uv;
        float span = length(toBomb);
        float2 way = span > 1e-5 ? toBomb / span : float2(0.0, 0.0);
        float reachUv = shell * p.shell.z * min(span * 2.0, 1.0);
        int taps = max(int(p.tuning.z), 1);
        float jitter = Hash(float2(pixel) + (p.tuning.y * 7.31)) - 0.5;
        float3 sum = float3(0.0, 0.0, 0.0);
        for (int i = 0; i < taps; i++)
        {
            float k = ((float(i) + 0.5) / float(taps)) - 0.5 + (jitter / float(taps));
            float2 at = uv + (way * reachUv * k);

            // Pulled a little in towards the middle of the picture: the punch of the air thrown at the eye.
            at = lerp(at, float2(0.5, 0.5), 0.02 * shell);
            sum += SourceAt(int2(clamp(at, 0.0, 1.0) * p.size.xy));
        }

        float3 streaked = sum / float(taps);

        // What is already bright flares as the front passes it.
        float bright = saturate(dot(streaked, Luma) * p.tuning.x);
        streaked += streaked * pow(bright, 6.0) * 1.5;
        shown = lerp(scene, streaked, saturate(shell * p.shell.w));
    }

    // The dust, once the front has passed the eye.
    float passed = smoothstep(front, front - p.reach.w, length(p.blast.xyz));
    shown = lerp(shown, p.haze.rgb, passed * passed * p.haze.w);

    uTarget.Store(pixel, float4(shown, 1.0));
}
