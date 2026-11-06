struct _MatrixStorage_float4x4_ColMajorstd140_0
{
    @align(16) data_0 : array<vec4<f32>, i32(4)>,
};

struct ModePointPush_std140_0
{
    @align(16) modelViewProjection_0 : _MatrixStorage_float4x4_ColMajorstd140_0,
    @align(16) colour_0 : vec4<f32>,
    @align(16) state_0 : vec4<f32>,
    @align(16) target_0 : vec4<f32>,
};

@binding(0) @group(3) var<uniform> push_0 : ModePointPush_std140_0;
@id(65) override cTargetEncodes_0 : i32 = i32(0);

@id(64) override cClipYDown_0 : i32 = i32(1);

fn OpenIgiClipY_0( y_0 : f32) -> f32
{
    var _S1 : f32;
    if(cClipYDown_0 != i32(0))
    {
        _S1 = - y_0;
    }
    else
    {
        _S1 = y_0;
    }
    return _S1;
}

struct ModePointVaryings_0
{
    @builtin(position) position_0 : vec4<f32>,
    @location(0) uv_0 : vec2<f32>,
    @location(1) shape_0 : f32,
};

struct vertexInput_0
{
    @location(0) position_1 : vec3<f32>,
    @location(1) normal_0 : vec3<f32>,
    @location(2) diffuseUv_0 : vec2<f32>,
    @location(3) lightmapUv_0 : vec2<f32>,
    @location(4) colorBgra_0 : vec4<f32>,
    @location(5) previousPosition_0 : vec3<f32>,
    @location(6) previousNormal_0 : vec3<f32>,
};

@vertex
fn vertexMain( _S2 : vertexInput_0) -> ModePointVaryings_0
{
    var output_0 : ModePointVaryings_0;
    output_0.uv_0 = _S2.diffuseUv_0;
    output_0.shape_0 = _S2.lightmapUv_0.x;
    var _S3 : vec4<f32> = (((vec4<f32>(_S2.position_1, 1.0f)) * (mat4x4<f32>(push_0.modelViewProjection_0.data_0[i32(0)][i32(0)], push_0.modelViewProjection_0.data_0[i32(1)][i32(0)], push_0.modelViewProjection_0.data_0[i32(2)][i32(0)], push_0.modelViewProjection_0.data_0[i32(3)][i32(0)], push_0.modelViewProjection_0.data_0[i32(0)][i32(1)], push_0.modelViewProjection_0.data_0[i32(1)][i32(1)], push_0.modelViewProjection_0.data_0[i32(2)][i32(1)], push_0.modelViewProjection_0.data_0[i32(3)][i32(1)], push_0.modelViewProjection_0.data_0[i32(0)][i32(2)], push_0.modelViewProjection_0.data_0[i32(1)][i32(2)], push_0.modelViewProjection_0.data_0[i32(2)][i32(2)], push_0.modelViewProjection_0.data_0[i32(3)][i32(2)], push_0.modelViewProjection_0.data_0[i32(0)][i32(3)], push_0.modelViewProjection_0.data_0[i32(1)][i32(3)], push_0.modelViewProjection_0.data_0[i32(2)][i32(3)], push_0.modelViewProjection_0.data_0[i32(3)][i32(3)]))));
    var clip_0 : vec4<f32> = _S3;
    clip_0[i32(1)] = OpenIgiClipY_0(_S3.y);
    clip_0[i32(2)] = (clip_0.w - clip_0.z) * 0.5f;
    output_0.position_0 = clip_0;
    return output_0;
}

fn Cover_0( distance_0 : f32) -> f32
{
    return saturate(0.5f - distance_0 / max((fwidth((distance_0))), 0.00000999999974738f));
}

fn Segment_0( p_0 : vec2<f32>,  a_0 : vec2<f32>,  b_0 : vec2<f32>) -> f32
{
    var pa_0 : vec2<f32> = p_0 - a_0;
    var ba_0 : vec2<f32> = b_0 - a_0;
    return length(pa_0 - ba_0 * vec2<f32>(saturate(dot(pa_0, ba_0) / dot(ba_0, ba_0))));
}

fn RightArc_0( p_1 : vec2<f32>,  centre_0 : vec2<f32>,  radius_0 : f32) -> f32
{
    var d_0 : vec2<f32> = p_1 - centre_0;
    if((d_0.x) < 0.0f)
    {
        var _S4 : vec2<f32> = vec2<f32>(0.0f, radius_0);
        return min(length(p_1 - (centre_0 + _S4)), length(p_1 - (centre_0 - _S4)));
    }
    return abs(length(d_0) - radius_0);
}

