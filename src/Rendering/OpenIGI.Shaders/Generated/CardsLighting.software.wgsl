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
    @align(16) uActiveRadius_0 : f32,
    @align(4) uLightCount_0 : i32,
    @align(8) uLocalScale_0 : f32,
    @align(4) uProbeWidth_0 : i32,
    @align(16) uProbeHeight_0 : i32,
    @align(4) uTemporalIndex_0 : i32,
    @align(8) uMaxFrames_0 : i32,
    @align(4) uJitterStride_0 : i32,
    @align(16) uMode_0 : i32,
    @align(4) uBoost_0 : f32,
    @align(8) uSkyShValid_0 : i32,
    @align(4) uProbe_0 : i32,
    @align(16) uProbeInvExtent_0 : f32,
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

fn rsqrt_0( x_0 : f32) -> f32
{
    return 1.0f / sqrt(x_0);
}

var<private> kProbeNeighbour_0 : array<vec2<i32>, i32(4)> = array<vec2<i32>, i32(4)>( vec2<i32>(i32(-1), i32(0)), vec2<i32>(i32(1), i32(0)), vec2<i32>(i32(0), i32(-1)), vec2<i32>(i32(0), i32(1)) );
var<private> kProbeCorner_0 : array<vec2<i32>, i32(4)> = array<vec2<i32>, i32(4)>( vec2<i32>(i32(0), i32(0)), vec2<i32>(i32(1), i32(0)), vec2<i32>(i32(0), i32(1)), vec2<i32>(i32(1), i32(1)) );
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
    var _S1 : i32 = frame_0 / max(Globals_0.uJitterStride_0, i32(1));
    var n_0 : i32 = (_S1 & (i32(65535)));
    return fract(vec2<f32>(fract(f32(n_0) * 0.6180340051651001f), RadicalInverse_0(n_0)) + vec2<f32>(rotation_0, fract(rotation_0 * 1.6180340051651001f)));
}

struct HemisphereBasis_0
{
     tangent_0 : vec3<f32>,
     bitangent_0 : vec3<f32>,
     normal_0 : vec3<f32>,
};

fn BasisAround_0( normal_1 : vec3<f32>) -> HemisphereBasis_0
{
    var basis_0 : HemisphereBasis_0;
    basis_0.normal_0 = normal_1;
    var _S2 : f32 = normal_1.z;
    if(_S2 < -0.99999988079071045f)
    {
        basis_0.tangent_0 = vec3<f32>(0.0f, -1.0f, 0.0f);
        basis_0.bitangent_0 = vec3<f32>(-1.0f, 0.0f, 0.0f);
        return basis_0;
    }
    var a_0 : f32 = 1.0f / (1.0f + _S2);
    var _S3 : f32 = normal_1.x;
    var _S4 : f32 = - _S3;
    var _S5 : f32 = normal_1.y;
    var b_0 : f32 = _S4 * _S5 * a_0;
    basis_0.tangent_0 = vec3<f32>(1.0f - _S3 * _S3 * a_0, b_0, _S4);
    basis_0.bitangent_0 = vec3<f32>(b_0, 1.0f - _S5 * _S5 * a_0, - _S5);
    return basis_0;
}

fn Orient_0( basis_1 : HemisphereBasis_0,  local_0 : vec3<f32>) -> vec3<f32>
{
    return basis_1.tangent_0 * vec3<f32>(local_0.x) + basis_1.bitangent_0 * vec3<f32>(local_0.y) + basis_1.normal_0 * vec3<f32>(local_0.z);
}

fn DistanceAt_0( world_0 : vec3<f32>,  known_0 : ptr<function, bool>,  raw_0 : ptr<function, f32>,  resolved_0 : ptr<function, f32>) -> f32
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
        var _S6 : f32 = Globals_0.uClipmapOrigin_0[level_0].w;
        var voxel_0 : vec3<f32> = (world_0 - Globals_0.uClipmapOrigin_0[level_0].xyz) / vec3<f32>(_S6);
        var _S7 : bool;
        if((any((voxel_0 < vec3<f32>(0.5f, 0.5f, 0.5f)))))
        {
            _S7 = true;
        }
        else
        {
            _S7 = (any((voxel_0 > vec3<f32>(f32(Globals_0.uShadowResolution_0) - 0.5f, f32(Globals_0.uShadowResolution_0) - 0.5f, f32(Globals_0.uShadowResolution_0) - 0.5f))));
        }
        if(_S7)
        {
            level_0 = level_0 + i32(1);
            continue;
        }
        var uvw_0 : vec3<f32> = vec3<f32>(voxel_0.x / f32(Globals_0.uShadowResolution_0), voxel_0.y / f32(Globals_0.uShadowResolution_0), (f32(level_0) * f32(Globals_0.uShadowResolution_0) + voxel_0.z) / f32(Globals_0.uShadowResolution_0 * Globals_0.uShadowLevels_0));
        (*known_0) = true;
        (*raw_0) = (textureSampleLevel((uClipmaps_0), (uClipmapsSampler_0), (uvw_0), (0.0f))).x;
        (*resolved_0) = _S6;
        return ((*raw_0) * 2.0f - 1.0f) * Globals_0.uClipmapRange_0.data_0[level_0].x;
    }
    return 1.0e+06f;
}

fn DistanceAt_1( world_1 : vec3<f32>,  known_1 : ptr<function, bool>,  raw_1 : ptr<function, f32>,  resolved_1 : ptr<function, f32>) -> f32
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
        var _S8 : f32 = Globals_0.uClipmapOrigin_0[level_1].w;
        var voxel_1 : vec3<f32> = (world_1 - Globals_0.uClipmapOrigin_0[level_1].xyz) / vec3<f32>(_S8);
        var _S9 : bool;
        if((any((voxel_1 < vec3<f32>(0.5f, 0.5f, 0.5f)))))
        {
            _S9 = true;
        }
        else
        {
            _S9 = (any((voxel_1 > vec3<f32>(f32(Globals_0.uShadowResolution_0) - 0.5f, f32(Globals_0.uShadowResolution_0) - 0.5f, f32(Globals_0.uShadowResolution_0) - 0.5f))));
        }
        if(_S9)
        {
            level_1 = level_1 + i32(1);
            continue;
        }
        var uvw_1 : vec3<f32> = vec3<f32>(voxel_1.x / f32(Globals_0.uShadowResolution_0), voxel_1.y / f32(Globals_0.uShadowResolution_0), (f32(level_1) * f32(Globals_0.uShadowResolution_0) + voxel_1.z) / f32(Globals_0.uShadowResolution_0 * Globals_0.uShadowLevels_0));
        (*known_1) = true;
        (*raw_1) = (textureSampleLevel((uClipmaps_0), (uClipmapsSampler_0), (uvw_1), (0.0f))).x;
        (*resolved_1) = _S8;
        return ((*raw_1) * 2.0f - 1.0f) * Globals_0.uClipmapRange_0.data_0[level_1].x;
    }
    return 1.0e+06f;
}

