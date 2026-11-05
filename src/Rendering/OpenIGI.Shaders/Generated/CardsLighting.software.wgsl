struct _Array_std140_float4_0
{
    @align(16) data_0 : array<vec4<f32>, i32(4)>,
};

struct SLANG_ParameterGroup_Globals_std140_0
{
    @align(16) uClipmapOrigin_0 : array<vec4<f32>, i32(4)>,
    @align(16) uSkySh_0 : array<vec4<f32>, i32(7)>,
    @align(16) uAtlasSize_0 : vec3<f32>,
    @align(16) uLookupMin_0 : vec3<f32>,
    @align(16) uSunDirection_0 : vec3<f32>,
    @align(16) uSunColour_0 : vec3<f32>,
    @align(16) uSkyColour_0 : vec3<f32>,
    @align(16) uActiveCentre_0 : vec3<f32>,
    @align(16) uProbeMin_0 : vec3<f32>,
    @align(16) uTraceOrigin_0 : vec3<f32>,
    @align(16) uClipmapRange_0 : _Array_std140_float4_0,
    @align(16) uShadowLevels_0 : i32,
    @align(4) uShadowResolution_0 : i32,
    @align(8) uLookupBuckets_0 : i32,
    @align(4) uLookupCell_0 : f32,
    @align(16) uTileCount_0 : i32,
    @align(4) uTileOffset_0 : i32,
    @align(8) uUseActiveList_0 : i32,
    @align(4) uActiveCount_0 : i32,
    @align(16) uTileStride_0 : i32,
    @align(4) uActiveRadius_0 : f32,
    @align(8) uLightCount_0 : i32,
    @align(4) uLocalScale_0 : f32,
    @align(16) uProbeWidth_0 : i32,
    @align(4) uProbeHeight_0 : i32,
    @align(8) uTemporalIndex_0 : i32,
    @align(4) uMaxFrames_0 : i32,
    @align(16) uJitterStride_0 : i32,
    @align(4) uMode_0 : i32,
    @align(8) uBoost_0 : f32,
    @align(4) uSkyShValid_0 : i32,
    @align(16) uProbe_0 : i32,
    @align(4) uProbeInvExtent_0 : f32,
};

@binding(49) @group(0) var<uniform> Globals_0 : SLANG_ParameterGroup_Globals_std140_0;
@binding(22) @group(0) var<storage, read> uActiveTiles_0 : array<i32>;

@binding(17) @group(0) var<storage, read> uTiles_0 : array<vec4<i32>>;

@binding(16) @group(0) var<storage, read> uPages_0 : array<vec4<f32>>;

@binding(33) @group(0) var uAlbedo_0 : texture_2d<f32>;

@binding(65) @group(0) var uAlbedoSampler_0 : sampler;

@binding(0) @group(0) var uDirect_0 : texture_storage_2d<rgba16float, write>;

@binding(34) @group(0) var uNormal_0 : texture_2d<f32>;

@binding(66) @group(0) var uNormalSampler_0 : sampler;

@binding(36) @group(0) var uDepth_0 : texture_2d<f32>;

@binding(68) @group(0) var uDepthSampler_0 : sampler;

@binding(21) @group(0) var<storage, read_write> uProbePos_0 : array<vec4<f32>>;

@binding(37) @group(0) var uClipmaps_0 : texture_3d<f32>;

@binding(69) @group(0) var uClipmapsSampler_0 : sampler;

@binding(19) @group(0) var<storage, read_write> uCells_0 : array<vec2<i32>>;

@binding(20) @group(0) var<storage, read_write> uCellPages_0 : array<i32>;

@binding(38) @group(0) var uPrevious_0 : texture_2d<f32>;

@binding(70) @group(0) var uPreviousSampler_0 : sampler;

@binding(44) @group(0) var uDirectHistory_0 : texture_2d<f32>;

@binding(39) @group(0) var uIndirectRead_0 : texture_2d<f32>;

@binding(71) @group(0) var uIndirectReadSampler_0 : sampler;

@binding(18) @group(0) var<storage, read> uLights_0 : array<vec4<f32>>;

@binding(40) @group(0) var uLampMaps_0 : texture_depth_2d_array;

@binding(23) @group(0) var<storage, read> uLampMapRecords_0 : array<vec4<f32>>;

fn rsqrt_0( x_0 : f32) -> f32
{
    return 1.0f / sqrt(x_0);
}

var<private> kProbeNeighbour_0 : array<vec2<i32>, i32(4)> = array<vec2<i32>, i32(4)>( vec2<i32>(i32(-1), i32(0)), vec2<i32>(i32(1), i32(0)), vec2<i32>(i32(0), i32(-1)), vec2<i32>(i32(0), i32(1)) );
var<private> kProbeCorner_0 : array<vec2<i32>, i32(4)> = array<vec2<i32>, i32(4)>( vec2<i32>(i32(0), i32(0)), vec2<i32>(i32(1), i32(0)), vec2<i32>(i32(0), i32(1)), vec2<i32>(i32(1), i32(1)) );
fn GroupTile_0( groupId_0 : vec3<u32>,  entry_0 : ptr<function, vec4<i32>>,  at_0 : ptr<function, i32>,  lightPage_0 : ptr<function, vec4<f32>>,  cardPage_0 : ptr<function, vec4<f32>>) -> bool
{
    (*entry_0) = vec4<i32>(i32(0), i32(0), i32(0), i32(0));
    (*at_0) = i32(0);
    const _S1 : vec4<f32> = vec4<f32>(0.0f, 0.0f, 0.0f, 0.0f);
    (*lightPage_0) = _S1;
    (*cardPage_0) = _S1;
    var slot_0 : i32 = i32(groupId_0.x) * max(Globals_0.uTileStride_0, i32(1)) + Globals_0.uTileOffset_0;
    var tile_0 : i32;
    if((Globals_0.uUseActiveList_0) != i32(0))
    {
        if(slot_0 >= (Globals_0.uActiveCount_0))
        {
            return false;
        }
        tile_0 = uActiveTiles_0[slot_0];
    }
    else
    {
        tile_0 = slot_0;
    }
    if(tile_0 >= (Globals_0.uTileCount_0))
    {
        return false;
    }
    var _S2 : vec4<i32> = uTiles_0[tile_0];
    (*entry_0) = _S2;
    var _S3 : i32 = _S2.x * i32(10);
    (*at_0) = _S3;
    if((uPages_0[_S3 + i32(5)].w) > 0.5f)
    {
        return false;
    }
    if((Globals_0.uActiveRadius_0) > 0.0f)
    {
        var reach_0 : vec4<f32> = uPages_0[(*at_0) + i32(9)];
        var toCentre_0 : vec3<f32> = reach_0.xyz - Globals_0.uActiveCentre_0;
        var limit_0 : f32 = Globals_0.uActiveRadius_0 + reach_0.w;
        if((dot(toCentre_0, toCentre_0)) > (limit_0 * limit_0))
        {
            return false;
        }
    }
    (*lightPage_0) = uPages_0[(*at_0)];
    (*cardPage_0) = uPages_0[(*at_0) + i32(1)];
    return true;
}

fn TexelAlbedo_0( lightPage_1 : vec4<f32>,  cardPage_1 : vec4<f32>,  within_0 : vec2<i32>,  uv_0 : ptr<function, vec2<f32>>,  cardUv_0 : ptr<function, vec2<f32>>) -> vec4<f32>
{
    var _S4 : vec2<f32> = (vec2<f32>(within_0) + vec2<f32>(0.5f)) / vec2<f32>(lightPage_1.z, lightPage_1.w);
    (*uv_0) = _S4;
    var albedoWidth_0 : u32;
    var albedoHeight_0 : u32;
    {var dim = textureDimensions((uAlbedo_0));((albedoWidth_0)) = dim.x;((albedoHeight_0)) = dim.y;};
    var _S5 : vec2<f32> = (cardPage_1.xy + _S4 * cardPage_1.zw) / vec2<f32>(f32(albedoWidth_0), f32(albedoHeight_0));
    (*cardUv_0) = _S5;
    return (textureSampleLevel((uAlbedo_0), (uAlbedoSampler_0), (_S5), (0.0f)));
}

fn TexelSurface_0( at_1 : i32,  uv_1 : vec2<f32>,  cardUv_1 : vec2<f32>,  world_0 : ptr<function, vec3<f32>>,  worldNormal_0 : ptr<function, vec3<f32>>)
{
    var centreAndHalfWidth_0 : vec4<f32> = uPages_0[at_1 + i32(2)];
    var rightAndHalfHeight_0 : vec4<f32> = uPages_0[at_1 + i32(3)];
    var upAndDepth_0 : vec4<f32> = uPages_0[at_1 + i32(4)];
    var row0_0 : vec4<f32> = uPages_0[at_1 + i32(6)];
    var row1_0 : vec4<f32> = uPages_0[at_1 + i32(7)];
    var row2_0 : vec4<f32> = uPages_0[at_1 + i32(8)];
    var normal_0 : vec3<f32> = normalize((textureSampleLevel((uNormal_0), (uNormalSampler_0), (cardUv_1), (0.0f))).xyz * vec3<f32>(2.0f) - vec3<f32>(1.0f));
    var local_0 : vec3<f32> = centreAndHalfWidth_0.xyz + rightAndHalfHeight_0.xyz * vec3<f32>((uv_1.x * 2.0f - 1.0f)) * vec3<f32>(centreAndHalfWidth_0.w) + upAndDepth_0.xyz * vec3<f32>((uv_1.y * 2.0f - 1.0f)) * vec3<f32>(rightAndHalfHeight_0.w) - uPages_0[at_1 + i32(5)].xyz * vec3<f32>(((textureSampleLevel((uDepth_0), (uDepthSampler_0), (cardUv_1), (0.0f))).x * upAndDepth_0.w));
    var _S6 : vec3<f32> = vec3<f32>(row0_0.x, row1_0.x, row2_0.x);
    var _S7 : vec3<f32> = vec3<f32>(row0_0.y, row1_0.y, row2_0.y);
    var _S8 : vec3<f32> = vec3<f32>(row0_0.z, row1_0.z, row2_0.z);
    (*world_0) = vec3<f32>(dot(_S6, local_0), dot(_S7, local_0), dot(_S8, local_0)) + vec3<f32>(row0_0.w, row1_0.w, row2_0.w);
    (*worldNormal_0) = normalize(vec3<f32>(dot(_S6, normal_0), dot(_S7, normal_0), dot(_S8, normal_0)));
    return;
}

