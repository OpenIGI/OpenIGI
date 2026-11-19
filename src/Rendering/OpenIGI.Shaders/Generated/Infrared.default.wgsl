struct _MatrixStorage_float4x4_ColMajorstd140_0
{
    @align(16) data_0 : array<vec4<f32>, i32(4)>,
};

struct InfraredPush_std140_0
{
    @align(16) modelViewProjection_0 : _MatrixStorage_float4x4_ColMajorstd140_0,
    @align(16) viewRow0_0 : vec4<f32>,
    @align(16) viewRow1_0 : vec4<f32>,
    @align(16) viewRow2_0 : vec4<f32>,
    @align(16) control_0 : vec4<f32>,
};

@binding(0) @group(3) var<uniform> push_0 : InfraredPush_std140_0;
@binding(0) @group(0) var uDiffuse_0 : texture_2d<f32>;

@binding(1) @group(0) var uDiffuseSampler_0 : sampler;

@id(65) override cTargetEncodes_0 : i32 = i32(0);

@id(66) override cSamplerDecodes_0 : i32 = i32(0);

@id(64) override cClipYDown_0 : i32 = i32(1);

fn rsqrt_0( x_0 : f32) -> f32
{
    return 1.0f / sqrt(x_0);
}

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

struct InfraredVaryings_0
{
    @builtin(position) position_0 : vec4<f32>,
    @location(0) ramp_0 : f32,
};

struct vertexInput_0
{
    @location(0) position_1 : vec3<f32>,
    @location(1) normal_0 : vec3<f32>,
    @location(2) diffuseUv_0 : vec2<f32>,
    @location(3) lightmapUv_0 : vec2<f32>,
    @location(4) colorBgra_0 : vec4<f32>,
    @location(5) previousPosition_0 : vec3<f32>,
    @location(6) previousNormal_0 : vec3<f32>,
};

@vertex
fn vertexMain( _S2 : vertexInput_0) -> InfraredVaryings_0
{
    var _S3 : vec3<f32> = vec3<f32>(push_0.control_0.x);
    var localPosition_0 : vec3<f32> = _S2.position_1 + _S3 * (_S2.previousPosition_0 - _S2.position_1);
    var localNormal_0 : vec3<f32> = _S2.normal_0 + _S3 * (_S2.previousNormal_0 - _S2.normal_0);
    var _S4 : f32 = dot(push_0.viewRow2_0.xyz, localNormal_0);
    var viewNormal_0 : vec3<f32> = vec3<f32>(dot(push_0.viewRow0_0.xyz, localNormal_0), dot(push_0.viewRow1_0.xyz, localNormal_0), _S4);
    var lengthSquared_0 : f32 = dot(viewNormal_0, viewNormal_0);
    var depth_0 : f32;
    if(lengthSquared_0 > 9.999999960041972e-13f)
    {
        depth_0 = _S4 * rsqrt_0(lengthSquared_0);
    }
    else
    {
        depth_0 = 0.0f;
    }
    var output_0 : InfraredVaryings_0;
    output_0.ramp_0 = 1.0f + depth_0;
    var _S5 : vec4<f32> = (((vec4<f32>(localPosition_0, 1.0f)) * (mat4x4<f32>(push_0.modelViewProjection_0.data_0[i32(0)][i32(0)], push_0.modelViewProjection_0.data_0[i32(1)][i32(0)], push_0.modelViewProjection_0.data_0[i32(2)][i32(0)], push_0.modelViewProjection_0.data_0[i32(3)][i32(0)], push_0.modelViewProjection_0.data_0[i32(0)][i32(1)], push_0.modelViewProjection_0.data_0[i32(1)][i32(1)], push_0.modelViewProjection_0.data_0[i32(2)][i32(1)], push_0.modelViewProjection_0.data_0[i32(3)][i32(1)], push_0.modelViewProjection_0.data_0[i32(0)][i32(2)], push_0.modelViewProjection_0.data_0[i32(1)][i32(2)], push_0.modelViewProjection_0.data_0[i32(2)][i32(2)], push_0.modelViewProjection_0.data_0[i32(3)][i32(2)], push_0.modelViewProjection_0.data_0[i32(0)][i32(3)], push_0.modelViewProjection_0.data_0[i32(1)][i32(3)], push_0.modelViewProjection_0.data_0[i32(2)][i32(3)], push_0.modelViewProjection_0.data_0[i32(3)][i32(3)]))));
    var clip_0 : vec4<f32> = _S5;
    clip_0[i32(1)] = OpenIgiClipY_0(_S5.y);
    clip_0[i32(2)] = (clip_0.w - clip_0.z) * 0.5f;
    output_0.position_0 = clip_0;
    return output_0;
}

