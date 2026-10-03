struct LoadingPush_std140_0
{
    @align(16) time_0 : f32,
    @align(4) blur_0 : f32,
    @align(8) desaturate_0 : f32,
    @align(4) vignette_0 : f32,
    @align(16) zoom_0 : f32,
    @align(4) grain_0 : f32,
    @align(8) scanline_0 : f32,
    @align(4) opacity_0 : f32,
    @align(16) letterbox_0 : f32,
    @align(4) hasPicture_0 : f32,
    @align(8) aspect_0 : f32,
    @align(4) sweep_0 : f32,
    @align(16) reserved0_0 : f32,
    @align(4) reserved1_0 : f32,
    @align(8) reserved2_0 : f32,
    @align(4) reserved3_0 : f32,
};

@binding(0) @group(3) var<uniform> p_0 : LoadingPush_std140_0;
@binding(0) @group(0) var uTexture_0 : texture_2d<f32>;

@binding(1) @group(0) var uTextureSampler_0 : sampler;

@id(65) override cTargetEncodes_0 : i32 = i32(0);

@id(66) override cSamplerDecodes_0 : i32 = i32(0);

var<private> kDisc_0 : array<vec2<f32>, i32(16)> = array<vec2<f32>, i32(16)>( vec2<f32>(-0.61299997568130493f, -0.61699998378753662f), vec2<f32>(0.17000000178813934f, -0.03999999910593033f), vec2<f32>(-0.29899999499320984f, 0.73500001430511475f), vec2<f32>(0.64499998092651367f, 0.50700002908706665f), vec2<f32>(0.42100000381469727f, -0.73500001430511475f), vec2<f32>(-0.85900002717971802f, 0.11800000071525574f), vec2<f32>(0.90200001001358032f, -0.11699999868869781f), vec2<f32>(-0.18299999833106995f, -0.29100000858306885f), vec2<f32>(0.21899999678134918f, 0.41800001263618469f), vec2<f32>(-0.5559999942779541f, 0.34299999475479126f), vec2<f32>(0.70200002193450928f, 0.1289999932050705f), vec2<f32>(-0.11400000005960464f, 0.97600001096725464f), vec2<f32>(-0.38299998641014099f, -0.91200000047683716f), vec2<f32>(0.51200002431869507f, -0.37200000882148743f), vec2<f32>(-0.76200002431869507f, -0.28999999165534973f), vec2<f32>(0.06100000068545341f, -0.66500002145767212f) );
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
fn vertexMain(@builtin(vertex_index) vertexIndex_1 : u32) -> FullscreenVaryings_0
{
    return FullscreenTriangle_0(vertexIndex_1);
}

fn Hash_0( v_0 : vec2<f32>) -> f32
{
    return fract(sin(dot(v_0, vec2<f32>(127.09999847412109375f, 311.70001220703125f))) * 43758.546875f);
}

fn EncodeSrgb_0( c_0 : vec3<f32>) -> vec3<f32>
{
    return mix(vec3<f32>(1.0549999475479126f) * pow(max(c_0, vec3<f32>(0.0f, 0.0f, 0.0f)), vec3<f32>(0.4166666567325592f, 0.4166666567325592f, 0.4166666567325592f)) - vec3<f32>(0.05499999970197678f), c_0 * vec3<f32>(12.92000007629394531f), step(c_0, vec3<f32>(0.00313080009073019f, 0.00313080009073019f, 0.00313080009073019f)));
}

fn SampledDisplay_0( sampled_0 : vec3<f32>) -> vec3<f32>
{
    var _S4 : vec3<f32>;
    if(cSamplerDecodes_0 != i32(0))
    {
        _S4 = EncodeSrgb_0(sampled_0);
    }
    else
    {
        _S4 = sampled_0;
    }
    return _S4;
}