fn DistanceAt_2( world_2 : vec3<f32>,  known_2 : ptr<function, bool>,  raw_2 : ptr<function, f32>,  resolved_2 : ptr<function, f32>) -> f32
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
        var _S10 : f32 = Globals_0.uClipmapOrigin_0[level_2].w;
        var voxel_2 : vec3<f32> = (world_2 - Globals_0.uClipmapOrigin_0[level_2].xyz) / vec3<f32>(_S10);
        var _S11 : bool;
        if((any((voxel_2 < vec3<f32>(0.5f, 0.5f, 0.5f)))))
        {
            _S11 = true;
        }
        else
        {
            _S11 = (any((voxel_2 > vec3<f32>(f32(Globals_0.uShadowResolution_0) - 0.5f, f32(Globals_0.uShadowResolution_0) - 0.5f, f32(Globals_0.uShadowResolution_0) - 0.5f))));
        }
        if(_S11)
        {
            level_2 = level_2 + i32(1);
            continue;
        }
        var uvw_2 : vec3<f32> = vec3<f32>(voxel_2.x / f32(Globals_0.uShadowResolution_0), voxel_2.y / f32(Globals_0.uShadowResolution_0), (f32(level_2) * f32(Globals_0.uShadowResolution_0) + voxel_2.z) / f32(Globals_0.uShadowResolution_0 * Globals_0.uShadowLevels_0));
        (*known_2) = true;
        (*raw_2) = (textureSampleLevel((uClipmaps_0), (uClipmapsSampler_0), (uvw_2), (0.0f))).x;
        (*resolved_2) = _S10;
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

fn VisibilityAt_0( world_3 : vec3<f32>,  normal_2 : vec3<f32>,  direction_0 : vec3<f32>,  hit_0 : ptr<function, vec3<f32>>) -> f32
{
    (*hit_0) = world_3;
    if((Globals_0.uShadowLevels_0) <= i32(0))
    {
        return 1.0f;
    }
    var _S12 : vec3<f32> = world_3 + normal_2 * vec3<f32>(Globals_0.uClipmapOrigin_0[i32(0)].w) * vec3<f32>(1.5f);
    var _S13 : f32 = Globals_0.uClipmapOrigin_0[i32(0)].w * f32(Globals_0.uShadowResolution_0) * f32((i32(1) << (u32(Globals_0.uShadowLevels_0 - i32(1)))));
    var step_0 : i32 = i32(0);
    var travelled_3 : f32 = 0.0f;
    for(;;)
    {
        var _S14 : bool;
        if(step_0 < i32(64))
        {
            _S14 = travelled_3 < _S13;
        }
        else
        {
            _S14 = false;
        }
        if(_S14)
        {
        }
        else
        {
            break;
        }
        var known_3 : bool;
        var raw_3 : f32;
        var resolved_6 : f32;
        var distance_0 : f32 = DistanceAt_2(_S12 + direction_0 * vec3<f32>(travelled_3), &(known_3), &(raw_3), &(resolved_6));
        if(!known_3)
        {
            return 1.0f;
        }
        var hitDistance_3 : f32;
        var _S15 : bool = FieldStepHits_2(distance_0, travelled_3, resolved_6, &(hitDistance_3));
        if(_S15)
        {
            (*hit_0) = _S12 + direction_0 * vec3<f32>(hitDistance_3);
            return 0.0f;
        }
        var travelled_4 : f32 = travelled_3 + max(distance_0, resolved_6 * 0.25f);
        step_0 = step_0 + i32(1);
        travelled_3 = travelled_4;
    }
    return 1.0f;
}

