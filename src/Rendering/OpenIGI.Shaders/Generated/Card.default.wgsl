struct _MatrixStorage_float4x4_ColMajorstd140_0
{
    @align(16) data_0 : array<vec4<f32>, i32(4)>,
};

struct CardPush_std140_0
{
    @align(16) modelViewProjection_0 : _MatrixStorage_float4x4_ColMajorstd140_0,
    @align(16) normal0_0 : vec4<f32>,
    @align(16) normal1_0 : vec4<f32>,
    @align(16) normal2_0 : vec4<f32>,
    @align(16) material_0 : vec4<f32>,
};

@binding(0) @group(3) var<uniform> push_0 : CardPush_std140_0;
@binding(0) @group(0) var uDiffuse_0 : texture_2d<f32>;

@binding(1) @group(0) var uDiffuseSampler_0 : sampler;

@id(66) override cSamplerDecodes_0 : i32 = i32(0);

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

struct Varyings_0
{
    @builtin(position) position_0 : vec4<f32>,
    @location(0) diffuseUv_0 : vec2<f32>,
    @location(1) color_0 : vec4<f32>,
    @location(2) modelNormal_0 : vec3<f32>,
};

struct vertexInput_0
{
    @location(0) position_1 : vec3<f32>,
    @location(1) normal_0 : vec3<f32>,
    @location(2) diffuseUv_1 : vec2<f32>,
    @location(3) lightmapUv_0 : vec2<f32>,
    @location(4) colorBgra_0 : vec4<f32>,
};

@vertex
fn vertexMain( _S2 : vertexInput_0) -> Varyings_0
{
    var _S3 : vec4<f32> = (((vec4<f32>(_S2.position_1, 1.0f)) * (mat4x4<f32>(push_0.modelViewProjection_0.data_0[i32(0)][i32(0)], push_0.modelViewProjection_0.data_0[i32(1)][i32(0)], push_0.modelViewProjection_0.data_0[i32(2)][i32(0)], push_0.modelViewProjection_0.data_0[i32(3)][i32(0)], push_0.modelViewProjection_0.data_0[i32(0)][i32(1)], push_0.modelViewProjection_0.data_0[i32(1)][i32(1)], push_0.modelViewProjection_0.data_0[i32(2)][i32(1)], push_0.modelViewProjection_0.data_0[i32(3)][i32(1)], push_0.modelViewProjection_0.data_0[i32(0)][i32(2)], push_0.modelViewProjection_0.data_0[i32(1)][i32(2)], push_0.modelViewProjection_0.data_0[i32(2)][i32(2)], push_0.modelViewProjection_0.data_0[i32(3)][i32(2)], push_0.modelViewProjection_0.data_0[i32(0)][i32(3)], push_0.modelViewProjection_0.data_0[i32(1)][i32(3)], push_0.modelViewProjection_0.data_0[i32(2)][i32(3)], push_0.modelViewProjection_0.data_0[i32(3)][i32(3)]))));
    var clip_0 : vec4<f32> = _S3;
    clip_0[i32(1)] = OpenIgiClipY_0(_S3.y);
    clip_0[i32(2)] = (clip_0.z + clip_0.w) * 0.5f;
    var output_0 : Varyings_0;
    output_0.position_0 = clip_0;
    output_0.modelNormal_0 = _S2.normal_0;
    output_0.diffuseUv_0 = _S2.diffuseUv_1;
    output_0.color_0 = _S2.colorBgra_0.zyxw;
    return output_0;
}

fn DecodeSrgb_0( c_0 : vec3<f32>) -> vec3<f32>
{
    return mix(pow(max((c_0 + vec3<f32>(0.05499999970197678f)) / vec3<f32>(1.0549999475479126f), vec3<f32>(0.0f, 0.0f, 0.0f)), vec3<f32>(2.40000009536743164f, 2.40000009536743164f, 2.40000009536743164f)), c_0 / vec3<f32>(12.92000007629394531f), step(c_0, vec3<f32>(0.04044999927282333f, 0.04044999927282333f, 0.04044999927282333f)));
}

fn SampledLinear_0( sampled_0 : vec3<f32>) -> vec3<f32>
{
    var _S4 : vec3<f32>;
    if(cSamplerDecodes_0 != i32(0))
    {
        _S4 = sampled_0;
    }
    else
    {
        _S4 = DecodeSrgb_0(sampled_0);
    }
    return _S4;
}

struct CardTargets_0
{
    @location(0) albedo_0 : vec4<f32>,
    @location(1) normal_1 : vec4<f32>,
    @location(2) material_1 : vec4<f32>,
    @location(3) depth_0 : vec4<f32>,
};

struct pixelInput_0
{
    @location(0) diffuseUv_2 : vec2<f32>,
    @location(1) color_1 : vec4<f32>,
    @location(2) modelNormal_1 : vec3<f32>,
};

@fragment
fn fragmentMain( _S5 : pixelInput_0, @builtin(position) position_2 : vec4<f32>) -> CardTargets_0
{
    var diffuse_0 : vec4<f32> = (textureSample((uDiffuse_0), (uDiffuseSampler_0), (_S5.diffuseUv_2)));
    if((diffuse_0.w) < 0.5f)
    {
        discard;
    }
    var output_1 : CardTargets_0;
    output_1.albedo_0 = vec4<f32>(SampledLinear_0(diffuse_0.xyz) * DecodeSrgb_0(_S5.color_1.xyz), 1.0f);
    var _S6 : vec3<f32> = vec3<f32>(0.5f);
    output_1.normal_1 = vec4<f32>(normalize(_S5.modelNormal_1) * _S6 + _S6, 1.0f);
    output_1.material_1 = vec4<f32>(push_0.material_0.x, push_0.material_0.y, push_0.material_0.z, 1.0f);
    output_1.depth_0 = vec4<f32>(position_2.z, 0.0f, 0.0f, 1.0f);
    return output_1;
}

