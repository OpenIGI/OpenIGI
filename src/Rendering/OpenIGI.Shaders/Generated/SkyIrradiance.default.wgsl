struct Params_std140_0
{
    @align(16) size_0 : vec4<f32>,
    @align(16) sunDir_0 : vec4<f32>,
    @align(16) sunColour_0 : vec4<f32>,
    @align(16) misc_0 : vec4<f32>,
    @align(16) moonDir_0 : vec4<f32>,
    @align(16) moonColour_0 : vec4<f32>,
};

@binding(3) @group(0) var<uniform> p_0 : Params_std140_0;
@binding(0) @group(0) var uSkyView_0 : texture_2d<f32>;

@binding(1) @group(0) var uSkyViewSampler_0 : sampler;

@binding(2) @group(0) var uIrradiance_0 : texture_storage_2d<rgba32float, write>;

fn SkyViewUv_0( worldDir_0 : vec3<f32>,  viewHeight_0 : f32) -> vec2<f32>
{
    var beta_0 : f32 = acos(clamp(sqrt(max(0.0f, viewHeight_0 * viewHeight_0 - 4.04496e+07f)) / viewHeight_0, -1.0f, 1.0f));
    var zenithHorizonAngle_0 : f32 = 3.14159274101257324f - beta_0;
    var viewZenithAngle_0 : f32 = acos(clamp(worldDir_0.z, -1.0f, 1.0f));
    var v_0 : f32;
    if(viewZenithAngle_0 < zenithHorizonAngle_0)
    {
        v_0 = (1.0f - sqrt(max(0.0f, 1.0f - viewZenithAngle_0 / zenithHorizonAngle_0))) * 0.5f;
    }
    else
    {
        v_0 = sqrt(max(0.0f, (viewZenithAngle_0 - zenithHorizonAngle_0) / max(beta_0, 9.99999997475242708e-07f))) * 0.5f + 0.5f;
    }
    var azimuth_0 : f32 = atan2(worldDir_0.y, worldDir_0.x);
    var azimuth_1 : f32;
    if(azimuth_0 < 0.0f)
    {
        azimuth_1 = azimuth_0 + 6.28318548202514648f;
    }
    else
    {
        azimuth_1 = azimuth_0;
    }
    return vec2<f32>(azimuth_1 / 6.28318548202514648f, v_0);
}

fn SkyViewInset_0( uv_0 : vec2<f32>,  size_1 : vec2<f32>) -> vec2<f32>
{
    return uv_0 * ((size_1 - vec2<f32>(1.0f)) / size_1) + vec2<f32>(0.5f) / size_1;
}

fn SampleSkyViewUv_0( skyView_0 : texture_2d<f32>,  skyViewSampler_0 : sampler,  uv_1 : vec2<f32>) -> vec3<f32>
{
    var width_0 : u32;
    var height_0 : u32;
    {var dim = textureDimensions((skyView_0));((width_0)) = dim.x;((height_0)) = dim.y;};
    return (textureSampleLevel((skyView_0), (skyViewSampler_0), (SkyViewInset_0(uv_1, vec2<f32>(f32(width_0), f32(height_0)))), (0.0f))).xyz;
}

fn SampleSkyView_0( skyView_1 : texture_2d<f32>,  skyViewSampler_1 : sampler,  worldDir_1 : vec3<f32>,  viewHeight_1 : f32) -> vec3<f32>
{
    return SampleSkyViewUv_0(skyView_1, skyViewSampler_1, SkyViewUv_0(worldDir_1, viewHeight_1));
}

var<workgroup> sCoefficients_0 : array<array<vec3<f32>, i32(9)>, i32(64)>;