fn VisibilityAt_1( world_4 : vec3<f32>,  normal_3 : vec3<f32>,  direction_1 : vec3<f32>,  hit_1 : ptr<function, vec3<f32>>) -> f32
{
    (*hit_1) = world_4;
    if((Globals_0.uShadowLevels_0) <= i32(0))
    {
        return 1.0f;
    }
    var _S16 : vec3<f32> = world_4 + normal_3 * vec3<f32>(Globals_0.uClipmapOrigin_0[i32(0)].w) * vec3<f32>(1.5f);
    var _S17 : f32 = Globals_0.uClipmapOrigin_0[i32(0)].w * f32(Globals_0.uShadowResolution_0) * f32((i32(1) << (u32(Globals_0.uShadowLevels_0 - i32(1)))));
    var step_1 : i32 = i32(0);
    var travelled_5 : f32 = 0.0f;
    for(;;)
    {
        var _S18 : bool;
        if(step_1 < i32(64))
        {
            _S18 = travelled_5 < _S17;
        }
        else
        {
            _S18 = false;
        }
        if(_S18)
        {
        }
        else
        {
            break;
        }
        var known_4 : bool;
        var raw_4 : f32;
        var resolved_7 : f32;
        var distance_1 : f32 = DistanceAt_2(_S16 + direction_1 * vec3<f32>(travelled_5), &(known_4), &(raw_4), &(resolved_7));
        if(!known_4)
        {
            return 1.0f;
        }
        var hitDistance_4 : f32;
        var _S19 : bool = FieldStepHits_2(distance_1, travelled_5, resolved_7, &(hitDistance_4));
        if(_S19)
        {
            (*hit_1) = _S16 + direction_1 * vec3<f32>(hitDistance_4);
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

fn ProjectOntoCard_0( page_0 : i32,  world_5 : vec3<f32>,  directionW_0 : vec3<f32>,  across_0 : ptr<function, vec2<f32>>,  halfSize_0 : ptr<function, vec2<f32>>,  behind_0 : ptr<function, f32>,  depth_0 : ptr<function, f32>,  facing_0 : ptr<function, vec3<f32>>,  directionLocal_0 : ptr<function, vec3<f32>>,  litRect_0 : ptr<function, vec4<f32>>,  cardPage_0 : ptr<function, vec4<f32>>)
{
    var _S20 : i32 = page_0 * i32(10);
    (*litRect_0) = uPages_0[_S20];
    (*cardPage_0) = uPages_0[_S20 + i32(1)];
    var centreAndHalfWidth_0 : vec4<f32> = uPages_0[_S20 + i32(2)];
    var rightAndHalfHeight_0 : vec4<f32> = uPages_0[_S20 + i32(3)];
    var upAndDepth_0 : vec4<f32> = uPages_0[_S20 + i32(4)];
    var direction_2 : vec4<f32> = uPages_0[_S20 + i32(5)];
    var row0_0 : vec4<f32> = uPages_0[_S20 + i32(6)];
    var row1_0 : vec4<f32> = uPages_0[_S20 + i32(7)];
    var row2_0 : vec4<f32> = uPages_0[_S20 + i32(8)];
    var delta_0 : vec3<f32> = world_5 - vec3<f32>(row0_0.w, row1_0.w, row2_0.w);
    var _S21 : vec3<f32> = row0_0.xyz;
    var _S22 : vec3<f32> = row1_0.xyz;
    var _S23 : vec3<f32> = row2_0.xyz;
    var local_1 : vec3<f32> = vec3<f32>(dot(_S21, delta_0), dot(_S22, delta_0), dot(_S23, delta_0));
    (*directionLocal_0) = vec3<f32>(dot(_S21, directionW_0), dot(_S22, directionW_0), dot(_S23, directionW_0));
    var relative_0 : vec3<f32> = local_1 - centreAndHalfWidth_0.xyz;
    var _S24 : vec2<f32> = max(vec2<f32>(centreAndHalfWidth_0.w, rightAndHalfHeight_0.w), vec2<f32>(9.99999997475242708e-07f, 9.99999997475242708e-07f));
    (*halfSize_0) = _S24;
    (*across_0) = vec2<f32>(dot(relative_0, rightAndHalfHeight_0.xyz), dot(relative_0, upAndDepth_0.xyz)) / _S24;
    var _S25 : vec3<f32> = direction_2.xyz;
    (*behind_0) = - dot(relative_0, _S25);
    (*depth_0) = upAndDepth_0.w;
    (*facing_0) = _S25;
    return;
}

fn LookupCardWithin_0( world_6 : vec3<f32>,  arrivingW_0 : vec3<f32>,  skin_0 : f32,  found_0 : ptr<function, vec2<f32>>,  litFound_0 : ptr<function, vec2<f32>>,  agreement_0 : ptr<function, f32>,  cardDepth_0 : ptr<function, f32>) -> bool
{
    (*cardDepth_0) = 0.0f;
    const _S26 : vec2<f32> = vec2<f32>(0.0f, 0.0f);
    (*found_0) = _S26;
    (*litFound_0) = _S26;
    (*agreement_0) = 1.0e+09f;
    if((Globals_0.uLookupBuckets_0) <= i32(0))
    {
        return false;
    }
    var _S27 : vec2<i32> = uCells_0[LookupBucket_0(vec3<i32>(floor((world_6 - Globals_0.uLookupMin_0) / vec3<f32>(Globals_0.uLookupCell_0))))];
    var albedoWidth_0 : u32;
    var albedoHeight_0 : u32;
    {var dim = textureDimensions((uAlbedo_0));((albedoWidth_0)) = dim.x;((albedoHeight_0)) = dim.y;};
    var _S28 : vec2<f32> = vec2<f32>(f32(albedoWidth_0), f32(albedoHeight_0));
    var settled_0 : bool = false;
    var anyFound_0 : bool = false;
    var walk_0 : i32 = i32(0);
    for(;;)
    {
        var _S29 : bool;
        if(walk_0 < i32(2))
        {
            _S29 = !settled_0;
        }
        else
        {
            _S29 = false;
        }
        if(_S29)
        {
        }
        else
        {
            break;
        }
        var _S30 : bool = walk_0 == i32(0);
        var _S31 : vec2<i32>;
        if(_S30)
        {
            _S31 = _S27;
        }
        else
        {
            _S31 = uCells_0[Globals_0.uLookupBuckets_0];
        }
        var settled_1 : bool = settled_0;
        var anyFound_1 : bool = anyFound_0;
        var slot_0 : i32 = i32(0);
        for(;;)
        {
            var _S32 : bool;
            if(slot_0 < (_S31.y))
            {
                _S32 = !settled_1;
            }
            else
            {
                _S32 = false;
            }
            if(_S32)
            {
            }
            else
            {
                break;
            }
            var page_1 : i32 = uCellPages_0[_S31.x + slot_0];
            var _S33 : i32 = uCellPages_0[_S31.x + slot_0] * i32(10);
            if(((uPages_0[_S33 + i32(5)].w) > 0.5f) == _S30)
            {
                slot_0 = slot_0 + i32(1);
                continue;
            }
            var bound_0 : vec4<f32> = uPages_0[_S33 + i32(9)];
            var toCentre_0 : vec3<f32> = world_6 - bound_0.xyz;
            var _S34 : f32 = bound_0.w;
            if((dot(toCentre_0, toCentre_0)) > (_S34 * _S34))
            {
                slot_0 = slot_0 + i32(1);
                continue;
            }
            var across_1 : vec2<f32>;
            var halfSize_1 : vec2<f32>;
            var behind_1 : f32;
            var depth_1 : f32;
            var facing_1 : vec3<f32>;
            var arrivingLocal_0 : vec3<f32>;
            var litRect_1 : vec4<f32>;
            var cardPage_1 : vec4<f32>;
            ProjectOntoCard_0(page_1, world_6, (vec3<f32>(0) - arrivingW_0), &(across_1), &(halfSize_1), &(behind_1), &(depth_1), &(facing_1), &(arrivingLocal_0), &(litRect_1), &(cardPage_1));
            var _S35 : bool;
            if((abs(across_1.x)) > 1.0f)
            {
                _S35 = true;
            }
            else
            {
                _S35 = (abs(across_1.y)) > 1.0f;
            }
            if(_S35)
            {
                slot_0 = slot_0 + i32(1);
                continue;
            }
            var _S36 : bool;
            if((dot(arrivingW_0, arrivingW_0)) > 0.5f)
            {
                _S36 = (dot(facing_1, arrivingLocal_0)) < -0.05000000074505806f;
            }
            else
            {
                _S36 = false;
            }
            if(_S36)
            {
                slot_0 = slot_0 + i32(1);
                continue;
            }
            var _S37 : bool;
            if(behind_1 < (- skin_0))
            {
                _S37 = true;
            }
            else
            {
                _S37 = behind_1 > (depth_1 + skin_0);
            }
            if(_S37)
            {
                slot_0 = slot_0 + i32(1);
                continue;
            }
            var _S38 : vec2<f32> = vec2<f32>(0.5f);
            var uv_0 : vec2<f32> = across_1 * _S38 + _S38;
            var texel_0 : vec2<f32> = (cardPage_1.xy + uv_0 * cardPage_1.zw) / _S28;
            if(((textureSampleLevel((uAlbedo_0), (uAlbedoSampler_0), (texel_0), (0.0f))).w) < 0.5f)
            {
                slot_0 = slot_0 + i32(1);
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
                var _S39 : bool = error_0 < 0.01999999955296516f;
                (*litFound_0) = (litRect_1.xy + uv_0 * litRect_1.zw) / Globals_0.uAtlasSize_0.xy;
                settled_2 = _S39;
                anyFound_2 = true;
            }
            else
            {
                settled_2 = settled_1;
                anyFound_2 = anyFound_1;
            }
            settled_1 = settled_2;
            anyFound_1 = anyFound_2;
            slot_0 = slot_0 + i32(1);
        }
        var _S40 : i32 = walk_0 + i32(1);
        settled_0 = settled_1;
        anyFound_0 = anyFound_1;
        walk_0 = _S40;
    }
    return anyFound_0;
}

fn LookupCardHit_0( world_7 : vec3<f32>,  rayDirection_0 : vec3<f32>,  found_1 : ptr<function, vec2<f32>>,  litFound_1 : ptr<function, vec2<f32>>,  agreement_1 : ptr<function, f32>) -> bool
{
    var depth_2 : f32;
    var _S41 : bool = LookupCardWithin_0(world_7, rayDirection_0, 0.25f, &((*found_1)), &((*litFound_1)), &((*agreement_1)), &(depth_2));
    return _S41;
}

fn ShBasis_0( d_0 : vec3<f32>) -> vec4<f32>
{
    return vec4<f32>(0.28209498524665833f, -0.48860299587249756f * d_0.y, 0.48860299587249756f * d_0.z, -0.48860299587249756f * d_0.x);
}

fn ShIrradianceTransfer_0( normal_4 : vec3<f32>) -> vec4<f32>
{
    var basis_2 : vec4<f32> = ShBasis_0(normal_4);
    return vec4<f32>(basis_2.x * 3.14159274101257324f, basis_2.yzw * vec3<f32>(2.09439516067504883f));
}

fn Gather_0( world_8 : vec3<f32>,  normal_5 : vec3<f32>,  center_0 : vec2<f32>) -> vec3<f32>
{
    var _S42 : bool;
    if((Globals_0.uShadowLevels_0) <= i32(0))
    {
        _S42 = true;
    }
    else
    {
        _S42 = (Globals_0.uLookupBuckets_0) <= i32(0);
    }
    if(_S42)
    {
        return vec3<f32>(0.0f, 0.0f, 0.0f);
    }
    var _S43 : HemisphereBasis_0 = BasisAround_0(normal_5);
    var _S44 : f32 = Globals_0.uClipmapOrigin_0[i32(0)].w;
    const _S45 : vec4<f32> = vec4<f32>(0.0f, 0.0f, 0.0f, 0.0f);
    var cell_1 : i32 = i32(0);
    var red_0 : vec4<f32> = _S45;
    var green_0 : vec4<f32> = _S45;
    var blue_0 : vec4<f32> = _S45;
    for(;;)
    {
        if(cell_1 < i32(16))
        {
        }
        else
        {
            break;
        }
        var square_0 : vec2<f32> = (vec2<f32>(f32(cell_1 % i32(4)), f32(cell_1 / i32(4))) + center_0) / vec2<f32>(4.0f);
        var phi_0 : f32 = 6.28318548202514648f * square_0.x;
        var cosTheta_0 : f32 = square_0.y;
        var sinTheta_0 : f32 = sqrt(max(0.0f, 1.0f - cosTheta_0 * cosTheta_0));
        var direction_3 : vec3<f32> = Orient_0(_S43, vec3<f32>(sinTheta_0 * cos(phi_0), sinTheta_0 * sin(phi_0), cosTheta_0));
        const _S46 : vec3<f32> = vec3<f32>(0.0f, 0.0f, 0.0f);
        var hit_2 : vec3<f32>;
        var _S47 : f32 = VisibilityAt_1(world_8, normal_5, direction_3, &(hit_2));
        var radiance_0 : vec3<f32>;
        if(_S47 <= 0.5f)
        {
            var found_2 : vec2<f32>;
            var litFound_2 : vec2<f32>;
            var agreement_2 : f32;
            var _S48 : bool = LookupCardHit_0(hit_2, direction_3, &(found_2), &(litFound_2), &(agreement_2));
            if(_S48)
            {
                radiance_0 = (textureSampleLevel((uPrevious_0), (uPreviousSampler_0), (litFound_2), (0.0f))).xyz * vec3<f32>(smoothstep(1.5f * _S44, 2.0f * _S44, length(hit_2 - world_8)));
            }
            else
            {
                radiance_0 = _S46;
            }
        }
        else
        {
            radiance_0 = _S46;
        }
        var basisHere_0 : vec4<f32> = ShBasis_0(direction_3);
        var red_1 : vec4<f32> = red_0 + basisHere_0 * vec4<f32>(radiance_0.x);
        var green_1 : vec4<f32> = green_0 + basisHere_0 * vec4<f32>(radiance_0.y);
        var blue_1 : vec4<f32> = blue_0 + basisHere_0 * vec4<f32>(radiance_0.z);
        cell_1 = cell_1 + i32(1);
        red_0 = red_1;
        green_0 = green_1;
        blue_0 = blue_1;
    }
    var transfer_0 : vec4<f32> = ShIrradianceTransfer_0(normal_5);
    return max(vec3<f32>(dot(red_0, transfer_0), dot(green_0, transfer_0), dot(blue_0, transfer_0)) * vec3<f32>(0.39269909262657166f), vec3<f32>(0.0f, 0.0f, 0.0f));
}

fn ProbeUsable_0( probeCoord_1 : vec2<i32>,  pageMin_0 : vec2<i32>,  pageMax_0 : vec2<i32>,  position_0 : ptr<function, vec3<f32>>) -> bool
{
    (*position_0) = vec3<f32>(0.0f, 0.0f, 0.0f);
    var _S49 : bool;
    if((any((probeCoord_1 < vec2<i32>(i32(0), i32(0))))))
    {
        _S49 = true;
    }
    else
    {
        _S49 = (probeCoord_1.x) >= (Globals_0.uProbeWidth_0);
    }
    if(_S49)
    {
        _S49 = true;
    }
    else
    {
        _S49 = (probeCoord_1.y) >= (Globals_0.uProbeHeight_0);
    }
    if(_S49)
    {
        return false;
    }
    var texel_1 : vec2<i32> = probeCoord_1 * vec2<i32>(i32(4));
    if((any((texel_1 < pageMin_0))))
    {
        _S49 = true;
    }
    else
    {
        _S49 = (any((texel_1 >= pageMax_0)));
    }
    if(_S49)
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
    var _S50 : bool;
    if((any((probeCoord_2 < vec2<i32>(i32(0), i32(0))))))
    {
        _S50 = true;
    }
    else
    {
        _S50 = (probeCoord_2.x) >= (Globals_0.uProbeWidth_0);
    }
    if(_S50)
    {
        _S50 = true;
    }
    else
    {
        _S50 = (probeCoord_2.y) >= (Globals_0.uProbeHeight_0);
    }
    if(_S50)
    {
        return false;
    }
    var texel_2 : vec2<i32> = probeCoord_2 * vec2<i32>(i32(4));
    if((any((texel_2 < pageMin_1))))
    {
        _S50 = true;
    }
    else
    {
        _S50 = (any((texel_2 >= pageMax_1)));
    }
    if(_S50)
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
    var _S51 : f32;
    if((abs(dot(offset_0, texelNormal_0)) / (length(offset_0) + 0.00999999977648258f)) < 0.25775998830795288f)
    {
        _S51 = 1.0f;
    }
    else
    {
        _S51 = 0.0f;
    }
    return _S51;
}

fn ProbeIrradiance_0( probeCoord_3 : vec2<i32>) -> vec3<f32>
{
    return (textureSampleLevel((uIndirectRead_0), (uIndirectReadSampler_0), ((vec2<f32>(probeCoord_3 * vec2<i32>(i32(4))) + vec2<f32>(0.5f)) / Globals_0.uAtlasSize_0.xy), (0.0f))).xyz;
}

fn FilteredProbe_0( probeCoord_4 : vec2<i32>,  pageMin_2 : vec2<i32>,  pageMax_2 : vec2<i32>,  texelWorld_1 : vec3<f32>,  texelNormal_1 : vec3<f32>) -> vec3<f32>
{
    var _S52 : vec3<f32> = ProbeIrradiance_0(probeCoord_4) * vec3<f32>(2.0f);
    var k_0 : i32 = i32(0);
    var sum_0 : vec3<f32> = _S52;
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
        var _S53 : bool = ProbeUsable_0(neighbour_0, pageMin_2, pageMax_2, &(position_2));
        if(!_S53)
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

fn LookupCard_0( world_9 : vec3<f32>,  found_3 : ptr<function, vec2<f32>>,  litFound_3 : ptr<function, vec2<f32>>,  agreement_3 : ptr<function, f32>) -> bool
{
    var depth_3 : f32;
    var _S54 : bool = LookupCardWithin_0(world_9, vec3<f32>(0.0f, 0.0f, 0.0f), 0.25f, &((*found_3)), &((*litFound_3)), &((*agreement_3)), &(depth_3));
    return _S54;
}

fn ProbedTrace_0( world_10 : vec3<f32>,  normal_6 : vec3<f32>) -> vec4<f32>
{
    var finest_0 : f32 = Globals_0.uClipmapOrigin_0[i32(0)].w;
    var _S55 : f32 = Globals_0.uClipmapOrigin_0[max(Globals_0.uShadowLevels_0 - i32(1), i32(0))].w;
    var _S56 : vec3<f32> = world_10 + normal_6 * vec3<f32>(finest_0) * vec3<f32>(1.5f);
    var reach_0 : f32 = finest_0 * f32(Globals_0.uShadowResolution_0) * f32((i32(1) << (u32(Globals_0.uShadowLevels_0 - i32(1)))));
    var step_2 : i32 = i32(0);
    var travelled_7 : f32 = 0.0f;
    for(;;)
    {
        var _S57 : bool;
        if(step_2 < i32(64))
        {
            _S57 = travelled_7 < reach_0;
        }
        else
        {
            _S57 = false;
        }
        if(_S57)
        {
        }
        else
        {
            break;
        }
        var known_5 : bool;
        var raw_5 : f32;
        var resolved_8 : f32;
        var distance_2 : f32 = DistanceAt_0(_S56 + Globals_0.uSunDirection_0 * vec3<f32>(travelled_7), &(known_5), &(raw_5), &(resolved_8));
        if(!known_5)
        {
            return vec4<f32>(1.0f, 0.0f, travelled_7 / reach_0, 1.0f);
        }
        var touchedAt_0 : f32;
        if(FieldStepHits_0(distance_2, travelled_7, resolved_8, &(touchedAt_0)))
        {
            return vec4<f32>(0.0f, resolved_8 / _S55, travelled_7 / reach_0, 1.0f);
        }
        var travelled_8 : f32 = travelled_7 + max(distance_2, resolved_8 * 0.25f);
        step_2 = step_2 + i32(1);
        travelled_7 = travelled_8;
    }
    return vec4<f32>(0.5f, 0.0f, clamp(travelled_7 / reach_0, 0.0f, 1.0f), 1.0f);
}

fn Probed_0( world_11 : vec3<f32>,  normal_7 : vec3<f32>,  cardUv_0 : vec2<f32>,  texel_3 : vec2<i32>) -> vec4<f32>
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
        var _S58 : bool = LookupCard_0(world_11, &(found_4), &(litFound_4), &(agreement_4));
        if(!_S58)
        {
            return vec4<f32>(1.0f, 0.0f, 0.0f, 1.0f);
        }
        var albedoWidth_1 : u32;
        var albedoHeight_1 : u32;
        {var dim = textureDimensions((uAlbedo_0));((albedoWidth_1)) = dim.x;((albedoHeight_1)) = dim.y;};
        var apart_0 : vec2<f32> = abs(found_4 - cardUv_0) * vec2<f32>(f32(albedoWidth_1), f32(albedoHeight_1));
        var _S59 : vec4<f32>;
        if((max(apart_0.x, apart_0.y)) < 1.5f)
        {
            _S59 = vec4<f32>(0.0f, 1.0f, 0.0f, 1.0f);
        }
        else
        {
            _S59 = vec4<f32>(1.0f, 1.0f, 0.0f, 1.0f);
        }
        return _S59;
    }
    if((Globals_0.uProbe_0) == i32(4))
    {
        return ProbedTrace_0(world_11, normal_7);
    }
    if((Globals_0.uProbe_0) == i32(2))
    {
        return vec4<f32>(clamp((world_11 - Globals_0.uProbeMin_0) * vec3<f32>(Globals_0.uProbeInvExtent_0), vec3<f32>(0.0f, 0.0f, 0.0f), vec3<f32>(1.0f, 1.0f, 1.0f)), 1.0f);
    }
    if((Globals_0.uProbe_0) == i32(3))
    {
        var _S60 : vec3<f32> = vec3<f32>(0.5f);
        return vec4<f32>(normal_7 * _S60 + _S60, 1.0f);
    }
    var finest_1 : f32 = Globals_0.uClipmapOrigin_0[i32(0)].w;
    var known_6 : bool;
    var raw_6 : f32;
    var resolved_9 : f32;
    var start_0 : f32 = DistanceAt_1(world_11 + normal_7 * vec3<f32>(finest_1) * vec3<f32>(1.5f), &(known_6), &(raw_6), &(resolved_9));
    var _S61 : f32;
    if((Globals_0.uShadowLevels_0) > i32(0))
    {
        _S61 = clamp(Globals_0.uClipmapRange_0.data_0[i32(0)].x / (finest_1 * 4.0f), 0.0f, 1.0f);
    }
    else
    {
        _S61 = 0.0f;
    }
    var _S62 : f32 = raw_6;
    var _S63 : f32;
    if(known_6)
    {
        _S63 = clamp(start_0 / (finest_1 * 4.0f), 0.0f, 1.0f);
    }
    else
    {
        _S63 = 1.0f;
    }
    var _S64 : f32;
    if(known_6)
    {
        _S64 = 1.0f;
    }
    else
    {
        _S64 = 0.5f;
    }
    return vec4<f32>(_S61, _S62, _S63, _S64);
}

fn Visibility_0( world_12 : vec3<f32>,  normal_8 : vec3<f32>,  direction_4 : vec3<f32>) -> f32
{
    var ignored_0 : vec3<f32>;
    return VisibilityAt_0(world_12, normal_8, direction_4, &(ignored_0));
}

fn SunVisibility_0( world_13 : vec3<f32>,  normal_9 : vec3<f32>) -> f32
{
    return Visibility_0(world_13, normal_9, Globals_0.uSunDirection_0);
}

fn SkyRadiance_0( direction_5 : vec3<f32>) -> vec3<f32>
{
    if((Globals_0.uSkyShValid_0) == i32(0))
    {
        return Globals_0.uSkyColour_0 * vec3<f32>((direction_5.z * 0.5f + 0.5f));
    }
    var n_1 : vec4<f32> = vec4<f32>(direction_5, 1.0f);
    var q_0 : vec4<f32> = n_1.xyzz * n_1.yzzx;
    var _S65 : f32 = direction_5.x;
    var _S66 : f32 = direction_5.y;
    return max(vec3<f32>(0.0f, 0.0f, 0.0f), vec3<f32>(dot(Globals_0.uSkySh_0[i32(0)], n_1), dot(Globals_0.uSkySh_0[i32(1)], n_1), dot(Globals_0.uSkySh_0[i32(2)], n_1)) + vec3<f32>(dot(Globals_0.uSkySh_0[i32(3)], q_0), dot(Globals_0.uSkySh_0[i32(4)], q_0), dot(Globals_0.uSkySh_0[i32(5)], q_0)) + Globals_0.uSkySh_0[i32(6)].xyz * vec3<f32>((_S65 * _S65 - _S66 * _S66)));
}

fn SkyLight_0( world_14 : vec3<f32>,  normal_10 : vec3<f32>) -> vec3<f32>
{
    if((Globals_0.uShadowLevels_0) <= i32(0))
    {
        return SkyRadiance_0(normal_10) * vec3<f32>(3.14159274101257324f);
    }
    var aside_0 : vec3<f32>;
    if((abs(normal_10.z)) < 0.89999997615814209f)
    {
        aside_0 = vec3<f32>(0.0f, 0.0f, 1.0f);
    }
    else
    {
        aside_0 = vec3<f32>(1.0f, 0.0f, 0.0f);
    }
    var tangent_1 : vec3<f32> = normalize(cross(normal_10, aside_0));
    var _S67 : vec3<f32> = cross(normal_10, tangent_1);
    const _S68 : vec3<f32> = vec3<f32>(0.0f, 0.0f, 0.0f);
    var ray_0 : i32 = i32(0);
    var total_0 : vec3<f32> = _S68;
    for(;;)
    {
        if(ray_0 < i32(8))
        {
        }
        else
        {
            break;
        }
        var u1_0 : f32 = (f32(ray_0) + 0.5f) / 8.0f;
        var radius_3 : f32 = sqrt(u1_0);
        var angle_0 : f32 = 6.28318548202514648f * RadicalInverse_0(ray_0);
        var direction_6 : vec3<f32> = normalize(tangent_1 * vec3<f32>((radius_3 * cos(angle_0))) + _S67 * vec3<f32>((radius_3 * sin(angle_0))) + normal_10 * vec3<f32>(sqrt(max(0.0f, 1.0f - u1_0))));
        var total_1 : vec3<f32> = total_0 + SkyRadiance_0(direction_6) * vec3<f32>(Visibility_0(world_14, normal_10, direction_6));
        ray_0 = ray_0 + i32(1);
        total_0 = total_1;
    }
    return total_0 * vec3<f32>(0.39269909262657166f);
}

fn SegmentVisibility_0( world_15 : vec3<f32>,  normal_11 : vec3<f32>,  target_0 : vec3<f32>,  stopShort_0 : f32) -> f32
{
    if((Globals_0.uShadowLevels_0) <= i32(0))
    {
        return 1.0f;
    }
    var finest_2 : f32 = Globals_0.uClipmapOrigin_0[i32(0)].w;
    var at_0 : vec3<f32> = world_15 + normal_11 * vec3<f32>(finest_2) * vec3<f32>(1.5f);
    var toTarget_0 : vec3<f32> = target_0 - at_0;
    var distance_3 : f32 = length(toTarget_0);
    var limit_0 : f32 = distance_3 - max(stopShort_0, finest_2);
    if(limit_0 <= 0.0f)
    {
        return 1.0f;
    }
    var _S69 : vec3<f32> = toTarget_0 / vec3<f32>(distance_3);
    var step_3 : i32 = i32(0);
    var travelled_9 : f32 = 0.0f;
    for(;;)
    {
        var _S70 : bool;
        if(step_3 < i32(48))
        {
            _S70 = travelled_9 < limit_0;
        }
        else
        {
            _S70 = false;
        }
        if(_S70)
        {
        }
        else
        {
            break;
        }
        var known_7 : bool;
        var raw_7 : f32;
        var resolved_10 : f32;
        var field_3 : f32 = DistanceAt_1(at_0 + _S69 * vec3<f32>(travelled_9), &(known_7), &(raw_7), &(resolved_10));
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
fn computeMain(@builtin(workgroup_id) groupId_0 : vec3<u32>, @builtin(local_invocation_id) localId_0 : vec3<u32>)
{
    var slot_1 : i32 = i32(groupId_0.x) + Globals_0.uTileOffset_0;
    var tile_0 : i32;
    if((Globals_0.uUseActiveList_0) != i32(0))
    {
        if(slot_1 >= (Globals_0.uActiveCount_0))
        {
            return;
        }
        tile_0 = uActiveTiles_0[slot_1];
    }
    else
    {
        tile_0 = slot_1;
    }
    if(tile_0 >= (Globals_0.uTileCount_0))
    {
        return;
    }
    var entry_0 : vec4<i32> = uTiles_0[tile_0];
    var within_0 : vec2<i32> = entry_0.yz * vec2<i32>(i32(8)) + vec2<i32>(localId_0.xy);
    var at_1 : i32 = entry_0.x * i32(10);
    var _S71 : i32 = at_1 + i32(5);
    if((uPages_0[_S71].w) > 0.5f)
    {
        return;
    }
    if((Globals_0.uActiveRadius_0) > 0.0f)
    {
        var reach_1 : vec4<f32> = uPages_0[at_1 + i32(9)];
        var toCentre_1 : vec3<f32> = reach_1.xyz - Globals_0.uActiveCentre_0;
        var limit_1 : f32 = Globals_0.uActiveRadius_0 + reach_1.w;
        if((dot(toCentre_1, toCentre_1)) > (limit_1 * limit_1))
        {
            return;
        }
    }
    var lightPage_0 : vec4<f32> = uPages_0[at_1];
    var cardPage_2 : vec4<f32> = uPages_0[at_1 + i32(1)];
    var _S72 : f32 = lightPage_0.z;
    var _S73 : i32 = i32(_S72);
    var _S74 : bool;
    if((within_0.x) >= _S73)
    {
        _S74 = true;
    }
    else
    {
        _S74 = (within_0.y) >= i32(lightPage_0.w);
    }
    if(_S74)
    {
        return;
    }
    var centreAndHalfWidth_1 : vec4<f32> = uPages_0[at_1 + i32(2)];
    var rightAndHalfHeight_1 : vec4<f32> = uPages_0[at_1 + i32(3)];
    var upAndDepth_1 : vec4<f32> = uPages_0[at_1 + i32(4)];
    var direction_7 : vec4<f32> = uPages_0[_S71];
    var row0_1 : vec4<f32> = uPages_0[at_1 + i32(6)];
    var row1_1 : vec4<f32> = uPages_0[at_1 + i32(7)];
    var row2_1 : vec4<f32> = uPages_0[at_1 + i32(8)];
    var _S75 : f32 = lightPage_0.w;
    var uv_1 : vec2<f32> = (vec2<f32>(within_0) + vec2<f32>(0.5f)) / vec2<f32>(_S72, _S75);
    var albedoWidth_2 : u32;
    var albedoHeight_2 : u32;
    {var dim = textureDimensions((uAlbedo_0));((albedoWidth_2)) = dim.x;((albedoHeight_2)) = dim.y;};
    var cardUv_1 : vec2<f32> = (cardPage_2.xy + uv_1 * cardPage_2.zw) / vec2<f32>(f32(albedoWidth_2), f32(albedoHeight_2));
    var albedo_1 : vec4<f32> = (textureSampleLevel((uAlbedo_0), (uAlbedoSampler_0), (cardUv_1), (0.0f)));
    var pageAt_0 : vec2<i32> = vec2<i32>(i32(lightPage_0.x), i32(lightPage_0.y));
    if((albedo_1.w) < 0.5f)
    {
        textureStore((uDirect_0), (vec2<u32>(pageAt_0 + within_0)), (vec4<f32>(0.0f, 0.0f, 0.0f, 0.0f)));
        return;
    }
    var normal_12 : vec3<f32> = normalize((textureSampleLevel((uNormal_0), (uNormalSampler_0), (cardUv_1), (0.0f))).xyz * vec3<f32>(2.0f) - vec3<f32>(1.0f));
    var local_2 : vec3<f32> = centreAndHalfWidth_1.xyz + rightAndHalfHeight_1.xyz * vec3<f32>((uv_1.x * 2.0f - 1.0f)) * vec3<f32>(centreAndHalfWidth_1.w) + upAndDepth_1.xyz * vec3<f32>((uv_1.y * 2.0f - 1.0f)) * vec3<f32>(rightAndHalfHeight_1.w) - direction_7.xyz * vec3<f32>(((textureSampleLevel((uDepth_0), (uDepthSampler_0), (cardUv_1), (0.0f))).x * upAndDepth_1.w));
    var _S76 : vec3<f32> = vec3<f32>(row0_1.x, row1_1.x, row2_1.x);
    var _S77 : vec3<f32> = vec3<f32>(row0_1.y, row1_1.y, row2_1.y);
    var _S78 : vec3<f32> = vec3<f32>(row0_1.z, row1_1.z, row2_1.z);
    var world_16 : vec3<f32> = vec3<f32>(dot(_S76, local_2), dot(_S77, local_2), dot(_S78, local_2)) + vec3<f32>(row0_1.w, row1_1.w, row2_1.w);
    var worldNormal_0 : vec3<f32> = normalize(vec3<f32>(dot(_S76, normal_12), dot(_S77, normal_12), dot(_S78, normal_12)));
    var texelAt_0 : vec2<i32> = pageAt_0 + within_0;
    var traced_0 : f32;
    if((Globals_0.uMode_0) == i32(1))
    {
        if((((((texelAt_0.x) | ((texelAt_0.y)))) & (i32(3)))) != i32(0))
        {
            textureStore((uDirect_0), (vec2<u32>(texelAt_0)), (vec4<f32>(0.0f, 0.0f, 0.0f, 0.0f)));
            return;
        }
        var probeCoord_5 : vec2<i32> = (texelAt_0 >> (vec2<u32>(u32(2))));
        uProbePos_0[probeCoord_5.y * Globals_0.uProbeWidth_0 + probeCoord_5.x] = vec4<f32>(world_16, 1.0f);
        var _S79 : vec2<f32> = ProbeCenter_0(probeCoord_5, Globals_0.uTemporalIndex_0);
        var gathered_0 : vec3<f32> = Gather_0(world_16, worldNormal_0, _S79);
        var _S80 : vec3<i32> = vec3<i32>(texelAt_0, i32(0));
        var previous_0 : vec4<f32> = (textureLoad((uDirectHistory_0), ((_S80)).xy, ((_S80)).z));
        if((Globals_0.uTemporalIndex_0) > i32(0))
        {
            traced_0 = previous_0.w;
        }
        else
        {
            traced_0 = 0.0f;
        }
        var limit_2 : f32 = f32(Globals_0.uMaxFrames_0);
        textureStore((uDirect_0), (vec2<u32>(texelAt_0)), (vec4<f32>(mix(previous_0.xyz, gathered_0, vec3<f32>((1.0f / (1.0f + min(traced_0, limit_2))))), min(traced_0 + 1.0f, limit_2 + 1.0f))));
        return;
    }
    var corner_0 : i32;
    var indirectSum_0 : vec3<f32>;
    if((Globals_0.uMode_0) == i32(2))
    {
        var _S81 : vec3<i32> = vec3<i32>(texelAt_0, i32(0));
        var direct_0 : vec3<f32> = (textureLoad((uPrevious_0), ((_S81)).xy, ((_S81)).z)).xyz;
        var _S82 : vec2<i32> = pageAt_0 + vec2<i32>(_S73, i32(_S75));
        var probe00_0 : vec2<i32> = (texelAt_0 >> (vec2<u32>(u32(2))));
        var _S83 : vec2<f32> = vec2<f32>(1.0f);
        var _S84 : vec2<f32> = (vec2<f32>(texelAt_0 - probe00_0 * vec2<i32>(i32(4))) + _S83) / vec2<f32>(6.0f);
        const _S85 : vec3<f32> = vec3<f32>(0.0f, 0.0f, 0.0f);
        corner_0 = i32(0);
        indirectSum_0 = _S85;
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
            var _S86 : i32 = corner_0;
            var probeCoord_6 : vec2<i32> = probe00_0 + kProbeCorner_0[corner_0];
            var position_3 : vec3<f32>;
            var _S87 : bool = ProbeUsable_1(probeCoord_6, pageAt_0, _S82, &(position_3));
            if(!_S87)
            {
                corner_0 = corner_0 + i32(1);
                continue;
            }
            if((ProbePlaneAccept_0(world_16, worldNormal_0, position_3)) <= 0.0f)
            {
                corner_0 = corner_0 + i32(1);
                continue;
            }
            var share_0 : vec2<f32> = mix(_S83 - _S84, _S84, vec2<f32>(kProbeCorner_0[_S86]));
            var w_0 : f32 = share_0.x * share_0.y;
            var indirectWeight_0 : f32 = traced_0 + w_0;
            indirectSum_0 = indirectSum_0 + FilteredProbe_0(probeCoord_6, pageAt_0, _S82, world_16, worldNormal_0) * vec3<f32>(w_0);
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
        textureStore((uDirect_0), (vec2<u32>(texelAt_0)), (vec4<f32>((direct_0 + indirect_0) * (BoostAlbedo_0(albedo_1.xyz, Globals_0.uBoost_0) / vec3<f32>(3.14159274101257324f)), 1.0f)));
        return;
    }
    if((Globals_0.uMode_0) == i32(3))
    {
        var _S88 : vec3<i32> = vec3<i32>(texelAt_0, i32(0));
        textureStore((uDirect_0), (vec2<u32>(texelAt_0)), (vec4<f32>((textureLoad((uPrevious_0), ((_S88)).xy, ((_S88)).z)).xyz * (BoostAlbedo_0(albedo_1.xyz, Globals_0.uBoost_0) / vec3<f32>(3.14159274101257324f)), 1.0f)));
        return;
    }
    if((Globals_0.uProbe_0) != i32(0))
    {
        textureStore((uDirect_0), (vec2<u32>(texelAt_0)), (Probed_0(world_16, worldNormal_0, cardUv_1, texelAt_0)));
        return;
    }
    var _S89 : f32 = max(dot(worldNormal_0, Globals_0.uSunDirection_0), 0.0f);
    if(_S89 > 0.0f)
    {
        traced_0 = SunVisibility_0(world_16, worldNormal_0);
    }
    else
    {
        traced_0 = 1.0f;
    }
    var _S90 : vec3<f32> = Globals_0.uSunColour_0 * vec3<f32>(_S89) * vec3<f32>(traced_0) + SkyLight_0(world_16, worldNormal_0);
    corner_0 = i32(0);
    indirectSum_0 = _S90;
    for(;;)
    {
        if(corner_0 < (Globals_0.uLightCount_0))
        {
        }
        else
        {
            break;
        }
        var _S91 : i32 = corner_0 * i32(3);
        var place_0 : vec4<f32> = uLights_0[_S91];
        var emission_0 : vec4<f32> = uLights_0[_S91 + i32(1)];
        var facing_2 : vec4<f32> = uLights_0[_S91 + i32(2)];
        var _S92 : vec3<f32> = place_0.xyz;
        var delta_1 : vec3<f32> = _S92 - world_16;
        var squared_0 : f32 = dot(delta_1, delta_1);
        var _S93 : f32 = max(place_0.w * 3.0f, 2.0f);
        var reachSquared_0 : f32 = _S93 * _S93;
        if(squared_0 >= reachSquared_0)
        {
            corner_0 = corner_0 + i32(1);
            continue;
        }
        var toLight_0 : vec3<f32> = delta_1 * vec3<f32>(rsqrt_0(max(squared_0, 9.99999997475242708e-07f)));
        var _S94 : f32 = max(dot(worldNormal_0, toLight_0), 0.0f);
        if(_S94 <= 0.0f)
        {
            corner_0 = corner_0 + i32(1);
            continue;
        }
        var falloff_0 : f32 = 1.0f / (squared_0 + 1.0f);
        var window_0 : f32 = clamp(1.0f - squared_0 * squared_0 / (reachSquared_0 * reachSquared_0), 0.0f, 1.0f);
        var _S95 : vec3<f32> = facing_2.xyz;
        var aim_0 : f32;
        if((dot(_S95, _S95)) > 0.25f)
        {
            aim_0 = max(dot(normalize(_S95), (vec3<f32>(0) - toLight_0)), 0.0f);
        }
        else
        {
            aim_0 = 1.0f;
        }
        var contribution_0 : f32 = emission_0.w * Globals_0.uLocalScale_0 * _S94 * falloff_0 * window_0 * window_0 * aim_0;
        var _S96 : vec3<f32> = emission_0.xyz;
        if((contribution_0 * dot(_S96, vec3<f32>(0.2125999927520752f, 0.71520000696182251f, 0.07220000028610229f))) < 0.00000999999974738f)
        {
            corner_0 = corner_0 + i32(1);
            continue;
        }
        if((SegmentVisibility_0(world_16, worldNormal_0, _S92, 0.11999999731779099f)) <= 0.0f)
        {
            corner_0 = corner_0 + i32(1);
            continue;
        }
        indirectSum_0 = indirectSum_0 + _S96 * vec3<f32>(contribution_0);
        corner_0 = corner_0 + i32(1);
    }
    textureStore((uDirect_0), (vec2<u32>(texelAt_0)), (vec4<f32>(indirectSum_0, 1.0f)));
    return;
}

