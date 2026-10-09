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
    @align(16) uGroundSun_0 : vec3<f32>,
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
    @align(8) uFrame_0 : i32,
    @align(16) uColourWorldToView0_0 : vec3<f32>,
    @align(16) uColourWorldToView1_0 : vec3<f32>,
    @align(16) uColourWorldToView2_0 : vec3<f32>,
    @align(16) uColourCameraToCache_0 : vec3<f32>,
    @align(16) uTerrainMap0_0 : vec4<f32>,
    @align(16) uTerrainMap1_0 : vec4<f32>,
    @align(16) uShelterMap0_0 : vec4<f32>,
    @align(16) uShelterMap1_0 : vec4<f32>,
    @align(16) uShelterMap2_0 : vec4<f32>,
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

@binding(46) @group(0) var uTerrainHeights_0 : texture_2d<f32>;

@binding(19) @group(0) var<storage, read_write> uCells_0 : array<vec2<i32>>;

@binding(33) @group(0) var uAlbedo_0 : texture_2d<f32>;

@binding(20) @group(0) var<storage, read_write> uCellPages_0 : array<i32>;

@binding(16) @group(0) var<storage, read> uPages_0 : array<vec4<f32>>;

@binding(65) @group(0) var uAlbedoSampler_0 : sampler;

@binding(36) @group(0) var uDepth_0 : texture_2d<f32>;

@binding(68) @group(0) var uDepthSampler_0 : sampler;

@binding(38) @group(0) var uFinal_0 : texture_2d<f32>;

@binding(70) @group(0) var uFinalSampler_0 : sampler;

@binding(47) @group(0) var uRoofs_0 : texture_2d<f32>;

fn rsqrt_0( x_0 : f32) -> f32
{
    return 1.0f / sqrt(x_0);
}

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
        var _S24 : f32 = Globals_0.uClipmapOrigin_0[level_1].w;
        var voxel_1 : vec3<f32> = (world_2 - Globals_0.uClipmapOrigin_0[level_1].xyz) / vec3<f32>(_S24);
        var _S25 : bool;
        if((any((voxel_1 < vec3<f32>(0.5f, 0.5f, 0.5f)))))
        {
            _S25 = true;
        }
        else
        {
            _S25 = (any((voxel_1 > vec3<f32>(f32(Globals_0.uShadowResolution_0) - 0.5f, f32(Globals_0.uShadowResolution_0) - 0.5f, f32(Globals_0.uShadowResolution_0) - 0.5f))));
        }
        if(_S25)
        {
            level_1 = level_1 + i32(1);
            continue;
        }
        var uvw_1 : vec3<f32> = vec3<f32>(voxel_1.x / f32(Globals_0.uShadowResolution_0), voxel_1.y / f32(Globals_0.uShadowResolution_0), (f32(level_1) * f32(Globals_0.uShadowResolution_0) + voxel_1.z) / f32(Globals_0.uShadowResolution_0 * Globals_0.uShadowLevels_0));
        (*known_1) = true;
        (*raw_1) = (textureSampleLevel((uClipmaps_0), (uClipmapsSampler_0), (uvw_1), (0.0f))).x;
        (*resolved_1) = _S24;
        return ((*raw_1) * 2.0f - 1.0f) * Globals_0.uClipmapRange_0.data_0[level_1].x;
    }
    return 1.0e+06f;
}

fn FieldStepHits_0( field_0 : f32,  travelled_0 : f32,  resolved_2 : f32,  hitDistance_0 : ptr<function, f32>) -> bool
{
    var radius_0 : f32 = 0.5f * resolved_2 * saturate(travelled_0 / max(2.0f * resolved_2, 9.99999997475242708e-07f));
    (*hitDistance_0) = max(travelled_0 + field_0 - radius_0, 0.0f);
    return field_0 < radius_0;
}

fn VisibilityAt_0( world_3 : vec3<f32>,  normal_2 : vec3<f32>,  direction_0 : vec3<f32>,  hit_0 : ptr<function, vec3<f32>>) -> f32
{
    (*hit_0) = world_3;
    if((Globals_0.uShadowLevels_0) <= i32(0))
    {
        return 1.0f;
    }
    var _S26 : vec3<f32> = world_3 + normal_2 * vec3<f32>(Globals_0.uClipmapOrigin_0[i32(0)].w) * vec3<f32>(1.5f);
    var _S27 : f32 = Globals_0.uClipmapOrigin_0[i32(0)].w * f32(Globals_0.uShadowResolution_0) * f32((i32(1) << (u32(Globals_0.uShadowLevels_0 - i32(1)))));
    var step_0 : i32 = i32(0);
    var travelled_1 : f32 = 0.0f;
    for(;;)
    {
        var _S28 : bool;
        if(step_0 < i32(64))
        {
            _S28 = travelled_1 < _S27;
        }
        else
        {
            _S28 = false;
        }
        if(_S28)
        {
        }
        else
        {
            break;
        }
        var known_2 : bool;
        var raw_2 : f32;
        var resolved_3 : f32;
        var distance_0 : f32 = DistanceAt_1(_S26 + direction_0 * vec3<f32>(travelled_1), &(known_2), &(raw_2), &(resolved_3));
        if(!known_2)
        {
            return 1.0f;
        }
        var hitDistance_1 : f32;
        var _S29 : bool = FieldStepHits_0(distance_0, travelled_1, resolved_3, &(hitDistance_1));
        if(_S29)
        {
            (*hit_0) = _S26 + direction_0 * vec3<f32>(hitDistance_1);
            return 0.0f;
        }
        var travelled_2 : f32 = travelled_1 + max(distance_0, resolved_3 * 0.25f);
        step_0 = step_0 + i32(1);
        travelled_1 = travelled_2;
    }
    return 1.0f;
}

struct ShelterMap_0
{
     origin_1 : vec2<f32>,
     invCell_0 : f32,
     lowestZ_0 : f32,
     scale_0 : f32,
     headroom_0 : f32,
     reach_0 : f32,
     size_0 : vec2<f32>,
     present_0 : f32,
};

fn TerrainMap_0() -> ShelterMap_0
{
    var m_0 : ShelterMap_0;
    m_0.origin_1 = Globals_0.uTerrainMap0_0.xy;
    m_0.invCell_0 = Globals_0.uTerrainMap0_0.z;
    m_0.present_0 = Globals_0.uTerrainMap0_0.w;
    m_0.size_0 = Globals_0.uTerrainMap1_0.xy;
    m_0.lowestZ_0 = Globals_0.uTerrainMap1_0.z;
    m_0.scale_0 = Globals_0.uTerrainMap1_0.w;
    m_0.headroom_0 = 0.0f;
    m_0.reach_0 = 0.0f;
    return m_0;
}

