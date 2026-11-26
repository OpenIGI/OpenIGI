struct _MatrixStorage_float4x4_ColMajorstd140_0
{
    @align(16) data_0 : array<vec4<f32>, i32(4)>,
};

struct ThermalMaskPush_std140_0
{
    @align(16) modelViewProjection_0 : _MatrixStorage_float4x4_ColMajorstd140_0,
    @align(16) depthRow_0 : vec4<f32>,
    @align(16) control_0 : vec4<f32>,
};

@binding(0) @group(3) var<uniform> push_0 : ThermalMaskPush_std140_0;
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

struct ThermalMaskVaryings_0
{
    @builtin(position) position_0 : vec4<f32>,
    @location(0) surface_0 : vec2<f32>,
};

struct vertexInput_0
{
    @location(0) position_1 : vec3<f32>,
    @location(1) normal_0 : vec3<f32>,
    @location(5) previousPosition_0 : vec3<f32>,
    @location(6) previousNormal_0 : vec3<f32>,
};

@vertex
fn vertexMain( _S2 : vertexInput_0) -> ThermalMaskVaryings_0
{
    var _S3 : vec3<f32> = vec3<f32>(push_0.control_0.x);
    var localPosition_0 : vec3<f32> = _S2.position_1 + _S3 * (_S2.previousPosition_0 - _S2.position_1);
    var localNormal_0 : vec3<f32> = _S2.normal_0 + _S3 * (_S2.previousNormal_0 - _S2.normal_0);
    var _S4 : f32 = length(push_0.depthRow_0.xyz) * length(localNormal_0);
    var facing_0 : f32;
    if(_S4 > 9.999999960041972e-13f)
    {
        facing_0 = abs(dot(push_0.depthRow_0.xyz, localNormal_0)) / _S4;
    }
    else
    {
        facing_0 = 1.0f;
    }
    var output_0 : ThermalMaskVaryings_0;
    output_0.surface_0 = vec2<f32>(facing_0, dot(push_0.depthRow_0.xyz, localPosition_0) + push_0.depthRow_0.w);
    var _S5 : vec4<f32> = (((vec4<f32>(localPosition_0, 1.0f)) * (mat4x4<f32>(push_0.modelViewProjection_0.data_0[i32(0)][i32(0)], push_0.modelViewProjection_0.data_0[i32(1)][i32(0)], push_0.modelViewProjection_0.data_0[i32(2)][i32(0)], push_0.modelViewProjection_0.data_0[i32(3)][i32(0)], push_0.modelViewProjection_0.data_0[i32(0)][i32(1)], push_0.modelViewProjection_0.data_0[i32(1)][i32(1)], push_0.modelViewProjection_0.data_0[i32(2)][i32(1)], push_0.modelViewProjection_0.data_0[i32(3)][i32(1)], push_0.modelViewProjection_0.data_0[i32(0)][i32(2)], push_0.modelViewProjection_0.data_0[i32(1)][i32(2)], push_0.modelViewProjection_0.data_0[i32(2)][i32(2)], push_0.modelViewProjection_0.data_0[i32(3)][i32(2)], push_0.modelViewProjection_0.data_0[i32(0)][i32(3)], push_0.modelViewProjection_0.data_0[i32(1)][i32(3)], push_0.modelViewProjection_0.data_0[i32(2)][i32(3)], push_0.modelViewProjection_0.data_0[i32(3)][i32(3)]))));
    var clip_0 : vec4<f32> = _S5;
    clip_0[i32(1)] = OpenIgiClipY_0(_S5.y);
    clip_0[i32(2)] = (clip_0.w - clip_0.z) * 0.5f;
    output_0.position_0 = clip_0;
    return output_0;
}

struct pixelOutput_0
{
    @location(0) output_1 : vec4<f32>,
};

struct pixelInput_0
{
    @location(0) surface_1 : vec2<f32>,
};

@fragment
fn fragmentMain( _S6 : pixelInput_0, @builtin(position) position_2 : vec4<f32>) -> pixelOutput_0
{
    var heat_0 : f32 = saturate(push_0.control_0.y);
    var _S7 : pixelOutput_0 = pixelOutput_0( vec4<f32>(heat_0 * (0.72000002861022949f + 0.2800000011920929f * sqrt(saturate(_S6.surface_1.x))), heat_0, 1.0f / (1.0f + max(_S6.surface_1.y, 0.0f)), 1.0f) );
    return _S7;
}