fn DecodeSrgb_0( c_0 : vec3<f32>) -> vec3<f32>
{
    return mix(pow(max((c_0 + vec3<f32>(0.05499999970197678f)) / vec3<f32>(1.0549999475479126f), vec3<f32>(0.0f, 0.0f, 0.0f)), vec3<f32>(2.40000009536743164f, 2.40000009536743164f, 2.40000009536743164f)), c_0 / vec3<f32>(12.92000007629394531f), step(c_0, vec3<f32>(0.04044999927282333f, 0.04044999927282333f, 0.04044999927282333f)));
}

fn SampledLinear_0( sampled_0 : vec3<f32>) -> vec3<f32>
{
    var _S6 : vec3<f32>;
    if(cSamplerDecodes_0 != i32(0))
    {
        _S6 = sampled_0;
    }
    else
    {
        _S6 = DecodeSrgb_0(sampled_0);
    }
    return _S6;
}

fn EncodeSrgb_0( c_1 : vec3<f32>) -> vec3<f32>
{
    return mix(vec3<f32>(1.0549999475479126f) * pow(max(c_1, vec3<f32>(0.0f, 0.0f, 0.0f)), vec3<f32>(0.4166666567325592f, 0.4166666567325592f, 0.4166666567325592f)) - vec3<f32>(0.05499999970197678f), c_1 * vec3<f32>(12.92000007629394531f), step(c_1, vec3<f32>(0.00313080009073019f, 0.00313080009073019f, 0.00313080009073019f)));
}

fn SampledDisplay_0( sampled_1 : vec3<f32>) -> vec3<f32>
{
    var _S7 : vec3<f32>;
    if(cSamplerDecodes_0 != i32(0))
    {
        _S7 = EncodeSrgb_0(sampled_1);
    }
    else
    {
        _S7 = sampled_1;
    }
    return _S7;
}

fn ToTarget_0( display_0 : vec4<f32>) -> vec4<f32>
{
    var _S8 : vec4<f32>;
    if(cTargetEncodes_0 != i32(0))
    {
        _S8 = vec4<f32>(DecodeSrgb_0(display_0.xyz), display_0.w);
    }
    else
    {
        _S8 = display_0;
    }
    return _S8;
}

struct pixelOutput_0
{
    @location(0) output_1 : vec4<f32>,
};

struct pixelInput_0
{
    @location(0) ramp_1 : f32,
};

@fragment
fn fragmentMain( _S9 : pixelInput_0, @builtin(position) position_2 : vec4<f32>) -> pixelOutput_0
{
    var width_0 : u32;
    var height_0 : u32;
    {var dim = textureDimensions((uDiffuse_0));((width_0)) = dim.x;((height_0)) = dim.y;};
    var u_0 : f32;
    if((_S9.ramp_1) <= 1.0f)
    {
        u_0 = _S9.ramp_1;
    }
    else
    {
        u_0 = 2.0f - _S9.ramp_1;
    }
    var edge_0 : f32 = 0.5f / max(f32(width_0), 1.0f);
    var sampled_2 : vec4<f32> = (textureSampleLevel((uDiffuse_0), (uDiffuseSampler_0), (vec2<f32>(clamp(u_0, edge_0, 1.0f - edge_0), 0.0f)), (0.0f)));
    var alpha_0 : f32 = saturate(sampled_2.w * push_0.control_0.y);
    if((push_0.control_0.w) != 0.0f)
    {
        var _S10 : pixelOutput_0 = pixelOutput_0( vec4<f32>(SampledLinear_0(sampled_2.xyz) * vec3<f32>(push_0.control_0.z), alpha_0) );
        return _S10;
    }
    var _S11 : pixelOutput_0 = pixelOutput_0( ToTarget_0(vec4<f32>(SampledDisplay_0(sampled_2.xyz), alpha_0)) );
    return _S11;
}

