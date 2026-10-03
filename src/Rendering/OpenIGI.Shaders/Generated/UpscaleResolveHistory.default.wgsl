struct Params_std140_0
{
    @align(16) outputSize_0 : vec4<f32>,
    @align(16) historySize_0 : vec4<f32>,
    @align(16) control_0 : vec4<f32>,
};

@binding(11) @group(0) var<uniform> p_0 : Params_std140_0;
@binding(3) @group(0) var uVelocity_0 : texture_2d<f32>;

@binding(4) @group(0) var uVelocitySampler_0 : sampler;

@binding(5) @group(0) var uParallax_0 : texture_2d<f32>;

@binding(6) @group(0) var uParallaxSampler_0 : sampler;

@binding(7) @group(0) var uShading_0 : texture_2d<f32>;

@binding(8) @group(0) var uShadingSampler_0 : sampler;

@binding(9) @group(0) var uHistoryMetadata_0 : texture_2d<f32>;

@binding(10) @group(0) var uHistoryMetadataSampler_0 : sampler;

@binding(2) @group(0) var uOutput_0 : texture_storage_2d<rgba16float, write>;

@binding(0) @group(0) var uHistoryColour_0 : texture_2d<f32>;

@binding(1) @group(0) var uHistoryColourSampler_0 : sampler;

fn HdrWeight_0( c_0 : vec3<f32>) -> f32
{
    return max(0.00009999999747379f, 1.0f / (c_0.x + 2.0f * c_0.y + c_0.z + 4.0f));
}

fn MitchellNetravali_0( x_0 : f32) -> f32
{
    var ax_0 : f32 = abs(x_0);
    var ax2_0 : f32 = ax_0 * ax_0;
    var ax3_0 : f32 = ax2_0 * ax_0;
    if(ax_0 < 1.0f)
    {
        return (7.0f * ax3_0 + -12.0f * ax2_0 + 5.33333349227905273f) / 6.0f;
    }
    if(ax_0 < 2.0f)
    {
        return (-2.33333325386047363f * ax3_0 + 12.0f * ax2_0 + -20.0f * ax_0 + 10.66666698455810547f) / 6.0f;
    }
    return 0.0f;
}

