struct _MatrixStorage_float4x4_ColMajorstd140_0
{
    @align(16) data_0 : array<vec4<f32>, i32(4)>,
};

struct OcclusionPush_std140_0
{
    @align(16) modelViewProjection_0 : _MatrixStorage_float4x4_ColMajorstd140_0,
    @align(16) minimum_0 : vec4<f32>,
    @align(16) maximum_0 : vec4<f32>,
};

@binding(0) @group(3) var<uniform> push_0 : OcclusionPush_std140_0;
@id(64) override cClipYDown_0 : i32 = i32(1);

var<private> kCorners_0 : array<vec3<f32>, i32(8)> = array<vec3<f32>, i32(8)>( vec3<f32>(0.0f, 0.0f, 0.0f), vec3<f32>(1.0f, 0.0f, 0.0f), vec3<f32>(1.0f, 1.0f, 0.0f), vec3<f32>(0.0f, 1.0f, 0.0f), vec3<f32>(0.0f, 0.0f, 1.0f), vec3<f32>(1.0f, 0.0f, 1.0f), vec3<f32>(1.0f, 1.0f, 1.0f), vec3<f32>(0.0f, 1.0f, 1.0f) );
var<private> kIndices_0 : array<i32, i32(36)> = array<i32, i32(36)>( i32(0), i32(2), i32(1), i32(0), i32(3), i32(2), i32(4), i32(5), i32(6), i32(4), i32(6), i32(7), i32(0), i32(1), i32(5), i32(0), i32(5), i32(4), i32(1), i32(2), i32(6), i32(1), i32(6), i32(5), i32(2), i32(3), i32(7), i32(2), i32(7), i32(6), i32(3), i32(0), i32(4), i32(3), i32(4), i32(7) );
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

@vertex
fn vertexMain(@builtin(vertex_index) vertexIndex_0 : u32) -> vertexOutput_0
{
    var _S2 : vec4<f32> = (((vec4<f32>(mix(push_0.minimum_0.xyz, push_0.maximum_0.xyz, kCorners_0[kIndices_0[vertexIndex_0]]), 1.0f)) * (mat4x4<f32>(push_0.modelViewProjection_0.data_0[i32(0)][i32(0)], push_0.modelViewProjection_0.data_0[i32(1)][i32(0)], push_0.modelViewProjection_0.data_0[i32(2)][i32(0)], push_0.modelViewProjection_0.data_0[i32(3)][i32(0)], push_0.modelViewProjection_0.data_0[i32(0)][i32(1)], push_0.modelViewProjection_0.data_0[i32(1)][i32(1)], push_0.modelViewProjection_0.data_0[i32(2)][i32(1)], push_0.modelViewProjection_0.data_0[i32(3)][i32(1)], push_0.modelViewProjection_0.data_0[i32(0)][i32(2)], push_0.modelViewProjection_0.data_0[i32(1)][i32(2)], push_0.modelViewProjection_0.data_0[i32(2)][i32(2)], push_0.modelViewProjection_0.data_0[i32(3)][i32(2)], push_0.modelViewProjection_0.data_0[i32(0)][i32(3)], push_0.modelViewProjection_0.data_0[i32(1)][i32(3)], push_0.modelViewProjection_0.data_0[i32(2)][i32(3)], push_0.modelViewProjection_0.data_0[i32(3)][i32(3)]))));
    var clip_0 : vec4<f32> = _S2;
    clip_0[i32(1)] = OpenIgiClipY_0(_S2.y);
    clip_0[i32(2)] = (clip_0.w - clip_0.z) * 0.5f;
    var _S3 : vertexOutput_0 = vertexOutput_0( clip_0 );
    return _S3;
}

@fragment
fn fragmentMain()
{
    return;
}

