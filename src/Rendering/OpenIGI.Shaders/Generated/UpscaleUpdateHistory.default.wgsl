struct Params_std140_0
{
    @align(16) input_0 : vec4<f32>,
    @align(16) history_0 : vec4<f32>,
    @align(16) control_0 : vec4<f32>,
    @align(16) control2_0 : vec4<f32>,
    @align(16) control3_0 : vec4<f32>,
};

@binding(20) @group(0) var<uniform> p_0 : Params_std140_0;
@binding(0) @group(0) var uCurrent_0 : texture_2d<f32>;

@binding(1) @group(0) var uCurrentSampler_0 : sampler;

@binding(18) @group(0) var uDepth_0 : texture_depth_2d;

@binding(8) @group(0) var uHistoryColour_0 : texture_storage_2d<rgba16float, write>;

@binding(9) @group(0) var uHistoryMetadata_0 : texture_storage_2d<rgba32float, write>;

@binding(2) @group(0) var uVelocity_0 : texture_2d<f32>;

@binding(3) @group(0) var uVelocitySampler_0 : sampler;

@binding(10) @group(0) var uHistoryRejection_0 : texture_2d<f32>;

@binding(11) @group(0) var uHistoryRejectionSampler_0 : sampler;

@binding(14) @group(0) var uShadingRejection_0 : texture_2d<f32>;

@binding(15) @group(0) var uShadingRejectionSampler_0 : sampler;

@binding(16) @group(0) var uRejectionBlend_0 : texture_2d<f32>;

@binding(17) @group(0) var uRejectionBlendSampler_0 : sampler;

@binding(12) @group(0) var uSpatialAa_0 : texture_2d<f32>;

@binding(4) @group(0) var uPrevHistoryColour_0 : texture_2d<f32>;

@binding(5) @group(0) var uPrevHistoryColourSampler_0 : sampler;

@binding(6) @group(0) var uPrevHistoryMetadata_0 : texture_2d<f32>;

@binding(7) @group(0) var uPrevHistoryMetadataSampler_0 : sampler;

fn isinf_0( x_0 : f32) -> bool
{
    var _S1 : u32 = (bitcast<u32>((x_0)));
    var _S2 : u32 = (_S1 & (u32(8388607)));
    var _S3 : bool;
    if(((((_S1 >> (u32(23)))) & (u32(255)))) == u32(255))
    {
        _S3 = _S2 == u32(0);
    }
    else
    {
        _S3 = false;
    }
    return _S3;
}

fn isnan_0( x_1 : f32) -> bool
{
    var _S4 : u32 = (bitcast<u32>((x_1)));
    var _S5 : u32 = (_S4 & (u32(8388607)));
    var _S6 : bool;
    if(((((_S4 >> (u32(23)))) & (u32(255)))) == u32(255))
    {
        _S6 = _S5 != u32(0);
    }
    else
    {
        _S6 = false;
    }
    return _S6;
}

fn isinf_1( x_2 : vec3<f32>) -> vec3<bool>
{
    var result_0 : vec3<bool>;
    var i_0 : i32 = i32(0);
    for(;;)
    {
        if(i_0 < i32(3))
        {
        }
        else
        {
            break;
        }
        result_0[i_0] = isinf_0(x_2[i_0]);
        i_0 = i_0 + i32(1);
    }
    return result_0;
}

fn isnan_1( x_3 : vec3<f32>) -> vec3<bool>
{
    var result_1 : vec3<bool>;
    var i_1 : i32 = i32(0);
    for(;;)
    {
        if(i_1 < i32(3))
        {
        }
        else
        {
            break;
        }
        result_1[i_1] = isnan_0(x_3[i_1]);
        i_1 = i_1 + i32(1);
    }
    return result_1;
}

fn DepthGatherLoaded_0( depth_0 : texture_depth_2d,  uv_0 : vec2<f32>) -> vec4<f32>
{
    var width_0 : u32;
    var height_0 : u32;
    {var dim = textureDimensions((depth_0));((width_0)) = dim.x;((height_0)) = dim.y;};
    var last_0 : vec2<i32> = vec2<i32>(i32(width_0) - i32(1), i32(height_0) - i32(1));
    var _S7 : vec2<i32> = vec2<i32>(floor(uv_0 * vec2<f32>(f32(width_0), f32(height_0)) - vec2<f32>(0.5f)));
    const _S8 : vec2<i32> = vec2<i32>(i32(0), i32(0));
    var _S9 : vec3<i32> = vec3<i32>(clamp(_S7 + vec2<i32>(i32(0), i32(1)), _S8, last_0), i32(0));
    var _S10 : vec3<i32> = vec3<i32>(clamp(_S7 + vec2<i32>(i32(1), i32(1)), _S8, last_0), i32(0));
    var _S11 : vec3<i32> = vec3<i32>(clamp(_S7 + vec2<i32>(i32(1), i32(0)), _S8, last_0), i32(0));
    var _S12 : vec3<i32> = vec3<i32>(clamp(_S7, _S8, last_0), i32(0));
    return vec4<f32>((textureLoad((depth_0), ((_S9)).xy, ((_S9)).z)), (textureLoad((depth_0), ((_S10)).xy, ((_S10)).z)), (textureLoad((depth_0), ((_S11)).xy, ((_S11)).z)), (textureLoad((depth_0), ((_S12)).xy, ((_S12)).z)));
}

