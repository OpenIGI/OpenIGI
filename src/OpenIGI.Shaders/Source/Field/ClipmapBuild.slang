// Resamples the level's per-mesh distance fields into one clipmap level.
//
// Each voxel asks the cells near it which meshes reach it, and takes the nearest surface any of them
// reports. The per-mesh fields live in one atlas and each instance carries its own placement, so a mesh
// that stands in a level twice is stored once.

#include "../Common/ComputeBindings.slang"

OPENIGI_COMPUTE_IMAGE(0) [[vk::image_format("r8")]] WTexture3D<float4> uClipmaps;

// The per-mesh fields, packed into one volume. Bound by the texture unit the parameter of the same name
// holds, which is how the seam's default-block samplers arrive.
OPENIGI_COMPUTE_TEXTURE(0) Sampler3D<float4> uAtlas;

OPENIGI_COMPUTE_BUFFER(0) StructuredBuffer<float4> uInstances;
OPENIGI_COMPUTE_BUFFER(1) StructuredBuffer<int> uCellOffsets;
OPENIGI_COMPUTE_BUFFER(2) StructuredBuffer<int> uCellIndices;

struct Globals
{
    int uResolution;
    int uLevel;
    int uCellsPerAxis;
    float uVoxelSize;
    float uCellSize;
    float uRange;
    float3 uOrigin;

    // The texture unit uAtlas is bound through. Named for the sampler, which is what makes it one.
    int uAtlas;
};

OPENIGI_COMPUTE_GLOBALS ConstantBuffer<Globals> g;

// Seven vectors per instance: three rows of its placement, the slot it occupies in the atlas, that
// slot's extent, where its own field starts, and how big one of its voxels is.
static const int kFloatsPerInstance = 7;

// How far the surface is from a point, as one instance's field describes it -- or a fair lower bound
// where the point is outside that field, which is the distance to the field's own edge. A field only
// covers its mesh's box, and a point beyond it is at least that far from anything the mesh holds.
float SampleInstance(int instance, float3 world)
{
    int at = instance * kFloatsPerInstance;
    float4 row0 = uInstances[at + 0];
    float4 row1 = uInstances[at + 1];
    float4 row2 = uInstances[at + 2];
    float4 slotCorner = uInstances[at + 3];
    float4 slotSize = uInstances[at + 4];
    float4 fieldOrigin = uInstances[at + 5];
    float4 voxelSize = uInstances[at + 6];

    if (slotCorner.w <= 0.0)
    {
        return 1e6;
    }

    float3 offset = world - float3(row0.w, row1.w, row2.w);
    float3 local = float3(dot(row0.xyz, offset), dot(row1.xyz, offset), dot(row2.xyz, offset));
    float3 voxel = (local - fieldOrigin.xyz) / voxelSize.xyz;
    float3 clamped = clamp(voxel, float3(0.5, 0.5, 0.5), slotSize.xyz - float3(0.5, 0.5, 0.5));

    uint width;
    uint height;
    uint depth;
    uAtlas.GetDimensions(width, height, depth);
    float3 uvw = (slotCorner.xyz + clamped) / float3(float(width), float(height), float(depth));

    float encoded = uAtlas.Sample(uvw).r;
    float distance = ((encoded * 2.0) - 1.0) * slotCorner.w;
    float beyond = length((clamped - voxel) * voxelSize.xyz);
    return beyond > 0.0 ? beyond : distance;
}

[shader("compute")]
[numthreads(4, 4, 4)]
void computeMain(uint3 id : SV_DispatchThreadID)
{
    int3 at = int3(id);

    if (any(at >= int3(g.uResolution, g.uResolution, g.uResolution)))
    {
        return;
    }

    float3 world = g.uOrigin + ((float3(at) + 0.5) * g.uVoxelSize);
    int3 cell = clamp(
        int3(floor((world - g.uOrigin) / g.uCellSize)),
        int3(0, 0, 0),
        int3(g.uCellsPerAxis - 1, g.uCellsPerAxis - 1, g.uCellsPerAxis - 1));

    int cellIndex = (((cell.z * g.uCellsPerAxis) + cell.y) * g.uCellsPerAxis) + cell.x;
    int first = uCellOffsets[cellIndex];
    int last = uCellOffsets[cellIndex + 1];

    float nearest = g.uRange;
    for (int i = first; i < last; i++)
    {
        nearest = min(nearest, SampleInstance(uCellIndices[i], world));
    }

    float encoded = (clamp(nearest / g.uRange, -1.0, 1.0) * 0.5) + 0.5;
    uClipmaps.Store(
        int3(at.xy, (g.uLevel * g.uResolution) + at.z), float4(encoded, 0.0, 0.0, 0.0));
}
