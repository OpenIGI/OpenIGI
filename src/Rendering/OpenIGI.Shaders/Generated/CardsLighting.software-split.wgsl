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

@binding(37) @group(0) var uClipmaps_0 : texture_3d<f32>;

@binding(69) @group(0) var uClipmapsSampler_0 : sampler;

@binding(19) @group(0) var<storage, read_write> uCells_0 : array<vec2<i32>>;

@binding(20) @group(0) var<storage, read_write> uCellPages_0 : array<i32>;

@binding(38) @group(0) var uPrevious_0 : texture_2d<f32>;

@binding(70) @group(0) var uPreviousSampler_0 : sampler;

@binding(21) @group(0) var<storage, read_write> uProbePos_0 : array<vec4<f32>>;

@binding(44) @group(0) var uDirectHistory_0 : texture_2d<f32>;

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

fn FieldStepHits_0( field_0 : f32,  travelled_0 : f32,  resolved_1 : f32,  hitDistance_0 : ptr<function, f32>) -> bool
{
    var radius_0 : f32 = 0.5f * resolved_1 * saturate(travelled_0 / max(2.0f * resolved_1, 9.99999997475242708e-07f));
    (*hitDistance_0) = max(travelled_0 + field_0 - radius_0, 0.0f);
    return field_0 < radius_0;
}

fn VisibilityAt_0( world_2 : vec3<f32>,  normal_3 : vec3<f32>,  direction_0 : vec3<f32>,  hit_0 : ptr<function, vec3<f32>>) -> f32
{
    (*hit_0) = world_2;
    if((Globals_0.uShadowLevels_0) <= i32(0))
    {
        return 1.0f;
    }
    var _S17 : vec3<f32> = world_2 + normal_3 * vec3<f32>(Globals_0.uClipmapOrigin_0[i32(0)].w) * vec3<f32>(1.5f);
    var _S18 : f32 = Globals_0.uClipmapOrigin_0[i32(0)].w * f32(Globals_0.uShadowResolution_0) * f32((i32(1) << (u32(Globals_0.uShadowLevels_0 - i32(1)))));
    var step_0 : i32 = i32(0);
    var travelled_1 : f32 = 0.0f;
    for(;;)
    {
        var _S19 : bool;
        if(step_0 < i32(64))
        {
            _S19 = travelled_1 < _S18;
        }
        else
        {
            _S19 = false;
        }
        if(_S19)
        {
        }
        else
        {
            break;
        }
        var known_1 : bool;
        var raw_1 : f32;
        var resolved_2 : f32;
        var distance_0 : f32 = DistanceAt_0(_S17 + direction_0 * vec3<f32>(travelled_1), &(known_1), &(raw_1), &(resolved_2));
        if(!known_1)
        {
            return 1.0f;
        }
        var hitDistance_1 : f32;
        var _S20 : bool = FieldStepHits_0(distance_0, travelled_1, resolved_2, &(hitDistance_1));
        if(_S20)
        {
            (*hit_0) = _S17 + direction_0 * vec3<f32>(hitDistance_1);
            return 0.0f;
        }
        var travelled_2 : f32 = travelled_1 + max(distance_0, resolved_2 * 0.25f);
        step_0 = step_0 + i32(1);
        travelled_1 = travelled_2;
    }
    return 1.0f;
}

fn LookupBucket_0( cell_0 : vec3<i32>) -> i32
{
    return i32(((((((u32(cell_0.x) * u32(2376512323)) ^ ((u32(cell_0.y) * u32(3625334849))))) ^ ((u32(cell_0.z) * u32(3407524639))))) & (u32(Globals_0.uLookupBuckets_0 - i32(1)))));
}

