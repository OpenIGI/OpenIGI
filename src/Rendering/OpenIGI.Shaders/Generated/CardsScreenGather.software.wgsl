struct _Array_std140_float4_0
{
    @align(16) data_0 : array<vec4<f32>, i32(4)>,
};

struct SLANG_ParameterGroup_Globals_std140_0
{
    @align(16) uClipmapOrigin_0 : array<vec4<f32>, i32(4)>,
    @align(16) uSkySh_0 : array<vec4<f32>, i32(7)>,
    @align(16) uUnproject_0 : vec4<f32>,
    @align(16) uViewToWorld0_0 : vec3<f32>,
    @align(16) uViewToWorld1_0 : vec3<f32>,
    @align(16) uViewToWorld2_0 : vec3<f32>,
    @align(16) uCameraToCache_0 : vec3<f32>,
    @align(16) uTraceOrigin_0 : vec3<f32>,
    @align(16) uAtlasSize_0 : vec3<f32>,
    @align(16) uLookupMin_0 : vec3<f32>,
    @align(16) uSkyColour_0 : vec3<f32>,
    @align(16) uClipmapRange_0 : _Array_std140_float4_0,
    @align(16) uShadowLevels_0 : i32,
    @align(4) uShadowResolution_0 : i32,
    @align(8) uLookupBuckets_0 : i32,
    @align(4) uLookupCell_0 : f32,
    @align(16) uScreenWidth_0 : i32,
    @align(4) uScreenHeight_0 : i32,
    @align(8) uProbeStep_0 : i32,
    @align(4) uMaxRayIntensity_0 : f32,
    @align(16) uScreenTraceSteps_0 : i32,
    @align(4) uScreenTraceDistance_0 : f32,
    @align(8) uScreenThickness_0 : f32,
    @align(4) uSkyShValid_0 : i32,
    @align(16) uBounceScale_0 : f32,
    @align(4) uDebugSource_0 : i32,
};

@binding(49) @group(0) var<uniform> Globals_0 : SLANG_ParameterGroup_Globals_std140_0;
@binding(39) @group(0) var uSceneDepth_0 : texture_depth_2d;

@binding(71) @group(0) var uSceneDepthSampler_0 : sampler;

@binding(0) @group(0) var uIndirect_0 : texture_storage_2d<rgba16float, write>;

@binding(40) @group(0) var uSceneNormal_0 : texture_2d<f32>;

@binding(72) @group(0) var uSceneNormalSampler_0 : sampler;

@binding(43) @group(0) var uSceneColour_0 : texture_2d<f32>;

@binding(75) @group(0) var uSceneColourSampler_0 : sampler;

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

fn DepthBilinearLoaded_0( depth_0 : texture_depth_2d,  uv_0 : vec2<f32>) -> f32
{
    var width_0 : u32;
    var height_0 : u32;
    {var dim = textureDimensions((depth_0));((width_0)) = dim.x;((height_0)) = dim.y;};
    var last_0 : vec2<i32> = vec2<i32>(i32(width_0) - i32(1), i32(height_0) - i32(1));
    var position_0 : vec2<f32> = uv_0 * vec2<f32>(f32(width_0), f32(height_0)) - vec2<f32>(0.5f);
    var floored_0 : vec2<f32> = floor(position_0);
    var weight_0 : vec2<f32> = position_0 - floored_0;
    var _S1 : vec2<i32> = vec2<i32>(floored_0);
    const _S2 : vec2<i32> = vec2<i32>(i32(0), i32(0));
    var _S3 : vec3<i32> = vec3<i32>(clamp(_S1, _S2, last_0), i32(0));
    var _S4 : vec3<i32> = vec3<i32>(clamp(_S1 + vec2<i32>(i32(1), i32(0)), _S2, last_0), i32(0));
    var _S5 : vec3<i32> = vec3<i32>(clamp(_S1 + vec2<i32>(i32(0), i32(1)), _S2, last_0), i32(0));
    var _S6 : vec3<i32> = vec3<i32>(clamp(_S1 + vec2<i32>(i32(1), i32(1)), _S2, last_0), i32(0));
    var _S7 : f32 = weight_0.x;
    return mix(mix((textureLoad((depth_0), ((_S3)).xy, ((_S3)).z)), (textureLoad((depth_0), ((_S4)).xy, ((_S4)).z)), _S7), mix((textureLoad((depth_0), ((_S5)).xy, ((_S5)).z)), (textureLoad((depth_0), ((_S6)).xy, ((_S6)).z)), _S7), weight_0.y);
}

