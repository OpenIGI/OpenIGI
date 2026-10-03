struct _MatrixStorage_float4x4_ColMajorstd140_0
{
    @align(16) data_0 : array<vec4<f32>, i32(4)>,
};

struct OutlinePush_std140_0
{
    @align(16) modelViewProjection_0 : _MatrixStorage_float4x4_ColMajorstd140_0,
    @align(16) colour_0 : vec4<f32>,
    @align(16) control_0 : vec4<f32>,
    @align(16) viewport_0 : vec4<f32>,
};

@binding(0) @group(3) var<uniform> push_0 : OutlinePush_std140_0;
fn rsqrt_0( x_0 : f32) -> f32
{
    return 1.0f / sqrt(x_0);
}

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

struct vertexOutput_0
{
    @builtin(position) output_0 : vec4<f32>,
};

struct vertexInput_0
{
    @location(0) position_0 : vec3<f32>,
    @location(1) normal_0 : vec3<f32>,
    @location(5) previousPosition_0 : vec3<f32>,
    @location(6) previousNormal_0 : vec3<f32>,
};

@vertex
fn vertexMain( _S2 : vertexInput_0) -> vertexOutput_0
{
    var _S3 : vec3<f32> = vec3<f32>(push_0.control_0.y);
    var localNormal_0 : vec3<f32> = _S2.normal_0 + _S3 * (_S2.previousNormal_0 - _S2.normal_0);
    var _S4 : vec4<f32> = (((vec4<f32>(_S2.position_0 + _S3 * (_S2.previousPosition_0 - _S2.position_0), 1.0f)) * (mat4x4<f32>(push_0.modelViewProjection_0.data_0[i32(0)][i32(0)], push_0.modelViewProjection_0.data_0[i32(1)][i32(0)], push_0.modelViewProjection_0.data_0[i32(2)][i32(0)], push_0.modelViewProjection_0.data_0[i32(3)][i32(0)], push_0.modelViewProjection_0.data_0[i32(0)][i32(1)], push_0.modelViewProjection_0.data_0[i32(1)][i32(1)], push_0.modelViewProjection_0.data_0[i32(2)][i32(1)], push_0.modelViewProjection_0.data_0[i32(3)][i32(1)], push_0.modelViewProjection_0.data_0[i32(0)][i32(2)], push_0.modelViewProjection_0.data_0[i32(1)][i32(2)], push_0.modelViewProjection_0.data_0[i32(2)][i32(2)], push_0.modelViewProjection_0.data_0[i32(3)][i32(2)], push_0.modelViewProjection_0.data_0[i32(0)][i32(3)], push_0.modelViewProjection_0.data_0[i32(1)][i32(3)], push_0.modelViewProjection_0.data_0[i32(2)][i32(3)], push_0.modelViewProjection_0.data_0[i32(3)][i32(3)]))));
    var clip_0 : vec4<f32> = _S4;
    clip_0[i32(1)] = OpenIgiClipY_0(_S4.y);
    clip_0[i32(2)] = (clip_0.w - clip_0.z) * 0.5f;
    var clipNormal_0 : vec3<f32> = (((vec4<f32>(localNormal_0, 0.0f)) * (mat4x4<f32>(push_0.modelViewProjection_0.data_0[i32(0)][i32(0)], push_0.modelViewProjection_0.data_0[i32(1)][i32(0)], push_0.modelViewProjection_0.data_0[i32(2)][i32(0)], push_0.modelViewProjection_0.data_0[i32(3)][i32(0)], push_0.modelViewProjection_0.data_0[i32(0)][i32(1)], push_0.modelViewProjection_0.data_0[i32(1)][i32(1)], push_0.modelViewProjection_0.data_0[i32(2)][i32(1)], push_0.modelViewProjection_0.data_0[i32(3)][i32(1)], push_0.modelViewProjection_0.data_0[i32(0)][i32(2)], push_0.modelViewProjection_0.data_0[i32(1)][i32(2)], push_0.modelViewProjection_0.data_0[i32(2)][i32(2)], push_0.modelViewProjection_0.data_0[i32(3)][i32(2)], push_0.modelViewProjection_0.data_0[i32(0)][i32(3)], push_0.modelViewProjection_0.data_0[i32(1)][i32(3)], push_0.modelViewProjection_0.data_0[i32(2)][i32(3)], push_0.modelViewProjection_0.data_0[i32(3)][i32(3)])))).xyz;
    var direction_0 : vec2<f32> = vec2<f32>(clipNormal_0.x, - clipNormal_0.y);
    var lengthSquared_0 : f32 = dot(direction_0, direction_0);
    var direction_1 : vec2<f32>;
    if(lengthSquared_0 > 9.999999960041972e-13f)
    {
        direction_1 = direction_0 * vec2<f32>(rsqrt_0(lengthSquared_0));
    }
    else
    {
        direction_1 = vec2<f32>(0.0f, 0.0f);
    }
    var _S5 : vec2<f32> = clip_0.xy + direction_1 * vec2<f32>(push_0.control_0.x) * vec2<f32>(2.0f) / push_0.viewport_0.xy * vec2<f32>(clip_0.w);
    clip_0.x = _S5.x;
    clip_0.y = _S5.y;
    clip_0[i32(2)] = mix(clip_0.z, push_0.control_0.w * clip_0.w, push_0.control_0.z);
    var _S6 : vertexOutput_0 = vertexOutput_0( clip_0 );
    return _S6;
}

struct pixelOutput_0
{
    @location(0) output_1 : vec4<f32>,
};

@fragment
fn fragmentMain() -> pixelOutput_0
{
    var _S7 : pixelOutput_0 = pixelOutput_0( push_0.colour_0 );
    return _S7;
}