fn KernelWeight_0( k_0 : f32,  delta_0 : vec2<f32>) -> f32
{
    var s_0 : f32 = saturate(k_0 * k_0 * dot(delta_0, delta_0));
    return saturate(1.0f - 1.89999997615814209f * s_0 + 0.89999997615814209f * s_0 * s_0 + 0.00499999988824129f);
}

fn HdrWeight_0( c_0 : vec3<f32>) -> f32
{
    return max(0.00009999999747379f, 1.0f / (c_0.x + 2.0f * c_0.y + c_0.z + 4.0f));
}

fn ToBox_0( x_4 : vec3<f32>) -> vec3<f32>
{
    var t_0 : vec3<f32> = x_4 / (x_4 + vec3<f32>(0.17000000178813934f));
    return t_0 * t_0;
}

fn SampleHistoryCatmullRom_0( uv_1 : vec2<f32>) -> vec4<f32>
{
    var samplePos_0 : vec2<f32> = uv_1 * p_0.history_0.xy;
    var _S13 : vec2<f32> = vec2<f32>(0.5f);
    var t1_0 : vec2<f32> = floor(samplePos_0 - _S13) + _S13;
    var f_0 : vec2<f32> = samplePos_0 - t1_0;
    var _S14 : vec2<f32> = _S13 * f_0;
    var _S15 : vec2<f32> = vec2<f32>(1.0f);
    var _S16 : vec2<f32> = vec2<f32>(-0.5f);
    var w0_0 : vec2<f32> = f_0 * (_S16 + f_0 * (_S15 - _S14));
    var _S17 : vec2<f32> = f_0 * f_0;
    var _S18 : vec2<f32> = vec2<f32>(1.5f) * f_0;
    var _S19 : vec2<f32> = vec2<f32>(2.0f);
    var w2_0 : vec2<f32> = f_0 * (_S13 + f_0 * (_S19 - _S18));
    var w3_0 : vec2<f32> = _S17 * (_S16 + _S14);
    var w12_0 : vec2<f32> = _S15 + _S17 * (vec2<f32>(-2.5f) + _S18) + w2_0;
    var uv0_0 : vec2<f32> = (t1_0 - _S15) * p_0.history_0.zw;
    var uv12_0 : vec2<f32> = (t1_0 + w2_0 / max(w12_0, vec2<f32>(0.00000999999974738f))) * p_0.history_0.zw;
    var uv3_0 : vec2<f32> = (t1_0 + _S19) * p_0.history_0.zw;
    var _S20 : f32 = uv0_0.x;
    var _S21 : f32 = uv12_0.x;
    var _S22 : f32 = uv3_0.x;
    var _S23 : f32 = uv0_0.y;
    var _S24 : f32 = uv12_0.y;
    var _S25 : f32 = uv3_0.y;
    var _S26 : f32 = w0_0.x;
    var _S27 : f32 = w12_0.x;
    var _S28 : f32 = w3_0.x;
    var _S29 : f32 = w0_0.y;
    var _S30 : f32 = w12_0.y;
    var _S31 : f32 = w3_0.y;
    return (textureSampleLevel((uPrevHistoryColour_0), (uPrevHistoryColourSampler_0), (vec2<f32>(_S20, _S23)), (0.0f))) * vec4<f32>((_S26 * _S29)) + (textureSampleLevel((uPrevHistoryColour_0), (uPrevHistoryColourSampler_0), (vec2<f32>(_S21, _S23)), (0.0f))) * vec4<f32>((_S27 * _S29)) + (textureSampleLevel((uPrevHistoryColour_0), (uPrevHistoryColourSampler_0), (vec2<f32>(_S22, _S23)), (0.0f))) * vec4<f32>((_S28 * _S29)) + (textureSampleLevel((uPrevHistoryColour_0), (uPrevHistoryColourSampler_0), (vec2<f32>(_S20, _S24)), (0.0f))) * vec4<f32>((_S26 * _S30)) + (textureSampleLevel((uPrevHistoryColour_0), (uPrevHistoryColourSampler_0), (vec2<f32>(_S21, _S24)), (0.0f))) * vec4<f32>((_S27 * _S30)) + (textureSampleLevel((uPrevHistoryColour_0), (uPrevHistoryColourSampler_0), (vec2<f32>(_S22, _S24)), (0.0f))) * vec4<f32>((_S28 * _S30)) + (textureSampleLevel((uPrevHistoryColour_0), (uPrevHistoryColourSampler_0), (vec2<f32>(_S20, _S25)), (0.0f))) * vec4<f32>((_S26 * _S31)) + (textureSampleLevel((uPrevHistoryColour_0), (uPrevHistoryColourSampler_0), (vec2<f32>(_S21, _S25)), (0.0f))) * vec4<f32>((_S27 * _S31)) + (textureSampleLevel((uPrevHistoryColour_0), (uPrevHistoryColourSampler_0), (vec2<f32>(_S22, _S25)), (0.0f))) * vec4<f32>((_S28 * _S31));
}

fn IsFiniteColour_0( c_1 : vec3<f32>) -> bool
{
    var _S32 : bool;
    if((any((isnan_1(c_1)))))
    {
        _S32 = true;
    }
    else
    {
        _S32 = (any((isinf_1(c_1))));
    }
    return !_S32;
}

