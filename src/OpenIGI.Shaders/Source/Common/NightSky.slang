// The part of a night sky that is not air: the stars, a faint band of the galaxy, and the moon's disc.
//
// The atmosphere's table carries everything that scatters -- the moonlit blue, the airglow, an overcast
// deck -- and the frame draws it where there is no geometry. This is added on top of that draw, and
// only there: it is what the eye picks out of a sky, not light a surface is lit by. The moon's light on
// the world travels as the directional light, the same slot the sun uses by day.
//
// Everything here is procedural, so the sky has no texture to stream and no resolution: a star is a
// point wherever the camera looks. The stars live on a celestial sphere tied to the moon, so they wheel
// across the sky with it as the night goes on rather than staying painted on the dome.

#ifndef OPENIGI_NIGHT_SKY_INCLUDED
#define OPENIGI_NIGHT_SKY_INCLUDED

// The sky's axis of rotation: the celestial pole, as seen from a middle northern latitude. The world
// has no north, so this only has to be plausible, and a pole a little over halfway up the sky keeps the
// wheeling slow and gentle.
static const float3 kCelestialPole = float3(0.0, 0.62, 0.785);

// Where the galaxy's disc stands, as its pole in the celestial frame. Chosen to lay the band steeply
// across the sky from most places, which is how the summer Milky Way is usually seen.
static const float3 kGalacticPole = float3(0.46, -0.52, 0.72);

// The moon's angular radius, in radians. Real is 0.0045. A game camera's wide field of view makes that
// a few pixels, which reads as a star rather than a moon, so it is drawn at a little over twice that --
// the enlargement every game and most film makes.
static const float kMoonRadius = 0.0105;

uint3 NightPcg3(uint3 v)
{
    v = (v * 1664525u) + 1013904223u;
    v.x += v.y * v.z;
    v.y += v.z * v.x;
    v.z += v.x * v.y;
    v ^= v >> 16u;
    v.x += v.y * v.z;
    v.y += v.z * v.x;
    v.z += v.x * v.y;
    return v;
}

float3 NightHash3(uint3 cell)
{
    return float3(NightPcg3(cell)) * (1.0 / 4294967296.0);
}

// A direction's cube face and where on it, in [-1, 1] across both axes.
uint NightCubeFace(float3 d, out float2 uv)
{
    float3 a = abs(d);
    if (a.x >= a.y && a.x >= a.z)
    {
        uv = d.yz / a.x;
        return d.x > 0.0 ? 0u : 1u;
    }

    if (a.y >= a.z)
    {
        uv = d.xz / a.y;
        return d.y > 0.0 ? 2u : 3u;
    }

    uv = d.xy / a.z;
    return d.z > 0.0 ? 4u : 5u;
}

float3 NightCubeDirection(uint face, float2 uv)
{
    float3 d;
    if (face == 0u)
    {
        d = float3(1.0, uv.x, uv.y);
    }
    else if (face == 1u)
    {
        d = float3(-1.0, uv.x, uv.y);
    }
    else if (face == 2u)
    {
        d = float3(uv.x, 1.0, uv.y);
    }
    else if (face == 3u)
    {
        d = float3(uv.x, -1.0, uv.y);
    }
    else if (face == 4u)
    {
        d = float3(uv.x, uv.y, 1.0);
    }
    else
    {
        d = float3(uv.x, uv.y, -1.0);
    }

    return normalize(d);
}

// The celestial frame: its pole fixed, its zero meridian carried round by the moon. A world direction
// expressed in it is where on the star sphere that direction looks, tonight, at this hour.
float3 NightCelestial(float3 worldDir, float3 moonDir)
{
    float3 axisZ = kCelestialPole;
    float3 meridian = moonDir - (dot(moonDir, axisZ) * axisZ);
    float meridianLength = length(meridian);
    float3 axisX = meridianLength > 1e-4 ? meridian / meridianLength : float3(1.0, 0.0, 0.0);
    float3 axisY = cross(axisZ, axisX);
    return float3(dot(worldDir, axisX), dot(worldDir, axisY), dot(worldDir, axisZ));
}

