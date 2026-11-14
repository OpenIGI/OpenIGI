struct MapFeedParams_std140_0
{
    @align(16) frame_0 : vec4<f32>,
    @align(16) signal_0 : vec4<f32>,
    @align(16) tube_0 : vec4<f32>,
    @align(16) look_0 : vec4<f32>,
    @align(16) shadow_0 : vec4<f32>,
    @align(16) body_0 : vec4<f32>,
    @align(16) highlight_0 : vec4<f32>,
};

@binding(0) @group(3) var<uniform> p_0 : MapFeedParams_std140_0;
@binding(0) @group(0) var uScene_0 : texture_2d<f32>;

@binding(1) @group(0) var uSceneSampler_0 : sampler;

@binding(2) @group(0) var uSceneSoft_0 : texture_2d<f32>;

@binding(3) @group(0) var uSceneWide_0 : texture_2d<f32>;

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

fn LinearToSrgb_0( c_0 : vec3<f32>) -> vec3<f32>
{
    var _S4 : vec3<f32> = max(c_0, vec3<f32>(0.0f));
    return select(vec3<f32>(1.0549999475479126f) * pow(_S4, vec3<f32>(0.4166666567325592f)) - vec3<f32>(0.05499999970197678f), _S4 * vec3<f32>(12.92000007629394531f), _S4 <= vec3<f32>(0.00313080009073019f));
}

fn Hash_0( n_0 : f32) -> f32
{
    return fract(sin(n_0 * 12.98980045318603516f) * 43758.546875f);
}

fn Luminance_0( c_1 : vec3<f32>) -> f32
{
    return dot(c_1, vec3<f32>(0.2125999927520752f, 0.71520000696182251f, 0.07220000028610229f));
}

fn Ground_0( uv_1 : vec2<f32>,  soften_0 : f32) -> f32
{
    var soft_0 : f32 = Luminance_0((textureSampleLevel((uSceneSoft_0), (uSceneSampler_0), (uv_1), (0.0f))).xyz);
    var wide_0 : f32 = Luminance_0((textureSampleLevel((uSceneWide_0), (uSceneSampler_0), (uv_1), (0.0f))).xyz);
    return max(mix(mix(Luminance_0((textureSampleLevel((uScene_0), (uSceneSampler_0), (uv_1), (0.0f))).xyz), soft_0, 0.5f), soft_0, soften_0) + (soft_0 - wide_0) * 0.60000002384185791f, 0.0f) * mix(1.0f, clamp(0.15999999642372131f / (wide_0 + 0.03999999910593033f), 0.80000001192092896f, 2.59999990463256836f), 0.55000001192092896f);
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
    @location(0) uv_2 : vec2<f32>,
};

