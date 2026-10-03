struct _MatrixStorage_float4x4_ColMajorstd140_0
{
    @align(16) data_0 : array<vec4<f32>, i32(4)>,
};

struct RainPush_std140_0
{
    @align(16) modelViewProjection_0 : _MatrixStorage_float4x4_ColMajorstd140_0,
};

@binding(0) @group(3) var<uniform> push_0 : RainPush_std140_0;
@binding(0) @group(0) var uDiffuse_0 : texture_2d<f32>;

@binding(1) @group(0) var uDiffuseSampler_0 : sampler;

struct RainParams_std140_0
{
    @align(16) fog_0 : vec4<f32>,
    @align(16) screen_0 : vec4<f32>,
    @align(16) lens_0 : vec4<f32>,
};

@binding(2) @group(1) var<uniform> p_0 : RainParams_std140_0;
@binding(0) @group(1) var uBackdrop_0 : texture_2d<f32>;

@binding(1) @group(1) var uBackdropSampler_0 : sampler;

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

struct RainVaryings_0
{
    @builtin(position) position_0 : vec4<f32>,
    @location(0) diffuseUv_0 : vec2<f32>,
    @location(1) color_0 : vec4<f32>,
    @location(2) viewDepth_0 : f32,
};

struct vertexInput_0
{
    @location(0) position_1 : vec3<f32>,
    @location(1) normal_0 : vec3<f32>,
    @location(2) diffuseUv_1 : vec2<f32>,
    @location(3) lightmapUv_0 : vec2<f32>,
    @location(4) colorBgra_0 : vec4<f32>,
    @location(5) previousPosition_0 : vec3<f32>,
    @location(6) previousNormal_0 : vec3<f32>,
};

@vertex
fn vertexMain( _S2 : vertexInput_0) -> RainVaryings_0
{
    var output_0 : RainVaryings_0;
    output_0.diffuseUv_0 = _S2.diffuseUv_1;
    output_0.color_0 = _S2.colorBgra_0.zyxw;
    var _S3 : vec4<f32> = (((vec4<f32>(_S2.position_1, 1.0f)) * (mat4x4<f32>(push_0.modelViewProjection_0.data_0[i32(0)][i32(0)], push_0.modelViewProjection_0.data_0[i32(1)][i32(0)], push_0.modelViewProjection_0.data_0[i32(2)][i32(0)], push_0.modelViewProjection_0.data_0[i32(3)][i32(0)], push_0.modelViewProjection_0.data_0[i32(0)][i32(1)], push_0.modelViewProjection_0.data_0[i32(1)][i32(1)], push_0.modelViewProjection_0.data_0[i32(2)][i32(1)], push_0.modelViewProjection_0.data_0[i32(3)][i32(1)], push_0.modelViewProjection_0.data_0[i32(0)][i32(2)], push_0.modelViewProjection_0.data_0[i32(1)][i32(2)], push_0.modelViewProjection_0.data_0[i32(2)][i32(2)], push_0.modelViewProjection_0.data_0[i32(3)][i32(2)], push_0.modelViewProjection_0.data_0[i32(0)][i32(3)], push_0.modelViewProjection_0.data_0[i32(1)][i32(3)], push_0.modelViewProjection_0.data_0[i32(2)][i32(3)], push_0.modelViewProjection_0.data_0[i32(3)][i32(3)]))));
    var clip_0 : vec4<f32> = _S3;
    clip_0[i32(1)] = OpenIgiClipY_0(_S3.y);
    clip_0[i32(2)] = (clip_0.w - clip_0.z) * 0.5f;
    output_0.position_0 = clip_0;
    output_0.viewDepth_0 = clip_0.w;
    return output_0;
}

fn WideView_0( uv_0 : vec2<f32>,  aspect_0 : f32) -> vec3<f32>
{
    const _S4 : vec3<f32> = vec3<f32>(0.0f, 0.0f, 0.0f);
    var i_0 : i32 = i32(0);
    var sum_0 : vec3<f32> = _S4;
    for(;;)
    {
        if(i_0 < i32(8))
        {
        }
        else
        {
            break;
        }
        var angle_0 : f32 = (f32(i_0) + 0.5f) * 0.78539818525314331f;
        var sum_1 : vec3<f32> = sum_0 + (textureSampleLevel((uBackdrop_0), (uBackdropSampler_0), (clamp(uv_0 + vec2<f32>(cos(angle_0) / aspect_0, sin(angle_0)) * vec2<f32>(0.11999999731779099f) - vec2<f32>(0.0f, 0.07999999821186066f), vec2<f32>(0.0f, 0.0f), vec2<f32>(1.0f, 1.0f))), (0.0f))).xyz;
        i_0 = i_0 + i32(1);
        sum_0 = sum_1;
    }
    return sum_0 * vec3<f32>(0.125f);
}

struct pixelOutput_0
{
    @location(0) output_1 : vec4<f32>,
};

struct pixelInput_0
{
    @location(0) diffuseUv_2 : vec2<f32>,
    @location(1) color_1 : vec4<f32>,
    @location(2) viewDepth_1 : f32,
};

@fragment
fn fragmentMain( _S5 : pixelInput_0, @builtin(position) position_2 : vec4<f32>) -> pixelOutput_0
{
    var shape_0 : f32 = (textureSample((uDiffuse_0), (uDiffuseSampler_0), (_S5.diffuseUv_2))).w;
    var coverage_0 : f32 = shape_0 * _S5.color_1.w;
    if(coverage_0 < 0.0020000000949949f)
    {
        discard;
    }
    var _S6 : f32 = max(p_0.lens_0.z, 1.0f);
    var _S7 : f32 = _S5.diffuseUv_2.x;
    var across_0 : f32 = fract(_S7 * _S6) * 2.0f - 1.0f;
    var slope_0 : vec2<f32> = vec2<f32>(dpdx(_S7), dpdy(_S7));
    var _S8 : f32 = max(length(slope_0), 9.99999997475242708e-07f);
    var pixel_0 : vec2<f32> = position_2.xy;
    var rim_0 : f32 = across_0 * across_0;
    var _S9 : vec3<f32> = _S5.color_1.xyz * vec3<f32>(shape_0);
    var alpha_0 : f32 = clamp(coverage_0 * 1.39999997615814209f, 0.0f, 1.0f);
    var _S10 : pixelOutput_0 = pixelOutput_0( vec4<f32>((mix((textureSampleLevel((uBackdrop_0), (uBackdropSampler_0), (clamp((pixel_0 + slope_0 / vec2<f32>(_S8) * vec2<f32>((- across_0 * max(1.0f / _S6 / _S8 * p_0.lens_0.x, 1.0f)))) * p_0.screen_0.zw, vec2<f32>(0.0f, 0.0f), vec2<f32>(1.0f, 1.0f))), (0.0f))).xyz, WideView_0(pixel_0 * p_0.screen_0.zw, p_0.screen_0.x * p_0.screen_0.w), vec3<f32>(saturate(p_0.lens_0.y * (0.69999998807907104f + 0.60000002384185791f * rim_0)))) * vec3<f32>((1.0f + p_0.lens_0.w * rim_0 - 0.05999999865889549f * (1.0f - rim_0))) + _S9 + _S9 * vec3<f32>((exp(- p_0.fog_0.w * max(_S5.viewDepth_1, 0.0f)) - 1.0f))) * vec3<f32>(alpha_0), alpha_0) );
    return _S10;
}

