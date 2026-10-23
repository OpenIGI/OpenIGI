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
    var intensity_0 : f32 = saturate(p_0.visor_0.x);
    if(intensity_0 <= 0.0f)
    {
        var _S6 : pixelOutput_0 = pixelOutput_0( Layer_0(_S5.uv_2) );
        return _S6;
    }
    var _S7 : vec2<f32> = vec2<f32>(2.0f);
    var _S8 : vec2<f32> = vec2<f32>(1.0f);
    var centred_0 : vec2<f32> = (_S5.uv_2 - p_0.shake_0.xy * p_0.frame_0.zw * vec2<f32>(intensity_0)) * _S7 - _S8;
    var bend_0 : f32 = dot(centred_0, centred_0);
    var centred_1 : vec2<f32> = centred_0 * vec2<f32>((1.0f + p_0.visor_0.y * intensity_0 * bend_0));
    var _S9 : vec2<f32> = vec2<f32>(0.5f);
    var uv_3 : vec2<f32> = centred_1 * _S9 + _S9;
    var fringe_0 : vec2<f32> = centred_1 * vec2<f32>((p_0.visor_0.z * intensity_0 * bend_0 * 0.5f)) * p_0.frame_0.zw;
    var red_0 : vec4<f32> = Layer_0(uv_3 + fringe_0);
    var green_0 : vec4<f32> = Layer_0(uv_3);
    var blue_0 : vec4<f32> = Layer_0(uv_3 - fringe_0);
    var _S10 : f32 = red_0.x;
    var _S11 : f32 = green_0.y;
    var _S12 : f32 = blue_0.z;
    var colour_0 : vec4<f32> = vec4<f32>(_S10, _S11, _S12, max(green_0.w, max(red_0.w, blue_0.w)));
    var bloom_0 : vec3<f32> = max(((textureSampleLevel((uGlow0_0), (uHudSampler_0), (uv_3), (0.0f))) * vec4<f32>(0.5f) + (textureSampleLevel((uGlow1_0), (uHudSampler_0), (uv_3), (0.0f))) * vec4<f32>(0.30000001192092896f) + (textureSampleLevel((uGlow2_0), (uHudSampler_0), (uv_3), (0.0f))) * vec4<f32>(0.20000000298023224f)).xyz - vec3<f32>(0.44999998807907104f), vec3<f32>(0.0f)) * vec3<f32>(1.81818175315856934f) * vec3<f32>((p_0.visor_0.w * intensity_0));
    var _S13 : vec3<f32> = vec3<f32>(_S10, _S11, _S12) + bloom_0;
    colour_0.x = _S13.x;
    colour_0.y = _S13.y;
    colour_0.z = _S13.z;
    colour_0[i32(3)] = saturate(colour_0.w + dot(bloom_0, vec3<f32>(0.29899999499320984f, 0.58700001239776611f, 0.11400000005960464f)) * 0.5f);
    var _S14 : f32 = uv_3.y;
    var y_1 : f32 = _S14 * p_0.frame_0.y;
    var _S15 : vec3<f32> = colour_0.xyz * vec3<f32>((1.0f - p_0.lines_0.y * intensity_0 * (0.5f - 0.5f * cos(y_1 * (p_0.lines_0.x / 1080.0f) * 6.28318548202514648f)) + 0.05999999865889549f * intensity_0 * exp(- pow((fract(_S14 - p_0.lines_0.w * 0.07999999821186066f) - 0.5f) * 9.0f, 2.0f))));
    colour_0.x = _S15.x;
    colour_0.y = _S15.y;
    colour_0.z = _S15.z;
    var appear_0 : f32 = saturate(p_0.lines_0.z);
    if(appear_0 < 1.0f)
    {
        var row_0 : f32 = floor(y_1 / 6.0f);
        colour_0 = colour_0 * vec4<f32>((step(Hash_0(row_0 * 1.37000000476837158f), appear_0) * mix(0.34999999403953552f, 1.0f, step(0.5f - appear_0 * 0.5f, Hash_0(row_0 + floor(p_0.lines_0.w * 30.0f))))));
    }
    var wash_0 : f32 = saturate(p_0.damage_0.x * saturate(1.0f - length(_S5.uv_2) * 1.39999997615814209f) + p_0.damage_0.y * saturate(1.0f - length(_S5.uv_2 - vec2<f32>(1.0f, 0.0f)) * 1.39999997615814209f) + p_0.damage_0.z * saturate(1.0f - length(_S5.uv_2 - vec2<f32>(0.0f, 1.0f)) * 1.39999997615814209f) + p_0.damage_0.w * saturate(1.0f - length(_S5.uv_2 - vec2<f32>(1.0f, 1.0f)) * 1.39999997615814209f)) * intensity_0;
    var _S16 : vec3<f32> = vec3<f32>(wash_0);
    var _S17 : vec3<f32> = mix(colour_0.xyz, p_0.damageColour_0.xyz * _S16, vec3<f32>((wash_0 * 0.60000002384185791f))) + p_0.damageColour_0.xyz * _S16 * vec3<f32>(0.40000000596046448f);
    colour_0.x = _S17.x;
    colour_0.y = _S17.y;
    colour_0.z = _S17.z;
    colour_0[i32(3)] = saturate(max(colour_0.w, wash_0 * 0.64999997615814209f));
    var threat_0 : f32 = saturate(p_0.alert_0.x) * intensity_0;
    if(threat_0 > 0.0f)
    {
        var edge_0 : vec2<f32> = abs(_S5.uv_2 * _S7 - _S8);
        var tint_0 : f32 = smoothstep(1.0f - 0.34999999403953552f * threat_0, 1.0f, max(edge_0.x, edge_0.y * 1.14999997615814209f)) * threat_0 * 0.5f;
        var _S18 : vec3<f32> = colour_0.xyz + p_0.alert_0.yzw * vec3<f32>(tint_0);
        colour_0.x = _S18.x;
        colour_0.y = _S18.y;
        colour_0.z = _S18.z;
        colour_0[i32(3)] = saturate(max(colour_0.w, tint_0));
    }
    var _S19 : pixelOutput_0 = pixelOutput_0( mix(Layer_0(_S5.uv_2), colour_0, vec4<f32>(intensity_0)) );
    return _S19;
}

