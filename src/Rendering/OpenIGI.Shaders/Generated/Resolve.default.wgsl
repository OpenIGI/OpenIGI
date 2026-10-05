struct Resolve_std140_0
{
    @align(16) params_0 : vec4<f32>,
    @align(16) vignette_0 : vec4<f32>,
    @align(16) grain_0 : vec4<f32>,
    @align(16) lens_0 : vec4<f32>,
    @align(16) grade_0 : vec4<f32>,
};

@binding(0) @group(3) var<uniform> resolve_0 : Resolve_std140_0;
@binding(0) @group(0) var uScene_0 : texture_2d<f32>;

@binding(1) @group(0) var uSceneSampler_0 : sampler;

@binding(4) @group(0) var uColourLut_0 : texture_3d<f32>;

@binding(5) @group(0) var uColourLutSampler_0 : sampler;

@binding(2) @group(0) var uBloom_0 : texture_2d<f32>;

@binding(3) @group(0) var uBloomSampler_0 : sampler;

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

fn EncodeSrgb_0( c_0 : vec3<f32>) -> vec3<f32>
{
    return mix(vec3<f32>(1.0549999475479126f) * pow(max(c_0, vec3<f32>(0.0f, 0.0f, 0.0f)), vec3<f32>(0.4166666567325592f, 0.4166666567325592f, 0.4166666567325592f)) - vec3<f32>(0.05499999970197678f), c_0 * vec3<f32>(12.92000007629394531f), step(c_0, vec3<f32>(0.00313080009073019f, 0.00313080009073019f, 0.00313080009073019f)));
}

fn SceneColour_0( uv_1 : vec2<f32>) -> vec3<f32>
{
    var texel_0 : vec2<f32> = resolve_0.lens_0.zw;
    var colour_0 : vec3<f32>;
    if((resolve_0.lens_0.x) > 0.0f)
    {
        var fromCentre_0 : vec2<f32> = uv_1 - vec2<f32>(0.5f);
        var shift_0 : vec2<f32> = fromCentre_0 * vec2<f32>(dot(fromCentre_0, fromCentre_0)) * vec2<f32>(resolve_0.lens_0.x) * texel_0 * vec2<f32>(4.0f);
        colour_0 = vec3<f32>((textureSample((uScene_0), (uSceneSampler_0), (uv_1 + shift_0))).x, (textureSample((uScene_0), (uSceneSampler_0), (uv_1))).y, (textureSample((uScene_0), (uSceneSampler_0), (uv_1 - shift_0))).z);
    }
    else
    {
        colour_0 = (textureSample((uScene_0), (uSceneSampler_0), (uv_1))).xyz;
    }
    if((resolve_0.lens_0.y) > 0.0f)
    {
        var _S4 : vec2<f32> = vec2<f32>(texel_0.x, 0.0f);
        var _S5 : vec2<f32> = vec2<f32>(0.0f, texel_0.y);
        colour_0 = max(colour_0 + (colour_0 - ((textureSample((uScene_0), (uSceneSampler_0), (uv_1 + _S4))).xyz + (textureSample((uScene_0), (uSceneSampler_0), (uv_1 - _S4))).xyz + (textureSample((uScene_0), (uSceneSampler_0), (uv_1 + _S5))).xyz + (textureSample((uScene_0), (uSceneSampler_0), (uv_1 - _S5))).xyz) * vec3<f32>(0.25f)) * vec3<f32>(resolve_0.lens_0.y), vec3<f32>(0.0f, 0.0f, 0.0f));
    }
    return colour_0;
}

fn Reinhard_0( c_1 : vec3<f32>,  white_0 : f32) -> vec3<f32>
{
    var _S6 : vec3<f32> = vec3<f32>(1.0f);
    return c_1 * (_S6 + c_1 / vec3<f32>((white_0 * white_0))) / (_S6 + c_1);
}

