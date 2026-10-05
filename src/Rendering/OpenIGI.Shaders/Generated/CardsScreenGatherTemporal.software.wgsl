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
    @align(8) uHasHistory_0 : i32,
    @align(4) uFrameIndex_0 : i32,
    @align(16) uMaxAccumulated_0 : i32,
    @align(4) uHasVelocity_0 : i32,
    @align(8) uGatherPass_0 : i32,
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

@binding(19) @group(0) var<storage, read_write> uCells_0 : array<vec2<i32>>;

@binding(33) @group(0) var uAlbedo_0 : texture_2d<f32>;

@binding(20) @group(0) var<storage, read_write> uCellPages_0 : array<i32>;

@binding(16) @group(0) var<storage, read> uPages_0 : array<vec4<f32>>;

@binding(65) @group(0) var uAlbedoSampler_0 : sampler;

@binding(36) @group(0) var uDepth_0 : texture_2d<f32>;

@binding(68) @group(0) var uDepthSampler_0 : sampler;

@binding(38) @group(0) var uFinal_0 : texture_2d<f32>;

@binding(70) @group(0) var uFinalSampler_0 : sampler;

@binding(0) @group(0) var uIndirect_0 : texture_storage_2d<rgba16float, write>;

@binding(1) @group(0) var uPosition_0 : texture_storage_2d<rgba16float, write>;

@binding(44) @group(0) var uRaw_0 : texture_2d<f32>;

@binding(45) @group(0) var uVelocity_0 : texture_2d<f32>;

@binding(77) @group(0) var uVelocitySampler_0 : sampler;

@binding(41) @group(0) var uHistory_0 : texture_2d<f32>;

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

fn ProbeSurface_0( probe_0 : vec2<i32>,  world_0 : ptr<function, vec3<f32>>,  viewPosition_0 : ptr<function, vec3<f32>>,  worldNormal_0 : ptr<function, vec3<f32>>) -> bool
{
    const _S9 : vec3<f32> = vec3<f32>(0.0f, 0.0f, 0.0f);
    (*world_0) = _S9;
    (*viewPosition_0) = _S9;
    (*worldNormal_0) = vec3<f32>(0.0f, 0.0f, 1.0f);
    var uv_3 : vec2<f32> = (vec2<f32>(min(probe_0 * vec2<i32>(Globals_0.uProbeStep_0) + ProbeTileOffset_0(Globals_0.uFrameIndex_0, Globals_0.uProbeStep_0), vec2<i32>(Globals_0.uScreenWidth_0, Globals_0.uScreenHeight_0) - vec2<i32>(i32(1)))) + vec2<f32>(0.5f)) / vec2<f32>(f32(Globals_0.uScreenWidth_0), f32(Globals_0.uScreenHeight_0));
    var depth_4 : f32 = DepthBilinear_0(uSceneDepth_0, uSceneDepthSampler_0, uv_3);
    if(IsSkyDepth_0(depth_4))
    {
        return false;
    }
    var _S10 : vec3<f32> = ViewPositionAt_0(uv_3, depth_4);
    (*viewPosition_0) = _S10;
    (*world_0) = Globals_0.uCameraToCache_0 + Globals_0.uViewToWorld0_0 * vec3<f32>(_S10.x) + Globals_0.uViewToWorld1_0 * vec3<f32>(_S10.y) + Globals_0.uViewToWorld2_0 * vec3<f32>(_S10.z);
    (*worldNormal_0) = normalize((textureSampleLevel((uSceneNormal_0), (uSceneNormalSampler_0), (uv_3), (0.0f))).xyz * vec3<f32>(2.0f) - vec3<f32>(1.0f));
    return true;
}

fn RadicalInverseBits_0( bits_0 : u32) -> f32
{
    var _S11 : u32 = (((bits_0 << (u32(16)))) | (((bits_0 >> (u32(16))))));
    var _S12 : u32 = (((((_S11 & (u32(1431655765)))) << (u32(1)))) | (((((_S11 & (u32(2863311530)))) >> (u32(1))))));
    var _S13 : u32 = (((((_S12 & (u32(858993459)))) << (u32(2)))) | (((((_S12 & (u32(3435973836)))) >> (u32(2))))));
    var _S14 : u32 = (((((_S13 & (u32(252645135)))) << (u32(4)))) | (((((_S13 & (u32(4042322160)))) >> (u32(4))))));
    return f32((((((_S14 & (u32(16711935)))) << (u32(8)))) | (((((_S14 & (u32(4278255360)))) >> (u32(8))))))) * 2.32830643653869629e-10f;
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
     normal_0 : vec3<f32>,
};

fn BasisAround_0( normal_1 : vec3<f32>) -> HemisphereBasis_0
{
    var basis_0 : HemisphereBasis_0;
    basis_0.normal_0 = normal_1;
    var _S15 : f32 = normal_1.z;
    if(_S15 < -0.99999988079071045f)
    {
        basis_0.tangent_0 = vec3<f32>(0.0f, -1.0f, 0.0f);
        basis_0.bitangent_0 = vec3<f32>(-1.0f, 0.0f, 0.0f);
        return basis_0;
    }
    var a_0 : f32 = 1.0f / (1.0f + _S15);
    var _S16 : f32 = normal_1.x;
    var _S17 : f32 = - _S16;
    var _S18 : f32 = normal_1.y;
    var b_0 : f32 = _S17 * _S18 * a_0;
    basis_0.tangent_0 = vec3<f32>(1.0f - _S16 * _S16 * a_0, b_0, _S17);
    basis_0.bitangent_0 = vec3<f32>(b_0, 1.0f - _S18 * _S18 * a_0, - _S18);
    return basis_0;
}

fn ToView_0( world_1 : vec3<f32>) -> vec3<f32>
{
    return vec3<f32>(dot(Globals_0.uViewToWorld0_0, world_1), dot(Globals_0.uViewToWorld1_0, world_1), dot(Globals_0.uViewToWorld2_0, world_1));
}

fn Orient_0( basis_1 : HemisphereBasis_0,  local_0 : vec3<f32>) -> vec3<f32>
{
    return basis_1.tangent_0 * vec3<f32>(local_0.x) + basis_1.bitangent_0 * vec3<f32>(local_0.y) + basis_1.normal_0 * vec3<f32>(local_0.z);
}

