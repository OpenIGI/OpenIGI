struct _MatrixStorage_float4x4_ColMajorstd140_0
{
    @align(16) data_0 : array<vec4<f32>, i32(4)>,
};

struct TracerPush_std140_0
{
    @align(16) modelViewProjection_0 : _MatrixStorage_float4x4_ColMajorstd140_0,
    @align(16) style_0 : vec4<f32>,
};

@binding(0) @group(3) var<uniform> push_0 : TracerPush_std140_0;
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

struct TracerVaryings_0
{
    @builtin(position) position_0 : vec4<f32>,
    @location(0) strip_0 : vec2<f32>,
    @location(1) color_0 : vec4<f32>,
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
fn vertexMain( _S2 : vertexInput_0) -> TracerVaryings_0
{
    var output_0 : TracerVaryings_0;
    output_0.strip_0 = _S2.diffuseUv_0;
    output_0.color_0 = _S2.colorBgra_0.zyxw;
    var _S3 : vec4<f32> = (((vec4<f32>(_S2.position_1, 1.0f)) * (mat4x4<f32>(push_0.modelViewProjection_0.data_0[i32(0)][i32(0)], push_0.modelViewProjection_0.data_0[i32(1)][i32(0)], push_0.modelViewProjection_0.data_0[i32(2)][i32(0)], push_0.modelViewProjection_0.data_0[i32(3)][i32(0)], push_0.modelViewProjection_0.data_0[i32(0)][i32(1)], push_0.modelViewProjection_0.data_0[i32(1)][i32(1)], push_0.modelViewProjection_0.data_0[i32(2)][i32(1)], push_0.modelViewProjection_0.data_0[i32(3)][i32(1)], push_0.modelViewProjection_0.data_0[i32(0)][i32(2)], push_0.modelViewProjection_0.data_0[i32(1)][i32(2)], push_0.modelViewProjection_0.data_0[i32(2)][i32(2)], push_0.modelViewProjection_0.data_0[i32(3)][i32(2)], push_0.modelViewProjection_0.data_0[i32(0)][i32(3)], push_0.modelViewProjection_0.data_0[i32(1)][i32(3)], push_0.modelViewProjection_0.data_0[i32(2)][i32(3)], push_0.modelViewProjection_0.data_0[i32(3)][i32(3)]))));
    var clip_0 : vec4<f32> = _S3;
    clip_0[i32(1)] = OpenIgiClipY_0(_S3.y);
    clip_0[i32(2)] = (clip_0.w - clip_0.z) * 0.5f;
    output_0.position_0 = clip_0;
    return output_0;
}

fn DecodeSrgb_0( c_0 : vec3<f32>) -> vec3<f32>
{
    return mix(pow(max((c_0 + vec3<f32>(0.05499999970197678f)) / vec3<f32>(1.0549999475479126f), vec3<f32>(0.0f, 0.0f, 0.0f)), vec3<f32>(2.40000009536743164f, 2.40000009536743164f, 2.40000009536743164f)), c_0 / vec3<f32>(12.92000007629394531f), step(c_0, vec3<f32>(0.04044999927282333f, 0.04044999927282333f, 0.04044999927282333f)));
}

fn ToTarget_0( display_0 : vec4<f32>) -> vec4<f32>
{
    var _S4 : vec4<f32>;
    if(cTargetEncodes_0 != i32(0))
    {
        _S4 = vec4<f32>(DecodeSrgb_0(display_0.xyz), display_0.w);
    }
    else
    {
        _S4 = display_0;
    }
    return _S4;
}

fn RetailLine_0( input_0 : TracerVaryings_0) -> vec4<f32>
{
    var alpha_0 : f32 = saturate(input_0.color_0.w);
    if((push_0.style_0.w) != 0.0f)
    {
        return vec4<f32>(DecodeSrgb_0(input_0.color_0.xyz) * vec3<f32>(push_0.style_0.z), alpha_0);
    }
    return ToTarget_0(vec4<f32>(input_0.color_0.xyz, alpha_0));
}

fn EncodeSrgb_0( c_1 : vec3<f32>) -> vec3<f32>
{
    return mix(vec3<f32>(1.0549999475479126f) * pow(max(c_1, vec3<f32>(0.0f, 0.0f, 0.0f)), vec3<f32>(0.4166666567325592f, 0.4166666567325592f, 0.4166666567325592f)) - vec3<f32>(0.05499999970197678f), c_1 * vec3<f32>(12.92000007629394531f), step(c_1, vec3<f32>(0.00313080009073019f, 0.00313080009073019f, 0.00313080009073019f)));
}

fn RemasteredStreak_0( input_1 : TracerVaryings_0) -> vec4<f32>
{
    var across_0 : f32 = input_1.strip_0.y * 2.0f - 1.0f;
    var d2_0 : f32 = across_0 * across_0;
    var _S5 : f32 = - d2_0;
    var core_0 : f32 = exp(_S5 * 9.0f);
    var halo_0 : f32 = exp(_S5 * 3.0f) * (1.0f - d2_0);
    var energy_0 : f32 = saturate(input_1.color_0.w);
    var energy_1 : f32 = energy_0 * energy_0;
    var hue_0 : vec3<f32> = DecodeSrgb_0(input_1.color_0.xyz);
    var radiance_0 : vec3<f32> = (hue_0 * vec3<f32>(halo_0) * vec3<f32>(0.34999999403953552f) + mix(hue_0, vec3<f32>(1.0f, 0.95999997854232788f, 0.86000001430511475f), vec3<f32>(0.34999999403953552f)) * vec3<f32>(core_0)) * vec3<f32>(energy_1) * vec3<f32>(push_0.style_0.y);
    var marker_0 : f32 = saturate((halo_0 * 0.40000000596046448f + core_0) * energy_1);
    if((push_0.style_0.w) != 0.0f)
    {
        return vec4<f32>(radiance_0 * vec3<f32>(push_0.style_0.z), marker_0);
    }
    return ToTarget_0(vec4<f32>(EncodeSrgb_0(saturate(radiance_0)), marker_0));
}

struct pixelOutput_0
{
    @location(0) output_1 : vec4<f32>,
};

struct pixelInput_0
{
    @location(0) strip_1 : vec2<f32>,
    @location(1) color_1 : vec4<f32>,
};

@fragment
fn fragmentMain( _S6 : pixelInput_0, @builtin(position) position_2 : vec4<f32>) -> pixelOutput_0
{
    var _S7 : TracerVaryings_0 = TracerVaryings_0( position_2, _S6.strip_1, _S6.color_1 );
    var _S8 : vec4<f32>;
    if((push_0.style_0.x) < 0.5f)
    {
        _S8 = RetailLine_0(_S7);
    }
    else
    {
        _S8 = RemasteredStreak_0(_S7);
    }
    var _S9 : pixelOutput_0 = pixelOutput_0( _S8 );
    return _S9;
}