@fragment
fn fragmentMain( _S5 : pixelInput_0, @builtin(position) position_1 : vec4<f32>) -> pixelOutput_0
{
    var amount_0 : f32 = saturate(p_0.signal_0.x);
    var picture_0 : vec3<f32> = (textureSampleLevel((uScene_0), (uSceneSampler_0), (_S5.uv_2), (0.0f))).xyz;
    var encode_0 : bool = (p_0.look_0.z) > 1.5f;
    var colour_0 : vec3<f32>;
    if(amount_0 <= 0.0f)
    {
        if(encode_0)
        {
            colour_0 = LinearToSrgb_0(picture_0);
        }
        else
        {
            colour_0 = picture_0;
        }
        var _S6 : pixelOutput_0 = pixelOutput_0( vec4<f32>(colour_0, 1.0f) );
        return _S6;
    }
    var time_0 : f32 = p_0.tube_0.x;
    var tick_1 : f32 = floor(time_0 * 24.0f);
    var boot_0 : f32 = saturate(p_0.signal_0.y);
    var disturbed_0 : f32 = saturate(boot_0 + saturate(p_0.look_0.y));
    var aspect_0 : f32 = p_0.frame_0.x * p_0.frame_0.w;
    var _S7 : vec2<f32> = vec2<f32>(2.0f);
    var _S8 : vec2<f32> = vec2<f32>(1.0f);
    var centred_0 : vec2<f32> = _S5.uv_2 * _S7 - _S8;
    var _S9 : vec2<f32> = vec2<f32>(0.5f);
    var _S10 : vec2<f32> = centred_0 * vec2<f32>((1.0f + p_0.tube_0.y * amount_0 * dot(centred_0, centred_0))) * _S9 + _S9;
    var uv_3 : vec2<f32> = _S10;
    var face_0 : vec2<f32> = abs(_S10 * _S7 - _S8);
    var outside_0 : f32 = length(max(face_0 - vec2<f32>(0.93999999761581421f), vec2<f32>(0.0f)) * vec2<f32>(aspect_0, 1.0f)) - 0.05999999865889549f + max(max(face_0.x, face_0.y) - 1.0f, 0.0f);
    var inside_0 : f32 = 1.0f - smoothstep(-0.00400000018998981f, 0.00400000018998981f, outside_0);
    var _S11 : f32 = _S10.y;
    var rowBand_0 : f32 = floor(_S11 * 54.0f);
    var slice_0 : f32 = floor(_S11 * 1080.0f / (10.0f + 22.0f * Hash_0(floor(_S11 * 23.0f) + tick_1)));
    uv_3[i32(0)] = uv_3[i32(0)] + (step(0.97500002384185791f, Hash_0(floor(time_0 * 2.0f) + 7.0f)) * step(0.85000002384185791f, Hash_0(rowBand_0 + floor(time_0 * 9.0f))) * (Hash_0(rowBand_0 * 1.70000004768371582f + tick_1) - 0.5f) * 0.00999999977648258f * amount_0 + step(0.55000001192092896f, Hash_0(slice_0 * 3.17000007629394531f + tick_1)) * (Hash_0(slice_0 + tick_1 * 1.30999994277954102f) - 0.5f) * 0.09000000357627869f * disturbed_0);
    var dim_0 : f32 = saturate(p_0.look_0.x);
    var soften_1 : f32 = dim_0 * 0.85000002384185791f;
    var light_0 : f32 = Ground_0(uv_3, soften_1);
    var v_0 : f32 = saturate((pow(saturate(max(light_0, mix(light_0, Ground_0(uv_3 - vec2<f32>(3.5f * p_0.frame_0.z, 0.0f), soften_1), 0.34999999403953552f))), 0.45454543828964233f) - 0.5f) * 1.08000004291534424f + 0.5f);
    var block_0 : vec2<f32> = floor(uv_3 * vec2<f32>(48.0f, 27.0f));
    var _S12 : f32 = 0.5f * disturbed_0;
    var _S13 : f32 = block_0.x;
    var _S14 : f32 = block_0.y;
    var v_1 : f32 = mix(mix(v_0, v_0 * v_0 * (3.0f - 2.0f * v_0), 0.60000002384185791f), Hash_0(_S13 * 1.29999995231628418f + _S14 * 91.0f + tick_1), step(1.0f - _S12, Hash_0(_S13 + _S14 * 57.0f + tick_1 * 3.09999990463256836f)) * 0.80000001192092896f);
    if(v_1 < 0.60000002384185791f)
    {
        colour_0 = mix(p_0.shadow_0.xyz, p_0.body_0.xyz, vec3<f32>((v_1 * 1.66666662693023682f)));
    }
    else
    {
        colour_0 = mix(p_0.body_0.xyz, p_0.highlight_0.xyz, vec3<f32>(((v_1 - 0.60000002384185791f) * 2.5f)));
    }
    var _S15 : f32 = max(3.0f, floor(p_0.frame_0.y / 360.0f));
    var _S16 : f32 = - fract(time_0 * 0.2199999988079071f - uv_3.y);
    var colour_1 : vec3<f32> = ((colour_0 * vec3<f32>(0.72000002861022949f) + p_0.body_0.xyz * vec3<f32>((max(pow(saturate(Luminance_0((textureSampleLevel((uSceneSoft_0), (uSceneSampler_0), (uv_3), (0.0f))).xyz) * 1.60000002384185791f), 0.45454543828964233f) - 0.55000001192092896f, 0.0f) * 0.5f))) * vec3<f32>((mix(1.0f - p_0.tube_0.w * 2.20000004768371582f, 1.0f, pow(0.5f - 0.5f * cos(fract(uv_3.y * p_0.frame_0.y / _S15) * 6.28318548202514648f), mix(1.60000002384185791f, 0.60000002384185791f, v_1))) * (1.0f - 0.14000000059604645f * step(0.6600000262260437f, fract(_S5.uv_2.x * p_0.frame_0.x / _S15))))) * vec3<f32>((0.86000001430511475f + 0.30000001192092896f * exp(_S16 * 5.0f))) + p_0.highlight_0.xyz * vec3<f32>((0.2800000011920929f * exp(_S16 * 160.0f)))) * vec3<f32>(((1.0f - 0.07000000029802322f * exp(- pow((fract(uv_3.y + time_0 * 0.04500000178813934f) - 0.5f) * 5.0f, 2.0f))) * (1.0f + 0.01200000010430813f * sin(time_0 * 47.0f))));
    var sweep_0 : f32 = saturate(p_0.signal_0.z);
    if(sweep_0 > 0.0f)
    {
        var from_0 : f32 = uv_3.y - p_0.signal_0.w;
        colour_0 = colour_1 * vec3<f32>((1.0f - sweep_0 * 0.75f * smoothstep(0.0f, 0.02999999932944775f, from_0))) + p_0.highlight_0.xyz * vec3<f32>((sweep_0 * 0.89999997615814209f * exp(- pow(from_0 * 70.0f, 2.0f))));
    }
    else
    {
        colour_0 = colour_1;
    }
    var result_0 : vec3<f32> = mix(picture_0, max(((colour_0 + p_0.body_0.xyz * vec3<f32>(((Grain_0(_S5.uv_2 * p_0.frame_0.xy, tick_1) - 0.5f) * (p_0.look_0.w + _S12)))) * vec3<f32>((1.0f + 0.55000001192092896f * boot_0)) * vec3<f32>((1.0f - 0.55000001192092896f * smoothstep(0.55000001192092896f, 1.25f, length((uv_3 * _S7 - _S8) * vec2<f32>(0.81999999284744263f, 1.0f))))) + p_0.highlight_0.xyz * vec3<f32>((0.01200000010430813f * (smoothstep(0.55000001192092896f, 0.0f, uv_3.y) * smoothstep(1.29999995231628418f, 0.20000000298023224f, abs(uv_3.x * 2.0f - 0.69999998807907104f))))) + p_0.body_0.xyz * vec3<f32>((0.25f * smoothstep(-0.02999999932944775f, 0.0f, outside_0) * inside_0))) * vec3<f32>((1.0f - 0.55000001192092896f * dim_0)), vec3<f32>(0.0f)) * vec3<f32>(inside_0), vec3<f32>(amount_0));
    if(encode_0)
    {
        colour_0 = LinearToSrgb_0(result_0);
    }
    else
    {
        colour_0 = result_0;
    }
    var _S17 : pixelOutput_0 = pixelOutput_0( vec4<f32>(colour_0, 1.0f) );
    return _S17;
}

