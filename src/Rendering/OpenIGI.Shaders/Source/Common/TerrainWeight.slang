// The share of the ground a terrain material pass carries at a vertex, given the level-of-detail fade.
//
// Two encodings, told apart by the colour. A vertex the file stores is white and keeps a selector in
// its alpha, in thirds: nothing, the fade, its complement, or one -- for how this vertex's material
// relates to the pass's (see TerrainChunkMesh). A vertex the smoothing refinement made is a blend of
// several of those, and halfway between two selectors is no selector, so it carries the weight as
// c0 + c1 * fade instead: c0 in red, the positive part of c1 in green and its negative part in blue,
// which are never both set, so such a vertex is never white (see TerrainChunkMesh.Smooth).
//
// colorBgra is the vertex colour as stored: x is blue, y green, z red, w alpha.
float TerrainPassWeight(float4 colorBgra, float morph)
{
    if (colorBgra.x > 0.999 && colorBgra.y > 0.999 && colorBgra.z > 0.999)
    {
        float selector = colorBgra.w * 3.0;
        return selector < 0.5 ? 0.0 : (selector < 1.5 ? morph : (selector < 2.5 ? 1.0 - morph : 1.0));
    }

    return colorBgra.z + (colorBgra.y - colorBgra.x) * morph;
}