fn ProjectToUv_0( viewPosition_1 : vec3<f32>) -> vec2<f32>
{
    var _S19 : f32 = viewPosition_1.z;
    var _S20 : vec2<f32> = vec2<f32>(0.5f);
    return vec2<f32>(viewPosition_1.x * Globals_0.uUnproject_0.x / _S19, -1.0f * (viewPosition_1.y * Globals_0.uUnproject_0.y) / _S19) * _S20 + _S20;
}

fn CapRayIntensity_0( radiance_0 : vec3<f32>) -> vec3<f32>
{
    var _S21 : f32 = max(radiance_0.x, max(radiance_0.y, radiance_0.z));
    var _S22 : vec3<f32>;
    if(_S21 > (Globals_0.uMaxRayIntensity_0))
    {
        _S22 = radiance_0 * vec3<f32>((Globals_0.uMaxRayIntensity_0 / _S21));
    }
    else
    {
        _S22 = radiance_0;
    }
    return _S22;
}

fn ScreenTraceRadiance_0( viewOrigin_0 : vec3<f32>,  viewNormal_0 : vec3<f32>,  viewDir_0 : vec3<f32>,  radiance_1 : ptr<function, vec3<f32>>) -> bool
{
    var hitFraction_0 : f32;
    (*radiance_1) = vec3<f32>(0.0f, 0.0f, 0.0f);
    var _S23 : bool;
    if((Globals_0.uScreenTraceSteps_0) <= i32(0))
    {
        _S23 = true;
    }
    else
    {
        _S23 = (viewDir_0.z) == 0.0f;
    }
    if(_S23)
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
    var _S24 : f32 = farPoint_1.z;
    if(_S24 <= (Globals_0.uUnproject_0.z))
    {
        return false;
    }
    var startUv_0 : vec2<f32> = ProjectToUv_0(origin_0);
    var travel_0 : vec2<f32> = ProjectToUv_0(farPoint_1) - startUv_0;
    var used_0 : i32 = i32(clamp(length(travel_0 / (vec2<f32>(1.0f) / vec2<f32>(f32(Globals_0.uScreenWidth_0), f32(Globals_0.uScreenHeight_0)))), 1.0f, f32(Globals_0.uScreenTraceSteps_0)));
    var stride_0 : f32 = 1.0f / f32(used_0);
    var _S25 : f32 = fract(52.98291778564453125f * fract(dot(startUv_0 * vec2<f32>(f32(Globals_0.uScreenWidth_0), f32(Globals_0.uScreenHeight_0)) + vec2<f32>((5.58823776245117188f * f32(((Globals_0.uFrameIndex_0) & (i32(63)))))), vec2<f32>(0.06711056083440781f, 0.00583714991807938f)))) * stride_0;
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
        var t_0 : f32 = f32(i_1) * stride_0 + _S25;
        if(t_0 > 1.0f)
        {
            hitFraction_0 = -1.0f;
            break;
        }
        var uv_4 : vec2<f32> = startUv_0 + travel_0 * vec2<f32>(t_0);
        if((any((uv_4 < vec2<f32>(0.0f, 0.0f)))))
        {
            _S23 = true;
        }
        else
        {
            _S23 = (any((uv_4 > vec2<f32>(1.0f, 1.0f))));
        }
        if(_S23)
        {
            hitFraction_0 = -1.0f;
            break;
        }
        var _S26 : f32 = origin_0.z;
        var sceneDepth_0 : f32 = ViewPositionAt_0(uv_4, DepthBilinear_0(uSceneDepth_0, uSceneDepthSampler_0, uv_4)).z;
        var difference_0 : f32 = 1.0f / mix(1.0f / _S26, 1.0f / _S24, t_0) - sceneDepth_0;
        var _S27 : bool;
        if(difference_0 > 0.0f)
        {
            _S27 = difference_0 < (Globals_0.uScreenThickness_0 + sceneDepth_0 * 0.01999999955296516f);
        }
        else
        {
            _S27 = false;
        }
        if(_S27)
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
                if((1.0f / mix(1.0f / _S26, 1.0f / _S24, mid_0) - ViewPositionAt_0(midUv_0, DepthBilinear_0(uSceneDepth_0, uSceneDepthSampler_0, midUv_0)).z) > 0.0f)
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
        var _S28 : i32 = i_1 + i32(1);
        previous_0 = t_0;
        i_1 = _S28;
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
    var _S29 : f32 = hitWorld_0.x;
    var _S30 : f32 = hitWorld_0.y;
    var _S31 : f32 = hitWorld_0.z;
    var colourView_0 : vec3<f32> = Globals_0.uColourWorldToView0_0 * vec3<f32>((_S29 - Globals_0.uColourCameraToCache_0.x)) + Globals_0.uColourWorldToView1_0 * vec3<f32>((_S30 - Globals_0.uColourCameraToCache_0.y)) + Globals_0.uColourWorldToView2_0 * vec3<f32>((_S31 - Globals_0.uColourCameraToCache_0.z));
    if((Globals_0.uHasHistory_0) == i32(0))
    {
        _S23 = true;
    }
    else
    {
        _S23 = (colourView_0.z) <= (Globals_0.uUnproject_0.z);
    }
    if(_S23)
    {
        return false;
    }
    var colourUv_0 : vec2<f32> = ProjectToUv_0(colourView_0);
    if((any((colourUv_0 < vec2<f32>(0.0f, 0.0f)))))
    {
        _S23 = true;
    }
    else
    {
        _S23 = (any((colourUv_0 > vec2<f32>(1.0f, 1.0f))));
    }
    if(_S23)
    {
        return false;
    }
    var prevView_0 : vec3<f32> = Globals_0.uPrevWorldToView0_0 * vec3<f32>((_S29 - Globals_0.uPrevCameraToCache_0.x)) + Globals_0.uPrevWorldToView1_0 * vec3<f32>((_S30 - Globals_0.uPrevCameraToCache_0.y)) + Globals_0.uPrevWorldToView2_0 * vec3<f32>((_S31 - Globals_0.uPrevCameraToCache_0.z));
    var _S32 : f32 = prevView_0.z;
    if(_S32 <= (Globals_0.uUnproject_0.z))
    {
        return false;
    }
    var probeFrameUv_0 : vec2<f32> = ProjectToUv_0(prevView_0);
    var probeGrid_0 : vec2<i32> = (vec2<i32>(Globals_0.uScreenWidth_0, Globals_0.uScreenHeight_0) + vec2<i32>((Globals_0.uProbeStep_0 - i32(1)))) / vec2<i32>(Globals_0.uProbeStep_0);
    var _S33 : vec2<i32> = vec2<i32>(probeFrameUv_0 * vec2<f32>(f32(Globals_0.uScreenWidth_0), f32(Globals_0.uScreenHeight_0))) / vec2<i32>(Globals_0.uProbeStep_0);
    var _S34 : vec3<i32> = vec3<i32>(clamp(_S33, vec2<i32>(i32(0), i32(0)), probeGrid_0 - vec2<i32>(i32(1), i32(1))), i32(0));
    var prevProbePos_0 : vec4<f32> = (textureLoad((uPositionHistory_0), ((_S34)).xy, ((_S34)).z));
    var hitWorldNormal_0 : vec3<f32> = normalize(Globals_0.uViewToWorld0_0 * vec3<f32>(hitNormal_0.x) + Globals_0.uViewToWorld1_0 * vec3<f32>(hitNormal_0.y) + Globals_0.uViewToWorld2_0 * vec3<f32>(hitNormal_0.z));
    var probeSpan_0 : f32 = 2.0f * _S32 * f32(Globals_0.uProbeStep_0) / (Globals_0.uUnproject_0.y * f32(Globals_0.uScreenHeight_0));
    if((prevProbePos_0.w) <= 0.0f)
    {
        _S23 = true;
    }
    else
    {
        _S23 = (abs(dot(prevProbePos_0.xyz + Globals_0.uPrevCameraToCache_0 - hitWorld_0, hitWorldNormal_0))) > (max(0.10000000149011612f, 2.0f * probeSpan_0));
    }
    if(_S23)
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

fn DistanceAt_0( world_2 : vec3<f32>,  known_0 : ptr<function, bool>,  raw_0 : ptr<function, f32>,  resolved_0 : ptr<function, f32>) -> f32
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
        var _S35 : f32 = Globals_0.uClipmapOrigin_0[level_0].w;
        var voxel_0 : vec3<f32> = (world_2 - Globals_0.uClipmapOrigin_0[level_0].xyz) / vec3<f32>(_S35);
        var _S36 : bool;
        if((any((voxel_0 < vec3<f32>(0.5f, 0.5f, 0.5f)))))
        {
            _S36 = true;
        }
        else
        {
            _S36 = (any((voxel_0 > vec3<f32>(f32(Globals_0.uShadowResolution_0) - 0.5f, f32(Globals_0.uShadowResolution_0) - 0.5f, f32(Globals_0.uShadowResolution_0) - 0.5f))));
        }
        if(_S36)
        {
            level_0 = level_0 + i32(1);
            continue;
        }
        var uvw_0 : vec3<f32> = vec3<f32>(voxel_0.x / f32(Globals_0.uShadowResolution_0), voxel_0.y / f32(Globals_0.uShadowResolution_0), (f32(level_0) * f32(Globals_0.uShadowResolution_0) + voxel_0.z) / f32(Globals_0.uShadowResolution_0 * Globals_0.uShadowLevels_0));
        (*known_0) = true;
        (*raw_0) = (textureSampleLevel((uClipmaps_0), (uClipmapsSampler_0), (uvw_0), (0.0f))).x;
        (*resolved_0) = _S35;
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

fn VisibilityAt_0( world_3 : vec3<f32>,  normal_2 : vec3<f32>,  direction_0 : vec3<f32>,  hit_0 : ptr<function, vec3<f32>>) -> f32
{
    (*hit_0) = world_3;
    if((Globals_0.uShadowLevels_0) <= i32(0))
    {
        return 1.0f;
    }
    var _S37 : vec3<f32> = world_3 + normal_2 * vec3<f32>(Globals_0.uClipmapOrigin_0[i32(0)].w) * vec3<f32>(1.5f);
    var _S38 : f32 = Globals_0.uClipmapOrigin_0[i32(0)].w * f32(Globals_0.uShadowResolution_0) * f32((i32(1) << (u32(Globals_0.uShadowLevels_0 - i32(1)))));
    var step_1 : i32 = i32(0);
    var travelled_1 : f32 = 0.0f;
    for(;;)
    {
        var _S39 : bool;
        if(step_1 < i32(64))
        {
            _S39 = travelled_1 < _S38;
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
        var known_1 : bool;
        var raw_1 : f32;
        var resolved_2 : f32;
        var distance_0 : f32 = DistanceAt_0(_S37 + direction_0 * vec3<f32>(travelled_1), &(known_1), &(raw_1), &(resolved_2));
        if(!known_1)
        {
            return 1.0f;
        }
        var hitDistance_1 : f32;
        var _S40 : bool = FieldStepHits_0(distance_0, travelled_1, resolved_2, &(hitDistance_1));
        if(_S40)
        {
            (*hit_0) = _S37 + direction_0 * vec3<f32>(hitDistance_1);
            return 0.0f;
        }
        var travelled_2 : f32 = travelled_1 + max(distance_0, resolved_2 * 0.25f);
        step_1 = step_1 + i32(1);
        travelled_1 = travelled_2;
    }
    return 1.0f;
}

fn LookupBucket_0( cell_0 : vec3<i32>) -> i32
{
    return i32(((((((u32(cell_0.x) * u32(2376512323)) ^ ((u32(cell_0.y) * u32(3625334849))))) ^ ((u32(cell_0.z) * u32(3407524639))))) & (u32(Globals_0.uLookupBuckets_0 - i32(1)))));
}

fn MoverPages_0( headers_0 : i32,  index_0 : i32,  world_4 : vec3<f32>,  bias_0 : f32) -> vec2<i32>
{
    var at_0 : i32 = headers_0 + index_0 * i32(6);
    var reach_0 : f32 = (bitcast<f32>((uCellPages_0[at_0 + i32(3)]))) + bias_0;
    var toCentre_0 : vec3<f32> = world_4 - vec3<f32>((bitcast<f32>((uCellPages_0[at_0]))), (bitcast<f32>((uCellPages_0[at_0 + i32(1)]))), (bitcast<f32>((uCellPages_0[at_0 + i32(2)]))));
    var _S41 : vec2<i32>;
    if((dot(toCentre_0, toCentre_0)) > (reach_0 * reach_0))
    {
        _S41 = vec2<i32>(i32(0), i32(0));
    }
    else
    {
        _S41 = vec2<i32>(uCellPages_0[at_0 + i32(4)], uCellPages_0[at_0 + i32(5)]);
    }
    return _S41;
}

fn ProjectOntoCard_0( page_0 : i32,  world_5 : vec3<f32>,  directionW_0 : vec3<f32>,  across_0 : ptr<function, vec2<f32>>,  halfSize_0 : ptr<function, vec2<f32>>,  behind_0 : ptr<function, f32>,  depth_5 : ptr<function, f32>,  facing_0 : ptr<function, vec3<f32>>,  directionLocal_0 : ptr<function, vec3<f32>>,  litRect_0 : ptr<function, vec4<f32>>,  cardPage_0 : ptr<function, vec4<f32>>)
{
    var _S42 : i32 = page_0 * i32(10);
    (*litRect_0) = uPages_0[_S42];
    (*cardPage_0) = uPages_0[_S42 + i32(1)];
    var centreAndHalfWidth_0 : vec4<f32> = uPages_0[_S42 + i32(2)];
    var rightAndHalfHeight_0 : vec4<f32> = uPages_0[_S42 + i32(3)];
    var upAndDepth_0 : vec4<f32> = uPages_0[_S42 + i32(4)];
    var direction_1 : vec4<f32> = uPages_0[_S42 + i32(5)];
    var row0_0 : vec4<f32> = uPages_0[_S42 + i32(6)];
    var row1_0 : vec4<f32> = uPages_0[_S42 + i32(7)];
    var row2_0 : vec4<f32> = uPages_0[_S42 + i32(8)];
    var delta_0 : vec3<f32> = world_5 - vec3<f32>(row0_0.w, row1_0.w, row2_0.w);
    var _S43 : vec3<f32> = row0_0.xyz;
    var _S44 : vec3<f32> = row1_0.xyz;
    var _S45 : vec3<f32> = row2_0.xyz;
    var local_1 : vec3<f32> = vec3<f32>(dot(_S43, delta_0), dot(_S44, delta_0), dot(_S45, delta_0));
    (*directionLocal_0) = vec3<f32>(dot(_S43, directionW_0), dot(_S44, directionW_0), dot(_S45, directionW_0));
    var relative_0 : vec3<f32> = local_1 - centreAndHalfWidth_0.xyz;
    var _S46 : vec2<f32> = max(vec2<f32>(centreAndHalfWidth_0.w, rightAndHalfHeight_0.w), vec2<f32>(9.99999997475242708e-07f, 9.99999997475242708e-07f));
    (*halfSize_0) = _S46;
    (*across_0) = vec2<f32>(dot(relative_0, rightAndHalfHeight_0.xyz), dot(relative_0, upAndDepth_0.xyz)) / _S46;
    var _S47 : vec3<f32> = direction_1.xyz;
    (*behind_0) = - dot(relative_0, _S47);
    (*depth_5) = upAndDepth_0.w;
    (*facing_0) = _S47;
    return;
}

fn LookupCardWithin_0( world_6 : vec3<f32>,  arrivingW_0 : vec3<f32>,  skin_0 : f32,  found_0 : ptr<function, vec2<f32>>,  litFound_0 : ptr<function, vec2<f32>>,  agreement_0 : ptr<function, f32>,  cardDepth_0 : ptr<function, f32>) -> bool
{
    (*cardDepth_0) = 0.0f;
    const _S48 : vec2<f32> = vec2<f32>(0.0f, 0.0f);
    (*found_0) = _S48;
    (*litFound_0) = _S48;
    (*agreement_0) = 1.0e+09f;
    if((Globals_0.uLookupBuckets_0) <= i32(0))
    {
        return false;
    }
    var _S49 : vec2<i32> = uCells_0[LookupBucket_0(vec3<i32>(floor((world_6 - Globals_0.uLookupMin_0) / vec3<f32>(Globals_0.uLookupCell_0))))];
    var albedoWidth_0 : u32;
    var albedoHeight_0 : u32;
    {var dim = textureDimensions((uAlbedo_0));((albedoWidth_0)) = dim.x;((albedoHeight_0)) = dim.y;};
    var _S50 : vec2<f32> = vec2<f32>(f32(albedoWidth_0), f32(albedoHeight_0));
    var _S51 : vec2<i32> = uCells_0[Globals_0.uLookupBuckets_0];
    var settled_0 : bool = false;
    var anyFound_0 : bool = false;
    var walk_0 : i32 = i32(0);
    for(;;)
    {
        var _S52 : bool;
        if(walk_0 <= (_S51.y))
        {
            _S52 = !settled_0;
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
        var _S53 : bool = walk_0 == i32(0);
        var _S54 : vec2<i32>;
        if(_S53)
        {
            _S54 = _S49;
        }
        else
        {
            _S54 = MoverPages_0(_S51.x, walk_0 - i32(1), world_6, 0.0f);
        }
        var settled_1 : bool = settled_0;
        var anyFound_1 : bool = anyFound_0;
        var slot_0 : i32 = i32(0);
        for(;;)
        {
            var _S55 : bool;
            if(slot_0 < (_S54.y))
            {
                _S55 = !settled_1;
            }
            else
            {
                _S55 = false;
            }
            if(_S55)
            {
            }
            else
            {
                break;
            }
            var page_1 : i32 = uCellPages_0[_S54.x + slot_0];
            var _S56 : i32 = uCellPages_0[_S54.x + slot_0] * i32(10);
            if(((uPages_0[_S56 + i32(5)].w) > 0.5f) == _S53)
            {
                slot_0 = slot_0 + i32(1);
                continue;
            }
            var bound_0 : vec4<f32> = uPages_0[_S56 + i32(9)];
            var toCentre_1 : vec3<f32> = world_6 - bound_0.xyz;
            var _S57 : f32 = bound_0.w;
            if((dot(toCentre_1, toCentre_1)) > (_S57 * _S57))
            {
                slot_0 = slot_0 + i32(1);
                continue;
            }
            var across_1 : vec2<f32>;
            var halfSize_1 : vec2<f32>;
            var behind_1 : f32;
            var depth_6 : f32;
            var facing_1 : vec3<f32>;
            var arrivingLocal_0 : vec3<f32>;
            var litRect_1 : vec4<f32>;
            var cardPage_1 : vec4<f32>;
            ProjectOntoCard_0(page_1, world_6, (vec3<f32>(0) - arrivingW_0), &(across_1), &(halfSize_1), &(behind_1), &(depth_6), &(facing_1), &(arrivingLocal_0), &(litRect_1), &(cardPage_1));
            var _S58 : bool;
            if((abs(across_1.x)) > 1.0f)
            {
                _S58 = true;
            }
            else
            {
                _S58 = (abs(across_1.y)) > 1.0f;
            }
            if(_S58)
            {
                slot_0 = slot_0 + i32(1);
                continue;
            }
            var _S59 : bool;
            if((dot(arrivingW_0, arrivingW_0)) > 0.5f)
            {
                _S59 = (dot(facing_1, arrivingLocal_0)) < -0.05000000074505806f;
            }
            else
            {
                _S59 = false;
            }
            if(_S59)
            {
                slot_0 = slot_0 + i32(1);
                continue;
            }
            var _S60 : bool;
            if(behind_1 < (- skin_0))
            {
                _S60 = true;
            }
            else
            {
                _S60 = behind_1 > (depth_6 + skin_0);
            }
            if(_S60)
            {
                slot_0 = slot_0 + i32(1);
                continue;
            }
            var _S61 : vec2<f32> = vec2<f32>(0.5f);
            var uv_5 : vec2<f32> = across_1 * _S61 + _S61;
            var texel_0 : vec2<f32> = (cardPage_1.xy + uv_5 * cardPage_1.zw) / _S50;
            if(((textureSampleLevel((uAlbedo_0), (uAlbedoSampler_0), (texel_0), (0.0f))).w) < 0.5f)
            {
                slot_0 = slot_0 + i32(1);
                continue;
            }
            var error_0 : f32 = abs((textureSampleLevel((uDepth_0), (uDepthSampler_0), (texel_0), (0.0f))).x * depth_6 - behind_1);
            var settled_2 : bool;
            var anyFound_2 : bool;
            if(error_0 < (*agreement_0))
            {
                (*agreement_0) = error_0;
                (*cardDepth_0) = depth_6;
                (*found_0) = texel_0;
                var _S62 : bool = error_0 < 0.01999999955296516f;
                (*litFound_0) = (litRect_1.xy + uv_5 * litRect_1.zw) / Globals_0.uAtlasSize_0.xy;
                settled_2 = _S62;
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
        var _S63 : i32 = walk_0 + i32(1);
        settled_0 = settled_1;
        anyFound_0 = anyFound_1;
        walk_0 = _S63;
    }
    return anyFound_0;
}

fn LookupCardHit_0( world_7 : vec3<f32>,  rayDirection_0 : vec3<f32>,  found_1 : ptr<function, vec2<f32>>,  litFound_1 : ptr<function, vec2<f32>>,  agreement_1 : ptr<function, f32>) -> bool
{
    var depth_7 : f32;
    var _S64 : bool = LookupCardWithin_0(world_7, rayDirection_0, 0.25f, &((*found_1)), &((*litFound_1)), &((*agreement_1)), &(depth_7));
    return _S64;
}

fn LookupCardHit_1( world_8 : vec3<f32>,  rayDirection_1 : vec3<f32>,  found_2 : ptr<function, vec2<f32>>,  litFound_2 : ptr<function, vec2<f32>>,  agreement_2 : ptr<function, f32>) -> bool
{
    var depth_8 : f32;
    var _S65 : bool = LookupCardWithin_0(world_8, rayDirection_1, 0.25f, &((*found_2)), &((*litFound_2)), &((*agreement_2)), &(depth_8));
    return _S65;
}

fn SkyRadiance_0( direction_2 : vec3<f32>) -> vec3<f32>
{
    if((Globals_0.uSkyShValid_0) == i32(0))
    {
        return Globals_0.uSkyColour_0;
    }
    var n_1 : vec4<f32> = vec4<f32>(direction_2, 1.0f);
    var q_0 : vec4<f32> = n_1.xyzz * n_1.yzzx;
    var _S66 : f32 = direction_2.x;
    var _S67 : f32 = direction_2.y;
    return max(vec3<f32>(0.0f, 0.0f, 0.0f), vec3<f32>(dot(Globals_0.uSkySh_0[i32(0)], n_1), dot(Globals_0.uSkySh_0[i32(1)], n_1), dot(Globals_0.uSkySh_0[i32(2)], n_1)) + vec3<f32>(dot(Globals_0.uSkySh_0[i32(3)], q_0), dot(Globals_0.uSkySh_0[i32(4)], q_0), dot(Globals_0.uSkySh_0[i32(5)], q_0)) + Globals_0.uSkySh_0[i32(6)].xyz * vec3<f32>((_S66 * _S66 - _S67 * _S67)));
}

fn GatherIndirect_0( world_9 : vec3<f32>,  normal_3 : vec3<f32>,  viewOrigin_1 : vec3<f32>,  jitter_0 : vec2<f32>) -> vec3<f32>
{
    var radiance_2 : vec3<f32>;
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
    var _S68 : HemisphereBasis_0 = BasisAround_0(normal_3);
    var _S69 : vec3<f32> = ToView_0(normal_3);
    const _S70 : vec3<f32> = vec3<f32>(0.0f, 0.0f, 0.0f);
    var ray_0 : i32 = i32(0);
    var surfaceSum_0 : vec3<f32> = _S70;
    var surfaceCount_0 : f32 = 0.0f;
    var unplaced_0 : f32 = 0.0f;
    var openSum_0 : vec3<f32> = _S70;
    var openCount_0 : f32 = 0.0f;
    var irradiance_0 : vec3<f32> = _S70;
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
        var direction_3 : vec3<f32> = Orient_0(_S68, vec3<f32>(sinTheta_0 * cos(phi_0), sinTheta_0 * sin(phi_0), cosTheta_0));
        var source_0 : i32;
        if((Globals_0.uScreenTraceSteps_0) > i32(0))
        {
            var screenRadiance_0 : vec3<f32>;
            var _S71 : bool = ScreenTraceRadiance_0(viewOrigin_1, _S69, ToView_0(direction_3), &(screenRadiance_0));
            if(_S71)
            {
                source_0 = i32(1);
                radiance_2 = screenRadiance_0;
            }
            else
            {
                source_0 = i32(0);
                radiance_2 = _S70;
            }
            resolved_3 = _S71;
        }
        else
        {
            resolved_3 = false;
            source_0 = i32(0);
            radiance_2 = _S70;
        }
        var source_1 : i32;
        var radiance_3 : vec3<f32>;
        var surfaceSum_1 : vec3<f32>;
        var radiance_4 : vec3<f32>;
        var weight_1 : f32;
        var surfaceCount_1 : f32;
        var unplaced_1 : f32;
        if(!resolved_3)
        {
            var hit_1 : vec3<f32>;
            var _S72 : f32 = VisibilityAt_0(world_9, normal_3, direction_3, &(hit_1));
            var openCount_1 : f32;
            if(_S72 <= 0.5f)
            {
                var finest_0 : f32 = Globals_0.uClipmapOrigin_0[i32(0)].w;
                var near_1 : f32 = smoothstep(1.5f * finest_0, 2.0f * finest_0, length(hit_1 - world_9));
                var found_3 : vec2<f32>;
                var litFound_3 : vec2<f32>;
                var agreement_3 : f32;
                var _S73 : bool = LookupCardHit_0(hit_1, direction_3, &(found_3), &(litFound_3), &(agreement_3));
                if(_S73)
                {
                    var radiance_5 : vec3<f32> = (textureSampleLevel((uFinal_0), (uFinalSampler_0), (litFound_3), (0.0f))).xyz * vec3<f32>(Globals_0.uBounceScale_0);
                    var surfaceSum_2 : vec3<f32> = surfaceSum_0 + CapRayIntensity_0(radiance_5) * vec3<f32>(near_1);
                    var surfaceCount_2 : f32 = surfaceCount_0 + near_1;
                    source_1 = i32(2);
                    radiance_3 = radiance_5;
                    weight_1 = near_1;
                    surfaceSum_1 = surfaceSum_2;
                    surfaceCount_1 = surfaceCount_2;
                    unplaced_1 = unplaced_0;
                }
                else
                {
                    var unplaced_2 : f32 = unplaced_0 + near_1;
                    source_1 = source_0;
                    radiance_3 = radiance_2;
                    weight_1 = 0.0f;
                    surfaceSum_1 = surfaceSum_0;
                    surfaceCount_1 = surfaceCount_0;
                    unplaced_1 = unplaced_2;
                }
                radiance_4 = openSum_0;
                openCount_1 = openCount_0;
            }
            else
            {
                if((direction_3.z) >= 0.0f)
                {
                    radiance_3 = SkyRadiance_0(direction_3);
                }
                else
                {
                    radiance_3 = vec3<f32>(0.25f) * (SkyRadiance_0(vec3<f32>(0.0f, 0.0f, 1.0f)) + Globals_0.uGroundSun_0);
                }
                var openSum_1 : vec3<f32> = openSum_0 + CapRayIntensity_0(radiance_3);
                var openCount_2 : f32 = openCount_0 + 1.0f;
                source_1 = i32(3);
                weight_1 = 1.0f;
                surfaceSum_1 = surfaceSum_0;
                surfaceCount_1 = surfaceCount_0;
                unplaced_1 = unplaced_0;
                radiance_4 = openSum_1;
                openCount_1 = openCount_2;
            }
            openSum_0 = radiance_4;
            openCount_0 = openCount_1;
        }
        else
        {
            source_1 = source_0;
            radiance_3 = radiance_2;
            weight_1 = 1.0f;
            surfaceSum_1 = surfaceSum_0;
            surfaceCount_1 = surfaceCount_0;
            unplaced_1 = unplaced_0;
        }
        var _S74 : bool;
        if((Globals_0.uDebugSource_0) != i32(0))
        {
            _S74 = source_1 != (Globals_0.uDebugSource_0);
        }
        else
        {
            _S74 = false;
        }
        if(_S74)
        {
            radiance_4 = _S70;
        }
        else
        {
            radiance_4 = radiance_3;
        }
        var irradiance_1 : vec3<f32> = irradiance_0 + CapRayIntensity_0(radiance_4) * vec3<f32>(weight_1);
        var counted_1 : f32 = counted_0 + weight_1;
        ray_0 = ray_0 + i32(1);
        surfaceSum_0 = surfaceSum_1;
        surfaceCount_0 = surfaceCount_1;
        unplaced_0 = unplaced_1;
        irradiance_0 = irradiance_1;
        counted_0 = counted_1;
    }
    if(unplaced_0 > 0.0f)
    {
        if((Globals_0.uDebugSource_0) == i32(0))
        {
            resolved_3 = true;
        }
        else
        {
            resolved_3 = (Globals_0.uDebugSource_0) == i32(2);
        }
    }
    else
    {
        resolved_3 = false;
    }
    if(resolved_3)
    {
        if(surfaceCount_0 > 0.0f)
        {
            radiance_2 = surfaceSum_0 / vec3<f32>(surfaceCount_0);
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
            radiance_2 = surfaceSum_0 * vec3<f32>(0.25f);
        }
        var irradiance_2 : vec3<f32> = irradiance_0 + radiance_2 * vec3<f32>(unplaced_0);
        counted_0 = counted_0 + unplaced_0;
        irradiance_0 = irradiance_2;
    }
    return max(irradiance_0 * vec3<f32>((3.14159274101257324f / max(counted_0, 4.0f))), _S70);
}

@compute
@workgroup_size(8, 8, 1)
fn computeMain(@builtin(global_invocation_id) id_0 : vec3<u32>, @builtin(local_invocation_id) localId_0 : vec3<u32>)
{
    var _S75 : bool;
    var _S76 : bool;
    var nMin_0 : vec3<f32>;
    var nMax_0 : vec3<f32>;
    var dx_0 : i32;
    var nCount_0 : f32;
    var nCount_1 : f32;
    var _S77 : vec2<i32> = vec2<i32>(id_0.xy);
    var grid_0 : vec2<i32> = (vec2<i32>(Globals_0.uScreenWidth_0, Globals_0.uScreenHeight_0) + vec2<i32>((Globals_0.uProbeStep_0 - i32(1)))) / vec2<i32>(Globals_0.uProbeStep_0);
    var ownMotion_0 : bool;
    if((_S77.x) >= (grid_0.x))
    {
        ownMotion_0 = true;
    }
    else
    {
        ownMotion_0 = (_S77.y) >= (grid_0.y);
    }
    if(ownMotion_0)
    {
        return;
    }
    var world_10 : vec3<f32>;
    var viewPosition_2 : vec3<f32>;
    var worldNormal_1 : vec3<f32>;
    var onSurface_0 : bool = ProbeSurface_0(_S77, &(world_10), &(viewPosition_2), &(worldNormal_1));
    var traced_0 : vec3<f32>;
    var nCount_2 : f32;
    if((Globals_0.uGatherPass_0) == i32(0))
    {
        const _S78 : vec3<f32> = vec3<f32>(0.0f, 0.0f, 0.0f);
        if(onSurface_0)
        {
            var _S79 : vec3<f32> = GatherIndirect_0(world_10, worldNormal_1, viewPosition_2, FrameJitter_0(_S77, Globals_0.uFrameIndex_0));
            if((Globals_0.uDebugSource_0) == i32(4))
            {
                var found_4 : vec2<f32>;
                var litFound_4 : vec2<f32>;
                var agreement_4 : f32;
                var _S80 : bool = LookupCardHit_1(world_10, (vec3<f32>(0) - worldNormal_1), &(found_4), &(litFound_4), &(agreement_4));
                if(_S80)
                {
                    traced_0 = (textureSampleLevel((uFinal_0), (uFinalSampler_0), (litFound_4), (0.0f))).xyz;
                }
                else
                {
                    traced_0 = _S78;
                }
            }
            else
            {
                traced_0 = _S79;
            }
        }
        else
        {
            traced_0 = _S78;
        }
        var _S81 : vec2<u32> = vec2<u32>(_S77);
        if(onSurface_0)
        {
            nCount_2 = 1.0f;
        }
        else
        {
            nCount_2 = 0.0f;
        }
        textureStore((uIndirect_0), (_S81), (vec4<f32>(traced_0, nCount_2)));
        var _S82 : vec4<f32>;
        if(onSurface_0)
        {
            _S82 = vec4<f32>(world_10 - Globals_0.uCameraToCache_0, 1.0f);
        }
        else
        {
            _S82 = vec4<f32>(0.0f, 0.0f, 0.0f, 0.0f);
        }
        textureStore((uPosition_0), (_S81), (_S82));
        return;
    }
    if(!onSurface_0)
    {
        var _S83 : vec2<u32> = vec2<u32>(_S77);
        const _S84 : vec4<f32> = vec4<f32>(0.0f, 0.0f, 0.0f, 0.0f);
        textureStore((uIndirect_0), (_S83), (_S84));
        textureStore((uPosition_0), (_S83), (_S84));
        return;
    }
    var _S85 : vec3<i32> = vec3<i32>(_S77, i32(0));
    var current_0 : vec3<f32> = (textureLoad((uRaw_0), ((_S85)).xy, ((_S85)).z)).xyz;
    var _S86 : f32 = max(0.34999999403953552f, 0.05999999865889549f * length(viewPosition_2));
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
            var n_2 : vec2<i32> = _S77 + vec2<i32>(dx_0, dy_0);
            if((any((n_2 < vec2<i32>(i32(0), i32(0))))))
            {
                _S75 = true;
            }
            else
            {
                _S75 = (any((n_2 >= grid_0)));
            }
            if(_S75)
            {
                dx_0 = dx_0 + i32(1);
                continue;
            }
            var _S87 : vec3<i32> = vec3<i32>(n_2, i32(0));
            var raw_2 : vec4<f32> = (textureLoad((uRaw_0), ((_S87)).xy, ((_S87)).z));
            var nWorld_0 : vec3<f32>;
            var nView_0 : vec3<f32>;
            var nNormal_0 : vec3<f32>;
            if((raw_2.w) > 0.5f)
            {
                var _S88 : bool = ProbeSurface_0(n_2, &(nWorld_0), &(nView_0), &(nNormal_0));
                _S76 = _S88;
            }
            else
            {
                _S76 = false;
            }
            var _S89 : bool;
            if(_S76)
            {
                _S89 = (distance(nWorld_0, world_10)) < _S86;
            }
            else
            {
                _S89 = false;
            }
            var _S90 : bool;
            if(_S89)
            {
                _S90 = (dot(nNormal_0, worldNormal_1)) > 0.80000001192092896f;
            }
            else
            {
                _S90 = false;
            }
            var nMin_1 : vec3<f32>;
            var nMax_2 : vec3<f32>;
            var nSum_2 : vec3<f32>;
            if(_S90)
            {
                var _S91 : vec3<f32> = raw_2.xyz;
                var _S92 : vec3<f32> = max(nMax_0, _S91);
                var nSum_3 : vec3<f32> = nSum_1 + _S91;
                var nCount_3 : f32 = nCount_0 + 1.0f;
                nMin_1 = min(nMin_0, _S91);
                nMax_2 = _S92;
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
        var _S93 : i32 = dy_0 + i32(1);
        traced_0 = nMin_0;
        nMax_1 = nMax_0;
        dy_0 = _S93;
        nSum_0 = nSum_1;
        nCount_2 = nCount_0;
    }
    var est_0 : vec3<f32> = nSum_0 / vec3<f32>(nCount_2);
    var frames_0 : f32;
    if((Globals_0.uHasHistory_0) != i32(0))
    {
        var prevView_1 : vec3<f32> = Globals_0.uPrevWorldToView0_0 * vec3<f32>((world_10.x - Globals_0.uPrevCameraToCache_0.x)) + Globals_0.uPrevWorldToView1_0 * vec3<f32>((world_10.y - Globals_0.uPrevCameraToCache_0.y)) + Globals_0.uPrevWorldToView2_0 * vec3<f32>((world_10.z - Globals_0.uPrevCameraToCache_0.z));
        var _S94 : f32 = prevView_1.z;
        if(_S94 > 0.0f)
        {
            var _S95 : vec2<f32> = vec2<f32>(0.5f);
            var prevUv_0 : vec2<f32> = vec2<f32>(prevView_1.x * Globals_0.uPrevUnproject_0.x / _S94, -1.0f * (prevView_1.y * Globals_0.uPrevUnproject_0.y) / _S94) * _S95 + _S95;
            var prevUv_1 : vec2<f32>;
            if((Globals_0.uHasVelocity_0) != i32(0))
            {
                var screen_0 : vec2<f32> = vec2<f32>(f32(Globals_0.uScreenWidth_0), f32(Globals_0.uScreenHeight_0));
                var probeUv_1 : vec2<f32> = (vec2<f32>(min(_S77 * vec2<i32>(Globals_0.uProbeStep_0) + ProbeTileOffset_0(Globals_0.uFrameIndex_0, Globals_0.uProbeStep_0), vec2<i32>(Globals_0.uScreenWidth_0, Globals_0.uScreenHeight_0) - vec2<i32>(i32(1)))) + _S95) / screen_0;
                var motion_0 : vec2<f32> = (textureSampleLevel((uVelocity_0), (uVelocitySampler_0), (probeUv_1), (0.0f))).xy;
                var apart_0 : vec2<f32> = (motion_0 - (probeUv_1 - prevUv_0)) * screen_0;
                var _S96 : bool = (dot(apart_0, apart_0)) > 2.25f;
                if(_S96)
                {
                    prevUv_1 = probeUv_1 - motion_0;
                }
                else
                {
                    prevUv_1 = prevUv_0;
                }
                ownMotion_0 = _S96;
            }
            else
            {
                prevUv_1 = prevUv_0;
                ownMotion_0 = false;
            }
            if((all((prevUv_1 >= vec2<f32>(0.0f, 0.0f)))))
            {
                _S75 = (all((prevUv_1 <= vec2<f32>(1.0f, 1.0f))));
            }
            else
            {
                _S75 = false;
            }
            if(_S75)
            {
                var tc_0 : vec2<f32> = (prevUv_1 * vec2<f32>(f32(Globals_0.uScreenWidth_0), f32(Globals_0.uScreenHeight_0)) - vec2<f32>(ProbeTileOffset_0(Globals_0.uFrameIndex_0 - i32(1), Globals_0.uProbeStep_0)) - _S95) / vec2<f32>(f32(Globals_0.uProbeStep_0));
                var tcBase_0 : vec2<f32> = floor(tc_0);
                var _S97 : vec2<f32> = tc_0 - tcBase_0;
                var _S98 : f32 = 2.0f * viewPosition_2.z * f32(Globals_0.uProbeStep_0) / (Globals_0.uUnproject_0.y * f32(Globals_0.uScreenHeight_0));
                const _S99 : vec3<f32> = vec3<f32>(0.0f, 0.0f, 0.0f);
                dx_0 = i32(0);
                nMin_0 = _S99;
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
                    var _S100 : i32 = (dx_0 & (i32(1)));
                    var _S101 : i32 = (dx_0 >> (u32(1)));
                    var tap_0 : vec2<i32> = vec2<i32>(tcBase_0) + vec2<i32>(_S100, _S101);
                    if((any((tap_0 < vec2<i32>(i32(0), i32(0))))))
                    {
                        _S75 = true;
                    }
                    else
                    {
                        _S75 = (any((tap_0 >= grid_0)));
                    }
                    if(_S75)
                    {
                        dx_0 = dx_0 + i32(1);
                        continue;
                    }
                    var _S102 : vec3<i32> = vec3<i32>(tap_0, i32(0));
                    var tapPos_0 : vec4<f32> = (textureLoad((uPositionHistory_0), ((_S102)).xy, ((_S102)).z));
                    if((tapPos_0.w) <= 0.0f)
                    {
                        dx_0 = dx_0 + i32(1);
                        continue;
                    }
                    var planeDistance_0 : f32 = abs(dot(tapPos_0.xyz + Globals_0.uPrevCameraToCache_0 - world_10, worldNormal_1));
                    if(!ownMotion_0)
                    {
                        _S76 = planeDistance_0 > (max(0.05000000074505806f, 0.5f * _S98));
                    }
                    else
                    {
                        _S76 = false;
                    }
                    if(_S76)
                    {
                        dx_0 = dx_0 + i32(1);
                        continue;
                    }
                    if(_S100 == i32(0))
                    {
                        nCount_2 = 1.0f - _S97.x;
                    }
                    else
                    {
                        nCount_2 = _S97.x;
                    }
                    if(_S101 == i32(0))
                    {
                        frames_0 = 1.0f - _S97.y;
                    }
                    else
                    {
                        frames_0 = _S97.y;
                    }
                    var w_0 : f32 = nCount_2 * frames_0;
                    var tapHistory_0 : vec4<f32> = (textureLoad((uHistory_0), ((_S102)).xy, ((_S102)).z));
                    var frameSum_0 : f32 = nCount_0 + tapHistory_0.w * w_0;
                    var weightSum_0 : f32 = nCount_1 + w_0;
                    nMin_0 = nMin_0 + tapHistory_0.xyz * vec3<f32>(w_0);
                    nCount_0 = frameSum_0;
                    nCount_1 = weightSum_0;
                    dx_0 = dx_0 + i32(1);
                }
                if(nCount_1 > 0.00999999977648258f)
                {
                    var _S103 : vec3<f32> = vec3<f32>(0.5f);
                    var windowCentre_0 : vec3<f32> = _S103 * (traced_0 + nMax_1);
                    var _S104 : vec3<f32> = vec3<f32>(2.0f) * (_S103 * (nMax_1 - traced_0) + vec3<f32>(0.25f) * windowCentre_0 + vec3<f32>(0.00100000004749745f));
                    var _S105 : f32 = min(nCount_0 / nCount_1 + 1.0f, f32(Globals_0.uMaxAccumulated_0));
                    nMax_0 = mix(clamp(nMin_0 / vec3<f32>(nCount_1), windowCentre_0 - _S104, windowCentre_0 + _S104), est_0, vec3<f32>((1.0f / (1.0f + _S105))));
                    frames_0 = _S105;
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
    var _S106 : vec2<u32> = vec2<u32>(_S77);
    textureStore((uIndirect_0), (_S106), (vec4<f32>(nMax_0, frames_0)));
    textureStore((uPosition_0), (_S106), (vec4<f32>(world_10 - Globals_0.uCameraToCache_0, 1.0f)));
    return;
}

