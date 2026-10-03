struct Params_std140_0
{
    @align(16) resolution_0 : vec4<f32>,
    @align(16) unproject_0 : vec4<f32>,
    @align(16) lens_0 : vec4<f32>,
};

@binding(5) @group(0) var<uniform> p_0 : Params_std140_0;
@binding(0) @group(0) var uRaw_0 : texture_2d<f32>;

@binding(2) @group(0) var uDepth_0 : texture_depth_2d;

@binding(4) @group(0) var uMask_0 : texture_storage_2d<rgba32float, write>;

var<private> kDisc_0 : array<vec2<f32>, i32(12)> = array<vec2<f32>, i32(12)>( vec2<f32>(0.20409999787807465f, 0.0f), vec2<f32>(-0.26769998669624329f, 0.24549999833106995f), vec2<f32>(0.03689999878406525f, -0.49410000443458557f), vec2<f32>(0.34209999442100525f, 0.46959999203681946f), vec2<f32>(-0.63889998197555542f, -0.1062999963760376f), vec2<f32>(0.62029999494552612f, -0.37119999527931213f), vec2<f32>(-0.20329999923706055f, 0.73940002918243408f), vec2<f32>(-0.37119999527931213f, -0.72500002384185791f), vec2<f32>(0.81199997663497925f, 0.22480000555515289f), vec2<f32>(-0.84549999237060547f, 0.32829999923706055f), vec2<f32>(0.31760001182556152f, -0.89109998941421509f), vec2<f32>(0.34189999103546143f, 0.93980002403259277f) );
fn ViewDepthAt_0( depth_0 : f32,  unproject_1 : vec4<f32>) -> f32
{
    var _S1 : f32 = unproject_1.z;
    var _S2 : f32 = unproject_1.w;
    return 2.0f * _S1 * _S2 / (_S2 + _S1 - (1.0f - 2.0f * depth_0) * (_S2 - _S1));
}

@compute
@workgroup_size(8, 8, 1)
fn computeMain(@builtin(global_invocation_id) id_0 : vec3<u32>)
{
    var x_0 : i32;
    var _S3 : vec2<i32> = vec2<i32>(id_0.xy);
    var _S4 : bool;
    if((_S3.x) >= i32(p_0.resolution_0.x))
    {
        _S4 = true;
    }
    else
    {
        _S4 = (_S3.y) >= i32(p_0.resolution_0.y);
    }
    if(_S4)
    {
        return;
    }
    var _S5 : vec3<i32> = vec3<i32>(_S3, i32(0));
    var centre_0 : vec4<f32> = (textureLoad((uRaw_0), ((_S5)).xy, ((_S5)).z));
    var depth_1 : f32 = (textureLoad((uDepth_0), ((_S5)).xy, ((_S5)).z));
    if(depth_1 <= 0.0f)
    {
        textureStore((uMask_0), (vec2<u32>(_S3)), (vec4<f32>(1.0f, 0.0f, 0.0f, 0.0f)));
        return;
    }
    var viewZ_0 : f32 = ViewDepthAt_0(depth_1, p_0.unproject_0);
    var _S6 : vec2<i32> = vec2<i32>(p_0.resolution_0.xy) - vec2<i32>(i32(1));
    var ratio_0 : f32 = centre_0.y;
    var y_0 : i32 = i32(-1);
    for(;;)
    {
        if(y_0 <= i32(1))
        {
        }
        else
        {
            break;
        }
        x_0 = i32(-1);
        for(;;)
        {
            if(x_0 <= i32(1))
            {
            }
            else
            {
                break;
            }
            var _S7 : vec3<i32> = vec3<i32>(clamp(_S3 + vec2<i32>(x_0, y_0) * vec2<i32>(i32(2)), vec2<i32>(i32(0), i32(0)), _S6), i32(0));
            var _S8 : f32 = max(ratio_0, (textureLoad((uRaw_0), ((_S7)).xy, ((_S7)).z)).y);
            var _S9 : i32 = x_0 + i32(1);
            ratio_0 = _S8;
            x_0 = _S9;
        }
        y_0 = y_0 + i32(1);
    }
    if(ratio_0 <= 0.0f)
    {
        textureStore((uMask_0), (vec2<u32>(_S3)), (vec4<f32>(centre_0.x, 0.0f, 0.0f, 0.0f)));
        return;
    }
    var _S10 : f32 = clamp(p_0.lens_0.x * ratio_0 / max(2.0f * viewZ_0 / max(p_0.unproject_0.x * p_0.resolution_0.x, 0.00009999999747379f), 9.99999997475242708e-07f), 1.0f, 12.0f);
    var angle_0 : f32 = fract(sin(dot(vec2<f32>(_S3), vec2<f32>(12.98980045318603516f, 78.233001708984375f))) * 43758.546875f) * 6.28318548202514648f;
    var _S11 : f32 = cos(angle_0);
    var _S12 : f32 = sin(angle_0);
    var _S13 : f32 = centre_0.x;
    x_0 = i32(0);
    var total_0 : f32 = _S13;
    var weights_0 : f32 = 1.0f;
    for(;;)
    {
        if(x_0 < i32(12))
        {
        }
        else
        {
            break;
        }
        var _S14 : f32 = kDisc_0[x_0].x;
        var _S15 : f32 = kDisc_0[x_0].y;
        var _S16 : vec3<i32> = vec3<i32>(clamp(_S3 + vec2<i32>(round(vec2<f32>(_S14 * _S11 - _S15 * _S12, _S14 * _S12 + _S15 * _S11) * vec2<f32>(_S10))), vec2<i32>(i32(0), i32(0)), _S6), i32(0));
        var tapDepth_0 : f32 = (textureLoad((uDepth_0), ((_S16)).xy, ((_S16)).z));
        if(tapDepth_0 <= 0.0f)
        {
            x_0 = x_0 + i32(1);
            continue;
        }
        var weight_0 : f32 = exp2(- abs(ViewDepthAt_0(tapDepth_0, p_0.unproject_0) - viewZ_0) / max(0.01999999955296516f * viewZ_0, 0.00999999977648258f));
        var weights_1 : f32 = weights_0 + weight_0;
        total_0 = total_0 + (textureLoad((uRaw_0), ((_S16)).xy, ((_S16)).z)).x * weight_0;
        weights_0 = weights_1;
        x_0 = x_0 + i32(1);
    }
    textureStore((uMask_0), (vec2<u32>(_S3)), (vec4<f32>(total_0 / weights_0, ratio_0, 0.0f, 0.0f)));
    return;
}

