struct _MatrixStorage_float4x4_ColMajorstd140_0
{
    @align(16) data_0 : array<vec4<f32>, i32(4)>,
};

struct ObjectVelocityPush_std140_0
{
    @align(16) current_0 : _MatrixStorage_float4x4_ColMajorstd140_0,
    @align(16) previousX_0 : vec4<f32>,
    @align(16) previousY_0 : vec4<f32>,
    @align(16) previousW_0 : vec4<f32>,
    @align(16) pose_0 : vec4<f32>,
};

@binding(0) @group(3) var<uniform> push_0 : ObjectVelocityPush_std140_0;
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
    @location(0) currentClip_0 : vec4<f32>,
    @location(1) previousClip_0 : vec4<f32>,
};

struct vertexInput_0
{
    @location(0) position_1 : vec3<f32>,
    @location(1) previousPosition_0 : vec3<f32>,
};

@vertex
fn vertexMain( _S2 : vertexInput_0) -> Varyings_0
{
    var poseDelta_0 : vec3<f32> = _S2.previousPosition_0 - _S2.position_1;
    var was_0 : vec4<f32> = vec4<f32>(_S2.position_1 + vec3<f32>(push_0.pose_0.y) * poseDelta_0, 1.0f);
    var _S3 : vec4<f32> = (((vec4<f32>(_S2.position_1 + vec3<f32>(push_0.pose_0.x) * poseDelta_0, 1.0f)) * (mat4x4<f32>(push_0.current_0.data_0[i32(0)][i32(0)], push_0.current_0.data_0[i32(1)][i32(0)], push_0.current_0.data_0[i32(2)][i32(0)], push_0.current_0.data_0[i32(3)][i32(0)], push_0.current_0.data_0[i32(0)][i32(1)], push_0.current_0.data_0[i32(1)][i32(1)], push_0.current_0.data_0[i32(2)][i32(1)], push_0.current_0.data_0[i32(3)][i32(1)], push_0.current_0.data_0[i32(0)][i32(2)], push_0.current_0.data_0[i32(1)][i32(2)], push_0.current_0.data_0[i32(2)][i32(2)], push_0.current_0.data_0[i32(3)][i32(2)], push_0.current_0.data_0[i32(0)][i32(3)], push_0.current_0.data_0[i32(1)][i32(3)], push_0.current_0.data_0[i32(2)][i32(3)], push_0.current_0.data_0[i32(3)][i32(3)]))));
    var clip_0 : vec4<f32> = _S3;
    var output_0 : Varyings_0;
    output_0.currentClip_0 = _S3;
    output_0.previousClip_0 = vec4<f32>(dot(push_0.previousX_0, was_0), dot(push_0.previousY_0, was_0), 0.0f, dot(push_0.previousW_0, was_0));
    clip_0[i32(1)] = OpenIgiClipY_0(_S3.y);
    clip_0[i32(2)] = (clip_0.w - clip_0.z) * 0.5f;
    output_0.position_0 = clip_0;
    return output_0;
}

fn ImageUv_0( clip_1 : vec4<f32>) -> vec2<f32>
{
    var ndc_0 : vec2<f32> = clip_1.xy / vec2<f32>(clip_1.w);
    return vec2<f32>(ndc_0.x * 0.5f + 0.5f, - ndc_0.y * 0.5f + 0.5f);
}

struct pixelOutput_0
{
    @location(0) output_1 : vec4<f32>,
};

struct pixelInput_0
{
    @location(0) currentClip_1 : vec4<f32>,
    @location(1) previousClip_1 : vec4<f32>,
};

@fragment
fn fragmentMain( _S4 : pixelInput_0, @builtin(position) position_2 : vec4<f32>) -> pixelOutput_0
{
    if((_S4.previousClip_1.w) < 0.00100000004749745f)
    {
        var _S5 : pixelOutput_0 = pixelOutput_0( vec4<f32>(0.0f, 0.0f, 0.0f, 0.0f) );
        return _S5;
    }
    var _S6 : pixelOutput_0 = pixelOutput_0( vec4<f32>(ImageUv_0(_S4.currentClip_1) - ImageUv_0(_S4.previousClip_1), 0.0f, 0.0f) );
    return _S6;
}

