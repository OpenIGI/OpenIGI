struct HudFxParams_std140_0
{
    @align(16) frame_0 : vec4<f32>,
    @align(16) visor_0 : vec4<f32>,
    @align(16) lines_0 : vec4<f32>,
    @align(16) shake_0 : vec4<f32>,
    @align(16) damage_0 : vec4<f32>,
    @align(16) damageColour_0 : vec4<f32>,
    @align(16) alert_0 : vec4<f32>,
};

@binding(0) @group(3) var<uniform> p_0 : HudFxParams_std140_0;
@binding(0) @group(0) var uHud_0 : texture_2d<f32>;

@binding(1) @group(0) var uHudSampler_0 : sampler;

@binding(2) @group(0) var uGlow0_0 : texture_2d<f32>;

@binding(3) @group(0) var uGlow1_0 : texture_2d<f32>;

@binding(4) @group(0) var uGlow2_0 : texture_2d<f32>;

@binding(5) @group(0) var uScene_0 : texture_2d<f32>;

@binding(6) @group(0) var uSceneBlur_0 : texture_2d<f32>;

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

struct FullscreenVaryings_0
{
    @builtin(position) position_0 : vec4<f32>,
    @location(0) uv_0 : vec2<f32>,
};

fn FullscreenTriangle_0( vertexIndex_0 : u32) -> FullscreenVaryings_0
{
    var output_0 : FullscreenVaryings_0;
    var _S2 : f32 = f32((((vertexIndex_0 << (u32(1)))) & (u32(2))));
    var _S3 : f32 = f32((vertexIndex_0 & (u32(2))));
    output_0.uv_0 = vec2<f32>(_S2, _S3);
    output_0.position_0 = vec4<f32>(_S2 * 2.0f - 1.0f, OpenIgiClipY_0(1.0f - _S3 * 2.0f), 0.0f, 1.0f);
    return output_0;
}

@vertex
fn vertexMain(@builtin(vertex_index) index_0 : u32) -> FullscreenVaryings_0
{
    return FullscreenTriangle_0(index_0);
}

fn Layer_0( uv_1 : vec2<f32>) -> vec4<f32>
{
    var _S4 : bool;
    if((any((uv_1 < vec2<f32>(0.0f)))))
    {
        _S4 = true;
    }
    else
    {
        _S4 = (any((uv_1 > vec2<f32>(1.0f))));
    }
    if(_S4)
    {
        return vec4<f32>(0.0f, 0.0f, 0.0f, 0.0f);
    }
    return (textureSampleLevel((uHud_0), (uHudSampler_0), (uv_1), (0.0f)));
}

fn Hash_0( n_0 : f32) -> f32
{
    return fract(sin(n_0 * 12.98980045318603516f) * 43758.546875f);
}

fn HexEdge_0( v_0 : vec2<f32>) -> f32
{
    var i_0 : i32;
    var _S5 : vec2<f32>;
    var _S6 : vec2<f32>;
    const s_0 : vec2<f32> = vec2<f32>(1.0f, 1.73205077648162842f);
    var _S7 : vec2<f32> = abs(v_0);
    for(;;)
    {
        var result_0 : vec2<f32>;
        i_0 = i32(0);
        for(;;)
        {
            if(i_0 < i32(2))
            {
            }
            else
            {
                break;
            }
            result_0[i_0] = ((((_S7[i_0])) % ((s_0[i_0]))));
            i_0 = i_0 + i32(1);
        }
        _S5 = result_0;
        break;
    }
    var _S8 : vec2<f32> = s_0 * vec2<f32>(0.5f);
    var a_0 : vec2<f32> = _S5 - _S8;
    var _S9 : vec2<f32> = abs(v_0 - _S8);
    for(;;)
    {
        var result_1 : vec2<f32>;
        i_0 = i32(0);
        for(;;)
        {
            if(i_0 < i32(2))
            {
            }
            else
            {
                break;
            }
            result_1[i_0] = ((((_S9[i_0])) % ((s_0[i_0]))));
            i_0 = i_0 + i32(1);
        }
        _S6 = result_1;
        break;
    }
    var b_0 : vec2<f32> = _S6 - _S8;
    var _S10 : vec2<f32>;
    if((dot(a_0, a_0)) < (dot(b_0, b_0)))
    {
        _S10 = a_0;
    }
    else
    {
        _S10 = b_0;
    }
    var g_0 : vec2<f32> = abs(_S10);
    return 0.5f - max(dot(g_0, normalize(s_0)), g_0.x);
}

