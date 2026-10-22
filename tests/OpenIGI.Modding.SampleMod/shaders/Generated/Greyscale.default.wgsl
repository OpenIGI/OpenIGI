struct ModPostParams_std140_0
{
    @align(16) size_0 : vec4<f32>,
    @align(16) time_0 : vec4<f32>,
    @align(16) values_0 : array<vec4<f32>, i32(4)>,
};

@binding(4) @group(0) var<uniform> p_0 : ModPostParams_std140_0;
@binding(0) @group(0) var uSource_0 : texture_2d<f32>;

@binding(3) @group(0) var uTarget_0 : texture_storage_2d<rgba16float, write>;

@compute
@workgroup_size(8, 8, 1)
fn main(@builtin(global_invocation_id) id_0 : vec3<u32>)
{
    var _S1 : vec2<u32> = id_0.xy;
    if((any((_S1 >= vec2<u32>(p_0.size_0.xy)))))
    {
        return;
    }
    var _S2 : vec3<i32> = vec3<i32>(vec3<u32>(_S1, u32(0)));
    var _S3 : vec4<f32> = (textureLoad((uSource_0), ((_S2)).xy, ((_S2)).z));
    var _S4 : vec3<f32> = _S3.xyz;
    textureStore((uTarget_0), (_S1), (vec4<f32>(mix(_S4, vec3<f32>(dot(_S4, vec3<f32>(0.2125999927520752f, 0.71520000696182251f, 0.07220000028610229f))), vec3<f32>(saturate(p_0.values_0[i32(0)].x))), _S3.w)));
    return;
}