fn DepthBilinear_0( depth_1 : texture_depth_2d,  linear_0 : sampler,  uv_1 : vec2<f32>) -> f32
{
    return DepthBilinearLoaded_0(depth_1, uv_1);
}

fn IsSkyDepth_0( depth_2 : f32) -> bool
{
    return depth_2 <= 0.0f;
}

fn ViewPositionAt_0( uv_2 : vec2<f32>,  depth_3 : f32) -> vec3<f32>
{
    var near_0 : f32 = Globals_0.uUnproject_0.z;
    var far_0 : f32 = Globals_0.uUnproject_0.w;
    var viewZ_0 : f32 = 2.0f * near_0 * far_0 / (far_0 + near_0 - (1.0f - 2.0f * depth_3) * (far_0 - near_0));
    var ndc_0 : vec2<f32> = uv_2 * vec2<f32>(2.0f) - vec2<f32>(1.0f);
    return vec3<f32>(ndc_0.x * viewZ_0 / Globals_0.uUnproject_0.x, -1.0f * (ndc_0.y * viewZ_0) / Globals_0.uUnproject_0.y, viewZ_0);
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
    var _S8 : f32 = normal_1.z;
    if(_S8 < -0.99999988079071045f)
    {
        basis_0.tangent_0 = vec3<f32>(0.0f, -1.0f, 0.0f);
        basis_0.bitangent_0 = vec3<f32>(-1.0f, 0.0f, 0.0f);
        return basis_0;
    }
    var a_0 : f32 = 1.0f / (1.0f + _S8);
    var _S9 : f32 = normal_1.x;
    var _S10 : f32 = - _S9;
    var _S11 : f32 = normal_1.y;
    var b_0 : f32 = _S10 * _S11 * a_0;
    basis_0.tangent_0 = vec3<f32>(1.0f - _S9 * _S9 * a_0, b_0, _S10);
    basis_0.bitangent_0 = vec3<f32>(b_0, 1.0f - _S11 * _S11 * a_0, - _S11);
    return basis_0;
}

fn ToView_0( world_0 : vec3<f32>) -> vec3<f32>
{
    return vec3<f32>(dot(Globals_0.uViewToWorld0_0, world_0), dot(Globals_0.uViewToWorld1_0, world_0), dot(Globals_0.uViewToWorld2_0, world_0));
}

fn Orient_0( basis_1 : HemisphereBasis_0,  local_0 : vec3<f32>) -> vec3<f32>
{
    return basis_1.tangent_0 * vec3<f32>(local_0.x) + basis_1.bitangent_0 * vec3<f32>(local_0.y) + basis_1.normal_0 * vec3<f32>(local_0.z);
}

fn ProjectToUv_0( viewPosition_0 : vec3<f32>) -> vec2<f32>
{
    var _S12 : f32 = viewPosition_0.z;
    var _S13 : vec2<f32> = vec2<f32>(0.5f);
    return vec2<f32>(viewPosition_0.x * Globals_0.uUnproject_0.x / _S12, -1.0f * (viewPosition_0.y * Globals_0.uUnproject_0.y) / _S12) * _S13 + _S13;
}

fn CapRayIntensity_0( radiance_0 : vec3<f32>) -> vec3<f32>
{
    var _S14 : f32 = max(radiance_0.x, max(radiance_0.y, radiance_0.z));
    var _S15 : vec3<f32>;
    if(_S14 > (Globals_0.uMaxRayIntensity_0))
    {
        _S15 = radiance_0 * vec3<f32>((Globals_0.uMaxRayIntensity_0 / _S14));
    }
    else
    {
        _S15 = radiance_0;
    }
    return _S15;
}