fn ValueNoise_0( v_1 : vec2<f32>) -> f32
{
    var i_0 : vec2<f32> = floor(v_1);
    var f_0 : vec2<f32> = fract(v_1);
    var f_1 : vec2<f32> = f_0 * f_0 * (vec2<f32>(3.0f) - vec2<f32>(2.0f) * f_0);
    var _S5 : f32 = f_1.x;
    return mix(mix(Hash_0(i_0), Hash_0(i_0 + vec2<f32>(1.0f, 0.0f)), _S5), mix(Hash_0(i_0 + vec2<f32>(0.0f, 1.0f)), Hash_0(i_0 + vec2<f32>(1.0f, 1.0f)), _S5), f_1.y);
}

fn Fbm_0( v_2 : vec2<f32>) -> f32
{
    var _S6 : vec2<f32> = v_2;
    var i_1 : i32 = i32(0);
    var amplitude_0 : f32 = 0.5f;
    var sum_0 : f32 = 0.0f;
    for(;;)
    {
        if(i_1 < i32(4))
        {
        }
        else
        {
            break;
        }
        var sum_1 : f32 = sum_0 + amplitude_0 * ValueNoise_0(_S6);
        var _S7 : vec2<f32> = _S6 * vec2<f32>(2.02999997138977051f) + vec2<f32>(17.0f, 9.0f);
        var amplitude_1 : f32 = amplitude_0 * 0.5f;
        var _S8 : i32 = i_1 + i32(1);
        _S6 = _S7;
        i_1 = _S8;
        amplitude_0 = amplitude_1;
        sum_0 = sum_1;
    }
    return sum_0;
}

fn DecodeSrgb_0( c_1 : vec3<f32>) -> vec3<f32>
{
    return mix(pow(max((c_1 + vec3<f32>(0.05499999970197678f)) / vec3<f32>(1.0549999475479126f), vec3<f32>(0.0f, 0.0f, 0.0f)), vec3<f32>(2.40000009536743164f, 2.40000009536743164f, 2.40000009536743164f)), c_1 / vec3<f32>(12.92000007629394531f), step(c_1, vec3<f32>(0.04044999927282333f, 0.04044999927282333f, 0.04044999927282333f)));
}

fn ToTarget_0( display_0 : vec4<f32>) -> vec4<f32>
{
    var _S9 : vec4<f32>;
    if(cTargetEncodes_0 != i32(0))
    {
        _S9 = vec4<f32>(DecodeSrgb_0(display_0.xyz), display_0.w);
    }
    else
    {
        _S9 = display_0;
    }
    return _S9;
}

struct pixelOutput_0
{
    @location(0) output_1 : vec4<f32>,
};

struct pixelInput_0
{
    @location(0) uv_1 : vec2<f32>,
};

