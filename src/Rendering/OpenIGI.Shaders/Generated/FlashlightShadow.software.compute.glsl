#version 430
layout(local_size_x = 8, local_size_y = 8, local_size_z = 1) in;

layout(binding = 0, std140) uniform Params_std140
{
    layout(row_major) mat4 viewToWorld;
    vec4 unproject;
    vec4 resolution;
    vec4 cameraFrame;
    vec4 sourceLens;
    vec4 axisCull;
    vec4 reach;
    vec4 records;
} p;

layout(binding = 0, rg16f) uniform restrict writeonly image2D uRaw;
layout(binding = 0) uniform sampler2D _941;
layout(binding = 1) uniform sampler2D _944;

mat4 spvWorkaroundRowMajor(mat4 wrap) { return wrap; }

void main()
{
    bool _767 = false;
    {
        ivec2 _54 = ivec2(gl_GlobalInvocationID.xy);
        bool _7;
        if (_54.x >= int(p.resolution.x))
        {
            _7 = true;
        }
        else
        {
            _7 = _54.y >= int(p.resolution.y);
        }
        if (!(_7))
        {
            ivec2 _85 = ivec3(_54, 0).xy;
            vec4 sampled = texelFetch(_941, _85, 0);
            float _91 = sampled.x;
            if (_91 <= 0.0)
            {
                imageStore(uRaw, ivec2(gl_GlobalInvocationID.xy), vec4(1.0, 0.0, 0.0, 0.0));
            }
            else
            {
                float _628 = ((2.0 * p.unproject.z) * p.unproject.w) / ((p.unproject.w + p.unproject.z) - ((1.0 - (2.0 * _91)) * (p.unproject.w - p.unproject.z)));
                vec2 _605 = (((vec2(_54) + vec2(0.5)) * p.resolution.zw) * 2.0) - vec2(1.0);
                vec3 _615 = vec3((_605.x * _628) / p.unproject.x, ((-_605.y) * _628) / p.unproject.y, _628);
                vec3 toLens = p.sourceLens.xyz - _615;
                float _160 = length(toLens);
                vec3 toLensDir = toLens / vec3(max(_160, 9.9999997473787516355514526367188e-06));
                if (_160 >= p.reach.x)
                {
                    _7 = true;
                }
                else
                {
                    _7 = dot(p.axisCull.xyz, -toLensDir) <= p.axisCull.w;
                }
                if (_7)
                {
                    imageStore(uRaw, ivec2(gl_GlobalInvocationID.xy), vec4(1.0, 0.0, 0.0, 0.0));
                }
                else
                {
                    vec3 normalView = (spvWorkaroundRowMajor(p.viewToWorld) * vec4(normalize((texelFetch(_944, _85, 0).xyz * 2.0) - vec3(1.0)), 0.0)).xyz;
                    if (dot(normalView, toLensDir) <= 0.0)
                    {
                        imageStore(uRaw, ivec2(gl_GlobalInvocationID.xy), vec4(1.0, 0.0, 0.0, 0.0));
                    }
                    else
                    {
                        vec3 random;
                        if (p.reach.w > 0.5)
                        {
                            uvec3 _653 = (uvec3(gl_GlobalInvocationID.xy, uint(p.cameraFrame.w) ^ 861445437u) * uvec3(1664525u)) + uvec3(1013904223u);
                            _653.x = _653.x + (_653.y * _653.z);
                            _653.y = _653.y + (_653.z * _653.x);
                            _653.z = _653.z + (_653.x * _653.y);
                            uvec3 _680 = _653 ^ (_653 >> uvec3(16u));
                            _680.x = _680.x + (_680.y * _680.z);
                            _680.y = _680.y + (_680.z * _680.x);
                            _680.z = _680.z + (_680.x * _680.y);
                            random = vec3(_680 >> uvec3(8u)) * 5.9604644775390625e-08;
                        }
                        else
                        {
                            uvec3 _719 = (uvec3(gl_GlobalInvocationID.xy, 1759714724u) * uvec3(1664525u)) + uvec3(1013904223u);
                            _719.x = _719.x + (_719.y * _719.z);
                            _719.y = _719.y + (_719.z * _719.x);
                            _719.z = _719.z + (_719.x * _719.y);
                            uvec3 _746 = _719 ^ (_719 >> uvec3(16u));
                            _746.x = _746.x + (_746.y * _746.z);
                            _746.y = _746.y + (_746.z * _746.x);
                            _746.z = _746.z + (_746.x * _746.y);
                            random = fract((vec3(_746 >> uvec3(8u)) * 5.9604644775390625e-08) + (vec3(0.75487768650054931640625, 0.56984031200408935546875, 0.61803400516510009765625) * p.cameraFrame.w));
                        }
                        vec3 helper;
                        if (abs(p.axisCull.y) < 0.89999997615814208984375)
                        {
                            helper = vec3(0.0, 1.0, 0.0);
                        }
                        else
                        {
                            helper = vec3(1.0, 0.0, 0.0);
                        }
                        vec3 _365 = normalize(cross(helper, p.axisCull.xyz));
                        float spread = sqrt(random.x) * p.sourceLens.w;
                        float around = 6.283185482025146484375 * random.y;
                        vec3 origin = _615 + (normalView * (0.02999999932944774627685546875 + (0.001000000047497451305389404296875 * _615.z)));
                        vec3 toTarget = ((p.sourceLens.xyz + (_365 * (spread * cos(around)))) + (cross(p.axisCull.xyz, _365) * (spread * sin(around)))) - origin;
                        float _395 = length(toTarget);
                        vec3 direction = toTarget / vec3(max(_395, 9.9999997473787516355514526367188e-06));
                        float segment = _395 - p.reach.y;
                        if (segment <= 0.0199999995529651641845703125)
                        {
                            imageStore(uRaw, ivec2(gl_GlobalInvocationID.xy), vec4(1.0, 0.0, 0.0, 0.0));
                        }
                        else
                        {
                            _767 = false;
                            float t;
                            bool _768;
                            {
                                t = 0.0;
                                int _779 = int(p.reach.z + 0.5);
                                ivec2 _785 = ivec2(p.resolution.xy) - ivec2(1);
                                int _769 = 0;
                                bool _770;
                                bool _771;
                                for (;;)
                                {
                                    bool _788_ladder_break = false;
                                    {
                                        if (!(_769 < _779))
                                        {
                                            _788_ladder_break = true;
                                        }
                                        else
                                        {
                                            float _797 = (float(_769) + random.z) / float(_779);
                                            float _799 = (segment * _797) * _797;
                                            vec3 _801 = origin + (direction * _799);
                                            if (_801.z <= p.unproject.z)
                                            {
                                                _788_ladder_break = true;
                                            }
                                            else
                                            {
                                                vec2 _878 = (vec2((_801.x * p.unproject.x) / _801.z, ((-_801.y) * p.unproject.y) / _801.z) * 0.5) + vec2(0.5);
                                                if (any(lessThan(_878, vec2(0.0))))
                                                {
                                                    _770 = true;
                                                }
                                                else
                                                {
                                                    _770 = any(greaterThan(_878, vec2(1.0)));
                                                }
                                                if (_770)
                                                {
                                                    _788_ladder_break = true;
                                                }
                                                else
                                                {
                                                    vec4 _828 = texelFetch(_941, ivec3(clamp(ivec2(_878 * p.resolution.xy), ivec2(0), _785), 0).xy, 0);
                                                    float _829 = _828.x;
                                                    if (!(_829 <= 0.0))
                                                    {
                                                        float _891 = ((2.0 * p.unproject.z) * p.unproject.w) / ((p.unproject.w + p.unproject.z) - ((1.0 - (2.0 * _829)) * (p.unproject.w - p.unproject.z)));
                                                        float _835 = _801.z - _891;
                                                        if (_835 > (0.0199999995529651641845703125 + (0.00200000009499490261077880859375 * _891)))
                                                        {
                                                            _771 = _835 < max(0.300000011920928955078125, 0.0599999986588954925537109375 * _891);
                                                        }
                                                        else
                                                        {
                                                            _771 = false;
                                                        }
                                                        if (_771)
                                                        {
                                                            t = _799;
                                                            _767 = true;
                                                            _768 = true;
                                                            _788_ladder_break = true;
                                                        }
                                                    }
                                                }
                                            }
                                        }
                                    }
                                    if (_788_ladder_break)
                                    {
                                        break;
                                    }
                                    _769++;
                                    continue;
                                }
                                if (!(_767))
                                {
                                    _767 = true;
                                    _768 = false;
                                }
                            }
                            float ratio;
                            if (_768)
                            {
                                ratio = clamp(t / max(_395 - t, 0.0500000007450580596923828125), 0.00999999977648258209228515625, 64.0);
                            }
                            else
                            {
                                ratio = 0.0;
                            }
                            float _16;
                            if (_768)
                            {
                                _16 = 0.0;
                            }
                            else
                            {
                                _16 = 1.0;
                            }
                            imageStore(uRaw, ivec2(gl_GlobalInvocationID.xy), vec4(_16, ratio, 0.0, 0.0));
                        }
                    }
                }
            }
        }
    }
}