fn Letter_0( p_2 : vec2<f32>,  letter_0 : f32) -> f32
{
    var _S5 : bool;
    if(letter_0 > 0.5f)
    {
        _S5 = letter_0 < 1.5f;
    }
    else
    {
        _S5 = false;
    }
    var d_1 : f32;
    if(_S5)
    {
        const _S6 : vec2<f32> = vec2<f32>(0.0f, 0.34000000357627869f);
        d_1 = min(min(min(1000.0f, Segment_0(p_2, vec2<f32>(-0.25999999046325684f, -0.31999999284744263f), _S6)), Segment_0(p_2, vec2<f32>(0.25999999046325684f, -0.31999999284744263f), _S6)), Segment_0(p_2, vec2<f32>(-0.15000000596046448f, -0.05999999865889549f), vec2<f32>(0.15000000596046448f, -0.05999999865889549f)));
    }
    else
    {
        if(letter_0 > 1.5f)
        {
            _S5 = letter_0 < 2.5f;
        }
        else
        {
            _S5 = false;
        }
        if(_S5)
        {
            const _S7 : vec2<f32> = vec2<f32>(-0.20999999344348907f, -0.33000001311302185f);
            const _S8 : vec2<f32> = vec2<f32>(-0.20999999344348907f, 0.33000001311302185f);
            d_1 = min(min(min(min(min(min(1000.0f, Segment_0(p_2, _S7, _S8)), Segment_0(p_2, _S8, vec2<f32>(0.01999999955296516f, 0.33000001311302185f))), RightArc_0(p_2, vec2<f32>(0.01999999955296516f, 0.17499999701976776f), 0.1550000011920929f)), Segment_0(p_2, vec2<f32>(-0.20999999344348907f, 0.01999999955296516f), vec2<f32>(0.03999999910593033f, 0.01999999955296516f))), RightArc_0(p_2, vec2<f32>(0.03999999910593033f, -0.1550000011920929f), 0.17499999701976776f)), Segment_0(p_2, _S7, vec2<f32>(0.03999999910593033f, -0.33000001311302185f)));
        }
        else
        {
            if(letter_0 > 2.5f)
            {
                var ring_0 : f32 = abs(length(p_2) - 0.30000001192092896f);
                if((abs(atan2(p_2.y, p_2.x))) < 0.75f)
                {
                    var _S9 : f32 = 0.30000001192092896f * cos(0.75f);
                    var _S10 : f32 = sin(0.75f);
                    d_1 = min(length(p_2 - vec2<f32>(_S9, 0.30000001192092896f * _S10)), length(p_2 - vec2<f32>(_S9, -0.30000001192092896f * _S10)));
                }
                else
                {
                    d_1 = ring_0;
                }
            }
            else
            {
                d_1 = 1000.0f;
            }
        }
    }
    return d_1 - 0.04500000178813934f;
}

fn Disc_0( uv_1 : vec2<f32>,  time_0 : f32,  alarm_0 : f32,  letter_1 : f32) -> vec2<f32>
{
    var r_0 : f32 = length(uv_1);
    if(r_0 > 1.01999998092651367f)
    {
        return vec2<f32>(0.0f, 0.0f);
    }
    var angle_0 : f32 = atan2(uv_1.y, uv_1.x);
    var stroke_0 : f32 = Letter_0(uv_1, letter_1);
    var glow_0 : f32 = saturate(Cover_0(abs(r_0 - 0.9649999737739563f) - 0.02999999932944775f) + Cover_0(abs(r_0 - 0.88499999046325684f) - 0.00600000005215406f) * 0.55000001192092896f + Cover_0(abs(r_0 - 0.92500001192092896f) - 0.01400000043213367f) * step(fract((angle_0 / 6.28318548202514648f + time_0 * (0.18000000715255737f + alarm_0 * 0.60000002384185791f)) * 24.0f), 0.57999998331069946f) * 0.80000001192092896f + Cover_0(abs(fract(angle_0 / 6.28318548202514648f * 12.0f + 0.5f) - 0.5f) * r_0 * 0.52359879016876221f - 0.00800000037997961f) * Cover_0(abs(r_0 - 0.81999999284744263f) - 0.05000000074505806f) * 0.60000002384185791f + Cover_0(stroke_0) + exp(- max(stroke_0, 0.0f) * 18.0f) * 0.34999999403953552f * step(0.5f, letter_1));
    return vec2<f32>(saturate(glow_0 + smoothstep(0.15000000596046448f, 0.95999997854232788f, r_0) * 0.2199999988079071f * (1.0f - smoothstep(0.95999997854232788f, 1.0f, r_0))), glow_0);
}