fn ShelterTexel_0( m_1 : ShelterMap_0,  worldXY_0 : vec2<f32>) -> vec2<i32>
{
    return vec2<i32>(floor((worldXY_0 - m_1.origin_1) * vec2<f32>(m_1.invCell_0)));
}

fn ShelterHolds_0( m_2 : ShelterMap_0,  texel_0 : vec2<i32>) -> bool
{
    var _S30 : bool;
    if((m_2.present_0) > 0.5f)
    {
        _S30 = (texel_0.x) >= i32(0);
    }
    else
    {
        _S30 = false;
    }
    if(_S30)
    {
        _S30 = (texel_0.y) >= i32(0);
    }
    else
    {
        _S30 = false;
    }
    if(_S30)
    {
        _S30 = (texel_0.x) < i32(m_2.size_0.x);
    }
    else
    {
        _S30 = false;
    }
    if(_S30)
    {
        _S30 = (texel_0.y) < i32(m_2.size_0.y);
    }
    else
    {
        _S30 = false;
    }
    return _S30;
}

fn ShelterSurface_0( m_3 : ShelterMap_0,  texel_1 : vec4<f32>) -> f32
{
    var packed_0 : f32 = floor(texel_1.z * 255.0f + 0.5f) + floor(texel_1.y * 255.0f + 0.5f) * 256.0f;
    var _S31 : f32;
    if(packed_0 < 0.5f)
    {
        _S31 = -1.00000001504746622e+30f;
    }
    else
    {
        _S31 = m_3.lowestZ_0 + (packed_0 - 1.0f) * m_3.scale_0;
    }
    return _S31;
}

fn TerrainHeightAt_0( m_4 : ShelterMap_0,  worldXY_1 : vec2<f32>) -> f32
{
    var at_0 : vec2<i32> = ShelterTexel_0(m_4, worldXY_1);
    var _S32 : f32;
    if(ShelterHolds_0(m_4, at_0))
    {
        var _S33 : vec3<i32> = vec3<i32>(at_0, i32(0));
        _S32 = ShelterSurface_0(m_4, (textureLoad((uTerrainHeights_0), ((_S33)).xy, ((_S33)).z)));
    }
    else
    {
        _S32 = -1.00000001504746622e+30f;
    }
    return _S32;
}

fn TerrainHitDistance_0( absoluteOrigin_0 : vec3<f32>,  direction_1 : vec3<f32>,  reach_1 : f32) -> f32
{
    var m_5 : ShelterMap_0 = TerrainMap_0();
    var _S34 : bool;
    if((m_5.present_0) < 0.5f)
    {
        _S34 = true;
    }
    else
    {
        _S34 = (direction_1.z) > -0.00999999977648258f;
    }
    if(_S34)
    {
        _S34 = true;
    }
    else
    {
        _S34 = (m_5.invCell_0) <= 0.0f;
    }
    if(_S34)
    {
        return -1.0f;
    }
    var _S35 : f32 = 0.5f / m_5.invCell_0;
    var previous_1 : f32 = 0.0f;
    var i_1 : i32 = i32(1);
    for(;;)
    {
        if(i_1 <= i32(64))
        {
        }
        else
        {
            break;
        }
        var _S36 : f32 = min(f32(i_1) * _S35, reach_1);
        var at_1 : vec3<f32> = absoluteOrigin_0 + direction_1 * vec3<f32>(_S36);
        if((at_1.z) <= (TerrainHeightAt_0(m_5, at_1.xy)))
        {
            var low_0 : f32 = previous_1;
            var high_1 : f32 = _S36;
            var refine_1 : i32 = i32(0);
            for(;;)
            {
                if(refine_1 < i32(4))
                {
                }
                else
                {
                    break;
                }
                var middle_0 : f32 = (low_0 + high_1) * 0.5f;
                var probe_0 : vec3<f32> = absoluteOrigin_0 + direction_1 * vec3<f32>(middle_0);
                if((probe_0.z) <= (TerrainHeightAt_0(m_5, probe_0.xy)))
                {
                    high_1 = middle_0;
                }
                else
                {
                    low_0 = middle_0;
                }
                refine_1 = refine_1 + i32(1);
            }
            return high_1;
        }
        if(_S36 >= reach_1)
        {
            break;
        }
        var _S37 : i32 = i_1 + i32(1);
        previous_1 = _S36;
        i_1 = _S37;
    }
    return -1.0f;
}

fn LookupBucket_0( cell_0 : vec3<i32>) -> i32
{
    return i32(((((((u32(cell_0.x) * u32(2376512323)) ^ ((u32(cell_0.y) * u32(3625334849))))) ^ ((u32(cell_0.z) * u32(3407524639))))) & (u32(Globals_0.uLookupBuckets_0 - i32(1)))));
}

fn MoverPages_0( headers_0 : i32,  index_0 : i32,  world_4 : vec3<f32>,  bias_0 : f32) -> vec2<i32>
{
    var at_2 : i32 = headers_0 + index_0 * i32(6);
    var reach_2 : f32 = (bitcast<f32>((uCellPages_0[at_2 + i32(3)]))) + bias_0;
    var toCentre_0 : vec3<f32> = world_4 - vec3<f32>((bitcast<f32>((uCellPages_0[at_2]))), (bitcast<f32>((uCellPages_0[at_2 + i32(1)]))), (bitcast<f32>((uCellPages_0[at_2 + i32(2)]))));
    var _S38 : vec2<i32>;
    if((dot(toCentre_0, toCentre_0)) > (reach_2 * reach_2))
    {
        _S38 = vec2<i32>(i32(0), i32(0));
    }
    else
    {
        _S38 = vec2<i32>(uCellPages_0[at_2 + i32(4)], uCellPages_0[at_2 + i32(5)]);
    }
    return _S38;
}