@fragment
fn fragmentMain( _S10 : pixelInput_0, @builtin(position) position_1 : vec4<f32>) -> pixelOutput_0
{
    var colour_0 : vec3<f32>;
    if((p_0.hasPicture_0) > 0.5f)
    {
        var _S11 : vec2<f32> = vec2<f32>(0.5f);
        var _S12 : vec2<f32> = (_S10.uv_1 - _S11) / vec2<f32>(max(p_0.zoom_0, 0.5f)) + _S11;
        var radius_0 : f32 = p_0.blur_0 * 0.02199999988079071f;
        var _S13 : vec2<f32> = vec2<f32>(radius_0 / max(p_0.aspect_0, 0.10000000149011612f), radius_0);
        var turn_0 : f32 = Hash_0(_S10.uv_1 * vec2<f32>(1919.0f, 1079.0f)) * 6.28318548202514648f;
        var _S14 : f32 = cos(turn_0);
        var _S15 : f32 = sin(turn_0);
        var _S16 : mat2x2<f32> = mat2x2<f32>(_S14, _S15, - _S15, _S14);
        const _S17 : vec3<f32> = vec3<f32>(0.0f, 0.0f, 0.0f);
        var i_2 : i32 = i32(0);
        colour_0 = _S17;
        for(;;)
        {
            if(i_2 < i32(16))
            {
            }
            else
            {
                break;
            }
            var sum_2 : vec3<f32> = colour_0 + SampledDisplay_0((textureSample((uTexture_0), (uTextureSampler_0), (clamp(_S12 + (((_S16) * (kDisc_0[i_2]))) * _S13, vec2<f32>(0.0020000000949949f, 0.0020000000949949f), vec2<f32>(0.99800002574920654f, 0.99800002574920654f))))).xyz);
            i_2 = i_2 + i32(1);
            colour_0 = sum_2;
        }
        colour_0 = colour_0 / vec3<f32>(16.0f);
    }
    else
    {
        var cell_0 : vec2<f32> = fract(_S10.uv_1 * vec2<f32>(20.0f * p_0.aspect_0, 20.0f));
        var _S18 : f32 = cell_0.x;
        var _S19 : f32 = cell_0.y;
        colour_0 = mix(vec3<f32>(0.02999999932944775f, 0.03799999877810478f, 0.05000000074505806f), vec3<f32>(0.10499999672174454f, 0.12999999523162842f, 0.16500000655651093f), vec3<f32>((Fbm_0(_S10.uv_1 * vec2<f32>(3.0f * p_0.aspect_0, 3.0f) + vec2<f32>(p_0.time_0 * 0.02500000037252903f, - p_0.time_0 * 0.01700000092387199f)) * 0.85000002384185791f + (1.0f - _S10.uv_1.y) * 0.20000000298023224f))) + vec3<f32>((1.0f - smoothstep(0.0f, 0.05000000074505806f, min(min(_S18, _S19), min(1.0f - _S18, 1.0f - _S19))))) * vec3<f32>(0.55000001192092896f, 0.69999998807907104f, 0.89999997615814209f) * vec3<f32>(0.04500000178813934f);
    }
    var luma_0 : f32 = dot(colour_0, vec3<f32>(0.29899999499320984f, 0.58700001239776611f, 0.11400000005960464f));
    var _S20 : vec3<f32> = vec3<f32>(luma_0, luma_0, luma_0);
    var _S21 : f32 = _S10.uv_1.y;
    var band_0 : f32 = _S10.uv_1.x + _S21 * 0.34999999403953552f - p_0.sweep_0;
    var _S22 : f32 = p_0.letterbox_0 - 0.00300000002607703f;
    var _S23 : f32 = p_0.letterbox_0 + 0.00300000002607703f;
    var _S24 : pixelOutput_0 = pixelOutput_0( ToTarget_0(vec4<f32>(max(((mix(colour_0, mix(_S20, _S20 * vec3<f32>(0.8399999737739563f, 0.93999999761581421f, 1.10000002384185791f), vec3<f32>(0.64999997615814209f)), vec3<f32>(p_0.desaturate_0)) * vec3<f32>(mix(1.0f, 0.51999998092651367f, p_0.desaturate_0)) + vec3<f32>(exp(- band_0 * band_0 * 36.0f)) * vec3<f32>(0.08500000089406967f, 0.10000000149011612f, 0.125f) * vec3<f32>(p_0.desaturate_0)) * vec3<f32>(mix(1.0f, smoothstep(0.98000001907348633f, 0.2800000011920929f, length((_S10.uv_1 - vec2<f32>(0.5f)) * vec2<f32>(1.10000002384185791f, 1.0f))), p_0.vignette_0)) * vec3<f32>((1.0f - p_0.scanline_0 * (0.5f + 0.5f * sin(_S21 * 1800.0f)))) + vec3<f32>(((Hash_0(_S10.uv_1 * vec2<f32>(1920.0f, 1080.0f) + vec2<f32>(fract(p_0.time_0 * 7.30999994277954102f))) - 0.5f) * p_0.grain_0))) * vec3<f32>((smoothstep(_S22, _S23, _S21) * smoothstep(_S22, _S23, 1.0f - _S21))), vec3<f32>(0.0f, 0.0f, 0.0f)), p_0.opacity_0)) );
    return _S24;
}

