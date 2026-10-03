struct SdfTextPush_std140_0
{
    @align(16) projectionSize_0 : vec2<f32>,
    @align(16) range_0 : vec4<f32>,
    @align(16) outline_0 : vec4<f32>,
    @align(16) glow_0 : vec4<f32>,
    @align(16) glowWidth_0 : vec4<f32>,
};

@binding(0) @group(3) var<uniform> push_0 : SdfTextPush_std140_0;
@binding(0) @group(0) var uTexture_0 : texture_2d<f32>;

@binding(1) @group(0) var uTextureSampler_0 : sampler;

@id(65) override cTargetEncodes_0 : i32 = i32(0);

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

fn ScreenPixelRange_0( texCoord_3 : vec2<f32>) -> f32
{
    var width_0 : u32;
    var height_0 : u32;
    {var dim = textureDimensions((uTexture_0));((width_0)) = dim.x;((height_0)) = dim.y;};
    return max(0.5f * dot(vec2<f32>(push_0.range_0.x, push_0.range_0.x) / vec2<f32>(f32(width_0), f32(height_0)), vec2<f32>(1.0f, 1.0f) / (fwidth((texCoord_3)))), 1.0f);
}

fn Hash_0( v_0 : vec2<f32>) -> f32
{
    return fract(sin(dot(v_0, vec2<f32>(127.09999847412109375f, 311.70001220703125f))) * 43758.546875f);
}

fn DecodeSrgb_0( c_0 : vec3<f32>) -> vec3<f32>
{
    return mix(pow(max((c_0 + vec3<f32>(0.05499999970197678f)) / vec3<f32>(1.0549999475479126f), vec3<f32>(0.0f, 0.0f, 0.0f)), vec3<f32>(2.40000009536743164f, 2.40000009536743164f, 2.40000009536743164f)), c_0 / vec3<f32>(12.92000007629394531f), step(c_0, vec3<f32>(0.04044999927282333f, 0.04044999927282333f, 0.04044999927282333f)));
}

fn ToTarget_0( display_0 : vec4<f32>) -> vec4<f32>
{
    var _S5 : vec4<f32>;
    if(cTargetEncodes_0 != i32(0))
    {
        _S5 = vec4<f32>(DecodeSrgb_0(display_0.xyz), display_0.w);
    }
    else
    {
        _S5 = display_0;
    }
    return _S5;
}

struct pixelOutput_0
{
    @location(0) output_1 : vec4<f32>,
};

struct pixelInput_0
{
    @location(0) texCoord_4 : vec2<f32>,
    @location(1) color_1 : vec4<f32>,
};

@fragment
fn fragmentMain( _S6 : pixelInput_0, @builtin(position) position_3 : vec4<f32>) -> pixelOutput_0
{
    var d_0 : f32 = (textureSample((uTexture_0), (uTextureSampler_0), (_S6.texCoord_4))).w;
    var range_1 : f32 = ScreenPixelRange_0(_S6.texCoord_4);
    var dissolve_0 : f32 = clamp(push_0.range_0.y, 0.0f, 1.0f);
    var grain_0 : f32 = Hash_0(floor(_S6.texCoord_4 * vec2<f32>(1024.0f)) + vec2<f32>(push_0.range_0.z * 17.0f, push_0.range_0.z * 5.0f));
    var _S7 : bool = dissolve_0 > 0.0f;
    var _S8 : bool;
    if(_S7)
    {
        _S8 = grain_0 < dissolve_0;
    }
    else
    {
        _S8 = false;
    }
    if(_S8)
    {
        discard;
    }
    var ember_0 : f32;
    if(_S7)
    {
        ember_0 = 1.0f - smoothstep(dissolve_0, dissolve_0 + 0.15999999642372131f, grain_0);
    }
    else
    {
        ember_0 = 0.0f;
    }
    var fill_0 : f32 = clamp((d_0 - 0.5f - dissolve_0 * 0.2199999988079071f) * range_1 + 0.5f, 0.0f, 1.0f);
    var result_0 : vec4<f32> = vec4<f32>(0.0f, 0.0f, 0.0f, 0.0f);
    if((push_0.glow_0.w) > 0.0f)
    {
        _S8 = (push_0.glowWidth_0.x) > 0.0f;
    }
    else
    {
        _S8 = false;
    }
    if(_S8)
    {
        result_0 = vec4<f32>(push_0.glow_0.xyz, smoothstep(0.5f - push_0.glowWidth_0.x, 0.5f, d_0) * push_0.glow_0.w);
    }
    if((push_0.outline_0.w) > 0.0f)
    {
        result_0 = mix(result_0, vec4<f32>(push_0.outline_0.xyz, 1.0f), vec4<f32>(clamp((d_0 - (0.5f - push_0.outline_0.w)) * range_1 + 0.5f, 0.0f, 1.0f)));
    }
    result_0 = mix(result_0, vec4<f32>(mix(_S6.color_1.xyz, vec3<f32>(1.0f, 0.86000001430511475f, 0.62000000476837158f), vec3<f32>((ember_0 * 0.89999997615814209f))), 1.0f), vec4<f32>(fill_0));
    result_0[i32(3)] = result_0[i32(3)] * _S6.color_1.w;
    var _S9 : pixelOutput_0 = pixelOutput_0( ToTarget_0(result_0) );
    return _S9;
}

