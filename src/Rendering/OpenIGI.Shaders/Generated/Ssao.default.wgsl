struct _MatrixStorage_float4x4_ColMajorstd140_0
{
    @align(16) data_0 : array<vec4<f32>, i32(4)>,
};

struct Params_std140_0
{
    @align(16) viewToWorld_0 : _MatrixStorage_float4x4_ColMajorstd140_0,
    @align(16) prevWorldToView_0 : _MatrixStorage_float4x4_ColMajorstd140_0,
    @align(16) resolution_0 : vec4<f32>,
    @align(16) fullResolution_0 : vec4<f32>,
    @align(16) unproject_0 : vec4<f32>,
    @align(16) params_0 : vec4<f32>,
    @align(16) cameraDelta_0 : vec4<f32>,
    @align(16) temporal_0 : vec4<f32>,
};

@binding(7) @group(0) var<uniform> p_0 : Params_std140_0;
@binding(0) @group(0) var uDepth_0 : texture_depth_2d;

@binding(1) @group(0) var uDepthSampler_0 : sampler;

@binding(2) @group(0) var uOcclusion_0 : texture_storage_2d<rgba32float, write>;

@binding(8) @group(0) var uVelocity_0 : texture_2d<f32>;

@binding(3) @group(0) var uPreviousOcclusion_0 : texture_2d<f32>;

@binding(4) @group(0) var uPreviousOcclusionSampler_0 : sampler;

@binding(5) @group(0) var uPreviousDepth_0 : texture_2d<f32>;

@binding(6) @group(0) var uPreviousDepthSampler_0 : sampler;

fn DepthBilinearLoaded_0( depth_0 : texture_depth_2d,  uv_0 : vec2<f32>) -> f32
{
    var width_0 : u32;
    var height_0 : u32;
    {var dim = textureDimensions((depth_0));((width_0)) = dim.x;((height_0)) = dim.y;};
    var last_0 : vec2<i32> = vec2<i32>(i32(width_0) - i32(1), i32(height_0) - i32(1));
    var position_0 : vec2<f32> = uv_0 * vec2<f32>(f32(width_0), f32(height_0)) - vec2<f32>(0.5f);
    var floored_0 : vec2<f32> = floor(position_0);
    var weight_0 : vec2<f32> = position_0 - floored_0;
    var _S1 : vec2<i32> = vec2<i32>(floored_0);
    const _S2 : vec2<i32> = vec2<i32>(i32(0), i32(0));
    var _S3 : vec3<i32> = vec3<i32>(clamp(_S1, _S2, last_0), i32(0));
    var _S4 : vec3<i32> = vec3<i32>(clamp(_S1 + vec2<i32>(i32(1), i32(0)), _S2, last_0), i32(0));
    var _S5 : vec3<i32> = vec3<i32>(clamp(_S1 + vec2<i32>(i32(0), i32(1)), _S2, last_0), i32(0));
    var _S6 : vec3<i32> = vec3<i32>(clamp(_S1 + vec2<i32>(i32(1), i32(1)), _S2, last_0), i32(0));
    var _S7 : f32 = weight_0.x;
    return mix(mix((textureLoad((depth_0), ((_S3)).xy, ((_S3)).z)), (textureLoad((depth_0), ((_S4)).xy, ((_S4)).z)), _S7), mix((textureLoad((depth_0), ((_S5)).xy, ((_S5)).z)), (textureLoad((depth_0), ((_S6)).xy, ((_S6)).z)), _S7), weight_0.y);
}

fn DepthBilinear_0( depth_1 : texture_depth_2d,  linear_0 : sampler,  uv_1 : vec2<f32>) -> f32
{
    return DepthBilinearLoaded_0(depth_1, uv_1);
}

fn ViewDepthAt_0( depth_2 : f32,  unproject_1 : vec4<f32>) -> f32
{
    var _S8 : f32 = unproject_1.z;
    var _S9 : f32 = unproject_1.w;
    return 2.0f * _S8 * _S9 / (_S9 + _S8 - (1.0f - 2.0f * depth_2) * (_S9 - _S8));
}

