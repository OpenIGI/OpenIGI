struct Globals_std140_0
{
    @align(16) uUnproject_0 : vec4<f32>,
    @align(16) uPrevUnproject_0 : vec4<f32>,
    @align(16) uViewToWorld0_0 : vec3<f32>,
    @align(16) uViewToWorld1_0 : vec3<f32>,
    @align(16) uViewToWorld2_0 : vec3<f32>,
    @align(16) uCameraToCache_0 : vec3<f32>,
    @align(16) uPrevWorldToView0_0 : vec3<f32>,
    @align(16) uPrevWorldToView1_0 : vec3<f32>,
    @align(16) uPrevWorldToView2_0 : vec3<f32>,
    @align(16) uPrevCameraToCache_0 : vec3<f32>,
    @align(16) uProbeWorldToView0_0 : vec3<f32>,
    @align(16) uProbeWorldToView1_0 : vec3<f32>,
    @align(16) uProbeWorldToView2_0 : vec3<f32>,
    @align(16) uProbeCameraToCache_0 : vec3<f32>,
    @align(4) uScreenWidth_0 : i32,
    @align(16) uScreenHeight_0 : i32,
    @align(4) uHasHistory_0 : i32,
    @align(8) uMaxFrames_0 : i32,
    @align(4) uProbe_0 : i32,
    @align(16) uSceneDepth_0 : i32,
    @align(4) uSceneNormal_0 : i32,
    @align(8) uHistory_0 : i32,
    @align(4) uHistoryPosition_0 : i32,
    @align(16) uProbePosition_0 : i32,
    @align(4) uVelocity_0 : i32,
    @align(8) uProbeStep_0 : i32,
    @align(4) uHasVelocity_0 : i32,
};

@binding(49) @group(0) var<uniform> g_0 : Globals_std140_0;
@binding(33) @group(0) var uSceneDepth_1 : texture_depth_2d;

@binding(65) @group(0) var uSceneDepthSampler_0 : sampler;

@binding(0) @group(0) var uAccum_0 : texture_storage_2d<rgba16float, write>;

@binding(1) @group(0) var uAccumPosition_0 : texture_storage_2d<rgba16float, write>;

@binding(34) @group(0) var uSceneNormal_1 : texture_2d<f32>;

@binding(66) @group(0) var uSceneNormalSampler_0 : sampler;

@binding(32) @group(0) var uProbe_1 : texture_2d<f32>;

@binding(37) @group(0) var uProbePosition_1 : texture_2d<f32>;

@binding(38) @group(0) var uVelocity_1 : texture_2d<f32>;

@binding(70) @group(0) var uVelocitySampler_0 : sampler;

@binding(36) @group(0) var uHistoryPosition_1 : texture_2d<f32>;

@binding(35) @group(0) var uHistory_1 : texture_2d<f32>;

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

fn DepthComputeSample_0( depth_1 : texture_depth_2d,  linear_0 : sampler,  uv_1 : vec2<f32>) -> f32
{
    return DepthBilinearLoaded_0(depth_1, uv_1);
}

fn IsSkyDepth_0( depth_2 : f32) -> bool
{
    return depth_2 <= 0.0f;
}

fn ViewPositionAt_0( uv_2 : vec2<f32>,  depth_3 : f32) -> vec3<f32>
{
    var viewZ_0 : f32 = 2.0f * g_0.uUnproject_0.z * g_0.uUnproject_0.w / (g_0.uUnproject_0.w + g_0.uUnproject_0.z - (1.0f - 2.0f * depth_3) * (g_0.uUnproject_0.w - g_0.uUnproject_0.z));
    var ndc_0 : vec2<f32> = uv_2 * vec2<f32>(2.0f) - vec2<f32>(1.0f);
    return vec3<f32>(ndc_0.x * viewZ_0 / g_0.uUnproject_0.x, -1.0f * (ndc_0.y * viewZ_0) / g_0.uUnproject_0.y, viewZ_0);
}