fn RadicalInverse_0( index_0 : i32) -> f32
{
    var bits_0 : u32 = u32(index_0);
    var bits_1 : u32 = (((bits_0 << (u32(16)))) | (((bits_0 >> (u32(16))))));
    var bits_2 : u32 = (((((bits_1 & (u32(1431655765)))) << (u32(1)))) | (((((bits_1 & (u32(2863311530)))) >> (u32(1))))));
    var bits_3 : u32 = (((((bits_2 & (u32(858993459)))) << (u32(2)))) | (((((bits_2 & (u32(3435973836)))) >> (u32(2))))));
    var bits_4 : u32 = (((((bits_3 & (u32(252645135)))) << (u32(4)))) | (((((bits_3 & (u32(4042322160)))) >> (u32(4))))));
    return f32((((((bits_4 & (u32(16711935)))) << (u32(8)))) | (((((bits_4 & (u32(4278255360)))) >> (u32(8))))))) * 2.32830643653869629e-10f;
}

fn ProbeCenter_0( probeCoord_0 : vec2<i32>,  frame_0 : i32) -> vec2<f32>
{
    if(frame_0 <= i32(0))
    {
        return vec2<f32>(0.5f, 0.5f);
    }
    var rotation_0 : f32 = f32(((((u32(probeCoord_0.x) * u32(73856093)) ^ ((u32(probeCoord_0.y) * u32(19349663))))) & (u32(65535)))) / 6.5536e+04f;
    var _S9 : i32 = frame_0 / max(Globals_0.uJitterStride_0, i32(1));
    var n_0 : i32 = (_S9 & (i32(65535)));
    return fract(vec2<f32>(fract(f32(n_0) * 0.6180340051651001f), RadicalInverse_0(n_0)) + vec2<f32>(rotation_0, fract(rotation_0 * 1.6180340051651001f)));
}

fn ProbeGathers_0() -> bool
{
    var _S10 : bool;
    if((Globals_0.uShadowLevels_0) > i32(0))
    {
        _S10 = (Globals_0.uLookupBuckets_0) > i32(0);
    }
    else
    {
        _S10 = false;
    }
    return _S10;
}

struct HemisphereBasis_0
{
     tangent_0 : vec3<f32>,
     bitangent_0 : vec3<f32>,
     normal_1 : vec3<f32>,
};

fn BasisAround_0( normal_2 : vec3<f32>) -> HemisphereBasis_0
{
    var basis_0 : HemisphereBasis_0;
    basis_0.normal_1 = normal_2;
    var _S11 : f32 = normal_2.z;
    if(_S11 < -0.99999988079071045f)
    {
        basis_0.tangent_0 = vec3<f32>(0.0f, -1.0f, 0.0f);
        basis_0.bitangent_0 = vec3<f32>(-1.0f, 0.0f, 0.0f);
        return basis_0;
    }
    var a_0 : f32 = 1.0f / (1.0f + _S11);
    var _S12 : f32 = normal_2.x;
    var _S13 : f32 = - _S12;
    var _S14 : f32 = normal_2.y;
    var b_0 : f32 = _S13 * _S14 * a_0;
    basis_0.tangent_0 = vec3<f32>(1.0f - _S12 * _S12 * a_0, b_0, _S13);
    basis_0.bitangent_0 = vec3<f32>(b_0, 1.0f - _S14 * _S14 * a_0, - _S14);
    return basis_0;
}

fn Orient_0( basis_1 : HemisphereBasis_0,  local_1 : vec3<f32>) -> vec3<f32>
{
    return basis_1.tangent_0 * vec3<f32>(local_1.x) + basis_1.bitangent_0 * vec3<f32>(local_1.y) + basis_1.normal_1 * vec3<f32>(local_1.z);
}

fn DistanceAt_0( world_1 : vec3<f32>,  known_0 : ptr<function, bool>,  raw_0 : ptr<function, f32>,  resolved_0 : ptr<function, f32>) -> f32
{
    (*known_0) = false;
    (*raw_0) = 0.0f;
    (*resolved_0) = Globals_0.uClipmapOrigin_0[i32(0)].w;
    var level_0 : i32 = i32(0);
    for(;;)
    {
        if(level_0 < (Globals_0.uShadowLevels_0))
        {
        }
        else
        {
            break;
        }
        var _S15 : f32 = Globals_0.uClipmapOrigin_0[level_0].w;
        var voxel_0 : vec3<f32> = (world_1 - Globals_0.uClipmapOrigin_0[level_0].xyz) / vec3<f32>(_S15);
        var _S16 : bool;
        if((any((voxel_0 < vec3<f32>(0.5f, 0.5f, 0.5f)))))
        {
            _S16 = true;
        }
        else
        {
            _S16 = (any((voxel_0 > vec3<f32>(f32(Globals_0.uShadowResolution_0) - 0.5f, f32(Globals_0.uShadowResolution_0) - 0.5f, f32(Globals_0.uShadowResolution_0) - 0.5f))));
        }
        if(_S16)
        {
            level_0 = level_0 + i32(1);
            continue;
        }
        var uvw_0 : vec3<f32> = vec3<f32>(voxel_0.x / f32(Globals_0.uShadowResolution_0), voxel_0.y / f32(Globals_0.uShadowResolution_0), (f32(level_0) * f32(Globals_0.uShadowResolution_0) + voxel_0.z) / f32(Globals_0.uShadowResolution_0 * Globals_0.uShadowLevels_0));
        (*known_0) = true;
        (*raw_0) = (textureSampleLevel((uClipmaps_0), (uClipmapsSampler_0), (uvw_0), (0.0f))).x;
        (*resolved_0) = _S15;
        return ((*raw_0) * 2.0f - 1.0f) * Globals_0.uClipmapRange_0.data_0[level_0].x;
    }
    return 1.0e+06f;
}

fn DistanceAt_1( world_2 : vec3<f32>,  known_1 : ptr<function, bool>,  raw_1 : ptr<function, f32>,  resolved_1 : ptr<function, f32>) -> f32
{
    (*known_1) = false;
    (*raw_1) = 0.0f;
    (*resolved_1) = Globals_0.uClipmapOrigin_0[i32(0)].w;
    var level_1 : i32 = i32(0);
    for(;;)
    {
        if(level_1 < (Globals_0.uShadowLevels_0))
        {
        }
        else
        {
            break;
        }
        var _S17 : f32 = Globals_0.uClipmapOrigin_0[level_1].w;
        var voxel_1 : vec3<f32> = (world_2 - Globals_0.uClipmapOrigin_0[level_1].xyz) / vec3<f32>(_S17);
        var _S18 : bool;
        if((any((voxel_1 < vec3<f32>(0.5f, 0.5f, 0.5f)))))
        {
            _S18 = true;
        }
        else
        {
            _S18 = (any((voxel_1 > vec3<f32>(f32(Globals_0.uShadowResolution_0) - 0.5f, f32(Globals_0.uShadowResolution_0) - 0.5f, f32(Globals_0.uShadowResolution_0) - 0.5f))));
        }
        if(_S18)
        {
            level_1 = level_1 + i32(1);
            continue;
        }
        var uvw_1 : vec3<f32> = vec3<f32>(voxel_1.x / f32(Globals_0.uShadowResolution_0), voxel_1.y / f32(Globals_0.uShadowResolution_0), (f32(level_1) * f32(Globals_0.uShadowResolution_0) + voxel_1.z) / f32(Globals_0.uShadowResolution_0 * Globals_0.uShadowLevels_0));
        (*known_1) = true;
        (*raw_1) = (textureSampleLevel((uClipmaps_0), (uClipmapsSampler_0), (uvw_1), (0.0f))).x;
        (*resolved_1) = _S17;
        return ((*raw_1) * 2.0f - 1.0f) * Globals_0.uClipmapRange_0.data_0[level_1].x;
    }
    return 1.0e+06f;
}