fn ApplyLut_0( colour_1 : vec3<f32>) -> vec3<f32>
{
    var amount_0 : f32 = resolve_0.grain_0.z;
    var size_0 : f32 = resolve_0.grain_0.w;
    var _S7 : bool;
    if(amount_0 <= 0.0f)
    {
        _S7 = true;
    }
    else
    {
        _S7 = size_0 < 2.0f;
    }
    if(_S7)
    {
        return colour_1;
    }
    return mix(colour_1, (textureSample((uColourLut_0), (uColourLutSampler_0), (clamp(colour_1, vec3<f32>(0.0f), vec3<f32>(1.0f)) * vec3<f32>(((size_0 - 1.0f) / size_0)) + vec3<f32>((0.5f / size_0))))).xyz, vec3<f32>(clamp(amount_0, 0.0f, 1.0f)));
}

fn ApplyGrade_0( colour_2 : vec3<f32>) -> vec3<f32>
{
    var _S8 : vec3<f32>;
    if((resolve_0.grade_0.y) != 1.0f)
    {
        var luminance_0 : f32 = dot(colour_2, vec3<f32>(0.2125999927520752f, 0.71520000696182251f, 0.07220000028610229f));
        _S8 = mix(vec3<f32>(luminance_0, luminance_0, luminance_0), colour_2, vec3<f32>(resolve_0.grade_0.y));
    }
    else
    {
        _S8 = colour_2;
    }
    if((resolve_0.grade_0.x) != 1.0f)
    {
        var _S9 : vec3<f32> = vec3<f32>(0.5f);
        _S8 = (_S8 - _S9) * vec3<f32>(resolve_0.grade_0.x) + _S9;
    }
    if((resolve_0.grade_0.z) > 0.5f)
    {
        var phosphor_0 : f32 = pow(dot(max(_S8, vec3<f32>(0.0f, 0.0f, 0.0f)), vec3<f32>(0.2125999927520752f, 0.71520000696182251f, 0.07220000028610229f)), 1.10000002384185791f);
        _S8 = vec3<f32>(phosphor_0 * 0.01799999922513962f, phosphor_0 * 0.55000001192092896f, phosphor_0 * 0.05999999865889549f);
    }
    return _S8;
}

fn ApplyGrain_0( colour_3 : vec3<f32>,  uv_2 : vec2<f32>) -> vec3<f32>
{
    var noise_0 : f32 = fract(sin(dot(uv_2 + vec2<f32>(fract(resolve_0.grain_0.y)), vec2<f32>(12.98980045318603516f, 78.233001708984375f))) * 43758.546875f);
    var _S10 : vec3<f32>;
    if((resolve_0.grain_0.x) > 0.0f)
    {
        _S10 = colour_3 + vec3<f32>(((noise_0 - 0.5f) * resolve_0.grain_0.x));
    }
    else
    {
        _S10 = colour_3;
    }
    if((resolve_0.vignette_0.w) > 0.0f)
    {
        var signed_0 : f32 = noise_0 * 2.0f - 1.0f;
        _S10 = _S10 + vec3<f32>((f32((i32(sign((signed_0))))) * (1.0f - sqrt(saturate(1.0f - abs(signed_0)))) * resolve_0.vignette_0.w));
    }
    return _S10;
}

fn VignetteMask_0( uv_3 : vec2<f32>) -> f32
{
    var intensity_0 : f32 = resolve_0.vignette_0.x;
    if(intensity_0 <= 0.0f)
    {
        return 1.0f;
    }
    var aspect_0 : f32 = resolve_0.vignette_0.y;
    var s_0 : vec2<f32> = uv_3 * vec2<f32>(2.0f) - vec2<f32>(1.0f);
    s_0[i32(1)] = s_0[i32(1)] * aspect_0;
    var r_0 : vec2<f32> = s_0 * vec2<f32>((sqrt(2.0f / (1.0f + aspect_0 * aspect_0)) * intensity_0));
    var cosSquared_0 : f32 = 1.0f / (1.0f + dot(r_0, r_0));
    return cosSquared_0 * cosSquared_0;
}

