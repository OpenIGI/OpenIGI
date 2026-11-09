// The remastered bomb blast, in one draw a blast: its fireball and the column of smoke it rolls up into, marched as a
// volume, and the shock front it throws out, bending the picture where it passes.
//
// Drawn over the lit image in the forward pass, on a box around the blast whose INSIDE faces are drawn with no depth test,
// so every pixel the blast could touch runs once whether the eye is outside the box or in it. The scene's own depth (a
// copy, since the depth buffer is the pass's attachment) says where each ray stops, and the lit image as it stood (a
// copy too: a pass cannot read what it writes) is what the shock front bends and the smoke covers. The colour is written
// whole -- the backdrop through the blast -- and the blast's coverage is ADDED to the target's alpha, the marker the
// temporal resolve reads to keep moving overlays out of its history.
//
// The volume, in metres about the blast's centre with z up:
//   - the FIREBALL, a ball that reaches most of its size in a tenth of a second and rises as it burns;
//   - the STEM, a narrowing column from the ground up to it, and the CAP, a flattened, rolling torus where the column
//     spreads -- the mushroom every large charge rolls up into;
// each a soft shape whose surface is broken up by domain-warped fractal noise that boils upward with time. Heat is
// highest in the fireball's core and falls with time; where it is hot the volume gives out black-body light (dull red
// through orange to yellow-white), where it is cool it is smoke, lit by the sun and by the fire inside it, with a few
// steps toward each for its own shadow.
//
// The shock front is a sphere growing out of the centre. Where it meets a surface the scene there is pushed outward along
// the screen direction from the blast, with the three channels pushed by different amounts so the ring fringes; where the
// eye looks through its edge in the air, it bends like a lens rim. The ground under the front lifts a little dust.

#include "../Common/PushConstants.slang"
#include "../Common/ClipSpace.slang"
#include "../Common/DepthSlot.slang"
#include "../Common/ViewDepth.slang"

// How many blasts one frame's parameters carry.
#define BLAST_SLOTS 3

struct BlastVolumePush
{
    // The box's transform into view space; the projection is in the parameters.
    float4x4 modelView;

    // x: which blast of the parameters this draw is.
    // y: one over the exposure the resolve will apply.
    // z: how many steps the march takes.
    // w: the frame's index, for the march's jitter.
    float4 draw;
};

OPENIGI_PUSH_CONSTANT(BlastVolumePush, push);

struct BlastSlot
{
    // xyz: the blast's centre in view space, metres. w: the half-size of the box it is drawn on, a cube about the centre
    // in world axes.
    float4 centre;

    // x: its age in seconds. y: its seed, as a float. z: the fireball's radius. w: the fireball's heat, 0..1.
    float4 time;

    // x: the top of the column above the centre. y: the smoke's density. z: the shock front's radius. w: its strength.
    float4 shape;

    // xyz: the fire's light, linear. w: how brightly the fire glows, against the exposure.
    float4 fire;

    // x: the kind -- nought a bomb's fireball, one a building's collapse dust (time.z its radius, shape.x its height,
    // shape.y its density, centre.xyz its footprint's middle on the ground). yzw: unused.
    float4 extra;
};

struct BlastVolumeParams
{
    // The projection the box is drawn through.
    float4x4 projection;

    // The view's axes in world space, as rows: view to world for a direction is mul(viewToWorld, d).
    float4 viewToWorld0;
    float4 viewToWorld1;
    float4 viewToWorld2;

    // x scale, y scale, near and far, for the depth's view distance (Common/ViewDepth.slang).
    float4 unproject;

    // xy: the target's size. zw: one over it.
    float4 screen;

    // xyz: the way to the sun in view space. w: how much of the sun reaches the air (nought at night).
    float4 sun;

    // rgb: the sun's light on the smoke. a: unused.
    float4 sunColour;

    // rgb: the sky's light on the smoke's shadowed side. a: unused.
    float4 ambient;

    BlastSlot blasts[BLAST_SLOTS];
};