// The colour of a star from its surface temperature, reduced to the four kinds the eye can tell apart
// in a night sky: blue-white, white, yellow and orange, weighted as they appear to the naked eye.
float3 NightStarColour(float t)
{
    if (t < 0.18)
    {
        return float3(0.72, 0.82, 1.0);
    }

    if (t < 0.62)
    {
        return float3(0.95, 0.96, 1.0);
    }

    if (t < 0.86)
    {
        return float3(1.0, 0.92, 0.76);
    }

    return float3(1.0, 0.76, 0.52);
}

// One layer of stars: a grid of cells on the cube, each holding at most one star, placed well inside the
// cell so that a star never needs its neighbours' cells to be drawn. Brightness follows a steep power law
// -- a handful bright, a great many at the edge of seeing -- which is what makes a star field read as sky
// rather than as noise.
//
// Each star is a small Gaussian at least a pixel wide, so a star smaller than a pixel is drawn at a
// pixel's size rather than aliasing in and out as the camera turns.
float3 NightStarLayer(
    float3 celestial,
    float cellsPerFace,
    float density,
    uint seed,
    float pixelAngle,
    float seconds,
    float scintillation)
{
    float2 uv;
    uint face = NightCubeFace(celestial, uv);
    float2 grid = ((uv * 0.5) + 0.5) * cellsPerFace;
    float2 cell = floor(grid);

    uint3 key = uint3(uint(cell.x), uint(cell.y), (face * 7919u) + seed);
    float3 h = NightHash3(key);
    if (h.z > density)
    {
        return float3(0.0, 0.0, 0.0);
    }

    float3 h2 = NightHash3(key + uint3(101u, 211u, 307u));

    float2 centreGrid = cell + 0.25 + (0.5 * h.xy);
    float2 centreUv = ((centreGrid / cellsPerFace) - 0.5) * 2.0;
    float3 starDir = NightCubeDirection(face, centreUv);

    float angle = length(celestial - starDir);
    float sigma = max(pixelAngle * 0.6, 1e-5);
    float spot = exp(-(angle * angle) / (2.0 * sigma * sigma));

    // Magnitude: h2.x is uniform, and raising it to a high power leaves most stars dim and a few bright.
    // The floor is the faintest star the eye picks out, which is most of them.
    float magnitude = pow(h2.x, 12.0);
    float brightness = 0.015 + (0.985 * magnitude);

    // Scintillation: turbulence in the air lensing the point, faster and deeper near the horizon where
    // the path through the air is longest.
    float twinkle = 1.0 + (scintillation * 0.45 * sin((seconds * (2.0 + (6.0 * h2.y))) + (h2.z * 60.0)));

    return NightStarColour(h2.z) * (brightness * twinkle * spot);
}

// A little low-frequency value noise on the sphere, for the galaxy's mottling and the moon's seas.
float NightValueNoise(float3 p)
{
    float3 i = floor(p);
    float3 f = p - i;
    f = f * f * (3.0 - (2.0 * f));

    uint3 base = uint3(int3(i) + int3(4096, 4096, 4096));
    float n000 = NightHash3(base).x;
    float n100 = NightHash3(base + uint3(1u, 0u, 0u)).x;
    float n010 = NightHash3(base + uint3(0u, 1u, 0u)).x;
    float n110 = NightHash3(base + uint3(1u, 1u, 0u)).x;
    float n001 = NightHash3(base + uint3(0u, 0u, 1u)).x;
    float n101 = NightHash3(base + uint3(1u, 0u, 1u)).x;
    float n011 = NightHash3(base + uint3(0u, 1u, 1u)).x;
    float n111 = NightHash3(base + uint3(1u, 1u, 1u)).x;

    float x00 = lerp(n000, n100, f.x);
    float x10 = lerp(n010, n110, f.x);
    float x01 = lerp(n001, n101, f.x);
    float x11 = lerp(n011, n111, f.x);
    return lerp(lerp(x00, x10, f.y), lerp(x01, x11, f.y), f.z);
}

