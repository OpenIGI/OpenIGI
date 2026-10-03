struct Globals_std140_0
{
    @align(16) uResolution_0 : i32,
    @align(4) uLevel_0 : i32,
    @align(8) uCellsPerAxis_0 : i32,
    @align(4) uVoxelSize_0 : f32,
    @align(16) uCellSize_0 : f32,
    @align(4) uRange_0 : f32,
    @align(16) uOrigin_0 : vec3<f32>,
    @align(4) uAtlas_0 : i32,
};

@binding(49) @group(0) var<uniform> g_0 : Globals_std140_0;
@binding(17) @group(0) var<storage, read> uCellOffsets_0 : array<i32>;

@binding(18) @group(0) var<storage, read> uCellIndices_0 : array<i32>;

@binding(16) @group(0) var<storage, read> uInstances_0 : array<vec4<f32>>;

@binding(32) @group(0) var uAtlas_1 : texture_3d<f32>;

@binding(64) @group(0) var uAtlasSampler_0 : sampler;

@binding(0) @group(0) var uClipmaps_0 : texture_storage_3d<rgba32float, write>;

fn ComputeSample_0( texture_0 : texture_3d<f32>,  sampler_0 : sampler,  uvw_0 : vec3<f32>) -> vec4<f32>
{
    return (textureSampleLevel((texture_0), (sampler_0), (uvw_0), (0.0f)));
}

fn SampleInstance_0( instance_0 : i32,  world_0 : vec3<f32>) -> f32
{
    var at_0 : i32 = instance_0 * i32(7);
    var row0_0 : vec4<f32> = uInstances_0[at_0];
    var row1_0 : vec4<f32> = uInstances_0[at_0 + i32(1)];
    var row2_0 : vec4<f32> = uInstances_0[at_0 + i32(2)];
    var slotCorner_0 : vec4<f32> = uInstances_0[at_0 + i32(3)];
    var slotSize_0 : vec4<f32> = uInstances_0[at_0 + i32(4)];
    var fieldOrigin_0 : vec4<f32> = uInstances_0[at_0 + i32(5)];
    var voxelSize_0 : vec4<f32> = uInstances_0[at_0 + i32(6)];
    var _S1 : f32 = slotCorner_0.w;
    if(_S1 <= 0.0f)
    {
        return 1.0e+06f;
    }
    var offset_0 : vec3<f32> = world_0 - vec3<f32>(row0_0.w, row1_0.w, row2_0.w);
    var _S2 : vec3<f32> = voxelSize_0.xyz;
    var voxel_0 : vec3<f32> = (vec3<f32>(dot(row0_0.xyz, offset_0), dot(row1_0.xyz, offset_0), dot(row2_0.xyz, offset_0)) - fieldOrigin_0.xyz) / _S2;
    const _S3 : vec3<f32> = vec3<f32>(0.5f, 0.5f, 0.5f);
    var clamped_0 : vec3<f32> = clamp(voxel_0, _S3, slotSize_0.xyz - _S3);
    var width_0 : u32;
    var height_0 : u32;
    var depth_0 : u32;
    {var dim = textureDimensions((uAtlas_1));((width_0)) = dim.x;((height_0)) = dim.y;((depth_0)) = dim.z;};
    var distance_0 : f32 = (ComputeSample_0(uAtlas_1, uAtlasSampler_0, (slotCorner_0.xyz + clamped_0) / vec3<f32>(f32(width_0), f32(height_0), f32(depth_0))).x * 2.0f - 1.0f) * _S1;
    var beyond_0 : f32 = length((clamped_0 - voxel_0) * _S2);
    var _S4 : f32;
    if(beyond_0 > 0.0f)
    {
        _S4 = beyond_0;
    }
    else
    {
        _S4 = distance_0;
    }
    return _S4;
}

@compute
@workgroup_size(4, 4, 4)
fn computeMain(@builtin(global_invocation_id) id_0 : vec3<u32>)
{
    var _S5 : vec3<i32> = vec3<i32>(id_0);
    if((any((_S5 >= vec3<i32>(g_0.uResolution_0, g_0.uResolution_0, g_0.uResolution_0)))))
    {
        return;
    }
    var world_1 : vec3<f32> = g_0.uOrigin_0 + (vec3<f32>(_S5) + vec3<f32>(0.5f)) * vec3<f32>(g_0.uVoxelSize_0);
    var cell_0 : vec3<i32> = clamp(vec3<i32>(floor((world_1 - g_0.uOrigin_0) / vec3<f32>(g_0.uCellSize_0))), vec3<i32>(i32(0), i32(0), i32(0)), vec3<i32>(g_0.uCellsPerAxis_0 - i32(1), g_0.uCellsPerAxis_0 - i32(1), g_0.uCellsPerAxis_0 - i32(1)));
    var cellIndex_0 : i32 = (cell_0.z * g_0.uCellsPerAxis_0 + cell_0.y) * g_0.uCellsPerAxis_0 + cell_0.x;
    var first_0 : i32 = uCellOffsets_0[cellIndex_0];
    var _S6 : i32 = uCellOffsets_0[cellIndex_0 + i32(1)];
    var nearest_0 : f32 = g_0.uRange_0;
    var i_0 : i32 = first_0;
    for(;;)
    {
        if(i_0 < _S6)
        {
        }
        else
        {
            break;
        }
        var _S7 : f32 = min(nearest_0, SampleInstance_0(uCellIndices_0[i_0], world_1));
        var _S8 : i32 = i_0 + i32(1);
        nearest_0 = _S7;
        i_0 = _S8;
    }
    textureStore((uClipmaps_0), (vec3<u32>(vec3<i32>(_S5.xy, g_0.uLevel_0 * g_0.uResolution_0 + _S5.z))), (vec4<f32>(clamp(nearest_0 / g_0.uRange_0, -1.0f, 1.0f) * 0.5f + 0.5f, 0.0f, 0.0f, 0.0f)));
    return;
}

