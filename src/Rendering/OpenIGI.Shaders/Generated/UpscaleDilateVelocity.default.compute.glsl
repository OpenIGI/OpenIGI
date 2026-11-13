#version 430
layout(local_size_x = 8, local_size_y = 8, local_size_z = 1) in;

layout(binding = 0, std140) uniform Params_std140
{
    layout(row_major) mat4 viewToWorld;
    layout(row_major) mat4 prevWorldToView;
    vec4 unproject;
    vec4 resolution;
    vec4 cameraDelta;
    vec4 prevProjection;
    vec4 jitter;
} p;

layout(binding = 0, std430) restrict buffer RWStructuredBuffer
{
    uint _member0[];
} uOccluder;

layout(binding = 0, rg16f) uniform restrict writeonly image2D uDilatedVelocity;
layout(binding = 1, r32f) uniform restrict writeonly image2D uClosestDepth;
layout(binding = 0) uniform sampler2D _717;
layout(binding = 1) uniform sampler2D _720;

mat4 spvWorkaroundRowMajor(mat4 wrap) { return wrap; }

void main()
{
    {
        ivec2 _98 = ivec2(gl_GlobalInvocationID.xy);
        ivec2 _110 = ivec2(p.resolution.xy);
        int _112 = _110.x;
        bool _7;
        if (_98.x >= _112)
        {
            _7 = true;
        }
        else
        {
            _7 = _98.y >= _110.y;
        }
        if (!(_7))
        {
            vec4 _563 = texelFetch(_717, ivec3(clamp(_98, ivec2(0), _110 - ivec2(1)), 0).xy, 0);
            float _565 = _563.x;
            float _581 = ((2.0 * p.unproject.z) * p.unproject.w) / ((p.unproject.w + p.unproject.z) - ((1.0 - (2.0 * _565)) * (p.unproject.w - p.unproject.z)));
            float _213 = abs(((1.0 - (((p.unproject.w + p.unproject.z) - (((2.0 * p.unproject.z) * p.unproject.w) / max(_581 + (2.0 * (_581 / max((0.5 * p.resolution.y) * p.unproject.y, 9.9999999747524270787835121154785e-07))), 9.9999999747524270787835121154785e-07))) / (p.unproject.w - p.unproject.z))) * 0.5) - _565);
            vec4 _619 = texelFetch(_717, ivec3(clamp(_98 + ivec2(1, 0), ivec2(0), _110 - ivec2(1)), 0).xy, 0);
            float _621 = _619.x;
            vec4 _630 = texelFetch(_717, ivec3(clamp(_98 - ivec2(1, 0), ivec2(0), _110 - ivec2(1)), 0).xy, 0);
            float _632 = _630.x;
            float bestDepth;
            ivec2 bestOffset;
            if ((((_621 + _632) * 0.5) - _565) > max(0.25 * abs(_632 - _621), _213))
            {
                if (_621 > _565)
                {
                    bestDepth = _621;
                    bestOffset = ivec2(1, 0);
                }
                else
                {
                    bestDepth = _565;
                    bestOffset = ivec2(0);
                }
                if (_632 > bestDepth)
                {
                    bestDepth = _632;
                    bestOffset = ivec2(-1, 0);
                }
            }
            else
            {
                bestDepth = _565;
                bestOffset = ivec2(0);
            }
            vec4 _641 = texelFetch(_717, ivec3(clamp(_98 + ivec2(0, 1), ivec2(0), _110 - ivec2(1)), 0).xy, 0);
            float _643 = _641.x;
            vec4 _652 = texelFetch(_717, ivec3(clamp(_98 - ivec2(0, 1), ivec2(0), _110 - ivec2(1)), 0).xy, 0);
            float _654 = _652.x;
            if ((((_643 + _654) * 0.5) - _565) > max(0.25 * abs(_654 - _643), _213))
            {
                if (_643 > bestDepth)
                {
                    bestDepth = _643;
                    bestOffset = ivec2(0, 1);
                }
                if (_654 > bestDepth)
                {
                    bestDepth = _654;
                    bestOffset = ivec2(0, -1);
                }
            }
            vec2 dilated = texelFetch(_720, ivec3(clamp(_98 + bestOffset, ivec2(0), _110 - ivec2(1)), 0).xy, 0).xy;
            imageStore(uDilatedVelocity, ivec2(gl_GlobalInvocationID.xy), vec4(dilated, 0.0, 0.0));
            imageStore(uClosestDepth, ivec2(gl_GlobalInvocationID.xy), vec4(bestDepth, 0.0, 0.0, 0.0));
            vec2 currentUv = ((vec2(_98) + vec2(0.5)) * p.resolution.zw) - (p.jitter.xy * p.resolution.zw);
            float prevViewZ;
            if (_565 > 0.0)
            {
                float _690 = ((2.0 * p.unproject.z) * p.unproject.w) / ((p.unproject.w + p.unproject.z) - ((1.0 - (2.0 * _565)) * (p.unproject.w - p.unproject.z)));
                vec2 _661 = (currentUv * 2.0) - vec2(1.0);
                prevViewZ = (vec4((vec4(vec3((_661.x * _690) / p.unproject.x, ((-_661.y) * _690) / p.unproject.y, _690), 0.0) * spvWorkaroundRowMajor(p.viewToWorld)).xyz + p.cameraDelta.xyz, 0.0) * spvWorkaroundRowMajor(p.prevWorldToView)).z;
            }
            else
            {
                prevViewZ = p.prevProjection.w;
            }
            if (prevViewZ > p.prevProjection.z)
            {
                ivec2 _396 = ivec2(floor((((currentUv - dilated) + (p.jitter.zw * p.resolution.zw)) * p.resolution.xy) - vec2(0.5)));
                uint _399 = ~floatBitsToUint(prevViewZ);
                int _400 = _396.x;
                if (_400 >= 0)
                {
                    _7 = _396.y >= 0;
                }
                else
                {
                    _7 = false;
                }
                if (_7)
                {
                    _7 = _400 < _112;
                }
                else
                {
                    _7 = false;
                }
                if (_7)
                {
                    _7 = _396.y < _110.y;
                }
                else
                {
                    _7 = false;
                }
                if (_7)
                {
                    uint _439 = atomicMax(uOccluder._member0[(_396.y * _112) + _400], _399);
                }
                ivec2 at = _396 + ivec2(1, 0);
                int _444 = at.x;
                if (_444 >= 0)
                {
                    _7 = at.y >= 0;
                }
                else
                {
                    _7 = false;
                }
                if (_7)
                {
                    _7 = _444 < _112;
                }
                else
                {
                    _7 = false;
                }
                if (_7)
                {
                    _7 = at.y < _110.y;
                }
                else
                {
                    _7 = false;
                }
                if (_7)
                {
                    uint _475 = atomicMax(uOccluder._member0[(at.y * _112) + _444], _399);
                }
                ivec2 at_1 = _396 + ivec2(0, 1);
                int _480 = at_1.x;
                if (_480 >= 0)
                {
                    _7 = at_1.y >= 0;
                }
                else
                {
                    _7 = false;
                }
                if (_7)
                {
                    _7 = _480 < _112;
                }
                else
                {
                    _7 = false;
                }
                if (_7)
                {
                    _7 = at_1.y < _110.y;
                }
                else
                {
                    _7 = false;
                }
                if (_7)
                {
                    uint _511 = atomicMax(uOccluder._member0[(at_1.y * _112) + _480], _399);
                }
                ivec2 at_2 = _396 + ivec2(1);
                int _516 = at_2.x;
                if (_516 >= 0)
                {
                    _7 = at_2.y >= 0;
                }
                else
                {
                    _7 = false;
                }
                if (_7)
                {
                    _7 = _516 < _112;
                }
                else
                {
                    _7 = false;
                }
                if (_7)
                {
                    _7 = at_2.y < _110.y;
                }
                else
                {
                    _7 = false;
                }
                if (_7)
                {
                    uint _547 = atomicMax(uOccluder._member0[(at_2.y * _112) + _516], _399);
                }
            }
        }
    }
}