fn Column_0( uv_2 : vec2<f32>,  time_1 : f32,  alarm_1 : f32) -> f32
{
    var up_0 : f32 = saturate(uv_2.y);
    return pow(1.0f - up_0, 2.40000009536743164f) * (0.69999998807907104f + 0.30000001192092896f * sin((up_0 * 6.0f - time_1 * (0.60000002384185791f + alarm_1)) * 2.0f * 3.14159274101257324f)) * 0.41999998688697815f + exp(- up_0 * 22.0f) * 0.60000002384185791f;
}

fn EncodeSrgb_0( c_0 : vec3<f32>) -> vec3<f32>
{
    return mix(vec3<f32>(1.0549999475479126f) * pow(max(c_0, vec3<f32>(0.0f, 0.0f, 0.0f)), vec3<f32>(0.4166666567325592f, 0.4166666567325592f, 0.4166666567325592f)) - vec3<f32>(0.05499999970197678f), c_0 * vec3<f32>(12.92000007629394531f), step(c_0, vec3<f32>(0.00313080009073019f, 0.00313080009073019f, 0.00313080009073019f)));
}

fn DecodeSrgb_0( c_1 : vec3<f32>) -> vec3<f32>
{
    return mix(pow(max((c_1 + vec3<f32>(0.05499999970197678f)) / vec3<f32>(1.0549999475479126f), vec3<f32>(0.0f, 0.0f, 0.0f)), vec3<f32>(2.40000009536743164f, 2.40000009536743164f, 2.40000009536743164f)), c_1 / vec3<f32>(12.92000007629394531f), step(c_1, vec3<f32>(0.04044999927282333f, 0.04044999927282333f, 0.04044999927282333f)));
}

fn ToTarget_0( display_0 : vec4<f32>) -> vec4<f32>
{
    var _S11 : vec4<f32>;
    if(cTargetEncodes_0 != i32(0))
    {
        _S11 = vec4<f32>(DecodeSrgb_0(display_0.xyz), display_0.w);
    }
    else
    {
        _S11 = display_0;
    }
    return _S11;
}

struct pixelOutput_0
{
    @location(0) output_1 : vec4<f32>,
};

struct pixelInput_0
{
    @location(0) uv_3 : vec2<f32>,
    @location(1) shape_1 : f32,
};

@fragment
fn fragmentMain( _S12 : pixelInput_0, @builtin(position) position_2 : vec4<f32>) -> pixelOutput_0
{
    var time_2 : f32 = push_0.state_0.y;
    var alarm_2 : f32 = saturate(push_0.state_0.z);
    var colour_1 : vec3<f32> = push_0.colour_0.xyz * vec3<f32>(push_0.colour_0.w) * vec3<f32>((1.0f + alarm_2 * (0.60000002384185791f + 0.60000002384185791f * sin(time_2 * 2.0f * 3.14159274101257324f * 1.60000002384185791f))));
    var light_0 : vec3<f32>;
    var cover_0 : f32;
    if((_S12.shape_1) < 0.5f)
    {
        var disc_0 : vec2<f32> = Disc_0(_S12.uv_3, time_2, alarm_2, push_0.state_0.x);
        var _S13 : f32 = disc_0.y;
        var _S14 : f32 = disc_0.x;
        var _S15 : f32 = _S14 * 0.60000002384185791f;
        light_0 = colour_1 * vec3<f32>((_S13 + (_S14 - _S13) * 0.5f));
        cover_0 = _S15;
    }
    else
    {
        light_0 = colour_1 * vec3<f32>(Column_0(_S12.uv_3, time_2, alarm_2));
        cover_0 = 0.0f;
    }
    if((push_0.target_0.x) != 0.0f)
    {
        var _S16 : pixelOutput_0 = pixelOutput_0( vec4<f32>(light_0 * vec3<f32>(push_0.state_0.w), cover_0) );
        return _S16;
    }
    var _S17 : pixelOutput_0 = pixelOutput_0( ToTarget_0(vec4<f32>(EncodeSrgb_0(saturate(light_0)), cover_0)) );
    return _S17;
}