fn ApplyNightShift_0( colour_4 : vec3<f32>) -> vec3<f32>
{
    var amount_1 : f32 = resolve_0.grade_0.w;
    if(amount_1 <= 0.0f)
    {
        return colour_4;
    }
    const photopic_0 : vec3<f32> = vec3<f32>(0.2125999927520752f, 0.71520000696182251f, 0.07220000028610229f);
    const nightTint_0 : vec3<f32> = vec3<f32>(0.86000001430511475f, 0.93000000715255737f, 1.0f);
    return mix(colour_4, vec3<f32>(dot(colour_4, vec3<f32>(0.02999999932944775f, 0.56000000238418579f, 0.40999999642372131f))) * nightTint_0 / vec3<f32>(dot(nightTint_0, photopic_0)), vec3<f32>((amount_1 * (1.0f - smoothstep(0.01999999955296516f, 0.44999998807907104f, dot(colour_4, photopic_0))) * 0.34999999403953552f)));
}

fn FilmSaturation_0( c_2 : vec3<f32>) -> f32
{
    var _S11 : f32 = c_2.x;
    var _S12 : f32 = c_2.y;
    var _S13 : f32 = c_2.z;
    var _S14 : f32 = max(_S11, max(_S12, _S13));
    return (max(_S14, 1.00000001335143196e-10f) - max(min(_S11, min(_S12, _S13)), 1.00000001335143196e-10f)) / max(_S14, 0.00999999977648258f);
}

fn FilmYc_0( c_3 : vec3<f32>) -> f32
{
    var _S15 : f32 = c_3.z;
    var _S16 : f32 = c_3.y;
    var _S17 : f32 = c_3.x;
    return (_S17 + _S16 + _S15 + 1.75f * sqrt(max(_S15 * (_S15 - _S16) + _S16 * (_S16 - _S17) + _S17 * (_S17 - _S15), 0.0f))) / 3.0f;
}

fn FilmShaper_0( x_0 : f32) -> f32
{
    var _S18 : f32 = max(1.0f - abs(x_0 * 0.5f), 0.0f);
    return 0.5f * (1.0f + f32((i32(sign((x_0))))) * (1.0f - _S18 * _S18));
}

fn FilmGlowAmount_0( yc_0 : f32,  gain_0 : f32,  mid_0 : f32) -> f32
{
    if(yc_0 <= (0.66666668653488159f * mid_0))
    {
        return gain_0;
    }
    if(yc_0 >= (2.0f * mid_0))
    {
        return 0.0f;
    }
    return gain_0 * (mid_0 / yc_0 - 0.5f);
}

fn FilmHueDegrees_0( c_4 : vec3<f32>) -> f32
{
    var _S19 : f32 = c_4.x;
    var _S20 : f32 = c_4.y;
    var _S21 : bool;
    if(_S19 == _S20)
    {
        _S21 = _S20 == (c_4.z);
    }
    else
    {
        _S21 = false;
    }
    if(_S21)
    {
        return 0.0f;
    }
    var _S22 : f32 = c_4.z;
    var h_0 : f32 = degrees(atan2(sqrt(3.0f) * (_S20 - _S22), 2.0f * _S19 - _S20 - _S22));
    var h_1 : f32;
    if(h_0 < 0.0f)
    {
        h_1 = h_0 + 360.0f;
    }
    else
    {
        h_1 = h_0;
    }
    return clamp(h_1, 0.0f, 360.0f);
}

fn FilmLog10_0( x_1 : f32) -> f32
{
    return log(x_1) * 0.43429449200630188f;
}

