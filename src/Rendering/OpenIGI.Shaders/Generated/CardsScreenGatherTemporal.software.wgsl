struct _Array_std140_float4_0
{
    @align(16) data_0 : array<vec4<f32>, i32(4)>,
};

struct SLANG_ParameterGroup_Globals_std140_0
{
    @align(16) uClipmapOrigin_0 : array<vec4<f32>, i32(4)>,
    @align(16) uSkySh_0 : array<vec4<f32>, i32(7)>,
    @align(16) uUnproject_0 : vec4<f32>,
    @align(16) uPrevUnproject_0 : vec4<f32>,
    @align(16) uViewToWorld0_0 : vec3<f32>,
    @align(16) uViewToWorld1_0 : vec3<f32>,
    @align(16) uViewToWorld2_0 : vec3<f32>,
    @align(16) uCameraToCache_0 : vec3<f32>,
    @align(16) uTraceOrigin_0 : vec3<f32>,
    @align(16) uPrevWorldToView0_0 : vec3<f32>,
    @align(16) uPrevWorldToView1_0 : vec3<f32>,
    @align(16) uPrevWorldToView2_0 : vec3<f32>,
    @align(16) uPrevCameraToCache_0 : vec3<f32>,
    @align(16) uColourWorldToView0_0 : vec3<f32>,
    @align(16) uColourWorldToView1_0 : vec3<f32>,
    @align(16) uColourWorldToView2_0 : vec3<f32>,
    @align(16) uColourCameraToCache_0 : vec3<f32>,
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
    @align(16) uTerrainMap0_0 : vec4<f32>,
    @align(16) uTerrainMap1_0 : vec4<f32>,
    @align(16) uShelterMap0_0 : vec4<f32>,
    @align(16) uShelterMap1_0 : vec4<f32>,
    @align(16) uShelterMap2_0 : vec4<f32>,
    @align(16) uHasHistory_0 : i32,
    @align(4) uFrameIndex_0 : i32,
    @align(8) uMaxAccumulated_0 : i32,
    @align(4) uHasVelocity_0 : i32,
    @align(16) uGatherPass_0 : i32,
};

@binding(49) @group(0) var<uniform> Globals_0 : SLANG_ParameterGroup_Globals_std140_0;
@binding(39) @group(0) var uSceneDepth_0 : texture_depth_2d;

@binding(71) @group(0) var uSceneDepthSampler_0 : sampler;

@binding(40) @group(0) var uSceneNormal_0 : texture_2d<f32>;

@binding(72) @group(0) var uSceneNormalSampler_0 : sampler;

@binding(42) @group(0) var uPositionHistory_0 : texture_2d<f32>;

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

@binding(0) @group(0) var uIndirect_0 : texture_storage_2d<rgba16float, write>;

@binding(1) @group(0) var uPosition_0 : texture_storage_2d<rgba16float, write>;

@binding(44) @group(0) var uRaw_0 : texture_2d<f32>;

@binding(45) @group(0) var uVelocity_0 : texture_2d<f32>;

@binding(77) @group(0) var uVelocitySampler_0 : sampler;

@binding(41) @group(0) var uHistory_0 : texture_2d<f32>;

fn rsqrt_0( x_0 : f32) -> f32
{
    return 1.0f / sqrt(x_0);
}