// The set every mesh pipeline shares. The blast samples none of it; it is declared because the set is shared.
[[vk::binding(0, 0)]] Texture2D<float4> uDiffuse;
[[vk::binding(1, 0)]] SamplerState uDiffuseSampler;
[[vk::binding(2, 0)]] Texture2D<float4> uLightmap;
[[vk::binding(3, 0)]] SamplerState uLightmapSampler;
[[vk::binding(4, 0)]] Texture2D<float4> uDetail;
[[vk::binding(5, 0)]] SamplerState uDetailSampler;

// The blast's own: the scene's depth and lit image as they stood before it, and the frame's parameters.
[[vk::binding(0, 1)]] DepthTexture2D uBlastDepth;
[[vk::binding(1, 1)]] Texture2D<float4> uBackdrop;
[[vk::binding(2, 1)]] SamplerState uBackdropSampler;
[[vk::binding(3, 1)]] ConstantBuffer<BlastVolumeParams> p;

struct BlastVertexInput
{
    [[vk::location(0)]] float3 position : TEXCOORD0;
    [[vk::location(1)]] float3 normal : TEXCOORD1;
    [[vk::location(2)]] float2 diffuseUv : TEXCOORD2;
    [[vk::location(3)]] float2 lightmapUv : TEXCOORD3;
    [[vk::location(4)]] float4 colorBgra : TEXCOORD4;
    [[vk::location(5)]] float3 previousPosition : TEXCOORD5;
    [[vk::location(6)]] float3 previousNormal : TEXCOORD6;
};

struct BlastVaryings
{
    float4 position : SV_Position;
    [[vk::location(0)]] float3 viewPos : TEXCOORD0;
};

[shader("vertex")]
BlastVaryings vertexMain(BlastVertexInput input)
{
    BlastVaryings output;
    float4 view = mul(push.modelView, float4(input.position, 1.0));
    output.viewPos = view.xyz;

    float4 clip = mul(p.projection, view);
    clip.y = OpenIgiClipY(clip.y);
    clip.z = (clip.w - clip.z) * 0.5;
    output.position = clip;
    return output;
}

// ---------------------------------------------------------------------------------------------------------------------
// Noise.

float Hash3(float3 q)
{
    q = frac(q * 0.3183099 + float3(0.71, 0.113, 0.419));
    q *= 17.0;
    return frac(q.x * q.y * q.z * (q.x + q.y + q.z));
}

// Value noise, smoothed, in [0,1].
float Noise3(float3 x)
{
    float3 i = floor(x);
    float3 f = frac(x);
    f = f * f * (3.0 - (2.0 * f));
    float n000 = Hash3(i);
    float n100 = Hash3(i + float3(1, 0, 0));
    float n010 = Hash3(i + float3(0, 1, 0));
    float n110 = Hash3(i + float3(1, 1, 0));
    float n001 = Hash3(i + float3(0, 0, 1));
    float n101 = Hash3(i + float3(1, 0, 1));
    float n011 = Hash3(i + float3(0, 1, 1));
    float n111 = Hash3(i + float3(1, 1, 1));
    return lerp(
        lerp(lerp(n000, n100, f.x), lerp(n010, n110, f.x), f.y),
        lerp(lerp(n001, n101, f.x), lerp(n011, n111, f.x), f.y),
        f.z);
}

// Fractal noise of a few octaves, in about [0,1]; each octave turned so their lattices do not line up.
float Fbm(float3 x, int octaves)
{
    float sum = 0.0;
    float amplitude = 0.5;
    float total = 0.0;
    for (int i = 0; i < 5; i++)
    {
        if (i >= octaves)
        {
            break;
        }

        sum += Noise3(x) * amplitude;
        total += amplitude;
        x = float3((x.x * 1.6) + (x.y * 1.2), (x.y * 1.6) - (x.x * 1.2), x.z * 2.0) + float3(3.1, 1.7, 5.3);
        amplitude *= 0.5;
    }

    return sum / max(total, 1e-4);
}

// ---------------------------------------------------------------------------------------------------------------------
// The volume.

struct VolumeSample
{
    float density;
    float heat;
};