fn ComputeSample_0( texture_0 : texture_2d<f32>,  sampler_0 : sampler,  uv_3 : vec2<f32>) -> vec4<f32>
{
    return (textureSampleLevel((texture_0), (sampler_0), (uv_3), (0.0f)));
}

fn ProjectToUv_0( viewPosition_0 : vec3<f32>) -> vec2<f32>
{
    var _S8 : f32 = viewPosition_0.z;
    var _S9 : vec2<f32> = vec2<f32>(0.5f);
    return vec2<f32>(viewPosition_0.x * g_0.uUnproject_0.x / _S8, -1.0f * (viewPosition_0.y * g_0.uUnproject_0.y) / _S8) * _S9 + _S9;
}

fn UpsampleProbe_0( uv_4 : vec2<f32>,  pixelWorld_0 : vec3<f32>,  pixelViewZ_0 : f32,  pixelNormal_0 : vec3<f32>) -> vec3<f32>
{
    var width_1 : u32;
    var height_1 : u32;
    {var dim = textureDimensions((uProbe_1));((width_1)) = dim.x;((height_1)) = dim.y;};
    var _S10 : vec2<i32> = vec2<i32>(i32(width_1), i32(height_1));
    var probeView_0 : vec3<f32> = g_0.uProbeWorldToView0_0 * vec3<f32>((pixelWorld_0.x - g_0.uProbeCameraToCache_0.x)) + g_0.uProbeWorldToView1_0 * vec3<f32>((pixelWorld_0.y - g_0.uProbeCameraToCache_0.y)) + g_0.uProbeWorldToView2_0 * vec3<f32>((pixelWorld_0.z - g_0.uProbeCameraToCache_0.z));
    var probeFrameUv_0 : vec2<f32>;
    if((probeView_0.z) > (g_0.uUnproject_0.z))
    {
        probeFrameUv_0 = ProjectToUv_0(probeView_0);
    }
    else
    {
        probeFrameUv_0 = uv_4;
    }
    var step_0 : f32 = f32(max(g_0.uProbeStep_0, i32(1)));
    var probeCoord_0 : vec2<f32> = (probeFrameUv_0 * vec2<f32>(f32(g_0.uScreenWidth_0), f32(g_0.uScreenHeight_0)) - vec2<f32>((0.5f * step_0)) - vec2<f32>(0.5f)) / vec2<f32>(step_0);
    var base_0 : vec2<f32> = floor(probeCoord_0);
    var _S11 : vec2<f32> = probeCoord_0 - base_0;
    const _S12 : vec3<f32> = vec3<f32>(0.0f, 0.0f, 0.0f);
    var _S13 : bool = (dot(pixelNormal_0, pixelNormal_0)) > 0.5f;
    var nearestDistance_0 : f32 = 3.39999995214436425e+38f;
    var nearestValue_0 : vec3<f32> = _S12;
    var y_0 : i32 = i32(0);
    var strictSum_0 : vec3<f32> = _S12;
    var strictWeight_0 : f32 = 0.0f;
    var looseSum_0 : vec3<f32> = _S12;
    var looseWeight_0 : f32 = 0.0f;
    for(;;)
    {
        if(y_0 < i32(2))
        {
        }
        else
        {
            break;
        }
        var nearestDistance_1 : f32 = nearestDistance_0;
        var nearestValue_1 : vec3<f32> = nearestValue_0;
        var x_0 : i32 = i32(0);
        var strictSum_1 : vec3<f32> = strictSum_0;
        var strictWeight_1 : f32 = strictWeight_0;
        var looseSum_1 : vec3<f32> = looseSum_0;
        var looseWeight_1 : f32 = looseWeight_0;
        for(;;)
        {
            if(x_0 < i32(2))
            {
            }
            else
            {
                break;
            }
            var coord_0 : vec2<i32> = vec2<i32>(base_0) + vec2<i32>(x_0, y_0);
            var _S14 : bool;
            if((any((coord_0 < vec2<i32>(i32(0), i32(0))))))
            {
                _S14 = true;
            }
            else
            {
                _S14 = (any((coord_0 >= _S10)));
            }
            if(_S14)
            {
                x_0 = x_0 + i32(1);
                continue;
            }
            var _S15 : vec3<i32> = vec3<i32>(coord_0, i32(0));
            var probePosition_0 : vec4<f32> = (textureLoad((uProbePosition_1), ((_S15)).xy, ((_S15)).z));
            if((probePosition_0.w) <= 0.0f)
            {
                x_0 = x_0 + i32(1);
                continue;
            }
            var offset_0 : vec3<f32> = probePosition_0.xyz + g_0.uProbeCameraToCache_0 - pixelWorld_0;
            var planeDistance_0 : f32;
            if(_S13)
            {
                planeDistance_0 = abs(dot(offset_0, pixelNormal_0));
            }
            else
            {
                planeDistance_0 = length(offset_0);
            }
            var relative_0 : f32 = planeDistance_0 / max(pixelViewZ_0, 0.00100000004749745f);
            var relative2_0 : f32 = relative_0 * relative_0;
            var _S16 : f32;
            if(x_0 == i32(0))
            {
                _S16 = 1.0f - _S11.x;
            }
            else
            {
                _S16 = _S11.x;
            }
            var _S17 : f32;
            if(y_0 == i32(0))
            {
                _S17 = 1.0f - _S11.y;
            }
            else
            {
                _S17 = _S11.y;
            }
            var bilinear_0 : f32 = _S16 * _S17;
            var value_0 : vec3<f32> = (textureLoad((uProbe_1), ((_S15)).xy, ((_S15)).z)).xyz;
            var strict_0 : f32 = bilinear_0 * exp2(-10000.0f * relative2_0);
            var loose_0 : f32 = bilinear_0 * exp2(-1000.0f * relative2_0);
            var strictSum_2 : vec3<f32> = strictSum_1 + value_0 * vec3<f32>(strict_0);
            var strictWeight_2 : f32 = strictWeight_1 + strict_0;
            var looseSum_2 : vec3<f32> = looseSum_1 + value_0 * vec3<f32>(loose_0);
            var looseWeight_2 : f32 = looseWeight_1 + loose_0;
            var apart_0 : f32 = dot(offset_0, offset_0);
            var nearestDistance_2 : f32;
            var nearestValue_2 : vec3<f32>;
            if(apart_0 < nearestDistance_1)
            {
                nearestDistance_2 = apart_0;
                nearestValue_2 = value_0;
            }
            else
            {
                nearestDistance_2 = nearestDistance_1;
                nearestValue_2 = nearestValue_1;
            }
            nearestDistance_1 = nearestDistance_2;
            nearestValue_1 = nearestValue_2;
            strictSum_1 = strictSum_2;
            strictWeight_1 = strictWeight_2;
            looseSum_1 = looseSum_2;
            looseWeight_1 = looseWeight_2;
            x_0 = x_0 + i32(1);
        }
        var _S18 : i32 = y_0 + i32(1);
        nearestDistance_0 = nearestDistance_1;
        nearestValue_0 = nearestValue_1;
        y_0 = _S18;
        strictSum_0 = strictSum_1;
        strictWeight_0 = strictWeight_1;
        looseSum_0 = looseSum_1;
        looseWeight_0 = looseWeight_1;
    }
    if(strictWeight_0 > 0.00009999999747379f)
    {
        return strictSum_0 / vec3<f32>(strictWeight_0);
    }
    if(looseWeight_0 > 0.00009999999747379f)
    {
        return looseSum_0 / vec3<f32>(looseWeight_0);
    }
    return nearestValue_0;
}

