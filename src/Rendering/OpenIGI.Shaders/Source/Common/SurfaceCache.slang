// Which card page covers a point, and where on it.
//
// One copy, because every pass that reads the cache has to agree about it. The card layout's own numbers
// describe how the pages were WRITTEN -- how many vectors a page record spans, how far outside its
// rectangle a point may stray and still belong to a card, and how close a candidate's recorded depth must
// come before the search stops looking at the rest. A reader that disagrees with the builder about any of
// them does not fail; it quietly reads the wrong card, which looks like a reflection landing on the wrong
// wall rather than like a bug.
//
// The host must declare before including: StructuredBuffer<float4> uPages, StructuredBuffer<int2> uCells
// and StructuredBuffer<int> uCellPages; Texture2D<float4> uAlbedo and uDepth, each with its sampler; and
// the macros uLookupBuckets, uLookupMin, uLookupCell and uAtlasSize. A host that also defines
// kCardLighting and kCardLightingSampler gets SampleCardsWithin.

#ifndef OPENIGI_SURFACE_CACHE_INCLUDED
#define OPENIGI_SURFACE_CACHE_INCLUDED

static const int kVectorsPerPage = 10;
static const float kCardSkin = 0.25;
static const float kCardSettled = 0.02;

// Which bucket a cell is filed under. Teschner's three primes, one per axis; must match
// SurfaceCacheLookup.Bucket exactly, or a page is filed where no lookup will look for it.
//
// Hashed rather than laid out in a box because a level is not a box: its geometry sits in a few dense
// clumps kilometres apart, with strays tens of kilometres further still, and a box grid has to stretch
// one cell size over all of it. See SurfaceCacheLookup.CellMetres for what that cost.
int LookupBucket(int3 cell)
{
    uint hash = (uint(cell.x) * 0x8DA6B343u)
        ^ (uint(cell.y) * 0xD8163841u)
        ^ (uint(cell.z) * 0xCB1AB31Fu);
    return int(hash & uint(uLookupBuckets - 1));
}

// A point taken onto one page's card: where across the rectangle it falls, in [-1, 1] each way when
// inside; how far behind the card's plane it sits; the card's own depth and facing; and a direction
// carried into the same space, for a caller that has a surface normal to judge by.
//
// Every lookup below goes through this, which is the point of it: there is one projection, so the
// reflections and the gather cannot disagree about which card a point falls on.
void ProjectOntoCard(
    int page,
    float3 world,
    float3 directionW,
    out float2 across,
    out float2 halfSize,
    out float behind,
    out float depth,
    out float3 facing,
    out float3 directionLocal,
    out float4 litRect,
    out float4 cardPage)
{
    litRect = uPages[(page * kVectorsPerPage) + 0];
    cardPage = uPages[(page * kVectorsPerPage) + 1];
    float4 centreAndHalfWidth = uPages[(page * kVectorsPerPage) + 2];
    float4 rightAndHalfHeight = uPages[(page * kVectorsPerPage) + 3];
    float4 upAndDepth = uPages[(page * kVectorsPerPage) + 4];
    float4 direction = uPages[(page * kVectorsPerPage) + 5];
    float4 row0 = uPages[(page * kVectorsPerPage) + 6];
    float4 row1 = uPages[(page * kVectorsPerPage) + 7];
    float4 row2 = uPages[(page * kVectorsPerPage) + 8];

    // Back into the placement's own space. The rows carry model-to-world and the lighting applies them
    // transposed, so undoing that is the untransposed multiply.
    float3 delta = world - float3(row0.w, row1.w, row2.w);
    float3 local = float3(dot(row0.xyz, delta), dot(row1.xyz, delta), dot(row2.xyz, delta));
    directionLocal = float3(
        dot(row0.xyz, directionW), dot(row1.xyz, directionW), dot(row2.xyz, directionW));
    float3 relative = local - centreAndHalfWidth.xyz;

    halfSize = max(float2(centreAndHalfWidth.w, rightAndHalfHeight.w), float2(1e-6, 1e-6));
    across = float2(dot(relative, rightAndHalfHeight.xyz), dot(relative, upAndDepth.xyz)) / halfSize;

    // How far behind the plane the point is. The capture looked along -Direction, so a surface further
    // from the plane is more negative along it.
    behind = -dot(relative, direction.xyz);
    depth = upAndDepth.w;
    facing = direction.xyz;
}