// Billowed fractal noise: the creased, rounded puffs of rolling smoke rather than fuzz, in about [0,1].
float Billow(float3 x, int octaves)
{
    float sum = 0.0;
    float amplitude = 0.5;
    float total = 0.0;
    for (int i = 0; i < 4; i++)
    {
        if (i >= octaves)
        {
            break;
        }

        sum += (1.0 - abs((Noise3(x) * 2.0) - 1.0)) * amplitude;
        total += amplitude;
        x = float3((x.x * 1.7) - (x.z * 1.1), (x.y * 1.7) + (x.z * 0.9), (x.z * 1.9) + (x.x * 0.4)) + float3(5.7, 2.3, 9.1);
        amplitude *= 0.5;
    }

    return sum / max(total, 1e-4);
}

// How many heads the fireball throws off its core.
#define FIRE_LOBES 6

// One head of the fireball, drawn from the blast's seed: a ball thrown off the core, up-biased, a few hundredths of a
// second after it, so the fire bursts out in heads rather than growing as a sphere. xyz where it ends up about the
// fireball's centre, w its radius; both in fireball radii.
float4 Lobe(float seed, int i)
{
    float k = float(i) + 1.0;
    float h0 = Hash3(float3(seed * 0.00113, k * 1.37, 0.71));
    float h1 = Hash3(float3(k * 2.11, seed * 0.00071, 1.93));
    float h2 = Hash3(float3(0.37, k * 3.07, seed * 0.00157));
    float h3 = Hash3(float3((seed * 0.00031) + k, 4.1, k * 0.53));
    float3 d = normalize(float3((h0 * 2.0) - 1.0, (h1 * 2.0) - 1.0, (h2 * 1.4) - 0.2) + float3(0.0, 0.0, 1e-3));
    return float4(d * (0.32 + (0.34 * h3)), 0.42 + (0.26 * h2));
}

// The fireball's heads as they stand this frame -- where each is and how big -- worked out once a pixel by PrepareLobes
// rather than at every one of the march's look-ups: they hang only on the blast's seed and age.
static float4 gLobeAt[FIRE_LOBES];
static float gLobeCore[FIRE_LOBES];

void PrepareLobes(BlastSlot b, float3 fireCentre)
{
    float age = b.time.x;
    float seed = b.time.y;
    float fireR = max(b.time.z, 0.1);
    float grow = 1.0 - exp(-age / 0.11);
    for (int i = 0; i < FIRE_LOBES; i++)
    {
        float4 lobe = Lobe(seed, i);
        float delay = 0.012 * float(i);
        float thrown = saturate((age - delay) / 0.28);
        thrown = 1.0 - ((1.0 - thrown) * (1.0 - thrown));
        float radius = fireR * lobe.w * saturate(((age - delay) / 0.09) + 0.15);
        gLobeAt[i] = float4(fireCentre + (lobe.xyz * fireR * (0.4 + (0.6 * thrown))), 1.0 / max(radius, 0.05));
        gLobeCore[i] = 1.0 - (0.35 * grow * lobe.w);
    }
}

// Where the fireball's middle is at an age: it rises as it burns, and the column's cap is where it ends up.
float3 FireCentre(BlastSlot b)
{
    float age = b.time.x;
    float fireR = max(b.time.z, 0.1);
    float rise = fireR * (0.35 + (0.6 * smoothstep(0.2, 3.0, age)));
    float capZ = max(b.shape.x - (fireR * 0.4), rise);
    return float3(0.0, 0.0, lerp(rise, capZ, smoothstep(0.8, 6.0, age)));
}