fn DistanceAt_2( world_3 : vec3<f32>,  known_2 : ptr<function, bool>,  raw_2 : ptr<function, f32>,  resolved_2 : ptr<function, f32>) -> f32
{
    (*known_2) = false;
    (*raw_2) = 0.0f;
    (*resolved_2) = Globals_0.uClipmapOrigin_0[i32(0)].w;
    var level_2 : i32 = i32(0);
    for(;;)
    {
        if(level_2 < (Globals_0.uShadowLevels_0))
        {
        }
        else
        {
            break;
        }
        var _S19 : f32 = Globals_0.uClipmapOrigin_0[level_2].w;
        var voxel_2 : vec3<f32> = (world_3 - Globals_0.uClipmapOrigin_0[level_2].xyz) / vec3<f32>(_S19);
        var _S20 : bool;
        if((any((voxel_2 < vec3<f32>(0.5f, 0.5f, 0.5f)))))
        {
            _S20 = true;
        }
        else
        {
            _S20 = (any((voxel_2 > vec3<f32>(f32(Globals_0.uShadowResolution_0) - 0.5f, f32(Globals_0.uShadowResolution_0) - 0.5f, f32(Globals_0.uShadowResolution_0) - 0.5f))));
        }
        if(_S20)
        {
            level_2 = level_2 + i32(1);
            continue;
        }
        var uvw_2 : vec3<f32> = vec3<f32>(voxel_2.x / f32(Globals_0.uShadowResolution_0), voxel_2.y / f32(Globals_0.uShadowResolution_0), (f32(level_2) * f32(Globals_0.uShadowResolution_0) + voxel_2.z) / f32(Globals_0.uShadowResolution_0 * Globals_0.uShadowLevels_0));
        (*known_2) = true;
        (*raw_2) = (textureSampleLevel((uClipmaps_0), (uClipmapsSampler_0), (uvw_2), (0.0f))).x;
        (*resolved_2) = _S19;
        return ((*raw_2) * 2.0f - 1.0f) * Globals_0.uClipmapRange_0.data_0[level_2].x;
    }
    return 1.0e+06f;
}

fn FieldStepHits_0( field_0 : f32,  travelled_0 : f32,  resolved_3 : f32,  hitDistance_0 : ptr<function, f32>) -> bool
{
    var radius_0 : f32 = 0.5f * resolved_3 * saturate(travelled_0 / max(2.0f * resolved_3, 9.99999997475242708e-07f));
    (*hitDistance_0) = max(travelled_0 + field_0 - radius_0, 0.0f);
    return field_0 < radius_0;
}

fn FieldStepHits_1( field_1 : f32,  travelled_1 : f32,  resolved_4 : f32,  hitDistance_1 : ptr<function, f32>) -> bool
{
    var radius_1 : f32 = 0.5f * resolved_4 * saturate(travelled_1 / max(2.0f * resolved_4, 9.99999997475242708e-07f));
    (*hitDistance_1) = max(travelled_1 + field_1 - radius_1, 0.0f);
    return field_1 < radius_1;
}

fn FieldStepHits_2( field_2 : f32,  travelled_2 : f32,  resolved_5 : f32,  hitDistance_2 : ptr<function, f32>) -> bool
{
    var radius_2 : f32 = 0.5f * resolved_5 * saturate(travelled_2 / max(2.0f * resolved_5, 9.99999997475242708e-07f));
    (*hitDistance_2) = max(travelled_2 + field_2 - radius_2, 0.0f);
    return field_2 < radius_2;
}

fn VisibilityAt_0( world_4 : vec3<f32>,  normal_3 : vec3<f32>,  direction_0 : vec3<f32>,  hit_0 : ptr<function, vec3<f32>>) -> f32
{
    (*hit_0) = world_4;
    if((Globals_0.uShadowLevels_0) <= i32(0))
    {
        return 1.0f;
    }
    var _S21 : vec3<f32> = world_4 + normal_3 * vec3<f32>(Globals_0.uClipmapOrigin_0[i32(0)].w) * vec3<f32>(1.5f);
    var _S22 : f32 = Globals_0.uClipmapOrigin_0[i32(0)].w * f32(Globals_0.uShadowResolution_0) * f32((i32(1) << (u32(Globals_0.uShadowLevels_0 - i32(1)))));
    var step_0 : i32 = i32(0);
    var travelled_3 : f32 = 0.0f;
    for(;;)
    {
        var _S23 : bool;
        if(step_0 < i32(64))
        {
            _S23 = travelled_3 < _S22;
        }
        else
        {
            _S23 = false;
        }
        if(_S23)
        {
        }
        else
        {
            break;
        }
        var known_3 : bool;
        var raw_3 : f32;
        var resolved_6 : f32;
        var distance_0 : f32 = DistanceAt_2(_S21 + direction_0 * vec3<f32>(travelled_3), &(known_3), &(raw_3), &(resolved_6));
        if(!known_3)
        {
            return 1.0f;
        }
        var hitDistance_3 : f32;
        var _S24 : bool = FieldStepHits_2(distance_0, travelled_3, resolved_6, &(hitDistance_3));
        if(_S24)
        {
            (*hit_0) = _S21 + direction_0 * vec3<f32>(hitDistance_3);
            return 0.0f;
        }
        var travelled_4 : f32 = travelled_3 + max(distance_0, resolved_6 * 0.25f);
        step_0 = step_0 + i32(1);
        travelled_3 = travelled_4;
    }
    return 1.0f;
}

fn VisibilityAt_1( world_5 : vec3<f32>,  normal_4 : vec3<f32>,  direction_1 : vec3<f32>,  hit_1 : ptr<function, vec3<f32>>) -> f32
{
    (*hit_1) = world_5;
    if((Globals_0.uShadowLevels_0) <= i32(0))
    {
        return 1.0f;
    }
    var _S25 : vec3<f32> = world_5 + normal_4 * vec3<f32>(Globals_0.uClipmapOrigin_0[i32(0)].w) * vec3<f32>(1.5f);
    var _S26 : f32 = Globals_0.uClipmapOrigin_0[i32(0)].w * f32(Globals_0.uShadowResolution_0) * f32((i32(1) << (u32(Globals_0.uShadowLevels_0 - i32(1)))));
    var step_1 : i32 = i32(0);
    var travelled_5 : f32 = 0.0f;
    for(;;)
    {
        var _S27 : bool;
        if(step_1 < i32(64))
        {
            _S27 = travelled_5 < _S26;
        }
        else
        {
            _S27 = false;
        }
        if(_S27)
        {
        }
        else
        {
            break;
        }
        var known_4 : bool;
        var raw_4 : f32;
        var resolved_7 : f32;
        var distance_1 : f32 = DistanceAt_2(_S25 + direction_1 * vec3<f32>(travelled_5), &(known_4), &(raw_4), &(resolved_7));
        if(!known_4)
        {
            return 1.0f;
        }
        var hitDistance_4 : f32;
        var _S28 : bool = FieldStepHits_2(distance_1, travelled_5, resolved_7, &(hitDistance_4));
        if(_S28)
        {
            (*hit_1) = _S25 + direction_1 * vec3<f32>(hitDistance_4);
            return 0.0f;
        }
        var travelled_6 : f32 = travelled_5 + max(distance_1, resolved_7 * 0.25f);
        step_1 = step_1 + i32(1);
        travelled_5 = travelled_6;
    }
    return 1.0f;
}

fn LookupBucket_0( cell_0 : vec3<i32>) -> i32
{
    return i32(((((((u32(cell_0.x) * u32(2376512323)) ^ ((u32(cell_0.y) * u32(3625334849))))) ^ ((u32(cell_0.z) * u32(3407524639))))) & (u32(Globals_0.uLookupBuckets_0 - i32(1)))));
}

fn MoverPages_0( headers_0 : i32,  index_1 : i32,  world_6 : vec3<f32>,  bias_0 : f32) -> vec2<i32>
{
    var at_2 : i32 = headers_0 + index_1 * i32(6);
    var reach_1 : f32 = (bitcast<f32>((uCellPages_0[at_2 + i32(3)]))) + bias_0;
    var toCentre_1 : vec3<f32> = world_6 - vec3<f32>((bitcast<f32>((uCellPages_0[at_2]))), (bitcast<f32>((uCellPages_0[at_2 + i32(1)]))), (bitcast<f32>((uCellPages_0[at_2 + i32(2)]))));
    var _S29 : vec2<i32>;
    if((dot(toCentre_1, toCentre_1)) > (reach_1 * reach_1))
    {
        _S29 = vec2<i32>(i32(0), i32(0));
    }
    else
    {
        _S29 = vec2<i32>(uCellPages_0[at_2 + i32(4)], uCellPages_0[at_2 + i32(5)]);
    }
    return _S29;
}

