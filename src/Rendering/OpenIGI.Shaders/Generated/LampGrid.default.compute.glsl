#version 430
layout(local_size_x = 64, local_size_y = 1, local_size_z = 1) in;

struct _Array_std140_vector_float_4_160
{
    vec4 data[160];
};

layout(binding = 0, std140) uniform Params_std140
{
    vec4 shape;
    vec4 unproject;
    vec4 depth;
    _Array_std140_vector_float_4_160 positionRadius;
} p;

layout(binding = 0, std430) restrict buffer RWStructuredBuffer
{
    uint _member0[];
} uLampGrid;

void main()
{
    {
        uint tilesX = uint(p.shape.x);
        uint tilesY = uint(p.shape.y);
        uint slices = uint(p.shape.z);
        int _91 = int(p.shape.w + 0.5);
        if (gl_GlobalInvocationID.x == 0u)
        {
            uLampGrid._member0[0] = tilesX;
            uLampGrid._member0[1] = tilesY;
            uLampGrid._member0[2] = slices;
            uLampGrid._member0[3] = floatBitsToUint(p.depth.x);
            uLampGrid._member0[4] = floatBitsToUint(p.depth.y);
            uLampGrid._member0[5] = floatBitsToUint(p.depth.z);
            uLampGrid._member0[6] = floatBitsToUint(p.depth.w);
            uLampGrid._member0[7] = 0u;
        }
        uint _143 = tilesX * tilesY;
        if (!(gl_GlobalInvocationID.x >= (_143 * slices)))
        {
            uint tx = gl_GlobalInvocationID.x % tilesX;
            uint ty = (gl_GlobalInvocationID.x / tilesX) % tilesY;
            uint sz = gl_GlobalInvocationID.x / _143;
            float _158 = float(tx) / p.depth.z;
            float _164 = float(tx + 1u) / p.depth.z;
            float _168 = float(ty) / p.depth.w;
            float _173 = float(ty + 1u) / p.depth.w;
            float _7;
            if (sz == 0u)
            {
                _7 = 0.0;
            }
            else
            {
                _7 = p.depth.x * exp2(float(sz) / p.depth.y);
            }
            uint _190 = sz + 1u;
            float _8;
            if (_190 >= slices)
            {
                _8 = max(p.unproject.w, p.depth.x) * 1.0099999904632568359375;
            }
            else
            {
                _8 = p.depth.x * exp2(float(_190) / p.depth.y);
            }
            int corner = 0;
            vec3 lo = vec3(1000000015047466219876688855040.0);
            vec3 hi = vec3(-1000000015047466219876688855040.0);
            float _16;
            float _17;
            float _18;
            for (;;)
            {
                if (!(corner < 8))
                {
                    break;
                }
                if ((corner & 1) != 0)
                {
                    _16 = _164;
                }
                else
                {
                    _16 = _158;
                }
                if ((corner & 2) != 0)
                {
                    _17 = _173;
                }
                else
                {
                    _17 = _168;
                }
                if ((corner & 4) != 0)
                {
                    _18 = _8;
                }
                else
                {
                    _18 = _7;
                }
                vec3 _386 = vec3((((_16 * 2.0) - 1.0) * _18) / p.unproject.x, ((1.0 - (_17 * 2.0)) * _18) / p.unproject.y, _18);
                corner++;
                lo = min(lo, _386);
                hi = max(hi, _386);
                continue;
            }
            vec3 margin = ((abs(lo) + abs(hi)) * 0.001000000047497451305389404296875) + vec3(0.001000000047497451305389404296875);
            vec3 lo_1 = lo - margin;
            vec3 hi_1 = hi + margin;
            uint _304 = gl_GlobalInvocationID.x * 32u;
            int i = 0;
            uint count = 0u;
            for (;;)
            {
                if (!(i < _91))
                {
                    break;
                }
                float reach = (p.positionRadius.data[i].w * 1.00100004673004150390625) + 0.001000000047497451305389404296875;
                vec3 nearest = clamp(p.positionRadius.data[i].xyz, lo_1, hi_1) - p.positionRadius.data[i].xyz;
                if (dot(nearest, nearest) < (reach * reach))
                {
                    if (count < 31u)
                    {
                        uLampGrid._member0[(_304 + 9u) + count] = uint(i);
                    }
                    count++;
                }
                i++;
                continue;
            }
            if (count > 31u)
            {
                count = 4294967295u;
            }
            uLampGrid._member0[8u + _304] = count;
        }
    }
}

