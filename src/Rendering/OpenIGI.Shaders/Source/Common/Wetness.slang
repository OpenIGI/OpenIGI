// Standing water: where it lies, what shape its surface is, and how sharply it reflects.
//
// Why this is its own file. Two passes have to agree about the water to the last bit. The shading pass
// decides which pixels are under water, darkens them, flattens them and puts the sky in them; the
// reflection pass marches the scene into the same pixels afterwards. If the two disagree about the
// puddle by so much as a shoreline, the reflection lands beside the water rather than in it -- and they
// disagreed about a good deal more than that while each carried its own copy of the height field: the
// reflection knew nothing of the rain's ripples, nothing of the roofs the rain does not reach, and drew
// a hard mirror over water the shading had already rippled.
//
// So the water is defined once, here, and both passes include it. Nothing in it reads a binding: every
// function takes a WetParams the calling pass fills from whatever block it happens to carry the weather
// in, which is what lets one description serve a fragment shader reading a subpass uniform and a compute
// shader reading its own parameter buffer.
//
// It is all closed form: a height field that decides where water lies, a slope gate that keeps it off
// anything that is not level, staggered lattices of expanding rings for the rain, and a scrolling ruffle
// for the wind. None of that needs an image, which matters because the pass that would have had to
// sample one -- the G-buffer fill -- has neither the bindings nor a world position to sample it with.

#ifndef OPENIGI_WETNESS_INCLUDED
#define OPENIGI_WETNESS_INCLUDED

#include "Shelter.slang"

// The weather, as the water cares about it. Assembled by the including pass from its own uniform.
struct WetParams
{
    // the camera's world XY, which every lattice below is anchored to.
    float2 camera;

    // how wet the world is, 0 dry to 1 soaked.
    float amount;

    // what share of level ground stands under water.
    float coverage;

    // how far across one hollow in the ground is, in metres.
    float puddleSize;

    // how hard the rain strikes the water, 0 to 1.
    float rippleStrength;

    // how far apart the drops strike, in metres.
    float rippleSize;

    // how much the wind textures the water, 0 to 1.
    float windStrength;

    // the clock the ripples and the wind are animated from.
    float seconds;
};

// How smooth rain alone is allowed to make a surface. Standing water goes far below this; nothing else
// may, however soaked it is.
static const float kDampRoughnessFloor = 0.55;

// How smooth still water is. Not zero and not near it: even a sheltered puddle carries a texture finer
// than any lattice here describes, and a surface at zero roughness is a chrome sheet, which is the single
// most recognisable tell of a rendered puddle.
static const float kWaterRoughness = 0.05;

// Where water can stand at all: a surface whose normal points up by at least this cosine (about thirty
// degrees off level at most), with the gate closing over a short ramp past it. Water runs off anything
// steeper, and a gate that closed gradually would leave a glassy film climbing every wall.
static const float kPuddleLevelCosine = 0.86;
static const float kPuddleLevelSharpness = 12.0;

// How quickly the ground goes from dry to under water, per unit of the height field. Twenty puts the whole
// waterline inside a twentieth of the field's range: crisp enough to read as an edge, and still a ramp a
// pixel or two wide at a few metres, so the shore is anti-aliased rather than stair-stepped.
static const float kPuddleShoreSharpness = 20.0;

float WetHash(float2 cell)
{
    // The same bit mix the storm's own gust field uses, so the ground and the rain agree about where the
    // world is busy. A sine hash is periodic at exactly the coordinates a level occupies.
    uint2 c = uint2(int2(cell) + 1048576);
    uint h = (c.x * 73856093u) ^ (c.y * 19349663u);
    h ^= h >> 13; h *= 0x5BD1E995u; h ^= h >> 15;
    return float(h & 0xFFFFFFu) * (1.0 / float(0xFFFFFF));
}

// Three independent numbers from ONE mix. A cell of the ripple lattice needs a birth time and a
// two-dimensional offset, and the gather below visits four cells per layer and four layers per pixel: at
// three separate hashes a cell that is forty-eight rounds of the mix, where re-stirring one round's output
// is sixteen.
float3 WetHash3(float2 cell)
{
    uint2 c = uint2(int2(cell) + 1048576);
    uint h = (c.x * 73856093u) ^ (c.y * 19349663u);
    h ^= h >> 13; h *= 0x5BD1E995u; h ^= h >> 15;
    uint g = h * 0x27D4EB2Du; g ^= g >> 15;
    uint k = g * 0x85EBCA6Bu; k ^= k >> 13;
    return float3(h & 0xFFFFFFu, g & 0xFFFFFFu, k & 0xFFFFFFu) * (1.0 / float(0xFFFFFF));
}