fn FilmChannel_0( v_0 : f32) -> f32
{
    var _S23 : f32 = FilmLog10_0(0.18000000715255737f) - 0.5f * log(0.25000002980232239f) * 0.51136362552642822f;
    var straightMatch_0 : f32 = 0.51136362552642822f - _S23;
    var shoulderMatch_0 : f32 = 0.29545453190803528f - straightMatch_0;
    var l_0 : f32 = FilmLog10_0(max(v_0, 1.00000001335143196e-10f));
    var straight_0 : f32 = 0.87999999523162842f * (l_0 + straightMatch_0);
    var toe_0 : f32;
    if(l_0 < _S23)
    {
        toe_0 = 0.89999997615814209f / (1.0f + exp(-3.91111111640930176f * (l_0 - _S23)));
    }
    else
    {
        toe_0 = straight_0;
    }
    var shoulder_0 : f32;
    if(l_0 > shoulderMatch_0)
    {
        shoulder_0 = 1.03999996185302734f - 1.55999994277954102f / (1.0f + exp(2.25641036033630371f * (l_0 - shoulderMatch_0)));
    }
    else
    {
        shoulder_0 = straight_0;
    }
    var t_0 : f32 = saturate((l_0 - _S23) / (shoulderMatch_0 - _S23));
    var t_1 : f32;
    if(shoulderMatch_0 < _S23)
    {
        t_1 = 1.0f - t_0;
    }
    else
    {
        t_1 = t_0;
    }
    return mix(toe_0, shoulder_0, t_1 * t_1 * (3.0f - 2.0f * t_1));
}

fn FilmCurve_0( working_0 : vec3<f32>) -> vec3<f32>
{
    var _S24 : vec3<f32> = (((working_0) * (mat3x3<f32>(0.69545221328735352f, 0.14067870378494263f, 0.16386906802654266f, 0.04479456320405006f, 0.85967111587524414f, 0.0955343171954155f, -0.0055258828215301f, 0.00402521016076207f, 1.00150072574615479f))));
    var saturation_0 : f32 = FilmSaturation_0(_S24);
    var _S25 : vec3<f32> = _S24 * vec3<f32>((1.0f + FilmGlowAmount_0(FilmYc_0(_S24), 0.05000000074505806f * FilmShaper_0((saturation_0 - 0.40000000596046448f) / 0.20000000298023224f), 0.07999999821186066f)));
    var c_5 : vec3<f32> = _S25;
    var centred_0 : f32 = FilmHueDegrees_0(_S25);
    var centred_1 : f32;
    if(centred_0 < -180.0f)
    {
        centred_1 = centred_0 + 360.0f;
    }
    else
    {
        if(centred_0 > 180.0f)
        {
            centred_1 = centred_0 - 360.0f;
        }
        else
        {
            centred_1 = centred_0;
        }
    }
    var weight_0 : f32 = smoothstep(0.0f, 1.0f, 1.0f - abs(2.0f * centred_1 / 135.0f));
    c_5[i32(0)] = c_5[i32(0)] + weight_0 * weight_0 * saturation_0 * (0.02999999932944775f - c_5.x) * 0.18000000715255737f;
    const _S26 : vec3<f32> = vec3<f32>(0.0f, 0.0f, 0.0f);
    var w_0 : vec3<f32> = max((((c_5) * (mat3x3<f32>(1.4514392614364624f, -0.2365107536315918f, -0.21492856740951538f, -0.07655376940965652f, 1.17622971534729004f, -0.09967592358589172f, 0.00831614807248116f, -0.00603245012462139f, 0.99771630764007568f)))), _S26);
    var w_1 : vec3<f32> = mix(vec3<f32>(dot(w_0, vec3<f32>(0.27222871780395508f, 0.67408174276351929f, 0.05368951708078384f))), w_0, vec3<f32>(0.95999997854232788f));
    var tone_0 : vec3<f32> = vec3<f32>(FilmChannel_0(w_1.x), FilmChannel_0(w_1.y), FilmChannel_0(w_1.z));
    return max(mix(vec3<f32>(dot(tone_0, vec3<f32>(0.27222871780395508f, 0.67408174276351929f, 0.05368951708078384f))), tone_0, vec3<f32>(0.93000000715255737f)), _S26);
}

