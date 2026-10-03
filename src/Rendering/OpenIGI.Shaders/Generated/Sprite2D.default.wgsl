struct SpritePush_std140_0
{
    @align(16) projectionSize_0 : vec2<f32>,
};

@binding(0) @group(3) var<uniform> push_0 : SpritePush_std140_0;
@binding(0) @group(0) var uTexture_0 : texture_2d<f32>;

@binding(1) @group(0) var uTextureSampler_0 : sampler;

@id(65) override cTargetEncodes_0 : i32 = i32(0);

@id(66) override cSamplerDecodes_0 : i32 = i32(0);

struct Sprite2DVaryings_0
{
    @builtin(position) position_0 : vec4<f32>,
    @location(0) texCoord_0 : vec2<f32>,
    @location(1) color_0 : vec4<f32>,
};

struct Sprite2DVertexInput_0
{
     position_1 : vec2<f32>,
     texCoord_1 : vec2<f32>,
     colorBgra_0 : vec4<f32>,
};

fn Sprite2DTransform_0( input_0 : Sprite2DVertexInput_0,  projectionSize_1 : vec2<f32>) -> Sprite2DVaryings_0
{
    var output_0 : Sprite2DVaryings_0;
    var _S1 : f32 = projectionSize_1.y;
    var ndc_0 : vec2<f32> = input_0.position_1 / vec2<f32>(projectionSize_1.x, abs(_S1)) * vec2<f32>(2.0f) - vec2<f32>(1.0f);
    var _S2 : f32;
    if(_S1 < 0.0f)
    {
        _S2 = - ndc_0.y;
    }
    else
    {
        _S2 = ndc_0.y;
    }
    ndc_0[i32(1)] = _S2;
    output_0.position_0 = vec4<f32>(ndc_0, 0.0f, 1.0f);
    output_0.texCoord_0 = input_0.texCoord_1;
    output_0.color_0 = input_0.colorBgra_0.zyxw;
    return output_0;
}

struct vertexInput_0
{
    @location(0) position_2 : vec2<f32>,
    @location(1) texCoord_2 : vec2<f32>,
    @location(2) colorBgra_1 : vec4<f32>,
};

@vertex
fn vertexMain( _S3 : vertexInput_0) -> Sprite2DVaryings_0
{
    var _S4 : Sprite2DVertexInput_0 = Sprite2DVertexInput_0( _S3.position_2, _S3.texCoord_2, _S3.colorBgra_1 );
    return Sprite2DTransform_0(_S4, push_0.projectionSize_0);
}

fn EncodeSrgb_0( c_0 : vec3<f32>) -> vec3<f32>
{
    return mix(vec3<f32>(1.0549999475479126f) * pow(max(c_0, vec3<f32>(0.0f, 0.0f, 0.0f)), vec3<f32>(0.4166666567325592f, 0.4166666567325592f, 0.4166666567325592f)) - vec3<f32>(0.05499999970197678f), c_0 * vec3<f32>(12.92000007629394531f), step(c_0, vec3<f32>(0.00313080009073019f, 0.00313080009073019f, 0.00313080009073019f)));
}

fn SampledDisplay_0( sampled_0 : vec3<f32>) -> vec3<f32>
{
    var _S5 : vec3<f32>;
    if(cSamplerDecodes_0 != i32(0))
    {
        _S5 = EncodeSrgb_0(sampled_0);
    }
    else
    {
        _S5 = sampled_0;
    }
    return _S5;
}

fn DecodeSrgb_0( c_1 : vec3<f32>) -> vec3<f32>
{
    return mix(pow(max((c_1 + vec3<f32>(0.05499999970197678f)) / vec3<f32>(1.0549999475479126f), vec3<f32>(0.0f, 0.0f, 0.0f)), vec3<f32>(2.40000009536743164f, 2.40000009536743164f, 2.40000009536743164f)), c_1 / vec3<f32>(12.92000007629394531f), step(c_1, vec3<f32>(0.04044999927282333f, 0.04044999927282333f, 0.04044999927282333f)));
}

fn ToTarget_0( display_0 : vec4<f32>) -> vec4<f32>
{
    var _S6 : vec4<f32>;
    if(cTargetEncodes_0 != i32(0))
    {
        _S6 = vec4<f32>(DecodeSrgb_0(display_0.xyz), display_0.w);
    }
    else
    {
        _S6 = display_0;
    }
    return _S6;
}

struct pixelOutput_0
{
    @location(0) output_1 : vec4<f32>,
};

struct pixelInput_0
{
    @location(0) texCoord_3 : vec2<f32>,
    @location(1) color_1 : vec4<f32>,
};

@fragment
fn fragmentMain( _S7 : pixelInput_0, @builtin(position) position_3 : vec4<f32>) -> pixelOutput_0
{
    var texel_0 : vec4<f32> = (textureSample((uTexture_0), (uTextureSampler_0), (_S7.texCoord_3)));
    var _S8 : pixelOutput_0 = pixelOutput_0( ToTarget_0(vec4<f32>(SampledDisplay_0(texel_0.xyz), texel_0.w) * _S7.color_1) );
    return _S8;
}