// A rotation of the lattice, carried as its cosine and sine rather than as a matrix: the two callers
// want one turn and its identity, and a 2x2 written out avoids having to reason about whether a
// constructor fills rows or columns.
float2 WetRotate(float2 v, float2 rotation)
{
    return float2((rotation.x * v.x) - (rotation.y * v.y), (rotation.y * v.x) + (rotation.x * v.y));
}

// A lattice laid over the ground at the given spacing, in metres, addressed CAMERA-RELATIVE.
//
// This is the difference between water that ripples and water that is glass-flat. The levels sit tens of
// kilometres from the world origin, so an absolute world coordinate divided by half a metre lands past a
// hundred thousand, where a 32-bit float has a few centimetres of resolution and a fraction has about
// thirty distinct values. Every ring in the lattice then quantises to a handful of steps and the whole
// field averages out to nothing. Subtracting the camera first keeps the arithmetic near zero, and the
// lattice stays world-locked because the camera's own whole-cell part is carried alongside and only ever
// used to pick a hash.
struct WetGrid
{
    // the position within the lattice, near zero, exact.
    float2 uv;

    // the camera's whole-cell offset, added back when hashing.
    float2 base;
};

// The position handed in is CAMERA-RELATIVE and never absolute, which is the whole point: adding the
// camera's world position on and subtracting it again inside here would throw away every bit of
// precision this exists to keep. Only the lattice's origin needs to know where in the world the camera
// is, and that is a whole number of cells, which a float holds exactly.
//
// The rotation turns the LATTICE, not the sample: the camera's anchor is turned with it, so the field is
// a world lattice standing at an angle rather than a field that swims as the camera walks. Turning only
// one of the two is the classic way to build a noise that slides.
WetGrid WetGridAt(WetParams w, float3 relative, float size, float2 rotation)
{
    float2 camera = WetRotate(w.camera, rotation) / size;
    float2 cell = floor(camera);
    WetGrid grid;
    grid.uv = (WetRotate(relative.xy, rotation) / size) + (camera - cell);
    grid.base = cell;
    return grid;
}

WetGrid WetGridAt(WetParams w, float3 relative, float size)
{
    return WetGridAt(w, relative, size, float2(1.0, 0.0));
}

float WetNoise(float2 p, float2 base)
{
    float2 cell = floor(p);
    float2 f = p - cell;
    f = f * f * (3.0 - (2.0 * f));
    cell += base;
    float a = WetHash(cell);
    float b = WetHash(cell + float2(1.0, 0.0));
    float c = WetHash(cell + float2(0.0, 1.0));
    float d = WetHash(cell + float2(1.0, 1.0));
    return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
}

// The fine octave stands at an angle to the coarse one. Value noise interpolated across a square lattice
// has its features lined up with the lattice, and two octaves lined up the same way compound it: the
// puddles came out as rectangles with rounded corners, all of them the same way round, which is a shape
// no hollow in a road has ever had. A rotation costs two multiplies and is the whole of the fix. Roughly
// thirty-seven degrees, which shares no factor with a right angle, so the two lattices never re-align at
// any scale.
static const float2 kWetFineRotation = float2(0.8, 0.6);

// Where the ground is low. Two octaves and no more: a puddle is a hollow in a road, not a landscape, and
// the detail a third octave adds turns the water's edge into a fringe of speckle that reads as noise on
// the texture rather than as a shoreline.
//
// What the second octave does instead of being a third is WARP the first. Adding detail to a height field
// adds detail to its contours; displacing the field sideways bends them, so the shoreline wanders the way
// a real one does without gaining any frequency the mask cannot hold.
float PuddleHeight(WetParams w, float3 relative, float size)
{
    WetGrid fine = WetGridAt(w, relative, size * 0.46, kWetFineRotation);
    float detail = WetNoise(fine.uv + 11.3, fine.base);

    WetGrid coarse = WetGridAt(w, relative, size);
    float body = WetNoise(coarse.uv + ((detail - 0.5) * 0.4), coarse.base);

    return (body * 0.65) + (detail * 0.35);
}

// How far the water level stands above the ground here: positive under water, negative on dry land, in
// the height field's own units. Both the shoreline and the damp band around it are cut from this one
// number, which is what keeps them concentric.
float PuddleDepth(WetParams w, float3 relative)
{
    float size = max(w.puddleSize, 0.05);
    return clamp(w.coverage, 0.0, 1.0) - PuddleHeight(w, relative, size);
}