fn ToneMapScene_0( linearColour_0 : vec3<f32>) -> vec3<f32>
{
    var a_0 : vec3<f32> = (((linearColour_0) * (mat3x3<f32>(0.6130974292755127f, 0.33952313661575317f, 0.04737945273518562f, 0.07019372284412384f, 0.91635388135910034f, 0.01345239859074354f, 0.0206155925989151f, 0.10956977307796478f, 0.86981463432312012f))));
    var luma_0 : f32 = dot(a_0, vec3<f32>(0.27222871780395508f, 0.67408174276351929f, 0.05368951708078384f));
    var fromGrey_0 : vec3<f32> = a_0 / vec3<f32>(max(luma_0, 1.00000001335143196e-10f)) - vec3<f32>(1.0f, 1.0f, 1.0f);
    const _S27 : vec3<f32> = vec3<f32>(0.0f, 0.0f, 0.0f);
    var a_1 : vec3<f32> = max(mix(a_0, (((a_0) * (mat3x3<f32>(1.37041234970092773f, -0.3292921781539917f, -0.06368312239646912f, -0.08343349397182465f, 1.09709274768829346f, -0.01086138002574444f, -0.02579332143068314f, -0.09862580150365829f, 1.20369493961334229f)))), vec3<f32>(((1.0f - exp2(-4.0f * dot(fromGrey_0, fromGrey_0))) * (1.0f - exp2(-4.0f * luma_0 * luma_0))))), _S27);
    var _S28 : vec3<f32> = vec3<f32>(0.60000002384185791f);
    var a_2 : vec3<f32> = FilmCurve_0(mix(a_1, (((a_1) * (mat3x3<f32>(0.93863940238952637f, 0.0f, 0.06136062368750572f, 0.0f, 0.83079415559768677f, 0.16920587420463562f, 0.0f, 0.0f, 1.0f)))), _S28));
    return max((((mix(a_2, (((a_2) * (mat3x3<f32>(1.06537485122680664f, 1.44700004511832958e-06f, -0.06537100672721863f, -3.46000007311886293e-07f, 1.20366346836090088f, -0.20366771519184113f, 1.99999998784505806e-08f, 2.09999999611909516e-08f, 0.99999958276748657f)))), _S28)) * (mat3x3<f32>(1.70505094528198242f, -0.62179213762283325f, -0.08325887471437454f, -0.13025641441345215f, 1.14080476760864258f, -0.01054831873625517f, -0.02400335669517517f, -0.1289689689874649f, 1.15297234058380127f)))), _S27);
}

fn LinearToSrgbDisplay_0( c_6 : vec3<f32>) -> vec3<f32>
{
    var low_0 : vec3<f32> = c_6 * vec3<f32>(12.92000007629394531f);
    var high_0 : vec3<f32> = vec3<f32>(1.0549999475479126f) * pow(max(c_6, vec3<f32>(0.0f, 0.0f, 0.0f)), vec3<f32>(0.4166666567325592f, 0.4166666567325592f, 0.4166666567325592f)) - vec3<f32>(0.05499999970197678f);
    var _S29 : f32;
    if((c_6.x) < 0.00313066993840039f)
    {
        _S29 = low_0.x;
    }
    else
    {
        _S29 = high_0.x;
    }
    var _S30 : f32;
    if((c_6.y) < 0.00313066993840039f)
    {
        _S30 = low_0.y;
    }
    else
    {
        _S30 = high_0.y;
    }
    var _S31 : f32;
    if((c_6.z) < 0.00313066993840039f)
    {
        _S31 = low_0.z;
    }
    else
    {
        _S31 = high_0.z;
    }
    return vec3<f32>(_S29, _S30, _S31);
}

