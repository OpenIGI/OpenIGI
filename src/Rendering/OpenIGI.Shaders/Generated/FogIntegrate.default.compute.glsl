#version 430
layout(local_size_x = 8, local_size_y = 8, local_size_z = 1) in;

struct _Array_std140_matrix_float_4_4_4
{
    mat4 data[4];
};

struct _Array_std140_vector_float_4_8
{
    vec4 data[8];
};

struct _Array_std140_vector_float_4_160
{
    vec4 data[160];
};

struct FlashlightParams_std140
{
    vec4 position;
    vec4 axis;
    vec4 up;
    vec4 colour;
    vec4 cone;
    vec4 control;
};

struct _Array_std140_vector_float_4_4
{
    vec4 data[4];
};

layout(binding = 0, std140) uniform FogParams_std140
{
    layout(row_major) mat4 viewToWorld;
    layout(row_major) mat4 prevWorldToView;
    layout(row_major) _Array_std140_matrix_float_4_4_4 cascade;
    vec4 cascadeSplits;
    vec4 grid;
    vec4 unproject;
    vec4 prevUnproject;
    vec4 zParams;
    vec4 medium;
    vec4 march;
    vec4 albedo;
    vec4 sun;
    vec4 sunColour;
    vec4 ambient;
    vec4 camera;
    vec4 jitter;
    vec4 cameraWorld;
    vec4 shelter0;
    vec4 shelter1;
    vec4 shelter2;
    _Array_std140_vector_float_4_8 volumeCentre;
    _Array_std140_vector_float_4_8 volumeExtent;
    _Array_std140_vector_float_4_160 lampPosition;
    _Array_std140_vector_float_4_160 lampColour;
    FlashlightParams_std140 flashlight;
    vec4 phase;
    vec4 dust;
    vec4 records;
    vec4 fieldMisc;
    _Array_std140_vector_float_4_4 fieldOrigin;
    vec4 fieldRange;
    _Array_std140_vector_float_4_160 lampDirection;
} p;

layout(binding = 0, rgba16f) uniform restrict writeonly image3D uIntegrated;
layout(binding = 0) uniform sampler3D _925;