fn MoverPages_0( headers_0 : i32,  index_1 : i32,  world_3 : vec3<f32>,  bias_0 : f32) -> vec2<i32>
{
    var at_2 : i32 = headers_0 + index_1 * i32(6);
    var reach_1 : f32 = (bitcast<f32>((uCellPages_0[at_2 + i32(3)]))) + bias_0;
    var toCentre_1 : vec3<f32> = world_3 - vec3<f32>((bitcast<f32>((uCellPages_0[at_2]))), (bitcast<f32>((uCellPages_0[at_2 + i32(1)]))), (bitcast<f32>((uCellPages_0[at_2 + i32(2)]))));
    var _S21 : vec2<i32>;
    if((dot(toCentre_1, toCentre_1)) > (reach_1 * reach_1))
    {
        _S21 = vec2<i32>(i32(0), i32(0));
    }
    else
    {
        _S21 = vec2<i32>(uCellPages_0[at_2 + i32(4)], uCellPages_0[at_2 + i32(5)]);
    }
    return _S21;
}

fn ProjectOntoCard_0( page_0 : i32,  world_4 : vec3<f32>,  directionW_0 : vec3<f32>,  across_0 : ptr<function, vec2<f32>>,  halfSize_0 : ptr<function, vec2<f32>>,  behind_0 : ptr<function, f32>,  depth_0 : ptr<function, f32>,  facing_0 : ptr<function, vec3<f32>>,  directionLocal_0 : ptr<function, vec3<f32>>,  litRect_0 : ptr<function, vec4<f32>>,  cardPage_2 : ptr<function, vec4<f32>>)
{
    var _S22 : i32 = page_0 * i32(10);
    (*litRect_0) = uPages_0[_S22];
    (*cardPage_2) = uPages_0[_S22 + i32(1)];
    var centreAndHalfWidth_1 : vec4<f32> = uPages_0[_S22 + i32(2)];
    var rightAndHalfHeight_1 : vec4<f32> = uPages_0[_S22 + i32(3)];
    var upAndDepth_1 : vec4<f32> = uPages_0[_S22 + i32(4)];
    var direction_1 : vec4<f32> = uPages_0[_S22 + i32(5)];
    var row0_1 : vec4<f32> = uPages_0[_S22 + i32(6)];
    var row1_1 : vec4<f32> = uPages_0[_S22 + i32(7)];
    var row2_1 : vec4<f32> = uPages_0[_S22 + i32(8)];
    var delta_0 : vec3<f32> = world_4 - vec3<f32>(row0_1.w, row1_1.w, row2_1.w);
    var _S23 : vec3<f32> = row0_1.xyz;
    var _S24 : vec3<f32> = row1_1.xyz;
    var _S25 : vec3<f32> = row2_1.xyz;
    var local_2 : vec3<f32> = vec3<f32>(dot(_S23, delta_0), dot(_S24, delta_0), dot(_S25, delta_0));
    (*directionLocal_0) = vec3<f32>(dot(_S23, directionW_0), dot(_S24, directionW_0), dot(_S25, directionW_0));
    var relative_0 : vec3<f32> = local_2 - centreAndHalfWidth_1.xyz;
    var _S26 : vec2<f32> = max(vec2<f32>(centreAndHalfWidth_1.w, rightAndHalfHeight_1.w), vec2<f32>(9.99999997475242708e-07f, 9.99999997475242708e-07f));
    (*halfSize_0) = _S26;
    (*across_0) = vec2<f32>(dot(relative_0, rightAndHalfHeight_1.xyz), dot(relative_0, upAndDepth_1.xyz)) / _S26;
    var _S27 : vec3<f32> = direction_1.xyz;
    (*behind_0) = - dot(relative_0, _S27);
    (*depth_0) = upAndDepth_1.w;
    (*facing_0) = _S27;
    return;
}