fn ProjectOntoCard_0( page_0 : i32,  world_7 : vec3<f32>,  directionW_0 : vec3<f32>,  across_0 : ptr<function, vec2<f32>>,  halfSize_0 : ptr<function, vec2<f32>>,  behind_0 : ptr<function, f32>,  depth_0 : ptr<function, f32>,  facing_0 : ptr<function, vec3<f32>>,  directionLocal_0 : ptr<function, vec3<f32>>,  litRect_0 : ptr<function, vec4<f32>>,  cardPage_2 : ptr<function, vec4<f32>>)
{
    var _S30 : i32 = page_0 * i32(10);
    (*litRect_0) = uPages_0[_S30];
    (*cardPage_2) = uPages_0[_S30 + i32(1)];
    var centreAndHalfWidth_1 : vec4<f32> = uPages_0[_S30 + i32(2)];
    var rightAndHalfHeight_1 : vec4<f32> = uPages_0[_S30 + i32(3)];
    var upAndDepth_1 : vec4<f32> = uPages_0[_S30 + i32(4)];
    var direction_2 : vec4<f32> = uPages_0[_S30 + i32(5)];
    var row0_1 : vec4<f32> = uPages_0[_S30 + i32(6)];
    var row1_1 : vec4<f32> = uPages_0[_S30 + i32(7)];
    var row2_1 : vec4<f32> = uPages_0[_S30 + i32(8)];
    var delta_0 : vec3<f32> = world_7 - vec3<f32>(row0_1.w, row1_1.w, row2_1.w);
    var _S31 : vec3<f32> = row0_1.xyz;
    var _S32 : vec3<f32> = row1_1.xyz;
    var _S33 : vec3<f32> = row2_1.xyz;
    var local_2 : vec3<f32> = vec3<f32>(dot(_S31, delta_0), dot(_S32, delta_0), dot(_S33, delta_0));
    (*directionLocal_0) = vec3<f32>(dot(_S31, directionW_0), dot(_S32, directionW_0), dot(_S33, directionW_0));
    var relative_0 : vec3<f32> = local_2 - centreAndHalfWidth_1.xyz;
    var _S34 : vec2<f32> = max(vec2<f32>(centreAndHalfWidth_1.w, rightAndHalfHeight_1.w), vec2<f32>(9.99999997475242708e-07f, 9.99999997475242708e-07f));
    (*halfSize_0) = _S34;
    (*across_0) = vec2<f32>(dot(relative_0, rightAndHalfHeight_1.xyz), dot(relative_0, upAndDepth_1.xyz)) / _S34;
    var _S35 : vec3<f32> = direction_2.xyz;
    (*behind_0) = - dot(relative_0, _S35);
    (*depth_0) = upAndDepth_1.w;
    (*facing_0) = _S35;
    return;
}

fn LookupCardWithin_0( world_8 : vec3<f32>,  arrivingW_0 : vec3<f32>,  skin_0 : f32,  found_0 : ptr<function, vec2<f32>>,  litFound_0 : ptr<function, vec2<f32>>,  agreement_0 : ptr<function, f32>,  cardDepth_0 : ptr<function, f32>) -> bool
{
    (*cardDepth_0) = 0.0f;
    const _S36 : vec2<f32> = vec2<f32>(0.0f, 0.0f);
    (*found_0) = _S36;
    (*litFound_0) = _S36;
    (*agreement_0) = 1.0e+09f;
    if((Globals_0.uLookupBuckets_0) <= i32(0))
    {
        return false;
    }
    var _S37 : vec2<i32> = uCells_0[LookupBucket_0(vec3<i32>(floor((world_8 - Globals_0.uLookupMin_0) / vec3<f32>(Globals_0.uLookupCell_0))))];
    var albedoWidth_1 : u32;
    var albedoHeight_1 : u32;
    {var dim = textureDimensions((uAlbedo_0));((albedoWidth_1)) = dim.x;((albedoHeight_1)) = dim.y;};
    var _S38 : vec2<f32> = vec2<f32>(f32(albedoWidth_1), f32(albedoHeight_1));
    var _S39 : vec2<i32> = uCells_0[Globals_0.uLookupBuckets_0];
    var settled_0 : bool = false;
    var anyFound_0 : bool = false;
    var walk_0 : i32 = i32(0);
    for(;;)
    {
        var _S40 : bool;
        if(walk_0 <= (_S39.y))
        {
            _S40 = !settled_0;
        }
        else
        {
            _S40 = false;
        }
        if(_S40)
        {
        }
        else
        {
            break;
        }
        var _S41 : bool = walk_0 == i32(0);
        var _S42 : vec2<i32>;
        if(_S41)
        {
            _S42 = _S37;
        }
        else
        {
            _S42 = MoverPages_0(_S39.x, walk_0 - i32(1), world_8, 0.0f);
        }
        var settled_1 : bool = settled_0;
        var anyFound_1 : bool = anyFound_0;
        var slot_1 : i32 = i32(0);
        for(;;)
        {
            var _S43 : bool;
            if(slot_1 < (_S42.y))
            {
                _S43 = !settled_1;
            }
            else
            {
                _S43 = false;
            }
            if(_S43)
            {
            }
            else
            {
                break;
            }
            var page_1 : i32 = uCellPages_0[_S42.x + slot_1];
            var _S44 : i32 = uCellPages_0[_S42.x + slot_1] * i32(10);
            if(((uPages_0[_S44 + i32(5)].w) > 0.5f) == _S41)
            {
                slot_1 = slot_1 + i32(1);
                continue;
            }
            var bound_0 : vec4<f32> = uPages_0[_S44 + i32(9)];
            var toCentre_2 : vec3<f32> = world_8 - bound_0.xyz;
            var _S45 : f32 = bound_0.w;
            if((dot(toCentre_2, toCentre_2)) > (_S45 * _S45))
            {
                slot_1 = slot_1 + i32(1);
                continue;
            }
            var across_1 : vec2<f32>;
            var halfSize_1 : vec2<f32>;
            var behind_1 : f32;
            var depth_1 : f32;
            var facing_1 : vec3<f32>;
            var arrivingLocal_0 : vec3<f32>;
            var litRect_1 : vec4<f32>;
            var cardPage_3 : vec4<f32>;
            ProjectOntoCard_0(page_1, world_8, (vec3<f32>(0) - arrivingW_0), &(across_1), &(halfSize_1), &(behind_1), &(depth_1), &(facing_1), &(arrivingLocal_0), &(litRect_1), &(cardPage_3));
            var _S46 : bool;
            if((abs(across_1.x)) > 1.0f)
            {
                _S46 = true;
            }
            else
            {
                _S46 = (abs(across_1.y)) > 1.0f;
            }
            if(_S46)
            {
                slot_1 = slot_1 + i32(1);
                continue;
            }
            var _S47 : bool;
            if((dot(arrivingW_0, arrivingW_0)) > 0.5f)
            {
                _S47 = (dot(facing_1, arrivingLocal_0)) < -0.05000000074505806f;
            }
            else
            {
                _S47 = false;
            }
            if(_S47)
            {
                slot_1 = slot_1 + i32(1);
                continue;
            }
            var _S48 : bool;
            if(behind_1 < (- skin_0))
            {
                _S48 = true;
            }
            else
            {
                _S48 = behind_1 > (depth_1 + skin_0);
            }
            if(_S48)
            {
                slot_1 = slot_1 + i32(1);
                continue;
            }
            var _S49 : vec2<f32> = vec2<f32>(0.5f);
            var uv_2 : vec2<f32> = across_1 * _S49 + _S49;
            var texel_0 : vec2<f32> = (cardPage_3.xy + uv_2 * cardPage_3.zw) / _S38;
            if(((textureSampleLevel((uAlbedo_0), (uAlbedoSampler_0), (texel_0), (0.0f))).w) < 0.5f)
            {
                slot_1 = slot_1 + i32(1);
                continue;
            }
            var error_0 : f32 = abs((textureSampleLevel((uDepth_0), (uDepthSampler_0), (texel_0), (0.0f))).x * depth_1 - behind_1);
            var settled_2 : bool;
            var anyFound_2 : bool;
            if(error_0 < (*agreement_0))
            {
                (*agreement_0) = error_0;
                (*cardDepth_0) = depth_1;
                (*found_0) = texel_0;
                var _S50 : bool = error_0 < 0.01999999955296516f;
                (*litFound_0) = (litRect_1.xy + uv_2 * litRect_1.zw) / Globals_0.uAtlasSize_0.xy;
                settled_2 = _S50;
                anyFound_2 = true;
            }
            else
            {
                settled_2 = settled_1;
                anyFound_2 = anyFound_1;
            }
            settled_1 = settled_2;
            anyFound_1 = anyFound_2;
            slot_1 = slot_1 + i32(1);
        }
        var _S51 : i32 = walk_0 + i32(1);
        settled_0 = settled_1;
        anyFound_0 = anyFound_1;
        walk_0 = _S51;
    }
    return anyFound_0;
}

fn LookupCardHit_0( world_9 : vec3<f32>,  rayDirection_0 : vec3<f32>,  found_1 : ptr<function, vec2<f32>>,  litFound_1 : ptr<function, vec2<f32>>,  agreement_1 : ptr<function, f32>) -> bool
{
    var depth_2 : f32;
    var _S52 : bool = LookupCardWithin_0(world_9, rayDirection_0, 0.25f, &((*found_1)), &((*litFound_1)), &((*agreement_1)), &(depth_2));
    return _S52;
}

fn ShBasis_0( d_0 : vec3<f32>) -> vec4<f32>
{
    return vec4<f32>(0.28209498524665833f, -0.48860299587249756f * d_0.y, 0.48860299587249756f * d_0.z, -0.48860299587249756f * d_0.x);
}

fn GatherProbeRay_0( world_10 : vec3<f32>,  normal_5 : vec3<f32>,  center_0 : vec2<f32>,  basis_2 : HemisphereBasis_0,  finest_0 : f32,  cell_1 : i32,  radiance_0 : ptr<function, vec3<f32>>,  basisHere_0 : ptr<function, vec4<f32>>)
{
    var square_0 : vec2<f32> = (vec2<f32>(f32(cell_1 % i32(4)), f32(cell_1 / i32(4))) + center_0) / vec2<f32>(4.0f);
    var phi_0 : f32 = 6.28318548202514648f * square_0.x;
    var cosTheta_0 : f32 = square_0.y;
    var sinTheta_0 : f32 = sqrt(max(0.0f, 1.0f - cosTheta_0 * cosTheta_0));
    var direction_3 : vec3<f32> = Orient_0(basis_2, vec3<f32>(sinTheta_0 * cos(phi_0), sinTheta_0 * sin(phi_0), cosTheta_0));
    (*radiance_0) = vec3<f32>(0.0f, 0.0f, 0.0f);
    var hit_2 : vec3<f32>;
    var _S53 : f32 = VisibilityAt_1(world_10, normal_5, direction_3, &(hit_2));
    if(_S53 <= 0.5f)
    {
        var found_2 : vec2<f32>;
        var litFound_2 : vec2<f32>;
        var agreement_2 : f32;
        var _S54 : bool = LookupCardHit_0(hit_2, direction_3, &(found_2), &(litFound_2), &(agreement_2));
        if(_S54)
        {
            (*radiance_0) = (textureSampleLevel((uPrevious_0), (uPreviousSampler_0), (litFound_2), (0.0f))).xyz * vec3<f32>(smoothstep(1.5f * finest_0, 2.0f * finest_0, length(hit_2 - world_10)));
        }
    }
    (*basisHere_0) = ShBasis_0(direction_3);
    return;
}