fn ViewPositionAt_0( uv_2 : vec2<f32>,  depth_3 : f32,  unproject_2 : vec4<f32>) -> vec3<f32>
{
    var viewZ_0 : f32 = ViewDepthAt_0(depth_3, unproject_2);
    var ndc_0 : vec2<f32> = uv_2 * vec2<f32>(2.0f) - vec2<f32>(1.0f);
    return vec3<f32>(ndc_0.x * viewZ_0 / unproject_2.x, - ndc_0.y * viewZ_0 / unproject_2.y, viewZ_0);
}

fn ViewPosition_0( uv_3 : vec2<f32>) -> vec3<f32>
{
    return ViewPositionAt_0(uv_3, DepthBilinear_0(uDepth_0, uDepthSampler_0, uv_3), p_0.unproject_0);
}

fn Hash_0( v_0 : vec2<f32>) -> f32
{
    return fract(sin(dot(v_0, vec2<f32>(12.98980045318603516f, 78.233001708984375f))) * 43758.546875f);
}

fn ViewToUv_0( v_1 : vec3<f32>) -> vec2<f32>
{
    var _S10 : f32 = 2.0f * v_1.z;
    return vec2<f32>(0.5f + v_1.x * p_0.unproject_0.x / _S10, 0.5f - v_1.y * p_0.unproject_0.y / _S10);
}

fn FreshOcclusion_0( px_0 : vec2<i32>,  ao_0 : ptr<function, f32>,  position_1 : ptr<function, vec3<f32>>,  normal_0 : ptr<function, vec3<f32>>) -> bool
{
    (*ao_0) = 1.0f;
    (*position_1) = vec3<f32>(0.0f, 0.0f, 0.0f);
    (*normal_0) = vec3<f32>(0.0f, 0.0f, 1.0f);
    var _S11 : i32 = px_0.x;
    var _S12 : bool;
    if(_S11 >= i32(p_0.resolution_0.x))
    {
        _S12 = true;
    }
    else
    {
        _S12 = (px_0.y) >= i32(p_0.resolution_0.y);
    }
    if(_S12)
    {
        return false;
    }
    var _S13 : vec2<f32> = vec2<f32>(px_0);
    var uv_4 : vec2<f32> = (_S13 * vec2<f32>(2.0f) + vec2<f32>(0.5f)) * p_0.fullResolution_0.zw;
    var depth_4 : f32 = DepthBilinear_0(uDepth_0, uDepthSampler_0, uv_4);
    if(depth_4 <= 0.0f)
    {
        return false;
    }
    var _S14 : vec3<f32> = ViewPositionAt_0(uv_4, depth_4, p_0.unproject_0);
    (*position_1) = _S14;
    var _S15 : vec3<f32> = normalize(cross(ViewPosition_0(uv_4 + vec2<f32>(p_0.fullResolution_0.z, 0.0f)) - _S14, ViewPosition_0(uv_4 + vec2<f32>(0.0f, p_0.fullResolution_0.w)) - _S14));
    (*normal_0) = _S15;
    if((dot(_S15, (*position_1))) > 0.0f)
    {
        (*normal_0) = (vec3<f32>(0) - (*normal_0));
    }
    var frameIndex_0 : f32 = p_0.cameraDelta_0.w;
    var angle_0 : f32 = Hash_0(_S13 + vec2<f32>(frameIndex_0 * 17.0f, frameIndex_0 * 41.0f)) * 6.28318548202514648f;
    var randomVec_0 : vec3<f32> = vec3<f32>(cos(angle_0), sin(angle_0), 0.0f);
    var tangent_0 : vec3<f32> = normalize(randomVec_0 - (*normal_0) * vec3<f32>(dot(randomVec_0, (*normal_0))));
    var _S16 : vec3<f32> = cross((*normal_0), tangent_0);
    var radius_0 : f32 = p_0.params_0.x;
    var _S17 : f32 = 1.5f * ((*position_1).z * 2.0f / max(p_0.unproject_0.x * p_0.resolution_0.x, 0.00100000004749745f)) + p_0.params_0.z * 0.10000000149011612f * radius_0;
    var i_0 : i32 = i32(0);
    var occlusion_0 : f32 = 0.0f;
    for(;;)
    {
        if(i_0 < i32(16))
        {
        }
        else
        {
            break;
        }
        var _S18 : f32 = f32(i_0);
        var _S19 : i32 = px_0.y;
        var r2_0 : f32 = Hash_0(vec2<f32>(_S18 * 1.70000004768371582f + 0.5f + frameIndex_0 * 0.38199999928474426f, f32(_S19 * i32(3) + _S11)));
        var theta_0 : f32 = 6.28318548202514648f * Hash_0(vec2<f32>(_S18 + 0.12999999523162842f + frameIndex_0 * 0.61799997091293335f, f32(_S11 * i32(3) + _S19)));
        var disk_0 : f32 = sqrt(r2_0);
        var along_0 : f32 = (_S18 + 0.5f) / 16.0f;
        var samplePos_0 : vec3<f32> = (*position_1) + (tangent_0 * vec3<f32>((disk_0 * cos(theta_0))) + _S16 * vec3<f32>((disk_0 * sin(theta_0))) + (*normal_0) * vec3<f32>(sqrt(max(1.0f - r2_0, 0.0f)))) * vec3<f32>(radius_0) * vec3<f32>(mix(0.01999999955296516f, 1.0f, along_0 * along_0));
        var sampleUv_0 : vec2<f32> = ViewToUv_0(samplePos_0);
        if((any((sampleUv_0 < vec2<f32>(0.0f, 0.0f)))))
        {
            _S12 = true;
        }
        else
        {
            _S12 = (any((sampleUv_0 > vec2<f32>(1.0f, 1.0f))));
        }
        if(_S12)
        {
            i_0 = i_0 + i32(1);
            continue;
        }
        var sceneZ_0 : f32 = ViewPosition_0(sampleUv_0).z;
        var rangeCheck_0 : f32 = smoothstep(0.0f, 1.0f, radius_0 / max(abs((*position_1).z - sceneZ_0), 0.00009999999747379f));
        var _S20 : f32;
        if(sceneZ_0 <= (samplePos_0.z - _S17))
        {
            _S20 = 1.0f;
        }
        else
        {
            _S20 = 0.0f;
        }
        occlusion_0 = occlusion_0 + _S20 * rangeCheck_0;
        i_0 = i_0 + i32(1);
    }
    var _S21 : f32 = 1.0f - occlusion_0 / 16.0f * p_0.params_0.y;
    (*ao_0) = _S21;
    (*ao_0) = pow(clamp(_S21, 0.0f, 1.0f), max(p_0.params_0.w, 0.00999999977648258f));
    return true;
}