// A building's collapse at a point, in metres from its footprint's middle on the ground: a dome of dust that rolls out
// off the footprint along the ground and hangs.
VolumeSample Dust(BlastSlot b, float3 q, float3 seedShift, int octaves)
{
    VolumeSample s;
    float age = b.time.x;
    float r = max(b.time.z, 0.5);
    float h = max(b.shape.x, 0.5);
    float3 e = float3(q.xy / r, max(q.z, 0.0) / (h * 0.75));
    float dome = saturate(1.0 - length(e)) * (q.z > -1.0 ? 1.0 : 0.0);

    // The rim rolls: a fat ring at the edge, the surge every collapse pushes out along the ground.
    float2 ring = float2(length(q.xy) - (r * 0.78), max(q.z, 0.0) - (h * 0.18));
    float rim = saturate(1.0 - (length(ring) / max(h * 0.42, 0.3)));
    // Mostly the rolling surge; the dome over the heap only a broken haze, never a smooth cap.
    float body = max(dome * 0.6, rim);

    // No noise can raise dust where the shape holds none: most of the box, left at the cost of the shape alone.
    s.heat = 0.0;
    if (body < 0.05)
    {
        s.density = 0.0;
        return s;
    }

    float3 m = (float3(q.xy, q.z * 0.8) / r * 2.2) + seedShift;
    m.z -= age * 0.12;
    m.xy -= normalize(q.xy + float2(1e-3, 1e-3)) * (age * 0.18);
    float puff = saturate(((Billow(m, octaves) - 0.5) * 2.4) + 0.5);
    float grain = octaves >= 3 ? Fbm((m * 3.3) + float3(0.0, 0.0, age * 0.3), 2) : 0.5;
    float d = smoothstep(0.08, 0.6, (body * 1.6) - ((1.0 - puff) * 1.4));
    s.density = saturate(d - ((1.0 - d) * grain * 1.1)) * b.shape.y * 0.6;
    return s;
}

// The volume at a point, in metres from the centre with z up.
VolumeSample Volume(BlastSlot b, float3 q, int octaves)
{
    float age = b.time.x;
    float seed = b.time.y;
    float fireR = max(b.time.z, 0.1);
    float heat = b.time.w;
    float top = b.shape.x;
    float smoke = b.shape.y;
    float3 seedShift = float3(frac(seed * 0.0013) * 97.0, frac(seed * 0.0007) * 61.0, frac(seed * 0.0021) * 43.0);
    if (b.extra.x > 0.5)
    {
        return Dust(b, q, seedShift, octaves);
    }

    VolumeSample s;

    // The fireball rises as it burns, and the column's cap is where it ends up.
    float rise = fireR * (0.35 + (0.6 * smoothstep(0.2, 3.0, age)));
    float capZ = max(top - (fireR * 0.4), rise);
    float3 fireCentre = float3(0.0, 0.0, lerp(rise, capZ, smoothstep(0.8, 6.0, age)));

    // The fireball: a core and the heads it throws off, each a soft ball, the nearest one's shape kept.
    float ball = saturate(1.0 - (length(q - fireCentre) / (fireR * 0.78)));
    float core = saturate(1.0 - (length(q - fireCentre) / (fireR * 0.6)));
    for (int i = 0; i < FIRE_LOBES; i++)
    {
        float d = length(q - gLobeAt[i].xyz) * gLobeAt[i].w;
        ball = max(ball, saturate(1.0 - d));
        core = max(core, saturate(1.0 - (d * 1.3)) * gLobeCore[i]);
    }

    // The stem: from the ground to the cap, wider at the foot where the dust is sucked in.
    float stemT = saturate(q.z / max(capZ, 0.1));
    float stemR = fireR * lerp(0.55, 0.28, stemT) * smoothstep(0.15, 1.2, age);
    float dStem = (length(q.xy) / max(stemR, 0.05)) + (q.z < 0.0 ? -q.z / fireR * 4.0 : 0.0) + (q.z > capZ ? (q.z - capZ) / fireR * 4.0 : 0.0);

    // The cap: a flattened torus rolling where the column spreads.
    float capR = fireR * lerp(0.55, 0.95, smoothstep(0.4, 6.0, age));
    float2 torus = float2(length(q.xy) - (capR * 0.55), (q.z - capZ) * 1.6);
    float dCap = length(torus) / max(capR * 0.6, 0.1);

    float stem = saturate(1.0 - dStem) * smoke;
    float cap = saturate(1.0 - dCap) * smoothstep(0.5, 1.6, age) * smoke * 0.8;
    float shape = max(ball, max(stem * 0.85, cap));

    // No noise can raise smoke where the shape holds none: most of the box, left at the cost of the shape alone.
    s.heat = 0.0;
    if (shape <= 0.0)
    {
        s.density = 0.0;
        return s;
    }


    // The boil: the noise field climbs and turns over, fast while it burns and slow once it is smoke, warped by a coarser
    // field of itself.
    float boil = (age * 0.35) + (0.9 * (1.0 - exp(-age / 0.45)));
    float3 n = ((q / max(fireR, 1.0)) * 2.6) + seedShift;
    n.z -= boil;
    // A shadow's look-up (one octave) takes the field unwarped and as one noise: it only needs the body's depth.
    float3 warped = n;
    float fine;
    float puffs;
    if (octaves >= 2)
    {
        float3 warp = float3(Noise3(n * 0.5), Noise3((n * 0.5) + 7.3), Noise3((n * 0.5) + 13.1)) - 0.5;
        warped = n + (warp * 1.7);
        fine = Fbm(warped, octaves);
        puffs = Billow(warped * 0.7, max(octaves - 1, 2));
    }
    else
    {
        fine = Noise3(n);
        puffs = fine;
    }

    float detail = saturate(((lerp(fine, puffs, 0.55) - 0.5) * 2.5) + 0.5);

    // The noise breaks the surface: low noise eats in, high noise pushes out. A soft shoulder rather than a square, so the
    // ball is solid out to most of its radius and only its edge is billows.
    float density = smoothstep(0.0, 0.45, (shape * 1.9) - ((1.0 - detail) * 1.0));

    // The edges eaten into wisps by a finer field, where the body thins; its heart left whole.
    if (octaves >= 3 && density > 0.0 && density < 0.9)
    {
        float wisp = Fbm((warped * 3.1) + float3(0.0, 0.0, -boil * 1.7), 2);
        density = saturate((density - ((1.0 - density) * wisp * 0.75)) / max(1.0 - (wisp * 0.3), 0.3));
    }

    // Heat: the cores burn and their outsides do not -- the fire inside a shell of its own soot, which is what makes a
    // fireball read as one. It all cools with the blast's own heat.
    float shell = smoothstep(0.08, 0.5, ball);
    s.heat = saturate(core * (0.2 + (detail * 1.1)) * shell * 1.3) * heat;
    s.density = density * lerp(1.0, 0.8, s.heat) * max(smoke, heat);
    return s;
}