@compute
@workgroup_size(8, 8, 1)
fn computeMain(@builtin(local_invocation_id) localId_0 : vec3<u32>)
{
    var index_0 : u32 = localId_0.y * u32(8) + localId_0.x;
    var e_0 : vec2<f32> = (vec2<f32>(localId_0.xy) + vec2<f32>(0.5f)) / vec2<f32>(8.0f);
    var phi_0 : f32 = 6.28318548202514648f * e_0.x;
    var cosTheta_0 : f32 = 1.0f - 2.0f * e_0.y;
    var sinTheta_0 : f32 = sqrt(max(0.0f, 1.0f - cosTheta_0 * cosTheta_0));
    var _S1 : f32 = sinTheta_0 * cos(phi_0);
    var _S2 : f32 = sinTheta_0 * sin(phi_0);
    var _S3 : vec3<f32> = SampleSkyView_0(uSkyView_0, uSkyViewSampler_0, vec3<f32>(_S1, _S2, cosTheta_0), p_0.misc_0.x) * vec3<f32>(0.19634954631328583f) * vec3<f32>(p_0.misc_0.y);
    var basis_0 : array<f32, i32(9)>;
    basis_0[i32(0)] = 0.28209498524665833f;
    basis_0[i32(1)] = -0.48860299587249756f * _S2;
    basis_0[i32(2)] = 0.48860299587249756f * cosTheta_0;
    basis_0[i32(3)] = -0.48860299587249756f * _S1;
    basis_0[i32(4)] = 1.09254801273345947f * _S1 * _S2;
    basis_0[i32(5)] = -1.09254801273345947f * _S2 * cosTheta_0;
    basis_0[i32(6)] = 0.31539198756217957f * (3.0f * cosTheta_0 * cosTheta_0 - 1.0f);
    basis_0[i32(7)] = -1.09254801273345947f * _S1 * cosTheta_0;
    basis_0[i32(8)] = 0.54627400636672974f * (_S1 * _S1 - _S2 * _S2);
    var i_0 : i32 = i32(0);
    for(;;)
    {
        if(i_0 < i32(9))
        {
        }
        else
        {
            break;
        }
        sCoefficients_0[index_0][i_0] = _S3 * vec3<f32>(basis_0[i_0]);
        i_0 = i_0 + i32(1);
    }
    workgroupBarrier();
    var stride_0 : u32 = u32(32);
    for(;;)
    {
        if(stride_0 > u32(0))
        {
        }
        else
        {
            break;
        }
        if(index_0 < stride_0)
        {
            i_0 = i32(0);
            for(;;)
            {
                if(i_0 < i32(9))
                {
                }
                else
                {
                    break;
                }
                sCoefficients_0[index_0][i_0] = sCoefficients_0[index_0][i_0] + sCoefficients_0[index_0 + stride_0][i_0];
                i_0 = i_0 + i32(1);
            }
        }
        workgroupBarrier();
        stride_0 = (stride_0 >> (u32(1)));
    }
    if(index_0 != u32(0))
    {
        return;
    }
    var c_0 : array<vec3<f32>, i32(9)>;
    i_0 = i32(0);
    for(;;)
    {
        if(i_0 < i32(9))
        {
        }
        else
        {
            break;
        }
        c_0[i_0] = sCoefficients_0[i32(0)][i_0];
        i_0 = i_0 + i32(1);
    }
    textureStore((uIrradiance_0), (vec2<u32>(vec2<i32>(i32(0), i32(0)))), (vec4<f32>(-0.32573500275611877f * c_0[i32(3)].x, -0.32573500275611877f * c_0[i32(1)].x, 0.32573500275611877f * c_0[i32(2)].x, 0.282094806432724f * c_0[i32(0)].x - 0.07884784042835236f * c_0[i32(6)].x)));
    textureStore((uIrradiance_0), (vec2<u32>(vec2<i32>(i32(1), i32(0)))), (vec4<f32>(-0.32573500275611877f * c_0[i32(3)].y, -0.32573500275611877f * c_0[i32(1)].y, 0.32573500275611877f * c_0[i32(2)].y, 0.282094806432724f * c_0[i32(0)].y - 0.07884784042835236f * c_0[i32(6)].y)));
    textureStore((uIrradiance_0), (vec2<u32>(vec2<i32>(i32(2), i32(0)))), (vec4<f32>(-0.32573500275611877f * c_0[i32(3)].z, -0.32573500275611877f * c_0[i32(1)].z, 0.32573500275611877f * c_0[i32(2)].z, 0.282094806432724f * c_0[i32(0)].z - 0.07884784042835236f * c_0[i32(6)].z)));
    textureStore((uIrradiance_0), (vec2<u32>(vec2<i32>(i32(3), i32(0)))), (vec4<f32>(0.27313712239265442f * c_0[i32(4)].x, -0.27313712239265442f * c_0[i32(5)].x, 0.23654352128505707f * c_0[i32(6)].x, -0.27313712239265442f * c_0[i32(7)].x)));
    textureStore((uIrradiance_0), (vec2<u32>(vec2<i32>(i32(4), i32(0)))), (vec4<f32>(0.27313712239265442f * c_0[i32(4)].y, -0.27313712239265442f * c_0[i32(5)].y, 0.23654352128505707f * c_0[i32(6)].y, -0.27313712239265442f * c_0[i32(7)].y)));
    textureStore((uIrradiance_0), (vec2<u32>(vec2<i32>(i32(5), i32(0)))), (vec4<f32>(0.27313712239265442f * c_0[i32(4)].z, -0.27313712239265442f * c_0[i32(5)].z, 0.23654352128505707f * c_0[i32(6)].z, -0.27313712239265442f * c_0[i32(7)].z)));
    textureStore((uIrradiance_0), (vec2<u32>(vec2<i32>(i32(6), i32(0)))), (vec4<f32>(0.13656856119632721f * c_0[i32(8)].x, 0.13656856119632721f * c_0[i32(8)].y, 0.13656856119632721f * c_0[i32(8)].z, 1.0f)));
    textureStore((uIrradiance_0), (vec2<u32>(vec2<i32>(i32(7), i32(0)))), (vec4<f32>(c_0[i32(0)] * vec3<f32>(0.28209459781646729f), 1.0f)));
    return;
}