fn FromBox_0( s_1 : vec3<f32>) -> vec3<f32>
{
    var g_0 : vec3<f32> = sqrt(max(s_1, vec3<f32>(0.0f)));
    return g_0 * vec3<f32>(0.17000000178813934f) / max(vec3<f32>(1.0f) - g_0, vec3<f32>(0.00009999999747379f));
}

fn Luma_0( c_2 : vec3<f32>) -> f32
{
    return c_2.x + 2.0f * c_2.y + c_2.z;
}

@compute
@workgroup_size(8, 8, 1)
fn computeMain(@builtin(global_invocation_id) id_0 : vec3<u32>)
{
    var cov_0 : f32;
    var sumColour_0 : vec3<f32>;
    var _S33 : vec2<i32> = vec2<i32>(id_0.xy);
    var _S34 : vec2<i32> = vec2<i32>(p_0.history_0.xy);
    var offScreen_0 : bool;
    if((_S33.x) >= (_S34.x))
    {
        offScreen_0 = true;
    }
    else
    {
        offScreen_0 = (_S33.y) >= (_S34.y);
    }
    if(offScreen_0)
    {
        return;
    }
    var _S35 : f32 = max(1.0f, p_0.control_0.y);
    var _S36 : f32 = max(p_0.control_0.z, 1.0f);
    var jitter_0 : vec2<f32> = vec2<f32>(p_0.control_0.w, p_0.control2_0.x);
    var inputSize_0 : vec2<f32> = p_0.input_0.xy;
    var _S37 : vec2<i32> = vec2<i32>(inputSize_0);
    var _S38 : vec2<f32> = vec2<f32>(0.5f);
    var inputPos_0 : vec2<f32> = (vec2<f32>(_S33) + _S38) * inputSize_0 / p_0.history_0.xy;
    var uv_2 : vec2<f32> = inputPos_0 / inputSize_0;
    var _S39 : vec3<f32> = vec3<f32>(0.0f);
    var bilin_0 : vec3<f32> = max((textureSampleLevel((uCurrent_0), (uCurrentSampler_0), (uv_2), (0.0f))).xyz, _S39);
    var depths_0 : vec4<f32> = DepthGatherLoaded_0(uDepth_0, uv_2);
    var foreground_0 : bool = (max(max(depths_0.x, depths_0.y), max(depths_0.z, depths_0.w))) >= 0.89999997615814209f;
    var sj_0 : vec2<f32>;
    if(foreground_0)
    {
        sj_0 = vec2<f32>(0.0f, 0.0f);
    }
    else
    {
        sj_0 = jitter_0;
    }
    var uvJ_0 : vec2<f32> = uv_2 + sj_0 / inputSize_0;
    var bilinJ_0 : vec3<f32> = max((textureSampleLevel((uCurrent_0), (uCurrentSampler_0), (uvJ_0), (0.0f))).xyz, _S39);
    if((p_0.control_0.x) < 0.5f)
    {
        var _S40 : vec2<u32> = vec2<u32>(_S33);
        textureStore((uHistoryColour_0), (_S40), (vec4<f32>(bilin_0, 1.0f)));
        textureStore((uHistoryMetadata_0), (_S40), (vec4<f32>(1.0f, 0.0f, 0.0f, 0.0f)));
        return;
    }
    var v_0 : vec2<f32> = (textureSampleLevel((uVelocity_0), (uVelocitySampler_0), (uvJ_0), (0.0f))).xy;
    var prevUv_0 : vec2<f32> = uv_2 - v_0;
    var grid_0 : vec2<f32>;
    if((abs(p_0.control_0.z - p_0.control3_0.w)) > 0.00000999999974738f)
    {
        grid_0 = p_0.history_0.xy / vec2<f32>(max(p_0.control3_0.w, 1.0f));
    }
    else
    {
        grid_0 = inputSize_0;
    }
    var speed_0 : f32 = length(v_0 * grid_0);
    if((any((prevUv_0 < vec2<f32>(0.0f)))))
    {
        offScreen_0 = true;
    }
    else
    {
        offScreen_0 = (any((prevUv_0 > vec2<f32>(1.0f))));
    }
    var believable_0 : f32;
    if((p_0.control2_0.y) > 0.5f)
    {
        believable_0 = (textureSampleLevel((uHistoryRejection_0), (uHistoryRejectionSampler_0), (uvJ_0), (0.0f))).x;
    }
    else
    {
        believable_0 = 1.0f;
    }
    var shading_0 : f32;
    if((p_0.control2_0.w) > 0.5f)
    {
        shading_0 = (textureSampleLevel((uShadingRejection_0), (uShadingRejectionSampler_0), (uvJ_0), (0.0f))).x;
    }
    else
    {
        shading_0 = 1.0f;
    }
    var relaxClamp_0 : f32;
    if((p_0.control2_0.w) > 0.5f)
    {
        relaxClamp_0 = (textureSampleLevel((uRejectionBlend_0), (uRejectionBlendSampler_0), (uvJ_0), (0.0f))).x;
    }
    else
    {
        relaxClamp_0 = 1.0f;
    }
    var _S41 : bool;
    if(offScreen_0)
    {
        _S41 = true;
    }
    else
    {
        _S41 = believable_0 < 0.5f;
    }
    if(_S41)
    {
        relaxClamp_0 = 0.0f;
    }
    var relaxClamp_1 : f32 = relaxClamp_0 * believable_0;
    if(offScreen_0)
    {
        _S41 = true;
    }
    else
    {
        _S41 = foreground_0;
    }
    var refine_0 : f32;
    if(_S41)
    {
        refine_0 = 0.0f;
    }
    else
    {
        refine_0 = saturate(16.0f * believable_0 * shading_0 - 13.0f);
    }
    var _S42 : f32 = mix(1.0f, _S36, refine_0);
    var inputPos_1 : vec2<f32>;
    var sumWeight_0 : f32;
    if((p_0.control2_0.z) > 0.5f)
    {
        var _S43 : vec3<i32> = vec3<i32>(clamp(vec2<i32>(inputPos_0 + sj_0), vec2<i32>(i32(0), i32(0)), _S37 - vec2<i32>(i32(1))), i32(0));
        var o_0 : vec2<f32> = (textureLoad((uSpatialAa_0), ((_S43)).xy, ((_S43)).z)).xy;
        var _S44 : f32 = min(shading_0, relaxClamp_1);
        if(offScreen_0)
        {
            _S41 = true;
        }
        else
        {
            _S41 = believable_0 < 0.5f;
        }
        if(_S41)
        {
            sumWeight_0 = 1.0f;
        }
        else
        {
            sumWeight_0 = saturate(1.0f - 4.0f * _S44);
        }
        inputPos_1 = inputPos_0 + o_0 * vec2<f32>(sumWeight_0);
    }
    else
    {
        inputPos_1 = inputPos_0;
    }
    var _S45 : vec2<i32> = vec2<i32>(floor(inputPos_1 - vec2<f32>(1.0f)));
    const _S46 : vec3<f32> = vec3<f32>(0.0f, 0.0f, 0.0f);
    for(;;)
    {
        for(;;)
        {
            for(;;)
            {
                for(;;)
                {
                    for(;;)
                    {
                        for(;;)
                        {
                            var _S47 : i32 = _S45.x;
                            if(_S47 < i32(0))
                            {
                                _S41 = true;
                            }
                            else
                            {
                                _S41 = (_S45.y) < i32(0);
                            }
                            if(_S41)
                            {
                                _S41 = true;
                            }
                            else
                            {
                                _S41 = _S47 >= (_S37.x);
                            }
                            if(_S41)
                            {
                                _S41 = true;
                            }
                            else
                            {
                                _S41 = (_S45.y) >= (_S37.y);
                            }
                            if(_S41)
                            {
                                sumColour_0 = _S46;
                                sumWeight_0 = 0.0f;
                                cov_0 = 0.0f;
                                break;
                            }
                            var w_0 : f32 = KernelWeight_0(_S42, vec2<f32>(_S45) + _S38 - sj_0 - inputPos_1);
                            var _S48 : vec3<i32> = vec3<i32>(_S45, i32(0));
                            var c_3 : vec3<f32> = max((textureLoad((uCurrent_0), ((_S48)).xy, ((_S48)).z)).xyz, _S39);
                            var hw_0 : f32 = w_0 * HdrWeight_0(c_3);
                            sumColour_0 = c_3 * vec3<f32>(hw_0);
                            sumWeight_0 = hw_0;
                            cov_0 = w_0;
                            break;
                        }
                        break;
                    }
                    for(;;)
                    {
                        for(;;)
                        {
                            var kk_0 : vec2<i32> = _S45 + vec2<i32>(i32(1), i32(0));
                            var _S49 : i32 = kk_0.x;
                            if(_S49 < i32(0))
                            {
                                _S41 = true;
                            }
                            else
                            {
                                _S41 = (kk_0.y) < i32(0);
                            }
                            if(_S41)
                            {
                                _S41 = true;
                            }
                            else
                            {
                                _S41 = _S49 >= (_S37.x);
                            }
                            if(_S41)
                            {
                                _S41 = true;
                            }
                            else
                            {
                                _S41 = (kk_0.y) >= (_S37.y);
                            }
                            if(_S41)
                            {
                                break;
                            }
                            var w_1 : f32 = KernelWeight_0(_S42, vec2<f32>(kk_0) + _S38 - sj_0 - inputPos_1);
                            var _S50 : vec3<i32> = vec3<i32>(kk_0, i32(0));
                            var c_4 : vec3<f32> = max((textureLoad((uCurrent_0), ((_S50)).xy, ((_S50)).z)).xyz, _S39);
                            var hw_1 : f32 = w_1 * HdrWeight_0(c_4);
                            var sumWeight_1 : f32 = sumWeight_0 + hw_1;
                            var cov_1 : f32 = cov_0 + w_1;
                            sumColour_0 = sumColour_0 + c_4 * vec3<f32>(hw_1);
                            sumWeight_0 = sumWeight_1;
                            cov_0 = cov_1;
                            break;
                        }
                        break;
                    }
                    for(;;)
                    {
                        for(;;)
                        {
                            var kk_1 : vec2<i32> = _S45 + vec2<i32>(i32(2), i32(0));
                            var _S51 : i32 = kk_1.x;
                            if(_S51 < i32(0))
                            {
                                _S41 = true;
                            }
                            else
                            {
                                _S41 = (kk_1.y) < i32(0);
                            }
                            if(_S41)
                            {
                                _S41 = true;
                            }
                            else
                            {
                                _S41 = _S51 >= (_S37.x);
                            }
                            if(_S41)
                            {
                                _S41 = true;
                            }
                            else
                            {
                                _S41 = (kk_1.y) >= (_S37.y);
                            }
                            if(_S41)
                            {
                                break;
                            }
                            var w_2 : f32 = KernelWeight_0(_S42, vec2<f32>(kk_1) + _S38 - sj_0 - inputPos_1);
                            var _S52 : vec3<i32> = vec3<i32>(kk_1, i32(0));
                            var c_5 : vec3<f32> = max((textureLoad((uCurrent_0), ((_S52)).xy, ((_S52)).z)).xyz, _S39);
                            var hw_2 : f32 = w_2 * HdrWeight_0(c_5);
                            var sumWeight_2 : f32 = sumWeight_0 + hw_2;
                            var cov_2 : f32 = cov_0 + w_2;
                            sumColour_0 = sumColour_0 + c_5 * vec3<f32>(hw_2);
                            sumWeight_0 = sumWeight_2;
                            cov_0 = cov_2;
                            break;
                        }
                        break;
                    }
                    break;
                }
                break;
            }
            break;
        }
        for(;;)
        {
            for(;;)
            {
                for(;;)
                {
                    for(;;)
                    {
                        for(;;)
                        {
                            var kk_2 : vec2<i32> = _S45 + vec2<i32>(i32(0), i32(1));
                            var _S53 : i32 = kk_2.x;
                            if(_S53 < i32(0))
                            {
                                _S41 = true;
                            }
                            else
                            {
                                _S41 = (kk_2.y) < i32(0);
                            }
                            if(_S41)
                            {
                                _S41 = true;
                            }
                            else
                            {
                                _S41 = _S53 >= (_S37.x);
                            }
                            if(_S41)
                            {
                                _S41 = true;
                            }
                            else
                            {
                                _S41 = (kk_2.y) >= (_S37.y);
                            }
                            if(_S41)
                            {
                                break;
                            }
                            var w_3 : f32 = KernelWeight_0(_S42, vec2<f32>(kk_2) + _S38 - sj_0 - inputPos_1);
                            var _S54 : vec3<i32> = vec3<i32>(kk_2, i32(0));
                            var c_6 : vec3<f32> = max((textureLoad((uCurrent_0), ((_S54)).xy, ((_S54)).z)).xyz, _S39);
                            var hw_3 : f32 = w_3 * HdrWeight_0(c_6);
                            var sumWeight_3 : f32 = sumWeight_0 + hw_3;
                            var cov_3 : f32 = cov_0 + w_3;
                            sumColour_0 = sumColour_0 + c_6 * vec3<f32>(hw_3);
                            sumWeight_0 = sumWeight_3;
                            cov_0 = cov_3;
                            break;
                        }
                        break;
                    }
                    for(;;)
                    {
                        for(;;)
                        {
                            var kk_3 : vec2<i32> = _S45 + vec2<i32>(i32(1), i32(1));
                            var _S55 : i32 = kk_3.x;
                            if(_S55 < i32(0))
                            {
                                _S41 = true;
                            }
                            else
                            {
                                _S41 = (kk_3.y) < i32(0);
                            }
                            if(_S41)
                            {
                                _S41 = true;
                            }
                            else
                            {
                                _S41 = _S55 >= (_S37.x);
                            }
                            if(_S41)
                            {
                                _S41 = true;
                            }
                            else
                            {
                                _S41 = (kk_3.y) >= (_S37.y);
                            }
                            if(_S41)
                            {
                                break;
                            }
                            var w_4 : f32 = KernelWeight_0(_S42, vec2<f32>(kk_3) + _S38 - sj_0 - inputPos_1);
                            var _S56 : vec3<i32> = vec3<i32>(kk_3, i32(0));
                            var c_7 : vec3<f32> = max((textureLoad((uCurrent_0), ((_S56)).xy, ((_S56)).z)).xyz, _S39);
                            var hw_4 : f32 = w_4 * HdrWeight_0(c_7);
                            var sumWeight_4 : f32 = sumWeight_0 + hw_4;
                            var cov_4 : f32 = cov_0 + w_4;
                            sumColour_0 = sumColour_0 + c_7 * vec3<f32>(hw_4);
                            sumWeight_0 = sumWeight_4;
                            cov_0 = cov_4;
                            break;
                        }
                        break;
                    }
                    for(;;)
                    {
                        for(;;)
                        {
                            var kk_4 : vec2<i32> = _S45 + vec2<i32>(i32(2), i32(1));
                            var _S57 : i32 = kk_4.x;
                            if(_S57 < i32(0))
                            {
                                _S41 = true;
                            }
                            else
                            {
                                _S41 = (kk_4.y) < i32(0);
                            }
                            if(_S41)
                            {
                                _S41 = true;
                            }
                            else
                            {
                                _S41 = _S57 >= (_S37.x);
                            }
                            if(_S41)
                            {
                                _S41 = true;
                            }
                            else
                            {
                                _S41 = (kk_4.y) >= (_S37.y);
                            }
                            if(_S41)
                            {
                                break;
                            }
                            var w_5 : f32 = KernelWeight_0(_S42, vec2<f32>(kk_4) + _S38 - sj_0 - inputPos_1);
                            var _S58 : vec3<i32> = vec3<i32>(kk_4, i32(0));
                            var c_8 : vec3<f32> = max((textureLoad((uCurrent_0), ((_S58)).xy, ((_S58)).z)).xyz, _S39);
                            var hw_5 : f32 = w_5 * HdrWeight_0(c_8);
                            var sumWeight_5 : f32 = sumWeight_0 + hw_5;
                            var cov_5 : f32 = cov_0 + w_5;
                            sumColour_0 = sumColour_0 + c_8 * vec3<f32>(hw_5);
                            sumWeight_0 = sumWeight_5;
                            cov_0 = cov_5;
                            break;
                        }
                        break;
                    }
                    break;
                }
                break;
            }
            break;
        }
        for(;;)
        {
            for(;;)
            {
                for(;;)
                {
                    for(;;)
                    {
                        for(;;)
                        {
                            var kk_5 : vec2<i32> = _S45 + vec2<i32>(i32(0), i32(2));
                            var _S59 : i32 = kk_5.x;
                            if(_S59 < i32(0))
                            {
                                _S41 = true;
                            }
                            else
                            {
                                _S41 = (kk_5.y) < i32(0);
                            }
                            if(_S41)
                            {
                                _S41 = true;
                            }
                            else
                            {
                                _S41 = _S59 >= (_S37.x);
                            }
                            if(_S41)
                            {
                                _S41 = true;
                            }
                            else
                            {
                                _S41 = (kk_5.y) >= (_S37.y);
                            }
                            if(_S41)
                            {
                                break;
                            }
                            var w_6 : f32 = KernelWeight_0(_S42, vec2<f32>(kk_5) + _S38 - sj_0 - inputPos_1);
                            var _S60 : vec3<i32> = vec3<i32>(kk_5, i32(0));
                            var c_9 : vec3<f32> = max((textureLoad((uCurrent_0), ((_S60)).xy, ((_S60)).z)).xyz, _S39);
                            var hw_6 : f32 = w_6 * HdrWeight_0(c_9);
                            var sumWeight_6 : f32 = sumWeight_0 + hw_6;
                            var cov_6 : f32 = cov_0 + w_6;
                            sumColour_0 = sumColour_0 + c_9 * vec3<f32>(hw_6);
                            sumWeight_0 = sumWeight_6;
                            cov_0 = cov_6;
                            break;
                        }
                        break;
                    }
                    for(;;)
                    {
                        for(;;)
                        {
                            var kk_6 : vec2<i32> = _S45 + vec2<i32>(i32(1), i32(2));
                            var _S61 : i32 = kk_6.x;
                            if(_S61 < i32(0))
                            {
                                _S41 = true;
                            }
                            else
                            {
                                _S41 = (kk_6.y) < i32(0);
                            }
                            if(_S41)
                            {
                                _S41 = true;
                            }
                            else
                            {
                                _S41 = _S61 >= (_S37.x);
                            }
                            if(_S41)
                            {
                                _S41 = true;
                            }
                            else
                            {
                                _S41 = (kk_6.y) >= (_S37.y);
                            }
                            if(_S41)
                            {
                                break;
                            }
                            var w_7 : f32 = KernelWeight_0(_S42, vec2<f32>(kk_6) + _S38 - sj_0 - inputPos_1);
                            var _S62 : vec3<i32> = vec3<i32>(kk_6, i32(0));
                            var c_10 : vec3<f32> = max((textureLoad((uCurrent_0), ((_S62)).xy, ((_S62)).z)).xyz, _S39);
                            var hw_7 : f32 = w_7 * HdrWeight_0(c_10);
                            var sumWeight_7 : f32 = sumWeight_0 + hw_7;
                            var cov_7 : f32 = cov_0 + w_7;
                            sumColour_0 = sumColour_0 + c_10 * vec3<f32>(hw_7);
                            sumWeight_0 = sumWeight_7;
                            cov_0 = cov_7;
                            break;
                        }
                        break;
                    }
                    for(;;)
                    {
                        for(;;)
                        {
                            var kk_7 : vec2<i32> = _S45 + vec2<i32>(i32(2), i32(2));
                            var _S63 : i32 = kk_7.x;
                            if(_S63 < i32(0))
                            {
                                _S41 = true;
                            }
                            else
                            {
                                _S41 = (kk_7.y) < i32(0);
                            }
                            if(_S41)
                            {
                                _S41 = true;
                            }
                            else
                            {
                                _S41 = _S63 >= (_S37.x);
                            }
                            if(_S41)
                            {
                                _S41 = true;
                            }
                            else
                            {
                                _S41 = (kk_7.y) >= (_S37.y);
                            }
                            if(_S41)
                            {
                                break;
                            }
                            var w_8 : f32 = KernelWeight_0(_S42, vec2<f32>(kk_7) + _S38 - sj_0 - inputPos_1);
                            var _S64 : vec3<i32> = vec3<i32>(kk_7, i32(0));
                            var c_11 : vec3<f32> = max((textureLoad((uCurrent_0), ((_S64)).xy, ((_S64)).z)).xyz, _S39);
                            var hw_8 : f32 = w_8 * HdrWeight_0(c_11);
                            var sumColour_1 : vec3<f32> = sumColour_0 + c_11 * vec3<f32>(hw_8);
                            var cov_8 : f32 = cov_0 + w_8;
                            sumWeight_0 = sumWeight_0 + hw_8;
                            sumColour_0 = sumColour_1;
                            cov_0 = cov_8;
                            break;
                        }
                        break;
                    }
                    break;
                }
                break;
            }
            break;
        }
        break;
    }
    var current_0 : vec3<f32>;
    if(sumWeight_0 > 9.99999997475242708e-07f)
    {
        current_0 = sumColour_0 / vec3<f32>(sumWeight_0);
    }
    else
    {
        current_0 = bilinJ_0;
    }
    var cov_9 : f32 = saturate(cov_0);
    if(foreground_0)
    {
        var _S65 : vec2<u32> = vec2<u32>(_S33);
        textureStore((uHistoryColour_0), (_S65), (vec4<f32>(current_0, 1.0f)));
        textureStore((uHistoryMetadata_0), (_S65), (vec4<f32>(cov_9 / _S35, 0.0f, 0.0f, 0.0f)));
        return;
    }
    const _S66 : vec3<f32> = vec3<f32>(1.00000001504746622e+30f, 1.00000001504746622e+30f, 1.00000001504746622e+30f);
    const _S67 : vec3<f32> = vec3<f32>(-1.00000001504746622e+30f, -1.00000001504746622e+30f, -1.00000001504746622e+30f);
    var tap_0 : vec4<f32> = (textureSampleLevel((uCurrent_0), (uCurrentSampler_0), (uvJ_0 + vec2<f32>(-1.0f, -1.0f) / inputSize_0), (0.0f)));
    var s_2 : vec3<f32> = ToBox_0(max(tap_0.xyz, _S39));
    var tap_1 : vec4<f32> = (textureSampleLevel((uCurrent_0), (uCurrentSampler_0), (uvJ_0 + vec2<f32>(0.0f, -1.0f) / inputSize_0), (0.0f)));
    var s_3 : vec3<f32> = ToBox_0(max(tap_1.xyz, _S39));
    var tap_2 : vec4<f32> = (textureSampleLevel((uCurrent_0), (uCurrentSampler_0), (uvJ_0 + vec2<f32>(1.0f, -1.0f) / inputSize_0), (0.0f)));
    var s_4 : vec3<f32> = ToBox_0(max(tap_2.xyz, _S39));
    var tap_3 : vec4<f32> = (textureSampleLevel((uCurrent_0), (uCurrentSampler_0), (uvJ_0 + vec2<f32>(-1.0f, 0.0f) / inputSize_0), (0.0f)));
    var s_5 : vec3<f32> = ToBox_0(max(tap_3.xyz, _S39));
    var tap_4 : vec4<f32> = (textureSampleLevel((uCurrent_0), (uCurrentSampler_0), (uvJ_0 + vec2<f32>(0.0f, 0.0f) / inputSize_0), (0.0f)));
    var s_6 : vec3<f32> = ToBox_0(max(tap_4.xyz, _S39));
    var tap_5 : vec4<f32> = (textureSampleLevel((uCurrent_0), (uCurrentSampler_0), (uvJ_0 + vec2<f32>(1.0f, 0.0f) / inputSize_0), (0.0f)));
    var s_7 : vec3<f32> = ToBox_0(max(tap_5.xyz, _S39));
    var tap_6 : vec4<f32> = (textureSampleLevel((uCurrent_0), (uCurrentSampler_0), (uvJ_0 + vec2<f32>(-1.0f, 1.0f) / inputSize_0), (0.0f)));
    var s_8 : vec3<f32> = ToBox_0(max(tap_6.xyz, _S39));
    var tap_7 : vec4<f32> = (textureSampleLevel((uCurrent_0), (uCurrentSampler_0), (uvJ_0 + vec2<f32>(0.0f, 1.0f) / inputSize_0), (0.0f)));
    var s_9 : vec3<f32> = ToBox_0(max(tap_7.xyz, _S39));
    var tap_8 : vec4<f32> = (textureSampleLevel((uCurrent_0), (uCurrentSampler_0), (uvJ_0 + vec2<f32>(1.0f, 1.0f) / inputSize_0), (0.0f)));
    var s_10 : vec3<f32> = ToBox_0(max(tap_8.xyz, _S39));
    var reactive_0 : f32 = saturate(4.0f * max(0.0f, max(max(max(max(max(max(max(max(max(0.0f, tap_0.w - 1.0f), tap_1.w - 1.0f), tap_2.w - 1.0f), tap_3.w - 1.0f), tap_4.w - 1.0f), tap_5.w - 1.0f), tap_6.w - 1.0f), tap_7.w - 1.0f), tap_8.w - 1.0f)));
    var _S68 : vec3<f32> = vec3<f32>(9.0f);
    var mu_0 : vec3<f32> = (s_2 + s_3 + s_4 + s_5 + s_6 + s_7 + s_8 + s_9 + s_10) / _S68;
    var sigma_0 : vec3<f32> = sqrt(max((s_2 * s_2 + s_3 * s_3 + s_4 * s_4 + s_5 * s_5 + s_6 * s_6 + s_7 * s_7 + s_8 * s_8 + s_9 * s_9 + s_10 * s_10) / _S68 - mu_0 * mu_0, _S39));
    var boxLo_0 : vec3<f32> = max(min(min(min(min(min(min(min(min(min(_S66, s_2), s_3), s_4), s_5), s_6), s_7), s_8), s_9), s_10), mu_0 - sigma_0);
    var boxHi_0 : vec3<f32> = min(max(max(max(max(max(max(max(max(max(_S67, s_2), s_3), s_4), s_5), s_6), s_7), s_8), s_9), s_10), mu_0 + sigma_0);
    if(offScreen_0)
    {
        var _S69 : vec2<u32> = vec2<u32>(_S33);
        textureStore((uHistoryColour_0), (_S69), (vec4<f32>(current_0, 1.0f - reactive_0)));
        textureStore((uHistoryMetadata_0), (_S69), (vec4<f32>(1.0f / _S35, 0.0f, 0.0f, 0.0f)));
        return;
    }
    if(believable_0 < 0.5f)
    {
        var _S70 : vec2<u32> = vec2<u32>(_S33);
        textureStore((uHistoryColour_0), (_S70), (vec4<f32>(current_0, 1.0f - reactive_0)));
        textureStore((uHistoryMetadata_0), (_S70), (vec4<f32>(cov_9 / _S35, 0.0f, 0.0f, 0.0f)));
        return;
    }
    var hist4_0 : vec4<f32> = SampleHistoryCatmullRom_0(prevUv_0);
    var hLin_0 : vec3<f32> = max(hist4_0.xyz, _S39);
    var count_0 : f32 = _S35 * (textureSampleLevel((uPrevHistoryMetadata_0), (uPrevHistoryMetadataSampler_0), (prevUv_0), (0.0f))).x;
    if(!IsFiniteColour_0(hLin_0))
    {
        offScreen_0 = true;
    }
    else
    {
        offScreen_0 = count_0 < 0.00100000004749745f;
    }
    if(offScreen_0)
    {
        var _S71 : vec2<u32> = vec2<u32>(_S33);
        textureStore((uHistoryColour_0), (_S71), (vec4<f32>(current_0, 1.0f - reactive_0)));
        textureStore((uHistoryMetadata_0), (_S71), (vec4<f32>(cov_9 / _S35, 0.0f, 0.0f, 0.0f)));
        return;
    }
    var h_0 : vec3<f32> = ToBox_0(hLin_0);
    var _S72 : vec3<f32> = vec3<f32>(0.5f);
    var centre_0 : vec3<f32> = (boxLo_0 + boxHi_0) * _S72;
    var e_0 : vec3<f32> = h_0 - centre_0;
    var ratio_0 : vec3<f32> = abs(e_0) / max((boxHi_0 - boxLo_0) * _S72, vec3<f32>(0.00000999999974738f));
    var _S73 : f32 = max(ratio_0.x, max(ratio_0.y, ratio_0.z));
    var h_1 : vec3<f32>;
    if(_S73 > 1.0f)
    {
        h_1 = centre_0 + e_0 / vec3<f32>(_S73);
    }
    else
    {
        h_1 = h_0;
    }
    var hClip_0 : vec3<f32> = FromBox_0(h_1);
    var remembered_0 : f32 = saturate(1.0f - hist4_0.w);
    var _S74 : f32 = min(relaxClamp_1, 1.0f - remembered_0);
    var wClip_0 : f32 = (1.0f - _S74) * HdrWeight_0(hClip_0);
    var wLin_0 : f32 = _S74 * HdrWeight_0(hLin_0);
    var hist_0 : vec3<f32> = (hClip_0 * vec3<f32>(wClip_0) + hLin_0 * vec3<f32>(wLin_0)) / vec3<f32>(max(wClip_0 + wLin_0, 9.99999997475242708e-07f));
    var _S75 : f32 = min(min(count_0 + cov_9, _S35), mix(_S35, 3.0f, saturate(speed_0 / 8.0f)));
    var blend_0 : f32;
    if(_S75 > 9.99999997475242708e-07f)
    {
        blend_0 = saturate(cov_9 / _S75);
    }
    else
    {
        blend_0 = 1.0f;
    }
    var lc_0 : f32 = Luma_0(current_0);
    var lh_0 : f32 = Luma_0(hist_0);
    var _S76 : f32 = max(max(max(blend_0, 1.0f - max(1.0f - saturate(1.0f - 4.0f / _S35) * saturate(speed_0), saturate(abs(lc_0 - lh_0) / max(max(lc_0, lh_0), 0.00009999999747379f)))), saturate(2.0f * (0.5f - min(shading_0, _S74)))), 0.85000002384185791f * reactive_0);
    var _S77 : f32 = min(_S75, max(cov_9, 0.25f) / max(_S76, 0.00100000004749745f));
    var wc_0 : f32 = _S76 * HdrWeight_0(current_0);
    var wh_0 : f32 = (1.0f - _S76) * HdrWeight_0(hist_0);
    var _S78 : vec2<u32> = vec2<u32>(_S33);
    textureStore((uHistoryColour_0), (_S78), (vec4<f32>(max((current_0 * vec3<f32>(wc_0) + hist_0 * vec3<f32>(wh_0)) / vec3<f32>(max(wc_0 + wh_0, 9.99999997475242708e-07f)), _S39), 1.0f - max(reactive_0, 0.5f * remembered_0))));
    textureStore((uHistoryMetadata_0), (_S78), (vec4<f32>(_S77 / _S35, 0.0f, 0.0f, 0.0f)));
    return;
}