void main()
{
    {
        ivec2 _39 = ivec2(gl_GlobalInvocationID.xy);
        ivec3 _64 = ivec3(p.grid.xyz + vec3(0.5));
        if (!(any(greaterThanEqual(_39, _64.xy))))
        {
            vec2 uv = (vec2(_39) + vec2(0.5)) / p.grid.xy;
            float _586 = (1.0 - p.zParams.y) / p.zParams.x;
            vec2 _591 = (uv * 2.0) - vec2(1.0);
            vec3 previous = vec3((_591.x * _586) / p.unproject.x, ((-_591.y) * _586) / p.unproject.y, _586);
            int z = 0;
            float transmittance = 1.0;
            vec3 accumulated = vec3(0.0);
            float dust;
            vec3 _637;
            bool _639;
            vec3 _640;
            float _641;
            vec3 _735;
            bool _736;
            float _737;
            float _802;
            bool _803;
            vec3 _804;
            for (;;)
            {
                if (!(z < _64.z))
                {
                    break;
                }
                int _135 = z;
                vec4 sampled = texelFetch(_925, ivec4(_39, _135, 0).xyz, 0);
                vec4 medium = sampled;
                float _617 = (exp2((float(_135) + 0.5) / p.zParams.z) - p.zParams.y) / p.zParams.x;
                vec2 _622 = (uv * 2.0) - vec2(1.0);
                vec3 _635 = vec3((_622.x * _617) / p.unproject.x, ((-_622.y) * _617) / p.unproject.y, _617);
                vec3 _151 = previous;
                {
                    dust = 0.0;
                    vec4 _893 = p.flashlight.colour;
                    vec4 _894 = p.flashlight.control;
                    if (!(p.flashlight.position.w > 0.0))
                    {
                        _639 = true;
                    }
                    else
                    {
                        _639 = _894.y <= 0.0;
                    }
                    if (_639)
                    {
                        _637 = vec3(0.0);
                    }
                    else
                    {
                        {
                            vec3 _745 = _635 - p.flashlight.position.xyz;
                            float _746 = dot(_745, _745);
                            float _748 = sqrt(max(_746, 9.9999999392252902907785028219223e-09));
                            _641 = _748;
                            _640 = (-_745) / vec3(_748);
                            if (p.flashlight.position.w <= 0.0)
                            {
                                _736 = true;
                            }
                            else
                            {
                                _736 = _641 >= p.flashlight.position.w;
                            }
                            if (_736)
                            {
                                _735 = vec3(0.0);
                            }
                            else
                            {
                                vec3 _763 = -_640;
                                {
                                    float _812 = dot(p.flashlight.axis.xyz, _763);
                                    if (_812 <= p.flashlight.axis.w)
                                    {
                                        _803 = true;
                                    }
                                    else
                                    {
                                        _803 = _812 <= 0.0;
                                    }
                                    if (_803)
                                    {
                                        _802 = 0.0;
                                    }
                                    else
                                    {
                                        float _831 = (sqrt(max(1.0 - (_812 * _812), 0.0)) / _812) / max(p.flashlight.cone.x, 9.9999997473787516355514526367188e-05);
                                        float _843 = _831 - 0.86000001430511474609375;
                                        vec3 _855 = cross(p.flashlight.up.xyz, p.flashlight.axis.xyz);
                                        float _856 = length(_855);
                                        if (_856 > 9.9999997473787516355514526367188e-05)
                                        {
                                            _804 = _855 / vec3(_856);
                                        }
                                        else
                                        {
                                            _804 = vec3(1.0, 0.0, 0.0);
                                        }
                                        float _869 = atan(dot(_763, cross(p.flashlight.axis.xyz, _804)), dot(_763, _804) + 9.9999999747524270787835121154785e-07);
                                        _802 = max(((((0.550000011920928955078125 * exp(-((_831 * _831) / max((0.300000011920928955078125 * p.flashlight.cone.y) * p.flashlight.cone.y, 9.9999997473787516355514526367188e-05)))) + (0.449999988079071044921875 * (1.0 - smoothstep(p.flashlight.cone.y, 1.0, _831)))) + (0.070000000298023223876953125 * exp((_843 * _843) * (-249.9999847412109375)))) * ((1.0 + ((0.04500000178813934326171875 * _831) * sin((5.0 * _869) + 1.2999999523162841796875))) + (0.02999999932944774627685546875 * sin((33.0 * _831) + (2.0 * _869))))) + ((1.0 - smoothstep(1.0, p.flashlight.cone.z, _831)) * p.flashlight.cone.w), 0.0);
                                    }
                                }
                                if (_802 <= 0.0)
                                {
                                    _735 = vec3(0.0);
                                }
                                else
                                {
                                    float _769 = _746 / (p.flashlight.position.w * p.flashlight.position.w);
                                    float _772 = clamp(1.0 - (_769 * _769), 0.0, 1.0);
                                    if (false)
                                    {
                                        _736 = _641 > 5.0;
                                    }
                                    else
                                    {
                                        _736 = false;
                                    }
                                    if (_736)
                                    {
                                        _737 = pow(_641 * 0.20000000298023223876953125, p.flashlight.control.z);
                                    }
                                    else
                                    {
                                        _737 = 1.0;
                                    }
                                    _735 = p.flashlight.colour.xyz * (((_802 * (_772 * _772)) * _737) / max(_746, p.flashlight.colour.w));
                                }
                            }
                        }
                        if (all(lessThanEqual(_735, vec3(0.0))))
                        {
                            _637 = vec3(0.0);
                        }
                        else
                        {
                            vec3 _669 = _735 * clamp(_641 * _641, 0.0, 1.0);
                            float _671 = smoothstep(0.75, 4.0, _641);
                            float _693 = ((_894.w * clamp((dot(_669, vec3(0.2125999927520751953125, 0.715200006961822509765625, 0.072200000286102294921875)) * max(_641 * _641, 1.0)) / max(dot(_893.xyz, vec3(0.2125999927520751953125, 0.715200006961822509765625, 0.072200000286102294921875)), 9.9999997473787516355514526367188e-06), 0.0, 1.0)) * _671) * (1.0 - smoothstep(5.0, 12.0, _641));
                            dust = _693;
                            float _702 = p.medium.w * p.medium.w;
                            float _707 = max((1.0 + _702) - ((2.0 * p.medium.w) * dot(-_640, normalize(-_635))), 9.9999997473787516355514526367188e-05);
                            _637 = ((((_669 * p.albedo.xyz) * (sampled.w + _693)) * mix((1.0 - _702) / ((12.56637096405029296875 * _707) * sqrt(_707)), 0.079577468335628509521484375, 0.5)) * _894.y) * _671;
                        }
                    }
                }
                vec3 _500 = sampled.xyz + _637;
                vec4 _911 = medium;
                _911.x = _500.x;
                _911.y = _500.y;
                _911.z = _500.z;
                _911.w = _911.w + dust;
                medium = _911;
                float _517 = exp((-_911.w) * length(_635 - _151));
                float _529 = transmittance;
                vec3 accumulated_1 = accumulated + (((_911.xyz - (_911.xyz * _517)) / vec3(max(_911.w, 9.9999997473787516355514526367188e-06))) * _529);
                float transmittance_1 = _529 * _517;
                imageStore(uIntegrated, ivec3(uvec3(ivec3(_39, z))), vec4(accumulated_1, transmittance_1));
                previous = _635;
                z++;
                transmittance = transmittance_1;
                accumulated = accumulated_1;
                continue;
            }
        }
    }
}