fn ScreenTraceRadiance_0( viewOrigin_0 : vec3<f32>,  viewNormal_0 : vec3<f32>,  viewDir_0 : vec3<f32>,  radiance_1 : ptr<function, vec3<f32>>) -> bool
{
    var hitFraction_0 : f32;
    (*radiance_1) = vec3<f32>(0.0f, 0.0f, 0.0f);
    var _S16 : bool;
    if((Globals_0.uScreenTraceSteps_0) <= i32(0))
    {
        _S16 = true;
    }
    else
    {
        _S16 = (viewDir_0.z) == 0.0f;
    }
    if(_S16)
    {
        return false;
    }
    var origin_0 : vec3<f32> = viewOrigin_0 + viewNormal_0 * vec3<f32>(0.01999999955296516f);
    var farPoint_0 : vec3<f32> = origin_0 + viewDir_0 * vec3<f32>(Globals_0.uScreenTraceDistance_0);
    var farPoint_1 : vec3<f32>;
    if((farPoint_0.z) < (Globals_0.uUnproject_0.z + 0.00999999977648258f))
    {
        farPoint_1 = origin_0 + viewDir_0 * vec3<f32>(((Globals_0.uUnproject_0.z + 0.00999999977648258f - origin_0.z) / viewDir_0.z));
    }
    else
    {
        farPoint_1 = farPoint_0;
    }
    var _S17 : f32 = farPoint_1.z;
    if(_S17 <= (Globals_0.uUnproject_0.z))
    {
        return false;
    }
    var startUv_0 : vec2<f32> = ProjectToUv_0(origin_0);
    var travel_0 : vec2<f32> = ProjectToUv_0(farPoint_1) - startUv_0;
    var used_0 : i32 = i32(clamp(length(travel_0 / (vec2<f32>(1.0f) / vec2<f32>(f32(Globals_0.uScreenWidth_0), f32(Globals_0.uScreenHeight_0)))), 1.0f, f32(Globals_0.uScreenTraceSteps_0)));
    var stride_0 : f32 = 1.0f / f32(used_0);
    var _S18 : f32 = fract(sin(dot(startUv_0, vec2<f32>(12.98980045318603516f, 78.233001708984375f))) * 43758.546875f) * stride_0;
    var previous_0 : f32 = 0.0f;
    var i_0 : i32 = i32(1);
    for(;;)
    {
        if(i_0 <= i32(256))
        {
        }
        else
        {
            hitFraction_0 = -1.0f;
            break;
        }
        if(i_0 > used_0)
        {
            hitFraction_0 = -1.0f;
            break;
        }
        var t_0 : f32 = f32(i_0) * stride_0 + _S18;
        if(t_0 > 1.0f)
        {
            hitFraction_0 = -1.0f;
            break;
        }
        var uv_3 : vec2<f32> = startUv_0 + travel_0 * vec2<f32>(t_0);
        if((any((uv_3 < vec2<f32>(0.0f, 0.0f)))))
        {
            _S16 = true;
        }
        else
        {
            _S16 = (any((uv_3 > vec2<f32>(1.0f, 1.0f))));
        }
        if(_S16)
        {
            hitFraction_0 = -1.0f;
            break;
        }
        var _S19 : f32 = origin_0.z;
        var sceneDepth_0 : f32 = ViewPositionAt_0(uv_3, DepthBilinear_0(uSceneDepth_0, uSceneDepthSampler_0, uv_3)).z;
        var difference_0 : f32 = 1.0f / mix(1.0f / _S19, 1.0f / _S17, t_0) - sceneDepth_0;
        var _S20 : bool;
        if(difference_0 > 0.0f)
        {
            _S20 = difference_0 < (Globals_0.uScreenThickness_0 + sceneDepth_0 * 0.01999999955296516f);
        }
        else
        {
            _S20 = false;
        }
        if(_S20)
        {
            hitFraction_0 = previous_0;
            var high_0 : f32 = t_0;
            var refine_0 : i32 = i32(0);
            for(;;)
            {
                if(refine_0 < i32(4))
                {
                }
                else
                {
                    break;
                }
                var mid_0 : f32 = (hitFraction_0 + high_0) * 0.5f;
                var midUv_0 : vec2<f32> = startUv_0 + travel_0 * vec2<f32>(mid_0);
                if((1.0f / mix(1.0f / _S19, 1.0f / _S17, mid_0) - ViewPositionAt_0(midUv_0, DepthBilinear_0(uSceneDepth_0, uSceneDepthSampler_0, midUv_0)).z) > 0.0f)
                {
                    high_0 = mid_0;
                }
                else
                {
                    hitFraction_0 = mid_0;
                }
                refine_0 = refine_0 + i32(1);
            }
            hitFraction_0 = high_0;
            break;
        }
        var _S21 : i32 = i_0 + i32(1);
        previous_0 = t_0;
        i_0 = _S21;
    }
    if(hitFraction_0 <= 0.0f)
    {
        return false;
    }
    var hitUv_0 : vec2<f32> = startUv_0 + travel_0 * vec2<f32>(hitFraction_0);
    if((dot(ToView_0((textureSampleLevel((uSceneNormal_0), (uSceneNormalSampler_0), (hitUv_0), (0.0f))).xyz * vec3<f32>(2.0f) - vec3<f32>(1.0f)), viewDir_0)) >= 0.0f)
    {
        return false;
    }
    var hitColour_0 : vec4<f32> = (textureSampleLevel((uSceneColour_0), (uSceneColourSampler_0), (hitUv_0), (0.0f)));
    if((hitColour_0.w) < 0.5f)
    {
        return false;
    }
    (*radiance_1) = CapRayIntensity_0(hitColour_0.xyz);
    return true;
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
        var _S22 : f32 = Globals_0.uClipmapOrigin_0[level_0].w;
        var voxel_0 : vec3<f32> = (world_1 - Globals_0.uClipmapOrigin_0[level_0].xyz) / vec3<f32>(_S22);
        var _S23 : bool;
        if((any((voxel_0 < vec3<f32>(0.5f, 0.5f, 0.5f)))))
        {
            _S23 = true;
        }
        else
        {
            _S23 = (any((voxel_0 > vec3<f32>(f32(Globals_0.uShadowResolution_0) - 0.5f, f32(Globals_0.uShadowResolution_0) - 0.5f, f32(Globals_0.uShadowResolution_0) - 0.5f))));
        }
        if(_S23)
        {
            level_0 = level_0 + i32(1);
            continue;
        }
        var uvw_0 : vec3<f32> = vec3<f32>(voxel_0.x / f32(Globals_0.uShadowResolution_0), voxel_0.y / f32(Globals_0.uShadowResolution_0), (f32(level_0) * f32(Globals_0.uShadowResolution_0) + voxel_0.z) / f32(Globals_0.uShadowResolution_0 * Globals_0.uShadowLevels_0));
        (*known_0) = true;
        (*raw_0) = (textureSampleLevel((uClipmaps_0), (uClipmapsSampler_0), (uvw_0), (0.0f))).x;
        (*resolved_0) = _S22;
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

fn VisibilityAt_0( world_2 : vec3<f32>,  normal_2 : vec3<f32>,  direction_0 : vec3<f32>,  hit_0 : ptr<function, vec3<f32>>) -> f32
{
    (*hit_0) = world_2;
    if((Globals_0.uShadowLevels_0) <= i32(0))
    {
        return 1.0f;
    }
    var _S24 : vec3<f32> = world_2 + normal_2 * vec3<f32>(Globals_0.uClipmapOrigin_0[i32(0)].w) * vec3<f32>(1.5f);
    var _S25 : f32 = Globals_0.uClipmapOrigin_0[i32(0)].w * f32(Globals_0.uShadowResolution_0) * f32((i32(1) << (u32(Globals_0.uShadowLevels_0 - i32(1)))));
    var step_0 : i32 = i32(0);
    var travelled_1 : f32 = 0.0f;
    for(;;)
    {
        var _S26 : bool;
        if(step_0 < i32(64))
        {
            _S26 = travelled_1 < _S25;
        }
        else
        {
            _S26 = false;
        }
        if(_S26)
        {
        }
        else
        {
            break;
        }
        var known_1 : bool;
        var raw_1 : f32;
        var resolved_2 : f32;
        var distance_0 : f32 = DistanceAt_0(_S24 + direction_0 * vec3<f32>(travelled_1), &(known_1), &(raw_1), &(resolved_2));
        if(!known_1)
        {
            return 1.0f;
        }
        var hitDistance_1 : f32;
        var _S27 : bool = FieldStepHits_0(distance_0, travelled_1, resolved_2, &(hitDistance_1));
        if(_S27)
        {
            (*hit_0) = _S24 + direction_0 * vec3<f32>(hitDistance_1);
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

fn MoverPages_0( headers_0 : i32,  index_0 : i32,  world_3 : vec3<f32>,  bias_0 : f32) -> vec2<i32>
{
    var at_0 : i32 = headers_0 + index_0 * i32(6);
    var reach_0 : f32 = (bitcast<f32>((uCellPages_0[at_0 + i32(3)]))) + bias_0;
    var toCentre_0 : vec3<f32> = world_3 - vec3<f32>((bitcast<f32>((uCellPages_0[at_0]))), (bitcast<f32>((uCellPages_0[at_0 + i32(1)]))), (bitcast<f32>((uCellPages_0[at_0 + i32(2)]))));
    var _S28 : vec2<i32>;
    if((dot(toCentre_0, toCentre_0)) > (reach_0 * reach_0))
    {
        _S28 = vec2<i32>(i32(0), i32(0));
    }
    else
    {
        _S28 = vec2<i32>(uCellPages_0[at_0 + i32(4)], uCellPages_0[at_0 + i32(5)]);
    }
    return _S28;
}

fn ProjectOntoCard_0( page_0 : i32,  world_4 : vec3<f32>,  directionW_0 : vec3<f32>,  across_0 : ptr<function, vec2<f32>>,  halfSize_0 : ptr<function, vec2<f32>>,  behind_0 : ptr<function, f32>,  depth_4 : ptr<function, f32>,  facing_0 : ptr<function, vec3<f32>>,  directionLocal_0 : ptr<function, vec3<f32>>,  litRect_0 : ptr<function, vec4<f32>>,  cardPage_0 : ptr<function, vec4<f32>>)
{
    var _S29 : i32 = page_0 * i32(10);
    (*litRect_0) = uPages_0[_S29];
    (*cardPage_0) = uPages_0[_S29 + i32(1)];
    var centreAndHalfWidth_0 : vec4<f32> = uPages_0[_S29 + i32(2)];
    var rightAndHalfHeight_0 : vec4<f32> = uPages_0[_S29 + i32(3)];
    var upAndDepth_0 : vec4<f32> = uPages_0[_S29 + i32(4)];
    var direction_1 : vec4<f32> = uPages_0[_S29 + i32(5)];
    var row0_0 : vec4<f32> = uPages_0[_S29 + i32(6)];
    var row1_0 : vec4<f32> = uPages_0[_S29 + i32(7)];
    var row2_0 : vec4<f32> = uPages_0[_S29 + i32(8)];
    var delta_0 : vec3<f32> = world_4 - vec3<f32>(row0_0.w, row1_0.w, row2_0.w);
    var _S30 : vec3<f32> = row0_0.xyz;
    var _S31 : vec3<f32> = row1_0.xyz;
    var _S32 : vec3<f32> = row2_0.xyz;
    var local_1 : vec3<f32> = vec3<f32>(dot(_S30, delta_0), dot(_S31, delta_0), dot(_S32, delta_0));
    (*directionLocal_0) = vec3<f32>(dot(_S30, directionW_0), dot(_S31, directionW_0), dot(_S32, directionW_0));
    var relative_0 : vec3<f32> = local_1 - centreAndHalfWidth_0.xyz;
    var _S33 : vec2<f32> = max(vec2<f32>(centreAndHalfWidth_0.w, rightAndHalfHeight_0.w), vec2<f32>(9.99999997475242708e-07f, 9.99999997475242708e-07f));
    (*halfSize_0) = _S33;
    (*across_0) = vec2<f32>(dot(relative_0, rightAndHalfHeight_0.xyz), dot(relative_0, upAndDepth_0.xyz)) / _S33;
    var _S34 : vec3<f32> = direction_1.xyz;
    (*behind_0) = - dot(relative_0, _S34);
    (*depth_4) = upAndDepth_0.w;
    (*facing_0) = _S34;
    return;
}

fn LookupCardWithin_0( world_5 : vec3<f32>,  arrivingW_0 : vec3<f32>,  skin_0 : f32,  found_0 : ptr<function, vec2<f32>>,  litFound_0 : ptr<function, vec2<f32>>,  agreement_0 : ptr<function, f32>,  cardDepth_0 : ptr<function, f32>) -> bool
{
    (*cardDepth_0) = 0.0f;
    const _S35 : vec2<f32> = vec2<f32>(0.0f, 0.0f);
    (*found_0) = _S35;
    (*litFound_0) = _S35;
    (*agreement_0) = 1.0e+09f;
    if((Globals_0.uLookupBuckets_0) <= i32(0))
    {
        return false;
    }
    var _S36 : vec2<i32> = uCells_0[LookupBucket_0(vec3<i32>(floor((world_5 - Globals_0.uLookupMin_0) / vec3<f32>(Globals_0.uLookupCell_0))))];
    var albedoWidth_0 : u32;
    var albedoHeight_0 : u32;
    {var dim = textureDimensions((uAlbedo_0));((albedoWidth_0)) = dim.x;((albedoHeight_0)) = dim.y;};
    var _S37 : vec2<f32> = vec2<f32>(f32(albedoWidth_0), f32(albedoHeight_0));
    var _S38 : vec2<i32> = uCells_0[Globals_0.uLookupBuckets_0];
    var settled_0 : bool = false;
    var anyFound_0 : bool = false;
    var walk_0 : i32 = i32(0);
    for(;;)
    {
        var _S39 : bool;
        if(walk_0 <= (_S38.y))
        {
            _S39 = !settled_0;
        }
        else
        {
            _S39 = false;
        }
        if(_S39)
        {
        }
        else
        {
            break;
        }
        var _S40 : bool = walk_0 == i32(0);
        var _S41 : vec2<i32>;
        if(_S40)
        {
            _S41 = _S36;
        }
        else
        {
            _S41 = MoverPages_0(_S38.x, walk_0 - i32(1), world_5, 0.0f);
        }
        var settled_1 : bool = settled_0;
        var anyFound_1 : bool = anyFound_0;
        var slot_0 : i32 = i32(0);
        for(;;)
        {
            var _S42 : bool;
            if(slot_0 < (_S41.y))
            {
                _S42 = !settled_1;
            }
            else
            {
                _S42 = false;
            }
            if(_S42)
            {
            }
            else
            {
                break;
            }
            var page_1 : i32 = uCellPages_0[_S41.x + slot_0];
            var _S43 : i32 = uCellPages_0[_S41.x + slot_0] * i32(10);
            if(((uPages_0[_S43 + i32(5)].w) > 0.5f) == _S40)
            {
                slot_0 = slot_0 + i32(1);
                continue;
            }
            var bound_0 : vec4<f32> = uPages_0[_S43 + i32(9)];
            var toCentre_1 : vec3<f32> = world_5 - bound_0.xyz;
            var _S44 : f32 = bound_0.w;
            if((dot(toCentre_1, toCentre_1)) > (_S44 * _S44))
            {
                slot_0 = slot_0 + i32(1);
                continue;
            }
            var across_1 : vec2<f32>;
            var halfSize_1 : vec2<f32>;
            var behind_1 : f32;
            var depth_5 : f32;
            var facing_1 : vec3<f32>;
            var arrivingLocal_0 : vec3<f32>;
            var litRect_1 : vec4<f32>;
            var cardPage_1 : vec4<f32>;
            ProjectOntoCard_0(page_1, world_5, (vec3<f32>(0) - arrivingW_0), &(across_1), &(halfSize_1), &(behind_1), &(depth_5), &(facing_1), &(arrivingLocal_0), &(litRect_1), &(cardPage_1));
            var _S45 : bool;
            if((abs(across_1.x)) > 1.0f)
            {
                _S45 = true;
            }
            else
            {
                _S45 = (abs(across_1.y)) > 1.0f;
            }
            if(_S45)
            {
                slot_0 = slot_0 + i32(1);
                continue;
            }
            var _S46 : bool;
            if((dot(arrivingW_0, arrivingW_0)) > 0.5f)
            {
                _S46 = (dot(facing_1, arrivingLocal_0)) < -0.05000000074505806f;
            }
            else
            {
                _S46 = false;
            }
            if(_S46)
            {
                slot_0 = slot_0 + i32(1);
                continue;
            }
            var _S47 : bool;
            if(behind_1 < (- skin_0))
            {
                _S47 = true;
            }
            else
            {
                _S47 = behind_1 > (depth_5 + skin_0);
            }
            if(_S47)
            {
                slot_0 = slot_0 + i32(1);
                continue;
            }
            var _S48 : vec2<f32> = vec2<f32>(0.5f);
            var uv_4 : vec2<f32> = across_1 * _S48 + _S48;
            var texel_0 : vec2<f32> = (cardPage_1.xy + uv_4 * cardPage_1.zw) / _S37;
            if(((textureSampleLevel((uAlbedo_0), (uAlbedoSampler_0), (texel_0), (0.0f))).w) < 0.5f)
            {
                slot_0 = slot_0 + i32(1);
                continue;
            }
            var error_0 : f32 = abs((textureSampleLevel((uDepth_0), (uDepthSampler_0), (texel_0), (0.0f))).x * depth_5 - behind_1);
            var settled_2 : bool;
            var anyFound_2 : bool;
            if(error_0 < (*agreement_0))
            {
                (*agreement_0) = error_0;
                (*cardDepth_0) = depth_5;
                (*found_0) = texel_0;
                var _S49 : bool = error_0 < 0.01999999955296516f;
                (*litFound_0) = (litRect_1.xy + uv_4 * litRect_1.zw) / Globals_0.uAtlasSize_0.xy;
                settled_2 = _S49;
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
        var _S50 : i32 = walk_0 + i32(1);
        settled_0 = settled_1;
        anyFound_0 = anyFound_1;
        walk_0 = _S50;
    }
    return anyFound_0;
}

fn LookupCardHit_0( world_6 : vec3<f32>,  rayDirection_0 : vec3<f32>,  found_1 : ptr<function, vec2<f32>>,  litFound_1 : ptr<function, vec2<f32>>,  agreement_1 : ptr<function, f32>) -> bool
{
    var depth_6 : f32;
    var _S51 : bool = LookupCardWithin_0(world_6, rayDirection_0, 0.25f, &((*found_1)), &((*litFound_1)), &((*agreement_1)), &(depth_6));
    return _S51;
}

fn SkyRadiance_0( direction_2 : vec3<f32>) -> vec3<f32>
{
    if((Globals_0.uSkyShValid_0) == i32(0))
    {
        return Globals_0.uSkyColour_0;
    }
    var n_0 : vec4<f32> = vec4<f32>(direction_2, 1.0f);
    var q_0 : vec4<f32> = n_0.xyzz * n_0.yzzx;
    var _S52 : f32 = direction_2.x;
    var _S53 : f32 = direction_2.y;
    return max(vec3<f32>(0.0f, 0.0f, 0.0f), vec3<f32>(dot(Globals_0.uSkySh_0[i32(0)], n_0), dot(Globals_0.uSkySh_0[i32(1)], n_0), dot(Globals_0.uSkySh_0[i32(2)], n_0)) + vec3<f32>(dot(Globals_0.uSkySh_0[i32(3)], q_0), dot(Globals_0.uSkySh_0[i32(4)], q_0), dot(Globals_0.uSkySh_0[i32(5)], q_0)) + Globals_0.uSkySh_0[i32(6)].xyz * vec3<f32>((_S52 * _S52 - _S53 * _S53)));
}

fn GatherIndirect_0( world_7 : vec3<f32>,  normal_3 : vec3<f32>,  viewOrigin_1 : vec3<f32>,  jitter_0 : vec2<f32>) -> vec3<f32>
{
    var resolved_3 : bool;
    if((Globals_0.uShadowLevels_0) <= i32(0))
    {
        resolved_3 = true;
    }
    else
    {
        resolved_3 = (Globals_0.uLookupBuckets_0) <= i32(0);
    }
    if(resolved_3)
    {
        return vec3<f32>(0.0f, 0.0f, 0.0f);
    }
    var _S54 : HemisphereBasis_0 = BasisAround_0(normal_3);
    var _S55 : vec3<f32> = ToView_0(normal_3);
    const _S56 : vec3<f32> = vec3<f32>(0.0f, 0.0f, 0.0f);
    var ray_0 : i32 = i32(0);
    var irradiance_0 : vec3<f32> = _S56;
    for(;;)
    {
        if(ray_0 < i32(16))
        {
        }
        else
        {
            break;
        }
        var probeUv_0 : vec2<f32> = (vec2<f32>(f32(ray_0 % i32(4)), f32(ray_0 / i32(4))) + jitter_0) / vec2<f32>(4.0f);
        var phi_0 : f32 = 6.28318548202514648f * probeUv_0.x;
        var cosTheta_0 : f32 = sqrt(probeUv_0.y);
        var sinTheta_0 : f32 = sqrt(max(0.0f, 1.0f - cosTheta_0 * cosTheta_0));
        var direction_3 : vec3<f32> = Orient_0(_S54, vec3<f32>(sinTheta_0 * cos(phi_0), sinTheta_0 * sin(phi_0), cosTheta_0));
        var source_0 : i32;
        var radiance_2 : vec3<f32>;
        if((Globals_0.uScreenTraceSteps_0) > i32(0))
        {
            var screenRadiance_0 : vec3<f32>;
            var _S57 : bool = ScreenTraceRadiance_0(viewOrigin_1, _S55, ToView_0(direction_3), &(screenRadiance_0));
            if(_S57)
            {
                source_0 = i32(1);
                radiance_2 = screenRadiance_0;
            }
            else
            {
                source_0 = i32(0);
                radiance_2 = _S56;
            }
            resolved_3 = _S57;
        }
        else
        {
            resolved_3 = false;
            source_0 = i32(0);
            radiance_2 = _S56;
        }
        var source_1 : i32;
        var radiance_3 : vec3<f32>;
        if(!resolved_3)
        {
            var hit_1 : vec3<f32>;
            var _S58 : f32 = VisibilityAt_0(world_7, normal_3, direction_3, &(hit_1));
            if(_S58 <= 0.5f)
            {
                var found_2 : vec2<f32>;
                var litFound_2 : vec2<f32>;
                var agreement_2 : f32;
                var _S59 : bool = LookupCardHit_0(hit_1, direction_3, &(found_2), &(litFound_2), &(agreement_2));
                if(_S59)
                {
                    var finest_0 : f32 = Globals_0.uClipmapOrigin_0[i32(0)].w;
                    var _S60 : vec3<f32> = (textureSampleLevel((uFinal_0), (uFinalSampler_0), (litFound_2), (0.0f))).xyz * vec3<f32>((smoothstep(1.5f * finest_0, 2.0f * finest_0, length(hit_1 - world_7)) * Globals_0.uBounceScale_0));
                    source_1 = i32(2);
                    radiance_3 = _S60;
                }
                else
                {
                    source_1 = source_0;
                    radiance_3 = radiance_2;
                }
            }
            else
            {
                var _S61 : vec3<f32> = SkyRadiance_0(direction_3);
                source_1 = i32(3);
                radiance_3 = _S61;
            }
        }
        else
        {
            source_1 = source_0;
            radiance_3 = radiance_2;
        }
        var _S62 : bool;
        if((Globals_0.uDebugSource_0) != i32(0))
        {
            _S62 = source_1 != (Globals_0.uDebugSource_0);
        }
        else
        {
            _S62 = false;
        }
        var radiance_4 : vec3<f32>;
        if(_S62)
        {
            radiance_4 = _S56;
        }
        else
        {
            radiance_4 = radiance_3;
        }
        var irradiance_1 : vec3<f32> = irradiance_0 + CapRayIntensity_0(radiance_4);
        ray_0 = ray_0 + i32(1);
        irradiance_0 = irradiance_1;
    }
    return max(irradiance_0 * vec3<f32>(0.19634954631328583f), _S56);
}

@compute
@workgroup_size(8, 8, 1)
fn computeMain(@builtin(global_invocation_id) id_0 : vec3<u32>)
{
    var _S63 : vec2<i32> = vec2<i32>(id_0.xy);
    var grid_0 : vec2<i32> = (vec2<i32>(Globals_0.uScreenWidth_0, Globals_0.uScreenHeight_0) + vec2<i32>((Globals_0.uProbeStep_0 - i32(1)))) / vec2<i32>(Globals_0.uProbeStep_0);
    var _S64 : bool;
    if((_S63.x) >= (grid_0.x))
    {
        _S64 = true;
    }
    else
    {
        _S64 = (_S63.y) >= (grid_0.y);
    }
    if(_S64)
    {
        return;
    }
    var uv_5 : vec2<f32> = (vec2<f32>(min(_S63 * vec2<i32>(Globals_0.uProbeStep_0) + vec2<i32>((Globals_0.uProbeStep_0 / i32(2))), vec2<i32>(Globals_0.uScreenWidth_0, Globals_0.uScreenHeight_0) - vec2<i32>(i32(1)))) + vec2<f32>(0.5f)) / vec2<f32>(f32(Globals_0.uScreenWidth_0), f32(Globals_0.uScreenHeight_0));
    var depth_7 : f32 = DepthBilinear_0(uSceneDepth_0, uSceneDepthSampler_0, uv_5);
    if(IsSkyDepth_0(depth_7))
    {
        textureStore((uIndirect_0), (vec2<u32>(_S63)), (vec4<f32>(0.0f, 0.0f, 0.0f, 0.0f)));
        return;
    }
    var viewPosition_1 : vec3<f32> = ViewPositionAt_0(uv_5, depth_7);
    textureStore((uIndirect_0), (vec2<u32>(_S63)), (vec4<f32>(GatherIndirect_0(Globals_0.uCameraToCache_0 + Globals_0.uViewToWorld0_0 * vec3<f32>(viewPosition_1.x) + Globals_0.uViewToWorld1_0 * vec3<f32>(viewPosition_1.y) + Globals_0.uViewToWorld2_0 * vec3<f32>(viewPosition_1.z), normalize((textureSampleLevel((uSceneNormal_0), (uSceneNormalSampler_0), (uv_5), (0.0f))).xyz * vec3<f32>(2.0f) - vec3<f32>(1.0f)), viewPosition_1, vec2<f32>(0.5f, 0.5f)), 1.0f)));
    return;
}