fn ShIrradianceTransfer_0( normal_6 : vec3<f32>) -> vec4<f32>
{
    var basis_3 : vec4<f32> = ShBasis_0(normal_6);
    return vec4<f32>(basis_3.x * 3.14159274101257324f, basis_3.yzw * vec3<f32>(2.09439516067504883f));
}

fn FinishProbe_0( red_0 : vec4<f32>,  green_0 : vec4<f32>,  blue_0 : vec4<f32>,  normal_7 : vec3<f32>) -> vec3<f32>
{
    var transfer_0 : vec4<f32> = ShIrradianceTransfer_0(normal_7);
    return max(vec3<f32>(dot(red_0, transfer_0), dot(green_0, transfer_0), dot(blue_0, transfer_0)) * vec3<f32>(0.39269909262657166f), vec3<f32>(0.0f, 0.0f, 0.0f));
}

fn Gather_0( world_11 : vec3<f32>,  normal_8 : vec3<f32>,  center_1 : vec2<f32>) -> vec3<f32>
{
    if(!ProbeGathers_0())
    {
        return vec3<f32>(0.0f, 0.0f, 0.0f);
    }
    var _S55 : HemisphereBasis_0 = BasisAround_0(normal_8);
    var _S56 : f32 = Globals_0.uClipmapOrigin_0[i32(0)].w;
    const _S57 : vec4<f32> = vec4<f32>(0.0f, 0.0f, 0.0f, 0.0f);
    var cell_2 : i32 = i32(0);
    var red_1 : vec4<f32> = _S57;
    var green_1 : vec4<f32> = _S57;
    var blue_1 : vec4<f32> = _S57;
    for(;;)
    {
        if(cell_2 < i32(16))
        {
        }
        else
        {
            break;
        }
        var radiance_1 : vec3<f32>;
        var basisHere_1 : vec4<f32>;
        GatherProbeRay_0(world_11, normal_8, center_1, _S55, _S56, cell_2, &(radiance_1), &(basisHere_1));
        var red_2 : vec4<f32> = red_1 + basisHere_1 * vec4<f32>(radiance_1.x);
        var green_2 : vec4<f32> = green_1 + basisHere_1 * vec4<f32>(radiance_1.y);
        var blue_2 : vec4<f32> = blue_1 + basisHere_1 * vec4<f32>(radiance_1.z);
        cell_2 = cell_2 + i32(1);
        red_1 = red_2;
        green_1 = green_2;
        blue_1 = blue_2;
    }
    return FinishProbe_0(red_1, green_1, blue_1, normal_8);
}

fn ProbeUsable_0( probeCoord_1 : vec2<i32>,  pageMin_0 : vec2<i32>,  pageMax_0 : vec2<i32>,  position_0 : ptr<function, vec3<f32>>) -> bool
{
    (*position_0) = vec3<f32>(0.0f, 0.0f, 0.0f);
    var _S58 : bool;
    if((any((probeCoord_1 < vec2<i32>(i32(0), i32(0))))))
    {
        _S58 = true;
    }
    else
    {
        _S58 = (probeCoord_1.x) >= (Globals_0.uProbeWidth_0);
    }
    if(_S58)
    {
        _S58 = true;
    }
    else
    {
        _S58 = (probeCoord_1.y) >= (Globals_0.uProbeHeight_0);
    }
    if(_S58)
    {
        return false;
    }
    var texel_1 : vec2<i32> = probeCoord_1 * vec2<i32>(i32(4));
    if((any((texel_1 < pageMin_0))))
    {
        _S58 = true;
    }
    else
    {
        _S58 = (any((texel_1 >= pageMax_0)));
    }
    if(_S58)
    {
        return false;
    }
    var record_0 : vec4<f32> = uProbePos_0[probeCoord_1.y * Globals_0.uProbeWidth_0 + probeCoord_1.x];
    (*position_0) = uProbePos_0[probeCoord_1.y * Globals_0.uProbeWidth_0 + probeCoord_1.x].xyz;
    return (record_0.w) >= 0.5f;
}

fn ProbeUsable_1( probeCoord_2 : vec2<i32>,  pageMin_1 : vec2<i32>,  pageMax_1 : vec2<i32>,  position_1 : ptr<function, vec3<f32>>) -> bool
{
    (*position_1) = vec3<f32>(0.0f, 0.0f, 0.0f);
    var _S59 : bool;
    if((any((probeCoord_2 < vec2<i32>(i32(0), i32(0))))))
    {
        _S59 = true;
    }
    else
    {
        _S59 = (probeCoord_2.x) >= (Globals_0.uProbeWidth_0);
    }
    if(_S59)
    {
        _S59 = true;
    }
    else
    {
        _S59 = (probeCoord_2.y) >= (Globals_0.uProbeHeight_0);
    }
    if(_S59)
    {
        return false;
    }
    var texel_2 : vec2<i32> = probeCoord_2 * vec2<i32>(i32(4));
    if((any((texel_2 < pageMin_1))))
    {
        _S59 = true;
    }
    else
    {
        _S59 = (any((texel_2 >= pageMax_1)));
    }
    if(_S59)
    {
        return false;
    }
    var record_1 : vec4<f32> = uProbePos_0[probeCoord_2.y * Globals_0.uProbeWidth_0 + probeCoord_2.x];
    (*position_1) = uProbePos_0[probeCoord_2.y * Globals_0.uProbeWidth_0 + probeCoord_2.x].xyz;
    return (record_1.w) >= 0.5f;
}

fn ProbePlaneAccept_0( texelWorld_0 : vec3<f32>,  texelNormal_0 : vec3<f32>,  probeWorld_0 : vec3<f32>) -> f32
{
    var offset_0 : vec3<f32> = texelWorld_0 - probeWorld_0;
    var _S60 : f32;
    if((abs(dot(offset_0, texelNormal_0)) / (length(offset_0) + 0.00999999977648258f)) < 0.25775998830795288f)
    {
        _S60 = 1.0f;
    }
    else
    {
        _S60 = 0.0f;
    }
    return _S60;
}

fn ProbeIrradiance_0( probeCoord_3 : vec2<i32>) -> vec3<f32>
{
    return (textureSampleLevel((uIndirectRead_0), (uIndirectReadSampler_0), ((vec2<f32>(probeCoord_3 * vec2<i32>(i32(4))) + vec2<f32>(0.5f)) / Globals_0.uAtlasSize_0.xy), (0.0f))).xyz;
}

fn FilteredProbe_0( probeCoord_4 : vec2<i32>,  pageMin_2 : vec2<i32>,  pageMax_2 : vec2<i32>,  texelWorld_1 : vec3<f32>,  texelNormal_1 : vec3<f32>) -> vec3<f32>
{
    var _S61 : vec3<f32> = ProbeIrradiance_0(probeCoord_4) * vec3<f32>(2.0f);
    var k_0 : i32 = i32(0);
    var sum_0 : vec3<f32> = _S61;
    var weight_0 : f32 = 2.0f;
    for(;;)
    {
        if(k_0 < i32(4))
        {
        }
        else
        {
            break;
        }
        var neighbour_0 : vec2<i32> = probeCoord_4 + kProbeNeighbour_0[k_0];
        var position_2 : vec3<f32>;
        var _S62 : bool = ProbeUsable_0(neighbour_0, pageMin_2, pageMax_2, &(position_2));
        if(!_S62)
        {
            k_0 = k_0 + i32(1);
            continue;
        }
        if((ProbePlaneAccept_0(texelWorld_1, texelNormal_1, position_2)) <= 0.0f)
        {
            k_0 = k_0 + i32(1);
            continue;
        }
        var weight_1 : f32 = weight_0 + 1.0f;
        sum_0 = sum_0 + ProbeIrradiance_0(neighbour_0);
        weight_0 = weight_1;
        k_0 = k_0 + i32(1);
    }
    return sum_0 / vec3<f32>(weight_0);
}

fn BoostAlbedo_0( albedo_0 : vec3<f32>,  boost_0 : f32) -> vec3<f32>
{
    if(boost_0 >= 1.0f)
    {
        return albedo_0;
    }
    var luminance_0 : f32 = dot(albedo_0, vec3<f32>(0.30000001192092896f, 0.5899999737739563f, 0.10999999940395355f));
    return min(saturate(albedo_0 * vec3<f32>((pow(max(luminance_0, 0.0f), boost_0) / max(luminance_0, 9.99999993922529029e-09f)))), vec3<f32>(0.99000000953674316f, 0.99000000953674316f, 0.99000000953674316f));
}