fn ProjectOntoCard_0( page_0 : i32,  world_5 : vec3<f32>,  directionW_0 : vec3<f32>,  across_0 : ptr<function, vec2<f32>>,  halfSize_0 : ptr<function, vec2<f32>>,  behind_0 : ptr<function, f32>,  depth_4 : ptr<function, f32>,  facing_0 : ptr<function, vec3<f32>>,  directionLocal_0 : ptr<function, vec3<f32>>,  litRect_0 : ptr<function, vec4<f32>>,  cardPage_0 : ptr<function, vec4<f32>>)
{
    var _S39 : i32 = page_0 * i32(10);
    (*litRect_0) = uPages_0[_S39];
    (*cardPage_0) = uPages_0[_S39 + i32(1)];
    var centreAndHalfWidth_0 : vec4<f32> = uPages_0[_S39 + i32(2)];
    var rightAndHalfHeight_0 : vec4<f32> = uPages_0[_S39 + i32(3)];
    var upAndDepth_0 : vec4<f32> = uPages_0[_S39 + i32(4)];
    var direction_2 : vec4<f32> = uPages_0[_S39 + i32(5)];
    var row0_0 : vec4<f32> = uPages_0[_S39 + i32(6)];
    var row1_0 : vec4<f32> = uPages_0[_S39 + i32(7)];
    var row2_0 : vec4<f32> = uPages_0[_S39 + i32(8)];
    var delta_0 : vec3<f32> = world_5 - vec3<f32>(row0_0.w, row1_0.w, row2_0.w);
    var _S40 : vec3<f32> = row0_0.xyz;
    var _S41 : vec3<f32> = row1_0.xyz;
    var _S42 : vec3<f32> = row2_0.xyz;
    var local_1 : vec3<f32> = vec3<f32>(dot(_S40, delta_0), dot(_S41, delta_0), dot(_S42, delta_0));
    (*directionLocal_0) = vec3<f32>(dot(_S40, directionW_0), dot(_S41, directionW_0), dot(_S42, directionW_0));
    var relative_0 : vec3<f32> = local_1 - centreAndHalfWidth_0.xyz;
    var _S43 : vec2<f32> = max(vec2<f32>(centreAndHalfWidth_0.w, rightAndHalfHeight_0.w), vec2<f32>(9.99999997475242708e-07f, 9.99999997475242708e-07f));
    (*halfSize_0) = _S43;
    (*across_0) = vec2<f32>(dot(relative_0, rightAndHalfHeight_0.xyz), dot(relative_0, upAndDepth_0.xyz)) / _S43;
    var _S44 : vec3<f32> = direction_2.xyz;
    (*behind_0) = - dot(relative_0, _S44);
    (*depth_4) = upAndDepth_0.w;
    (*facing_0) = _S44;
    return;
}

