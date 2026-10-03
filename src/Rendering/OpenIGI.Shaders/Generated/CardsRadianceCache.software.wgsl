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
    @align(16) uSkyColour_0 : vec3<f32>,
    @align(16) uCacheOrigin_0 : vec3<f32>,
    @align(16) uCacheCount_0 : vec3<f32>,
    @align(16) uTraceOrigin_0 : vec3<f32>,
    @align(16) uClipmapRange_0 : _Array_std140_float4_0,
    @align(16) uShadowLevels_0 : i32,
    @align(4) uShadowResolution_0 : i32,
    @align(8) uLookupBuckets_0 : i32,
    @align(4) uLookupCell_0 : f32,
    @align(16) uSkyShValid_0 : i32,
    @align(4) uBounceScale_0 : f32,
    @align(8) uCacheSpacing_0 : f32,
    @align(4) uSlabBegin_0 : i32,
    @align(16) uSlabEnd_0 : i32,
    @align(4) uAlpha_0 : f32,
    @align(8) uRayCount_0 : i32,
    @align(4) uSweep_0 : i32,
    @align(16) uCacheScale_0 : f32,
    @align(4) uSkyReject_0 : f32,
};

@binding(49) @group(0) var<uniform> Globals_0 : SLANG_ParameterGroup_Globals_std140_0;
@binding(37) @group(0) var uClipmaps_0 : texture_3d<f32>;

@binding(69) @group(0) var uClipmapsSampler_0 : sampler;

@binding(19) @group(0) var<storage, read_write> uCells_0 : array<vec2<i32>>;

@binding(33) @group(0) var uAlbedo_0 : texture_2d<f32>;

@binding(20) @group(0) var<storage, read_write> uCellPages_0 : array<i32>;

@binding(16) @group(0) var<storage, read> uPages_0 : array<vec4<f32>>;

@binding(65) @group(0) var uAlbedoSampler_0 : sampler;

@binding(36) @group(0) var uDepth_0 : texture_2d<f32>;

@binding(68) @group(0) var uDepthSampler_0 : sampler;

@binding(38) @group(0) var uFinal_0 : texture_2d<f32>;

@binding(70) @group(0) var uFinalSampler_0 : sampler;

@binding(44) @group(0) var uCacheRedHistory_0 : texture_3d<f32>;

@binding(45) @group(0) var uCacheGreenHistory_0 : texture_3d<f32>;

@binding(46) @group(0) var uCacheBlueHistory_0 : texture_3d<f32>;

@binding(47) @group(0) var uCacheDistanceHistory_0 : texture_3d<f32>;

@binding(0) @group(0) var uCacheRed_0 : texture_storage_3d<rgba16float, write>;

@binding(1) @group(0) var uCacheGreen_0 : texture_storage_3d<rgba16float, write>;

@binding(2) @group(0) var uCacheBlue_0 : texture_storage_3d<rgba16float, write>;

@binding(3) @group(0) var uCacheDistance_0 : texture_storage_3d<rgba16float, write>;