var<workgroup> gFresh_0 : array<array<f32, i32(8)>, i32(8)>;

fn DepthBilinearLoaded_1( depth_5 : texture_2d<f32>,  uv_5 : vec2<f32>) -> f32
{
    var width_1 : u32;
    var height_1 : u32;
    {var dim = textureDimensions((depth_5));((width_1)) = dim.x;((height_1)) = dim.y;};
    var last_1 : vec2<i32> = vec2<i32>(i32(width_1) - i32(1), i32(height_1) - i32(1));
    var position_2 : vec2<f32> = uv_5 * vec2<f32>(f32(width_1), f32(height_1)) - vec2<f32>(0.5f);
    var floored_1 : vec2<f32> = floor(position_2);
    var weight_1 : vec2<f32> = position_2 - floored_1;
    var _S22 : vec2<i32> = vec2<i32>(floored_1);
    const _S23 : vec2<i32> = vec2<i32>(i32(0), i32(0));
    var _S24 : vec3<i32> = vec3<i32>(clamp(_S22, _S23, last_1), i32(0));
    var _S25 : vec3<i32> = vec3<i32>(clamp(_S22 + vec2<i32>(i32(1), i32(0)), _S23, last_1), i32(0));
    var _S26 : vec3<i32> = vec3<i32>(clamp(_S22 + vec2<i32>(i32(0), i32(1)), _S23, last_1), i32(0));
    var _S27 : vec3<i32> = vec3<i32>(clamp(_S22 + vec2<i32>(i32(1), i32(1)), _S23, last_1), i32(0));
    var _S28 : f32 = weight_1.x;
    return mix(mix((textureLoad((depth_5), ((_S24)).xy, ((_S24)).z)).x, (textureLoad((depth_5), ((_S25)).xy, ((_S25)).z)).x, _S28), mix((textureLoad((depth_5), ((_S26)).xy, ((_S26)).z)).x, (textureLoad((depth_5), ((_S27)).xy, ((_S27)).z)).x, _S28), weight_1.y);
}

