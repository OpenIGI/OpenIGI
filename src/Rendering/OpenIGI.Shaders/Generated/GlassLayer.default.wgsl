struct _MatrixStorage_float4x4_ColMajorstd140_0
{
    @align(16) data_0 : array<vec4<f32>, i32(4)>,
};

struct GlassPush_std140_0
{
    @align(16) modelViewProjection_0 : _MatrixStorage_float4x4_ColMajorstd140_0,
    @align(16) normal0_0 : vec4<f32>,
    @align(16) normal1_0 : vec4<f32>,
    @align(16) normal2_0 : vec4<f32>,
    @align(16) glass_0 : vec4<f32>,
};

@binding(0) @group(3) var<uniform> push_0 : GlassPush_std140_0;
@binding(0) @group(0) var uDiffuse_0 : texture_2d<f32>;

@binding(1) @group(0) var uDiffuseSampler_0 : sampler;

@binding(0) @group(1) var uSceneDepth_0 : texture_depth_2d;

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

struct GlassVaryings_0
{
    @builtin(position) position_0 : vec4<f32>,
    @location(0) diffuseUv_0 : vec2<f32>,
    @location(1) lightmapUv_0 : vec2<f32>,
    @location(2) color_0 : vec4<f32>,
    @location(3) worldNormal_0 : vec3<f32>,
    @location(4) viewDepth_0 : f32,
    @location(5) fromEye_0 : vec3<f32>,
};

struct GlassVertexInput_0
{
     position_1 : vec3<f32>,
     normal_0 : vec3<f32>,
     diffuseUv_1 : vec2<f32>,
     lightmapUv_1 : vec2<f32>,
     colorBgra_0 : vec4<f32>,
     previousPosition_0 : vec3<f32>,
     previousNormal_0 : vec3<f32>,
};

fn GlassTransform_0( input_0 : GlassVertexInput_0) -> GlassVaryings_0
{
    var output_0 : GlassVaryings_0;
    output_0.diffuseUv_0 = input_0.diffuseUv_1;
    output_0.lightmapUv_0 = input_0.lightmapUv_1;
    output_0.color_0 = input_0.colorBgra_0.zyxw;
    var _S2 : vec4<f32> = (((vec4<f32>(input_0.position_1, 1.0f)) * (mat4x4<f32>(push_0.modelViewProjection_0.data_0[i32(0)][i32(0)], push_0.modelViewProjection_0.data_0[i32(1)][i32(0)], push_0.modelViewProjection_0.data_0[i32(2)][i32(0)], push_0.modelViewProjection_0.data_0[i32(3)][i32(0)], push_0.modelViewProjection_0.data_0[i32(0)][i32(1)], push_0.modelViewProjection_0.data_0[i32(1)][i32(1)], push_0.modelViewProjection_0.data_0[i32(2)][i32(1)], push_0.modelViewProjection_0.data_0[i32(3)][i32(1)], push_0.modelViewProjection_0.data_0[i32(0)][i32(2)], push_0.modelViewProjection_0.data_0[i32(1)][i32(2)], push_0.modelViewProjection_0.data_0[i32(2)][i32(2)], push_0.modelViewProjection_0.data_0[i32(3)][i32(2)], push_0.modelViewProjection_0.data_0[i32(0)][i32(3)], push_0.modelViewProjection_0.data_0[i32(1)][i32(3)], push_0.modelViewProjection_0.data_0[i32(2)][i32(3)], push_0.modelViewProjection_0.data_0[i32(3)][i32(3)]))));
    var clip_0 : vec4<f32> = _S2;
    clip_0[i32(1)] = OpenIgiClipY_0(_S2.y);
    clip_0[i32(2)] = (clip_0.w - clip_0.z) * 0.5f;
    output_0.position_0 = clip_0;
    output_0.viewDepth_0 = clip_0.w;
    output_0.worldNormal_0 = push_0.normal0_0.xyz * vec3<f32>(input_0.normal_0.x) + push_0.normal1_0.xyz * vec3<f32>(input_0.normal_0.y) + push_0.normal2_0.xyz * vec3<f32>(input_0.normal_0.z);
    output_0.fromEye_0 = push_0.normal0_0.xyz * vec3<f32>(input_0.position_1.x) + push_0.normal1_0.xyz * vec3<f32>(input_0.position_1.y) + push_0.normal2_0.xyz * vec3<f32>(input_0.position_1.z) + vec3<f32>(push_0.normal0_0.w, push_0.normal1_0.w, push_0.normal2_0.w);
    return output_0;
}

struct vertexInput_0
{
    @location(0) position_2 : vec3<f32>,
    @location(1) normal_1 : vec3<f32>,
    @location(2) diffuseUv_2 : vec2<f32>,
    @location(3) lightmapUv_2 : vec2<f32>,
    @location(4) colorBgra_1 : vec4<f32>,
    @location(5) previousPosition_1 : vec3<f32>,
    @location(6) previousNormal_1 : vec3<f32>,
};

@vertex
fn vertexMain( _S3 : vertexInput_0) -> GlassVaryings_0
{
    var _S4 : GlassVertexInput_0 = GlassVertexInput_0( _S3.position_2, _S3.normal_1, _S3.diffuseUv_2, _S3.lightmapUv_2, _S3.colorBgra_1, _S3.previousPosition_1, _S3.previousNormal_1 );
    return GlassTransform_0(_S4);
}

struct pixelOutput_0
{
    @location(0) output_1 : vec4<f32>,
};

struct pixelInput_0
{
    @location(0) diffuseUv_3 : vec2<f32>,
    @location(1) lightmapUv_3 : vec2<f32>,
    @location(2) color_1 : vec4<f32>,
    @location(3) worldNormal_1 : vec3<f32>,
    @location(4) viewDepth_1 : f32,
    @location(5) fromEye_1 : vec3<f32>,
};

@fragment
fn fragmentMain( _S5 : pixelInput_0, @builtin(position) position_3 : vec4<f32>) -> pixelOutput_0
{
    var alpha_0 : f32 = (textureSample((uDiffuse_0), (uDiffuseSampler_0), (_S5.diffuseUv_3))).w * _S5.color_1.w;
    var _S6 : bool;
    if(alpha_0 >= 0.5f)
    {
        _S6 = true;
    }
    else
    {
        _S6 = alpha_0 < 0.01999999955296516f;
    }
    if(_S6)
    {
        discard;
    }
    var _S7 : vec3<i32> = vec3<i32>(vec2<i32>(position_3.xy), i32(0));
    if((position_3.z) < (textureLoad((uSceneDepth_0), ((_S7)).xy, ((_S7)).z)))
    {
        discard;
    }
    var _S8 : pixelOutput_0 = pixelOutput_0( vec4<f32>(normalize(_S5.worldNormal_1), max(_S5.viewDepth_1, 0.00009999999747379f)) );
    return _S8;
}