fn LookupCard_0( world_12 : vec3<f32>,  found_3 : ptr<function, vec2<f32>>,  litFound_3 : ptr<function, vec2<f32>>,  agreement_3 : ptr<function, f32>) -> bool
{
    var depth_3 : f32;
    var _S63 : bool = LookupCardWithin_0(world_12, vec3<f32>(0.0f, 0.0f, 0.0f), 0.25f, &((*found_3)), &((*litFound_3)), &((*agreement_3)), &(depth_3));
    return _S63;
}

fn ProbedTrace_0( world_13 : vec3<f32>,  normal_9 : vec3<f32>) -> vec4<f32>
{
    var finest_1 : f32 = Globals_0.uClipmapOrigin_0[i32(0)].w;
    var _S64 : f32 = Globals_0.uClipmapOrigin_0[max(Globals_0.uShadowLevels_0 - i32(1), i32(0))].w;
    var _S65 : vec3<f32> = world_13 + normal_9 * vec3<f32>(finest_1) * vec3<f32>(1.5f);
    var reach_2 : f32 = finest_1 * f32(Globals_0.uShadowResolution_0) * f32((i32(1) << (u32(Globals_0.uShadowLevels_0 - i32(1)))));
    var step_2 : i32 = i32(0);
    var travelled_7 : f32 = 0.0f;
    for(;;)
    {
        var _S66 : bool;
        if(step_2 < i32(64))
        {
            _S66 = travelled_7 < reach_2;
        }
        else
        {
            _S66 = false;
        }
        if(_S66)
        {
        }
        else
        {
            break;
        }
        var known_5 : bool;
        var raw_5 : f32;
        var resolved_8 : f32;
        var distance_2 : f32 = DistanceAt_0(_S65 + Globals_0.uSunDirection_0 * vec3<f32>(travelled_7), &(known_5), &(raw_5), &(resolved_8));
        if(!known_5)
        {
            return vec4<f32>(1.0f, 0.0f, travelled_7 / reach_2, 1.0f);
        }
        var touchedAt_0 : f32;
        if(FieldStepHits_0(distance_2, travelled_7, resolved_8, &(touchedAt_0)))
        {
            return vec4<f32>(0.0f, resolved_8 / _S64, travelled_7 / reach_2, 1.0f);
        }
        var travelled_8 : f32 = travelled_7 + max(distance_2, resolved_8 * 0.25f);
        step_2 = step_2 + i32(1);
        travelled_7 = travelled_8;
    }
    return vec4<f32>(0.5f, 0.0f, clamp(travelled_7 / reach_2, 0.0f, 1.0f), 1.0f);
}

fn Probed_0( world_14 : vec3<f32>,  normal_10 : vec3<f32>,  cardUv_2 : vec2<f32>,  texel_3 : vec2<i32>) -> vec4<f32>
{
    if((Globals_0.uProbe_0) == i32(5))
    {
        if((((((texel_3.x) | ((texel_3.y)))) & (i32(7)))) != i32(0))
        {
            return vec4<f32>(0.0f, 0.0f, 0.0f, 0.0f);
        }
        var found_4 : vec2<f32>;
        var litFound_4 : vec2<f32>;
        var agreement_4 : f32;
        var _S67 : bool = LookupCard_0(world_14, &(found_4), &(litFound_4), &(agreement_4));
        if(!_S67)
        {
            return vec4<f32>(1.0f, 0.0f, 0.0f, 1.0f);
        }
        var albedoWidth_2 : u32;
        var albedoHeight_2 : u32;
        {var dim = textureDimensions((uAlbedo_0));((albedoWidth_2)) = dim.x;((albedoHeight_2)) = dim.y;};
        var apart_0 : vec2<f32> = abs(found_4 - cardUv_2) * vec2<f32>(f32(albedoWidth_2), f32(albedoHeight_2));
        var _S68 : vec4<f32>;
        if((max(apart_0.x, apart_0.y)) < 1.5f)
        {
            _S68 = vec4<f32>(0.0f, 1.0f, 0.0f, 1.0f);
        }
        else
        {
            _S68 = vec4<f32>(1.0f, 1.0f, 0.0f, 1.0f);
        }
        return _S68;
    }
    if((Globals_0.uProbe_0) == i32(4))
    {
        return ProbedTrace_0(world_14, normal_10);
    }
    if((Globals_0.uProbe_0) == i32(2))
    {
        return vec4<f32>(clamp((world_14 - Globals_0.uProbeMin_0) * vec3<f32>(Globals_0.uProbeInvExtent_0), vec3<f32>(0.0f, 0.0f, 0.0f), vec3<f32>(1.0f, 1.0f, 1.0f)), 1.0f);
    }
    if((Globals_0.uProbe_0) == i32(3))
    {
        var _S69 : vec3<f32> = vec3<f32>(0.5f);
        return vec4<f32>(normal_10 * _S69 + _S69, 1.0f);
    }
    var finest_2 : f32 = Globals_0.uClipmapOrigin_0[i32(0)].w;
    var known_6 : bool;
    var raw_6 : f32;
    var resolved_9 : f32;
    var start_0 : f32 = DistanceAt_1(world_14 + normal_10 * vec3<f32>(finest_2) * vec3<f32>(1.5f), &(known_6), &(raw_6), &(resolved_9));
    var _S70 : f32;
    if((Globals_0.uShadowLevels_0) > i32(0))
    {
        _S70 = clamp(Globals_0.uClipmapRange_0.data_0[i32(0)].x / (finest_2 * 4.0f), 0.0f, 1.0f);
    }
    else
    {
        _S70 = 0.0f;
    }
    var _S71 : f32 = raw_6;
    var _S72 : f32;
    if(known_6)
    {
        _S72 = clamp(start_0 / (finest_2 * 4.0f), 0.0f, 1.0f);
    }
    else
    {
        _S72 = 1.0f;
    }
    var _S73 : f32;
    if(known_6)
    {
        _S73 = 1.0f;
    }
    else
    {
        _S73 = 0.5f;
    }
    return vec4<f32>(_S70, _S71, _S72, _S73);
}

fn Visibility_0( world_15 : vec3<f32>,  normal_11 : vec3<f32>,  direction_4 : vec3<f32>) -> f32
{
    var ignored_0 : vec3<f32>;
    return VisibilityAt_0(world_15, normal_11, direction_4, &(ignored_0));
}

fn SunVisibility_0( world_16 : vec3<f32>,  normal_12 : vec3<f32>) -> f32
{
    return Visibility_0(world_16, normal_12, Globals_0.uSunDirection_0);
}

fn SkyRadiance_0( direction_5 : vec3<f32>) -> vec3<f32>
{
    if((Globals_0.uSkyShValid_0) == i32(0))
    {
        return Globals_0.uSkyColour_0 * vec3<f32>((direction_5.z * 0.5f + 0.5f));
    }
    var n_1 : vec4<f32> = vec4<f32>(direction_5, 1.0f);
    var q_0 : vec4<f32> = n_1.xyzz * n_1.yzzx;
    var _S74 : f32 = direction_5.x;
    var _S75 : f32 = direction_5.y;
    return max(vec3<f32>(0.0f, 0.0f, 0.0f), vec3<f32>(dot(Globals_0.uSkySh_0[i32(0)], n_1), dot(Globals_0.uSkySh_0[i32(1)], n_1), dot(Globals_0.uSkySh_0[i32(2)], n_1)) + vec3<f32>(dot(Globals_0.uSkySh_0[i32(3)], q_0), dot(Globals_0.uSkySh_0[i32(4)], q_0), dot(Globals_0.uSkySh_0[i32(5)], q_0)) + Globals_0.uSkySh_0[i32(6)].xyz * vec3<f32>((_S74 * _S74 - _S75 * _S75)));
}

fn SkyLight_0( world_17 : vec3<f32>,  normal_13 : vec3<f32>) -> vec3<f32>
{
    if((Globals_0.uShadowLevels_0) <= i32(0))
    {
        return SkyRadiance_0(normal_13) * vec3<f32>(3.14159274101257324f);
    }
    var aside_0 : vec3<f32>;
    if((abs(normal_13.z)) < 0.89999997615814209f)
    {
        aside_0 = vec3<f32>(0.0f, 0.0f, 1.0f);
    }
    else
    {
        aside_0 = vec3<f32>(1.0f, 0.0f, 0.0f);
    }
    var tangent_1 : vec3<f32> = normalize(cross(normal_13, aside_0));
    var _S76 : vec3<f32> = cross(normal_13, tangent_1);
    const _S77 : vec3<f32> = vec3<f32>(0.0f, 0.0f, 0.0f);
    var cell_3 : vec3<f32> = floor(world_17 * vec3<f32>(8.0f));
    var _S78 : f32 = fract(sin(dot(cell_3, vec3<f32>(12.98980045318603516f, 78.233001708984375f, 37.71900177001953125f))) * 43758.546875f);
    var _S79 : f32 = fract(sin(dot(cell_3, vec3<f32>(39.34600067138671875f, 11.13500022888183594f, 83.154998779296875f))) * 24634.634765625f);
    var ray_0 : i32 = i32(0);
    var total_0 : vec3<f32> = _S77;
    for(;;)
    {
        if(ray_0 < i32(8))
        {
        }
        else
        {
            break;
        }
        var u1_0 : f32 = (f32(ray_0) + _S79) / 8.0f;
        var radius_3 : f32 = sqrt(u1_0);
        var angle_0 : f32 = 6.28318548202514648f * fract(RadicalInverse_0(ray_0) + _S78);
        var direction_6 : vec3<f32> = normalize(tangent_1 * vec3<f32>((radius_3 * cos(angle_0))) + _S76 * vec3<f32>((radius_3 * sin(angle_0))) + normal_13 * vec3<f32>(sqrt(max(0.0f, 1.0f - u1_0))));
        var total_1 : vec3<f32> = total_0 + SkyRadiance_0(direction_6) * vec3<f32>(Visibility_0(world_17, normal_13, direction_6));
        ray_0 = ray_0 + i32(1);
        total_0 = total_1;
    }
    return total_0 * vec3<f32>(0.39269909262657166f);
}