fn ResolveDisplay_0( input_0 : FullscreenVaryings_0) -> vec4<f32>
{
    if((resolve_0.params_0.w) > 0.5f)
    {
        return vec4<f32>(EncodeSrgb_0(clamp((textureSample((uScene_0), (uSceneSampler_0), (input_0.uv_0))).xyz, vec3<f32>(0.0f), vec3<f32>(1.0f))), 1.0f);
    }
    var colour_5 : vec3<f32> = SceneColour_0(input_0.uv_0) * vec3<f32>(resolve_0.params_0.x);
    var op_0 : i32 = i32(resolve_0.params_0.y + 0.5f);
    if(op_0 == i32(1))
    {
        var _S32 : vec3<f32> = vec3<f32>(0.0f);
        var _S33 : vec3<f32> = vec3<f32>(1.0f);
        return vec4<f32>(clamp(ApplyGrain_0(ApplyGrade_0(ApplyLut_0(EncodeSrgb_0(clamp(Reinhard_0(colour_5, resolve_0.params_0.z), _S32, _S33)))), input_0.uv_0), _S32, _S33), 1.0f);
    }
    if(op_0 >= i32(2))
    {
        var vignette_1 : f32 = VignetteMask_0(input_0.uv_0);
        var colour_6 : vec3<f32>;
        if((resolve_0.vignette_0.z) > 0.5f)
        {
            var bloom_0 : vec4<f32> = (textureSample((uBloom_0), (uBloomSampler_0), (input_0.uv_0)));
            colour_6 = colour_5 * vec3<f32>(saturate(bloom_0.w)) + max(bloom_0.xyz, vec3<f32>(0.0f, 0.0f, 0.0f)) * vec3<f32>(resolve_0.params_0.x);
        }
        else
        {
            colour_6 = colour_5;
        }
        return vec4<f32>(clamp(ApplyGrain_0(ApplyGrade_0(ApplyLut_0(LinearToSrgbDisplay_0(ToneMapScene_0(ApplyNightShift_0(colour_6) * vec3<f32>(vignette_1))))), input_0.uv_0), vec3<f32>(0.0f), vec3<f32>(1.0f)), 1.0f);
    }
    var _S34 : vec3<f32> = vec3<f32>(0.0f);
    var _S35 : vec3<f32> = vec3<f32>(1.0f);
    return vec4<f32>(clamp(ApplyGrain_0(ApplyGrade_0(ApplyLut_0(EncodeSrgb_0(clamp(colour_5, _S34, _S35)))), input_0.uv_0), _S34, _S35), 1.0f);
}

fn DecodeSrgb_0( c_7 : vec3<f32>) -> vec3<f32>
{
    return mix(pow(max((c_7 + vec3<f32>(0.05499999970197678f)) / vec3<f32>(1.0549999475479126f), vec3<f32>(0.0f, 0.0f, 0.0f)), vec3<f32>(2.40000009536743164f, 2.40000009536743164f, 2.40000009536743164f)), c_7 / vec3<f32>(12.92000007629394531f), step(c_7, vec3<f32>(0.04044999927282333f, 0.04044999927282333f, 0.04044999927282333f)));
}

fn ToTarget_0( display_0 : vec4<f32>) -> vec4<f32>
{
    var _S36 : vec4<f32>;
    if(cTargetEncodes_0 != i32(0))
    {
        _S36 = vec4<f32>(DecodeSrgb_0(display_0.xyz), display_0.w);
    }
    else
    {
        _S36 = display_0;
    }
    return _S36;
}

struct pixelOutput_0
{
    @location(0) output_1 : vec4<f32>,
};

struct pixelInput_0
{
    @location(0) uv_4 : vec2<f32>,
};

@fragment
fn fragmentMain( _S37 : pixelInput_0, @builtin(position) position_1 : vec4<f32>) -> pixelOutput_0
{
    var _S38 : FullscreenVaryings_0 = FullscreenVaryings_0( position_1, _S37.uv_4 );
    var _S39 : pixelOutput_0 = pixelOutput_0( ToTarget_0(ResolveDisplay_0(_S38)) );
    return _S39;
}