// The slope gate on its own: everything level enough to hold water.
float PuddleLevel(float3 normal)
{
    return clamp((normal.z - kPuddleLevelCosine) * kPuddleLevelSharpness, 0.0, 1.0);
}

// Where the water stands, 0 to 1. THE function the shading and the reflection must agree about: the
// reflection lands on exactly the water the shading drew because both ask this one question.
float PuddleMask(WetParams w, float3 relative, float3 normal)
{
    return clamp(PuddleDepth(w, relative) * kPuddleShoreSharpness, 0.0, 1.0)
        * PuddleLevel(normal) * w.amount;
}

// The ground immediately AROUND the water, which is wetter than the road at large. A puddle in a
// photograph is ringed by a band of ground that is dark and has no water standing on it -- the hollow's
// own sides, still draining. Without it the shoreline is the only edge in the picture and the water reads
// as a decal laid on dry tarmac; with it the eye is given the gradient it expects and stops asking. Cut
// from the same depth field as the water, which is what keeps the band concentric with the shoreline
// instead of merely near it.
//
// A band and not a second puddle: it reaches its full strength AT the waterline and falls away outward
// over about a sixth of the height field, so what it adds is a ring a hand's breadth or two wide, not a
// wash over every level surface in the level.
float PuddleShore(WetParams w, float3 relative, float3 normal)
{
    return clamp((PuddleDepth(w, relative) * 6.0) + 0.9, 0.0, 1.0) * PuddleLevel(normal);
}

// How wet a surface is that is not under standing water -- which is everything, because rain runs down a
// wall and darkens it, it just does not stand there. So the general damp keeps a share on anything not
// facing straight down, and only the puddles take the slope gate whole.
//
// Plus the band around each puddle, which is wetter still. Both passes need the same answer: the damp
// decides a surface's roughness, and a surface whose roughness the reflection disagrees about is one the
// reflection marches on and the shading does not, or the reverse.
float DampAmount(WetParams w, float3 relative, float3 normal)
{
    float facing = clamp((normal.z * 0.5) + 0.5, 0.0, 1.0);
    float damp = w.amount * lerp(0.25, 1.0, facing * facing);
    return clamp(damp + (PuddleShore(w, relative, normal) * w.amount * 0.45), 0.0, 1.0);
}

// Damp is not wet. An early version took the roughness down to a quarter, which is not a damp road -- it
// is a polished one, and under a lamp the whole yard came up as a sheet of glare brighter than it had
// been dry. Rain soaking into tarmac takes the edge off its roughness; it does not make glass of it. So
// the drop is proportional and floored well short of smooth, and only standing water is allowed past that
// floor. If the ground is shinier in the rain than it is dry, this is the line.
float DampRoughness(float roughness, float damp)
{
    return lerp(roughness, max(kDampRoughnessFloor, roughness * 0.75), damp);
}

// The rain on standing water: rings spreading from where drops strike.
//
// Each layer is a lattice laid on the ground with one strike per cell per cycle. A strike sends out a short
// train of waves -- a crest, a trough, a weaker crest -- that travels outward at a steady speed and dies
// away as it goes. Several layers at different spacings and slightly different rates are summed so that the
// strikes never fall into step and fine and coarse rings mix. Everything is returned as SLOPE, not as a
// normal: slopes add, where summing normals would average the rings flat.

// How steep a crest is, in height per unit of lattice. Project value.
static const float kRippleSlope = 0.45;

// How far a strike may land from its cell's centre, per axis, in cells. Project value.
static const float kRippleJitter = 0.25;

// How far a cell's centre may be from the pixel for its ring to be worth evaluating, in cells. Project value.
static const float kRippleReach = 0.78;

// How far a ring travels before it is gone, in cells. Project value.
static const float kRippleMaxRadius = 0.46;

// The wave train behind the front: four half-cycles (two full waves) inside a band a sixth of the ring's
// final radius wide. The band keeps its width as the ring grows; a whole number of half-cycles puts the
// profile at exactly zero at both of its edges, so there is no step at the front or on the flat water left
// inside it.
static const float kRippleTrainHalfCycles = 4.0;
static const float kRippleTrainSharpness = kRippleTrainHalfCycles * 6.0;

// How early in its cycle a ring dies when its layer is only just switched on. A full-weight layer lets a
// ring fade over its whole cycle; a faint one cuts it short, so drizzle shows small rings rather than large
// faint ones.
static const float kRippleEarliestDeath = 0.2;

static const float kRipplePi = 3.14159265;

