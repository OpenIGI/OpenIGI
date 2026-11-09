// The original's ground drawn the original's way: a draw per texture per pass rather than per chunk.
//
// Project I.G.I. transforms every visible chunk on the processor into one vertex buffer and sorts the
// triangles by texture, so a frame's ground is a few dozen draws (TerrainMesh_DrawD3D: the base layers,
// then the overlays, then the lightmap pages). Drawn a chunk at a time it was thousands, and on a slow
// processor -- a browser above all -- that is most of the frame.
//
// This is the forward mesh shader in that form: the chunks sit in one arena, each texture's triangles in
// one index list, and the vertex stage takes its chunk's transform, ramp and eye offset from a table the
// frame writes instead of from the draw's block. Everything else is the block, and the code that shades
// is the forward shader's own.

#define OPENIGI_TERRAIN_ARENA 1
#include "Mesh3D.slang"