fn SampleCardsAndAlbedoWithin_0( world_6 : vec3<f32>,  normalW_0 : vec3<f32>,  bias_1 : f32,  radiance_2 : ptr<function, vec3<f32>>,  albedo_0 : ptr<function, vec3<f32>>) -> bool
{
    const _S45 : vec3<f32> = vec3<f32>(0.0f, 0.0f, 0.0f);
    (*radiance_2) = _S45;
    (*albedo_0) = _S45;
    if((Globals_0.uLookupBuckets_0) <= i32(0))
    {
        return false;
    }
    var _S46 : vec2<i32> = uCells_0[LookupBucket_0(vec3<i32>(floor((world_6 - Globals_0.uLookupMin_0) / vec3<f32>(Globals_0.uLookupCell_0))))];
    var albedoWidth_0 : u32;
    var albedoHeight_0 : u32;
    {var dim = textureDimensions((uAlbedo_0));((albedoWidth_0)) = dim.x;((albedoHeight_0)) = dim.y;};
    var _S47 : vec2<f32> = vec2<f32>(f32(albedoWidth_0), f32(albedoHeight_0));
    var _S48 : vec2<i32> = uCells_0[Globals_0.uLookupBuckets_0];
    var walk_0 : i32 = i32(0);
    var weightSum_0 : f32 = 0.0f;
    for(;;)
    {
        if(walk_0 <= (_S48.y))
        {
        }
        else
        {
            break;
        }
        var _S49 : bool = walk_0 == i32(0);
        var _S50 : vec2<i32>;
        if(_S49)
        {
            _S50 = _S46;
        }
        else
        {
            _S50 = MoverPages_0(_S48.x, walk_0 - i32(1), world_6, bias_1);
        }
        var slot_0 : i32 = i32(0);
        var weightSum_1 : f32 = weightSum_0;
        for(;;)
        {
            if(slot_0 < (_S50.y))
            {
            }
            else
            {
                break;
            }
            var page_1 : i32 = uCellPages_0[_S50.x + slot_0];
            var _S51 : i32 = uCellPages_0[_S50.x + slot_0] * i32(10);
            if(((uPages_0[_S51 + i32(5)].w) > 0.5f) == _S49)
            {
                slot_0 = slot_0 + i32(1);
                continue;
            }
            var bound_0 : vec4<f32> = uPages_0[_S51 + i32(9)];
            var toCentre_1 : vec3<f32> = world_6 - bound_0.xyz;
            var reach_3 : f32 = bound_0.w + bias_1;
            if((dot(toCentre_1, toCentre_1)) > (reach_3 * reach_3))
            {
                slot_0 = slot_0 + i32(1);
                continue;
            }
            var across_1 : vec2<f32>;
            var halfSize_1 : vec2<f32>;
            var behind_1 : f32;
            var depth_5 : f32;
            var facing_1 : vec3<f32>;
            var normalLocal_0 : vec3<f32>;
            var litRect_1 : vec4<f32>;
            var cardPage_1 : vec4<f32>;
            ProjectOntoCard_0(page_1, world_6, normalW_0, &(across_1), &(halfSize_1), &(behind_1), &(depth_5), &(facing_1), &(normalLocal_0), &(litRect_1), &(cardPage_1));
            var towards_0 : f32 = dot(facing_1, normalLocal_0);
            if(towards_0 <= 0.0f)
            {
                slot_0 = slot_0 + i32(1);
                continue;
            }
            var squareness_0 : f32 = towards_0 * towards_0;
            var slack_0 : f32 = 0.5f * bias_1;
            var acrossSlack_0 : vec2<f32> = vec2<f32>(slack_0) / halfSize_1;
            var _S52 : bool;
            if((abs(across_1.x)) > (1.0f + acrossSlack_0.x))
            {
                _S52 = true;
            }
            else
            {
                _S52 = (abs(across_1.y)) > (1.0f + acrossSlack_0.y);
            }
            if(_S52)
            {
                slot_0 = slot_0 + i32(1);
                continue;
            }
            var _S53 : bool;
            if(behind_1 < (- slack_0))
            {
                _S53 = true;
            }
            else
            {
                _S53 = behind_1 > (depth_5 + slack_0);
            }
            if(_S53)
            {
                slot_0 = slot_0 + i32(1);
                continue;
            }
            var _S54 : vec2<f32> = vec2<f32>(0.5f);
            var atlasAt_0 : vec2<f32> = cardPage_1.xy + (clamp(across_1, vec2<f32>(-1.0f, -1.0f), vec2<f32>(1.0f, 1.0f)) * _S54 + _S54) * cardPage_1.zw - _S54;
            var lowCorner_0 : vec2<f32> = floor(atlasAt_0);
            var _S55 : vec2<f32> = atlasAt_0 - lowCorner_0;
            var _S56 : vec2<f32> = litRect_1.zw / max(cardPage_1.zw, vec2<f32>(9.99999997475242708e-07f, 9.99999997475242708e-07f));
            var _S57 : f32 = behind_1 / max(depth_5, 9.99999997475242708e-07f);
            var _S58 : f32 = bias_1 / max(0.5f * depth_5, 9.99999997475242708e-07f);
            var taps_0 : vec4<f32> = vec4<f32>(0.0f, 0.0f, 0.0f, 0.0f);
            var tapLight_0 : array<vec3<f32>, i32(4)>;
            var tapAlbedo_0 : array<vec3<f32>, i32(4)>;
            var tap_0 : i32 = i32(0);
            for(;;)
            {
                if(tap_0 < i32(4))
                {
                }
                else
                {
                    break;
                }
                var step_1 : vec2<f32> = vec2<f32>(f32((tap_0 & (i32(1)))), f32((tap_0 >> (u32(1)))));
                var _S59 : vec2<f32> = lowCorner_0 + step_1 + _S54;
                var centre_0 : vec2<f32> = _S59 / _S47;
                var photographed_0 : vec4<f32> = (textureSampleLevel((uAlbedo_0), (uAlbedoSampler_0), (centre_0), (0.0f)));
                var written_0 : f32;
                if((photographed_0.w) >= 0.5f)
                {
                    written_0 = 1.0f;
                }
                else
                {
                    written_0 = 0.0f;
                }
                tapAlbedo_0[tap_0] = photographed_0.xyz;
                var bilinear2_0 : vec2<f32> = mix(vec2<f32>(1.0f) - _S55, _S55, step_1);
                taps_0[tap_0] = bilinear2_0.x * bilinear2_0.y * written_0 * (1.0f - saturate((abs(_S57 - (textureSampleLevel((uDepth_0), (uDepthSampler_0), (centre_0), (0.0f))).x) - _S58) / (0.25f * _S58)));
                tapLight_0[tap_0] = (textureSampleLevel((uFinal_0), (uFinalSampler_0), ((floor(litRect_1.xy + (_S59 - cardPage_1.xy) * _S56) + _S54) / Globals_0.uAtlasSize_0.xy), (0.0f))).xyz;
                tap_0 = tap_0 + i32(1);
            }
            var coverage_0 : f32 = taps_0.x + taps_0.y + taps_0.z + taps_0.w;
            var cardWeight_0 : f32 = squareness_0 * coverage_0;
            if(cardWeight_0 <= 0.0f)
            {
                slot_0 = slot_0 + i32(1);
                continue;
            }
            var _S60 : vec3<f32> = vec3<f32>(coverage_0);
            var _S61 : vec3<f32> = vec3<f32>(cardWeight_0);
            (*radiance_2) = (*radiance_2) + max((tapLight_0[i32(0)] * vec3<f32>(taps_0.x) + tapLight_0[i32(1)] * vec3<f32>(taps_0.y) + tapLight_0[i32(2)] * vec3<f32>(taps_0.z) + tapLight_0[i32(3)] * vec3<f32>(taps_0.w)) / _S60, _S45) * _S61;
            (*albedo_0) = (*albedo_0) + (tapAlbedo_0[i32(0)] * vec3<f32>(taps_0.x) + tapAlbedo_0[i32(1)] * vec3<f32>(taps_0.y) + tapAlbedo_0[i32(2)] * vec3<f32>(taps_0.z) + tapAlbedo_0[i32(3)] * vec3<f32>(taps_0.w)) / _S60 * _S61;
            weightSum_1 = weightSum_1 + cardWeight_0;
            slot_0 = slot_0 + i32(1);
        }
        walk_0 = walk_0 + i32(1);
        weightSum_0 = weightSum_1;
    }
    if(weightSum_0 <= 0.0f)
    {
        (*radiance_2) = _S45;
        return false;
    }
    (*radiance_2) = (*radiance_2) / vec3<f32>(weightSum_0);
    (*albedo_0) = (*albedo_0) / vec3<f32>(weightSum_0);
    return true;
}

fn SampleCardsWithin_0( world_7 : vec3<f32>,  normalW_1 : vec3<f32>,  bias_2 : f32,  radiance_3 : ptr<function, vec3<f32>>) -> bool
{
    var albedo_1 : vec3<f32>;
    var _S62 : bool = SampleCardsAndAlbedoWithin_0(world_7, normalW_1, bias_2, &((*radiance_3)), &(albedo_1));
    return _S62;
}

fn SurfaceNormalAt_0( hit_1 : vec3<f32>) -> vec3<f32>
{
    var known_3 : bool;
    var raw_3 : f32;
    var resolved_4 : f32;
    var _S63 : f32 = DistanceAt_0(hit_1, &(known_3), &(raw_3), &(resolved_4));
    if(!known_3)
    {
        return vec3<f32>(0.0f, 0.0f, 0.0f);
    }
    var h_0 : f32 = 0.5f * resolved_4;
    var _S64 : vec3<f32> = vec3<f32>(h_0, 0.0f, 0.0f);
    var k_0 : bool;
    var r_0 : f32;
    var v_0 : f32;
    var _S65 : f32 = DistanceAt_0(hit_1 + _S64, &(k_0), &(r_0), &(v_0));
    var _S66 : f32 = DistanceAt_0(hit_1 - _S64, &(k_0), &(r_0), &(v_0));
    var _S67 : f32 = _S65 - _S66;
    var _S68 : vec3<f32> = vec3<f32>(0.0f, h_0, 0.0f);
    var _S69 : f32 = DistanceAt_0(hit_1 + _S68, &(k_0), &(r_0), &(v_0));
    var _S70 : f32 = DistanceAt_0(hit_1 - _S68, &(k_0), &(r_0), &(v_0));
    var _S71 : f32 = _S69 - _S70;
    var _S72 : vec3<f32> = vec3<f32>(0.0f, 0.0f, h_0);
    var _S73 : f32 = DistanceAt_0(hit_1 + _S72, &(k_0), &(r_0), &(v_0));
    var _S74 : f32 = DistanceAt_0(hit_1 - _S72, &(k_0), &(r_0), &(v_0));
    var gradient_0 : vec3<f32> = vec3<f32>(_S67, _S71, _S73 - _S74);
    var length2_0 : f32 = dot(gradient_0, gradient_0);
    var _S75 : vec3<f32>;
    if(length2_0 < 1.00000001335143196e-10f)
    {
        _S75 = vec3<f32>(0.0f, 0.0f, 0.0f);
    }
    else
    {
        _S75 = gradient_0 * vec3<f32>(rsqrt_0(length2_0));
    }
    return _S75;
}