fn DepthBilinear_1( depth_6 : texture_2d<f32>,  linear_1 : sampler,  uv_6 : vec2<f32>) -> f32
{
    return DepthBilinearLoaded_1(depth_6, uv_6);
}

@compute
@workgroup_size(8, 8, 1)
fn computeMain(@builtin(global_invocation_id) id_0 : vec3<u32>, @builtin(local_invocation_id) local_0 : vec3<u32>)
{
    var _S29 : vec2<i32> = vec2<i32>(id_0.xy);
    var ao_1 : f32;
    var position_3 : vec3<f32>;
    var normal_1 : vec3<f32>;
    var surface_0 : bool = FreshOcclusion_0(_S29, &(ao_1), &(position_3), &(normal_1));
    gFresh_0[local_0.y][local_0.x] = ao_1;
    workgroupBarrier();
    var ownMotion_0 : bool;
    if((_S29.x) >= i32(p_0.resolution_0.x))
    {
        ownMotion_0 = true;
    }
    else
    {
        ownMotion_0 = (_S29.y) >= i32(p_0.resolution_0.y);
    }
    if(ownMotion_0)
    {
        return;
    }
    if(!surface_0)
    {
        textureStore((uOcclusion_0), (vec2<u32>(_S29)), (vec4<f32>(1.0f, 1.0f, 1.0f, 1.0f)));
        return;
    }
    var freshMin_0 : f32 = ao_1;
    var freshMax_0 : f32 = ao_1;
    var ny_0 : i32 = i32(-1);
    for(;;)
    {
        if(ny_0 <= i32(1))
        {
        }
        else
        {
            break;
        }
        var freshMin_1 : f32 = freshMin_0;
        var freshMax_1 : f32 = freshMax_0;
        var nx_0 : i32 = i32(-1);
        for(;;)
        {
            if(nx_0 <= i32(1))
            {
            }
            else
            {
                break;
            }
            var n_0 : vec2<i32> = vec2<i32>(local_0.xy) + vec2<i32>(nx_0, ny_0);
            if((all((n_0 >= vec2<i32>(i32(0), i32(0))))))
            {
                ownMotion_0 = (all((n_0 < vec2<i32>(i32(8), i32(8)))));
            }
            else
            {
                ownMotion_0 = false;
            }
            if(ownMotion_0)
            {
                var _S30 : f32 = max(freshMax_1, gFresh_0[n_0.y][n_0.x]);
                freshMin_1 = min(freshMin_1, gFresh_0[n_0.y][n_0.x]);
                freshMax_1 = _S30;
            }
            nx_0 = nx_0 + i32(1);
        }
        var _S31 : i32 = ny_0 + i32(1);
        freshMin_0 = freshMin_1;
        freshMax_0 = freshMax_1;
        ny_0 = _S31;
    }
    if((p_0.temporal_0.x) > 0.5f)
    {
        var prevView_0 : vec3<f32> = (((vec4<f32>((((vec4<f32>(position_3, 0.0f)) * (mat4x4<f32>(p_0.viewToWorld_0.data_0[i32(0)][i32(0)], p_0.viewToWorld_0.data_0[i32(1)][i32(0)], p_0.viewToWorld_0.data_0[i32(2)][i32(0)], p_0.viewToWorld_0.data_0[i32(3)][i32(0)], p_0.viewToWorld_0.data_0[i32(0)][i32(1)], p_0.viewToWorld_0.data_0[i32(1)][i32(1)], p_0.viewToWorld_0.data_0[i32(2)][i32(1)], p_0.viewToWorld_0.data_0[i32(3)][i32(1)], p_0.viewToWorld_0.data_0[i32(0)][i32(2)], p_0.viewToWorld_0.data_0[i32(1)][i32(2)], p_0.viewToWorld_0.data_0[i32(2)][i32(2)], p_0.viewToWorld_0.data_0[i32(3)][i32(2)], p_0.viewToWorld_0.data_0[i32(0)][i32(3)], p_0.viewToWorld_0.data_0[i32(1)][i32(3)], p_0.viewToWorld_0.data_0[i32(2)][i32(3)], p_0.viewToWorld_0.data_0[i32(3)][i32(3)])))).xyz + p_0.cameraDelta_0.xyz, 0.0f)) * (mat4x4<f32>(p_0.prevWorldToView_0.data_0[i32(0)][i32(0)], p_0.prevWorldToView_0.data_0[i32(1)][i32(0)], p_0.prevWorldToView_0.data_0[i32(2)][i32(0)], p_0.prevWorldToView_0.data_0[i32(3)][i32(0)], p_0.prevWorldToView_0.data_0[i32(0)][i32(1)], p_0.prevWorldToView_0.data_0[i32(1)][i32(1)], p_0.prevWorldToView_0.data_0[i32(2)][i32(1)], p_0.prevWorldToView_0.data_0[i32(3)][i32(1)], p_0.prevWorldToView_0.data_0[i32(0)][i32(2)], p_0.prevWorldToView_0.data_0[i32(1)][i32(2)], p_0.prevWorldToView_0.data_0[i32(2)][i32(2)], p_0.prevWorldToView_0.data_0[i32(3)][i32(2)], p_0.prevWorldToView_0.data_0[i32(0)][i32(3)], p_0.prevWorldToView_0.data_0[i32(1)][i32(3)], p_0.prevWorldToView_0.data_0[i32(2)][i32(3)], p_0.prevWorldToView_0.data_0[i32(3)][i32(3)])))).xyz;
        var _S32 : f32 = prevView_0.z;
        if(_S32 > (p_0.unproject_0.z))
        {
            var _S33 : vec2<f32> = vec2<f32>(0.5f);
            var prevUv_0 : vec2<f32> = vec2<f32>(prevView_0.x * p_0.unproject_0.x / _S32, - prevView_0.y * p_0.unproject_0.y / _S32) * _S33 + _S33;
            var prevUv_1 : vec2<f32>;
            if((p_0.temporal_0.y) > 0.5f)
            {
                var fullUv_0 : vec2<f32> = (vec2<f32>(_S29) * vec2<f32>(2.0f) + _S33) * p_0.fullResolution_0.zw;
                var _S34 : vec3<i32> = vec3<i32>(_S29 * vec2<i32>(i32(2)), i32(0));
                var motion_0 : vec2<f32> = (textureLoad((uVelocity_0), ((_S34)).xy, ((_S34)).z)).xy;
                var apart_0 : vec2<f32> = (motion_0 - (fullUv_0 - prevUv_0)) * p_0.fullResolution_0.xy;
                var _S35 : bool = (dot(apart_0, apart_0)) > 2.25f;
                if(_S35)
                {
                    prevUv_1 = fullUv_0 - motion_0;
                }
                else
                {
                    prevUv_1 = prevUv_0;
                }
                ownMotion_0 = _S35;
            }
            else
            {
                ownMotion_0 = false;
                prevUv_1 = prevUv_0;
            }
            var _S36 : bool;
            if(ownMotion_0)
            {
                _S36 = (all((prevUv_1 >= vec2<f32>(0.0f, 0.0f))));
            }
            else
            {
                _S36 = false;
            }
            if(_S36)
            {
                _S36 = (all((prevUv_1 <= vec2<f32>(1.0f, 1.0f))));
            }
            else
            {
                _S36 = false;
            }
            if(_S36)
            {
                ao_1 = mix(clamp((textureSampleLevel((uPreviousOcclusion_0), (uPreviousOcclusionSampler_0), (prevUv_1), (0.0f))).x, freshMin_0, freshMax_0), ao_1, 0.125f);
            }
            else
            {
                if(!ownMotion_0)
                {
                    ownMotion_0 = (all((prevUv_1 >= vec2<f32>(0.0f, 0.0f))));
                }
                else
                {
                    ownMotion_0 = false;
                }
                if(ownMotion_0)
                {
                    ownMotion_0 = (all((prevUv_1 <= vec2<f32>(1.0f, 1.0f))));
                }
                else
                {
                    ownMotion_0 = false;
                }
                if(ownMotion_0)
                {
                    var prevDepth_0 : f32 = DepthBilinear_1(uPreviousDepth_0, uPreviousDepthSampler_0, prevUv_1);
                    if(prevDepth_0 > 0.0f)
                    {
                        if((abs(dot(ViewPositionAt_0(prevUv_1, prevDepth_0, p_0.unproject_0) - prevView_0, normalize(((((((vec4<f32>(normal_1, 0.0f)) * (mat4x4<f32>(p_0.viewToWorld_0.data_0[i32(0)][i32(0)], p_0.viewToWorld_0.data_0[i32(1)][i32(0)], p_0.viewToWorld_0.data_0[i32(2)][i32(0)], p_0.viewToWorld_0.data_0[i32(3)][i32(0)], p_0.viewToWorld_0.data_0[i32(0)][i32(1)], p_0.viewToWorld_0.data_0[i32(1)][i32(1)], p_0.viewToWorld_0.data_0[i32(2)][i32(1)], p_0.viewToWorld_0.data_0[i32(3)][i32(1)], p_0.viewToWorld_0.data_0[i32(0)][i32(2)], p_0.viewToWorld_0.data_0[i32(1)][i32(2)], p_0.viewToWorld_0.data_0[i32(2)][i32(2)], p_0.viewToWorld_0.data_0[i32(3)][i32(2)], p_0.viewToWorld_0.data_0[i32(0)][i32(3)], p_0.viewToWorld_0.data_0[i32(1)][i32(3)], p_0.viewToWorld_0.data_0[i32(2)][i32(3)], p_0.viewToWorld_0.data_0[i32(3)][i32(3)]))))) * (mat4x4<f32>(p_0.prevWorldToView_0.data_0[i32(0)][i32(0)], p_0.prevWorldToView_0.data_0[i32(1)][i32(0)], p_0.prevWorldToView_0.data_0[i32(2)][i32(0)], p_0.prevWorldToView_0.data_0[i32(3)][i32(0)], p_0.prevWorldToView_0.data_0[i32(0)][i32(1)], p_0.prevWorldToView_0.data_0[i32(1)][i32(1)], p_0.prevWorldToView_0.data_0[i32(2)][i32(1)], p_0.prevWorldToView_0.data_0[i32(3)][i32(1)], p_0.prevWorldToView_0.data_0[i32(0)][i32(2)], p_0.prevWorldToView_0.data_0[i32(1)][i32(2)], p_0.prevWorldToView_0.data_0[i32(2)][i32(2)], p_0.prevWorldToView_0.data_0[i32(3)][i32(2)], p_0.prevWorldToView_0.data_0[i32(0)][i32(3)], p_0.prevWorldToView_0.data_0[i32(1)][i32(3)], p_0.prevWorldToView_0.data_0[i32(2)][i32(3)], p_0.prevWorldToView_0.data_0[i32(3)][i32(3)])))).xyz)))) < (max(0.05000000074505806f, 8.0f * (2.0f * position_3.z / (p_0.unproject_0.y * p_0.fullResolution_0.y)))))
                        {
                            ao_1 = mix(clamp((textureSampleLevel((uPreviousOcclusion_0), (uPreviousOcclusionSampler_0), (prevUv_1), (0.0f))).x, freshMin_0, freshMax_0), ao_1, 0.125f);
                        }
                    }
                }
            }
        }
    }
    textureStore((uOcclusion_0), (vec2<u32>(_S29)), (vec4<f32>(ao_1, ao_1, ao_1, ao_1)));
    return;
}