// The night sky's own sources in one direction, to be added to the atmosphere's background.
//
// worldDir     the direction the pixel looks.
// moonDir      the world direction towards the moon.
// moonRadiance how bright the disc is; nought draws no moon.
// starRadiance how bright the brightest star is; nought draws no stars.
// cover        how overcast the night is: the stars and the disc go, the halo spreads into a glow.
// seconds      a running clock, for the twinkle.
// pixelAngle   how wide a pixel is, in radians, at the centre of the view.
// transmittance how much of the light from outside the air reaches the eye along this direction.
float3 NightSkySources(
    float3 worldDir,
    float3 moonDir,
    float moonRadiance,
    float starRadiance,
    float cover,
    float seconds,
    float pixelAngle,
    float3 transmittance)
{
    float3 sources = float3(0.0, 0.0, 0.0);
    float clear = saturate(1.0 - cover);

    if (starRadiance > 0.0 && clear > 0.0)
    {
        float3 celestial = NightCelestial(worldDir, moonDir);
        float scintillation = 0.35 + (0.65 * saturate(1.0 - (worldDir.z * 2.5)));

        // Two layers: a sparse one carrying the stars that can be told apart and a denser one of faint
        // ones for texture. A dark sky shows the naked eye some four and a half thousand stars in the
        // half of the sphere above the horizon, the great majority at the limit of seeing: about nine
        // thousand over the whole sphere in the first layer (6 x 55^2 cells, half of them holding one),
        // and a faint dust of perhaps four times that in the second.
        float3 stars = NightStarLayer(celestial, 55.0, 0.5, 17u, pixelAngle, seconds, scintillation)
            + (0.12 * NightStarLayer(celestial, 150.0, 0.3, 131u, pixelAngle, seconds, scintillation));

        // The galaxy: a soft band about its great circle, mottled by dust, and far fainter than a star.
        float bandDistance = dot(celestial, normalize(kGalacticPole));
        float band = exp(-(bandDistance * bandDistance) / (2.0 * 0.11 * 0.11));
        float mottle = (0.55 * NightValueNoise(celestial * 9.0)) + (0.45 * NightValueNoise(celestial * 23.0));
        float dust = smoothstep(0.35, 0.75, NightValueNoise((celestial * 14.0) + float3(3.1, 7.7, 1.3)));
        float galaxy = band * saturate((mottle * 1.3) - 0.25) * (1.0 - (0.6 * dust));

        // Clouds are not all or nothing: the stars thin out well before the deck closes over them.
        float starClear = clear * clear;
        sources += ((stars + (galaxy * 0.012 * float3(0.85, 0.9, 1.0))) * starRadiance * starClear);
    }

    if (moonRadiance > 0.0)
    {
        float cosAngle = dot(worldDir, moonDir);
        float angle = sqrt(max(2.0 - (2.0 * cosAngle), 0.0));

        // The disc: soft-edged by a pixel so it does not crawl, darkened towards its limb, with a few
        // broad seas. The disc goes quickly under cloud: two-thirds cover leaves only a smear.
        float edge = max(pixelAngle, kMoonRadius * 0.05);
        float disc = 1.0 - smoothstep(kMoonRadius - edge, kMoonRadius + edge, angle);
        if (disc > 0.0)
        {
            float r = saturate(angle / kMoonRadius);
            float limb = 0.62 + (0.38 * sqrt(max(1.0 - (r * r), 0.0)));

            float3 across = worldDir - moonDir;
            float seas = NightValueNoise((across / kMoonRadius) * 1.6 + float3(11.0, 5.0, 2.0));
            float albedo = 1.0 - (0.28 * smoothstep(0.45, 0.8, seas));

            float discClear = saturate(1.0 - (cover * 1.5));
            sources += float3(1.0, 0.98, 0.94) * (moonRadiance * disc * limb * albedo * discClear);
        }

        // The halo: forward scattering by haze close about the disc, and a wider, fainter aureole -- both
        // kept faint, because the bloom spreads whatever is here across a good part of the sky. Under
        // cloud the light that no longer comes through as a disc spreads into a broad glow in the deck.
        float nearHalo = pow(saturate(cosAngle), 6000.0);
        float farHalo = pow(saturate(cosAngle), 180.0);
        float glowSpread = pow(saturate(cosAngle), 18.0);
        float3 halo = float3(0.82, 0.88, 1.0) * moonRadiance
            * ((nearHalo * 0.006 * clear) + (farHalo * 0.0003) + (glowSpread * 0.0006 * cover));
        sources += halo;
    }

    return sources * transmittance;
}

#endif