fn ProbeTileOffset_0( frame_0 : i32,  step_0 : i32) -> vec2<i32>
{
    var i_0 : u32 = (u32(frame_0) & (u32(7)));
    var _S1 : i32 = step_0 - i32(1);
    return min(vec2<i32>(floor((vec2<f32>(f32(i_0), f32((((((((i_0 & (u32(1)))) << (u32(2)))) | (((i_0 & (u32(2))))))) | (((((i_0 >> (u32(2)))) & (u32(1)))))))) + vec2<f32>(0.5f)) / vec2<f32>(8.0f) * vec2<f32>(f32(step_0)))), vec2<i32>(_S1, _S1));
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
    var _S2 : vec2<i32> = vec2<i32>(floored_0);
    const _S3 : vec2<i32> = vec2<i32>(i32(0), i32(0));
    var _S4 : vec3<i32> = vec3<i32>(clamp(_S2, _S3, last_0), i32(0));
    var _S5 : vec3<i32> = vec3<i32>(clamp(_S2 + vec2<i32>(i32(1), i32(0)), _S3, last_0), i32(0));
    var _S6 : vec3<i32> = vec3<i32>(clamp(_S2 + vec2<i32>(i32(0), i32(1)), _S3, last_0), i32(0));
    var _S7 : vec3<i32> = vec3<i32>(clamp(_S2 + vec2<i32>(i32(1), i32(1)), _S3, last_0), i32(0));
    var _S8 : f32 = weight_0.x;
    return mix(mix((textureLoad((depth_0), ((_S4)).xy, ((_S4)).z)), (textureLoad((depth_0), ((_S5)).xy, ((_S5)).z)), _S8), mix((textureLoad((depth_0), ((_S6)).xy, ((_S6)).z)), (textureLoad((depth_0), ((_S7)).xy, ((_S7)).z)), _S8), weight_0.y);
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

fn ViewFacetNormal_0( uv_3 : vec2<f32>,  viewPosition_0 : vec3<f32>) -> vec3<f32>
{
    var texel_0 : vec2<f32> = vec2<f32>(1.0f) / vec2<f32>(f32(Globals_0.uScreenWidth_0), f32(Globals_0.uScreenHeight_0));
    var _S9 : vec2<f32> = vec2<f32>(texel_0.x, 0.0f);
    var _S10 : vec2<f32> = uv_3 + _S9;
    var right_0 : vec3<f32> = ViewPositionAt_0(_S10, DepthBilinear_0(uSceneDepth_0, uSceneDepthSampler_0, _S10));
    var _S11 : vec2<f32> = uv_3 - _S9;
    var left_0 : vec3<f32> = ViewPositionAt_0(_S11, DepthBilinear_0(uSceneDepth_0, uSceneDepthSampler_0, _S11));
    var _S12 : vec2<f32> = vec2<f32>(0.0f, texel_0.y);
    var _S13 : vec2<f32> = uv_3 + _S12;
    var down_0 : vec3<f32> = ViewPositionAt_0(_S13, DepthBilinear_0(uSceneDepth_0, uSceneDepthSampler_0, _S13));
    var _S14 : vec2<f32> = uv_3 - _S12;
    var up_0 : vec3<f32> = ViewPositionAt_0(_S14, DepthBilinear_0(uSceneDepth_0, uSceneDepthSampler_0, _S14));
    var _S15 : f32 = viewPosition_0.z;
    var alongX_0 : vec3<f32>;
    if((abs(right_0.z - _S15)) < (abs(_S15 - left_0.z)))
    {
        alongX_0 = right_0 - viewPosition_0;
    }
    else
    {
        alongX_0 = viewPosition_0 - left_0;
    }
    var alongY_0 : vec3<f32>;
    if((abs(down_0.z - _S15)) < (abs(_S15 - up_0.z)))
    {
        alongY_0 = down_0 - viewPosition_0;
    }
    else
    {
        alongY_0 = viewPosition_0 - up_0;
    }
    var facet_0 : vec3<f32> = cross(alongX_0, alongY_0);
    var length2_0 : f32 = dot(facet_0, facet_0);
    if(length2_0 <= 9.99999968265522539e-21f)
    {
        return vec3<f32>(0.0f, 0.0f, 0.0f);
    }
    var facet_1 : vec3<f32> = facet_0 * vec3<f32>(rsqrt_0(length2_0));
    if((dot(facet_1, viewPosition_0)) > 0.0f)
    {
        alongX_0 = (vec3<f32>(0) - facet_1);
    }
    else
    {
        alongX_0 = facet_1;
    }
    return alongX_0;
}

fn GatherNormal_0( normal_0 : vec3<f32>,  eyeToSurface_0 : vec3<f32>,  uv_4 : vec2<f32>,  viewPosition_1 : vec3<f32>) -> vec3<f32>
{
    var toEye_0 : vec3<f32> = (vec3<f32>(0) - normalize(eyeToSurface_0));
    var facing_0 : f32 = dot(normal_0, toEye_0);
    if(facing_0 >= 0.0f)
    {
        return normal_0;
    }
    var viewFacet_0 : vec3<f32> = ViewFacetNormal_0(uv_4, viewPosition_1);
    var facet_2 : vec3<f32> = Globals_0.uViewToWorld0_0 * vec3<f32>(viewFacet_0.x) + Globals_0.uViewToWorld1_0 * vec3<f32>(viewFacet_0.y) + Globals_0.uViewToWorld2_0 * vec3<f32>(viewFacet_0.z);
    var _S16 : bool;
    if((dot(facet_2, facet_2)) < 0.5f)
    {
        _S16 = true;
    }
    else
    {
        _S16 = (dot(normal_0, facet_2)) <= 0.0f;
    }
    if(_S16)
    {
        return (vec3<f32>(0) - normal_0);
    }
    return normalize(normal_0 - vec3<f32>((2.0f * facing_0)) * toEye_0);
}

fn ProbeSurface_0( probe_0 : vec2<i32>,  world_0 : ptr<function, vec3<f32>>,  viewPosition_2 : ptr<function, vec3<f32>>,  worldNormal_0 : ptr<function, vec3<f32>>) -> bool
{
    const _S17 : vec3<f32> = vec3<f32>(0.0f, 0.0f, 0.0f);
    (*world_0) = _S17;
    (*viewPosition_2) = _S17;
    (*worldNormal_0) = vec3<f32>(0.0f, 0.0f, 1.0f);
    var uv_5 : vec2<f32> = (vec2<f32>(min(probe_0 * vec2<i32>(Globals_0.uProbeStep_0) + ProbeTileOffset_0(Globals_0.uFrameIndex_0, Globals_0.uProbeStep_0), vec2<i32>(Globals_0.uScreenWidth_0, Globals_0.uScreenHeight_0) - vec2<i32>(i32(1)))) + vec2<f32>(0.5f)) / vec2<f32>(f32(Globals_0.uScreenWidth_0), f32(Globals_0.uScreenHeight_0));
    var depth_4 : f32 = DepthBilinear_0(uSceneDepth_0, uSceneDepthSampler_0, uv_5);
    if(IsSkyDepth_0(depth_4))
    {
        return false;
    }
    var _S18 : vec3<f32> = ViewPositionAt_0(uv_5, depth_4);
    (*viewPosition_2) = _S18;
    (*world_0) = Globals_0.uCameraToCache_0 + Globals_0.uViewToWorld0_0 * vec3<f32>(_S18.x) + Globals_0.uViewToWorld1_0 * vec3<f32>(_S18.y) + Globals_0.uViewToWorld2_0 * vec3<f32>(_S18.z);
    var _S19 : vec3<f32> = normalize((textureSampleLevel((uSceneNormal_0), (uSceneNormalSampler_0), (uv_5), (0.0f))).xyz * vec3<f32>(2.0f) - vec3<f32>(1.0f));
    (*worldNormal_0) = _S19;
    (*worldNormal_0) = GatherNormal_0(_S19, (*world_0) - Globals_0.uCameraToCache_0, uv_5, (*viewPosition_2));
    return true;
}

fn RadicalInverseBits_0( bits_0 : u32) -> f32
{
    var _S20 : u32 = (((bits_0 << (u32(16)))) | (((bits_0 >> (u32(16))))));
    var _S21 : u32 = (((((_S20 & (u32(1431655765)))) << (u32(1)))) | (((((_S20 & (u32(2863311530)))) >> (u32(1))))));
    var _S22 : u32 = (((((_S21 & (u32(858993459)))) << (u32(2)))) | (((((_S21 & (u32(3435973836)))) >> (u32(2))))));
    var _S23 : u32 = (((((_S22 & (u32(252645135)))) << (u32(4)))) | (((((_S22 & (u32(4042322160)))) >> (u32(4))))));
    return f32((((((_S23 & (u32(16711935)))) << (u32(8)))) | (((((_S23 & (u32(4278255360)))) >> (u32(8))))))) * 2.32830643653869629e-10f;
}

fn FrameJitter_0( probeCoord_0 : vec2<i32>,  frame_1 : i32) -> vec2<f32>
{
    if(frame_1 <= i32(0))
    {
        return vec2<f32>(0.5f, 0.5f);
    }
    var rotation_0 : f32 = f32(((((u32(probeCoord_0.x) * u32(73856093)) ^ ((u32(probeCoord_0.y) * u32(19349663))))) & (u32(65535)))) / 6.5536e+04f;
    var n_0 : u32 = u32((frame_1 & (i32(65535))));
    return fract(vec2<f32>(fract(f32(n_0) * 0.6180340051651001f), RadicalInverseBits_0(n_0)) + vec2<f32>(rotation_0, fract(rotation_0 * 1.6180340051651001f)));
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
    var _S24 : f32 = normal_2.z;
    if(_S24 < -0.99999988079071045f)
    {
        basis_0.tangent_0 = vec3<f32>(0.0f, -1.0f, 0.0f);
        basis_0.bitangent_0 = vec3<f32>(-1.0f, 0.0f, 0.0f);
        return basis_0;
    }
    var a_0 : f32 = 1.0f / (1.0f + _S24);
    var _S25 : f32 = normal_2.x;
    var _S26 : f32 = - _S25;
    var _S27 : f32 = normal_2.y;
    var b_0 : f32 = _S26 * _S27 * a_0;
    basis_0.tangent_0 = vec3<f32>(1.0f - _S25 * _S25 * a_0, b_0, _S26);
    basis_0.bitangent_0 = vec3<f32>(b_0, 1.0f - _S27 * _S27 * a_0, - _S27);
    return basis_0;
}

fn ToView_0( world_1 : vec3<f32>) -> vec3<f32>
{
    return vec3<f32>(dot(Globals_0.uViewToWorld0_0, world_1), dot(Globals_0.uViewToWorld1_0, world_1), dot(Globals_0.uViewToWorld2_0, world_1));
}

struct GatherProbe_0
{
     basis_1 : HemisphereBasis_0,
     viewNormal_0 : vec3<f32>,
     traceFrom_0 : vec3<f32>,
};

fn BeginGather_0( world_2 : vec3<f32>,  normal_3 : vec3<f32>,  viewOrigin_0 : vec3<f32>,  probe_1 : ptr<function, GatherProbe_0>) -> bool
{
    (*probe_1).basis_1 = BasisAround_0(normal_3);
    (*probe_1).viewNormal_0 = ToView_0(normal_3);
    (*probe_1).traceFrom_0 = world_2;
    var _S28 : bool;
    if((Globals_0.uShadowLevels_0) > i32(0))
    {
        _S28 = (Globals_0.uLookupBuckets_0) > i32(0);
    }
    else
    {
        _S28 = false;
    }
    return _S28;
}

struct GatherSums_0
{
     irradiance_0 : vec3<f32>,
     counted_0 : f32,
     surfaceSum_0 : vec3<f32>,
     surfaceCount_0 : f32,
     openSum_0 : vec3<f32>,
     openCount_0 : f32,
     unplaced_0 : f32,
};

fn BeginSums_0() -> GatherSums_0
{
    var sums_0 : GatherSums_0;
    const _S29 : vec3<f32> = vec3<f32>(0.0f, 0.0f, 0.0f);
    sums_0.irradiance_0 = _S29;
    sums_0.counted_0 = 0.0f;
    sums_0.surfaceSum_0 = _S29;
    sums_0.surfaceCount_0 = 0.0f;
    sums_0.openSum_0 = _S29;
    sums_0.openCount_0 = 0.0f;
    sums_0.unplaced_0 = 0.0f;
    return sums_0;
}

fn Orient_0( basis_2 : HemisphereBasis_0,  local_0 : vec3<f32>) -> vec3<f32>
{
    return basis_2.tangent_0 * vec3<f32>(local_0.x) + basis_2.bitangent_0 * vec3<f32>(local_0.y) + basis_2.normal_1 * vec3<f32>(local_0.z);
}

fn ProjectToUv_0( viewPosition_3 : vec3<f32>) -> vec2<f32>
{
    var _S30 : f32 = viewPosition_3.z;
    var _S31 : vec2<f32> = vec2<f32>(0.5f);
    return vec2<f32>(viewPosition_3.x * Globals_0.uUnproject_0.x / _S30, -1.0f * (viewPosition_3.y * Globals_0.uUnproject_0.y) / _S30) * _S31 + _S31;
}

fn CapRayIntensity_0( radiance_0 : vec3<f32>) -> vec3<f32>
{
    var _S32 : f32 = max(radiance_0.x, max(radiance_0.y, radiance_0.z));
    var _S33 : vec3<f32>;
    if(_S32 > (Globals_0.uMaxRayIntensity_0))
    {
        _S33 = radiance_0 * vec3<f32>((Globals_0.uMaxRayIntensity_0 / _S32));
    }
    else
    {
        _S33 = radiance_0;
    }
    return _S33;
}

fn ScreenTraceRadiance_0( viewOrigin_1 : vec3<f32>,  viewNormal_1 : vec3<f32>,  viewDir_0 : vec3<f32>,  radiance_1 : ptr<function, vec3<f32>>) -> bool
{
    var hitFraction_0 : f32;
    (*radiance_1) = vec3<f32>(0.0f, 0.0f, 0.0f);
    var _S34 : bool;
    if((Globals_0.uScreenTraceSteps_0) <= i32(0))
    {
        _S34 = true;
    }
    else
    {
        _S34 = (viewDir_0.z) == 0.0f;
    }
    if(_S34)
    {
        return false;
    }
    var origin_0 : vec3<f32> = viewOrigin_1 + viewNormal_1 * vec3<f32>(0.01999999955296516f);
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
    var _S35 : f32 = farPoint_1.z;
    if(_S35 <= (Globals_0.uUnproject_0.z))
    {
        return false;
    }
    var startUv_0 : vec2<f32> = ProjectToUv_0(origin_0);
    var travel_0 : vec2<f32> = ProjectToUv_0(farPoint_1) - startUv_0;
    var used_0 : i32 = i32(clamp(length(travel_0 / (vec2<f32>(1.0f) / vec2<f32>(f32(Globals_0.uScreenWidth_0), f32(Globals_0.uScreenHeight_0)))), 1.0f, f32(Globals_0.uScreenTraceSteps_0)));
    var stride_0 : f32 = 1.0f / f32(used_0);
    var _S36 : f32 = fract(52.98291778564453125f * fract(dot(startUv_0 * vec2<f32>(f32(Globals_0.uScreenWidth_0), f32(Globals_0.uScreenHeight_0)) + vec2<f32>((5.58823776245117188f * f32(((Globals_0.uFrameIndex_0) & (i32(63)))))), vec2<f32>(0.06711056083440781f, 0.00583714991807938f)))) * stride_0;
    var previous_0 : f32 = 0.0f;
    var i_1 : i32 = i32(1);
    for(;;)
    {
        if(i_1 <= i32(256))
        {
        }
        else
        {
            hitFraction_0 = -1.0f;
            break;
        }
        if(i_1 > used_0)
        {
            hitFraction_0 = -1.0f;
            break;
        }
        var t_0 : f32 = f32(i_1) * stride_0 + _S36;
        if(t_0 > 1.0f)
        {
            hitFraction_0 = -1.0f;
            break;
        }
        var uv_6 : vec2<f32> = startUv_0 + travel_0 * vec2<f32>(t_0);
        if((any((uv_6 < vec2<f32>(0.0f, 0.0f)))))
        {
            _S34 = true;
        }
        else
        {
            _S34 = (any((uv_6 > vec2<f32>(1.0f, 1.0f))));
        }
        if(_S34)
        {
            hitFraction_0 = -1.0f;
            break;
        }
        var _S37 : f32 = origin_0.z;
        var sceneDepth_0 : f32 = ViewPositionAt_0(uv_6, DepthBilinear_0(uSceneDepth_0, uSceneDepthSampler_0, uv_6)).z;
        var difference_0 : f32 = 1.0f / mix(1.0f / _S37, 1.0f / _S35, t_0) - sceneDepth_0;
        var _S38 : bool;
        if(difference_0 > 0.0f)
        {
            _S38 = difference_0 < (Globals_0.uScreenThickness_0 + sceneDepth_0 * 0.01999999955296516f);
        }
        else
        {
            _S38 = false;
        }
        if(_S38)
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
                if((1.0f / mix(1.0f / _S37, 1.0f / _S35, mid_0) - ViewPositionAt_0(midUv_0, DepthBilinear_0(uSceneDepth_0, uSceneDepthSampler_0, midUv_0)).z) > 0.0f)
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
        var _S39 : i32 = i_1 + i32(1);
        previous_0 = t_0;
        i_1 = _S39;
    }
    if(hitFraction_0 <= 0.0f)
    {
        return false;
    }
    var hitUv_0 : vec2<f32> = startUv_0 + travel_0 * vec2<f32>(hitFraction_0);
    var hitNormal_0 : vec3<f32> = ToView_0((textureSampleLevel((uSceneNormal_0), (uSceneNormalSampler_0), (hitUv_0), (0.0f))).xyz * vec3<f32>(2.0f) - vec3<f32>(1.0f));
    if((dot(hitNormal_0, viewDir_0)) >= 0.0f)
    {
        return false;
    }
    var hitView_0 : vec3<f32> = ViewPositionAt_0(hitUv_0, DepthBilinear_0(uSceneDepth_0, uSceneDepthSampler_0, hitUv_0));
    var hitWorld_0 : vec3<f32> = Globals_0.uCameraToCache_0 + Globals_0.uViewToWorld0_0 * vec3<f32>(hitView_0.x) + Globals_0.uViewToWorld1_0 * vec3<f32>(hitView_0.y) + Globals_0.uViewToWorld2_0 * vec3<f32>(hitView_0.z);
    var _S40 : f32 = hitWorld_0.x;
    var _S41 : f32 = hitWorld_0.y;
    var _S42 : f32 = hitWorld_0.z;
    var colourView_0 : vec3<f32> = Globals_0.uColourWorldToView0_0 * vec3<f32>((_S40 - Globals_0.uColourCameraToCache_0.x)) + Globals_0.uColourWorldToView1_0 * vec3<f32>((_S41 - Globals_0.uColourCameraToCache_0.y)) + Globals_0.uColourWorldToView2_0 * vec3<f32>((_S42 - Globals_0.uColourCameraToCache_0.z));
    if((Globals_0.uHasHistory_0) == i32(0))
    {
        _S34 = true;
    }
    else
    {
        _S34 = (colourView_0.z) <= (Globals_0.uUnproject_0.z);
    }
    if(_S34)
    {
        return false;
    }
    var colourUv_0 : vec2<f32> = ProjectToUv_0(colourView_0);
    if((any((colourUv_0 < vec2<f32>(0.0f, 0.0f)))))
    {
        _S34 = true;
    }
    else
    {
        _S34 = (any((colourUv_0 > vec2<f32>(1.0f, 1.0f))));
    }
    if(_S34)
    {
        return false;
    }
    var prevView_0 : vec3<f32> = Globals_0.uPrevWorldToView0_0 * vec3<f32>((_S40 - Globals_0.uPrevCameraToCache_0.x)) + Globals_0.uPrevWorldToView1_0 * vec3<f32>((_S41 - Globals_0.uPrevCameraToCache_0.y)) + Globals_0.uPrevWorldToView2_0 * vec3<f32>((_S42 - Globals_0.uPrevCameraToCache_0.z));
    var _S43 : f32 = prevView_0.z;
    if(_S43 <= (Globals_0.uUnproject_0.z))
    {
        return false;
    }
    var probeFrameUv_0 : vec2<f32> = ProjectToUv_0(prevView_0);
    var probeGrid_0 : vec2<i32> = (vec2<i32>(Globals_0.uScreenWidth_0, Globals_0.uScreenHeight_0) + vec2<i32>((Globals_0.uProbeStep_0 - i32(1)))) / vec2<i32>(Globals_0.uProbeStep_0);
    var _S44 : vec2<i32> = vec2<i32>(probeFrameUv_0 * vec2<f32>(f32(Globals_0.uScreenWidth_0), f32(Globals_0.uScreenHeight_0))) / vec2<i32>(Globals_0.uProbeStep_0);
    var _S45 : vec3<i32> = vec3<i32>(clamp(_S44, vec2<i32>(i32(0), i32(0)), probeGrid_0 - vec2<i32>(i32(1), i32(1))), i32(0));
    var prevProbePos_0 : vec4<f32> = (textureLoad((uPositionHistory_0), ((_S45)).xy, ((_S45)).z));
    var hitWorldNormal_0 : vec3<f32> = normalize(Globals_0.uViewToWorld0_0 * vec3<f32>(hitNormal_0.x) + Globals_0.uViewToWorld1_0 * vec3<f32>(hitNormal_0.y) + Globals_0.uViewToWorld2_0 * vec3<f32>(hitNormal_0.z));
    var probeSpan_0 : f32 = 2.0f * _S43 * f32(Globals_0.uProbeStep_0) / (Globals_0.uUnproject_0.y * f32(Globals_0.uScreenHeight_0));
    if((prevProbePos_0.w) <= 0.0f)
    {
        _S34 = true;
    }
    else
    {
        _S34 = (abs(dot(prevProbePos_0.xyz + Globals_0.uPrevCameraToCache_0 - hitWorld_0, hitWorldNormal_0))) > (max(0.10000000149011612f, 2.0f * probeSpan_0));
    }
    if(_S34)
    {
        return false;
    }
    var hitColour_0 : vec4<f32> = (textureSampleLevel((uSceneColour_0), (uSceneColourSampler_0), (colourUv_0), (0.0f)));
    if((hitColour_0.w) < 0.5f)
    {
        return false;
    }
    (*radiance_1) = CapRayIntensity_0(hitColour_0.xyz);
    return true;
}

fn DistanceAt_0( world_3 : vec3<f32>,  known_0 : ptr<function, bool>,  raw_0 : ptr<function, f32>,  resolved_0 : ptr<function, f32>) -> f32
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
        var _S46 : f32 = Globals_0.uClipmapOrigin_0[level_0].w;
        var voxel_0 : vec3<f32> = (world_3 - Globals_0.uClipmapOrigin_0[level_0].xyz) / vec3<f32>(_S46);
        var _S47 : bool;
        if((any((voxel_0 < vec3<f32>(0.5f, 0.5f, 0.5f)))))
        {
            _S47 = true;
        }
        else
        {
            _S47 = (any((voxel_0 > vec3<f32>(f32(Globals_0.uShadowResolution_0) - 0.5f, f32(Globals_0.uShadowResolution_0) - 0.5f, f32(Globals_0.uShadowResolution_0) - 0.5f))));
        }
        if(_S47)
        {
            level_0 = level_0 + i32(1);
            continue;
        }
        var uvw_0 : vec3<f32> = vec3<f32>(voxel_0.x / f32(Globals_0.uShadowResolution_0), voxel_0.y / f32(Globals_0.uShadowResolution_0), (f32(level_0) * f32(Globals_0.uShadowResolution_0) + voxel_0.z) / f32(Globals_0.uShadowResolution_0 * Globals_0.uShadowLevels_0));
        (*known_0) = true;
        (*raw_0) = (textureSampleLevel((uClipmaps_0), (uClipmapsSampler_0), (uvw_0), (0.0f))).x;
        (*resolved_0) = _S46;
        return ((*raw_0) * 2.0f - 1.0f) * Globals_0.uClipmapRange_0.data_0[level_0].x;
    }
    return 1.0e+06f;
}