// How many ints one moving object's entry in the movers' list takes: its bounding sphere's centre and
// radius as float bits, then where its pages start in uCellPages and how many there are. Must match
// SurfaceCacheLookup.MoverHeaderInts.
static const int kMoverHeaderInts = 6;

// The pages of one moving object, or none where the point lies outside it. The movers' bucket -- the one
// past the hashed ones -- lists the objects rather than their pages, so a lookup tests one sphere per
// object instead of walking every page of every vehicle, door and fan in the level: that walk was half
// of what every traced bounce cost.
int2 MoverPages(int headers, int index, float3 world, float bias)
{
    int at = headers + (index * kMoverHeaderInts);
    float3 centre = float3(asfloat(uCellPages[at]), asfloat(uCellPages[at + 1]), asfloat(uCellPages[at + 2]));
    float reach = asfloat(uCellPages[at + 3]) + bias;
    float3 toCentre = world - centre;

    return dot(toCentre, toCentre) > reach * reach
        ? int2(0, 0)
        : int2(uCellPages[at + 4], uCellPages[at + 5]);
}

// The grid narrows the field to a handful of candidates; the work here is deciding between them, because
// a point on a wall sits inside the box of every card of that wall and of whatever stands against it.
// Each candidate is taken back into its own placement's space, projected onto its card, and rejected if
// it falls outside the rectangle. What survives is judged on DEPTH: the card recorded how far behind its
// plane the surface was, so the candidate whose recorded depth best matches where the point actually is
// is the one whose geometry the point belongs to. Without that test a floor reads as the underside of
// the ceiling above it.
bool LookupCardWithin(
    float3 world,
    float3 arrivingW,
    float skin,
    out float2 found,
    out float2 litFound,
    out float agreement,
    out float cardDepth)
{
    cardDepth = 0.0;
    found = float2(0.0, 0.0);
    litFound = float2(0.0, 0.0);
    agreement = 1e9;

    if (uLookupBuckets <= 0)
    {
        return false;
    }

    int3 cell = int3(floor((world - uLookupMin) / uLookupCell));
    int2 range = uCells[LookupBucket(cell)];

    uint albedoWidth;
    uint albedoHeight;
    uAlbedo.GetDimensions(albedoWidth, albedoHeight);
    float2 albedoSize = float2(float(albedoWidth), float(albedoHeight));

    bool anyFound = false;
    bool settled = false;
    // The point's own cell, then each moving object it falls within. A page that moves is still filed
    // in the grid where it stood at load, so the cell's walk passes over it and the object's finds it
    // wherever its record now puts it. See MoverPages.
    int2 movers = uCells[uLookupBuckets];
    for (int walk = 0; walk <= movers.y && !settled; walk++)
    {
        int2 span = walk == 0 ? range : MoverPages(movers.x, walk - 1, world, 0.0);
        for (int slot = 0; slot < span.y && !settled; slot++)
        {
            int page = uCellPages[span.x + slot];
            if ((uPages[(page * kVectorsPerPage) + 5].w > 0.5) == (walk == 0))
            {
                continue;
            }

            float4 bound = uPages[(page * kVectorsPerPage) + 9];
            float3 toCentre = world - bound.xyz;
            if (dot(toCentre, toCentre) > bound.w * bound.w)
            {
                continue;
            }

            float2 across;
            float2 halfSize;
            float behind;
            float depth;
            float3 facing;
            float3 arrivingLocal;
            float4 litRect;
            float4 cardPage;
            ProjectOntoCard(
                page,
                world,
                -arrivingW,
                across,
                halfSize,
                behind,
                depth,
                facing,
                arrivingLocal,
                litRect,
                cardPage);

            if (abs(across.x) > 1.0 || abs(across.y) > 1.0)
            {
                continue;
            }

            // A ray can only have struck the face of a surface that looks back along it. A card whose
            // capture looked the other way photographed the far side of whatever the ray hit: the inside
            // of a wall the ray met from outside. Within the skin the two faces of a thin wall are equally
            // good matches on depth, so without this a ray arriving at a sealed building's outside wall
            // could come back with the lamp-lit inside of it. Nothing to test for a point that no ray
            // arrived at.
            if (dot(arrivingW, arrivingW) > 0.5 && dot(facing, arrivingLocal) < -0.05)
            {
                continue;
            }

            if (behind < -skin || behind > depth + skin)
            {
                continue;
            }

            float2 uv = (across * 0.5) + 0.5;
            float2 texel = (cardPage.xy + (uv * cardPage.zw)) / albedoSize;
            if (uAlbedo.SampleLevel(uAlbedoSampler, texel, 0.0).a < 0.5)
            {
                continue;
            }

            float recorded = uDepth.SampleLevel(uDepthSampler, texel, 0.0).r * depth;
            float error = abs(recorded - behind);
            if (error < agreement)
            {
                agreement = error;
                cardDepth = depth;
                found = texel;
                settled = error < kCardSettled;
                litFound = (litRect.xy + (uv * litRect.zw)) / uAtlasSize.xy;
                anyFound = true;
            }
        }
    }

    return anyFound;
}