fn CardLampMapVisibility_0( entry_1 : i32,  world_18 : vec3<f32>,  normal_14 : vec3<f32>) -> f32
{
    if(entry_1 < i32(0))
    {
        return -1.0f;
    }
    var mapWidth_0 : u32;
    var mapHeight_0 : u32;
    var mapLayers_0 : u32;
    {var dim = textureDimensions((uLampMaps_0));((mapWidth_0)) = dim.x;((mapHeight_0)) = dim.y;((mapLayers_0)) = textureNumLayers((uLampMaps_0));};
    var pointZ_0 : f32 = 0.25f;
    var face_0 : i32 = i32(0);
    for(;;)
    {
        if(face_0 < i32(6))
        {
        }
        else
        {
            break;
        }
        var forward_0 : vec4<f32> = uLampMapRecords_0[(entry_1 + face_0) * i32(5) + i32(3)];
        var _S80 : f32 = max(pointZ_0, dot(forward_0.xyz, world_18) + forward_0.w);
        var _S81 : i32 = face_0 + i32(1);
        pointZ_0 = _S80;
        face_0 = _S81;
    }
    var at_3 : vec3<f32> = world_18 + normal_14 * vec3<f32>((0.05000000074505806f + 1.5f * (pointZ_0 * 2.0f / f32(max(mapWidth_0, u32(1))))));
    var deepest_0 : f32 = -1.00000001504746622e+30f;
    var slot_2 : i32 = entry_1;
    face_0 = i32(0);
    for(;;)
    {
        if(face_0 < i32(6))
        {
        }
        else
        {
            break;
        }
        var _S82 : i32 = entry_1 + face_0;
        var forward_1 : vec4<f32> = uLampMapRecords_0[_S82 * i32(5) + i32(3)];
        var depth_4 : f32 = dot(forward_1.xyz, at_3) + forward_1.w;
        if(depth_4 > deepest_0)
        {
            deepest_0 = depth_4;
            slot_2 = _S82;
        }
        face_0 = face_0 + i32(1);
    }
    var _S83 : i32 = slot_2 * i32(5);
    var terms_0 : vec4<f32> = uLampMapRecords_0[_S83 + i32(4)];
    if((terms_0.z) <= 0.0f)
    {
        return -1.0f;
    }
    var row0_2 : vec4<f32> = uLampMapRecords_0[_S83];
    var row1_2 : vec4<f32> = uLampMapRecords_0[_S83 + i32(1)];
    var _S84 : f32 = terms_0.y;
    var _S85 : f32 = terms_0.x;
    if(deepest_0 <= (_S84 / (-1.0f - _S85)))
    {
        return 1.0f;
    }
    var _S86 : vec3<f32> = row0_2.xyz;
    var ndc_0 : vec2<f32> = vec2<f32>(dot(_S86, at_3) + row0_2.w, dot(row1_2.xyz, at_3) + row1_2.w) / vec2<f32>(deepest_0);
    var uv_3 : vec2<f32> = vec2<f32>(ndc_0.x * 0.5f + 0.5f, 0.5f - ndc_0.y * 0.5f);
    var width_0 : u32;
    var height_0 : u32;
    var layers_0 : u32;
    {var dim = textureDimensions((uLampMaps_0));((width_0)) = dim.x;((height_0)) = dim.y;((layers_0)) = textureNumLayers((uLampMaps_0));};
    var size_0 : vec2<i32> = vec2<i32>(i32(width_0), i32(height_0));
    var _S87 : f32 = 0.05000000074505806f + 0.00499999988824129f * deepest_0 + 2.0f * (deepest_0 * (2.0f / max(length(_S86), 0.00100000004749745f)) / f32(max(width_0, u32(1))));
    var texelAt_0 : vec2<f32> = uv_3 * vec2<f32>(size_0) - vec2<f32>(0.5f);
    var _S88 : vec2<f32> = floor(texelAt_0);
    var _S89 : vec2<i32> = vec2<i32>(i32(1));
    var _S90 : vec2<i32> = vec2<i32>(_S88) - _S89;
    var _S91 : vec2<f32> = texelAt_0 - _S88;
    var y_0 : i32 = i32(0);
    var lit_0 : f32 = 0.0f;
    var weightSum_0 : f32 = 0.0f;
    for(;;)
    {
        if(y_0 < i32(4))
        {
        }
        else
        {
            break;
        }
        var x_1 : i32 = i32(0);
        for(;;)
        {
            if(x_1 < i32(4))
            {
            }
            else
            {
                break;
            }
            var _S92 : vec4<i32> = vec4<i32>(clamp(_S90 + vec2<i32>(x_1, y_0), vec2<i32>(i32(0), i32(0)), size_0 - _S89), slot_2, i32(0));
            var tent_0 : vec2<f32> = saturate(vec2<f32>(1.0f) - abs(vec2<f32>(f32(x_1) - 1.0f, f32(y_0) - 1.0f) - _S91) / vec2<f32>(2.0f));
            var weight_2 : f32 = tent_0.x * tent_0.y;
            if((_S84 / min((textureLoad((uLampMaps_0), ((_S92)).xy, i32(((_S92)).z), ((_S92)).w)) * 2.0f - 1.0f - _S85, -9.99999997475242708e-07f)) >= (deepest_0 - _S87))
            {
                pointZ_0 = weight_2;
            }
            else
            {
                pointZ_0 = 0.0f;
            }
            var lit_1 : f32 = lit_0 + pointZ_0;
            var weightSum_1 : f32 = weightSum_0 + weight_2;
            x_1 = x_1 + i32(1);
            lit_0 = lit_1;
            weightSum_0 = weightSum_1;
        }
        y_0 = y_0 + i32(1);
    }
    return lit_0 / max(weightSum_0, 0.00009999999747379f);
}

fn SegmentVisibility_0( world_19 : vec3<f32>,  normal_15 : vec3<f32>,  target_0 : vec3<f32>,  stopShort_0 : f32) -> f32
{
    if((Globals_0.uShadowLevels_0) <= i32(0))
    {
        return 1.0f;
    }
    var finest_3 : f32 = Globals_0.uClipmapOrigin_0[i32(0)].w;
    var at_4 : vec3<f32> = world_19 + normal_15 * vec3<f32>(finest_3) * vec3<f32>(1.5f);
    var toTarget_0 : vec3<f32> = target_0 - at_4;
    var distance_3 : f32 = length(toTarget_0);
    var limit_1 : f32 = distance_3 - max(stopShort_0, finest_3);
    if(limit_1 <= 0.0f)
    {
        return 1.0f;
    }
    var _S93 : vec3<f32> = toTarget_0 / vec3<f32>(distance_3);
    var step_3 : i32 = i32(0);
    var travelled_9 : f32 = 0.0f;
    for(;;)
    {
        var _S94 : bool;
        if(step_3 < i32(48))
        {
            _S94 = travelled_9 < limit_1;
        }
        else
        {
            _S94 = false;
        }
        if(_S94)
        {
        }
        else
        {
            break;
        }
        var known_7 : bool;
        var raw_7 : f32;
        var resolved_10 : f32;
        var field_3 : f32 = DistanceAt_1(at_4 + _S93 * vec3<f32>(travelled_9), &(known_7), &(raw_7), &(resolved_10));
        if(!known_7)
        {
            return 1.0f;
        }
        var touchedAt_1 : f32;
        if(FieldStepHits_1(field_3, travelled_9, resolved_10, &(touchedAt_1)))
        {
            return 0.0f;
        }
        var travelled_10 : f32 = travelled_9 + max(field_3, resolved_10 * 0.25f);
        step_3 = step_3 + i32(1);
        travelled_9 = travelled_10;
    }
    return 1.0f;
}