fn DistanceAt_1( world_4 : vec3<f32>,  known_1 : ptr<function, bool>,  raw_1 : ptr<function, f32>,  resolved_1 : ptr<function, f32>) -> f32
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
        var _S48 : f32 = Globals_0.uClipmapOrigin_0[level_1].w;
        var voxel_1 : vec3<f32> = (world_4 - Globals_0.uClipmapOrigin_0[level_1].xyz) / vec3<f32>(_S48);
        var _S49 : bool;
        if((any((voxel_1 < vec3<f32>(0.5f, 0.5f, 0.5f)))))
        {
            _S49 = true;
        }
        else
        {
            _S49 = (any((voxel_1 > vec3<f32>(f32(Globals_0.uShadowResolution_0) - 0.5f, f32(Globals_0.uShadowResolution_0) - 0.5f, f32(Globals_0.uShadowResolution_0) - 0.5f))));
        }
        if(_S49)
        {
            level_1 = level_1 + i32(1);
            continue;
        }
        var uvw_1 : vec3<f32> = vec3<f32>(voxel_1.x / f32(Globals_0.uShadowResolution_0), voxel_1.y / f32(Globals_0.uShadowResolution_0), (f32(level_1) * f32(Globals_0.uShadowResolution_0) + voxel_1.z) / f32(Globals_0.uShadowResolution_0 * Globals_0.uShadowLevels_0));
        (*known_1) = true;
        (*raw_1) = (textureSampleLevel((uClipmaps_0), (uClipmapsSampler_0), (uvw_1), (0.0f))).x;
        (*resolved_1) = _S48;
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