fn ShelterCovers_0( m_6 : ShelterMap_0,  surfaceZ_0 : f32,  worldZ_0 : f32) -> bool
{
    var _S76 : bool;
    if(surfaceZ_0 > (worldZ_0 + m_6.headroom_0))
    {
        _S76 = surfaceZ_0 < (worldZ_0 + m_6.reach_0);
    }
    else
    {
        _S76 = false;
    }
    return _S76;
}

fn UnderRoof_0( worldPoint_0 : vec3<f32>) -> bool
{
    var m_7 : ShelterMap_0;
    m_7.origin_1 = Globals_0.uShelterMap0_0.xy;
    m_7.invCell_0 = Globals_0.uShelterMap0_0.z;
    m_7.present_0 = Globals_0.uShelterMap0_0.w;
    m_7.size_0 = Globals_0.uShelterMap1_0.xy;
    m_7.lowestZ_0 = Globals_0.uShelterMap1_0.z;
    m_7.scale_0 = Globals_0.uShelterMap1_0.w;
    m_7.headroom_0 = Globals_0.uShelterMap2_0.x;
    m_7.reach_0 = Globals_0.uShelterMap2_0.y;
    var at_3 : vec2<i32> = ShelterTexel_0(m_7, worldPoint_0.xy);
    if(!ShelterHolds_0(m_7, at_3))
    {
        return false;
    }
    var _S77 : vec3<i32> = vec3<i32>(at_3, i32(0));
    return ShelterCovers_0(m_7, ShelterSurface_0(m_7, (textureLoad((uRoofs_0), ((_S77)).xy, ((_S77)).z))), worldPoint_0.z);
}