@compute
@workgroup_size(8, 8, 1)
fn computeMain(@builtin(workgroup_id) groupId_1 : vec3<u32>, @builtin(local_invocation_id) localId_0 : vec3<u32>)
{
    var entry_2 : vec4<i32>;
    var at_5 : i32;
    var lightPage_2 : vec4<f32>;
    var cardPage_4 : vec4<f32>;
    var _S95 : bool = GroupTile_0(groupId_1, &(entry_2), &(at_5), &(lightPage_2), &(cardPage_4));
    if(!_S95)
    {
        return;
    }
    var within_1 : vec2<i32> = entry_2.yz * vec2<i32>(i32(8)) + vec2<i32>(localId_0.xy);
    var _S96 : bool;
    if((within_1.x) >= i32(lightPage_2.z))
    {
        _S96 = true;
    }
    else
    {
        _S96 = (within_1.y) >= i32(lightPage_2.w);
    }
    if(_S96)
    {
        return;
    }
    var uv_4 : vec2<f32>;
    var cardUv_3 : vec2<f32>;
    var albedo_1 : vec4<f32> = TexelAlbedo_0(lightPage_2, cardPage_4, within_1, &(uv_4), &(cardUv_3));
    var pageAt_0 : vec2<i32> = vec2<i32>(i32(lightPage_2.x), i32(lightPage_2.y));
    if((albedo_1.w) < 0.5f)
    {
        textureStore((uDirect_0), (vec2<u32>(pageAt_0 + within_1)), (vec4<f32>(0.0f, 0.0f, 0.0f, 0.0f)));
        return;
    }
    var world_20 : vec3<f32>;
    var worldNormal_1 : vec3<f32>;
    TexelSurface_0(at_5, uv_4, cardUv_3, &(world_20), &(worldNormal_1));
    var texelAt_1 : vec2<i32> = pageAt_0 + within_1;
    var traced_0 : f32;
    if((Globals_0.uMode_0) == i32(1))
    {
        if((((((texelAt_1.x) | ((texelAt_1.y)))) & (i32(3)))) != i32(0))
        {
            textureStore((uDirect_0), (vec2<u32>(texelAt_1)), (vec4<f32>(0.0f, 0.0f, 0.0f, 0.0f)));
            return;
        }
        var probeCoord_5 : vec2<i32> = (texelAt_1 >> (vec2<u32>(u32(2))));
        uProbePos_0[probeCoord_5.y * Globals_0.uProbeWidth_0 + probeCoord_5.x] = vec4<f32>(world_20, 1.0f);
        var _S97 : vec2<f32> = ProbeCenter_0(probeCoord_5, Globals_0.uTemporalIndex_0);
        var gathered_0 : vec3<f32> = Gather_0(world_20, worldNormal_1, _S97);
        var _S98 : vec3<i32> = vec3<i32>(texelAt_1, i32(0));
        var previous_0 : vec4<f32> = (textureLoad((uDirectHistory_0), ((_S98)).xy, ((_S98)).z));
        if((Globals_0.uTemporalIndex_0) > i32(0))
        {
            traced_0 = previous_0.w;
        }
        else
        {
            traced_0 = 0.0f;
        }
        var limit_2 : f32 = f32(Globals_0.uMaxFrames_0);
        textureStore((uDirect_0), (vec2<u32>(texelAt_1)), (vec4<f32>(mix(previous_0.xyz, gathered_0, vec3<f32>((1.0f / (1.0f + min(traced_0, limit_2))))), min(traced_0 + 1.0f, limit_2 + 1.0f))));
        return;
    }
    var corner_0 : i32;
    var indirectSum_0 : vec3<f32>;
    if((Globals_0.uMode_0) == i32(2))
    {
        var _S99 : vec3<i32> = vec3<i32>(texelAt_1, i32(0));
        var direct_0 : vec3<f32> = (textureLoad((uPrevious_0), ((_S99)).xy, ((_S99)).z)).xyz;
        var _S100 : vec2<i32> = pageAt_0 + vec2<i32>(i32(lightPage_2.z), i32(lightPage_2.w));
        var probe00_0 : vec2<i32> = (texelAt_1 >> (vec2<u32>(u32(2))));
        var _S101 : vec2<f32> = vec2<f32>(1.0f);
        var _S102 : vec2<f32> = (vec2<f32>(texelAt_1 - probe00_0 * vec2<i32>(i32(4))) + _S101) / vec2<f32>(6.0f);
        const _S103 : vec3<f32> = vec3<f32>(0.0f, 0.0f, 0.0f);
        corner_0 = i32(0);
        indirectSum_0 = _S103;
        traced_0 = 0.0f;
        for(;;)
        {
            if(corner_0 < i32(4))
            {
            }
            else
            {
                break;
            }
            var _S104 : i32 = corner_0;
            var probeCoord_6 : vec2<i32> = probe00_0 + kProbeCorner_0[corner_0];
            var position_3 : vec3<f32>;
            var _S105 : bool = ProbeUsable_1(probeCoord_6, pageAt_0, _S100, &(position_3));
            if(!_S105)
            {
                corner_0 = corner_0 + i32(1);
                continue;
            }
            if((ProbePlaneAccept_0(world_20, worldNormal_1, position_3)) <= 0.0f)
            {
                corner_0 = corner_0 + i32(1);
                continue;
            }
            var share_0 : vec2<f32> = mix(_S101 - _S102, _S102, vec2<f32>(kProbeCorner_0[_S104]));
            var w_0 : f32 = share_0.x * share_0.y;
            var indirectWeight_0 : f32 = traced_0 + w_0;
            indirectSum_0 = indirectSum_0 + FilteredProbe_0(probeCoord_6, pageAt_0, _S100, world_20, worldNormal_1) * vec3<f32>(w_0);
            traced_0 = indirectWeight_0;
            corner_0 = corner_0 + i32(1);
        }
        var indirect_0 : vec3<f32>;
        if(traced_0 > 0.0f)
        {
            indirect_0 = indirectSum_0 / vec3<f32>(traced_0);
        }
        else
        {
            indirect_0 = ProbeIrradiance_0(probe00_0);
        }
        textureStore((uDirect_0), (vec2<u32>(texelAt_1)), (vec4<f32>((direct_0 + indirect_0) * (BoostAlbedo_0(albedo_1.xyz, Globals_0.uBoost_0) / vec3<f32>(3.14159274101257324f)), 1.0f)));
        return;
    }
    if((Globals_0.uMode_0) == i32(3))
    {
        var _S106 : vec3<i32> = vec3<i32>(texelAt_1, i32(0));
        textureStore((uDirect_0), (vec2<u32>(texelAt_1)), (vec4<f32>((textureLoad((uPrevious_0), ((_S106)).xy, ((_S106)).z)).xyz * (BoostAlbedo_0(albedo_1.xyz, Globals_0.uBoost_0) / vec3<f32>(3.14159274101257324f)), 1.0f)));
        return;
    }
    if((Globals_0.uProbe_0) != i32(0))
    {
        textureStore((uDirect_0), (vec2<u32>(texelAt_1)), (Probed_0(world_20, worldNormal_1, cardUv_3, texelAt_1)));
        return;
    }
    var _S107 : f32 = max(dot(worldNormal_1, Globals_0.uSunDirection_0), 0.0f);
    if(_S107 > 0.0f)
    {
        traced_0 = SunVisibility_0(world_20, worldNormal_1);
    }
    else
    {
        traced_0 = 1.0f;
    }
    var _S108 : vec3<f32> = Globals_0.uSunColour_0 * vec3<f32>(_S107) * vec3<f32>(traced_0) + SkyLight_0(world_20, worldNormal_1);
    corner_0 = i32(0);
    indirectSum_0 = _S108;
    for(;;)
    {
        if(corner_0 < (Globals_0.uLightCount_0))
        {
        }
        else
        {
            break;
        }
        var _S109 : i32 = corner_0 * i32(3);
        var place_0 : vec4<f32> = uLights_0[_S109];
        var emission_0 : vec4<f32> = uLights_0[_S109 + i32(1)];
        var facing_2 : vec4<f32> = uLights_0[_S109 + i32(2)];
        var _S110 : vec3<f32> = place_0.xyz;
        var delta_1 : vec3<f32> = _S110 - world_20;
        var squared_0 : f32 = dot(delta_1, delta_1);
        var _S111 : f32 = max(place_0.w * 3.0f, 2.0f);
        var reachSquared_0 : f32 = _S111 * _S111;
        if(squared_0 >= reachSquared_0)
        {
            corner_0 = corner_0 + i32(1);
            continue;
        }
        var toLight_0 : vec3<f32> = delta_1 * vec3<f32>(rsqrt_0(max(squared_0, 9.99999997475242708e-07f)));
        var _S112 : f32 = max(dot(worldNormal_1, toLight_0), 0.0f);
        if(_S112 <= 0.0f)
        {
            corner_0 = corner_0 + i32(1);
            continue;
        }
        var falloff_0 : f32 = 1.0f / (squared_0 + 1.0f);
        var window_0 : f32 = clamp(1.0f - squared_0 * squared_0 / (reachSquared_0 * reachSquared_0), 0.0f, 1.0f);
        var _S113 : vec3<f32> = facing_2.xyz;
        var aim_0 : f32;
        if((dot(_S113, _S113)) > 0.25f)
        {
            aim_0 = max(dot(normalize(_S113), (vec3<f32>(0) - toLight_0)), 0.0f);
        }
        else
        {
            aim_0 = 1.0f;
        }
        var contribution_0 : f32 = emission_0.w * Globals_0.uLocalScale_0 * _S112 * falloff_0 * window_0 * window_0 * aim_0;
        var _S114 : vec3<f32> = emission_0.xyz;
        if((contribution_0 * dot(_S114, vec3<f32>(0.2125999927520752f, 0.71520000696182251f, 0.07220000028610229f))) < 0.00000999999974738f)
        {
            corner_0 = corner_0 + i32(1);
            continue;
        }
        var reaches_0 : f32 = CardLampMapVisibility_0((bitcast<i32>((facing_2.w))), world_20, worldNormal_1);
        var reaches_1 : f32;
        if(reaches_0 < 0.0f)
        {
            reaches_1 = SegmentVisibility_0(world_20, worldNormal_1, _S110, 0.11999999731779099f);
        }
        else
        {
            reaches_1 = reaches_0;
        }
        if(reaches_1 <= 0.0f)
        {
            corner_0 = corner_0 + i32(1);
            continue;
        }
        indirectSum_0 = indirectSum_0 + _S114 * vec3<f32>((contribution_0 * reaches_1));
        corner_0 = corner_0 + i32(1);
    }
    textureStore((uDirect_0), (vec2<u32>(texelAt_1)), (vec4<f32>(indirectSum_0, 1.0f)));
    return;
}