// One layer of strikes. `uv` is the layer's lattice position (already offset so that no two layers share
// cell walls), `base` the camera's whole-cell part for hashing, `phase` where the layer is in its cycle.
float2 RippleLayer(float2 uv, float2 base, float phase, float weight)
{
    if (weight <= 0.0)
    {
        return float2(0.0, 0.0);
    }

    float death = lerp(kRippleEarliestDeath, 1.0, clamp(weight, 0.0, 1.0));
    float2 slope = float2(0.0, 0.0);

    // The four cells whose centres straddle the pixel. A ring is struck off-centre and grows to nearly a
    // whole cell, so it reaches into its neighbours: evaluating only the pixel's own cell cuts every ring at
    // the cell wall and draws the lattice on the water.
    float2 anchor = floor(uv - 0.5);
    for (int j = 0; j < 2; j++)
    {
        for (int i = 0; i < 2; i++)
        {
            float2 cell = anchor + float2(float(i), float(j));
            float2 fromCentre = uv - (cell + 0.5);
            if (dot(fromCentre, fromCentre) > kRippleReach * kRippleReach)
            {
                continue;
            }

            float3 h = WetHash3(cell + base);
            float t = frac(phase + h.x);
            float2 offset = (h.yz - 0.5) * (2.0 * kRippleJitter);
            float2 fromStrike = fromCentre - offset;
            float distance = length(fromStrike);

            // 1 at the strike, 0 at the farthest the ring reaches. The front is where t + g = 1.
            float g = 1.0 - clamp(distance / kRippleMaxRadius, 0.0, 1.0);
            float behind = clamp((t - 1.0 + g) * kRippleTrainSharpness, 0.0, kRippleTrainHalfCycles);
            float wave = sin(kRipplePi * behind);
            float envelope = clamp(death - t, 0.0, 1.0);

            float2 outward = distance > 1e-5 ? fromStrike / distance : float2(0.0, 0.0);
            slope -= outward * (wave * envelope * kRippleSlope);
        }
    }

    return slope;
}

// The four layers: spacing as a share of the ripple size, a fixed lattice offset, a rate and a phase, and
// the rain intensity at which each one starts. The first is always running; the rest come in one after
// another, so a drizzle rings a few slow circles and a downpour boils. All four are fully on by intensity 1.
static const float4 kRippleSpacing = float4(0.52, 0.74, 0.97, 1.28);
static const float4 kRippleRate = float4(0.96, 0.81, 0.89, 0.72);
static const float4 kRipplePhase = float4(0.0, 0.31, 0.58, 0.83);
static const float4 kRippleStart = float4(0.0, 0.22, 0.44, 0.66);
static const float kRippleRamp = 0.25;
static const float2 kRippleOffset[4] = {
    float2(0.0, 0.0), float2(0.43, 0.71), float2(0.79, 0.24), float2(0.17, 0.46) };

float2 RainRippleSlope(WetParams w, float3 relative, float size, float time, float intensity)
{
    float2 slope = float2(0.0, 0.0);
    [unroll]
    for (int k = 0; k < 4; k++)
    {
        float weight = clamp((intensity - kRippleStart[k]) / kRippleRamp, 0.0, 1.0);
        if (weight <= 0.0)
        {
            continue;
        }

        WetGrid grid = WetGridAt(w, relative, size * kRippleSpacing[k]);
        float phase = frac((time * kRippleRate[k]) + kRipplePhase[k]);
        slope += RippleLayer(grid.uv + kRippleOffset[k], grid.base, phase, weight);
    }

    return slope;
}

// The wind's own texture on the water: a slow drift of shallow waves, taken as the gradient of the same
// value noise rather than from a normal map, which is the same shape without the image.
float2 WindRippleSlope(WetParams w, float3 relative, float time)
{
    WetGrid grid = WetGridAt(w, relative, 1.8);
    float2 p = grid.uv + float2(time * 0.08, time * 0.05);
    const float stepSize = 0.15;
    float here = WetNoise(p, grid.base);
    return float2(
        WetNoise(p + float2(stepSize, 0.0), grid.base) - here,
        WetNoise(p + float2(0.0, stepSize), grid.base) - here);
}

// How wide a patch of ground one pixel covers, in metres. The pixel's angular width carried out to the
// surface and stretched by however far from square-on that surface is seen -- ground at a grazing angle
// is what a pixel covers most of. This is the number that decides whether a ripple is something the frame
// can draw or something it can only average, and both passes compute it the same way from their own
// resolution so that they agree about the water at every distance.
float WetFootprint(float ndcPerPixel, float viewZ, float xScale, float nDotV)
{
    float across = abs(ndcPerPixel * viewZ) / max(xScale, 1e-4);
    return across / max(nDotV, 0.08);
}