fn LookupCardWithin_0( world_8 : vec3<f32>,  arrivingW_0 : vec3<f32>,  skin_0 : f32,  found_0 : ptr<function, vec2<f32>>,  litFound_0 : ptr<function, vec2<f32>>,  agreement_0 : ptr<function, f32>,  cardDepth_0 : ptr<function, f32>) -> bool
{
    (*cardDepth_0) = 0.0f;
    const _S78 : vec2<f32> = vec2<f32>(0.0f, 0.0f);
    (*found_0) = _S78;
    (*litFound_0) = _S78;
    (*agreement_0) = 1.0e+09f;
    if((Globals_0.uLookupBuckets_0) <= i32(0))
    {
        return false;
    }
    var _S79 : vec2<i32> = uCells_0[LookupBucket_0(vec3<i32>(floor((world_8 - Globals_0.uLookupMin_0) / vec3<f32>(Globals_0.uLookupCell_0))))];
    var albedoWidth_1 : u32;
    var albedoHeight_1 : u32;
    {var dim = textureDimensions((uAlbedo_0));((albedoWidth_1)) = dim.x;((albedoHeight_1)) = dim.y;};
    var _S80 : vec2<f32> = vec2<f32>(f32(albedoWidth_1), f32(albedoHeight_1));
    var _S81 : vec2<i32> = uCells_0[Globals_0.uLookupBuckets_0];
    var settled_0 : bool = false;
    var anyFound_0 : bool = false;
    var walk_1 : i32 = i32(0);
    for(;;)
    {
        var _S82 : bool;
        if(walk_1 <= (_S81.y))
        {
            _S82 = !settled_0;
        }
        else
        {
            _S82 = false;
        }
        if(_S82)
        {
        }
        else
        {
            break;
        }
        var _S83 : bool = walk_1 == i32(0);
        var _S84 : vec2<i32>;
        if(_S83)
        {
            _S84 = _S79;
        }
        else
        {
            _S84 = MoverPages_0(_S81.x, walk_1 - i32(1), world_8, 0.0f);
        }
        var settled_1 : bool = settled_0;
        var anyFound_1 : bool = anyFound_0;
        var slot_1 : i32 = i32(0);
        for(;;)
        {
            var _S85 : bool;
            if(slot_1 < (_S84.y))
            {
                _S85 = !settled_1;
            }
            else
            {
                _S85 = false;
            }
            if(_S85)
            {
            }
            else
            {
                break;
            }
            var page_2 : i32 = uCellPages_0[_S84.x + slot_1];
            var _S86 : i32 = uCellPages_0[_S84.x + slot_1] * i32(10);
            if(((uPages_0[_S86 + i32(5)].w) > 0.5f) == _S83)
            {
                slot_1 = slot_1 + i32(1);
                continue;
            }
            var bound_1 : vec4<f32> = uPages_0[_S86 + i32(9)];
            var toCentre_2 : vec3<f32> = world_8 - bound_1.xyz;
            var _S87 : f32 = bound_1.w;
            if((dot(toCentre_2, toCentre_2)) > (_S87 * _S87))
            {
                slot_1 = slot_1 + i32(1);
                continue;
            }
            var across_2 : vec2<f32>;
            var halfSize_2 : vec2<f32>;
            var behind_2 : f32;
            var depth_6 : f32;
            var facing_2 : vec3<f32>;
            var arrivingLocal_0 : vec3<f32>;
            var litRect_2 : vec4<f32>;
            var cardPage_2 : vec4<f32>;
            ProjectOntoCard_0(page_2, world_8, (vec3<f32>(0) - arrivingW_0), &(across_2), &(halfSize_2), &(behind_2), &(depth_6), &(facing_2), &(arrivingLocal_0), &(litRect_2), &(cardPage_2));
            var _S88 : bool;
            if((abs(across_2.x)) > 1.0f)
            {
                _S88 = true;
            }
            else
            {
                _S88 = (abs(across_2.y)) > 1.0f;
            }
            if(_S88)
            {
                slot_1 = slot_1 + i32(1);
                continue;
            }
            var _S89 : bool;
            if((dot(arrivingW_0, arrivingW_0)) > 0.5f)
            {
                _S89 = (dot(facing_2, arrivingLocal_0)) < -0.05000000074505806f;
            }
            else
            {
                _S89 = false;
            }
            if(_S89)
            {
                slot_1 = slot_1 + i32(1);
                continue;
            }
            var _S90 : bool;
            if(behind_2 < (- skin_0))
            {
                _S90 = true;
            }
            else
            {
                _S90 = behind_2 > (depth_6 + skin_0);
            }
            if(_S90)
            {
                slot_1 = slot_1 + i32(1);
                continue;
            }
            var _S91 : vec2<f32> = vec2<f32>(0.5f);
            var uv_4 : vec2<f32> = across_2 * _S91 + _S91;
            var texel_2 : vec2<f32> = (cardPage_2.xy + uv_4 * cardPage_2.zw) / _S80;
            if(((textureSampleLevel((uAlbedo_0), (uAlbedoSampler_0), (texel_2), (0.0f))).w) < 0.5f)
            {
                slot_1 = slot_1 + i32(1);
                continue;
            }
            var error_0 : f32 = abs((textureSampleLevel((uDepth_0), (uDepthSampler_0), (texel_2), (0.0f))).x * depth_6 - behind_2);
            var settled_2 : bool;
            var anyFound_2 : bool;
            if(error_0 < (*agreement_0))
            {
                (*agreement_0) = error_0;
                (*cardDepth_0) = depth_6;
                (*found_0) = texel_2;
                var _S92 : bool = error_0 < 0.01999999955296516f;
                (*litFound_0) = (litRect_2.xy + uv_4 * litRect_2.zw) / Globals_0.uAtlasSize_0.xy;
                settled_2 = _S92;
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
        var _S93 : i32 = walk_1 + i32(1);
        settled_0 = settled_1;
        anyFound_0 = anyFound_1;
        walk_1 = _S93;
    }
    return anyFound_0;
}

fn LookupCardHit_0( world_9 : vec3<f32>,  rayDirection_0 : vec3<f32>,  found_1 : ptr<function, vec2<f32>>,  litFound_1 : ptr<function, vec2<f32>>,  agreement_1 : ptr<function, f32>) -> bool
{
    var depth_7 : f32;
    var _S94 : bool = LookupCardWithin_0(world_9, rayDirection_0, 0.25f, &((*found_1)), &((*litFound_1)), &((*agreement_1)), &(depth_7));
    return _S94;
}

fn SkyRadiance_0( direction_3 : vec3<f32>) -> vec3<f32>
{
    if((Globals_0.uSkyShValid_0) == i32(0))
    {
        return Globals_0.uSkyColour_0;
    }
    var n_0 : vec4<f32> = vec4<f32>(direction_3, 1.0f);
    var q_0 : vec4<f32> = n_0.xyzz * n_0.yzzx;
    var _S95 : f32 = direction_3.x;
    var _S96 : f32 = direction_3.y;
    return max(vec3<f32>(0.0f, 0.0f, 0.0f), vec3<f32>(dot(Globals_0.uSkySh_0[i32(0)], n_0), dot(Globals_0.uSkySh_0[i32(1)], n_0), dot(Globals_0.uSkySh_0[i32(2)], n_0)) + vec3<f32>(dot(Globals_0.uSkySh_0[i32(3)], q_0), dot(Globals_0.uSkySh_0[i32(4)], q_0), dot(Globals_0.uSkySh_0[i32(5)], q_0)) + Globals_0.uSkySh_0[i32(6)].xyz * vec3<f32>((_S95 * _S95 - _S96 * _S96)));
}

fn GatherIndirect_0( world_10 : vec3<f32>,  normal_3 : vec3<f32>,  viewOrigin_1 : vec3<f32>,  jitter_0 : vec2<f32>) -> vec3<f32>
{
    var radiance_4 : vec3<f32>;
    var resolved_5 : bool;
    if((Globals_0.uShadowLevels_0) <= i32(0))
    {
        resolved_5 = true;
    }
    else
    {
        resolved_5 = (Globals_0.uLookupBuckets_0) <= i32(0);
    }
    if(resolved_5)
    {
        return vec3<f32>(0.0f, 0.0f, 0.0f);
    }
    var _S97 : HemisphereBasis_0 = BasisAround_0(normal_3);
    var _S98 : vec3<f32> = ToView_0(normal_3);
    const _S99 : vec3<f32> = vec3<f32>(0.0f, 0.0f, 0.0f);
    var ray_0 : i32 = i32(0);
    var surfaceSum_0 : vec3<f32> = _S99;
    var surfaceCount_0 : f32 = 0.0f;
    var unplaced_0 : f32 = 0.0f;
    var openSum_0 : vec3<f32> = _S99;
    var openCount_0 : f32 = 0.0f;
    var irradiance_0 : vec3<f32> = _S99;
    var counted_0 : f32 = 0.0f;
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
        var direction_4 : vec3<f32> = Orient_0(_S97, vec3<f32>(sinTheta_0 * cos(phi_0), sinTheta_0 * sin(phi_0), cosTheta_0));
        var source_0 : i32;
        if((Globals_0.uScreenTraceSteps_0) > i32(0))
        {
            var screenRadiance_0 : vec3<f32>;
            var _S100 : bool = ScreenTraceRadiance_0(viewOrigin_1, _S98, ToView_0(direction_4), &(screenRadiance_0));
            if(_S100)
            {
                source_0 = i32(1);
                radiance_4 = screenRadiance_0;
            }
            else
            {
                source_0 = i32(0);
                radiance_4 = _S99;
            }
            resolved_5 = _S100;
        }
        else
        {
            resolved_5 = false;
            source_0 = i32(0);
            radiance_4 = _S99;
        }
        var _S101 : bool;
        var source_1 : i32;
        var radiance_5 : vec3<f32>;
        var openSum_1 : vec3<f32>;
        var radiance_6 : vec3<f32>;
        var surfaceSum_1 : vec3<f32>;
        var openCount_1 : f32;
        var weight_1 : f32;
        var surfaceCount_1 : f32;
        var unplaced_1 : f32;
        if(!resolved_5)
        {
            var hit_2 : vec3<f32>;
            var _S102 : f32 = VisibilityAt_0(world_10, normal_3, direction_4, &(hit_2));
            var fieldHit_0 : bool = _S102 <= 0.5f;
            var terrainFrom_0 : vec3<f32> = world_10 + normal_3 * vec3<f32>(0.10000000149011612f);
            var terrainT_0 : f32 = TerrainHitDistance_0(terrainFrom_0 + Globals_0.uTraceOrigin_0, direction_4, 32.0f);
            if(terrainT_0 >= 0.0f)
            {
                if(!fieldHit_0)
                {
                    _S101 = true;
                }
                else
                {
                    _S101 = (length(hit_2 - world_10)) > terrainT_0;
                }
            }
            else
            {
                _S101 = false;
            }
            var resolved_6 : bool;
            var fieldHit_1 : bool;
            var source_2 : i32;
            var surfaceSum_2 : vec3<f32>;
            var surfaceCount_2 : f32;
            if(_S101)
            {
                var terrainLight_0 : vec3<f32>;
                var _S103 : bool = SampleCardsWithin_0(terrainFrom_0 + direction_4 * vec3<f32>(terrainT_0), vec3<f32>(0.0f, 0.0f, 1.0f), 0.5f * Globals_0.uClipmapOrigin_0[i32(0)].w, &(terrainLight_0));
                if(_S103)
                {
                    var radiance_7 : vec3<f32> = terrainLight_0 * vec3<f32>(Globals_0.uBounceScale_0);
                    var surfaceSum_3 : vec3<f32> = surfaceSum_0 + CapRayIntensity_0(radiance_7);
                    var surfaceCount_3 : f32 = surfaceCount_0 + 1.0f;
                    resolved_6 = true;
                    source_2 = i32(2);
                    radiance_5 = radiance_7;
                    surfaceSum_2 = surfaceSum_3;
                    surfaceCount_2 = surfaceCount_3;
                }
                else
                {
                    resolved_6 = resolved_5;
                    source_2 = source_0;
                    radiance_5 = radiance_4;
                    surfaceSum_2 = surfaceSum_0;
                    surfaceCount_2 = surfaceCount_0;
                }
                fieldHit_1 = false;
            }
            else
            {
                resolved_6 = resolved_5;
                fieldHit_1 = fieldHit_0;
                source_2 = source_0;
                radiance_5 = radiance_4;
                surfaceSum_2 = surfaceSum_0;
                surfaceCount_2 = surfaceCount_0;
            }
            if(resolved_6)
            {
                source_1 = source_2;
                radiance_6 = radiance_5;
                weight_1 = 1.0f;
                surfaceSum_1 = surfaceSum_2;
                surfaceCount_1 = surfaceCount_2;
                unplaced_1 = unplaced_0;
                openSum_1 = openSum_0;
                openCount_1 = openCount_0;
            }
            else
            {
                if(fieldHit_1)
                {
                    var found_2 : vec2<f32>;
                    var litFound_2 : vec2<f32>;
                    var agreement_2 : f32;
                    var finest_0 : f32 = Globals_0.uClipmapOrigin_0[i32(0)].w;
                    var _S104 : f32 = 1.5f * finest_0;
                    var _S105 : f32 = smoothstep(_S104, 2.0f * finest_0, length(hit_2 - world_10)) * smoothstep(0.75f * finest_0, _S104, dot(hit_2 - world_10, normal_3));
                    var hitNormal_0 : vec3<f32> = SurfaceNormalAt_0(hit_2);
                    var _S106 : bool;
                    if(!UnderRoof_0(world_10 + Globals_0.uTraceOrigin_0))
                    {
                        _S106 = UnderRoof_0(hit_2 + Globals_0.uTraceOrigin_0);
                    }
                    else
                    {
                        _S106 = false;
                    }
                    var unplaced_2 : f32;
                    if(_S106)
                    {
                        var unplaced_3 : f32 = unplaced_0 + 1.0f;
                        openCount_1 = 0.0f;
                        unplaced_2 = unplaced_3;
                    }
                    else
                    {
                        openCount_1 = _S105;
                        unplaced_2 = unplaced_0;
                    }
                    if((dot(hitNormal_0, direction_4)) > 0.0f)
                    {
                        openSum_1 = (vec3<f32>(0) - hitNormal_0);
                    }
                    else
                    {
                        openSum_1 = hitNormal_0;
                    }
                    var filtered_0 : vec3<f32>;
                    if(openCount_1 <= 0.0f)
                    {
                        source_1 = source_2;
                        radiance_6 = radiance_5;
                        weight_1 = 0.0f;
                        surfaceSum_1 = surfaceSum_2;
                        surfaceCount_1 = surfaceCount_2;
                        unplaced_1 = unplaced_2;
                    }
                    else
                    {
                        var _S107 : bool;
                        if((dot(openSum_1, openSum_1)) > 0.5f)
                        {
                            var _S108 : bool = SampleCardsWithin_0(hit_2, openSum_1, 0.5f * finest_0, &(filtered_0));
                            _S107 = _S108;
                        }
                        else
                        {
                            _S107 = false;
                        }
                        if(_S107)
                        {
                            var radiance_8 : vec3<f32> = filtered_0 * vec3<f32>(Globals_0.uBounceScale_0);
                            var surfaceSum_4 : vec3<f32> = surfaceSum_2 + CapRayIntensity_0(radiance_8) * vec3<f32>(openCount_1);
                            var surfaceCount_4 : f32 = surfaceCount_2 + openCount_1;
                            source_1 = i32(2);
                            radiance_6 = radiance_8;
                            weight_1 = openCount_1;
                            surfaceSum_1 = surfaceSum_4;
                            surfaceCount_1 = surfaceCount_4;
                            unplaced_1 = unplaced_2;
                        }
                        else
                        {
                            var _S109 : bool = LookupCardHit_0(hit_2, direction_4, &(found_2), &(litFound_2), &(agreement_2));
                            if(_S109)
                            {
                                var radiance_9 : vec3<f32> = (textureSampleLevel((uFinal_0), (uFinalSampler_0), (litFound_2), (0.0f))).xyz * vec3<f32>(Globals_0.uBounceScale_0);
                                var surfaceSum_5 : vec3<f32> = surfaceSum_2 + CapRayIntensity_0(radiance_9) * vec3<f32>(openCount_1);
                                var surfaceCount_5 : f32 = surfaceCount_2 + openCount_1;
                                source_1 = i32(2);
                                radiance_6 = radiance_9;
                                weight_1 = openCount_1;
                                surfaceSum_1 = surfaceSum_5;
                                surfaceCount_1 = surfaceCount_5;
                                unplaced_1 = unplaced_2;
                            }
                            else
                            {
                                var unplaced_4 : f32 = unplaced_2 + openCount_1;
                                source_1 = source_2;
                                radiance_6 = radiance_5;
                                weight_1 = 0.0f;
                                surfaceSum_1 = surfaceSum_2;
                                surfaceCount_1 = surfaceCount_2;
                                unplaced_1 = unplaced_4;
                            }
                        }
                    }
                    openSum_1 = openSum_0;
                    openCount_1 = openCount_0;
                }
                else
                {
                    if((direction_4.z) >= 0.0f)
                    {
                        openSum_1 = SkyRadiance_0(direction_4);
                    }
                    else
                    {
                        openSum_1 = vec3<f32>(0.25f) * (SkyRadiance_0(vec3<f32>(0.0f, 0.0f, 1.0f)) + Globals_0.uGroundSun_0);
                    }
                    var openSum_2 : vec3<f32> = openSum_0 + CapRayIntensity_0(openSum_1);
                    var openCount_2 : f32 = openCount_0 + 1.0f;
                    source_1 = i32(3);
                    radiance_6 = openSum_1;
                    weight_1 = 1.0f;
                    surfaceSum_1 = surfaceSum_2;
                    surfaceCount_1 = surfaceCount_2;
                    unplaced_1 = unplaced_0;
                    openSum_1 = openSum_2;
                    openCount_1 = openCount_2;
                }
            }
        }
        else
        {
            source_1 = source_0;
            radiance_6 = radiance_4;
            weight_1 = 1.0f;
            surfaceSum_1 = surfaceSum_0;
            surfaceCount_1 = surfaceCount_0;
            unplaced_1 = unplaced_0;
            openSum_1 = openSum_0;
            openCount_1 = openCount_0;
        }
        if((Globals_0.uDebugSource_0) != i32(0))
        {
            _S101 = source_1 != (Globals_0.uDebugSource_0);
        }
        else
        {
            _S101 = false;
        }
        if(_S101)
        {
            radiance_5 = _S99;
        }
        else
        {
            radiance_5 = radiance_6;
        }
        var irradiance_1 : vec3<f32> = irradiance_0 + CapRayIntensity_0(radiance_5) * vec3<f32>(weight_1);
        var counted_1 : f32 = counted_0 + weight_1;
        ray_0 = ray_0 + i32(1);
        surfaceSum_0 = surfaceSum_1;
        surfaceCount_0 = surfaceCount_1;
        unplaced_0 = unplaced_1;
        openSum_0 = openSum_1;
        openCount_0 = openCount_1;
        irradiance_0 = irradiance_1;
        counted_0 = counted_1;
    }
    if(unplaced_0 > 0.0f)
    {
        if((Globals_0.uDebugSource_0) == i32(0))
        {
            resolved_5 = true;
        }
        else
        {
            resolved_5 = (Globals_0.uDebugSource_0) == i32(2);
        }
    }
    else
    {
        resolved_5 = false;
    }
    if(resolved_5)
    {
        if(surfaceCount_0 > 0.0f)
        {
            radiance_4 = surfaceSum_0 / vec3<f32>(surfaceCount_0);
        }
        else
        {
            if(openCount_0 > 0.0f)
            {
                surfaceSum_0 = openSum_0 / vec3<f32>(openCount_0);
            }
            else
            {
                surfaceSum_0 = SkyRadiance_0(vec3<f32>(0.0f, 0.0f, 1.0f));
            }
            radiance_4 = surfaceSum_0 * vec3<f32>(0.25f);
        }
        var irradiance_2 : vec3<f32> = irradiance_0 + radiance_4 * vec3<f32>(unplaced_0);
        counted_0 = counted_0 + unplaced_0;
        irradiance_0 = irradiance_2;
    }
    return max(irradiance_0 * vec3<f32>((3.14159274101257324f / max(counted_0, 4.0f))), _S99);
}

@compute
@workgroup_size(8, 8, 1)
fn computeMain(@builtin(global_invocation_id) id_0 : vec3<u32>)
{
    var _S110 : vec2<i32> = vec2<i32>(id_0.xy);
    var grid_0 : vec2<i32> = (vec2<i32>(Globals_0.uScreenWidth_0, Globals_0.uScreenHeight_0) + vec2<i32>((Globals_0.uProbeStep_0 - i32(1)))) / vec2<i32>(Globals_0.uProbeStep_0);
    var _S111 : bool;
    if((_S110.x) >= (grid_0.x))
    {
        _S111 = true;
    }
    else
    {
        _S111 = (_S110.y) >= (grid_0.y);
    }
    if(_S111)
    {
        return;
    }
    var uv_5 : vec2<f32> = (vec2<f32>(min(_S110 * vec2<i32>(Globals_0.uProbeStep_0) + vec2<i32>((Globals_0.uProbeStep_0 / i32(2))), vec2<i32>(Globals_0.uScreenWidth_0, Globals_0.uScreenHeight_0) - vec2<i32>(i32(1)))) + vec2<f32>(0.5f)) / vec2<f32>(f32(Globals_0.uScreenWidth_0), f32(Globals_0.uScreenHeight_0));
    var depth_8 : f32 = DepthBilinear_0(uSceneDepth_0, uSceneDepthSampler_0, uv_5);
    if(IsSkyDepth_0(depth_8))
    {
        textureStore((uIndirect_0), (vec2<u32>(_S110)), (vec4<f32>(0.0f, 0.0f, 0.0f, 0.0f)));
        return;
    }
    var viewPosition_1 : vec3<f32> = ViewPositionAt_0(uv_5, depth_8);
    var world_11 : vec3<f32> = Globals_0.uCameraToCache_0 + Globals_0.uViewToWorld0_0 * vec3<f32>(viewPosition_1.x) + Globals_0.uViewToWorld1_0 * vec3<f32>(viewPosition_1.y) + Globals_0.uViewToWorld2_0 * vec3<f32>(viewPosition_1.z);
    var worldNormal_0 : vec3<f32> = normalize((textureSampleLevel((uSceneNormal_0), (uSceneNormalSampler_0), (uv_5), (0.0f))).xyz * vec3<f32>(2.0f) - vec3<f32>(1.0f));
    var worldNormal_1 : vec3<f32>;
    if((dot(worldNormal_0, world_11 - Globals_0.uCameraToCache_0)) > 0.0f)
    {
        worldNormal_1 = (vec3<f32>(0) - worldNormal_0);
    }
    else
    {
        worldNormal_1 = worldNormal_0;
    }
    textureStore((uIndirect_0), (vec2<u32>(_S110)), (vec4<f32>(GatherIndirect_0(world_11, worldNormal_1, viewPosition_1, vec2<f32>(0.5f, 0.5f)), 1.0f)));
    return;
}