fn VisibilityAt_0( world_5 : vec3<f32>,  normal_4 : vec3<f32>,  direction_0 : vec3<f32>,  hit_0 : ptr<function, vec3<f32>>) -> f32
{
    (*hit_0) = world_5;
    if((Globals_0.uShadowLevels_0) <= i32(0))
    {
        return 1.0f;
    }
    var _S50 : vec3<f32> = world_5 + normal_4 * vec3<f32>(Globals_0.uClipmapOrigin_0[i32(0)].w) * vec3<f32>(1.5f);
    var _S51 : f32 = Globals_0.uClipmapOrigin_0[i32(0)].w * f32(Globals_0.uShadowResolution_0) * f32((i32(1) << (u32(Globals_0.uShadowLevels_0 - i32(1)))));
    var step_1 : i32 = i32(0);
    var travelled_1 : f32 = 0.0f;
    for(;;)
    {
        var _S52 : bool;
        if(step_1 < i32(64))
        {
            _S52 = travelled_1 < _S51;
        }
        else
        {
            _S52 = false;
        }
        if(_S52)
        {
        }
        else
        {
            break;
        }
        var known_2 : bool;
        var raw_2 : f32;
        var resolved_3 : f32;
        var distance_0 : f32 = DistanceAt_1(_S50 + direction_0 * vec3<f32>(travelled_1), &(known_2), &(raw_2), &(resolved_3));
        if(!known_2)
        {
            return 1.0f;
        }
        var hitDistance_1 : f32;
        var _S53 : bool = FieldStepHits_0(distance_0, travelled_1, resolved_3, &(hitDistance_1));
        if(_S53)
        {
            (*hit_0) = _S50 + direction_0 * vec3<f32>(hitDistance_1);
            return 0.0f;
        }
        var travelled_2 : f32 = travelled_1 + max(distance_0, resolved_3 * 0.25f);
        step_1 = step_1 + i32(1);
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

fn ShelterHolds_0( m_2 : ShelterMap_0,  texel_1 : vec2<i32>) -> bool
{
    var _S54 : bool;
    if((m_2.present_0) > 0.5f)
    {
        _S54 = (texel_1.x) >= i32(0);
    }
    else
    {
        _S54 = false;
    }
    if(_S54)
    {
        _S54 = (texel_1.y) >= i32(0);
    }
    else
    {
        _S54 = false;
    }
    if(_S54)
    {
        _S54 = (texel_1.x) < i32(m_2.size_0.x);
    }
    else
    {
        _S54 = false;
    }
    if(_S54)
    {
        _S54 = (texel_1.y) < i32(m_2.size_0.y);
    }
    else
    {
        _S54 = false;
    }
    return _S54;
}

fn ShelterSurface_0( m_3 : ShelterMap_0,  texel_2 : vec4<f32>) -> f32
{
    var packed_0 : f32 = floor(texel_2.z * 255.0f + 0.5f) + floor(texel_2.y * 255.0f + 0.5f) * 256.0f;
    var _S55 : f32;
    if(packed_0 < 0.5f)
    {
        _S55 = -1.00000001504746622e+30f;
    }
    else
    {
        _S55 = m_3.lowestZ_0 + (packed_0 - 1.0f) * m_3.scale_0;
    }
    return _S55;
}

fn TerrainHeightAt_0( m_4 : ShelterMap_0,  worldXY_1 : vec2<f32>) -> f32
{
    var at_0 : vec2<i32> = ShelterTexel_0(m_4, worldXY_1);
    var _S56 : f32;
    if(ShelterHolds_0(m_4, at_0))
    {
        var _S57 : vec3<i32> = vec3<i32>(at_0, i32(0));
        _S56 = ShelterSurface_0(m_4, (textureLoad((uTerrainHeights_0), ((_S57)).xy, ((_S57)).z)));
    }
    else
    {
        _S56 = -1.00000001504746622e+30f;
    }
    return _S56;
}

fn TerrainHitDistance_0( absoluteOrigin_0 : vec3<f32>,  direction_1 : vec3<f32>,  reach_1 : f32) -> f32
{
    var m_5 : ShelterMap_0 = TerrainMap_0();
    var _S58 : bool;
    if((m_5.present_0) < 0.5f)
    {
        _S58 = true;
    }
    else
    {
        _S58 = (direction_1.z) > -0.00999999977648258f;
    }
    if(_S58)
    {
        _S58 = true;
    }
    else
    {
        _S58 = (m_5.invCell_0) <= 0.0f;
    }
    if(_S58)
    {
        return -1.0f;
    }
    var _S59 : f32 = 0.5f / m_5.invCell_0;
    var previous_1 : f32 = 0.0f;
    var i_2 : i32 = i32(1);
    for(;;)
    {
        if(i_2 <= i32(64))
        {
        }
        else
        {
            break;
        }
        var _S60 : f32 = min(f32(i_2) * _S59, reach_1);
        var at_1 : vec3<f32> = absoluteOrigin_0 + direction_1 * vec3<f32>(_S60);
        if((at_1.z) <= (TerrainHeightAt_0(m_5, at_1.xy)))
        {
            var low_0 : f32 = previous_1;
            var high_1 : f32 = _S60;
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
                var probe_2 : vec3<f32> = absoluteOrigin_0 + direction_1 * vec3<f32>(middle_0);
                if((probe_2.z) <= (TerrainHeightAt_0(m_5, probe_2.xy)))
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
        if(_S60 >= reach_1)
        {
            break;
        }
        var _S61 : i32 = i_2 + i32(1);
        previous_1 = _S60;
        i_2 = _S61;
    }
    return -1.0f;
}

fn LookupBucket_0( cell_0 : vec3<i32>) -> i32
{
    return i32(((((((u32(cell_0.x) * u32(2376512323)) ^ ((u32(cell_0.y) * u32(3625334849))))) ^ ((u32(cell_0.z) * u32(3407524639))))) & (u32(Globals_0.uLookupBuckets_0 - i32(1)))));
}

fn MoverPages_0( headers_0 : i32,  index_0 : i32,  world_6 : vec3<f32>,  bias_0 : f32) -> vec2<i32>
{
    var at_2 : i32 = headers_0 + index_0 * i32(6);
    var reach_2 : f32 = (bitcast<f32>((uCellPages_0[at_2 + i32(3)]))) + bias_0;
    var toCentre_0 : vec3<f32> = world_6 - vec3<f32>((bitcast<f32>((uCellPages_0[at_2]))), (bitcast<f32>((uCellPages_0[at_2 + i32(1)]))), (bitcast<f32>((uCellPages_0[at_2 + i32(2)]))));
    var _S62 : vec2<i32>;
    if((dot(toCentre_0, toCentre_0)) > (reach_2 * reach_2))
    {
        _S62 = vec2<i32>(i32(0), i32(0));
    }
    else
    {
        _S62 = vec2<i32>(uCellPages_0[at_2 + i32(4)], uCellPages_0[at_2 + i32(5)]);
    }
    return _S62;
}

fn ProjectOntoCard_0( page_0 : i32,  world_7 : vec3<f32>,  directionW_0 : vec3<f32>,  across_0 : ptr<function, vec2<f32>>,  halfSize_0 : ptr<function, vec2<f32>>,  behind_0 : ptr<function, f32>,  depth_5 : ptr<function, f32>,  facing_1 : ptr<function, vec3<f32>>,  directionLocal_0 : ptr<function, vec3<f32>>,  litRect_0 : ptr<function, vec4<f32>>,  cardPage_0 : ptr<function, vec4<f32>>)
{
    var _S63 : i32 = page_0 * i32(10);
    (*litRect_0) = uPages_0[_S63];
    (*cardPage_0) = uPages_0[_S63 + i32(1)];
    var centreAndHalfWidth_0 : vec4<f32> = uPages_0[_S63 + i32(2)];
    var rightAndHalfHeight_0 : vec4<f32> = uPages_0[_S63 + i32(3)];
    var upAndDepth_0 : vec4<f32> = uPages_0[_S63 + i32(4)];
    var direction_2 : vec4<f32> = uPages_0[_S63 + i32(5)];
    var row0_0 : vec4<f32> = uPages_0[_S63 + i32(6)];
    var row1_0 : vec4<f32> = uPages_0[_S63 + i32(7)];
    var row2_0 : vec4<f32> = uPages_0[_S63 + i32(8)];
    var delta_0 : vec3<f32> = world_7 - vec3<f32>(row0_0.w, row1_0.w, row2_0.w);
    var _S64 : vec3<f32> = row0_0.xyz;
    var _S65 : vec3<f32> = row1_0.xyz;
    var _S66 : vec3<f32> = row2_0.xyz;
    var local_1 : vec3<f32> = vec3<f32>(dot(_S64, delta_0), dot(_S65, delta_0), dot(_S66, delta_0));
    (*directionLocal_0) = vec3<f32>(dot(_S64, directionW_0), dot(_S65, directionW_0), dot(_S66, directionW_0));
    var relative_0 : vec3<f32> = local_1 - centreAndHalfWidth_0.xyz;
    var _S67 : vec2<f32> = max(vec2<f32>(centreAndHalfWidth_0.w, rightAndHalfHeight_0.w), vec2<f32>(9.99999997475242708e-07f, 9.99999997475242708e-07f));
    (*halfSize_0) = _S67;
    (*across_0) = vec2<f32>(dot(relative_0, rightAndHalfHeight_0.xyz), dot(relative_0, upAndDepth_0.xyz)) / _S67;
    var _S68 : vec3<f32> = direction_2.xyz;
    (*behind_0) = - dot(relative_0, _S68);
    (*depth_5) = upAndDepth_0.w;
    (*facing_1) = _S68;
    return;
}

fn ProjectOntoCard_1( page_1 : i32,  world_8 : vec3<f32>,  directionW_1 : vec3<f32>,  across_1 : ptr<function, vec2<f32>>,  halfSize_1 : ptr<function, vec2<f32>>,  behind_1 : ptr<function, f32>,  depth_6 : ptr<function, f32>,  facing_2 : ptr<function, vec3<f32>>,  directionLocal_1 : ptr<function, vec3<f32>>,  litRect_1 : ptr<function, vec4<f32>>,  cardPage_1 : ptr<function, vec4<f32>>)
{
    var _S69 : i32 = page_1 * i32(10);
    (*litRect_1) = uPages_0[_S69];
    (*cardPage_1) = uPages_0[_S69 + i32(1)];
    var centreAndHalfWidth_1 : vec4<f32> = uPages_0[_S69 + i32(2)];
    var rightAndHalfHeight_1 : vec4<f32> = uPages_0[_S69 + i32(3)];
    var upAndDepth_1 : vec4<f32> = uPages_0[_S69 + i32(4)];
    var direction_3 : vec4<f32> = uPages_0[_S69 + i32(5)];
    var row0_1 : vec4<f32> = uPages_0[_S69 + i32(6)];
    var row1_1 : vec4<f32> = uPages_0[_S69 + i32(7)];
    var row2_1 : vec4<f32> = uPages_0[_S69 + i32(8)];
    var delta_1 : vec3<f32> = world_8 - vec3<f32>(row0_1.w, row1_1.w, row2_1.w);
    var _S70 : vec3<f32> = row0_1.xyz;
    var _S71 : vec3<f32> = row1_1.xyz;
    var _S72 : vec3<f32> = row2_1.xyz;
    var local_2 : vec3<f32> = vec3<f32>(dot(_S70, delta_1), dot(_S71, delta_1), dot(_S72, delta_1));
    (*directionLocal_1) = vec3<f32>(dot(_S70, directionW_1), dot(_S71, directionW_1), dot(_S72, directionW_1));
    var relative_1 : vec3<f32> = local_2 - centreAndHalfWidth_1.xyz;
    var _S73 : vec2<f32> = max(vec2<f32>(centreAndHalfWidth_1.w, rightAndHalfHeight_1.w), vec2<f32>(9.99999997475242708e-07f, 9.99999997475242708e-07f));
    (*halfSize_1) = _S73;
    (*across_1) = vec2<f32>(dot(relative_1, rightAndHalfHeight_1.xyz), dot(relative_1, upAndDepth_1.xyz)) / _S73;
    var _S74 : vec3<f32> = direction_3.xyz;
    (*behind_1) = - dot(relative_1, _S74);
    (*depth_6) = upAndDepth_1.w;
    (*facing_2) = _S74;
    return;
}

fn SampleCardsAndAlbedoWithin_0( world_9 : vec3<f32>,  normalW_0 : vec3<f32>,  bias_1 : f32,  radiance_2 : ptr<function, vec3<f32>>,  albedo_0 : ptr<function, vec3<f32>>) -> bool
{
    const _S75 : vec3<f32> = vec3<f32>(0.0f, 0.0f, 0.0f);
    (*radiance_2) = _S75;
    (*albedo_0) = _S75;
    if((Globals_0.uLookupBuckets_0) <= i32(0))
    {
        return false;
    }
    var _S76 : vec2<i32> = uCells_0[LookupBucket_0(vec3<i32>(floor((world_9 - Globals_0.uLookupMin_0) / vec3<f32>(Globals_0.uLookupCell_0))))];
    var albedoWidth_0 : u32;
    var albedoHeight_0 : u32;
    {var dim = textureDimensions((uAlbedo_0));((albedoWidth_0)) = dim.x;((albedoHeight_0)) = dim.y;};
    var _S77 : vec2<f32> = vec2<f32>(f32(albedoWidth_0), f32(albedoHeight_0));
    var _S78 : vec2<i32> = uCells_0[Globals_0.uLookupBuckets_0];
    var walk_0 : i32 = i32(0);
    var weightSum_0 : f32 = 0.0f;
    for(;;)
    {
        if(walk_0 <= (_S78.y))
        {
        }
        else
        {
            break;
        }
        var _S79 : bool = walk_0 == i32(0);
        var _S80 : vec2<i32>;
        if(_S79)
        {
            _S80 = _S76;
        }
        else
        {
            _S80 = MoverPages_0(_S78.x, walk_0 - i32(1), world_9, bias_1);
        }
        var slot_0 : i32 = i32(0);
        var weightSum_1 : f32 = weightSum_0;
        for(;;)
        {
            if(slot_0 < (_S80.y))
            {
            }
            else
            {
                break;
            }
            var page_2 : i32 = uCellPages_0[_S80.x + slot_0];
            var _S81 : i32 = uCellPages_0[_S80.x + slot_0] * i32(10);
            if(((uPages_0[_S81 + i32(5)].w) > 0.5f) == _S79)
            {
                slot_0 = slot_0 + i32(1);
                continue;
            }
            var bound_0 : vec4<f32> = uPages_0[_S81 + i32(9)];
            var toCentre_1 : vec3<f32> = world_9 - bound_0.xyz;
            var reach_3 : f32 = bound_0.w + bias_1;
            if((dot(toCentre_1, toCentre_1)) > (reach_3 * reach_3))
            {
                slot_0 = slot_0 + i32(1);
                continue;
            }
            var across_2 : vec2<f32>;
            var halfSize_2 : vec2<f32>;
            var behind_2 : f32;
            var depth_7 : f32;
            var facing_3 : vec3<f32>;
            var normalLocal_0 : vec3<f32>;
            var litRect_2 : vec4<f32>;
            var cardPage_2 : vec4<f32>;
            ProjectOntoCard_1(page_2, world_9, normalW_0, &(across_2), &(halfSize_2), &(behind_2), &(depth_7), &(facing_3), &(normalLocal_0), &(litRect_2), &(cardPage_2));
            var towards_0 : f32 = dot(facing_3, normalLocal_0);
            if(towards_0 <= 0.0f)
            {
                slot_0 = slot_0 + i32(1);
                continue;
            }
            var squareness_0 : f32 = towards_0 * towards_0;
            var slack_0 : f32 = 0.5f * bias_1;
            var acrossSlack_0 : vec2<f32> = vec2<f32>(slack_0) / halfSize_2;
            var _S82 : bool;
            if((abs(across_2.x)) > (1.0f + acrossSlack_0.x))
            {
                _S82 = true;
            }
            else
            {
                _S82 = (abs(across_2.y)) > (1.0f + acrossSlack_0.y);
            }
            if(_S82)
            {
                slot_0 = slot_0 + i32(1);
                continue;
            }
            var _S83 : bool;
            if(behind_2 < (- slack_0))
            {
                _S83 = true;
            }
            else
            {
                _S83 = behind_2 > (depth_7 + slack_0);
            }
            if(_S83)
            {
                slot_0 = slot_0 + i32(1);
                continue;
            }
            var _S84 : vec2<f32> = vec2<f32>(0.5f);
            var atlasAt_0 : vec2<f32> = cardPage_2.xy + (clamp(across_2, vec2<f32>(-1.0f, -1.0f), vec2<f32>(1.0f, 1.0f)) * _S84 + _S84) * cardPage_2.zw - _S84;
            var lowCorner_0 : vec2<f32> = floor(atlasAt_0);
            var _S85 : vec2<f32> = atlasAt_0 - lowCorner_0;
            var _S86 : vec2<f32> = litRect_2.zw / max(cardPage_2.zw, vec2<f32>(9.99999997475242708e-07f, 9.99999997475242708e-07f));
            var _S87 : f32 = behind_2 / max(depth_7, 9.99999997475242708e-07f);
            var _S88 : f32 = bias_1 / max(0.5f * depth_7, 9.99999997475242708e-07f);
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
                var step_2 : vec2<f32> = vec2<f32>(f32((tap_0 & (i32(1)))), f32((tap_0 >> (u32(1)))));
                var _S89 : vec2<f32> = lowCorner_0 + step_2 + _S84;
                var centre_0 : vec2<f32> = _S89 / _S77;
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
                var bilinear2_0 : vec2<f32> = mix(vec2<f32>(1.0f) - _S85, _S85, step_2);
                taps_0[tap_0] = bilinear2_0.x * bilinear2_0.y * written_0 * (1.0f - saturate((abs(_S87 - (textureSampleLevel((uDepth_0), (uDepthSampler_0), (centre_0), (0.0f))).x) - _S88) / (0.25f * _S88)));
                tapLight_0[tap_0] = (textureSampleLevel((uFinal_0), (uFinalSampler_0), ((floor(litRect_2.xy + (_S89 - cardPage_2.xy) * _S86) + _S84) / Globals_0.uAtlasSize_0.xy), (0.0f))).xyz;
                tap_0 = tap_0 + i32(1);
            }
            var coverage_0 : f32 = taps_0.x + taps_0.y + taps_0.z + taps_0.w;
            var cardWeight_0 : f32 = squareness_0 * coverage_0;
            if(cardWeight_0 <= 0.0f)
            {
                slot_0 = slot_0 + i32(1);
                continue;
            }
            var _S90 : vec3<f32> = vec3<f32>(coverage_0);
            var _S91 : vec3<f32> = vec3<f32>(cardWeight_0);
            (*radiance_2) = (*radiance_2) + max((tapLight_0[i32(0)] * vec3<f32>(taps_0.x) + tapLight_0[i32(1)] * vec3<f32>(taps_0.y) + tapLight_0[i32(2)] * vec3<f32>(taps_0.z) + tapLight_0[i32(3)] * vec3<f32>(taps_0.w)) / _S90, _S75) * _S91;
            (*albedo_0) = (*albedo_0) + (tapAlbedo_0[i32(0)] * vec3<f32>(taps_0.x) + tapAlbedo_0[i32(1)] * vec3<f32>(taps_0.y) + tapAlbedo_0[i32(2)] * vec3<f32>(taps_0.z) + tapAlbedo_0[i32(3)] * vec3<f32>(taps_0.w)) / _S90 * _S91;
            weightSum_1 = weightSum_1 + cardWeight_0;
            slot_0 = slot_0 + i32(1);
        }
        walk_0 = walk_0 + i32(1);
        weightSum_0 = weightSum_1;
    }
    if(weightSum_0 <= 0.0f)
    {
        (*radiance_2) = _S75;
        return false;
    }
    (*radiance_2) = (*radiance_2) / vec3<f32>(weightSum_0);
    (*albedo_0) = (*albedo_0) / vec3<f32>(weightSum_0);
    return true;
}

fn SampleCardsWithin_0( world_10 : vec3<f32>,  normalW_1 : vec3<f32>,  bias_2 : f32,  radiance_3 : ptr<function, vec3<f32>>) -> bool
{
    var albedo_1 : vec3<f32>;
    var _S92 : bool = SampleCardsAndAlbedoWithin_0(world_10, normalW_1, bias_2, &((*radiance_3)), &(albedo_1));
    return _S92;
}

fn SurfaceNormalAt_0( hit_1 : vec3<f32>) -> vec3<f32>
{
    var known_3 : bool;
    var raw_3 : f32;
    var resolved_4 : f32;
    var _S93 : f32 = DistanceAt_0(hit_1, &(known_3), &(raw_3), &(resolved_4));
    if(!known_3)
    {
        return vec3<f32>(0.0f, 0.0f, 0.0f);
    }
    var h_0 : f32 = 0.5f * resolved_4;
    var _S94 : vec3<f32> = vec3<f32>(h_0, 0.0f, 0.0f);
    var k_0 : bool;
    var r_0 : f32;
    var v_0 : f32;
    var _S95 : f32 = DistanceAt_0(hit_1 + _S94, &(k_0), &(r_0), &(v_0));
    var _S96 : f32 = DistanceAt_0(hit_1 - _S94, &(k_0), &(r_0), &(v_0));
    var _S97 : f32 = _S95 - _S96;
    var _S98 : vec3<f32> = vec3<f32>(0.0f, h_0, 0.0f);
    var _S99 : f32 = DistanceAt_0(hit_1 + _S98, &(k_0), &(r_0), &(v_0));
    var _S100 : f32 = DistanceAt_0(hit_1 - _S98, &(k_0), &(r_0), &(v_0));
    var _S101 : f32 = _S99 - _S100;
    var _S102 : vec3<f32> = vec3<f32>(0.0f, 0.0f, h_0);
    var _S103 : f32 = DistanceAt_0(hit_1 + _S102, &(k_0), &(r_0), &(v_0));
    var _S104 : f32 = DistanceAt_0(hit_1 - _S102, &(k_0), &(r_0), &(v_0));
    var gradient_0 : vec3<f32> = vec3<f32>(_S97, _S101, _S103 - _S104);
    var length2_1 : f32 = dot(gradient_0, gradient_0);
    var _S105 : vec3<f32>;
    if(length2_1 < 1.00000001335143196e-10f)
    {
        _S105 = vec3<f32>(0.0f, 0.0f, 0.0f);
    }
    else
    {
        _S105 = gradient_0 * vec3<f32>(rsqrt_0(length2_1));
    }
    return _S105;
}

fn ShelterCovers_0( m_6 : ShelterMap_0,  surfaceZ_0 : f32,  worldZ_0 : f32) -> bool
{
    var _S106 : bool;
    if(surfaceZ_0 > (worldZ_0 + m_6.headroom_0))
    {
        _S106 = surfaceZ_0 < (worldZ_0 + m_6.reach_0);
    }
    else
    {
        _S106 = false;
    }
    return _S106;
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
    var _S107 : vec3<i32> = vec3<i32>(at_3, i32(0));
    return ShelterCovers_0(m_7, ShelterSurface_0(m_7, (textureLoad((uRoofs_0), ((_S107)).xy, ((_S107)).z))), worldPoint_0.z);
}

fn LookupCardWithin_0( world_11 : vec3<f32>,  arrivingW_0 : vec3<f32>,  skin_0 : f32,  found_0 : ptr<function, vec2<f32>>,  litFound_0 : ptr<function, vec2<f32>>,  agreement_0 : ptr<function, f32>,  cardDepth_0 : ptr<function, f32>) -> bool
{
    (*cardDepth_0) = 0.0f;
    const _S108 : vec2<f32> = vec2<f32>(0.0f, 0.0f);
    (*found_0) = _S108;
    (*litFound_0) = _S108;
    (*agreement_0) = 1.0e+09f;
    if((Globals_0.uLookupBuckets_0) <= i32(0))
    {
        return false;
    }
    var _S109 : vec2<i32> = uCells_0[LookupBucket_0(vec3<i32>(floor((world_11 - Globals_0.uLookupMin_0) / vec3<f32>(Globals_0.uLookupCell_0))))];
    var albedoWidth_1 : u32;
    var albedoHeight_1 : u32;
    {var dim = textureDimensions((uAlbedo_0));((albedoWidth_1)) = dim.x;((albedoHeight_1)) = dim.y;};
    var _S110 : vec2<f32> = vec2<f32>(f32(albedoWidth_1), f32(albedoHeight_1));
    var _S111 : vec2<i32> = uCells_0[Globals_0.uLookupBuckets_0];
    var settled_0 : bool = false;
    var anyFound_0 : bool = false;
    var walk_1 : i32 = i32(0);
    for(;;)
    {
        var _S112 : bool;
        if(walk_1 <= (_S111.y))
        {
            _S112 = !settled_0;
        }
        else
        {
            _S112 = false;
        }
        if(_S112)
        {
        }
        else
        {
            break;
        }
        var _S113 : bool = walk_1 == i32(0);
        var _S114 : vec2<i32>;
        if(_S113)
        {
            _S114 = _S109;
        }
        else
        {
            _S114 = MoverPages_0(_S111.x, walk_1 - i32(1), world_11, 0.0f);
        }
        var settled_1 : bool = settled_0;
        var anyFound_1 : bool = anyFound_0;
        var slot_1 : i32 = i32(0);
        for(;;)
        {
            var _S115 : bool;
            if(slot_1 < (_S114.y))
            {
                _S115 = !settled_1;
            }
            else
            {
                _S115 = false;
            }
            if(_S115)
            {
            }
            else
            {
                break;
            }
            var page_3 : i32 = uCellPages_0[_S114.x + slot_1];
            var _S116 : i32 = uCellPages_0[_S114.x + slot_1] * i32(10);
            if(((uPages_0[_S116 + i32(5)].w) > 0.5f) == _S113)
            {
                slot_1 = slot_1 + i32(1);
                continue;
            }
            var bound_1 : vec4<f32> = uPages_0[_S116 + i32(9)];
            var toCentre_2 : vec3<f32> = world_11 - bound_1.xyz;
            var _S117 : f32 = bound_1.w;
            if((dot(toCentre_2, toCentre_2)) > (_S117 * _S117))
            {
                slot_1 = slot_1 + i32(1);
                continue;
            }
            var across_3 : vec2<f32>;
            var halfSize_3 : vec2<f32>;
            var behind_3 : f32;
            var depth_8 : f32;
            var facing_4 : vec3<f32>;
            var arrivingLocal_0 : vec3<f32>;
            var litRect_3 : vec4<f32>;
            var cardPage_3 : vec4<f32>;
            ProjectOntoCard_0(page_3, world_11, (vec3<f32>(0) - arrivingW_0), &(across_3), &(halfSize_3), &(behind_3), &(depth_8), &(facing_4), &(arrivingLocal_0), &(litRect_3), &(cardPage_3));
            var _S118 : bool;
            if((abs(across_3.x)) > 1.0f)
            {
                _S118 = true;
            }
            else
            {
                _S118 = (abs(across_3.y)) > 1.0f;
            }
            if(_S118)
            {
                slot_1 = slot_1 + i32(1);
                continue;
            }
            var _S119 : bool;
            if((dot(arrivingW_0, arrivingW_0)) > 0.5f)
            {
                _S119 = (dot(facing_4, arrivingLocal_0)) < -0.05000000074505806f;
            }
            else
            {
                _S119 = false;
            }
            if(_S119)
            {
                slot_1 = slot_1 + i32(1);
                continue;
            }
            var _S120 : bool;
            if(behind_3 < (- skin_0))
            {
                _S120 = true;
            }
            else
            {
                _S120 = behind_3 > (depth_8 + skin_0);
            }
            if(_S120)
            {
                slot_1 = slot_1 + i32(1);
                continue;
            }
            var _S121 : vec2<f32> = vec2<f32>(0.5f);
            var uv_7 : vec2<f32> = across_3 * _S121 + _S121;
            var texel_3 : vec2<f32> = (cardPage_3.xy + uv_7 * cardPage_3.zw) / _S110;
            if(((textureSampleLevel((uAlbedo_0), (uAlbedoSampler_0), (texel_3), (0.0f))).w) < 0.5f)
            {
                slot_1 = slot_1 + i32(1);
                continue;
            }
            var error_0 : f32 = abs((textureSampleLevel((uDepth_0), (uDepthSampler_0), (texel_3), (0.0f))).x * depth_8 - behind_3);
            var settled_2 : bool;
            var anyFound_2 : bool;
            if(error_0 < (*agreement_0))
            {
                (*agreement_0) = error_0;
                (*cardDepth_0) = depth_8;
                (*found_0) = texel_3;
                var _S122 : bool = error_0 < 0.01999999955296516f;
                (*litFound_0) = (litRect_3.xy + uv_7 * litRect_3.zw) / Globals_0.uAtlasSize_0.xy;
                settled_2 = _S122;
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
        var _S123 : i32 = walk_1 + i32(1);
        settled_0 = settled_1;
        anyFound_0 = anyFound_1;
        walk_1 = _S123;
    }
    return anyFound_0;
}

fn LookupCardWithin_1( world_12 : vec3<f32>,  arrivingW_1 : vec3<f32>,  skin_1 : f32,  found_1 : ptr<function, vec2<f32>>,  litFound_1 : ptr<function, vec2<f32>>,  agreement_1 : ptr<function, f32>,  cardDepth_1 : ptr<function, f32>) -> bool
{
    (*cardDepth_1) = 0.0f;
    const _S124 : vec2<f32> = vec2<f32>(0.0f, 0.0f);
    (*found_1) = _S124;
    (*litFound_1) = _S124;
    (*agreement_1) = 1.0e+09f;
    if((Globals_0.uLookupBuckets_0) <= i32(0))
    {
        return false;
    }
    var _S125 : vec2<i32> = uCells_0[LookupBucket_0(vec3<i32>(floor((world_12 - Globals_0.uLookupMin_0) / vec3<f32>(Globals_0.uLookupCell_0))))];
    var albedoWidth_2 : u32;
    var albedoHeight_2 : u32;
    {var dim = textureDimensions((uAlbedo_0));((albedoWidth_2)) = dim.x;((albedoHeight_2)) = dim.y;};
    var _S126 : vec2<f32> = vec2<f32>(f32(albedoWidth_2), f32(albedoHeight_2));
    var _S127 : vec2<i32> = uCells_0[Globals_0.uLookupBuckets_0];
    var settled_3 : bool = false;
    var anyFound_3 : bool = false;
    var walk_2 : i32 = i32(0);
    for(;;)
    {
        var _S128 : bool;
        if(walk_2 <= (_S127.y))
        {
            _S128 = !settled_3;
        }
        else
        {
            _S128 = false;
        }
        if(_S128)
        {
        }
        else
        {
            break;
        }
        var _S129 : bool = walk_2 == i32(0);
        var _S130 : vec2<i32>;
        if(_S129)
        {
            _S130 = _S125;
        }
        else
        {
            _S130 = MoverPages_0(_S127.x, walk_2 - i32(1), world_12, 0.0f);
        }
        var settled_4 : bool = settled_3;
        var anyFound_4 : bool = anyFound_3;
        var slot_2 : i32 = i32(0);
        for(;;)
        {
            var _S131 : bool;
            if(slot_2 < (_S130.y))
            {
                _S131 = !settled_4;
            }
            else
            {
                _S131 = false;
            }
            if(_S131)
            {
            }
            else
            {
                break;
            }
            var page_4 : i32 = uCellPages_0[_S130.x + slot_2];
            var _S132 : i32 = uCellPages_0[_S130.x + slot_2] * i32(10);
            if(((uPages_0[_S132 + i32(5)].w) > 0.5f) == _S129)
            {
                slot_2 = slot_2 + i32(1);
                continue;
            }
            var bound_2 : vec4<f32> = uPages_0[_S132 + i32(9)];
            var toCentre_3 : vec3<f32> = world_12 - bound_2.xyz;
            var _S133 : f32 = bound_2.w;
            if((dot(toCentre_3, toCentre_3)) > (_S133 * _S133))
            {
                slot_2 = slot_2 + i32(1);
                continue;
            }
            var across_4 : vec2<f32>;
            var halfSize_4 : vec2<f32>;
            var behind_4 : f32;
            var depth_9 : f32;
            var facing_5 : vec3<f32>;
            var arrivingLocal_1 : vec3<f32>;
            var litRect_4 : vec4<f32>;
            var cardPage_4 : vec4<f32>;
            ProjectOntoCard_1(page_4, world_12, (vec3<f32>(0) - arrivingW_1), &(across_4), &(halfSize_4), &(behind_4), &(depth_9), &(facing_5), &(arrivingLocal_1), &(litRect_4), &(cardPage_4));
            var _S134 : bool;
            if((abs(across_4.x)) > 1.0f)
            {
                _S134 = true;
            }
            else
            {
                _S134 = (abs(across_4.y)) > 1.0f;
            }
            if(_S134)
            {
                slot_2 = slot_2 + i32(1);
                continue;
            }
            var _S135 : bool;
            if((dot(arrivingW_1, arrivingW_1)) > 0.5f)
            {
                _S135 = (dot(facing_5, arrivingLocal_1)) < -0.05000000074505806f;
            }
            else
            {
                _S135 = false;
            }
            if(_S135)
            {
                slot_2 = slot_2 + i32(1);
                continue;
            }
            var _S136 : bool;
            if(behind_4 < (- skin_1))
            {
                _S136 = true;
            }
            else
            {
                _S136 = behind_4 > (depth_9 + skin_1);
            }
            if(_S136)
            {
                slot_2 = slot_2 + i32(1);
                continue;
            }
            var _S137 : vec2<f32> = vec2<f32>(0.5f);
            var uv_8 : vec2<f32> = across_4 * _S137 + _S137;
            var texel_4 : vec2<f32> = (cardPage_4.xy + uv_8 * cardPage_4.zw) / _S126;
            if(((textureSampleLevel((uAlbedo_0), (uAlbedoSampler_0), (texel_4), (0.0f))).w) < 0.5f)
            {
                slot_2 = slot_2 + i32(1);
                continue;
            }
            var error_1 : f32 = abs((textureSampleLevel((uDepth_0), (uDepthSampler_0), (texel_4), (0.0f))).x * depth_9 - behind_4);
            var settled_5 : bool;
            var anyFound_5 : bool;
            if(error_1 < (*agreement_1))
            {
                (*agreement_1) = error_1;
                (*cardDepth_1) = depth_9;
                (*found_1) = texel_4;
                var _S138 : bool = error_1 < 0.01999999955296516f;
                (*litFound_1) = (litRect_4.xy + uv_8 * litRect_4.zw) / Globals_0.uAtlasSize_0.xy;
                settled_5 = _S138;
                anyFound_5 = true;
            }
            else
            {
                settled_5 = settled_4;
                anyFound_5 = anyFound_4;
            }
            settled_4 = settled_5;
            anyFound_4 = anyFound_5;
            slot_2 = slot_2 + i32(1);
        }
        var _S139 : i32 = walk_2 + i32(1);
        settled_3 = settled_4;
        anyFound_3 = anyFound_4;
        walk_2 = _S139;
    }
    return anyFound_3;
}

fn LookupCardHit_0( world_13 : vec3<f32>,  rayDirection_0 : vec3<f32>,  found_2 : ptr<function, vec2<f32>>,  litFound_2 : ptr<function, vec2<f32>>,  agreement_2 : ptr<function, f32>) -> bool
{
    var depth_10 : f32;
    var _S140 : bool = LookupCardWithin_0(world_13, rayDirection_0, 0.25f, &((*found_2)), &((*litFound_2)), &((*agreement_2)), &(depth_10));
    return _S140;
}

fn LookupCardHit_1( world_14 : vec3<f32>,  rayDirection_1 : vec3<f32>,  found_3 : ptr<function, vec2<f32>>,  litFound_3 : ptr<function, vec2<f32>>,  agreement_3 : ptr<function, f32>) -> bool
{
    var depth_11 : f32;
    var _S141 : bool = LookupCardWithin_1(world_14, rayDirection_1, 0.25f, &((*found_3)), &((*litFound_3)), &((*agreement_3)), &(depth_11));
    return _S141;
}

fn SkyRadiance_0( direction_4 : vec3<f32>) -> vec3<f32>
{
    if((Globals_0.uSkyShValid_0) == i32(0))
    {
        return Globals_0.uSkyColour_0;
    }
    var n_1 : vec4<f32> = vec4<f32>(direction_4, 1.0f);
    var q_0 : vec4<f32> = n_1.xyzz * n_1.yzzx;
    var _S142 : f32 = direction_4.x;
    var _S143 : f32 = direction_4.y;
    return max(vec3<f32>(0.0f, 0.0f, 0.0f), vec3<f32>(dot(Globals_0.uSkySh_0[i32(0)], n_1), dot(Globals_0.uSkySh_0[i32(1)], n_1), dot(Globals_0.uSkySh_0[i32(2)], n_1)) + vec3<f32>(dot(Globals_0.uSkySh_0[i32(3)], q_0), dot(Globals_0.uSkySh_0[i32(4)], q_0), dot(Globals_0.uSkySh_0[i32(5)], q_0)) + Globals_0.uSkySh_0[i32(6)].xyz * vec3<f32>((_S142 * _S142 - _S143 * _S143)));
}

struct GatherRay_0
{
     radiance_4 : vec3<f32>,
     weight_1 : f32,
     surface_0 : vec3<f32>,
     surfaceWeight_0 : f32,
     open_0 : vec3<f32>,
     unplaced_1 : f32,
     hasSurface_0 : bool,
     hasOpen_0 : bool,
     hasUnplaced_0 : bool,
};

fn GatherOneRay_0( probe_3 : GatherProbe_0,  world_15 : vec3<f32>,  normal_5 : vec3<f32>,  viewOrigin_2 : vec3<f32>,  jitter_0 : vec2<f32>,  ray_0 : i32) -> GatherRay_0
{
    var result_0 : GatherRay_0;
    const _S144 : vec3<f32> = vec3<f32>(0.0f, 0.0f, 0.0f);
    result_0.radiance_4 = _S144;
    result_0.weight_1 = 0.0f;
    result_0.surface_0 = _S144;
    result_0.surfaceWeight_0 = 0.0f;
    result_0.open_0 = _S144;
    result_0.unplaced_1 = 0.0f;
    result_0.hasSurface_0 = false;
    result_0.hasOpen_0 = false;
    result_0.hasUnplaced_0 = false;
    var probeUv_0 : vec2<f32> = (vec2<f32>(f32(ray_0 % i32(4)), f32(ray_0 / i32(4))) + jitter_0) / vec2<f32>(4.0f);
    var phi_0 : f32 = 6.28318548202514648f * probeUv_0.x;
    var cosTheta_0 : f32 = sqrt(probeUv_0.y);
    var sinTheta_0 : f32 = sqrt(max(0.0f, 1.0f - cosTheta_0 * cosTheta_0));
    var direction_5 : vec3<f32> = Orient_0(probe_3.basis_1, vec3<f32>(sinTheta_0 * cos(phi_0), sinTheta_0 * sin(phi_0), cosTheta_0));
    var source_0 : i32;
    var radiance_5 : vec3<f32>;
    var resolved_5 : bool;
    if((Globals_0.uScreenTraceSteps_0) > i32(0))
    {
        var screenRadiance_0 : vec3<f32>;
        var _S145 : bool = ScreenTraceRadiance_0(viewOrigin_2, probe_3.viewNormal_0, ToView_0(direction_5), &(screenRadiance_0));
        if(_S145)
        {
            source_0 = i32(1);
            radiance_5 = screenRadiance_0;
        }
        else
        {
            source_0 = i32(0);
            radiance_5 = _S144;
        }
        resolved_5 = _S145;
    }
    else
    {
        resolved_5 = false;
        source_0 = i32(0);
        radiance_5 = _S144;
    }
    var weight_2 : f32;
    if(!resolved_5)
    {
        var hit_2 : vec3<f32>;
        var _S146 : f32 = VisibilityAt_0(probe_3.traceFrom_0, normal_5, direction_5, &(hit_2));
        var fieldHit_0 : bool = _S146 <= 0.5f;
        var terrainFrom_0 : vec3<f32> = world_15 + normal_5 * vec3<f32>(0.10000000149011612f);
        var terrainT_0 : f32 = TerrainHitDistance_0(terrainFrom_0 + Globals_0.uTraceOrigin_0, direction_5, 32.0f);
        var fieldHit_1 : bool;
        if(terrainT_0 >= 0.0f)
        {
            if(!fieldHit_0)
            {
                fieldHit_1 = true;
            }
            else
            {
                fieldHit_1 = (length(hit_2 - world_15)) > terrainT_0;
            }
        }
        else
        {
            fieldHit_1 = false;
        }
        if(fieldHit_1)
        {
            var terrainLight_0 : vec3<f32>;
            var _S147 : bool = SampleCardsWithin_0(terrainFrom_0 + direction_5 * vec3<f32>(terrainT_0), vec3<f32>(0.0f, 0.0f, 1.0f), 0.5f * Globals_0.uClipmapOrigin_0[i32(0)].w, &(terrainLight_0));
            if(_S147)
            {
                var radiance_6 : vec3<f32> = terrainLight_0 * vec3<f32>(Globals_0.uBounceScale_0);
                result_0.surface_0 = CapRayIntensity_0(radiance_6);
                result_0.surfaceWeight_0 = 1.0f;
                result_0.hasSurface_0 = true;
                resolved_5 = true;
                source_0 = i32(2);
                radiance_5 = radiance_6;
            }
            fieldHit_1 = false;
        }
        else
        {
            fieldHit_1 = fieldHit_0;
        }
        if(resolved_5)
        {
            weight_2 = 1.0f;
        }
        else
        {
            if(fieldHit_1)
            {
                var found_4 : vec2<f32>;
                var litFound_4 : vec2<f32>;
                var agreement_4 : f32;
                var finest_0 : f32 = Globals_0.uClipmapOrigin_0[i32(0)].w;
                var _S148 : f32 = 1.5f * finest_0;
                var _S149 : f32 = smoothstep(_S148, 2.0f * finest_0, length(hit_2 - world_15)) * smoothstep(0.75f * finest_0, _S148, dot(hit_2 - world_15, normal_5));
                var hitNormal_1 : vec3<f32> = SurfaceNormalAt_0(hit_2);
                if(!UnderRoof_0(world_15 + Globals_0.uTraceOrigin_0))
                {
                    resolved_5 = UnderRoof_0(hit_2 + Globals_0.uTraceOrigin_0);
                }
                else
                {
                    resolved_5 = false;
                }
                var near_1 : f32;
                if(resolved_5)
                {
                    result_0.unplaced_1 = result_0.unplaced_1 + 1.0f;
                    result_0.hasUnplaced_0 = true;
                    near_1 = 0.0f;
                }
                else
                {
                    near_1 = _S149;
                }
                var hitNormal_2 : vec3<f32>;
                if((dot(hitNormal_1, direction_5)) > 0.0f)
                {
                    hitNormal_2 = (vec3<f32>(0) - hitNormal_1);
                }
                else
                {
                    hitNormal_2 = hitNormal_1;
                }
                var filtered_0 : vec3<f32>;
                if(near_1 <= 0.0f)
                {
                    weight_2 = 0.0f;
                }
                else
                {
                    if((dot(hitNormal_2, hitNormal_2)) > 0.5f)
                    {
                        var _S150 : bool = SampleCardsWithin_0(hit_2, hitNormal_2, 0.5f * finest_0, &(filtered_0));
                        resolved_5 = _S150;
                    }
                    else
                    {
                        resolved_5 = false;
                    }
                    if(resolved_5)
                    {
                        var radiance_7 : vec3<f32> = filtered_0 * vec3<f32>(Globals_0.uBounceScale_0);
                        result_0.surface_0 = CapRayIntensity_0(radiance_7);
                        result_0.surfaceWeight_0 = near_1;
                        result_0.hasSurface_0 = true;
                        source_0 = i32(2);
                        radiance_5 = radiance_7;
                        weight_2 = near_1;
                    }
                    else
                    {
                        var _S151 : bool = LookupCardHit_0(hit_2, direction_5, &(found_4), &(litFound_4), &(agreement_4));
                        if(_S151)
                        {
                            var radiance_8 : vec3<f32> = (textureSampleLevel((uFinal_0), (uFinalSampler_0), (litFound_4), (0.0f))).xyz * vec3<f32>(Globals_0.uBounceScale_0);
                            result_0.surface_0 = CapRayIntensity_0(radiance_8);
                            result_0.surfaceWeight_0 = near_1;
                            result_0.hasSurface_0 = true;
                            source_0 = i32(2);
                            radiance_5 = radiance_8;
                            weight_2 = near_1;
                        }
                        else
                        {
                            result_0.unplaced_1 = result_0.unplaced_1 + near_1;
                            result_0.hasUnplaced_0 = true;
                            weight_2 = 0.0f;
                        }
                    }
                }
            }
            else
            {
                if((direction_5.z) >= 0.0f)
                {
                    radiance_5 = SkyRadiance_0(direction_5);
                }
                else
                {
                    radiance_5 = vec3<f32>(0.25f) * (SkyRadiance_0(vec3<f32>(0.0f, 0.0f, 1.0f)) + Globals_0.uGroundSun_0);
                }
                result_0.open_0 = CapRayIntensity_0(radiance_5);
                result_0.hasOpen_0 = true;
                source_0 = i32(3);
                weight_2 = 1.0f;
            }
        }
    }
    else
    {
        weight_2 = 1.0f;
    }
    if((Globals_0.uDebugSource_0) != i32(0))
    {
        resolved_5 = source_0 != (Globals_0.uDebugSource_0);
    }
    else
    {
        resolved_5 = false;
    }
    if(resolved_5)
    {
        radiance_5 = _S144;
    }
    result_0.radiance_4 = CapRayIntensity_0(radiance_5);
    result_0.weight_1 = weight_2;
    return result_0;
}

fn AddRay_0( sums_1 : ptr<function, GatherSums_0>,  r_1 : GatherRay_0)
{
    if(r_1.hasSurface_0)
    {
        (*sums_1).surfaceSum_0 = (*sums_1).surfaceSum_0 + r_1.surface_0 * vec3<f32>(r_1.surfaceWeight_0);
        (*sums_1).surfaceCount_0 = (*sums_1).surfaceCount_0 + r_1.surfaceWeight_0;
    }
    if(r_1.hasUnplaced_0)
    {
        (*sums_1).unplaced_0 = (*sums_1).unplaced_0 + r_1.unplaced_1;
    }
    if(r_1.hasOpen_0)
    {
        (*sums_1).openSum_0 = (*sums_1).openSum_0 + r_1.open_0;
        (*sums_1).openCount_0 = (*sums_1).openCount_0 + 1.0f;
    }
    (*sums_1).irradiance_0 = (*sums_1).irradiance_0 + r_1.radiance_4 * vec3<f32>(r_1.weight_1);
    (*sums_1).counted_0 = (*sums_1).counted_0 + r_1.weight_1;
    return;
}

fn FinishGather_0( sums_2 : GatherSums_0) -> vec3<f32>
{
    var _S152 : bool;
    if((sums_2.unplaced_0) > 0.0f)
    {
        if((Globals_0.uDebugSource_0) == i32(0))
        {
            _S152 = true;
        }
        else
        {
            _S152 = (Globals_0.uDebugSource_0) == i32(2);
        }
    }
    else
    {
        _S152 = false;
    }
    var irradiance_1 : vec3<f32>;
    var counted_1 : f32;
    if(_S152)
    {
        if((sums_2.surfaceCount_0) > 0.0f)
        {
            irradiance_1 = sums_2.surfaceSum_0 / vec3<f32>(sums_2.surfaceCount_0);
        }
        else
        {
            if((sums_2.openCount_0) > 0.0f)
            {
                irradiance_1 = sums_2.openSum_0 / vec3<f32>(sums_2.openCount_0);
            }
            else
            {
                irradiance_1 = SkyRadiance_0(vec3<f32>(0.0f, 0.0f, 1.0f));
            }
            irradiance_1 = irradiance_1 * vec3<f32>(0.25f);
        }
        var irradiance_2 : vec3<f32> = sums_2.irradiance_0 + irradiance_1 * vec3<f32>(sums_2.unplaced_0);
        counted_1 = sums_2.counted_0 + sums_2.unplaced_0;
        irradiance_1 = irradiance_2;
    }
    else
    {
        counted_1 = sums_2.counted_0;
        irradiance_1 = sums_2.irradiance_0;
    }
    return max(irradiance_1 * vec3<f32>((3.14159274101257324f / max(counted_1, 4.0f))), vec3<f32>(0.0f, 0.0f, 0.0f));
}

fn GatherIndirect_0( world_16 : vec3<f32>,  normal_6 : vec3<f32>,  viewOrigin_3 : vec3<f32>,  jitter_1 : vec2<f32>) -> vec3<f32>
{
    var probe_4 : GatherProbe_0;
    var _S153 : bool = BeginGather_0(world_16, normal_6, viewOrigin_3, &(probe_4));
    if(!_S153)
    {
        return vec3<f32>(0.0f, 0.0f, 0.0f);
    }
    var sums_3 : GatherSums_0 = BeginSums_0();
    var ray_1 : i32 = i32(0);
    for(;;)
    {
        if(ray_1 < i32(16))
        {
        }
        else
        {
            break;
        }
        var _S154 : GatherRay_0 = GatherOneRay_0(probe_4, world_16, normal_6, viewOrigin_3, jitter_1, ray_1);
        AddRay_0(&(sums_3), _S154);
        ray_1 = ray_1 + i32(1);
    }
    return FinishGather_0(sums_3);
}

@compute
@workgroup_size(8, 8, 1)
fn computeMain(@builtin(global_invocation_id) id_0 : vec3<u32>, @builtin(local_invocation_id) localId_0 : vec3<u32>)
{
    var _S155 : bool;
    var _S156 : bool;
    var nMin_0 : vec3<f32>;
    var nMax_0 : vec3<f32>;
    var dx_0 : i32;
    var nCount_0 : f32;
    var nCount_1 : f32;
    var _S157 : vec2<i32> = vec2<i32>(id_0.xy);
    var grid_0 : vec2<i32> = (vec2<i32>(Globals_0.uScreenWidth_0, Globals_0.uScreenHeight_0) + vec2<i32>((Globals_0.uProbeStep_0 - i32(1)))) / vec2<i32>(Globals_0.uProbeStep_0);
    var ownMotion_0 : bool;
    if((_S157.x) >= (grid_0.x))
    {
        ownMotion_0 = true;
    }
    else
    {
        ownMotion_0 = (_S157.y) >= (grid_0.y);
    }
    if(ownMotion_0)
    {
        return;
    }
    var world_17 : vec3<f32>;
    var viewPosition_4 : vec3<f32>;
    var worldNormal_1 : vec3<f32>;
    var onSurface_0 : bool = ProbeSurface_0(_S157, &(world_17), &(viewPosition_4), &(worldNormal_1));
    var traced_0 : vec3<f32>;
    var nCount_2 : f32;
    if((Globals_0.uGatherPass_0) == i32(0))
    {
        const _S158 : vec3<f32> = vec3<f32>(0.0f, 0.0f, 0.0f);
        if(onSurface_0)
        {
            var _S159 : vec3<f32> = GatherIndirect_0(world_17, worldNormal_1, viewPosition_4, FrameJitter_0(_S157, Globals_0.uFrameIndex_0));
            if((Globals_0.uDebugSource_0) == i32(4))
            {
                var found_5 : vec2<f32>;
                var litFound_5 : vec2<f32>;
                var agreement_5 : f32;
                var _S160 : bool = LookupCardHit_1(world_17, (vec3<f32>(0) - worldNormal_1), &(found_5), &(litFound_5), &(agreement_5));
                if(_S160)
                {
                    traced_0 = (textureSampleLevel((uFinal_0), (uFinalSampler_0), (litFound_5), (0.0f))).xyz;
                }
                else
                {
                    traced_0 = _S158;
                }
            }
            else
            {
                traced_0 = _S159;
            }
        }
        else
        {
            traced_0 = _S158;
        }
        var _S161 : vec2<u32> = vec2<u32>(_S157);
        if(onSurface_0)
        {
            nCount_2 = 1.0f;
        }
        else
        {
            nCount_2 = 0.0f;
        }
        textureStore((uIndirect_0), (_S161), (vec4<f32>(traced_0, nCount_2)));
        var _S162 : vec4<f32>;
        if(onSurface_0)
        {
            _S162 = vec4<f32>(world_17 - Globals_0.uCameraToCache_0, 1.0f);
        }
        else
        {
            _S162 = vec4<f32>(0.0f, 0.0f, 0.0f, 0.0f);
        }
        textureStore((uPosition_0), (_S161), (_S162));
        return;
    }
    if(!onSurface_0)
    {
        var _S163 : vec2<u32> = vec2<u32>(_S157);
        const _S164 : vec4<f32> = vec4<f32>(0.0f, 0.0f, 0.0f, 0.0f);
        textureStore((uIndirect_0), (_S163), (_S164));
        textureStore((uPosition_0), (_S163), (_S164));
        return;
    }
    var _S165 : vec3<i32> = vec3<i32>(_S157, i32(0));
    var current_0 : vec3<f32> = (textureLoad((uRaw_0), ((_S165)).xy, ((_S165)).z)).xyz;
    var _S166 : f32 = max(0.34999999403953552f, 0.05999999865889549f * length(viewPosition_4));
    traced_0 = current_0;
    var nMax_1 : vec3<f32> = current_0;
    var dy_0 : i32 = i32(-1);
    var nSum_0 : vec3<f32> = current_0;
    nCount_2 = 1.0f;
    for(;;)
    {
        if(dy_0 <= i32(1))
        {
        }
        else
        {
            break;
        }
        nMin_0 = traced_0;
        nMax_0 = nMax_1;
        dx_0 = i32(-1);
        var nSum_1 : vec3<f32> = nSum_0;
        nCount_0 = nCount_2;
        for(;;)
        {
            if(dx_0 <= i32(1))
            {
            }
            else
            {
                break;
            }
            if(dx_0 == i32(0))
            {
                ownMotion_0 = dy_0 == i32(0);
            }
            else
            {
                ownMotion_0 = false;
            }
            if(ownMotion_0)
            {
                dx_0 = dx_0 + i32(1);
                continue;
            }
            var n_2 : vec2<i32> = _S157 + vec2<i32>(dx_0, dy_0);
            if((any((n_2 < vec2<i32>(i32(0), i32(0))))))
            {
                _S155 = true;
            }
            else
            {
                _S155 = (any((n_2 >= grid_0)));
            }
            if(_S155)
            {
                dx_0 = dx_0 + i32(1);
                continue;
            }
            var _S167 : vec3<i32> = vec3<i32>(n_2, i32(0));
            var raw_4 : vec4<f32> = (textureLoad((uRaw_0), ((_S167)).xy, ((_S167)).z));
            var nWorld_0 : vec3<f32>;
            var nView_0 : vec3<f32>;
            var nNormal_0 : vec3<f32>;
            if((raw_4.w) > 0.5f)
            {
                var _S168 : bool = ProbeSurface_0(n_2, &(nWorld_0), &(nView_0), &(nNormal_0));
                _S156 = _S168;
            }
            else
            {
                _S156 = false;
            }
            var _S169 : bool;
            if(_S156)
            {
                _S169 = (distance(nWorld_0, world_17)) < _S166;
            }
            else
            {
                _S169 = false;
            }
            var _S170 : bool;
            if(_S169)
            {
                _S170 = (dot(nNormal_0, worldNormal_1)) > 0.80000001192092896f;
            }
            else
            {
                _S170 = false;
            }
            var nMin_1 : vec3<f32>;
            var nMax_2 : vec3<f32>;
            var nSum_2 : vec3<f32>;
            if(_S170)
            {
                var _S171 : vec3<f32> = raw_4.xyz;
                var _S172 : vec3<f32> = max(nMax_0, _S171);
                var nSum_3 : vec3<f32> = nSum_1 + _S171;
                var nCount_3 : f32 = nCount_0 + 1.0f;
                nMin_1 = min(nMin_0, _S171);
                nMax_2 = _S172;
                nSum_2 = nSum_3;
                nCount_1 = nCount_3;
            }
            else
            {
                nMin_1 = nMin_0;
                nMax_2 = nMax_0;
                nSum_2 = nSum_1;
                nCount_1 = nCount_0;
            }
            nMin_0 = nMin_1;
            nMax_0 = nMax_2;
            nSum_1 = nSum_2;
            nCount_0 = nCount_1;
            dx_0 = dx_0 + i32(1);
        }
        var _S173 : i32 = dy_0 + i32(1);
        traced_0 = nMin_0;
        nMax_1 = nMax_0;
        dy_0 = _S173;
        nSum_0 = nSum_1;
        nCount_2 = nCount_0;
    }
    var est_0 : vec3<f32> = nSum_0 / vec3<f32>(nCount_2);
    var frames_0 : f32;
    if((Globals_0.uHasHistory_0) != i32(0))
    {
        var prevView_1 : vec3<f32> = Globals_0.uPrevWorldToView0_0 * vec3<f32>((world_17.x - Globals_0.uPrevCameraToCache_0.x)) + Globals_0.uPrevWorldToView1_0 * vec3<f32>((world_17.y - Globals_0.uPrevCameraToCache_0.y)) + Globals_0.uPrevWorldToView2_0 * vec3<f32>((world_17.z - Globals_0.uPrevCameraToCache_0.z));
        var _S174 : f32 = prevView_1.z;
        if(_S174 > 0.0f)
        {
            var _S175 : vec2<f32> = vec2<f32>(0.5f);
            var prevUv_0 : vec2<f32> = vec2<f32>(prevView_1.x * Globals_0.uPrevUnproject_0.x / _S174, -1.0f * (prevView_1.y * Globals_0.uPrevUnproject_0.y) / _S174) * _S175 + _S175;
            var prevUv_1 : vec2<f32>;
            if((Globals_0.uHasVelocity_0) != i32(0))
            {
                var screen_0 : vec2<f32> = vec2<f32>(f32(Globals_0.uScreenWidth_0), f32(Globals_0.uScreenHeight_0));
                var probeUv_1 : vec2<f32> = (vec2<f32>(min(_S157 * vec2<i32>(Globals_0.uProbeStep_0) + ProbeTileOffset_0(Globals_0.uFrameIndex_0, Globals_0.uProbeStep_0), vec2<i32>(Globals_0.uScreenWidth_0, Globals_0.uScreenHeight_0) - vec2<i32>(i32(1)))) + _S175) / screen_0;
                var motion_0 : vec2<f32> = (textureSampleLevel((uVelocity_0), (uVelocitySampler_0), (probeUv_1), (0.0f))).xy;
                var apart_0 : vec2<f32> = (motion_0 - (probeUv_1 - prevUv_0)) * screen_0;
                var _S176 : bool = (dot(apart_0, apart_0)) > 2.25f;
                if(_S176)
                {
                    prevUv_1 = probeUv_1 - motion_0;
                }
                else
                {
                    prevUv_1 = prevUv_0;
                }
                ownMotion_0 = _S176;
            }
            else
            {
                prevUv_1 = prevUv_0;
                ownMotion_0 = false;
            }
            if((all((prevUv_1 >= vec2<f32>(0.0f, 0.0f)))))
            {
                _S155 = (all((prevUv_1 <= vec2<f32>(1.0f, 1.0f))));
            }
            else
            {
                _S155 = false;
            }
            if(_S155)
            {
                var tc_0 : vec2<f32> = (prevUv_1 * vec2<f32>(f32(Globals_0.uScreenWidth_0), f32(Globals_0.uScreenHeight_0)) - vec2<f32>(ProbeTileOffset_0(Globals_0.uFrameIndex_0 - i32(1), Globals_0.uProbeStep_0)) - _S175) / vec2<f32>(f32(Globals_0.uProbeStep_0));
                var tcBase_0 : vec2<f32> = floor(tc_0);
                var _S177 : vec2<f32> = tc_0 - tcBase_0;
                var _S178 : f32 = 2.0f * viewPosition_4.z * f32(Globals_0.uProbeStep_0) / (Globals_0.uUnproject_0.y * f32(Globals_0.uScreenHeight_0));
                const _S179 : vec3<f32> = vec3<f32>(0.0f, 0.0f, 0.0f);
                dx_0 = i32(0);
                nMin_0 = _S179;
                nCount_0 = 0.0f;
                nCount_1 = 0.0f;
                for(;;)
                {
                    if(dx_0 < i32(4))
                    {
                    }
                    else
                    {
                        break;
                    }
                    var _S180 : i32 = (dx_0 & (i32(1)));
                    var _S181 : i32 = (dx_0 >> (u32(1)));
                    var tap_1 : vec2<i32> = vec2<i32>(tcBase_0) + vec2<i32>(_S180, _S181);
                    if((any((tap_1 < vec2<i32>(i32(0), i32(0))))))
                    {
                        _S155 = true;
                    }
                    else
                    {
                        _S155 = (any((tap_1 >= grid_0)));
                    }
                    if(_S155)
                    {
                        dx_0 = dx_0 + i32(1);
                        continue;
                    }
                    var _S182 : vec3<i32> = vec3<i32>(tap_1, i32(0));
                    var tapPos_0 : vec4<f32> = (textureLoad((uPositionHistory_0), ((_S182)).xy, ((_S182)).z));
                    if((tapPos_0.w) <= 0.0f)
                    {
                        dx_0 = dx_0 + i32(1);
                        continue;
                    }
                    var planeDistance_0 : f32 = abs(dot(tapPos_0.xyz + Globals_0.uPrevCameraToCache_0 - world_17, worldNormal_1));
                    if(!ownMotion_0)
                    {
                        _S156 = planeDistance_0 > (max(0.05000000074505806f, 0.5f * _S178));
                    }
                    else
                    {
                        _S156 = false;
                    }
                    if(_S156)
                    {
                        dx_0 = dx_0 + i32(1);
                        continue;
                    }
                    if(_S180 == i32(0))
                    {
                        nCount_2 = 1.0f - _S177.x;
                    }
                    else
                    {
                        nCount_2 = _S177.x;
                    }
                    if(_S181 == i32(0))
                    {
                        frames_0 = 1.0f - _S177.y;
                    }
                    else
                    {
                        frames_0 = _S177.y;
                    }
                    var w_0 : f32 = nCount_2 * frames_0;
                    var tapHistory_0 : vec4<f32> = (textureLoad((uHistory_0), ((_S182)).xy, ((_S182)).z));
                    var frameSum_0 : f32 = nCount_0 + tapHistory_0.w * w_0;
                    var weightSum_2 : f32 = nCount_1 + w_0;
                    nMin_0 = nMin_0 + tapHistory_0.xyz * vec3<f32>(w_0);
                    nCount_0 = frameSum_0;
                    nCount_1 = weightSum_2;
                    dx_0 = dx_0 + i32(1);
                }
                if(nCount_1 > 0.00999999977648258f)
                {
                    var _S183 : vec3<f32> = vec3<f32>(0.5f);
                    var windowCentre_0 : vec3<f32> = _S183 * (traced_0 + nMax_1);
                    var _S184 : vec3<f32> = vec3<f32>(2.0f) * (_S183 * (nMax_1 - traced_0) + vec3<f32>(0.25f) * windowCentre_0 + vec3<f32>(0.00100000004749745f));
                    var _S185 : f32 = min(nCount_0 / nCount_1 + 1.0f, f32(Globals_0.uMaxAccumulated_0));
                    nMax_0 = mix(clamp(nMin_0 / vec3<f32>(nCount_1), windowCentre_0 - _S184, windowCentre_0 + _S184), est_0, vec3<f32>((1.0f / (1.0f + _S185))));
                    frames_0 = _S185;
                }
                else
                {
                    nMax_0 = est_0;
                    frames_0 = 0.0f;
                }
            }
            else
            {
                nMax_0 = est_0;
                frames_0 = 0.0f;
            }
        }
        else
        {
            nMax_0 = est_0;
            frames_0 = 0.0f;
        }
    }
    else
    {
        nMax_0 = est_0;
        frames_0 = 0.0f;
    }
    if((Globals_0.uDebugSource_0) == i32(4))
    {
        nMax_0 = current_0;
        frames_0 = 1.0f;
    }
    var _S186 : vec2<u32> = vec2<u32>(_S157);
    textureStore((uIndirect_0), (_S186), (vec4<f32>(nMax_0, frames_0)));
    textureStore((uPosition_0), (_S186), (vec4<f32>(world_17 - Globals_0.uCameraToCache_0, 1.0f)));
    return;
}