// The surface of the water itself: which way it faces and how sharply it reflects.
struct WaterSurface
{
    // camera-relative world, Z up, as the G-buffer normal is.
    float3 normal;

    float roughness;

    // How much of this pixel's reflection ONE ray can still speak for. A screen-space march fires a
    // single ray, which is a mirror about the normal above -- and that is the right answer for exactly as
    // long as the pixel still RESOLVES the ripples. Once it is averaging them, its reflection is a lobe
    // rather than a direction, and the single ray has to give way to the smooth sky the shading laid in
    // the same pixel or the far half of every puddle carries a hard, crawling reflection of a wall that
    // the surface itself says is a smear.
    //
    // Note that it is the UNRESOLVED ripples that do this and not the resolved ones: a tilt the pixel can
    // see is in the normal, and a mirror about a tilted normal is still a mirror.
    float mirror;
};

// Ripples finer than the pixel are roughness, not shape. A pixel that covers a dozen rings sees a dozen
// tilts at once, and a dozen tilts at once is a broad lobe -- not a mirror pointing in one direction.
// Take only the mean and you get a flat mirror that crawls and sparkles as the camera moves; take the
// variance as well and you get the soft, wide sheen that distant wet ground actually has. This is
// Toksvig's normal-variance-to-roughness, read off the ripple slope rather than off a normal map, and it
// is most of the difference between water and chrome: the far half of a puddle should go glossy and
// quiet, and it was staying a hard mirror all the way out to the horizon.
WaterSurface WaterSurfaceAt(WetParams w, float3 relative, float footprint)
{
    float size = max(w.rippleSize, 0.02);

    float2 slope = RainRippleSlope(w, relative, size, w.seconds * 0.6, w.rippleStrength);

    // The wind's contribution is deliberately far smaller than it looks like it should be. It is a smooth
    // field over metres where the rings are sharp over centimetres, so a wind term strong enough to see
    // on its own swamps every ring in the puddle and leaves one broad, slowly-swimming tilt -- water that
    // looks like a warped mirror rather than like water.
    slope += WindRippleSlope(w, relative, w.seconds) * w.windStrength * 1.6;

    // How much of the ripple lattice has fallen inside one pixel. Half a cell is where a ring stops being
    // a shape and starts being an average.
    float blur = clamp(footprint / (size * 0.5), 0.0, 1.0);

    WaterSurface surface;

    // What the pixel can still resolve tilts it; what it cannot is handed to the roughness.
    surface.normal = normalize(float3(slope * (1.0 - blur), 1.0));

    // Two sources, and they trade off against each other exactly: the tilt this pixel did resolve, which
    // broadens its own highlight a little because a crest is curved within the pixel; and the whole
    // lattice's typical slope, weighted by however much of it the pixel had to average away.
    float resolved = length(slope) * (1.0 - blur) * 0.10;
    float averaged = ((0.20 * w.rippleStrength) + (0.10 * w.windStrength)) * blur;

    surface.roughness = clamp(kWaterRoughness + resolved + averaged, kWaterRoughness, 0.32);

    // A mirror while the ripples are still a shape, giving way to the sky as they become an average.
    // Faster than the blur itself, because a pixel holding two rings is already past the point where the
    // one it happens to reflect about is the one it should have.
    surface.mirror = clamp(1.0 - (blur * 2.0), 0.0, 1.0);
    return surface;
}

// Schlick against water's own index of refraction: two per cent looking straight down into it, ALL of it
// at a grazing angle. Taken against the RIPPLED normal, which is what makes one edge of a ring catch the
// sky and the other not.
//
// The ninety-degree term is one and not the nine tenths it was. Water has no absorption to speak of at a
// glancing angle -- a wet road at the far end of the street is a mirror, and that is why it looks like
// that -- and capping it below one is a fudge that dims exactly the part of the effect that is real while
// leaving the part that was too bright (the sky counted twice) untouched.
float WaterFresnel(float3 normal, float3 toEye)
{
    float grazing = 1.0 - clamp(dot(normal, toEye), 0.0, 1.0);
    float squared = grazing * grazing;
    return 0.02 + (0.98 * squared * squared * grazing);
}

// How much of the sky a reflected ray can still see. Faded out only for a ray that has genuinely turned
// below the horizon, which is a couple of degrees wide: cutting further up than that throws away exactly
// the reflections that make a puddle read as water -- the near-horizontal ones, looked at along the
// surface, which is where the sky in a puddle is brightest and where anybody sees it.
float WaterSkyward(float3 reflected)
{
    return clamp(reflected.z * 20.0, 0.0, 1.0);
}

#endif
