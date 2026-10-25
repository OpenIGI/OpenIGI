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

fn SceneSplit_0( uv_2 : vec2<f32>,  split_0 : vec2<f32>) -> vec3<f32>
{
    return vec3<f32>((textureSampleLevel((uScene_0), (uHudSampler_0), (uv_2 + split_0), (0.0f))).x, (textureSampleLevel((uScene_0), (uHudSampler_0), (uv_2), (0.0f))).y, (textureSampleLevel((uScene_0), (uHudSampler_0), (uv_2 - split_0), (0.0f))).z);
}

fn LinearToSrgb_0( c_0 : vec3<f32>) -> vec3<f32>
{
    var _S5 : vec3<f32> = max(c_0, vec3<f32>(0.0f));
    return select(vec3<f32>(1.0549999475479126f) * pow(_S5, vec3<f32>(0.4166666567325592f)) - vec3<f32>(0.05499999970197678f), _S5 * vec3<f32>(12.92000007629394531f), _S5 <= vec3<f32>(0.00313080009073019f));
}

fn Grain_0( pixel_0 : vec2<f32>,  tick_0 : f32) -> f32
{
    var h_0 : u32 = ((((u32(pixel_0.x) * u32(1664525)) ^ ((u32(pixel_0.y) * u32(1013904223))))) ^ ((u32(tick_0) * u32(2654435761))));
    var h_1 : u32 = ((h_0 ^ (((h_0 >> (u32(16))))))) * u32(2146121005);
    var h_2 : u32 = ((h_1 ^ (((h_1 >> (u32(15))))))) * u32(2221713035);
    return f32((h_2 ^ (((h_2 >> (u32(16))))))) * 2.32830643653869629e-10f;
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
fn fragmentMain( _S6 : pixelInput_0, @builtin(position) position_1 : vec4<f32>) -> pixelOutput_0
{
    var intensity_0 : f32 = saturate(p_0.visor_0.x);
    if(intensity_0 <= 0.0f)
    {
        var _S7 : pixelOutput_0 = pixelOutput_0( Layer_0(_S6.uv_3) );
        return _S7;
    }
    var centred_0 : vec2<f32> = (_S6.uv_3 - p_0.shake_0.xy * p_0.frame_0.zw * vec2<f32>(intensity_0)) * vec2<f32>(2.0f) - vec2<f32>(1.0f);
    var bend_0 : f32 = dot(centred_0, centred_0);
    var centred_1 : vec2<f32> = centred_0 * vec2<f32>((1.0f + p_0.visor_0.y * intensity_0 * bend_0));
    var _S8 : vec2<f32> = vec2<f32>(0.5f);
    var uv_4 : vec2<f32> = centred_1 * _S8 + _S8;
    var fringe_0 : vec2<f32> = centred_1 * vec2<f32>((p_0.visor_0.z * intensity_0 * bend_0 * 0.5f)) * p_0.frame_0.zw;
    var red_0 : vec4<f32> = Layer_0(uv_4 + fringe_0);
    var green_0 : vec4<f32> = Layer_0(uv_4);
    var blue_0 : vec4<f32> = Layer_0(uv_4 - fringe_0);
    var _S9 : f32 = red_0.x;
    var _S10 : f32 = green_0.y;
    var _S11 : f32 = blue_0.z;
    var colour_0 : vec4<f32> = vec4<f32>(_S9, _S10, _S11, max(green_0.w, max(red_0.w, blue_0.w)));
    var bloom_0 : vec3<f32> = max(((textureSampleLevel((uGlow0_0), (uHudSampler_0), (uv_4), (0.0f))) * vec4<f32>(0.5f) + (textureSampleLevel((uGlow1_0), (uHudSampler_0), (uv_4), (0.0f))) * vec4<f32>(0.30000001192092896f) + (textureSampleLevel((uGlow2_0), (uHudSampler_0), (uv_4), (0.0f))) * vec4<f32>(0.20000000298023224f)).xyz - vec3<f32>(0.44999998807907104f), vec3<f32>(0.0f)) * vec3<f32>(1.81818175315856934f) * vec3<f32>((p_0.visor_0.w * intensity_0));
    var _S12 : vec3<f32> = vec3<f32>(_S9, _S10, _S11) + bloom_0;
    colour_0.x = _S12.x;
    colour_0.y = _S12.y;
    colour_0.z = _S12.z;
    colour_0[i32(3)] = saturate(colour_0.w + dot(bloom_0, vec3<f32>(0.29899999499320984f, 0.58700001239776611f, 0.11400000005960464f)) * 0.5f);
    var _S13 : f32 = uv_4.y;
    var y_1 : f32 = _S13 * p_0.frame_0.y;
    var _S14 : vec3<f32> = colour_0.xyz * vec3<f32>((1.0f - p_0.lines_0.y * intensity_0 * (0.5f - 0.5f * cos(y_1 * (p_0.lines_0.x / 1080.0f) * 6.28318548202514648f)) + 0.05999999865889549f * intensity_0 * exp(- pow((fract(_S13 - p_0.lines_0.w * 0.07999999821186066f) - 0.5f) * 9.0f, 2.0f))));
    colour_0.x = _S14.x;
    colour_0.y = _S14.y;
    colour_0.z = _S14.z;
    var appear_0 : f32 = saturate(p_0.lines_0.z);
    if(appear_0 < 1.0f)
    {
        var row_0 : f32 = floor(y_1 / 6.0f);
        colour_0 = colour_0 * vec4<f32>((step(Hash_0(row_0 * 1.37000000476837158f), appear_0) * mix(0.34999999403953552f, 1.0f, step(0.5f - appear_0 * 0.5f, Hash_0(row_0 + floor(p_0.lines_0.w * 30.0f))))));
    }
    var aspect_0 : f32 = p_0.frame_0.x * p_0.frame_0.w;
    var _S15 : vec2<f32> = _S6.uv_3 - _S8;
    var _S16 : vec2<f32> = vec2<f32>(aspect_0, 1.0f);
    var fromMiddle_0 : vec2<f32> = _S15 * _S16;
    var tick_1 : f32 = floor(p_0.lines_0.w * 24.0f);
    var qa_0 : vec2<f32> = _S6.uv_3 * _S16;
    var tr_0 : vec2<f32> = qa_0 - vec2<f32>(aspect_0, 0.0f);
    var bl_0 : vec2<f32> = qa_0 - vec2<f32>(0.0f, 1.0f);
    var br_0 : vec2<f32> = qa_0 - _S16;
    var hit_0 : f32 = saturate((p_0.damage_0.x * exp(- dot(qa_0, qa_0) * 2.59999990463256836f) + p_0.damage_0.y * exp(- dot(tr_0, tr_0) * 2.59999990463256836f) + p_0.damage_0.z * exp(- dot(bl_0, bl_0) * 2.59999990463256836f) + p_0.damage_0.w * exp(- dot(br_0, br_0) * 2.59999990463256836f)) * 1.60000002384185791f) * intensity_0;
    const _S17 : vec3<f32> = vec3<f32>(0.0f, 0.0f, 0.0f);
    var _S18 : bool;
    if(hit_0 > 0.0f)
    {
        _S18 = (p_0.damageColour_0.w) > 0.5f;
    }
    else
    {
        _S18 = false;
    }
    var torn_0 : vec3<f32>;
    var tear_0 : f32;
    if(_S18)
    {
        var _S19 : f32 = _S6.uv_3.y;
        var slice_0 : f32 = floor(_S19 * 1080.0f / (14.0f + 10.0f * Hash_0(floor(_S19 * 31.0f) + tick_1)));
        var suv_0 : vec2<f32> = _S6.uv_3 + vec2<f32>(step(0.62000000476837158f, Hash_0(slice_0 * 3.17000007629394531f + tick_1)) * (Hash_0(slice_0 + tick_1 * 1.30999994277954102f) - 0.5f) * 0.05000000074505806f * hit_0, 0.0f);
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
        torn_0 = _S17;
        tear_0 = 0.0f;
    }
    var wheel_0 : f32 = saturate(p_0.shake_0.z) * intensity_0;
    if(wheel_0 > 0.0f)
    {
        _S18 = (p_0.damageColour_0.w) > 0.5f;
    }
    else
    {
        _S18 = false;
    }
    if(_S18)
    {
        var i_0 : i32 = i32(0);
        var smear_0 : vec3<f32> = _S17;
        for(;;)
        {
            if(i_0 < i32(10))
            {
            }
            else
            {
                break;
            }
            var smear_1 : vec3<f32> = smear_0 + (textureSampleLevel((uScene_0), (uHudSampler_0), (_S8 + _S15 * vec2<f32>((1.0f - f32(i_0) / 9.0f * 0.07000000029802322f * wheel_0))), (0.0f))).xyz;
            i_0 = i_0 + i32(1);
            smear_0 = smear_1;
        }
        var smear_2 : vec3<f32> = mix(smear_0 * vec3<f32>(0.10000000149011612f), (textureSampleLevel((uSceneBlur_0), (uHudSampler_0), (_S6.uv_3), (0.0f))).xyz, vec3<f32>((0.34999999403953552f * wheel_0)));
        var grey_1 : f32 = dot(smear_2, vec3<f32>(0.2125999927520752f, 0.71520000696182251f, 0.07220000028610229f));
        var grey_2 : f32 = grey_1 * grey_1 * (3.0f - 2.0f * grey_1);
        var drained_0 : vec3<f32> = mix(smear_2, vec3<f32>(grey_2, grey_2 * 0.98500001430511475f, grey_2 * 0.95999997854232788f), vec3<f32>((0.92000001668930054f * wheel_0))) * vec3<f32>(((1.0f - 0.30000001192092896f * wheel_0) * (1.0f - 0.60000002384185791f * wheel_0 * smoothstep(0.25f, 0.85000002384185791f, length(fromMiddle_0))))) + vec3<f32>(((Grain_0(_S6.uv_3 * p_0.frame_0.xy, tick_1) - 0.5f) * 0.02500000037252903f * wheel_0));
        var drained_1 : vec3<f32>;
        if((p_0.damageColour_0.w) > 1.5f)
        {
            drained_1 = LinearToSrgb_0(drained_0);
        }
        else
        {
            drained_1 = drained_0;
        }
        var _S20 : f32 = max(tear_0, wheel_0);
        torn_0 = mix(torn_0 * vec3<f32>(tear_0), drained_1, vec3<f32>(wheel_0)) / vec3<f32>(max(_S20, 0.00009999999747379f));
        tear_0 = _S20;
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
    var _S21 : vec4<f32> = mix(Layer_0(_S6.uv_3), colour_0, vec4<f32>(intensity_0));
    var layered_0 : vec4<f32> = _S21;
    var _S22 : vec3<f32> = _S21.xyz + torn_0 * vec3<f32>((tear_0 * (1.0f - _S21.w)));
    layered_0.x = _S22.x;
    layered_0.y = _S22.y;
    layered_0.z = _S22.z;
    layered_0[i32(3)] = layered_0[i32(3)] + max(tear_0, dim_0) * (1.0f - layered_0.w);
    var _S23 : pixelOutput_0 = pixelOutput_0( layered_0 );
    return _S23;
}

