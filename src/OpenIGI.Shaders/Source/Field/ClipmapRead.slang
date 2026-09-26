// Reads one clipmap level back into a buffer, byte for byte as it was stored.
//
// A diagnostic rather than a render pass: every voxel of a level is read so that the validation can
// compare it against the exact field. It costs a readback and a few hundred thousand comparisons, which
// is affordable because it runs when asked rather than every frame -- and worth it because a fault
// confined to one corner of the volume is exactly the kind a sample would miss.

#include "../Common/ComputeBindings.slang"

OPENIGI_COMPUTE_IMAGE(0) [[vk::image_format("r8")]] RWTexture3D<float> uClipmaps;

OPENIGI_COMPUTE_BUFFER(0) RWStructuredBuffer<uint> uValues;

struct Globals
{
    int uResolution;
    int uLevel;
};

OPENIGI_COMPUTE_GLOBALS ConstantBuffer<Globals> g;

[shader("compute")]
[numthreads(4, 4, 4)]
void computeMain(uint3 id : SV_DispatchThreadID)
{
    int3 at = int3(id);

    if (any(at >= int3(g.uResolution, g.uResolution, g.uResolution)))
    {
        return;
    }

    int index = (((at.z * g.uResolution) + at.y) * g.uResolution) + at.x;

    // Back through the byte range it was stored in, so the comparison is against what was written rather
    // than a float that rounded on the way out.
    uValues[index] = uint(round(
        uClipmaps[int3(at.xy, (g.uLevel * g.uResolution) + at.z)] * 255.0));
}