@compute
@workgroup_size(8, 8, 1)
fn computeMain(@builtin(global_invocation_id) id_0 : vec3<u32>)
{
    var _S1 : vec2<i32> = vec2<i32>(id_0.xy);
    var _S2 : vec2<i32> = vec2<i32>(p_0.outputSize_0.xy);
    var _S3 : bool;
    if((_S1.x) >= (_S2.x))
    {
        _S3 = true;
    }
    else
    {
        _S3 = (_S1.y) >= (_S2.y);
    }
    if(_S3)
    {
        return;
    }
    var scale_0 : vec2<f32> = max(p_0.control_0.xy, vec2<f32>(0.00100000004749745f, 0.00100000004749745f));
    var debugView_0 : i32 = i32(p_0.control_0.z + 0.5f);
    var shown_0 : vec3<f32>;
    if(debugView_0 != i32(0))
    {
        var uv_0 : vec2<f32> = (vec2<f32>(_S1) + vec2<f32>(0.5f)) * p_0.outputSize_0.zw;
        if(debugView_0 == i32(1))
        {
            var velocityWidth_0 : u32;
            var velocityHeight_0 : u32;
            {var dim = textureDimensions((uVelocity_0));((velocityWidth_0)) = dim.x;((velocityHeight_0)) = dim.y;};
            var v_0 : vec2<f32> = (textureSampleLevel((uVelocity_0), (uVelocitySampler_0), (uv_0), (0.0f))).xy * vec2<f32>(f32(velocityWidth_0), f32(velocityHeight_0));
            shown_0 = vec3<f32>(0.5f + v_0.y / 20.0f, 0.5f + v_0.x / 20.0f, 0.5f);
        }
        else
        {
            if(debugView_0 == i32(2))
            {
                shown_0 = (textureSampleLevel((uParallax_0), (uParallaxSampler_0), (uv_0), (0.0f))).xxx;
            }
            else
            {
                if(debugView_0 == i32(3))
                {
                    shown_0 = (textureSampleLevel((uShading_0), (uShadingSampler_0), (uv_0), (0.0f))).xxx;
                }
                else
                {
                    shown_0 = (textureSampleLevel((uHistoryMetadata_0), (uHistoryMetadataSampler_0), (uv_0 * p_0.historySize_0.xy * p_0.historySize_0.zw), (0.0f))).xxx;
                }
            }
        }
        textureStore((uOutput_0), (vec2<u32>(_S1)), (vec4<f32>(clamp(shown_0, vec3<f32>(0.0f, 0.0f, 0.0f), vec3<f32>(1.0f, 1.0f, 1.0f)), 1.0f)));
        return;
    }
    var _S4 : vec2<f32> = vec2<f32>(0.5f);
    var hp_0 : vec2<f32> = (vec2<f32>(_S1) + _S4) * scale_0;
    var _S5 : vec2<f32> = floor(hp_0 - vec2<f32>(1.5f)) + _S4;
    const _S6 : vec3<f32> = vec3<f32>(1.00000001504746622e+30f, 1.00000001504746622e+30f, 1.00000001504746622e+30f);
    const _S7 : vec3<f32> = vec3<f32>(-1.00000001504746622e+30f, -1.00000001504746622e+30f, -1.00000001504746622e+30f);
    var _S8 : vec2<f32> = vec2<f32>(0.0f);
    var _S9 : vec2<f32> = vec2<f32>(1.0f);
    var _S10 : vec3<f32> = vec3<f32>(0.0f);
    var c_1 : vec3<f32> = max((textureSampleLevel((uHistoryColour_0), (uHistoryColourSampler_0), (clamp(_S5 * p_0.historySize_0.zw, _S8, _S9)), (0.0f))).xyz, _S10);
    var hw_0 : f32 = HdrWeight_0(c_1);
    var _S11 : f32 = hp_0.x;
    var _S12 : f32 = hp_0.y;
    var wt_0 : f32 = MitchellNetravali_0(abs(_S5.x - _S11)) * MitchellNetravali_0(abs(_S5.y - _S12)) * hw_0;
    var pos_0 : vec2<f32> = _S5 + vec2<f32>(1.0f, 0.0f);
    var c_2 : vec3<f32> = max((textureSampleLevel((uHistoryColour_0), (uHistoryColourSampler_0), (clamp(pos_0 * p_0.historySize_0.zw, _S8, _S9)), (0.0f))).xyz, _S10);
    var hw_1 : f32 = HdrWeight_0(c_2);
    var wt_1 : f32 = MitchellNetravali_0(abs(pos_0.x - _S11)) * MitchellNetravali_0(abs(pos_0.y - _S12)) * hw_1;
    var pos_1 : vec2<f32> = _S5 + vec2<f32>(2.0f, 0.0f);
    var c_3 : vec3<f32> = max((textureSampleLevel((uHistoryColour_0), (uHistoryColourSampler_0), (clamp(pos_1 * p_0.historySize_0.zw, _S8, _S9)), (0.0f))).xyz, _S10);
    var hw_2 : f32 = HdrWeight_0(c_3);
    var wt_2 : f32 = MitchellNetravali_0(abs(pos_1.x - _S11)) * MitchellNetravali_0(abs(pos_1.y - _S12)) * hw_2;
    var pos_2 : vec2<f32> = _S5 + vec2<f32>(3.0f, 0.0f);
    var c_4 : vec3<f32> = max((textureSampleLevel((uHistoryColour_0), (uHistoryColourSampler_0), (clamp(pos_2 * p_0.historySize_0.zw, _S8, _S9)), (0.0f))).xyz, _S10);
    var hw_3 : f32 = HdrWeight_0(c_4);
    var wt_3 : f32 = MitchellNetravali_0(abs(pos_2.x - _S11)) * MitchellNetravali_0(abs(pos_2.y - _S12)) * hw_3;
    var pos_3 : vec2<f32> = _S5 + vec2<f32>(0.0f, 1.0f);
    var c_5 : vec3<f32> = max((textureSampleLevel((uHistoryColour_0), (uHistoryColourSampler_0), (clamp(pos_3 * p_0.historySize_0.zw, _S8, _S9)), (0.0f))).xyz, _S10);
    var hw_4 : f32 = HdrWeight_0(c_5);
    var wt_4 : f32 = MitchellNetravali_0(abs(pos_3.x - _S11)) * MitchellNetravali_0(abs(pos_3.y - _S12)) * hw_4;
    var pos_4 : vec2<f32> = _S5 + vec2<f32>(1.0f, 1.0f);
    var c_6 : vec3<f32> = max((textureSampleLevel((uHistoryColour_0), (uHistoryColourSampler_0), (clamp(pos_4 * p_0.historySize_0.zw, _S8, _S9)), (0.0f))).xyz, _S10);
    var hw_5 : f32 = HdrWeight_0(c_6);
    var wt_5 : f32 = MitchellNetravali_0(abs(pos_4.x - _S11)) * MitchellNetravali_0(abs(pos_4.y - _S12)) * hw_5;
    var pos_5 : vec2<f32> = _S5 + vec2<f32>(2.0f, 1.0f);
    var c_7 : vec3<f32> = max((textureSampleLevel((uHistoryColour_0), (uHistoryColourSampler_0), (clamp(pos_5 * p_0.historySize_0.zw, _S8, _S9)), (0.0f))).xyz, _S10);
    var hw_6 : f32 = HdrWeight_0(c_7);
    var wt_6 : f32 = MitchellNetravali_0(abs(pos_5.x - _S11)) * MitchellNetravali_0(abs(pos_5.y - _S12)) * hw_6;
    var pos_6 : vec2<f32> = _S5 + vec2<f32>(3.0f, 1.0f);
    var c_8 : vec3<f32> = max((textureSampleLevel((uHistoryColour_0), (uHistoryColourSampler_0), (clamp(pos_6 * p_0.historySize_0.zw, _S8, _S9)), (0.0f))).xyz, _S10);
    var hw_7 : f32 = HdrWeight_0(c_8);
    var wt_7 : f32 = MitchellNetravali_0(abs(pos_6.x - _S11)) * MitchellNetravali_0(abs(pos_6.y - _S12)) * hw_7;
    var pos_7 : vec2<f32> = _S5 + vec2<f32>(0.0f, 2.0f);
    var c_9 : vec3<f32> = max((textureSampleLevel((uHistoryColour_0), (uHistoryColourSampler_0), (clamp(pos_7 * p_0.historySize_0.zw, _S8, _S9)), (0.0f))).xyz, _S10);
    var hw_8 : f32 = HdrWeight_0(c_9);
    var wt_8 : f32 = MitchellNetravali_0(abs(pos_7.x - _S11)) * MitchellNetravali_0(abs(pos_7.y - _S12)) * hw_8;
    var pos_8 : vec2<f32> = _S5 + vec2<f32>(1.0f, 2.0f);
    var c_10 : vec3<f32> = max((textureSampleLevel((uHistoryColour_0), (uHistoryColourSampler_0), (clamp(pos_8 * p_0.historySize_0.zw, _S8, _S9)), (0.0f))).xyz, _S10);
    var hw_9 : f32 = HdrWeight_0(c_10);
    var wt_9 : f32 = MitchellNetravali_0(abs(pos_8.x - _S11)) * MitchellNetravali_0(abs(pos_8.y - _S12)) * hw_9;
    var pos_9 : vec2<f32> = _S5 + vec2<f32>(2.0f, 2.0f);
    var c_11 : vec3<f32> = max((textureSampleLevel((uHistoryColour_0), (uHistoryColourSampler_0), (clamp(pos_9 * p_0.historySize_0.zw, _S8, _S9)), (0.0f))).xyz, _S10);
    var hw_10 : f32 = HdrWeight_0(c_11);
    var wt_10 : f32 = MitchellNetravali_0(abs(pos_9.x - _S11)) * MitchellNetravali_0(abs(pos_9.y - _S12)) * hw_10;
    var pos_10 : vec2<f32> = _S5 + vec2<f32>(3.0f, 2.0f);
    var c_12 : vec3<f32> = max((textureSampleLevel((uHistoryColour_0), (uHistoryColourSampler_0), (clamp(pos_10 * p_0.historySize_0.zw, _S8, _S9)), (0.0f))).xyz, _S10);
    var hw_11 : f32 = HdrWeight_0(c_12);
    var wt_11 : f32 = MitchellNetravali_0(abs(pos_10.x - _S11)) * MitchellNetravali_0(abs(pos_10.y - _S12)) * hw_11;
    var pos_11 : vec2<f32> = _S5 + vec2<f32>(0.0f, 3.0f);
    var c_13 : vec3<f32> = max((textureSampleLevel((uHistoryColour_0), (uHistoryColourSampler_0), (clamp(pos_11 * p_0.historySize_0.zw, _S8, _S9)), (0.0f))).xyz, _S10);
    var hw_12 : f32 = HdrWeight_0(c_13);
    var wt_12 : f32 = MitchellNetravali_0(abs(pos_11.x - _S11)) * MitchellNetravali_0(abs(pos_11.y - _S12)) * hw_12;
    var pos_12 : vec2<f32> = _S5 + vec2<f32>(1.0f, 3.0f);
    var c_14 : vec3<f32> = max((textureSampleLevel((uHistoryColour_0), (uHistoryColourSampler_0), (clamp(pos_12 * p_0.historySize_0.zw, _S8, _S9)), (0.0f))).xyz, _S10);
    var hw_13 : f32 = HdrWeight_0(c_14);
    var wt_13 : f32 = MitchellNetravali_0(abs(pos_12.x - _S11)) * MitchellNetravali_0(abs(pos_12.y - _S12)) * hw_13;
    var pos_13 : vec2<f32> = _S5 + vec2<f32>(2.0f, 3.0f);
    var c_15 : vec3<f32> = max((textureSampleLevel((uHistoryColour_0), (uHistoryColourSampler_0), (clamp(pos_13 * p_0.historySize_0.zw, _S8, _S9)), (0.0f))).xyz, _S10);
    var hw_14 : f32 = HdrWeight_0(c_15);
    var wt_14 : f32 = MitchellNetravali_0(abs(pos_13.x - _S11)) * MitchellNetravali_0(abs(pos_13.y - _S12)) * hw_14;
    var pos_14 : vec2<f32> = _S5 + vec2<f32>(3.0f, 3.0f);
    var c_16 : vec3<f32> = max((textureSampleLevel((uHistoryColour_0), (uHistoryColourSampler_0), (clamp(pos_14 * p_0.historySize_0.zw, _S8, _S9)), (0.0f))).xyz, _S10);
    var hw_15 : f32 = HdrWeight_0(c_16);
    var wt_15 : f32 = MitchellNetravali_0(abs(pos_14.x - _S11)) * MitchellNetravali_0(abs(pos_14.y - _S12)) * hw_15;
    var sum_0 : vec3<f32> = c_1 * vec3<f32>(wt_0) + c_2 * vec3<f32>(wt_1) + c_3 * vec3<f32>(wt_2) + c_4 * vec3<f32>(wt_3) + c_5 * vec3<f32>(wt_4) + c_6 * vec3<f32>(wt_5) + c_7 * vec3<f32>(wt_6) + c_8 * vec3<f32>(wt_7) + c_9 * vec3<f32>(wt_8) + c_10 * vec3<f32>(wt_9) + c_11 * vec3<f32>(wt_10) + c_12 * vec3<f32>(wt_11) + c_13 * vec3<f32>(wt_12) + c_14 * vec3<f32>(wt_13) + c_15 * vec3<f32>(wt_14) + c_16 * vec3<f32>(wt_15);
    var sumWeight_0 : f32 = wt_0 + wt_1 + wt_2 + wt_3 + wt_4 + wt_5 + wt_6 + wt_7 + wt_8 + wt_9 + wt_10 + wt_11 + wt_12 + wt_13 + wt_14 + wt_15;
    var box_0 : vec3<f32> = c_1 * vec3<f32>(hw_0) + c_2 * vec3<f32>(hw_1) + c_3 * vec3<f32>(hw_2) + c_4 * vec3<f32>(hw_3) + c_5 * vec3<f32>(hw_4) + c_6 * vec3<f32>(hw_5) + c_7 * vec3<f32>(hw_6) + c_8 * vec3<f32>(hw_7) + c_9 * vec3<f32>(hw_8) + c_10 * vec3<f32>(hw_9) + c_11 * vec3<f32>(hw_10) + c_12 * vec3<f32>(hw_11) + c_13 * vec3<f32>(hw_12) + c_14 * vec3<f32>(hw_13) + c_15 * vec3<f32>(hw_14) + c_16 * vec3<f32>(hw_15);
    var boxWeight_0 : f32 = hw_0 + hw_1 + hw_2 + hw_3 + hw_4 + hw_5 + hw_6 + hw_7 + hw_8 + hw_9 + hw_10 + hw_11 + hw_12 + hw_13 + hw_14 + hw_15;
    var lo_0 : vec3<f32> = min(min(min(min(min(min(min(min(min(min(min(min(min(min(min(min(_S6, c_1), c_2), c_3), c_4), c_5), c_6), c_7), c_8), c_9), c_10), c_11), c_12), c_13), c_14), c_15), c_16);
    var hi_0 : vec3<f32> = max(max(max(max(max(max(max(max(max(max(max(max(max(max(max(max(_S7, c_1), c_2), c_3), c_4), c_5), c_6), c_7), c_8), c_9), c_10), c_11), c_12), c_13), c_14), c_15), c_16);
    if(sumWeight_0 != 0.0f)
    {
        shown_0 = sum_0 / vec3<f32>(sumWeight_0);
    }
    else
    {
        shown_0 = vec3<f32>(0.0f, 0.0f, 0.0f);
    }
    var sharpen_0 : f32 = p_0.control_0.w;
    if(sharpen_0 > 0.0f)
    {
        _S3 = boxWeight_0 > 0.0f;
    }
    else
    {
        _S3 = false;
    }
    if(_S3)
    {
        shown_0 = shown_0 + (shown_0 - box_0 / vec3<f32>(boxWeight_0)) * vec3<f32>(sharpen_0);
    }
    textureStore((uOutput_0), (vec2<u32>(_S1)), (vec4<f32>(max(clamp(shown_0, lo_0, hi_0), _S10), 1.0f)));
    return;
}