#ifndef PI
#define PI 3.14159265
#endif

// How many steps each sample's shadow takes toward the sun, and how many orders of scattering are summed.
#define SHADOW_STEPS 3
#define SCATTER_ORDERS 3
#define ISOTROPIC_PHASE (1.0 / (4.0 * PI))

// Henyey-Greenstein: how much light a particle sends off at an angle to how it came, for an asymmetry g.
float HenyeyGreenstein(float g, float cosTheta)
{
    float g2 = g * g;
    float denominator = max(1.0 + g2 - (2.0 * g * cosTheta), 1e-4);
    return (1.0 - g2) / (4.0 * PI * denominator * sqrt(denominator));
}

// Smoke and dust scatter mostly onward -- the bright rim of a cloud against the sun -- with a little thrown back.
float DualLobePhase(float cosTheta)
{
    return lerp(HenyeyGreenstein(0.6, cosTheta), HenyeyGreenstein(-0.25, cosTheta), 0.3);
}

// Black-body light for a heat in [0,1]: nothing cold, a dull red, orange, then yellow-white at the hottest.
float3 BlackBody(float heat)
{
    float t = saturate(heat);
    float3 colour = float3(1.0, pow(t, 1.6) * 0.5, pow(t, 4.0) * 0.18);
    return colour * (pow(t, 2.4) * 16.0);
}

// The world direction of a view-space direction.
float3 ViewToWorld(float3 d)
{
    return float3(dot(p.viewToWorld0.xyz, d), dot(p.viewToWorld1.xyz, d), dot(p.viewToWorld2.xyz, d));
}

// Where a ray enters and leaves a box about the origin, or (1,0) for a miss.
float2 RayBox(float3 origin, float3 dir, float3 halfSize)
{
    float3 inv = 1.0 / max(abs(dir), float3(1e-6, 1e-6, 1e-6)) * sign(dir + float3(1e-9, 1e-9, 1e-9));
    float3 t0 = (-halfSize - origin) * inv;
    float3 t1 = (halfSize - origin) * inv;
    float3 lo = min(t0, t1);
    float3 hi = max(t0, t1);
    return float2(max(max(lo.x, lo.y), lo.z), min(min(hi.x, hi.y), hi.z));
}