@compute
@workgroup_size(8, 8, 1)
fn computeMain(@builtin(global_invocation_id) id_0 : vec3<u32>)
{
    var nNormal_0 : vec3<f32>;
    var off_0 : vec2<f32>;
    var _S19 : vec2<i32> = vec2<i32>(id_0.xy);
    var ownMotion_0 : bool;
    if((_S19.x) >= (g_0.uScreenWidth_0))
    {
        ownMotion_0 = true;
    }
    else
    {
        ownMotion_0 = (_S19.y) >= (g_0.uScreenHeight_0);
    }
    if(ownMotion_0)
    {
        return;
    }
    var _S20 : vec2<f32> = vec2<f32>(0.5f);
    var uv_5 : vec2<f32> = (vec2<f32>(_S19) + _S20) / vec2<f32>(f32(g_0.uScreenWidth_0), f32(g_0.uScreenHeight_0));
    var depth_4 : f32 = DepthComputeSample_0(uSceneDepth_1, uSceneDepthSampler_0, uv_5);
    if(IsSkyDepth_0(depth_4))
    {
        var _S21 : vec2<u32> = vec2<u32>(_S19);
        const _S22 : vec4<f32> = vec4<f32>(0.0f, 0.0f, 0.0f, 0.0f);
        textureStore((uAccum_0), (_S21), (_S22));
        textureStore((uAccumPosition_0), (_S21), (_S22));
        return;
    }
    var viewPosition_1 : vec3<f32> = ViewPositionAt_0(uv_5, depth_4);
    var _S23 : f32 = viewPosition_1.z;
    var world_0 : vec3<f32> = g_0.uCameraToCache_0 + g_0.uViewToWorld0_0 * vec3<f32>(viewPosition_1.x) + g_0.uViewToWorld1_0 * vec3<f32>(viewPosition_1.y) + g_0.uViewToWorld2_0 * vec3<f32>(_S23);
    var _S24 : vec3<f32> = vec3<f32>(2.0f);
    var _S25 : vec3<f32> = vec3<f32>(1.0f);
    var storedNormal_0 : vec3<f32> = ComputeSample_0(uSceneNormal_1, uSceneNormalSampler_0, uv_5).xyz * _S24 - _S25;
    var storedLength_0 : f32 = length(storedNormal_0);
    var pixelNormal_1 : vec3<f32>;
    if(storedLength_0 > 0.00100000004749745f)
    {
        pixelNormal_1 = storedNormal_0 / vec3<f32>(storedLength_0);
    }
    else
    {
        pixelNormal_1 = vec3<f32>(0.0f, 0.0f, 0.0f);
    }
    var current_0 : vec3<f32> = UpsampleProbe_0(uv_5, world_0, _S23, pixelNormal_1);
    var _S26 : vec2<f32> = vec2<f32>(1.0f) / vec2<f32>(f32(g_0.uScreenWidth_0), f32(g_0.uScreenHeight_0));
    var _S27 : f32 = f32(max(g_0.uProbeStep_0, i32(2)));
    var _S28 : vec3<f32> = current_0 * current_0;
    var s_0 : i32 = i32(0);
    var m1_0 : vec3<f32> = current_0;
    var m2_0 : vec3<f32> = _S28;
    var samples_0 : f32 = 1.0f;
    for(;;)
    {
        if(s_0 < i32(4))
        {
        }
        else
        {
            break;
        }
        if(s_0 == i32(0))
        {
            off_0 = vec2<f32>(1.0f, 0.0f);
        }
        else
        {
            if(s_0 == i32(1))
            {
                off_0 = vec2<f32>(-1.0f, 0.0f);
            }
            else
            {
                if(s_0 == i32(2))
                {
                    off_0 = vec2<f32>(0.0f, 1.0f);
                }
                else
                {
                    off_0 = vec2<f32>(0.0f, -1.0f);
                }
            }
        }
        var nuv_0 : vec2<f32> = clamp(uv_5 + off_0 * _S26 * vec2<f32>(_S27), vec2<f32>(0.0f, 0.0f), vec2<f32>(1.0f, 1.0f));
        var nDepth_0 : f32 = DepthComputeSample_0(uSceneDepth_1, uSceneDepthSampler_0, nuv_0);
        if(IsSkyDepth_0(nDepth_0))
        {
            s_0 = s_0 + i32(1);
            continue;
        }
        var nView_0 : vec3<f32> = ViewPositionAt_0(nuv_0, nDepth_0);
        var _S29 : f32 = nView_0.z;
        var nWorld_0 : vec3<f32> = g_0.uCameraToCache_0 + g_0.uViewToWorld0_0 * vec3<f32>(nView_0.x) + g_0.uViewToWorld1_0 * vec3<f32>(nView_0.y) + g_0.uViewToWorld2_0 * vec3<f32>(_S29);
        var nNormal_1 : vec3<f32> = ComputeSample_0(uSceneNormal_1, uSceneNormalSampler_0, nuv_0).xyz * _S24 - _S25;
        var nLength_0 : f32 = length(nNormal_1);
        if(nLength_0 > 0.00100000004749745f)
        {
            nNormal_0 = nNormal_1 / vec3<f32>(nLength_0);
        }
        else
        {
            nNormal_0 = vec3<f32>(0.0f, 0.0f, 0.0f);
        }
        var neighbour_0 : vec3<f32> = UpsampleProbe_0(nuv_0, nWorld_0, _S29, nNormal_0);
        var m2_1 : vec3<f32> = m2_0 + neighbour_0 * neighbour_0;
        var samples_1 : f32 = samples_0 + 1.0f;
        m1_0 = m1_0 + neighbour_0;
        m2_0 = m2_1;
        samples_0 = samples_1;
        s_0 = s_0 + i32(1);
    }
    var mean_0 : vec3<f32> = m1_0 / vec3<f32>(samples_0);
    const _S30 : vec3<f32> = vec3<f32>(0.0f, 0.0f, 0.0f);
    var _S31 : vec3<f32> = vec3<f32>(1.5f) * sqrt(max(m2_0 / vec3<f32>(samples_0) - mean_0 * mean_0, _S30));
    var clampLo_0 : vec3<f32> = mean_0 - _S31;
    var clampHi_0 : vec3<f32> = mean_0 + _S31;
    var accumulated_0 : vec3<f32>;
    var frames_0 : f32;
    if((g_0.uHasHistory_0) != i32(0))
    {
        var prevView_0 : vec3<f32> = g_0.uPrevWorldToView0_0 * vec3<f32>((world_0.x - g_0.uPrevCameraToCache_0.x)) + g_0.uPrevWorldToView1_0 * vec3<f32>((world_0.y - g_0.uPrevCameraToCache_0.y)) + g_0.uPrevWorldToView2_0 * vec3<f32>((world_0.z - g_0.uPrevCameraToCache_0.z));
        var _S32 : f32 = prevView_0.z;
        if(_S32 > 0.0f)
        {
            var prevUv_0 : vec2<f32> = vec2<f32>(prevView_0.x * g_0.uPrevUnproject_0.x / _S32, -1.0f * (prevView_0.y * g_0.uPrevUnproject_0.y) / _S32) * _S20 + _S20;
            if((g_0.uHasVelocity_0) != i32(0))
            {
                var motion_0 : vec2<f32> = (textureSampleLevel((uVelocity_1), (uVelocitySampler_0), (uv_5), (0.0f))).xy;
                var apart_1 : vec2<f32> = (motion_0 - (uv_5 - prevUv_0)) * vec2<f32>(f32(g_0.uScreenWidth_0), f32(g_0.uScreenHeight_0));
                var _S33 : bool = (dot(apart_1, apart_1)) > 2.25f;
                if(_S33)
                {
                    off_0 = uv_5 - motion_0;
                }
                else
                {
                    off_0 = prevUv_0;
                }
                ownMotion_0 = _S33;
            }
            else
            {
                off_0 = prevUv_0;
                ownMotion_0 = false;
            }
            var _S34 : bool;
            if((all((off_0 >= vec2<f32>(0.0f, 0.0f)))))
            {
                _S34 = (all((off_0 <= vec2<f32>(1.0f, 1.0f))));
            }
            else
            {
                _S34 = false;
            }
            if(_S34)
            {
                var tc_0 : vec2<f32> = off_0 * vec2<f32>(f32(g_0.uScreenWidth_0), f32(g_0.uScreenHeight_0)) - _S20;
                var tcBase_0 : vec2<f32> = floor(tc_0);
                var _S35 : vec2<f32> = tc_0 - tcBase_0;
                var _S36 : f32 = 2.0f * _S23 / (g_0.uUnproject_0.y * f32(g_0.uScreenHeight_0));
                var t_0 : i32 = i32(0);
                nNormal_0 = _S30;
                var frameSum_0 : f32 = 0.0f;
                var weightSum_0 : f32 = 0.0f;
                for(;;)
                {
                    if(t_0 < i32(4))
                    {
                    }
                    else
                    {
                        break;
                    }
                    var _S37 : i32 = (t_0 & (i32(1)));
                    var _S38 : i32 = (t_0 >> (u32(1)));
                    var tap_0 : vec2<i32> = vec2<i32>(tcBase_0) + vec2<i32>(_S37, _S38);
                    if((any((tap_0 < vec2<i32>(i32(0), i32(0))))))
                    {
                        _S34 = true;
                    }
                    else
                    {
                        _S34 = (any((tap_0 >= vec2<i32>(g_0.uScreenWidth_0, g_0.uScreenHeight_0))));
                    }
                    if(_S34)
                    {
                        t_0 = t_0 + i32(1);
                        continue;
                    }
                    var _S39 : vec3<i32> = vec3<i32>(tap_0, i32(0));
                    var tapPos_0 : vec4<f32> = (textureLoad((uHistoryPosition_1), ((_S39)).xy, ((_S39)).z));
                    if((tapPos_0.w) <= 0.0f)
                    {
                        t_0 = t_0 + i32(1);
                        continue;
                    }
                    var planeDistance_1 : f32 = abs(dot(tapPos_0.xyz + g_0.uPrevCameraToCache_0 - world_0, pixelNormal_1));
                    var _S40 : bool;
                    if(!ownMotion_0)
                    {
                        _S40 = planeDistance_1 > (max(0.01999999955296516f, 4.0f * _S36));
                    }
                    else
                    {
                        _S40 = false;
                    }
                    if(_S40)
                    {
                        t_0 = t_0 + i32(1);
                        continue;
                    }
                    if(_S37 == i32(0))
                    {
                        samples_0 = 1.0f - _S35.x;
                    }
                    else
                    {
                        samples_0 = _S35.x;
                    }
                    if(_S38 == i32(0))
                    {
                        frames_0 = 1.0f - _S35.y;
                    }
                    else
                    {
                        frames_0 = _S35.y;
                    }
                    var w_0 : f32 = samples_0 * frames_0;
                    var tapHistory_0 : vec4<f32> = (textureLoad((uHistory_1), ((_S39)).xy, ((_S39)).z));
                    var frameSum_1 : f32 = frameSum_0 + tapHistory_0.w * w_0;
                    var weightSum_1 : f32 = weightSum_0 + w_0;
                    nNormal_0 = nNormal_0 + tapHistory_0.xyz * vec3<f32>(w_0);
                    frameSum_0 = frameSum_1;
                    weightSum_0 = weightSum_1;
                    t_0 = t_0 + i32(1);
                }
                if(weightSum_0 > 0.00999999977648258f)
                {
                    var history_0 : vec4<f32> = vec4<f32>(nNormal_0 / vec3<f32>(weightSum_0), frameSum_0 / weightSum_0);
                    var _S41 : f32 = min(history_0.w + 1.0f, f32(g_0.uMaxFrames_0));
                    accumulated_0 = mix(clamp(history_0.xyz, clampLo_0, clampHi_0), current_0, vec3<f32>((1.0f / (1.0f + _S41))));
                    frames_0 = _S41;
                }
                else
                {
                    accumulated_0 = current_0;
                    frames_0 = 0.0f;
                }
            }
            else
            {
                accumulated_0 = current_0;
                frames_0 = 0.0f;
            }
        }
        else
        {
            accumulated_0 = current_0;
            frames_0 = 0.0f;
        }
    }
    else
    {
        accumulated_0 = current_0;
        frames_0 = 0.0f;
    }
    var _S42 : vec2<u32> = vec2<u32>(_S19);
    textureStore((uAccum_0), (_S42), (vec4<f32>(accumulated_0, frames_0)));
    textureStore((uAccumPosition_0), (_S42), (vec4<f32>(world_0 - g_0.uCameraToCache_0, 1.0f)));
    return;
}

