struct _MatrixStorage_float4x4_ColMajorstd140_0
{
    @align(16) data_0 : array<vec4<f32>, i32(4)>,
};

struct ShadowPush_std140_0
{
    @align(16) modelViewProjection_0 : _MatrixStorage_float4x4_ColMajorstd140_0,
    @align(16) terrainRamp_0 : vec4<f32>,
    @align(16) terrainOffset_0 : vec4<f32>,
};

@binding(0) @group(3) var<uniform> push_0 : ShadowPush_std140_0;
@binding(0) @group(0) var uDiffuse_0 : texture_2d<f32>;

@binding(1) @group(0) var uDiffuseSampler_0 : sampler;

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
    @location(1) alpha_0 : f32,
};

struct vertexInput_0
{
    @location(0) position_1 : vec3<f32>,
    @location(1) normal_0 : vec3<f32>,
    @location(2) diffuseUv_1 : vec2<f32>,
    @location(3) lightmapUv_0 : vec2<f32>,
    @location(4) colorBgra_0 : vec4<f32>,
    @location(5) previousPosition_0 : vec3<f32>,
};

@vertex
fn vertexMain( _S2 : vertexInput_0) -> Varyings_0
{
    var output_0 : Varyings_0;
    var localPosition_0 : vec3<f32> = _S2.position_1 + vec3<f32>(push_0.terrainOffset_0.w) * (_S2.previousPosition_0 - _S2.position_1);
    var localPosition_1 : vec3<f32>;
    var diffuseUv_2 : vec2<f32>;
    if((push_0.terrainRamp_0.w) > 0.5f)
    {
        var target_0 : vec3<f32> = localPosition_0 + push_0.terrainOffset_0.xyz + _S2.normal_0;
        var morph_0 : f32 = clamp((max(abs(target_0.x), max(abs(target_0.y), abs(target_0.z))) * push_0.terrainRamp_0.x - push_0.terrainRamp_0.y) * push_0.terrainRamp_0.z, 0.0f, 1.0f);
        var diffuseUv_3 : vec2<f32> = _S2.diffuseUv_1 + _S2.lightmapUv_0 * vec2<f32>(morph_0);
        localPosition_1 = localPosition_0 + _S2.normal_0 * vec3<f32>(morph_0);
        diffuseUv_2 = diffuseUv_3;
    }
    else
    {
        localPosition_1 = localPosition_0;
        diffuseUv_2 = _S2.diffuseUv_1;
    }
    var _S3 : vec4<f32> = (((vec4<f32>(localPosition_1, 1.0f)) * (mat4x4<f32>(push_0.modelViewProjection_0.data_0[i32(0)][i32(0)], push_0.modelViewProjection_0.data_0[i32(1)][i32(0)], push_0.modelViewProjection_0.data_0[i32(2)][i32(0)], push_0.modelViewProjection_0.data_0[i32(3)][i32(0)], push_0.modelViewProjection_0.data_0[i32(0)][i32(1)], push_0.modelViewProjection_0.data_0[i32(1)][i32(1)], push_0.modelViewProjection_0.data_0[i32(2)][i32(1)], push_0.modelViewProjection_0.data_0[i32(3)][i32(1)], push_0.modelViewProjection_0.data_0[i32(0)][i32(2)], push_0.modelViewProjection_0.data_0[i32(1)][i32(2)], push_0.modelViewProjection_0.data_0[i32(2)][i32(2)], push_0.modelViewProjection_0.data_0[i32(3)][i32(2)], push_0.modelViewProjection_0.data_0[i32(0)][i32(3)], push_0.modelViewProjection_0.data_0[i32(1)][i32(3)], push_0.modelViewProjection_0.data_0[i32(2)][i32(3)], push_0.modelViewProjection_0.data_0[i32(3)][i32(3)]))));
    var clip_0 : vec4<f32> = _S3;
    clip_0[i32(1)] = OpenIgiClipY_0(_S3.y);
    clip_0[i32(2)] = clip_0.z * 0.5f + 0.5f;
    output_0.position_0 = clip_0;
    output_0.diffuseUv_0 = diffuseUv_2;
    var _S4 : f32;
    if((push_0.terrainRamp_0.w) > 0.5f)
    {
        _S4 = 1.0f;
    }
    else
    {
        _S4 = _S2.colorBgra_0.w;
    }
    output_0.alpha_0 = _S4;
    return output_0;
}

@id(0) override cAlphaReference_0 : f32 = 0.50196081399917603f;

struct pixelInput_0
{
    @location(0) diffuseUv_4 : vec2<f32>,
    @location(1) alpha_1 : f32,
};

@fragment
fn fragmentMain( _S5 : pixelInput_0, @builtin(position) position_2 : vec4<f32>)
{
    if(((textureSample((uDiffuse_0), (uDiffuseSampler_0), (_S5.diffuseUv_4))).w * _S5.alpha_1) < cAlphaReference_0)
    {
        discard;
    }
    return;
}