fn SceneSplit_0( uv_2 : vec2<f32>,  split_0 : vec2<f32>) -> vec3<f32>
{
    return vec3<f32>((textureSampleLevel((uScene_0), (uHudSampler_0), (uv_2 + split_0), (0.0f))).x, (textureSampleLevel((uScene_0), (uHudSampler_0), (uv_2), (0.0f))).y, (textureSampleLevel((uScene_0), (uHudSampler_0), (uv_2 - split_0), (0.0f))).z);
}

fn LinearToSrgb_0( c_0 : vec3<f32>) -> vec3<f32>
{
    var _S11 : vec3<f32> = max(c_0, vec3<f32>(0.0f));
    return select(vec3<f32>(1.0549999475479126f) * pow(_S11, vec3<f32>(0.4166666567325592f)) - vec3<f32>(0.05499999970197678f), _S11 * vec3<f32>(12.92000007629394531f), _S11 <= vec3<f32>(0.00313080009073019f));
}

struct pixelOutput_0
{
    @location(0) output_1 : vec4<f32>,
};

struct pixelInput_0
{
    @location(0) uv_3 : vec2<f32>,
};

@fragment
fn fragmentMain( _S12 : pixelInput_0, @builtin(position) position_1 : vec4<f32>) -> pixelOutput_0
{
    var intensity_0 : f32 = saturate(p_0.visor_0.x);
    if(intensity_0 <= 0.0f)
    {
        var _S13 : pixelOutput_0 = pixelOutput_0( Layer_0(_S12.uv_3) );
        return _S13;
    }
    var _S14 : vec2<f32> = vec2<f32>(2.0f);
    var centred_0 : vec2<f32> = (_S12.uv_3 - p_0.shake_0.xy * p_0.frame_0.zw * vec2<f32>(intensity_0)) * _S14 - vec2<f32>(1.0f);
    var bend_0 : f32 = dot(centred_0, centred_0);
    var centred_1 : vec2<f32> = centred_0 * vec2<f32>((1.0f + p_0.visor_0.y * intensity_0 * bend_0));
    var _S15 : vec2<f32> = vec2<f32>(0.5f);
    var uv_4 : vec2<f32> = centred_1 * _S15 + _S15;
    var fringe_0 : vec2<f32> = centred_1 * vec2<f32>((p_0.visor_0.z * intensity_0 * bend_0 * 0.5f)) * p_0.frame_0.zw;
    var red_0 : vec4<f32> = Layer_0(uv_4 + fringe_0);
    var green_0 : vec4<f32> = Layer_0(uv_4);
    var blue_0 : vec4<f32> = Layer_0(uv_4 - fringe_0);
    var _S16 : f32 = red_0.x;
    var _S17 : f32 = green_0.y;
    var _S18 : f32 = blue_0.z;
    var colour_0 : vec4<f32> = vec4<f32>(_S16, _S17, _S18, max(green_0.w, max(red_0.w, blue_0.w)));
    var bloom_0 : vec3<f32> = max(((textureSampleLevel((uGlow0_0), (uHudSampler_0), (uv_4), (0.0f))) * vec4<f32>(0.5f) + (textureSampleLevel((uGlow1_0), (uHudSampler_0), (uv_4), (0.0f))) * vec4<f32>(0.30000001192092896f) + (textureSampleLevel((uGlow2_0), (uHudSampler_0), (uv_4), (0.0f))) * vec4<f32>(0.20000000298023224f)).xyz - vec3<f32>(0.44999998807907104f), vec3<f32>(0.0f)) * vec3<f32>(1.81818175315856934f) * vec3<f32>((p_0.visor_0.w * intensity_0));
    var _S19 : vec3<f32> = vec3<f32>(_S16, _S17, _S18) + bloom_0;
    colour_0.x = _S19.x;
    colour_0.y = _S19.y;
    colour_0.z = _S19.z;
    colour_0[i32(3)] = saturate(colour_0.w + dot(bloom_0, vec3<f32>(0.29899999499320984f, 0.58700001239776611f, 0.11400000005960464f)) * 0.5f);
    var _S20 : f32 = uv_4.y;
    var y_1 : f32 = _S20 * p_0.frame_0.y;
    var _S21 : vec3<f32> = colour_0.xyz * vec3<f32>((1.0f - p_0.lines_0.y * intensity_0 * (0.5f - 0.5f * cos(y_1 * (p_0.lines_0.x / 1080.0f) * 6.28318548202514648f)) + 0.05999999865889549f * intensity_0 * exp(- pow((fract(_S20 - p_0.lines_0.w * 0.07999999821186066f) - 0.5f) * 9.0f, 2.0f))));
    colour_0.x = _S21.x;
    colour_0.y = _S21.y;
    colour_0.z = _S21.z;
    var appear_0 : f32 = saturate(p_0.lines_0.z);
    if(appear_0 < 1.0f)
    {
        var row_0 : f32 = floor(y_1 / 6.0f);
        colour_0 = colour_0 * vec4<f32>((step(Hash_0(row_0 * 1.37000000476837158f), appear_0) * mix(0.34999999403953552f, 1.0f, step(0.5f - appear_0 * 0.5f, Hash_0(row_0 + floor(p_0.lines_0.w * 30.0f))))));
    }
    var aspect_0 : f32 = p_0.frame_0.x * p_0.frame_0.w;
    var _S22 : vec2<f32> = _S12.uv_3 - _S15;
    var _S23 : vec2<f32> = vec2<f32>(aspect_0, 1.0f);
    var fromMiddle_0 : vec2<f32> = _S22 * _S23;
    var time_0 : f32 = p_0.lines_0.w;
    var tick_0 : f32 = floor(time_0 * 24.0f);
    var qa_0 : vec2<f32> = _S12.uv_3 * _S23;
    var tr_0 : vec2<f32> = qa_0 - vec2<f32>(aspect_0, 0.0f);
    var bl_0 : vec2<f32> = qa_0 - vec2<f32>(0.0f, 1.0f);
    var br_0 : vec2<f32> = qa_0 - _S23;
    var hit_0 : f32 = saturate((p_0.damage_0.x * exp(- dot(qa_0, qa_0) * 2.59999990463256836f) + p_0.damage_0.y * exp(- dot(tr_0, tr_0) * 2.59999990463256836f) + p_0.damage_0.z * exp(- dot(bl_0, bl_0) * 2.59999990463256836f) + p_0.damage_0.w * exp(- dot(br_0, br_0) * 2.59999990463256836f)) * 1.60000002384185791f) * intensity_0;
    const light_0 : vec3<f32> = vec3<f32>(0.0f, 0.0f, 0.0f);
    var threat_0 : f32 = saturate(p_0.alert_0.x) * intensity_0;
    var light_1 : vec3<f32>;
    if(threat_0 > 0.0f)
    {
        var oval_0 : f32 = length(_S22 * _S14);
        var side_0 : f32 = mix(0.30000001192092896f, pow(saturate(0.5f + 0.5f * cos(atan2(fromMiddle_0.x, - fromMiddle_0.y) - p_0.shake_0.z)), 6.0f), saturate(p_0.shake_0.w));
        var phase_0 : f32 = fract(time_0 * 0.80000001192092896f);
        light_1 = p_0.alert_0.yzw * vec3<f32>(((smoothstep(0.62000000476837158f + 0.25f * (1.0f - threat_0), 1.25f, oval_0) * side_0 * (0.34999999403953552f + 0.44999998807907104f * (1.0f - smoothstep(0.0f, 0.05999999865889549f, HexEdge_0(qa_0 * vec2<f32>(22.0f) + vec2<f32>(0.0f, time_0 * 0.34999999403953552f))))) * (0.75f + 0.25f * sin(time_0 * 4.0f + oval_0 * 18.0f)) + exp(- pow((oval_0 - (1.29999995231628418f - phase_0 * 0.55000001192092896f)) / 0.01200000010430813f, 2.0f)) * (1.0f - phase_0) * saturate((threat_0 - 0.69999998807907104f) / 0.25f) * side_0 * 0.89999997615814209f) * threat_0));
    }
    else
    {
        light_1 = light_0;
    }
    var _S24 : vec3<f32> = colour_0.xyz + light_1;
    colour_0.x = _S24.x;
    colour_0.y = _S24.y;
    colour_0.z = _S24.z;
    var _S25 : bool;
    if(hit_0 > 0.0f)
    {
        _S25 = (p_0.damageColour_0.w) > 0.5f;
    }
    else
    {
        _S25 = false;
    }
    var torn_0 : vec3<f32>;
    var tear_0 : f32;
    if(_S25)
    {
        var _S26 : f32 = _S12.uv_3.y;
        var slice_0 : f32 = floor(_S26 * 1080.0f / (14.0f + 10.0f * Hash_0(floor(_S26 * 31.0f) + tick_0)));
        var suv_0 : vec2<f32> = _S12.uv_3 + vec2<f32>(step(0.62000000476837158f, Hash_0(slice_0 * 3.17000007629394531f + tick_0)) * (Hash_0(slice_0 + tick_0 * 1.30999994277954102f) - 0.5f) * 0.05000000074505806f * hit_0, 0.0f);
        var picture_0 : vec3<f32> = mix(SceneSplit_0(suv_0, normalize(fromMiddle_0 + vec2<f32>(0.00009999999747379f)) * vec2<f32>(1.0f / aspect_0, 1.0f) * vec2<f32>((0.01200000010430813f * hit_0))), (textureSampleLevel((uSceneBlur_0), (uHudSampler_0), (suv_0), (0.0f))).xyz, vec3<f32>((0.40000000596046448f * hit_0)));
        var grey_0 : f32 = dot(picture_0, vec3<f32>(0.2125999927520752f, 0.71520000696182251f, 0.07220000028610229f));
        var torn_1 : vec3<f32> = mix(picture_0, vec3<f32>(grey_0, grey_0, grey_0), vec3<f32>((0.5f * hit_0))) * vec3<f32>((1.0f - 0.11999999731779099f * hit_0)) + p_0.damageColour_0.xyz * vec3<f32>((grey_0 * 0.25f * hit_0));
        if((p_0.damageColour_0.w) > 1.5f)
        {
            torn_0 = LinearToSrgb_0(torn_1);
        }
        else
        {
            torn_0 = torn_1;
        }
        tear_0 = saturate(hit_0 * 1.5f);
    }
    else
    {
        torn_0 = light_0;
        tear_0 = 0.0f;
    }
    var dim_0 : f32;
    if((p_0.damageColour_0.w) > 0.5f)
    {
        dim_0 = 0.0f;
    }
    else
    {
        dim_0 = hit_0 * 0.15000000596046448f;
    }
    var _S27 : vec4<f32> = mix(Layer_0(_S12.uv_3), colour_0, vec4<f32>(intensity_0));
    var layered_0 : vec4<f32> = _S27;
    var _S28 : vec3<f32> = _S27.xyz + torn_0 * vec3<f32>((tear_0 * (1.0f - _S27.w)));
    layered_0.x = _S28.x;
    layered_0.y = _S28.y;
    layered_0.z = _S28.z;
    layered_0[i32(3)] = layered_0[i32(3)] + max(tear_0, dim_0) * (1.0f - layered_0.w);
    var _S29 : pixelOutput_0 = pixelOutput_0( layered_0 );
    return _S29;
}

