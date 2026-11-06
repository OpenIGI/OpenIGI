// The lamp grid: which of the frame's lamps can reach each cell of the view, so a pixel walks only those.
//
// The view is cut into tiles of the screen and slices of depth, the slices spaced by the logarithm of the
// distance so a near cell is as small in the world as the lamps it holds and a far one as large. Lamp/Grid.slang
// lists, for every cell, the lamps whose reach sphere meets the cell's bounds; the shading and the lamp trace
// look a pixel's cell up and walk its list instead of every lamp. A lamp a cell leaves out is one whose reach
// window is nought everywhere in it, so the walk adds the same terms in the same order as walking them all.
//
// The buffer carries its own shape in a header, so neither reader's parameters change: a buffer with no grid in
// it -- a stand-in, where none was built -- says so, and the reader walks every lamp as before.
//
// The host declares the buffer as uLampGrid before including this.
#ifndef OPENIGI_LAMP_GRID_INCLUDED
#define OPENIGI_LAMP_GRID_INCLUDED

// A tile's edge in pixels, how many depth slices, and how many lamps a cell lists before it gives up and
// says "all": a cell that would list more walks every lamp, which is exact and only slower.
static const int kLampGridTile = 32;
static const int kLampGridSlices = 32;
static const int kLampGridCellLamps = 31;
static const uint kLampGridAll = 0xFFFFFFFFu;

// One count and the lamps after it.
static const uint kLampGridStride = uint(1 + kLampGridCellLamps);

// The header: tiles across, tiles down, slices, the nearest depth a slice starts at, slices per doubling of
// distance, tiles per unit of screen across and down, and a spare word. The cells follow it.
static const uint kLampGridHeader = 8u;

// The cell a view position at a screen fraction falls in, or false where there is no grid to ask.
bool LampGridCell(float2 uv, float viewZ, out uint cell)
{
    cell = 0u;
    uint tilesX = uLampGrid[0];
    uint tilesY = uLampGrid[1];
    if (tilesX == 0u || tilesY == 0u)
    {
        return false;
    }

    uint slices = uLampGrid[2];
    float nearest = asfloat(uLampGrid[3]);
    float slicesPerOctave = asfloat(uLampGrid[4]);
    float2 tilesPerUv = float2(asfloat(uLampGrid[5]), asfloat(uLampGrid[6]));

    uint tx = uint(clamp(int(floor(uv.x * tilesPerUv.x)), 0, int(tilesX) - 1));
    uint ty = uint(clamp(int(floor(uv.y * tilesPerUv.y)), 0, int(tilesY) - 1));
    int slice = viewZ <= nearest ? 0 : int(floor(log2(viewZ / nearest) * slicesPerOctave));
    uint sz = uint(clamp(slice, 0, int(slices) - 1));

    cell = (((sz * tilesY) + ty) * tilesX) + tx;
    return true;
}

// Where a cell's list starts, and how many lamps it holds; false where the cell lists them all.
bool LampGridList(uint cell, out uint first, out uint count)
{
    uint at = kLampGridHeader + (cell * kLampGridStride);
    count = uLampGrid[at];
    first = at + 1u;
    return count != kLampGridAll;
}

// The j-th lamp of a list begun at first.
int LampGridLamp(uint first, uint j)
{
    return int(uLampGrid[first + j]);
}

#endif
