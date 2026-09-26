// Is there a roof over this? Read off a top-down raster of the level's solid geometry, one texel per
// half-metre or so of ground, holding the height of the highest surface standing over that square --
// see ShelterHeightField, which builds it, and ShelterMapTransform, which is this struct on the other
// side of the wire.
//
// Split out from the water so a pass can ask the question without compiling the weather. The glazing's
// reflection needs it and has no water in it at all: a reflected ray that runs off the end of the
// distance field is given the sky, which is right out of doors and badly wrong under a roof -- the
// clipmaps cannot resolve a ceiling a few metres up, so indoors the march loses it and hands back open
// sky instead. By day that is a bright patch where the ceiling should be; at night the sky is deep blue
// and nothing else in the level is, so a third of every pane came back blue.
//
// Nothing here binds anything. A pass that uses it declares the map it reads and builds the struct from
// its own parameters.

#ifndef OPENIGI_SHELTER_INCLUDED
#define OPENIGI_SHELTER_INCLUDED

// Tested against the SURFACE and not against the camera: the player standing in a doorway must not dry
// the courtyard, and standing in the courtyard must not wet the room behind them.
//
// Why a picture rather than a list of boxes. This was the bounding boxes of the tasks the level happened
// to name "Building_", looped over per pixel. A building standing at an angle to the world axes fills
// barely a third of its own box, so the box dried the open apron in front of it -- a quarter of the
// ground inside those boxes on mission one has no roof over it at all -- while two fifths of the ground
// that IS under a roof lay outside every box, because whatever sheltered it had never been labelled a
// building, and its floor pooled with water indoors.
struct ShelterMap
{
    // the world XY, in metres, of the low corner of texel zero.
    float2 origin;

    // texels per metre.
    float invCell;

    // the height, in metres, the packed value counts up from.
    float lowestZ;

    // how many metres one step of the packed value stands for.
    float scale;

    // how far over a surface the roof must be for the surface to be sheltered.
    float headroom;

    // how far over a surface a roof may be and still be its roof.
    float reach;

    // how many texels across and down.
    float2 size;

    // zero on a level with no map, where nothing is ever sheltered.
    float present;
};

// Which texel a world position falls in. Outside the map is a negative coordinate or one past the edge,
// which ShelterHolds rejects rather than clamping: clamping would smear the map's border row across the
// whole rest of the level.
int2 ShelterTexel(ShelterMap m, float2 worldXY)
{
    return int2(floor((worldXY - m.origin) * m.invCell));
}

bool ShelterHolds(ShelterMap m, int2 texel)
{
    return m.present > 0.5
        && texel.x >= 0 && texel.y >= 0
        && texel.x < int(m.size.x) && texel.y < int(m.size.y);
}

// The height a fetched texel stands for. Sixteen bits across the blue and green channels, because a
// level's Z spans kilometres and eight bits of that is a step taller than a person; zero is the sentinel
// for open sky, so a real height counts from one. The fetch is a Load on the calling side and never a
// sample: filtering would mix the low byte into the high one and invent heights halfway up the level.
float ShelterSurface(ShelterMap m, float4 texel)
{
    float packed = floor((texel.b * 255.0) + 0.5) + (floor((texel.g * 255.0) + 0.5) * 256.0);
    return packed < 0.5 ? -1.0e30 : m.lowestZ + ((packed - 1.0) * m.scale);
}

// The comparison is against a surface ABOVE the one being shaded, which is what makes a roof shelter the
// floor under it and not itself: a roof's own upper face is the height the map holds, fails the test,
// and goes on collecting rain like anything else out in the open.
bool ShelterCovers(ShelterMap m, float surfaceZ, float worldZ)
{
    return surfaceZ > worldZ + m.headroom && surfaceZ < worldZ + m.reach;
}

// Whether a climbing ray will run into a ceiling the distance field cannot hold.
//
// The clipmaps are quarter-metre voxels at best and coarser further out; a ceiling is a thin sheet a few
// metres up, so a march goes straight through it and runs out of field on the far side. Answering that
// with the sky paints daylight -- or, at night, a deep blue -- across the ceiling of every interior. The
// roof raster is what the shading and the water already use to decide what is indoors, and it answers
// this too.
//
// The position is ABSOLUTE world, because the map is: a caller working camera-relative adds the camera
// back on first.
bool LosesCeiling(ShelterMap m, Texture2D<float4> map, float2 worldXY, float worldZ, float3 rayW)
{
    int2 at = ShelterTexel(m, worldXY);
    if (!ShelterHolds(m, at))
    {
        return false;
    }

    // Out of doors to begin with: nothing overhead to lose.
    //
    // ShelterCovers is not the test, and the difference matters. It caps how far above a surface a roof
    // may be and still shelter it, because that is the right question for RAIN -- a roof thirty metres up
    // does not keep the floor dry in a storm. It is the wrong question here: a reflected ray climbing
    // inside a hall meets that roof whatever its height. Capped, this fired on a sixth of the rays it
    // should have.
    float roof = ShelterSurface(m, map.Load(int3(at, 0)));
    if (roof <= worldZ + m.headroom)
    {
        return false;
    }

    // Indoors, but the ray is not climbing. It leaves through a wall, a doorway or the glazing it started
    // from, and out there the sky is a real answer -- which is not a quibble: at the viewpoint this was
    // measured from, the near-horizontal rays off the lower panes are a third of the whole pane and the
    // traced reference resolves every one of them to sky. A blanket "this pane is indoors, no sky" rule
    // turns all of it grey.
    if (rayW.z <= 0.05)
    {
        return false;
    }

    // Where it would meet that ceiling, and whether there is still a roof overhead by the time it gets
    // there. One fetch, and exact for a flat roof: a climbing ray inside a hall either reaches the
    // ceiling or crosses out over the footprint before it can.
    float travel = (roof - worldZ) / rayW.z;
    int2 above = ShelterTexel(m, worldXY + (rayW.xy * travel));
    if (!ShelterHolds(m, above))
    {
        return false;
    }

    return ShelterSurface(m, map.Load(int3(above, 0))) > worldZ + m.headroom;
}

#endif