float InterleavedGradient(float2 pixel, float frame)
{
    float2 q = pixel + (frame * float2(47.0, 17.0) * 0.695);
    return frac(52.9829189 * frac(dot(q, float2(0.06711056, 0.00583715))));
}

[shader("fragment")]
float4 fragmentMain(BlastVaryings input) : SV_Target
{
    BlastSlot b = p.blasts[clamp(int(push.draw.x + 0.5), 0, BLAST_SLOTS - 1)];
    int2 pixel = int2(input.position.xy);
    float2 uv = (float2(pixel) + 0.5) * p.screen.zw;

    float3 dir = normalize(input.viewPos);

    // Where the scene stops the ray, along it. The sky is cleared to nought (reverse-Z) and stops nothing.
    float depth = OPENIGI_DEPTH_LOAD(uBlastDepth, pixel);
    float sceneZ = depth > 0.0 ? ViewDepthAt(depth, p.unproject) : 1e7;
    float sceneT = sceneZ / max(dir.z, 1e-4);

    // Everything below in world axes about the blast's centre, metres.
    float3 originW = ViewToWorld(-b.centre.xyz);
    float3 dirW = ViewToWorld(dir);

    // Only the box's far face marches: where the ray leaves the box. Both faces are drawn so the eye may stand inside it,
    // and the near one is let go here rather than by winding, which the two clip conventions would each have to get right.
    float3 box = float3(b.centre.w, b.centre.w, b.centre.w);
    float2 through = RayBox(originW, dirW, box);
    if (length(input.viewPos) < through.y * 0.995)
    {
        discard;
    }

    // ---- The shock front --------------------------------------------------------------------------------------------
    float shockR = b.shape.z;
    float shockStrength = b.shape.w;
    float2 bend = float2(0.0, 0.0);
    float dust = 0.0;
    if (shockStrength > 0.001 && shockR > 0.1)
    {
        float width = (shockR * 0.06) + 0.6;

        // On the surfaces it reaches: the scene point's distance from the centre against the front's.
        float3 surface = originW + (dirW * min(sceneT, 1e5));
        float onSurface = depth > 0.0 ? exp(-pow((length(surface) - shockR) / width, 2.0)) : 0.0;

        // Through the air at its edge: where the ray grazes the sphere it bends most.
        float along = -dot(originW, dirW);
        float3 nearest = originW + (dirW * along);
        float miss = length(nearest);
        float rim = 0.0;
        if (miss < shockR && along > 0.0)
        {
            float halfChord = sqrt(max((shockR * shockR) - (miss * miss), 0.0));
            float entry = along - halfChord;
            if (entry > 0.0 && entry < sceneT)
            {
                // A band just inside the silhouette, faded in and out, so the edge of the sphere never shows as a line.
                float grazing = miss / shockR;
                rim = smoothstep(0.82, 0.96, grazing) * (1.0 - smoothstep(0.96, 1.0, grazing)) * 0.55;
            }
        }

        // Pushed outward along the screen line from the blast's centre, the far side of the ring the most.
        float4 centreClip = mul(p.projection, float4(b.centre.xyz, 1.0));
        float2 centreUv = centreClip.w > 1e-3
            ? float2((centreClip.x / centreClip.w) * 0.5 + 0.5, 0.5 - ((centreClip.y / centreClip.w) * 0.5))
            : uv;
        float2 out2 = uv - centreUv;
        float outLength = length(out2);
        float2 outward = outLength > 1e-4 ? out2 / outLength : float2(0.0, 1.0);

        float amount = (onSurface + rim) * shockStrength;
        bend = outward * amount * 0.010;

        // What the front kicks up off the ground as it passes, in the lower half of the sphere.
        dust = onSurface * shockStrength * saturate(-surface.z / max(shockR * 0.15, 1.0) + 0.6) * 0.35;
    }

    // ---- The heat haze over the fire: the air above it shimmers while it burns ---------------------------------------
    float hazeHeat = b.extra.x > 0.5 ? 0.0 : b.time.w;
    if (hazeHeat > 0.05)
    {
        float hazeR = max(b.time.z, 0.1) * 1.5;
        float3 hazeCentre = float3(0.0, 0.0, max(b.time.z, 0.1) * 1.6);
        float along = max(dot(hazeCentre - originW, dirW), 0.0);
        if (along < sceneT)
        {
            float miss = length(originW + (dirW * along) - hazeCentre);
            float haze = exp(-(miss * miss) / (hazeR * hazeR)) * smoothstep(0.05, 0.5, hazeHeat);
            float age = b.time.x;
            float2 wobble = float2(
                Noise3(float3(uv * float2(70.0, 38.0), age * 2.6)),
                Noise3(float3((uv * float2(70.0, 38.0)) + 11.7, (age * 2.6) + 5.1))) - 0.5;
            bend += wobble * (0.0045 * haze);
        }
    }

    // ---- The backdrop, bent and fringed -----------------------------------------------------------------------------
    float3 behind;
    float bendAmount = length(bend);
    if (bendAmount > 1e-5)
    {
        float r = uBackdrop.SampleLevel(uBackdropSampler, uv + (bend * 1.08), 0.0).r;
        float g = uBackdrop.SampleLevel(uBackdropSampler, uv + bend, 0.0).g;
        float bl = uBackdrop.SampleLevel(uBackdropSampler, uv + (bend * 0.92), 0.0).b;
        behind = float3(r, g, bl);

        // The compressed air is a little brighter at the front.
        behind *= 1.0 + (bendAmount * 3.0);
    }
    else
    {
        behind = uBackdrop.SampleLevel(uBackdropSampler, uv, 0.0).rgb;
    }

    behind = lerp(behind, float3(0.32, 0.27, 0.22) * (p.ambient.rgb + (p.sunColour.rgb * p.sun.w * 0.5)), saturate(dust));

    // ---- The fireball and the column --------------------------------------------------------------------------------
    bool isDust = b.extra.x > 0.5;
    float fireR = max(b.time.z, 0.1);
    float top = b.shape.x;
    float3 halfSize = isDust
        ? float3(fireR * 1.15, fireR * 1.15, (top * 0.6) + 1.0)
        : float3(fireR * 1.7, fireR * 1.7, (top + fireR * 1.6) * 0.5);
    float3 boxCentre = isDust ? float3(0.0, 0.0, halfSize.z - 1.0) : float3(0.0, 0.0, halfSize.z - (fireR * 0.6));
    float2 span = RayBox(originW - boxCentre, dirW, halfSize);
    span.x = max(span.x, 0.0);
    span.y = min(span.y, sceneT);

    float3 light = float3(0.0, 0.0, 0.0);
    float transmittance = 1.0;
    if (span.y > span.x && (b.time.w > 0.001 || b.shape.y > 0.001))
    {
        int steps = clamp(int(push.draw.z + 0.5), 8, 64);
        float stepLength = (span.y - span.x) / float(steps);
        float jitter = InterleavedGradient(float2(pixel), push.draw.w);
        float3 sunW = ViewToWorld(p.sun.xyz);
        int octaves = steps >= 20 ? 4 : 3;

        // How thick the smoke is per metre at full density: a fireball tens of metres across is opaque at its heart. Dust
        // is thinner per metre: a collapse's cloud is seen into, not a wall.
        float sigma = (isDust ? 4.0 : 7.0) / max(fireR, 1.0);

        // Soot early, grey smoke once the fire is out: the smoke right off a fireball is near black, its column greys.
        float soot = saturate(b.time.w * 1.6);
        float3 albedo = isDust
            ? float3(0.62, 0.56, 0.47)
            : lerp(float3(0.36, 0.34, 0.32), float3(0.08, 0.072, 0.065), soot);

        // The first frames: a flash core brighter than any fire, the whole ball lit through.
        float flash = isDust ? 0.0 : exp(-b.time.x / 0.05);
        if (!isDust)
        {
            PrepareLobes(b, FireCentre(b));
        }

        // The shadow look-ups are the march's cost: taken at every other lit sample and held between, which the jittered
        // march and the temporal resolve smooth over.
        float depthSun = 0.0;
        float depthFire = 0.0;
        float depthUp = 0.0;
        int lit = 0;
        for (int i = 0; i < 64; i++)
        {
            if (i >= steps || transmittance < 0.01)
            {
                break;
            }

            float t = span.x + ((float(i) + jitter) * stepLength);
            float3 q = originW + (dirW * t);
            VolumeSample s = Volume(b, q, octaves);
            if (s.density < 0.003)
            {
                continue;
            }

            // Its own shadow: the depth of smoke toward the sun, toward the fire's core and straight up, read at steps that
            // spread out with distance -- close detail for the billows' own shading, far reach for the body's.
            float reachAll = isDust ? max(top, 2.0) * 0.9 : fireR * 1.3;
            if ((lit & 1) == 0)
            {
                depthSun = 0.0;
                depthFire = 0.0;
                float3 core = float3(0.0, 0.0, fireR * 0.5);
                float3 toCore = normalize(core - q + float3(0.0, 0.0, 1e-3));
                float previous = 0.0;
                for (int k = 1; k <= SHADOW_STEPS; k++)
                {
                    float f = float(k) / float(SHADOW_STEPS);
                    float reach = reachAll * f * f;
                    float segment = reach - previous;
                    previous = reach;
                    depthSun += Volume(b, q + (sunW * reach), 1).density * segment;
                }

                if (!isDust && b.time.w > 0.01)
                {
                    depthFire = Volume(b, q + (toCore * (fireR * 0.4)), 1).density * fireR * 0.6;
                }

                // The sky reaches less of it the deeper into the smoke it is from above.
                float upReach = reachAll * 0.5;
                depthUp = Volume(b, q + float3(0.0, 0.0, upReach), 1).density * upReach;
            }

            lit++;

            // Light scattered several times: each order weaker, less shadowed and less directional, so the core stays
            // dark but the shaded side never goes flat black.
            float cosTheta = dot(dirW, sunW);
            float phase = DualLobePhase(cosTheta);
            float sunLit = 0.0;
            float scatterWeight = 1.0;
            float extinctionWeight = 1.0;
            float phaseWeight = 1.0;
            for (int o = 0; o < SCATTER_ORDERS; o++)
            {
                float orderPhase = lerp(ISOTROPIC_PHASE, phase, phaseWeight);
                sunLit += scatterWeight * exp(-depthSun * sigma * extinctionWeight) * orderPhase;
                scatterWeight *= 0.5;
                extinctionWeight *= 0.4;
                phaseWeight *= 0.5;
            }

            // In the scene's light as the fog's medium is lit: the sun through the phase, the sky by a share of it.
            sunLit *= p.sun.w;
            float skyLit = lerp(0.35, 1.0, exp(-depthUp * sigma * 0.6));

            // The ground's light from under it, a little, on the side facing down.
            float groundLit = 0.18 * saturate(1.0 - (q.z / max(reachAll, 1.0)));
            float fireLit = exp(-depthFire * sigma);

            // The smoke lit by the sun and the sky, in the scene's own light as the backdrop is, and from inside and below by
            // the fire it rolls over -- which, like the fire's glow, is lifted out of the exposure.
            float3 scattered = albedo * ((p.sunColour.rgb * sunLit) + (p.ambient.rgb * (skyLit + groundLit) * 0.25));
            float3 fireScattered = albedo * b.fire.rgb * (b.fire.w * b.time.w * fireLit * 3.0);
            float3 emitted = (BlackBody(s.heat) + (float3(1.0, 0.82, 0.6) * (flash * 60.0 * saturate(s.density * 2.0)))) * b.fire.w;

            float extinction = s.density * sigma;
            float absorbed = 1.0 - exp(-extinction * stepLength);

            // Energy-conserving integration of a slab of constant medium.
            // The smoke's lit colour is in the scene's own light, as the backdrop is; only the fire's glow is lifted out of
            // the exposure, so it burns under any.
            light += transmittance * absorbed * (scattered + ((emitted + fireScattered) * push.draw.y));
            transmittance *= 1.0 - absorbed;
        }
    }

    // What the eye sees: the blast's own light over the backdrop through it, divided out of the exposure the resolve will
    // apply back so the fire reads as fire under any exposure.
    float3 colour = light + (behind * transmittance);
    float coverage = saturate((1.0 - transmittance) + (bendAmount * 30.0) + dust);
    if (coverage <= 0.0005)
    {
        discard;
    }

    return float4(colour, coverage);
}