fn LookupCardWithin_0( world_5 : vec3<f32>,  arrivingW_0 : vec3<f32>,  skin_0 : f32,  found_0 : ptr<function, vec2<f32>>,  litFound_0 : ptr<function, vec2<f32>>,  agreement_0 : ptr<function, f32>,  cardDepth_0 : ptr<function, f32>) -> bool
{
    (*cardDepth_0) = 0.0f;
    const _S28 : vec2<f32> = vec2<f32>(0.0f, 0.0f);
    (*found_0) = _S28;
    (*litFound_0) = _S28;
    (*agreement_0) = 1.0e+09f;
    if((Globals_0.uLookupBuckets_0) <= i32(0))
    {
        return false;
    }
    var _S29 : vec2<i32> = uCells_0[LookupBucket_0(vec3<i32>(floor((world_5 - Globals_0.uLookupMin_0) / vec3<f32>(Globals_0.uLookupCell_0))))];
    var albedoWidth_1 : u32;
    var albedoHeight_1 : u32;
    {var dim = textureDimensions((uAlbedo_0));((albedoWidth_1)) = dim.x;((albedoHeight_1)) = dim.y;};
    var _S30 : vec2<f32> = vec2<f32>(f32(albedoWidth_1), f32(albedoHeight_1));
    var _S31 : vec2<i32> = uCells_0[Globals_0.uLookupBuckets_0];
    var settled_0 : bool = false;
    var anyFound_0 : bool = false;
    var walk_0 : i32 = i32(0);
    for(;;)
    {
        var _S32 : bool;
        if(walk_0 <= (_S31.y))
        {
            _S32 = !settled_0;
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
        var _S33 : bool = walk_0 == i32(0);
        var _S34 : vec2<i32>;
        if(_S33)
        {
            _S34 = _S29;
        }
        else
        {
            _S34 = MoverPages_0(_S31.x, walk_0 - i32(1), world_5, 0.0f);
        }
        var settled_1 : bool = settled_0;
        var anyFound_1 : bool = anyFound_0;
        var slot_1 : i32 = i32(0);
        for(;;)
        {
            var _S35 : bool;
            if(slot_1 < (_S34.y))
            {
                _S35 = !settled_1;
            }
            else
            {
                _S35 = false;
            }
            if(_S35)
            {
            }
            else
            {
                break;
            }
            var page_1 : i32 = uCellPages_0[_S34.x + slot_1];
            var _S36 : i32 = uCellPages_0[_S34.x + slot_1] * i32(10);
            if(((uPages_0[_S36 + i32(5)].w) > 0.5f) == _S33)
            {
                slot_1 = slot_1 + i32(1);
                continue;
            }
            var bound_0 : vec4<f32> = uPages_0[_S36 + i32(9)];
            var toCentre_2 : vec3<f32> = world_5 - bound_0.xyz;
            var _S37 : f32 = bound_0.w;
            if((dot(toCentre_2, toCentre_2)) > (_S37 * _S37))
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
            ProjectOntoCard_0(page_1, world_5, (vec3<f32>(0) - arrivingW_0), &(across_1), &(halfSize_1), &(behind_1), &(depth_1), &(facing_1), &(arrivingLocal_0), &(litRect_1), &(cardPage_3));
            var _S38 : bool;
            if((abs(across_1.x)) > 1.0f)
            {
                _S38 = true;
            }
            else
            {
                _S38 = (abs(across_1.y)) > 1.0f;
            }
            if(_S38)
            {
                slot_1 = slot_1 + i32(1);
                continue;
            }
            var _S39 : bool;
            if((dot(arrivingW_0, arrivingW_0)) > 0.5f)
            {
                _S39 = (dot(facing_1, arrivingLocal_0)) < -0.05000000074505806f;
            }
            else
            {
                _S39 = false;
            }
            if(_S39)
            {
                slot_1 = slot_1 + i32(1);
                continue;
            }
            var _S40 : bool;
            if(behind_1 < (- skin_0))
            {
                _S40 = true;
            }
            else
            {
                _S40 = behind_1 > (depth_1 + skin_0);
            }
            if(_S40)
            {
                slot_1 = slot_1 + i32(1);
                continue;
            }
            var _S41 : vec2<f32> = vec2<f32>(0.5f);
            var uv_2 : vec2<f32> = across_1 * _S41 + _S41;
            var texel_0 : vec2<f32> = (cardPage_3.xy + uv_2 * cardPage_3.zw) / _S30;
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
                var _S42 : bool = error_0 < 0.01999999955296516f;
                (*litFound_0) = (litRect_1.xy + uv_2 * litRect_1.zw) / Globals_0.uAtlasSize_0.xy;
                settled_2 = _S42;
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
        var _S43 : i32 = walk_0 + i32(1);
        settled_0 = settled_1;
        anyFound_0 = anyFound_1;
        walk_0 = _S43;
    }
    return anyFound_0;
}

fn LookupCardHit_0( world_6 : vec3<f32>,  rayDirection_0 : vec3<f32>,  found_1 : ptr<function, vec2<f32>>,  litFound_1 : ptr<function, vec2<f32>>,  agreement_1 : ptr<function, f32>) -> bool
{
    var depth_2 : f32;
    var _S44 : bool = LookupCardWithin_0(world_6, rayDirection_0, 0.25f, &((*found_1)), &((*litFound_1)), &((*agreement_1)), &(depth_2));
    return _S44;
}

fn ShBasis_0( d_0 : vec3<f32>) -> vec4<f32>
{
    return vec4<f32>(0.28209498524665833f, -0.48860299587249756f * d_0.y, 0.48860299587249756f * d_0.z, -0.48860299587249756f * d_0.x);
}

fn GatherProbeRay_0( world_7 : vec3<f32>,  normal_4 : vec3<f32>,  center_0 : vec2<f32>,  basis_2 : HemisphereBasis_0,  finest_0 : f32,  cell_1 : i32,  radiance_0 : ptr<function, vec3<f32>>,  basisHere_0 : ptr<function, vec4<f32>>)
{
    var square_0 : vec2<f32> = (vec2<f32>(f32(cell_1 % i32(4)), f32(cell_1 / i32(4))) + center_0) / vec2<f32>(4.0f);
    var phi_0 : f32 = 6.28318548202514648f * square_0.x;
    var cosTheta_0 : f32 = square_0.y;
    var sinTheta_0 : f32 = sqrt(max(0.0f, 1.0f - cosTheta_0 * cosTheta_0));
    var direction_2 : vec3<f32> = Orient_0(basis_2, vec3<f32>(sinTheta_0 * cos(phi_0), sinTheta_0 * sin(phi_0), cosTheta_0));
    (*radiance_0) = vec3<f32>(0.0f, 0.0f, 0.0f);
    var hit_1 : vec3<f32>;
    var _S45 : f32 = VisibilityAt_0(world_7, normal_4, direction_2, &(hit_1));
    if(_S45 <= 0.5f)
    {
        var found_2 : vec2<f32>;
        var litFound_2 : vec2<f32>;
        var agreement_2 : f32;
        var _S46 : bool = LookupCardHit_0(hit_1, direction_2, &(found_2), &(litFound_2), &(agreement_2));
        if(_S46)
        {
            (*radiance_0) = (textureSampleLevel((uPrevious_0), (uPreviousSampler_0), (litFound_2), (0.0f))).xyz * vec3<f32>(smoothstep(1.5f * finest_0, 2.0f * finest_0, length(hit_1 - world_7)));
        }
    }
    (*basisHere_0) = ShBasis_0(direction_2);
    return;
}

var<workgroup> sBounceBasis_0 : array<vec4<f32>, i32(64)>;

var<workgroup> sBounceRadiance_0 : array<vec3<f32>, i32(64)>;

fn ShIrradianceTransfer_0( normal_5 : vec3<f32>) -> vec4<f32>
{
    var basis_3 : vec4<f32> = ShBasis_0(normal_5);
    return vec4<f32>(basis_3.x * 3.14159274101257324f, basis_3.yzw * vec3<f32>(2.09439516067504883f));
}

fn FinishProbe_0( red_0 : vec4<f32>,  green_0 : vec4<f32>,  blue_0 : vec4<f32>,  normal_6 : vec3<f32>) -> vec3<f32>
{
    var transfer_0 : vec4<f32> = ShIrradianceTransfer_0(normal_6);
    return max(vec3<f32>(dot(red_0, transfer_0), dot(green_0, transfer_0), dot(blue_0, transfer_0)) * vec3<f32>(0.39269909262657166f), vec3<f32>(0.0f, 0.0f, 0.0f));
}

@compute
@workgroup_size(8, 8, 1)
fn computeMain(@builtin(workgroup_id) groupId_1 : vec3<u32>, @builtin(local_invocation_id) localId_0 : vec3<u32>)
{
    var entry_1 : vec4<i32>;
    var at_3 : i32;
    var lightPage_2 : vec4<f32>;
    var cardPage_4 : vec4<f32>;
    var _S47 : bool = GroupTile_0(groupId_1, &(entry_1), &(at_3), &(lightPage_2), &(cardPage_4));
    if(!_S47)
    {
        return;
    }
    var pageAt_0 : vec2<i32> = vec2<i32>(i32(lightPage_2.x), i32(lightPage_2.y));
    var _S48 : i32 = i32(lightPage_2.z);
    var _S49 : i32 = i32(lightPage_2.w);
    var _S50 : vec2<i32> = vec2<i32>(i32(8));
    var within_1 : vec2<i32> = entry_1.yz * _S50 + vec2<i32>(localId_0.xy);
    var probeLive_0 : bool;
    if((within_1.x) < _S48)
    {
        probeLive_0 = (within_1.y) < _S49;
    }
    else
    {
        probeLive_0 = false;
    }
    if(probeLive_0)
    {
        var texelAt_0 : vec2<i32> = pageAt_0 + within_1;
        var uv_3 : vec2<f32>;
        var cardUv_2 : vec2<f32>;
        if((TexelAlbedo_0(lightPage_2, cardPage_4, within_1, &(uv_3), &(cardUv_2)).w) < 0.5f)
        {
            probeLive_0 = true;
        }
        else
        {
            probeLive_0 = (((((texelAt_0.x) | ((texelAt_0.y)))) & (i32(3)))) != i32(0);
        }
        if(probeLive_0)
        {
            textureStore((uDirect_0), (vec2<u32>(texelAt_0)), (vec4<f32>(0.0f, 0.0f, 0.0f, 0.0f)));
        }
    }
    var lane_0 : i32 = i32(localId_0.y) * i32(8) + i32(localId_0.x);
    var probeIndex_0 : i32 = lane_0 / i32(16);
    var ray_0 : i32 = lane_0 % i32(16);
    var _S51 : vec2<i32> = vec2<i32>(i32(3));
    var probeWithin_0 : vec2<i32> = entry_1.yz * _S50 + (((vec2<i32>(i32(4), i32(4)) - (((pageAt_0 + entry_1.yz * _S50) & (_S51)))) & (_S51))) + vec2<i32>(probeIndex_0 % i32(2), probeIndex_0 / i32(2)) * vec2<i32>(i32(4));
    var probeTexel_0 : vec2<i32> = pageAt_0 + probeWithin_0;
    if((probeWithin_0.x) < _S48)
    {
        probeLive_0 = (probeWithin_0.y) < _S49;
    }
    else
    {
        probeLive_0 = false;
    }
    const _S52 : vec3<f32> = vec3<f32>(0.0f, 0.0f, 0.0f);
    var world_8 : vec3<f32> = _S52;
    var worldNormal_1 : vec3<f32> = vec3<f32>(0.0f, 0.0f, 1.0f);
    if(probeLive_0)
    {
        var uv_4 : vec2<f32>;
        var cardUv_3 : vec2<f32>;
        var albedo_0 : vec4<f32> = TexelAlbedo_0(lightPage_2, cardPage_4, probeWithin_0, &(uv_4), &(cardUv_3));
        var probeLive_1 : bool = (albedo_0.w) >= 0.5f;
        if(probeLive_1)
        {
            TexelSurface_0(at_3, uv_4, cardUv_3, &(world_8), &(worldNormal_1));
        }
        probeLive_0 = probeLive_1;
    }
    var probeCoord_1 : vec2<i32> = (probeTexel_0 >> (vec2<u32>(u32(2))));
    var center_1 : vec2<f32> = ProbeCenter_0(probeCoord_1, Globals_0.uTemporalIndex_0);
    var gathers_0 : bool;
    if(probeLive_0)
    {
        gathers_0 = ProbeGathers_0();
    }
    else
    {
        gathers_0 = false;
    }
    if(gathers_0)
    {
        var radiance_1 : vec3<f32>;
        var basisHere_1 : vec4<f32>;
        GatherProbeRay_0(world_8, worldNormal_1, center_1, BasisAround_0(worldNormal_1), Globals_0.uClipmapOrigin_0[i32(0)].w, ray_0, &(radiance_1), &(basisHere_1));
        sBounceBasis_0[lane_0] = basisHere_1;
        sBounceRadiance_0[lane_0] = radiance_1;
    }
    workgroupBarrier();
    if(ray_0 != i32(0))
    {
        probeLive_0 = true;
    }
    else
    {
        probeLive_0 = !probeLive_0;
    }
    if(probeLive_0)
    {
        return;
    }
    uProbePos_0[probeCoord_1.y * Globals_0.uProbeWidth_0 + probeCoord_1.x] = vec4<f32>(world_8, 1.0f);
    var gathered_0 : vec3<f32>;
    if(gathers_0)
    {
        const _S53 : vec4<f32> = vec4<f32>(0.0f, 0.0f, 0.0f, 0.0f);
        var cell_2 : i32 = i32(0);
        var red_1 : vec4<f32> = _S53;
        var green_1 : vec4<f32> = _S53;
        var blue_1 : vec4<f32> = _S53;
        for(;;)
        {
            if(cell_2 < i32(16))
            {
            }
            else
            {
                break;
            }
            var _S54 : i32 = lane_0 + cell_2;
            var red_2 : vec4<f32> = red_1 + sBounceBasis_0[_S54] * vec4<f32>(sBounceRadiance_0[_S54].x);
            var green_2 : vec4<f32> = green_1 + sBounceBasis_0[_S54] * vec4<f32>(sBounceRadiance_0[_S54].y);
            var blue_2 : vec4<f32> = blue_1 + sBounceBasis_0[_S54] * vec4<f32>(sBounceRadiance_0[_S54].z);
            cell_2 = cell_2 + i32(1);
            red_1 = red_2;
            green_1 = green_2;
            blue_1 = blue_2;
        }
        gathered_0 = FinishProbe_0(red_1, green_1, blue_1, worldNormal_1);
    }
    else
    {
        gathered_0 = _S52;
    }
    var _S55 : vec3<i32> = vec3<i32>(probeTexel_0, i32(0));
    var previous_0 : vec4<f32> = (textureLoad((uDirectHistory_0), ((_S55)).xy, ((_S55)).z));
    var traced_0 : f32;
    if((Globals_0.uTemporalIndex_0) > i32(0))
    {
        traced_0 = previous_0.w;
    }
    else
    {
        traced_0 = 0.0f;
    }
    var limit_1 : f32 = f32(Globals_0.uMaxFrames_0);
    textureStore((uDirect_0), (vec2<u32>(probeTexel_0)), (vec4<f32>(mix(previous_0.xyz, gathered_0, vec3<f32>((1.0f / (1.0f + min(traced_0, limit_1))))), min(traced_0 + 1.0f, limit_1 + 1.0f))));
    return;
}

