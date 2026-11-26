struct _MatrixStorage_float4x4_ColMajorstd140_0
{
    @align(16) data_0 : array<vec4<f32>, i32(4)>,
};

struct GhostMaskPush_std140_0
{
    @align(16) modelViewProjection_0 : _MatrixStorage_float4x4_ColMajorstd140_0,
    @align(16) rightRow_0 : vec4<f32>,
    @align(16) upRow_0 : vec4<f32>,
    @align(16) depthRow_0 : vec4<f32>,
    @align(16) control_0 : vec4<f32>,
};

@binding(0) @group(3) var<uniform> push_0 : GhostMaskPush_std140_0;
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

struct GhostMaskVaryings_0
{
    @builtin(position) position_0 : vec4<f32>,
    @location(0) viewNormal_0 : vec3<f32>,
    @location(1) surface_0 : vec2<f32>,
    @location(2) bodyMetres_0 : vec3<f32>,
};

struct vertexInput_0
{
    @location(0) position_1 : vec3<f32>,
    @location(1) normal_0 : vec3<f32>,
    @location(5) previousPosition_0 : vec3<f32>,
    @location(6) previousNormal_0 : vec3<f32>,
};

@vertex
fn vertexMain( _S2 : vertexInput_0) -> GhostMaskVaryings_0
{
    var _S3 : vec3<f32> = vec3<f32>(push_0.control_0.x);
    var localPosition_0 : vec3<f32> = _S2.position_1 + _S3 * (_S2.previousPosition_0 - _S2.position_1);
    var localNormal_0 : vec3<f32> = _S2.normal_0 + _S3 * (_S2.previousNormal_0 - _S2.normal_0);
    var scale_0 : f32 = length(push_0.depthRow_0.xyz);
    var output_0 : GhostMaskVaryings_0;
    output_0.viewNormal_0 = vec3<f32>(dot(push_0.rightRow_0.xyz, localNormal_0), dot(push_0.upRow_0.xyz, localNormal_0), dot(push_0.depthRow_0.xyz, localNormal_0));
    output_0.surface_0 = vec2<f32>(dot(push_0.depthRow_0.xyz, localPosition_0) + push_0.depthRow_0.w, localPosition_0.z * scale_0);
    output_0.bodyMetres_0 = localPosition_0 * vec3<f32>(scale_0);
    var _S4 : vec4<f32> = (((vec4<f32>(localPosition_0, 1.0f)) * (mat4x4<f32>(push_0.modelViewProjection_0.data_0[i32(0)][i32(0)], push_0.modelViewProjection_0.data_0[i32(1)][i32(0)], push_0.modelViewProjection_0.data_0[i32(2)][i32(0)], push_0.modelViewProjection_0.data_0[i32(3)][i32(0)], push_0.modelViewProjection_0.data_0[i32(0)][i32(1)], push_0.modelViewProjection_0.data_0[i32(1)][i32(1)], push_0.modelViewProjection_0.data_0[i32(2)][i32(1)], push_0.modelViewProjection_0.data_0[i32(3)][i32(1)], push_0.modelViewProjection_0.data_0[i32(0)][i32(2)], push_0.modelViewProjection_0.data_0[i32(1)][i32(2)], push_0.modelViewProjection_0.data_0[i32(2)][i32(2)], push_0.modelViewProjection_0.data_0[i32(3)][i32(2)], push_0.modelViewProjection_0.data_0[i32(0)][i32(3)], push_0.modelViewProjection_0.data_0[i32(1)][i32(3)], push_0.modelViewProjection_0.data_0[i32(2)][i32(3)], push_0.modelViewProjection_0.data_0[i32(3)][i32(3)]))));
    var clip_0 : vec4<f32> = _S4;
    clip_0[i32(1)] = OpenIgiClipY_0(_S4.y);
    clip_0[i32(2)] = (clip_0.w - clip_0.z) * 0.5f;
    output_0.position_0 = clip_0;
    return output_0;
}

fn Hash3_0( p_0 : vec3<f32>) -> f32
{
    var _S5 : vec3<f32> = fract(p_0 * vec3<f32>(0.1031000018119812f, 0.10300000011920929f, 0.09730000048875809f));
    var _S6 : vec3<f32> = _S5 + vec3<f32>(dot(_S5, _S5.yxz + vec3<f32>(33.3300018310546875f)));
    return fract((_S6.x + _S6.y) * _S6.z);
}

fn ValueNoise_0( p_1 : vec3<f32>) -> f32
{
    var i_0 : vec3<f32> = floor(p_1);
    var f_0 : vec3<f32> = fract(p_1);
    var u_0 : vec3<f32> = f_0 * f_0 * (vec3<f32>(3.0f) - vec3<f32>(2.0f) * f_0);
    var _S7 : f32 = u_0.x;
    var _S8 : f32 = u_0.y;
    return mix(mix(mix(Hash3_0(i_0), Hash3_0(i_0 + vec3<f32>(1.0f, 0.0f, 0.0f)), _S7), mix(Hash3_0(i_0 + vec3<f32>(0.0f, 1.0f, 0.0f)), Hash3_0(i_0 + vec3<f32>(1.0f, 1.0f, 0.0f)), _S7), _S8), mix(mix(Hash3_0(i_0 + vec3<f32>(0.0f, 0.0f, 1.0f)), Hash3_0(i_0 + vec3<f32>(1.0f, 0.0f, 1.0f)), _S7), mix(Hash3_0(i_0 + vec3<f32>(0.0f, 1.0f, 1.0f)), Hash3_0(i_0 + vec3<f32>(1.0f, 1.0f, 1.0f)), _S7), _S8), u_0.z);
}

struct GhostMaskTargets_0
{
    @location(0) shape_0 : vec4<f32>,
    @location(1) skin_0 : vec4<f32>,
};

struct pixelInput_0
{
    @location(0) viewNormal_1 : vec3<f32>,
    @location(1) surface_1 : vec2<f32>,
    @location(2) bodyMetres_1 : vec3<f32>,
};

@fragment
fn fragmentMain( _S9 : pixelInput_0, @builtin(position) position_2 : vec4<f32>) -> GhostMaskTargets_0
{
    var noise_0 : f32 = saturate(0.69999998807907104f * ValueNoise_0(_S9.bodyMetres_1 * vec3<f32>(4.0f)) + 0.30000001192092896f * Hash3_0(floor(_S9.bodyMetres_1 * vec3<f32>(16.0f))));
    if(noise_0 > (push_0.control_0.z * 1.00010001659393311f))
    {
        discard;
    }
    var length2_0 : f32 = dot(_S9.viewNormal_1, _S9.viewNormal_1);
    var n_0 : vec3<f32>;
    if(length2_0 > 9.999999960041972e-13f)
    {
        n_0 = _S9.viewNormal_1 * vec3<f32>(rsqrt_0(length2_0));
    }
    else
    {
        n_0 = vec3<f32>(0.0f, 0.0f, -1.0f);
    }
    if((n_0.z) > 0.0f)
    {
        n_0 = (vec3<f32>(0) - n_0);
    }
    var rim_0 : f32 = pow(saturate(1.0f - abs(n_0.z)), 2.40000009536743164f);
    var output_1 : GhostMaskTargets_0;
    output_1.shape_0 = vec4<f32>(n_0.xy, max(_S9.surface_1.x, 0.0f), 1.0f);
    output_1.skin_0 = vec4<f32>(saturate(_S9.surface_1.y / 1.89999997615814209f), noise_0, rim_0, 0.0f);
    return output_1;
}