fn SphereDirection_0( i_0 : i32,  count_0 : i32,  jitter_0 : f32) -> vec3<f32>
{
    var k_0 : f32 = f32(i_0) + jitter_0;
    var cosTheta_0 : f32 = clamp(1.0f - (2.0f * k_0 + 1.0f) / f32(count_0), -1.0f, 1.0f);
    var sinTheta_0 : f32 = sqrt(max(0.0f, 1.0f - cosTheta_0 * cosTheta_0));
    var phi_0 : f32 = 6.28318548202514648f * k_0 * 0.6180340051651001f;
    return vec3<f32>(sinTheta_0 * cos(phi_0), sinTheta_0 * sin(phi_0), cosTheta_0);
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
        var _S1 : f32 = Globals_0.uClipmapOrigin_0[level_0].w;
        var voxel_0 : vec3<f32> = (world_0 - Globals_0.uClipmapOrigin_0[level_0].xyz) / vec3<f32>(_S1);
        var _S2 : bool;
        if((any((voxel_0 < vec3<f32>(0.5f, 0.5f, 0.5f)))))
        {
            _S2 = true;
        }
        else
        {
            _S2 = (any((voxel_0 > vec3<f32>(f32(Globals_0.uShadowResolution_0) - 0.5f, f32(Globals_0.uShadowResolution_0) - 0.5f, f32(Globals_0.uShadowResolution_0) - 0.5f))));
        }
        if(_S2)
        {
            level_0 = level_0 + i32(1);
            continue;
        }
        var uvw_0 : vec3<f32> = vec3<f32>(voxel_0.x / f32(Globals_0.uShadowResolution_0), voxel_0.y / f32(Globals_0.uShadowResolution_0), (f32(level_0) * f32(Globals_0.uShadowResolution_0) + voxel_0.z) / f32(Globals_0.uShadowResolution_0 * Globals_0.uShadowLevels_0));
        (*known_0) = true;
        (*raw_0) = (textureSampleLevel((uClipmaps_0), (uClipmapsSampler_0), (uvw_0), (0.0f))).x;
        (*resolved_0) = _S1;
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

fn VisibilityAt_0( world_1 : vec3<f32>,  normal_0 : vec3<f32>,  direction_0 : vec3<f32>,  hit_0 : ptr<function, vec3<f32>>) -> f32
{
    (*hit_0) = world_1;
    if((Globals_0.uShadowLevels_0) <= i32(0))
    {
        return 1.0f;
    }
    var _S3 : vec3<f32> = world_1 + normal_0 * vec3<f32>(Globals_0.uClipmapOrigin_0[i32(0)].w) * vec3<f32>(1.5f);
    var _S4 : f32 = Globals_0.uClipmapOrigin_0[i32(0)].w * f32(Globals_0.uShadowResolution_0) * f32((i32(1) << (u32(Globals_0.uShadowLevels_0 - i32(1)))));
    var step_0 : i32 = i32(0);
    var travelled_1 : f32 = 0.0f;
    for(;;)
    {
        var _S5 : bool;
        if(step_0 < i32(64))
        {
            _S5 = travelled_1 < _S4;
        }
        else
        {
            _S5 = false;
        }
        if(_S5)
        {
        }
        else
        {
            break;
        }
        var known_1 : bool;
        var raw_1 : f32;
        var resolved_2 : f32;
        var distance_0 : f32 = DistanceAt_0(_S3 + direction_0 * vec3<f32>(travelled_1), &(known_1), &(raw_1), &(resolved_2));
        if(!known_1)
        {
            return 1.0f;
        }
        var hitDistance_1 : f32;
        var _S6 : bool = FieldStepHits_0(distance_0, travelled_1, resolved_2, &(hitDistance_1));
        if(_S6)
        {
            (*hit_0) = _S3 + direction_0 * vec3<f32>(hitDistance_1);
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

fn ProjectOntoCard_0( page_0 : i32,  world_2 : vec3<f32>,  directionW_0 : vec3<f32>,  across_0 : ptr<function, vec2<f32>>,  halfSize_0 : ptr<function, vec2<f32>>,  behind_0 : ptr<function, f32>,  depth_0 : ptr<function, f32>,  facing_0 : ptr<function, vec3<f32>>,  directionLocal_0 : ptr<function, vec3<f32>>,  litRect_0 : ptr<function, vec4<f32>>,  cardPage_0 : ptr<function, vec4<f32>>)
{
    var _S7 : i32 = page_0 * i32(10);
    (*litRect_0) = uPages_0[_S7];
    (*cardPage_0) = uPages_0[_S7 + i32(1)];
    var centreAndHalfWidth_0 : vec4<f32> = uPages_0[_S7 + i32(2)];
    var rightAndHalfHeight_0 : vec4<f32> = uPages_0[_S7 + i32(3)];
    var upAndDepth_0 : vec4<f32> = uPages_0[_S7 + i32(4)];
    var direction_1 : vec4<f32> = uPages_0[_S7 + i32(5)];
    var row0_0 : vec4<f32> = uPages_0[_S7 + i32(6)];
    var row1_0 : vec4<f32> = uPages_0[_S7 + i32(7)];
    var row2_0 : vec4<f32> = uPages_0[_S7 + i32(8)];
    var delta_0 : vec3<f32> = world_2 - vec3<f32>(row0_0.w, row1_0.w, row2_0.w);
    var _S8 : vec3<f32> = row0_0.xyz;
    var _S9 : vec3<f32> = row1_0.xyz;
    var _S10 : vec3<f32> = row2_0.xyz;
    var local_0 : vec3<f32> = vec3<f32>(dot(_S8, delta_0), dot(_S9, delta_0), dot(_S10, delta_0));
    (*directionLocal_0) = vec3<f32>(dot(_S8, directionW_0), dot(_S9, directionW_0), dot(_S10, directionW_0));
    var relative_0 : vec3<f32> = local_0 - centreAndHalfWidth_0.xyz;
    var _S11 : vec2<f32> = max(vec2<f32>(centreAndHalfWidth_0.w, rightAndHalfHeight_0.w), vec2<f32>(9.99999997475242708e-07f, 9.99999997475242708e-07f));
    (*halfSize_0) = _S11;
    (*across_0) = vec2<f32>(dot(relative_0, rightAndHalfHeight_0.xyz), dot(relative_0, upAndDepth_0.xyz)) / _S11;
    var _S12 : vec3<f32> = direction_1.xyz;
    (*behind_0) = - dot(relative_0, _S12);
    (*depth_0) = upAndDepth_0.w;
    (*facing_0) = _S12;
    return;
}

fn LookupCardWithin_0( world_3 : vec3<f32>,  arrivingW_0 : vec3<f32>,  skin_0 : f32,  found_0 : ptr<function, vec2<f32>>,  litFound_0 : ptr<function, vec2<f32>>,  agreement_0 : ptr<function, f32>,  cardDepth_0 : ptr<function, f32>) -> bool
{
    (*cardDepth_0) = 0.0f;
    const _S13 : vec2<f32> = vec2<f32>(0.0f, 0.0f);
    (*found_0) = _S13;
    (*litFound_0) = _S13;
    (*agreement_0) = 1.0e+09f;
    if((Globals_0.uLookupBuckets_0) <= i32(0))
    {
        return false;
    }
    var _S14 : vec2<i32> = uCells_0[LookupBucket_0(vec3<i32>(floor((world_3 - Globals_0.uLookupMin_0) / vec3<f32>(Globals_0.uLookupCell_0))))];
    var albedoWidth_0 : u32;
    var albedoHeight_0 : u32;
    {var dim = textureDimensions((uAlbedo_0));((albedoWidth_0)) = dim.x;((albedoHeight_0)) = dim.y;};
    var _S15 : vec2<f32> = vec2<f32>(f32(albedoWidth_0), f32(albedoHeight_0));
    var settled_0 : bool = false;
    var anyFound_0 : bool = false;
    var walk_0 : i32 = i32(0);
    for(;;)
    {
        var _S16 : bool;
        if(walk_0 < i32(2))
        {
            _S16 = !settled_0;
        }
        else
        {
            _S16 = false;
        }
        if(_S16)
        {
        }
        else
        {
            break;
        }
        var _S17 : bool = walk_0 == i32(0);
        var _S18 : vec2<i32>;
        if(_S17)
        {
            _S18 = _S14;
        }
        else
        {
            _S18 = uCells_0[Globals_0.uLookupBuckets_0];
        }
        var settled_1 : bool = settled_0;
        var anyFound_1 : bool = anyFound_0;
        var slot_0 : i32 = i32(0);
        for(;;)
        {
            var _S19 : bool;
            if(slot_0 < (_S18.y))
            {
                _S19 = !settled_1;
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
            var page_1 : i32 = uCellPages_0[_S18.x + slot_0];
            var _S20 : i32 = uCellPages_0[_S18.x + slot_0] * i32(10);
            if(((uPages_0[_S20 + i32(5)].w) > 0.5f) == _S17)
            {
                slot_0 = slot_0 + i32(1);
                continue;
            }
            var bound_0 : vec4<f32> = uPages_0[_S20 + i32(9)];
            var toCentre_0 : vec3<f32> = world_3 - bound_0.xyz;
            var _S21 : f32 = bound_0.w;
            if((dot(toCentre_0, toCentre_0)) > (_S21 * _S21))
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
            ProjectOntoCard_0(page_1, world_3, (vec3<f32>(0) - arrivingW_0), &(across_1), &(halfSize_1), &(behind_1), &(depth_1), &(facing_1), &(arrivingLocal_0), &(litRect_1), &(cardPage_1));
            var _S22 : bool;
            if((abs(across_1.x)) > 1.0f)
            {
                _S22 = true;
            }
            else
            {
                _S22 = (abs(across_1.y)) > 1.0f;
            }
            if(_S22)
            {
                slot_0 = slot_0 + i32(1);
                continue;
            }
            var _S23 : bool;
            if((dot(arrivingW_0, arrivingW_0)) > 0.5f)
            {
                _S23 = (dot(facing_1, arrivingLocal_0)) < -0.05000000074505806f;
            }
            else
            {
                _S23 = false;
            }
            if(_S23)
            {
                slot_0 = slot_0 + i32(1);
                continue;
            }
            var _S24 : bool;
            if(behind_1 < (- skin_0))
            {
                _S24 = true;
            }
            else
            {
                _S24 = behind_1 > (depth_1 + skin_0);
            }
            if(_S24)
            {
                slot_0 = slot_0 + i32(1);
                continue;
            }
            var _S25 : vec2<f32> = vec2<f32>(0.5f);
            var uv_0 : vec2<f32> = across_1 * _S25 + _S25;
            var texel_0 : vec2<f32> = (cardPage_1.xy + uv_0 * cardPage_1.zw) / _S15;
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
                var _S26 : bool = error_0 < 0.01999999955296516f;
                (*litFound_0) = (litRect_1.xy + uv_0 * litRect_1.zw) / Globals_0.uAtlasSize_0.xy;
                settled_2 = _S26;
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
        var _S27 : i32 = walk_0 + i32(1);
        settled_0 = settled_1;
        anyFound_0 = anyFound_1;
        walk_0 = _S27;
    }
    return anyFound_0;
}

fn LookupCardHit_0( world_4 : vec3<f32>,  rayDirection_0 : vec3<f32>,  found_1 : ptr<function, vec2<f32>>,  litFound_1 : ptr<function, vec2<f32>>,  agreement_1 : ptr<function, f32>) -> bool
{
    var depth_2 : f32;
    var _S28 : bool = LookupCardWithin_0(world_4, rayDirection_0, 0.25f, &((*found_1)), &((*litFound_1)), &((*agreement_1)), &(depth_2));
    return _S28;
}

fn SkyRadiance_0( direction_2 : vec3<f32>) -> vec3<f32>
{
    if((Globals_0.uSkyShValid_0) == i32(0))
    {
        return Globals_0.uSkyColour_0;
    }
    var n_0 : vec4<f32> = vec4<f32>(direction_2, 1.0f);
    var q_0 : vec4<f32> = n_0.xyzz * n_0.yzzx;
    var _S29 : f32 = direction_2.x;
    var _S30 : f32 = direction_2.y;
    return max(vec3<f32>(0.0f, 0.0f, 0.0f), vec3<f32>(dot(Globals_0.uSkySh_0[i32(0)], n_0), dot(Globals_0.uSkySh_0[i32(1)], n_0), dot(Globals_0.uSkySh_0[i32(2)], n_0)) + vec3<f32>(dot(Globals_0.uSkySh_0[i32(3)], q_0), dot(Globals_0.uSkySh_0[i32(4)], q_0), dot(Globals_0.uSkySh_0[i32(5)], q_0)) + Globals_0.uSkySh_0[i32(6)].xyz * vec3<f32>((_S29 * _S29 - _S30 * _S30)));
}

@compute
@workgroup_size(4, 4, 4)
fn computeMain(@builtin(global_invocation_id) id_0 : vec3<u32>)
{
    var dist_0 : f32;
    var _S31 : vec3<i32> = vec3<i32>(Globals_0.uCacheCount_0 + vec3<f32>(0.5f));
    var _S32 : i32 = i32(id_0.x);
    var _S33 : i32 = i32(id_0.y);
    var _S34 : i32 = Globals_0.uSlabBegin_0 + i32(id_0.z);
    var probe_0 : vec3<i32> = vec3<i32>(_S32, _S33, _S34);
    var _S35 : i32 = _S31.x;
    var sky_0 : bool;
    if(_S32 >= _S35)
    {
        sky_0 = true;
    }
    else
    {
        sky_0 = _S33 >= (_S31.y);
    }
    if(sky_0)
    {
        sky_0 = true;
    }
    else
    {
        sky_0 = _S34 >= (Globals_0.uSlabEnd_0);
    }
    if(sky_0)
    {
        sky_0 = true;
    }
    else
    {
        sky_0 = _S34 >= (_S31.z);
    }
    if(sky_0)
    {
        return;
    }
    if((Globals_0.uShadowLevels_0) <= i32(0))
    {
        sky_0 = true;
    }
    else
    {
        sky_0 = (Globals_0.uLookupBuckets_0) <= i32(0);
    }
    if(sky_0)
    {
        return;
    }
    var _S36 : vec3<f32> = Globals_0.uCacheOrigin_0 + vec3<f32>(probe_0) * vec3<f32>(Globals_0.uCacheSpacing_0);
    var _S37 : f32 = fract(f32(Globals_0.uSweep_0) * 0.6180340051651001f);
    var _S38 : f32 = Globals_0.uCacheSpacing_0 * f32(max(_S35, max(_S31.y, _S31.z)));
    const _S39 : vec3<f32> = vec3<f32>(0.0f, 0.0f, 0.0f);
    var ray_0 : i32 = i32(0);
    var escaped_0 : f32 = 0.0f;
    var s0_0 : vec3<f32> = _S39;
    var s1_0 : vec3<f32> = _S39;
    var s2_0 : vec3<f32> = _S39;
    var s3_0 : vec3<f32> = _S39;
    var c0_0 : vec3<f32> = _S39;
    var c1_0 : vec3<f32> = _S39;
    var c2_0 : vec3<f32> = _S39;
    var c3_0 : vec3<f32> = _S39;
    var distSum_0 : f32 = 0.0f;
    var distSqSum_0 : f32 = 0.0f;
    for(;;)
    {
        if(ray_0 < (Globals_0.uRayCount_0))
        {
        }
        else
        {
            break;
        }
        var dir_0 : vec3<f32> = SphereDirection_0(ray_0, Globals_0.uRayCount_0, _S37);
        var hit_1 : vec3<f32>;
        var _S40 : f32 = VisibilityAt_0(_S36, dir_0, dir_0, &(hit_1));
        var radiance_0 : vec3<f32>;
        if(_S40 <= 0.5f)
        {
            var _S41 : f32 = length(hit_1 - _S36);
            var found_2 : vec2<f32>;
            var litFound_2 : vec2<f32>;
            var agreement_2 : f32;
            var _S42 : bool = LookupCardHit_0(hit_1, dir_0, &(found_2), &(litFound_2), &(agreement_2));
            if(_S42)
            {
                radiance_0 = (textureSampleLevel((uFinal_0), (uFinalSampler_0), (litFound_2), (0.0f))).xyz * vec3<f32>(Globals_0.uBounceScale_0);
            }
            else
            {
                radiance_0 = _S39;
            }
            sky_0 = false;
            dist_0 = _S41;
        }
        else
        {
            var escaped_1 : f32 = escaped_0 + 1.0f;
            radiance_0 = SkyRadiance_0(dir_0);
            sky_0 = true;
            dist_0 = _S38;
            escaped_0 = escaped_1;
        }
        var radiance_1 : vec3<f32> = min(radiance_0, vec3<f32>(8.0f, 8.0f, 8.0f));
        if(sky_0)
        {
            var s1_1 : vec3<f32> = s1_0 + vec3<f32>((0.48860299587249756f * dir_0.x)) * radiance_1;
            var s2_1 : vec3<f32> = s2_0 + vec3<f32>((0.48860299587249756f * dir_0.y)) * radiance_1;
            var s3_1 : vec3<f32> = s3_0 + vec3<f32>((0.48860299587249756f * dir_0.z)) * radiance_1;
            s0_0 = s0_0 + vec3<f32>(0.28209498524665833f) * radiance_1;
            s1_0 = s1_1;
            s2_0 = s2_1;
            s3_0 = s3_1;
        }
        else
        {
            var c1_1 : vec3<f32> = c1_0 + vec3<f32>((0.48860299587249756f * dir_0.x)) * radiance_1;
            var c2_1 : vec3<f32> = c2_0 + vec3<f32>((0.48860299587249756f * dir_0.y)) * radiance_1;
            var c3_1 : vec3<f32> = c3_0 + vec3<f32>((0.48860299587249756f * dir_0.z)) * radiance_1;
            c0_0 = c0_0 + vec3<f32>(0.28209498524665833f) * radiance_1;
            c1_0 = c1_1;
            c2_0 = c2_1;
            c3_0 = c3_1;
        }
        var distSum_1 : f32 = distSum_0 + dist_0;
        var distSqSum_1 : f32 = distSqSum_0 + dist_0 * dist_0;
        ray_0 = ray_0 + i32(1);
        distSum_0 = distSum_1;
        distSqSum_0 = distSqSum_1;
    }
    if((Globals_0.uSkyReject_0) <= 0.0f)
    {
        dist_0 = 1.0f;
    }
    else
    {
        dist_0 = smoothstep(Globals_0.uSkyReject_0 * 0.33000001311302185f, Globals_0.uSkyReject_0, escaped_0 / f32(Globals_0.uRayCount_0));
    }
    var _S43 : vec3<f32> = vec3<f32>((12.56637096405029297f / f32(Globals_0.uRayCount_0) * Globals_0.uCacheScale_0));
    var c0_1 : vec3<f32> = (c0_0 + s0_0 * vec3<f32>(dist_0)) * _S43;
    var c1_2 : vec3<f32> = (c1_0 + s1_0 * vec3<f32>(dist_0)) * _S43;
    var c2_2 : vec3<f32> = (c2_0 + s2_0 * vec3<f32>(dist_0)) * _S43;
    var c3_2 : vec3<f32> = (c3_0 + s3_0 * vec3<f32>(dist_0)) * _S43;
    var invN_0 : f32 = 1.0f / f32(Globals_0.uRayCount_0);
    var red_0 : vec4<f32> = vec4<f32>(c0_1.x, c1_2.x, c2_2.x, c3_2.x);
    var green_0 : vec4<f32> = vec4<f32>(c0_1.y, c1_2.y, c2_2.y, c3_2.y);
    var blue_0 : vec4<f32> = vec4<f32>(c0_1.z, c1_2.z, c2_2.z, c3_2.z);
    var moments_0 : vec4<f32> = vec4<f32>(distSum_0 * invN_0, distSqSum_0 * invN_0, 0.0f, 0.0f);
    var red_1 : vec4<f32>;
    var green_1 : vec4<f32>;
    var blue_1 : vec4<f32>;
    var moments_1 : vec4<f32>;
    if((Globals_0.uAlpha_0) < 1.0f)
    {
        var _S44 : vec4<i32> = vec4<i32>(probe_0, i32(0));
        var _S45 : vec4<f32> = mix((textureLoad((uCacheGreenHistory_0), ((_S44)).xyz, ((_S44)).w)), green_0, vec4<f32>(Globals_0.uAlpha_0));
        var _S46 : vec4<f32> = mix((textureLoad((uCacheBlueHistory_0), ((_S44)).xyz, ((_S44)).w)), blue_0, vec4<f32>(Globals_0.uAlpha_0));
        var _S47 : vec4<f32> = mix((textureLoad((uCacheDistanceHistory_0), ((_S44)).xyz, ((_S44)).w)), moments_0, vec4<f32>(Globals_0.uAlpha_0));
        red_1 = mix((textureLoad((uCacheRedHistory_0), ((_S44)).xyz, ((_S44)).w)), red_0, vec4<f32>(Globals_0.uAlpha_0));
        green_1 = _S45;
        blue_1 = _S46;
        moments_1 = _S47;
    }
    else
    {
        red_1 = red_0;
        green_1 = green_0;
        blue_1 = blue_0;
        moments_1 = moments_0;
    }
    var _S48 : vec3<u32> = vec3<u32>(probe_0);
    textureStore((uCacheRed_0), (_S48), (red_1));
    textureStore((uCacheGreen_0), (_S48), (green_1));
    textureStore((uCacheBlue_0), (_S48), (blue_1));
    textureStore((uCacheDistance_0), (_S48), (moments_1));
    return;
}