bool LookupCard(float3 world, out float2 found, out float2 litFound, out float agreement)
{
    float depth;
    return LookupCardWithin(world, float3(0.0, 0.0, 0.0), kCardSkin, found, litFound, agreement, depth);
}

// The card a RAY struck, which is the lookup above told which way the ray was travelling, so that it
// can only answer with a face the ray could have hit. Every gather's hit goes through this one.
bool LookupCardHit(float3 world, float3 rayDirection, out float2 found, out float2 litFound, out float agreement)
{
    float depth;
    return LookupCardWithin(world, rayDirection, kCardSkin, found, litFound, agreement, depth);
}

#ifdef kCardLighting

// Every card whose capture LOOKED the way this surface faces, weighted by how squarely it did, by how
// well its recorded depth agrees, and bilinearly within each. The normal is what makes this the right
// answer for a wall rather than for whatever card happens to enclose it: the card that photographed a
// surface is the one that looked the way it faces.
bool SampleCardsWithin(float3 world, float3 normalW, float bias, out float3 radiance)
{
    float3 albedo;
    return SampleCardsAndAlbedoWithin(world, normalW, bias, radiance, albedo);
}

// The same, and the albedo the cards photographed there under the same weights. Radiance over albedo is
// the light arriving at that spot -- which is what a surface standing there that the cache never baked,
// a monitor on a desk, needs to be lit by the same lamps and bounce as the desk.
bool SampleCardsAndAlbedoWithin(
    float3 world, float3 normalW, float bias, out float3 radiance, out float3 albedo)
{
    radiance = float3(0.0, 0.0, 0.0);
    albedo = float3(0.0, 0.0, 0.0);

    if (uLookupBuckets <= 0)
    {
        return false;
    }

    int3 cell = int3(floor((world - uLookupMin) / uLookupCell));
    int2 range = uCells[LookupBucket(cell)];

    uint albedoWidth;
    uint albedoHeight;
    uAlbedo.GetDimensions(albedoWidth, albedoHeight);
    float2 albedoSize = float2(float(albedoWidth), float(albedoHeight));

    float weightSum = 0.0;
    // The point's own cell, then each moving object it falls within. A page that moves is still filed
    // in the grid where it stood at load, so the cell's walk passes over it and the object's finds it
    // wherever its record now puts it. See MoverPages.
    int2 movers = uCells[uLookupBuckets];
    for (int walk = 0; walk <= movers.y; walk++)
    {
        int2 span = walk == 0 ? range : MoverPages(movers.x, walk - 1, world, bias);
        for (int slot = 0; slot < span.y; slot++)
        {
            int page = uCellPages[span.x + slot];
            if ((uPages[(page * kVectorsPerPage) + 5].w > 0.5) == (walk == 0))
            {
                continue;
            }

            float4 bound = uPages[(page * kVectorsPerPage) + 9];
            float3 toCentre = world - bound.xyz;
            float reach = bound.w + bias;
            if (dot(toCentre, toCentre) > reach * reach)
            {
                continue;
            }

            float2 across;
            float2 halfSize;
            float behind;
            float depth;
            float3 facing;
            float3 normalLocal;
            float4 litRect;
            float4 cardPage;
            ProjectOntoCard(
                page,
                world,
                normalW,
                across,
                halfSize,
                behind,
                depth,
                facing,
                normalLocal,
                litRect,
                cardPage);

            // Only a card that looked at the surface's front can have photographed it, and the more
            // squarely it looked the more its texels are worth.
            float towards = dot(facing, normalLocal);
            if (towards <= 0.0)
            {
                continue;
            }

            float squareness = towards * towards;

            // Inside the card's volume, give or take half the bias in every direction.
            float slack = 0.5 * bias;
            float2 acrossSlack = slack / halfSize;
            if (abs(across.x) > 1.0 + acrossSlack.x || abs(across.y) > 1.0 + acrossSlack.y)
            {
                continue;
            }

            if (behind < -slack || behind > depth + slack)
            {
                continue;
            }

            // The footprint an ordinary bilinear fetch at this point of the card would take: the texel
            // whose centre lies at or below it on each axis and the next one on, weighted by the fraction
            // between their centres.
            float2 uv = (clamp(across, float2(-1.0, -1.0), float2(1.0, 1.0)) * 0.5) + 0.5;
            float2 atlasAt = cardPage.xy + (uv * cardPage.zw) - 0.5;
            float2 lowCorner = floor(atlasAt);
            float2 fraction = atlasAt - lowCorner;

            // The same footprint in the lighting atlas, which lays each card out the same way at its own
            // scale.
            float2 litScale = litRect.zw / max(cardPage.zw, float2(1e-6, 1e-6));

            float ownDepth = behind / max(depth, 1e-6);
            float tolerance = bias / max(0.5 * depth, 1e-6);

            float4 taps = float4(0.0, 0.0, 0.0, 0.0);
            float3 tapLight[4];
            float3 tapAlbedo[4];
            for (int tap = 0; tap < 4; tap++)
            {
                float2 step = float2(float(tap & 1), float(tap >> 1));

                // Sampled exactly at the texel's centre, which every filter answers with that texel alone.
                float2 centre = (lowCorner + step + 0.5) / albedoSize;
                float4 photographed = uAlbedo.SampleLevel(uAlbedoSampler, centre, 0.0);
                float written = photographed.a >= 0.5 ? 1.0 : 0.0;
                tapAlbedo[tap] = photographed.rgb;
                float captured = uDepth.SampleLevel(uDepthSampler, centre, 0.0).r;

                // Full trust while the recorded depth agrees with the point's own to within the tolerance,
                // none once it is a quarter of the tolerance further out.
                float agree =
                    1.0 - saturate((abs(ownDepth - captured) - tolerance) / (0.25 * tolerance));

                float2 bilinear2 = lerp(1.0 - fraction, fraction, step);
                taps[tap] = bilinear2.x * bilinear2.y * written * agree;

                float2 litTexel =
                    floor(litRect.xy + ((lowCorner + step + 0.5 - cardPage.xy) * litScale));
                float2 litCentre = (litTexel + 0.5) / uAtlasSize.xy;
                tapLight[tap] = kCardLighting.SampleLevel(kCardLightingSampler, litCentre, 0.0).rgb;
            }

            float coverage = taps.x + taps.y + taps.z + taps.w;
            float cardWeight = squareness * coverage;
            if (cardWeight <= 0.0)
            {
                continue;
            }

            float3 lighting = ((tapLight[0] * taps.x) + (tapLight[1] * taps.y)
                + (tapLight[2] * taps.z) + (tapLight[3] * taps.w)) / coverage;
            radiance += max(lighting, float3(0.0, 0.0, 0.0)) * cardWeight;
            albedo += (((tapAlbedo[0] * taps.x) + (tapAlbedo[1] * taps.y)
                + (tapAlbedo[2] * taps.z) + (tapAlbedo[3] * taps.w)) / coverage) * cardWeight;
            weightSum += cardWeight;
        }
    }

    if (weightSum <= 0.0)
    {
        radiance = float3(0.0, 0.0, 0.0);
        return false;
    }

    radiance /= weightSum;
    albedo /= weightSum;
    return true;
}

#endif

#endif
