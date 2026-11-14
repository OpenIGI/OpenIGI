#version 430
layout(local_size_x = 8, local_size_y = 8, local_size_z = 1) in;

struct _Array_std140_vector_float_4_4
{
    vec4 data[4];
};

struct _Array_std140_vector_float_4_7
{
    vec4 data[7];
};

struct _Array_std140_float4
{
    float data[4];
};

float _10460;

layout(binding = 0, std140) uniform SLANG_ParameterGroup_Globals_std140
{
    _Array_std140_vector_float_4_4 uClipmapOrigin;
    _Array_std140_vector_float_4_7 uSkySh;
    vec4 uUnproject;
    vec4 uPrevUnproject;
    vec3 uViewToWorld0;
    vec3 uViewToWorld1;
    vec3 uViewToWorld2;
    vec3 uCameraToCache;
    vec3 uTraceOrigin;
    vec3 uPrevWorldToView0;
    vec3 uPrevWorldToView1;
    vec3 uPrevWorldToView2;
    vec3 uPrevCameraToCache;
    vec3 uColourWorldToView0;
    vec3 uColourWorldToView1;
    vec3 uColourWorldToView2;
    vec3 uColourCameraToCache;
    vec3 uAtlasSize;
    vec3 uLookupMin;
    vec3 uSkyColour;
    vec3 uGroundSun;
    _Array_std140_float4 uClipmapRange;
    int uShadowLevels;
    int uShadowResolution;
    int uLookupBuckets;
    float uLookupCell;
    int uScreenWidth;
    int uScreenHeight;
    int uProbeStep;
    float uMaxRayIntensity;
    int uScreenTraceSteps;
    float uScreenTraceDistance;
    float uScreenThickness;
    int uSkyShValid;
    float uBounceScale;
    int uDebugSource;
    vec4 uTerrainMap0;
    vec4 uTerrainMap1;
    vec4 uShelterMap0;
    vec4 uShelterMap1;
    vec4 uShelterMap2;
    int uHasHistory;
    int uFrameIndex;
    int uMaxAccumulated;
    int uHasVelocity;
    int uGatherPass;
} Globals;

layout(binding = 1, std430) restrict buffer RWStructuredBuffer
{
    ivec2 _member0[];
} uCells;

layout(binding = 2, std430) restrict buffer uCellPages
{
    int _member0[];
} uCellPages_1;

layout(binding = 0, std430) restrict readonly buffer StructuredBuffer
{
    vec4 _member0[];
} uPages;

layout(binding = 0, rgba16f) uniform restrict writeonly image2D uIndirect;
layout(binding = 1, rgba16f) uniform restrict writeonly image2D uPosition;
layout(binding = 5) uniform sampler2D _10465;
layout(binding = 6) uniform sampler2D _10467;
layout(binding = 8) uniform sampler2D _10470;
layout(binding = 9) uniform sampler2D _10472;
layout(binding = 3) uniform sampler3D _10474;
layout(binding = 12) uniform sampler2D _10477;
layout(binding = 0) uniform sampler2D _10480;
layout(binding = 1) uniform sampler2D _10482;
layout(binding = 2) uniform sampler2D _10484;
layout(binding = 4) uniform sampler2D _10486;
layout(binding = 13) uniform sampler2D _10489;
layout(binding = 10) uniform sampler2D _10492;
layout(binding = 11) uniform sampler2D _10494;
layout(binding = 7) uniform sampler2D _10497;

void main()
{
    bool _7789 = false;
    bool _7691 = false;
    bool _7593 = false;
    bool _7495 = false;
    bool _7397 = false;
    bool _7299 = false;
    bool _7201 = false;
    bool _6228 = false;
    bool _6118 = false;
    bool _6037 = false;
    {
        ivec2 _173 = ivec2(gl_GlobalInvocationID.xy);
        ivec2 grid = (ivec2(Globals.uScreenWidth, Globals.uScreenHeight) + ivec2(Globals.uProbeStep - 1)) / ivec2(Globals.uProbeStep);
        bool ownMotion;
        if (_173.x >= grid.x)
        {
            ownMotion = true;
        }
        else
        {
            ownMotion = _173.y >= grid.y;
        }
        if (!(ownMotion))
        {
            vec3 world;
            vec3 viewPosition;
            vec3 worldNormal;
            bool _4589;
            {
                world = vec3(0.0);
                viewPosition = vec3(0.0);
                worldNormal = vec3(0.0, 0.0, 1.0);
                uint _4661 = uint(Globals.uFrameIndex);
                uint _4662 = _4661 & 7u;
                vec2 _4618 = (vec2(min(((_173 * ivec2(Globals.uProbeStep)) + min(ivec2(floor(((vec2(float(_4662), float((((_4661 & 1u) << uint(2)) | (_4661 & 2u)) | ((_4662 >> uint(2)) & 1u))) + vec2(0.5)) * vec2(0.125)) * float(Globals.uProbeStep))), ivec2(Globals.uProbeStep - 1))), (ivec2(Globals.uScreenWidth, Globals.uScreenHeight) - ivec2(1)))) + vec2(0.5)) / vec2(float(Globals.uScreenWidth), float(Globals.uScreenHeight));
                vec4 _4688 = textureLod(_10465, _4618, 0.0);
                float _4689 = _4688.x;
                if (_4689 <= 0.0)
                {
                    _4589 = false;
                }
                else
                {
                    float _4708 = ((2.0 * Globals.uUnproject.z) * Globals.uUnproject.w) / ((Globals.uUnproject.w + Globals.uUnproject.z) - ((1.0 - (2.0 * _4689)) * (Globals.uUnproject.w - Globals.uUnproject.z)));
                    vec2 _4711 = (_4618 * 2.0) - vec2(1.0);
                    vec3 _4723 = vec3((_4711.x * _4708) / Globals.uUnproject.x, ((-1.0) * (_4711.y * _4708)) / Globals.uUnproject.y, _4708);
                    viewPosition = _4723;
                    vec3 _4641 = ((Globals.uCameraToCache + (Globals.uViewToWorld0 * _4723.x)) + (Globals.uViewToWorld1 * _4723.y)) + (Globals.uViewToWorld2 * _4723.z);
                    world = _4641;
                    vec3 _4651 = normalize((textureLod(_10467, _4618, 0.0).xyz * 2.0) - vec3(1.0));
                    worldNormal = _4651;
                    vec3 _4725;
                    {
                        vec3 _4731 = -normalize(_4641 - Globals.uCameraToCache);
                        float _4732 = dot(_4651, _4731);
                        if (_4732 >= 0.0)
                        {
                            _4725 = _4651;
                        }
                        else
                        {
                            vec3 _4769;
                            {
                                vec2 _4783 = vec2(1.0) / vec2(float(Globals.uScreenWidth), float(Globals.uScreenHeight));
                                vec2 _4785 = vec2(_4783.x, 0.0);
                                vec2 _4786 = _4618 + _4785;
                                float _4868 = ((2.0 * Globals.uUnproject.z) * Globals.uUnproject.w) / ((Globals.uUnproject.w + Globals.uUnproject.z) - ((1.0 - (2.0 * textureLod(_10465, _4786, 0.0).x)) * (Globals.uUnproject.w - Globals.uUnproject.z)));
                                vec2 _4871 = (_4786 * 2.0) - vec2(1.0);
                                vec3 _4883 = vec3((_4871.x * _4868) / Globals.uUnproject.x, ((-1.0) * (_4871.y * _4868)) / Globals.uUnproject.y, _4868);
                                vec2 _4790 = _4618 - _4785;
                                float _4905 = ((2.0 * Globals.uUnproject.z) * Globals.uUnproject.w) / ((Globals.uUnproject.w + Globals.uUnproject.z) - ((1.0 - (2.0 * textureLod(_10465, _4790, 0.0).x)) * (Globals.uUnproject.w - Globals.uUnproject.z)));
                                vec2 _4908 = (_4790 * 2.0) - vec2(1.0);
                                vec3 _4920 = vec3((_4908.x * _4905) / Globals.uUnproject.x, ((-1.0) * (_4908.y * _4905)) / Globals.uUnproject.y, _4905);
                                vec2 _4795 = vec2(0.0, _4783.y);
                                vec2 _4796 = _4618 + _4795;
                                float _4942 = ((2.0 * Globals.uUnproject.z) * Globals.uUnproject.w) / ((Globals.uUnproject.w + Globals.uUnproject.z) - ((1.0 - (2.0 * textureLod(_10465, _4796, 0.0).x)) * (Globals.uUnproject.w - Globals.uUnproject.z)));
                                vec2 _4945 = (_4796 * 2.0) - vec2(1.0);
                                vec3 _4957 = vec3((_4945.x * _4942) / Globals.uUnproject.x, ((-1.0) * (_4945.y * _4942)) / Globals.uUnproject.y, _4942);
                                vec2 _4800 = _4618 - _4795;
                                float _4979 = ((2.0 * Globals.uUnproject.z) * Globals.uUnproject.w) / ((Globals.uUnproject.w + Globals.uUnproject.z) - ((1.0 - (2.0 * textureLod(_10465, _4800, 0.0).x)) * (Globals.uUnproject.w - Globals.uUnproject.z)));
                                vec2 _4982 = (_4800 * 2.0) - vec2(1.0);
                                vec3 _4994 = vec3((_4982.x * _4979) / Globals.uUnproject.x, ((-1.0) * (_4982.y * _4979)) / Globals.uUnproject.y, _4979);
                                vec3 _4770;
                                if (abs(_4883.z - _4723.z) < abs(_4723.z - _4920.z))
                                {
                                    _4770 = _4883 - _4723;
                                }
                                else
                                {
                                    _4770 = _4723 - _4920;
                                }
                                vec3 _4771;
                                if (abs(_4957.z - _4723.z) < abs(_4723.z - _4994.z))
                                {
                                    _4771 = _4957 - _4723;
                                }
                                else
                                {
                                    _4771 = _4723 - _4994;
                                }
                                vec3 _4829 = _4770;
                                vec3 _4831 = cross(_4829, _4771);
                                float _4832 = dot(_4831, _4831);
                                if (_4832 <= 9.9999996826552253889678874634872e-21)
                                {
                                    _4769 = vec3(0.0);
                                }
                                else
                                {
                                    vec3 _4837 = _4831 * inversesqrt(_4832);
                                    if (dot(_4837, _4723) > 0.0)
                                    {
                                        _4770 = -_4837;
                                    }
                                    else
                                    {
                                        _4770 = _4837;
                                    }
                                    _4769 = _4770;
                                }
                            }
                            vec3 _4750 = ((Globals.uViewToWorld0 * _4769.x) + (Globals.uViewToWorld1 * _4769.y)) + (Globals.uViewToWorld2 * _4769.z);
                            bool _4726;
                            if (dot(_4750, _4750) < 0.5)
                            {
                                _4726 = true;
                            }
                            else
                            {
                                _4726 = dot(_4651, _4750) <= 0.0;
                            }
                            if (_4726)
                            {
                                _4725 = -_4651;
                            }
                            else
                            {
                                _4725 = normalize(_4651 - (_4731 * (2.0 * _4732)));
                            }
                        }
                    }
                    worldNormal = _4725;
                    _4589 = true;
                }
            }
            vec3 nMin;
            float nCount;
            if (Globals.uGatherPass == 0)
            {
                if (_4589)
                {
                    vec2 _4996;
                    {
                        if (Globals.uFrameIndex <= 0)
                        {
                            _4996 = vec2(0.5);
                        }
                        else
                        {
                            float _5011 = float(((uint(_173.x) * 73856093u) ^ (uint(_173.y) * 19349663u)) & 65535u);
                            uint _5014 = uint(Globals.uFrameIndex & 65535);
                            uint _5031 = (_5014 << 16u) | (_5014 >> 16u);
                            uint _5036 = ((_5031 & 1431655765u) << 1u) | ((_5031 & 2863311530u) >> 1u);
                            uint _5041 = ((_5036 & 858993459u) << 2u) | ((_5036 & 3435973836u) >> 2u);
                            uint _5046 = ((_5041 & 252645135u) << 4u) | ((_5041 & 4042322160u) >> 4u);
                            _4996 = fract(vec2(fract(float(_5014) * 0.61803400516510009765625), float(((_5046 & 16711935u) << 8u) | ((_5046 & 4278255360u) >> 8u)) * 2.3283064365386962890625e-10) + vec2(_5011 * 1.52587890625e-05, fract(_5011 * 2.4689239580766297876834869384766e-05)));
                        }
                    }
                    vec3 _5055;
                    {
                        vec3 _10327;
                        vec3 _10328;
                        vec3 _10329;
                        {
                            vec3 _10342 = worldNormal;
                            if (worldNormal.z < (-0.99999988079071044921875))
                            {
                                _10327 = vec3(0.0, -1.0, 0.0);
                                _10328 = vec3(-1.0, 0.0, 0.0);
                                _10329 = _10342;
                            }
                            else
                            {
                                float _5125 = 1.0 / (1.0 + worldNormal.z);
                                float _5127 = -worldNormal.x;
                                float _5130 = (_5127 * worldNormal.y) * _5125;
                                _10327 = vec3(1.0 - ((worldNormal.x * worldNormal.x) * _5125), _5130, _5127);
                                _10328 = vec3(_5130, 1.0 - ((worldNormal.y * worldNormal.y) * _5125), -worldNormal.y);
                                _10329 = _10342;
                            }
                        }
                        vec3 _10428 = _10327;
                        vec3 _10429 = _10328;
                        vec3 _10430 = _10329;
                        vec3 _10362 = vec3(dot(Globals.uViewToWorld0, worldNormal), dot(Globals.uViewToWorld1, worldNormal), dot(Globals.uViewToWorld2, worldNormal));
                        vec3 _10363 = world;
                        bool _5092;
                        if (Globals.uShadowLevels > 0)
                        {
                            _5092 = Globals.uLookupBuckets > 0;
                        }
                        else
                        {
                            _5092 = false;
                        }
                        if (!_5092)
                        {
                            _5055 = vec3(0.0);
                        }
                        else
                        {
                            vec3 _10368 = vec3(0.0);
                            float _10369 = 0.0;
                            vec3 _10370 = vec3(0.0);
                            float _10371 = 0.0;
                            vec3 _10372 = vec3(0.0);
                            float _10373 = 0.0;
                            float _10374 = 0.0;
                            int _5058 = 0;
                            int _5170;
                            vec3 _5171;
                            bool _5172;
                            vec3 _5173;
                            float _5174;
                            vec3 _5175;
                            bool _5176;
                            vec3 _5177;
                            vec2 _5179;
                            float _5181;
                            vec3 _5182;
                            vec3 _5183;
                            bool _5436;
                            bool _5437;
                            vec3 _5438;
                            float _5441;
                            bool _5442;
                            vec3 _6019;
                            float _6038;
                            bool _6041;
                            bool _6042;
                            float _6044;
                            float _6119;
                            bool _6121;
                            float _6229;
                            bool _6230;
                            float _6348;
                            bool _6373;
                            float _6411;
                            float _6436;
                            bool _6461;
                            float _6499;
                            bool _6529;
                            ivec2 _6534;
                            bool _6545;
                            bool _6546;
                            vec3 _6548[4];
                            float _6551;
                            ivec2 _6916;
                            vec3 _7013;
                            vec3 _7032;
                            vec3 _7084;
                            vec3 _7135;
                            vec3 _7154;
                            bool _7155;
                            float _7157;
                            vec3 _7161;
                            bool _7204;
                            float _7300;
                            bool _7302;
                            float _7398;
                            bool _7400;
                            float _7496;
                            bool _7498;
                            float _7594;
                            bool _7596;
                            float _7692;
                            bool _7694;
                            float _7790;
                            bool _7792;
                            bool _7888;
                            bool _7954;
                            float _7992;
                            bool _8017;
                            bool _8033;
                            bool _8099;
                            float _8137;
                            bool _8162;
                            bool _8182;
                            ivec2 _8187;
                            bool _8198;
                            bool _8199;
                            vec3 _8201[4];
                            float _8204;
                            ivec2 _8569;
                            bool _8671;
                            bool _8677;
                            ivec2 _8678;
                            bool _8682;
                            bool _8691;
                            bool _8692;
                            bool _8693;
                            bool _8694;
                            bool _8695;
                            ivec2 _8925;
                            vec3 _9022;
                            vec3 _9040;
                            vec3 _9058;
                            for (;;)
                            {
                                if (!(_5058 < 16))
                                {
                                    break;
                                }
                                vec3 _10239 = vec3(0.0);
                                float _10240 = 0.0;
                                vec3 _10241 = vec3(0.0);
                                float _10242 = 0.0;
                                bool _10243 = false;
                                bool _10244 = false;
                                bool _10245 = false;
                                vec2 _5207 = (vec2(float(_5058 - 4 * (_5058 / 4)), float(_5058 / 4)) + _4996) * vec2(0.25);
                                float _5209 = 6.283185482025146484375 * _5207.x;
                                float _5211 = sqrt(_5207.y);
                                float _5215 = sqrt(max(0.0, 1.0 - (_5211 * _5211)));
                                vec3 _5422 = ((_10428 * (_5215 * cos(_5209))) + (_10429 * (_5215 * sin(_5209)))) + (_10430 * _5211);
                                if (Globals.uScreenTraceSteps > 0)
                                {
                                    vec3 _5434 = vec3(dot(Globals.uViewToWorld0, _5422), dot(Globals.uViewToWorld1, _5422), dot(Globals.uViewToWorld2, _5422));
                                    {
                                        _5173 = vec3(0.0);
                                        if (Globals.uScreenTraceSteps <= 0)
                                        {
                                            _5437 = true;
                                        }
                                        else
                                        {
                                            _5437 = _5434.z == 0.0;
                                        }
                                        if (_5437)
                                        {
                                            _5436 = false;
                                        }
                                        else
                                        {
                                            vec3 _5460 = viewPosition + (_10362 * 0.0199999995529651641845703125);
                                            vec3 _5464 = _5460 + (_5434 * Globals.uScreenTraceDistance);
                                            if (_5464.z < (Globals.uUnproject.z + 0.00999999977648258209228515625))
                                            {
                                                _5438 = _5460 + (_5434 * (((Globals.uUnproject.z + 0.00999999977648258209228515625) - _5460.z) / _5434.z));
                                            }
                                            else
                                            {
                                                _5438 = _5464;
                                            }
                                            if (_5438.z <= Globals.uUnproject.z)
                                            {
                                                _5436 = false;
                                            }
                                            else
                                            {
                                                float _5827 = _5460.z;
                                                vec2 _5838 = (vec2((_5460.x * Globals.uUnproject.x) / _5827, ((-1.0) * (_5460.y * Globals.uUnproject.y)) / _5827) * 0.5) + vec2(0.5);
                                                vec2 _5493 = ((vec2((_5438.x * Globals.uUnproject.x) / _5438.z, ((-1.0) * (_5438.y * Globals.uUnproject.y)) / _5438.z) * 0.5) + vec2(0.5)) - _5838;
                                                int _5508 = int(clamp(length(_5493 / (vec2(1.0) / vec2(float(Globals.uScreenWidth), float(Globals.uScreenHeight)))), 1.0, float(Globals.uScreenTraceSteps)));
                                                float _5510 = 1.0 / float(_5508);
                                                float _5527 = fract(52.98291778564453125 * fract(dot((_5838 * vec2(float(Globals.uScreenWidth), float(Globals.uScreenHeight))) + vec2(5.588237762451171875 * float(Globals.uFrameIndex & 63)), vec2(0.067110560834407806396484375, 0.005837149918079376220703125))));
                                                float _5439 = 0.0;
                                                int _5440 = 1;
                                                for (;;)
                                                {
                                                    if (!(_5440 <= 256))
                                                    {
                                                        _5441 = -1.0;
                                                        break;
                                                    }
                                                    if (_5440 > _5508)
                                                    {
                                                        _5441 = -1.0;
                                                        break;
                                                    }
                                                    float _5544 = _5510 * (float(_5440) + _5527);
                                                    if (_5544 > 1.0)
                                                    {
                                                        _5441 = -1.0;
                                                        break;
                                                    }
                                                    vec2 _5549 = _5838 + (_5493 * _5544);
                                                    if (any(lessThan(_5549, vec2(0.0))))
                                                    {
                                                        _5437 = true;
                                                    }
                                                    else
                                                    {
                                                        _5437 = any(greaterThan(_5549, vec2(1.0)));
                                                    }
                                                    if (_5437)
                                                    {
                                                        _5441 = -1.0;
                                                        break;
                                                    }
                                                    float _5560 = _5460.z;
                                                    float _5879 = ((2.0 * Globals.uUnproject.z) * Globals.uUnproject.w) / ((Globals.uUnproject.w + Globals.uUnproject.z) - ((1.0 - (2.0 * textureLod(_10465, _5549, 0.0).x)) * (Globals.uUnproject.w - Globals.uUnproject.z)));
                                                    vec2 _5882 = (_5549 * 2.0) - vec2(1.0);
                                                    vec3 _5894 = vec3(_10460, _10460, _5879);
                                                    float _5568 = _5894.z;
                                                    float _5569 = (1.0 / mix(1.0 / _5560, 1.0 / _5438.z, _5544)) - _5568;
                                                    if (_5569 > 0.0)
                                                    {
                                                        _5442 = _5569 < (Globals.uScreenThickness + (_5568 * 0.0199999995529651641845703125));
                                                    }
                                                    else
                                                    {
                                                        _5442 = false;
                                                    }
                                                    if (_5442)
                                                    {
                                                        _5441 = _5439;
                                                        float _5443 = _5544;
                                                        int _5444 = 0;
                                                        for (;;)
                                                        {
                                                            if (!(_5444 < 4))
                                                            {
                                                                break;
                                                            }
                                                            float _5591 = _5443;
                                                            float _5593 = (_5441 + _5591) * 0.5;
                                                            vec2 _5599 = _5838 + (_5493 * _5593);
                                                            float _5916 = ((2.0 * Globals.uUnproject.z) * Globals.uUnproject.w) / ((Globals.uUnproject.w + Globals.uUnproject.z) - ((1.0 - (2.0 * textureLod(_10465, _5599, 0.0).x)) * (Globals.uUnproject.w - Globals.uUnproject.z)));
                                                            vec2 _5919 = (_5599 * 2.0) - vec2(1.0);
                                                            if (((1.0 / mix(1.0 / _5560, 1.0 / _5438.z, _5593)) - vec3(_10460, _10460, _5916).z) > 0.0)
                                                            {
                                                                _5443 = _5593;
                                                            }
                                                            else
                                                            {
                                                                _5441 = _5593;
                                                            }
                                                            _5444++;
                                                            continue;
                                                        }
                                                        _5441 = _5443;
                                                        break;
                                                    }
                                                    _5439 = _5544;
                                                    _5440++;
                                                    continue;
                                                }
                                                if (_5441 <= 0.0)
                                                {
                                                    _5436 = false;
                                                }
                                                else
                                                {
                                                    vec2 _5629 = _5838 + (_5493 * _5441);
                                                    vec3 _5638 = (textureLod(_10467, _5629, 0.0).xyz * 2.0) - vec3(1.0);
                                                    vec3 _5943 = vec3(dot(Globals.uViewToWorld0, _5638), dot(Globals.uViewToWorld1, _5638), dot(Globals.uViewToWorld2, _5638));
                                                    if (dot(_5943, _5434) >= 0.0)
                                                    {
                                                        _5436 = false;
                                                    }
                                                    else
                                                    {
                                                        float _5965 = ((2.0 * Globals.uUnproject.z) * Globals.uUnproject.w) / ((Globals.uUnproject.w + Globals.uUnproject.z) - ((1.0 - (2.0 * textureLod(_10465, _5629, 0.0).x)) * (Globals.uUnproject.w - Globals.uUnproject.z)));
                                                        vec2 _5968 = (_5629 * 2.0) - vec2(1.0);
                                                        vec3 _5980 = vec3((_5968.x * _5965) / Globals.uUnproject.x, ((-1.0) * (_5968.y * _5965)) / Globals.uUnproject.y, _5965);
                                                        vec3 _5663 = ((Globals.uCameraToCache + (Globals.uViewToWorld0 * _5980.x)) + (Globals.uViewToWorld1 * _5980.y)) + (Globals.uViewToWorld2 * _5980.z);
                                                        float _5666 = _5663.x;
                                                        float _5674 = _5663.y;
                                                        float _5682 = _5663.z;
                                                        vec3 _5687 = ((Globals.uColourWorldToView0 * (_5666 - Globals.uColourCameraToCache.x)) + (Globals.uColourWorldToView1 * (_5674 - Globals.uColourCameraToCache.y))) + (Globals.uColourWorldToView2 * (_5682 - Globals.uColourCameraToCache.z));
                                                        if (Globals.uHasHistory == 0)
                                                        {
                                                            _5437 = true;
                                                        }
                                                        else
                                                        {
                                                            _5437 = _5687.z <= Globals.uUnproject.z;
                                                        }
                                                        if (_5437)
                                                        {
                                                            _5436 = false;
                                                        }
                                                        else
                                                        {
                                                            float _5988 = _5687.z;
                                                            vec2 _5999 = (vec2((_5687.x * Globals.uUnproject.x) / _5988, ((-1.0) * (_5687.y * Globals.uUnproject.y)) / _5988) * 0.5) + vec2(0.5);
                                                            if (any(lessThan(_5999, vec2(0.0))))
                                                            {
                                                                _5437 = true;
                                                            }
                                                            else
                                                            {
                                                                _5437 = any(greaterThan(_5999, vec2(1.0)));
                                                            }
                                                            if (_5437)
                                                            {
                                                                _5436 = false;
                                                            }
                                                            else
                                                            {
                                                                vec3 _5732 = ((Globals.uPrevWorldToView0 * (_5666 - Globals.uPrevCameraToCache.x)) + (Globals.uPrevWorldToView1 * (_5674 - Globals.uPrevCameraToCache.y))) + (Globals.uPrevWorldToView2 * (_5682 - Globals.uPrevCameraToCache.z));
                                                                float _5733 = _5732.z;
                                                                if (_5733 <= Globals.uUnproject.z)
                                                                {
                                                                    _5436 = false;
                                                                }
                                                                else
                                                                {
                                                                    float _6007 = _5732.z;
                                                                    vec4 _5766 = texelFetch(_10470, ivec3(clamp(ivec2(((vec2((_5732.x * Globals.uUnproject.x) / _6007, ((-1.0) * (_5732.y * Globals.uUnproject.y)) / _6007) * 0.5) + vec2(0.5)) * vec2(float(Globals.uScreenWidth), float(Globals.uScreenHeight))) / ivec2(Globals.uProbeStep), ivec2(0), ((ivec2(Globals.uScreenWidth, Globals.uScreenHeight) + ivec2(Globals.uProbeStep - 1)) / ivec2(Globals.uProbeStep)) - ivec2(1)), 0).xy, 0);
                                                                    if (_5766.w <= 0.0)
                                                                    {
                                                                        _5437 = true;
                                                                    }
                                                                    else
                                                                    {
                                                                        _5437 = abs(dot((_5766.xyz + Globals.uPrevCameraToCache) - _5663, normalize(((Globals.uViewToWorld0 * _5943.x) + (Globals.uViewToWorld1 * _5943.y)) + (Globals.uViewToWorld2 * _5943.z)))) > max(0.100000001490116119384765625, 2.0 * (((2.0 * _5733) * float(Globals.uProbeStep)) / (Globals.uUnproject.y * float(Globals.uScreenHeight))));
                                                                    }
                                                                    if (_5437)
                                                                    {
                                                                        _5436 = false;
                                                                    }
                                                                    else
                                                                    {
                                                                        vec4 _5810 = textureLod(_10472, _5999, 0.0);
                                                                        if (_5810.w < 0.5)
                                                                        {
                                                                            _5436 = false;
                                                                        }
                                                                        else
                                                                        {
                                                                            vec3 _5816 = _5810.xyz;
                                                                            float _6026 = max(_5810.x, max(_5810.y, _5810.z));
                                                                            if (_6026 > Globals.uMaxRayIntensity)
                                                                            {
                                                                                _6019 = _5816 * (Globals.uMaxRayIntensity / _6026);
                                                                            }
                                                                            else
                                                                            {
                                                                                _6019 = _5816;
                                                                            }
                                                                            _5173 = _6019;
                                                                            _5436 = true;
                                                                        }
                                                                    }
                                                                }
                                                            }
                                                        }
                                                    }
                                                }
                                            }
                                        }
                                    }
                                    if (_5436)
                                    {
                                        _5170 = 1;
                                        _5171 = _5173;
                                    }
                                    else
                                    {
                                        _5170 = 0;
                                        _5171 = vec3(0.0);
                                    }
                                    _5172 = _5436;
                                }
                                else
                                {
                                    _5172 = false;
                                    _5170 = 0;
                                    _5171 = vec3(0.0);
                                }
                                if (!_5172)
                                {
                                    _6037 = false;
                                    {
                                        _5175 = _10363;
                                        if (Globals.uShadowLevels <= 0)
                                        {
                                            _6037 = true;
                                            _6038 = 1.0;
                                        }
                                        else
                                        {
                                            vec3 _6061 = _10363 + ((worldNormal * Globals.uClipmapOrigin.data[0].w) * 1.5);
                                            float _6072 = (Globals.uClipmapOrigin.data[0].w * float(Globals.uShadowResolution)) * float(1 << (Globals.uShadowLevels - 1));
                                            int _6039 = 0;
                                            float _6040 = 0.0;
                                            for (;;)
                                            {
                                                if (_6039 < 64)
                                                {
                                                    _6041 = _6040 < _6072;
                                                }
                                                else
                                                {
                                                    _6041 = false;
                                                }
                                                if (!_6041)
                                                {
                                                    break;
                                                }
                                                vec3 _6089 = _6061 + (_5422 * _6040);
                                                _6118 = false;
                                                {
                                                    _6042 = false;
                                                    _6044 = Globals.uClipmapOrigin.data[0].w;
                                                    int _6120 = 0;
                                                    for (;;)
                                                    {
                                                        bool _6132_ladder_break = false;
                                                        {
                                                            if (!(_6120 < Globals.uShadowLevels))
                                                            {
                                                                _6132_ladder_break = true;
                                                            }
                                                            else
                                                            {
                                                                vec3 _6147 = (_6089 - Globals.uClipmapOrigin.data[_6120].xyz) / vec3(Globals.uClipmapOrigin.data[_6120].w);
                                                                if (any(lessThan(_6147, vec3(0.5))))
                                                                {
                                                                    _6121 = true;
                                                                }
                                                                else
                                                                {
                                                                    _6121 = any(greaterThan(_6147, vec3(float(Globals.uShadowResolution) - 0.5, float(Globals.uShadowResolution) - 0.5, float(Globals.uShadowResolution) - 0.5)));
                                                                }
                                                                if (!(_6121))
                                                                {
                                                                    _6042 = true;
                                                                    _6044 = Globals.uClipmapOrigin.data[_6120].w;
                                                                    _6118 = true;
                                                                    _6119 = ((textureLod(_10474, vec3(_6147.x / float(Globals.uShadowResolution), _6147.y / float(Globals.uShadowResolution), ((float(_6120) * float(Globals.uShadowResolution)) + _6147.z) / float(Globals.uShadowResolution * Globals.uShadowLevels)), 0.0).x * 2.0) - 1.0) * Globals.uClipmapRange.data[_6120];
                                                                    _6132_ladder_break = true;
                                                                }
                                                            }
                                                        }
                                                        if (_6132_ladder_break)
                                                        {
                                                            break;
                                                        }
                                                        _6120++;
                                                        continue;
                                                    }
                                                    if (!(_6118))
                                                    {
                                                        _6118 = true;
                                                        _6119 = 1000000.0;
                                                    }
                                                }
                                                if (!_6042)
                                                {
                                                    _6037 = true;
                                                    _6038 = 1.0;
                                                    break;
                                                }
                                                float _6223 = (0.5 * _6044) * clamp(_6040 / max(2.0 * _6044, 9.9999999747524270787835121154785e-07), 0.0, 1.0);
                                                float _6045 = max((_6040 + _6119) - _6223, 0.0);
                                                if (_6119 < _6223)
                                                {
                                                    _5175 = _6061 + (_5422 * _6045);
                                                    _6037 = true;
                                                    _6038 = 0.0;
                                                    break;
                                                }
                                                _6039++;
                                                _6040 += max(_6119, _6044 * 0.25);
                                                continue;
                                            }
                                            if (!(_6037))
                                            {
                                                _6037 = true;
                                                _6038 = 1.0;
                                            }
                                        }
                                    }
                                    bool _5240 = _6038 <= 0.5;
                                    vec3 _5242 = world + (worldNormal * 0.100000001490116119384765625);
                                    vec3 _5245 = _5242 + Globals.uTraceOrigin;
                                    _6228 = false;
                                    {
                                        if (Globals.uTerrainMap0.w < 0.5)
                                        {
                                            _6230 = true;
                                        }
                                        else
                                        {
                                            _6230 = _5422.z > (-0.00999999977648258209228515625);
                                        }
                                        if (_6230)
                                        {
                                            _6230 = true;
                                        }
                                        else
                                        {
                                            _6230 = Globals.uTerrainMap0.z <= 0.0;
                                        }
                                        if (_6230)
                                        {
                                            _6228 = true;
                                            _6229 = -1.0;
                                        }
                                        else
                                        {
                                            float _6259 = 0.5 / Globals.uTerrainMap0.z;
                                            float _6231 = 0.0;
                                            int _6232 = 1;
                                            for (;;)
                                            {
                                                if (!(_6232 <= 64))
                                                {
                                                    break;
                                                }
                                                float _6271 = min(float(_6232) * _6259, 32.0);
                                                vec3 _6273 = _5245 + (_5422 * _6271);
                                                float _10215 = Globals.uTerrainMap1.z;
                                                float _10216 = Globals.uTerrainMap1.w;
                                                vec2 _10217 = Globals.uTerrainMap1.xy;
                                                ivec2 _6372 = ivec2(floor((_6273.xy - Globals.uTerrainMap0.xy) * Globals.uTerrainMap0.z));
                                                if (Globals.uTerrainMap0.w > 0.5)
                                                {
                                                    _6373 = _6372.x >= 0;
                                                }
                                                else
                                                {
                                                    _6373 = false;
                                                }
                                                if (_6373)
                                                {
                                                    _6373 = _6372.y >= 0;
                                                }
                                                else
                                                {
                                                    _6373 = false;
                                                }
                                                if (_6373)
                                                {
                                                    _6373 = _6372.x < int(_10217.x);
                                                }
                                                else
                                                {
                                                    _6373 = false;
                                                }
                                                if (_6373)
                                                {
                                                    _6373 = _6372.y < int(_10217.y);
                                                }
                                                else
                                                {
                                                    _6373 = false;
                                                }
                                                if (_6373)
                                                {
                                                    vec4 _6358 = texelFetch(_10477, ivec3(_6372, 0).xy, 0);
                                                    float _6423 = floor((_6358.z * 255.0) + 0.5) + (floor((_6358.y * 255.0) + 0.5) * 256.0);
                                                    if (_6423 < 0.5)
                                                    {
                                                        _6411 = -1000000015047466219876688855040.0;
                                                    }
                                                    else
                                                    {
                                                        _6411 = _10215 + ((_6423 - 1.0) * _10216);
                                                    }
                                                    _6348 = _6411;
                                                }
                                                else
                                                {
                                                    _6348 = -1000000015047466219876688855040.0;
                                                }
                                                if (_6273.z <= _6348)
                                                {
                                                    float _6234 = _6231;
                                                    float _6235 = _6271;
                                                    int _6236 = 0;
                                                    for (;;)
                                                    {
                                                        if (!(_6236 < 4))
                                                        {
                                                            break;
                                                        }
                                                        float _6289 = _6235;
                                                        float _6291 = (_6234 + _6289) * 0.5;
                                                        vec3 _6293 = _5245 + (_5422 * _6291);
                                                        float _10227 = Globals.uTerrainMap1.z;
                                                        float _10228 = Globals.uTerrainMap1.w;
                                                        vec2 _10229 = Globals.uTerrainMap1.xy;
                                                        ivec2 _6460 = ivec2(floor((_6293.xy - Globals.uTerrainMap0.xy) * Globals.uTerrainMap0.z));
                                                        if (Globals.uTerrainMap0.w > 0.5)
                                                        {
                                                            _6461 = _6460.x >= 0;
                                                        }
                                                        else
                                                        {
                                                            _6461 = false;
                                                        }
                                                        if (_6461)
                                                        {
                                                            _6461 = _6460.y >= 0;
                                                        }
                                                        else
                                                        {
                                                            _6461 = false;
                                                        }
                                                        if (_6461)
                                                        {
                                                            _6461 = _6460.x < int(_10229.x);
                                                        }
                                                        else
                                                        {
                                                            _6461 = false;
                                                        }
                                                        if (_6461)
                                                        {
                                                            _6461 = _6460.y < int(_10229.y);
                                                        }
                                                        else
                                                        {
                                                            _6461 = false;
                                                        }
                                                        if (_6461)
                                                        {
                                                            vec4 _6446 = texelFetch(_10477, ivec3(_6460, 0).xy, 0);
                                                            float _6511 = floor((_6446.z * 255.0) + 0.5) + (floor((_6446.y * 255.0) + 0.5) * 256.0);
                                                            if (_6511 < 0.5)
                                                            {
                                                                _6499 = -1000000015047466219876688855040.0;
                                                            }
                                                            else
                                                            {
                                                                _6499 = _10227 + ((_6511 - 1.0) * _10228);
                                                            }
                                                            _6436 = _6499;
                                                        }
                                                        else
                                                        {
                                                            _6436 = -1000000015047466219876688855040.0;
                                                        }
                                                        if (_6293.z <= _6436)
                                                        {
                                                            _6235 = _6291;
                                                        }
                                                        else
                                                        {
                                                            _6234 = _6291;
                                                        }
                                                        _6236++;
                                                        continue;
                                                    }
                                                    _6228 = true;
                                                    _6229 = _6235;
                                                    break;
                                                }
                                                if (_6271 >= 32.0)
                                                {
                                                    break;
                                                }
                                                _6231 = _6271;
                                                _6232++;
                                                continue;
                                            }
                                            if (!(_6228))
                                            {
                                                _6228 = true;
                                                _6229 = -1.0;
                                            }
                                        }
                                    }
                                    if (_6229 >= 0.0)
                                    {
                                        if (!_5240)
                                        {
                                            _5176 = true;
                                        }
                                        else
                                        {
                                            _5176 = length(_5175 - world) > _6229;
                                        }
                                    }
                                    else
                                    {
                                        _5176 = false;
                                    }
                                    if (_5176)
                                    {
                                        vec3 _5263 = _5242 + (_5422 * _6229);
                                        float _5269 = 0.5 * Globals.uClipmapOrigin.data[0].w;
                                        {
                                            _5177 = vec3(0.0);
                                            if (Globals.uLookupBuckets <= 0)
                                            {
                                                _6529 = false;
                                            }
                                            else
                                            {
                                                ivec3 _6568 = ivec3(floor((_5263 - Globals.uLookupMin) / vec3(Globals.uLookupCell)));
                                                int _6915 = int((((uint(_6568.x) * 2376512323u) ^ (uint(_6568.y) * 3625334849u)) ^ (uint(_6568.z) * 3407524639u)) & uint(Globals.uLookupBuckets - 1));
                                                uvec2 _6573 = uvec2(textureSize(_10480, 0));
                                                vec2 _6580 = vec2(float(_6573.x), float(_6573.y));
                                                int _6532 = 0;
                                                float _6533 = 0.0;
                                                for (;;)
                                                {
                                                    if (!(_6532 <= uCells._member0[Globals.uLookupBuckets].y))
                                                    {
                                                        break;
                                                    }
                                                    bool _6594 = _6532 == 0;
                                                    if (_6594)
                                                    {
                                                        _6534 = uCells._member0[_6915];
                                                    }
                                                    else
                                                    {
                                                        int _6920 = uCells._member0[Globals.uLookupBuckets].x + ((_6532 - 1) * 6);
                                                        float _6937 = intBitsToFloat(uCellPages_1._member0[_6920 + 3]) + _5269;
                                                        vec3 _6938 = _5263 - vec3(intBitsToFloat(uCellPages_1._member0[_6920]), intBitsToFloat(uCellPages_1._member0[_6920 + 1]), intBitsToFloat(uCellPages_1._member0[_6920 + 2]));
                                                        if (dot(_6938, _6938) > (_6937 * _6937))
                                                        {
                                                            _6916 = ivec2(0);
                                                        }
                                                        else
                                                        {
                                                            _6916 = ivec2(uCellPages_1._member0[_6920 + 4], uCellPages_1._member0[_6920 + 5]);
                                                        }
                                                        _6534 = _6916;
                                                    }
                                                    int _6535 = 0;
                                                    float _6536 = _6533;
                                                    for (;;)
                                                    {
                                                        bool _6605_ladder_break = false;
                                                        {
                                                            if (!(_6535 < _6534.y))
                                                            {
                                                                _6605_ladder_break = true;
                                                            }
                                                            else
                                                            {
                                                                int _6616 = _6534.x + _6535;
                                                                int _6619 = uCellPages_1._member0[_6616] * 10;
                                                                if (!((uPages._member0[_6619 + 5].w > 0.5) == _6594))
                                                                {
                                                                    int _6628 = _6619 + 9;
                                                                    vec3 _6632 = _5263 - uPages._member0[_6628].xyz;
                                                                    float _6634 = uPages._member0[_6628].w + _5269;
                                                                    if (!(dot(_6632, _6632) > (_6634 * _6634)))
                                                                    {
                                                                        int _6954 = uCellPages_1._member0[_6616] * 10;
                                                                        vec4 _6543 = uPages._member0[_6954];
                                                                        vec4 _6544 = uPages._member0[_6954 + 1];
                                                                        int _6960 = _6954 + 2;
                                                                        int _6963 = _6954 + 3;
                                                                        int _6966 = _6954 + 4;
                                                                        int _6972 = _6954 + 6;
                                                                        int _6975 = _6954 + 7;
                                                                        int _6978 = _6954 + 8;
                                                                        vec3 _6985 = _5263 - vec3(uPages._member0[_6972].w, uPages._member0[_6975].w, uPages._member0[_6978].w);
                                                                        vec3 _6998 = vec3(dot(uPages._member0[_6972].xyz, _6985), dot(uPages._member0[_6975].xyz, _6985), dot(uPages._member0[_6978].xyz, _6985)) - uPages._member0[_6960].xyz;
                                                                        vec2 _7002 = max(vec2(uPages._member0[_6960].w, uPages._member0[_6963].w), vec2(9.9999999747524270787835121154785e-07));
                                                                        vec2 _6538 = _7002;
                                                                        vec2 _6537 = vec2(dot(_6998, uPages._member0[_6963].xyz), dot(_6998, uPages._member0[_6966].xyz)) / _7002;
                                                                        float _6539 = -dot(_6998, uPages._member0[_6954 + 5].xyz);
                                                                        float _6540 = uPages._member0[_6966].w;
                                                                        float _6643 = dot(uPages._member0[_6954 + 5].xyz, vec3(uPages._member0[_6972].z, uPages._member0[_6975].z, uPages._member0[_6978].z));
                                                                        if (!(_6643 < 0.100000001490116119384765625))
                                                                        {
                                                                            float _6648 = Globals.uClipmapOrigin.data[0].w * 0.25;
                                                                            vec2 _6651 = vec2(_6648) / _6538;
                                                                            if (abs(_6537.x) > (1.0 + _6651.x))
                                                                            {
                                                                                _6545 = true;
                                                                            }
                                                                            else
                                                                            {
                                                                                _6545 = abs(_6537.y) > (1.0 + _6651.y);
                                                                            }
                                                                            if (!(_6545))
                                                                            {
                                                                                if (_6539 < (Globals.uClipmapOrigin.data[0].w * (-0.25)))
                                                                                {
                                                                                    _6546 = true;
                                                                                }
                                                                                else
                                                                                {
                                                                                    _6546 = _6539 > (_6540 + _6648);
                                                                                }
                                                                                if (!(_6546))
                                                                                {
                                                                                    vec2 _6695 = (_6544.xy + (((clamp(_6537, vec2(-1.0), vec2(1.0)) * 0.5) + vec2(0.5)) * _6544.zw)) - vec2(0.5);
                                                                                    vec2 _6696 = floor(_6695);
                                                                                    vec2 _6697 = _6695 - _6696;
                                                                                    vec2 _6703 = _6543.zw / max(_6544.zw, vec2(9.9999999747524270787835121154785e-07));
                                                                                    float _6707 = _6539 / max(_6540, 9.9999999747524270787835121154785e-07);
                                                                                    float _6711 = _5269 / max(0.5 * _6540, 9.9999999747524270787835121154785e-07);
                                                                                    vec4 _6547 = vec4(0.0);
                                                                                    int _6550 = 0;
                                                                                    for (;;)
                                                                                    {
                                                                                        if (!(_6550 < 4))
                                                                                        {
                                                                                            break;
                                                                                        }
                                                                                        vec2 _6726 = vec2(float(_6550 & 1), float(_6550 >> 1));
                                                                                        vec2 _6729 = (_6696 + _6726) + vec2(0.5);
                                                                                        vec2 _6730 = _6729 / _6580;
                                                                                        if (textureLod(_10482, _6730, 0.0).w >= 0.5)
                                                                                        {
                                                                                            _6551 = 1.0;
                                                                                        }
                                                                                        else
                                                                                        {
                                                                                            _6551 = 0.0;
                                                                                        }
                                                                                        vec2 _6759 = mix(vec2(1.0) - _6697, _6697, _6726);
                                                                                        _6547[_6550] = ((_6759.x * _6759.y) * _6551) * (1.0 - clamp((abs(_6707 - textureLod(_10484, _6730, 0.0).x) - _6711) / (0.25 * _6711), 0.0, 1.0));
                                                                                        _6548[_6550] = textureLod(_10486, (floor(_6543.xy + ((_6729 - _6544.xy) * _6703)) + vec2(0.5)) / Globals.uAtlasSize.xy, 0.0).xyz;
                                                                                        _6550++;
                                                                                        continue;
                                                                                    }
                                                                                    float _6806 = ((_6547.x + _6547.y) + _6547.z) + _6547.w;
                                                                                    float _6807 = (_6643 * _6643) * _6806;
                                                                                    if (!(_6807 <= 0.0))
                                                                                    {
                                                                                        _5177 += (max(((((_6548[0] * _6547.x) + (_6548[1] * _6547.y)) + (_6548[2] * _6547.z)) + (_6548[3] * _6547.w)) / vec3(_6806), vec3(0.0)) * _6807);
                                                                                        _6536 += _6807;
                                                                                    }
                                                                                }
                                                                            }
                                                                        }
                                                                    }
                                                                }
                                                            }
                                                        }
                                                        if (_6605_ladder_break)
                                                        {
                                                            break;
                                                        }
                                                        _6535++;
                                                        continue;
                                                    }
                                                    _6532++;
                                                    _6533 = _6536;
                                                    continue;
                                                }
                                                if (_6533 <= 0.0)
                                                {
                                                    _5177 = vec3(0.0);
                                                    _6529 = false;
                                                }
                                                else
                                                {
                                                    _5177 /= vec3(_6533);
                                                    _6529 = true;
                                                }
                                            }
                                        }
                                        if (_6529)
                                        {
                                            vec3 _5275 = _5177 * Globals.uBounceScale;
                                            float _7020 = max(_5275.x, max(_5275.y, _5275.z));
                                            if (_7020 > Globals.uMaxRayIntensity)
                                            {
                                                _7013 = _5275 * (Globals.uMaxRayIntensity / _7020);
                                            }
                                            else
                                            {
                                                _7013 = _5275;
                                            }
                                            _10239 = _7013;
                                            _10240 = 1.0;
                                            _10243 = true;
                                            _5172 = true;
                                            _5170 = 2;
                                            _5171 = _5275;
                                        }
                                        _5176 = false;
                                    }
                                    else
                                    {
                                        _5176 = _5240;
                                    }
                                    if (_5172)
                                    {
                                        _5174 = 1.0;
                                    }
                                    else
                                    {
                                        if (_5176)
                                        {
                                            float _5302 = 1.5 * Globals.uClipmapOrigin.data[0].w;
                                            {
                                                _7201 = false;
                                                {
                                                    _7155 = false;
                                                    _7157 = Globals.uClipmapOrigin.data[0].w;
                                                    int _7203 = 0;
                                                    for (;;)
                                                    {
                                                        bool _7215_ladder_break = false;
                                                        {
                                                            if (!(_7203 < Globals.uShadowLevels))
                                                            {
                                                                _7215_ladder_break = true;
                                                            }
                                                            else
                                                            {
                                                                vec3 _7230 = (_5175 - Globals.uClipmapOrigin.data[_7203].xyz) / vec3(Globals.uClipmapOrigin.data[_7203].w);
                                                                if (any(lessThan(_7230, vec3(0.5))))
                                                                {
                                                                    _7204 = true;
                                                                }
                                                                else
                                                                {
                                                                    _7204 = any(greaterThan(_7230, vec3(float(Globals.uShadowResolution) - 0.5, float(Globals.uShadowResolution) - 0.5, float(Globals.uShadowResolution) - 0.5)));
                                                                }
                                                                if (!(_7204))
                                                                {
                                                                    _7155 = true;
                                                                    _7157 = Globals.uClipmapOrigin.data[_7203].w;
                                                                    _7201 = true;
                                                                    _7215_ladder_break = true;
                                                                }
                                                            }
                                                        }
                                                        if (_7215_ladder_break)
                                                        {
                                                            break;
                                                        }
                                                        _7203++;
                                                        continue;
                                                    }
                                                    if (!(_7201))
                                                    {
                                                        _7201 = true;
                                                    }
                                                }
                                                if (!_7155)
                                                {
                                                    _7154 = vec3(0.0);
                                                }
                                                else
                                                {
                                                    float _7171 = 0.5 * _7157;
                                                    vec3 _7172 = vec3(_7171, 0.0, 0.0);
                                                    vec3 _7173 = _5175 + _7172;
                                                    _7299 = false;
                                                    {
                                                        int _7301 = 0;
                                                        for (;;)
                                                        {
                                                            bool _7313_ladder_break = false;
                                                            {
                                                                if (!(_7301 < Globals.uShadowLevels))
                                                                {
                                                                    _7313_ladder_break = true;
                                                                }
                                                                else
                                                                {
                                                                    vec3 _7328 = (_7173 - Globals.uClipmapOrigin.data[_7301].xyz) / vec3(Globals.uClipmapOrigin.data[_7301].w);
                                                                    if (any(lessThan(_7328, vec3(0.5))))
                                                                    {
                                                                        _7302 = true;
                                                                    }
                                                                    else
                                                                    {
                                                                        _7302 = any(greaterThan(_7328, vec3(float(Globals.uShadowResolution) - 0.5, float(Globals.uShadowResolution) - 0.5, float(Globals.uShadowResolution) - 0.5)));
                                                                    }
                                                                    if (!(_7302))
                                                                    {
                                                                        _7299 = true;
                                                                        _7300 = ((textureLod(_10474, vec3(_7328.x / float(Globals.uShadowResolution), _7328.y / float(Globals.uShadowResolution), ((float(_7301) * float(Globals.uShadowResolution)) + _7328.z) / float(Globals.uShadowResolution * Globals.uShadowLevels)), 0.0).x * 2.0) - 1.0) * Globals.uClipmapRange.data[_7301];
                                                                        _7313_ladder_break = true;
                                                                    }
                                                                }
                                                            }
                                                            if (_7313_ladder_break)
                                                            {
                                                                break;
                                                            }
                                                            _7301++;
                                                            continue;
                                                        }
                                                        if (!(_7299))
                                                        {
                                                            _7299 = true;
                                                            _7300 = 1000000.0;
                                                        }
                                                    }
                                                    vec3 _7175 = _5175 - _7172;
                                                    _7397 = false;
                                                    {
                                                        int _7399 = 0;
                                                        for (;;)
                                                        {
                                                            bool _7411_ladder_break = false;
                                                            {
                                                                if (!(_7399 < Globals.uShadowLevels))
                                                                {
                                                                    _7411_ladder_break = true;
                                                                }
                                                                else
                                                                {
                                                                    vec3 _7426 = (_7175 - Globals.uClipmapOrigin.data[_7399].xyz) / vec3(Globals.uClipmapOrigin.data[_7399].w);
                                                                    if (any(lessThan(_7426, vec3(0.5))))
                                                                    {
                                                                        _7400 = true;
                                                                    }
                                                                    else
                                                                    {
                                                                        _7400 = any(greaterThan(_7426, vec3(float(Globals.uShadowResolution) - 0.5, float(Globals.uShadowResolution) - 0.5, float(Globals.uShadowResolution) - 0.5)));
                                                                    }
                                                                    if (!(_7400))
                                                                    {
                                                                        _7397 = true;
                                                                        _7398 = ((textureLod(_10474, vec3(_7426.x / float(Globals.uShadowResolution), _7426.y / float(Globals.uShadowResolution), ((float(_7399) * float(Globals.uShadowResolution)) + _7426.z) / float(Globals.uShadowResolution * Globals.uShadowLevels)), 0.0).x * 2.0) - 1.0) * Globals.uClipmapRange.data[_7399];
                                                                        _7411_ladder_break = true;
                                                                    }
                                                                }
                                                            }
                                                            if (_7411_ladder_break)
                                                            {
                                                                break;
                                                            }
                                                            _7399++;
                                                            continue;
                                                        }
                                                        if (!(_7397))
                                                        {
                                                            _7397 = true;
                                                            _7398 = 1000000.0;
                                                        }
                                                    }
                                                    vec3 _7178 = vec3(0.0, _7171, 0.0);
                                                    vec3 _7179 = _5175 + _7178;
                                                    _7495 = false;
                                                    {
                                                        int _7497 = 0;
                                                        for (;;)
                                                        {
                                                            bool _7509_ladder_break = false;
                                                            {
                                                                if (!(_7497 < Globals.uShadowLevels))
                                                                {
                                                                    _7509_ladder_break = true;
                                                                }
                                                                else
                                                                {
                                                                    vec3 _7524 = (_7179 - Globals.uClipmapOrigin.data[_7497].xyz) / vec3(Globals.uClipmapOrigin.data[_7497].w);
                                                                    if (any(lessThan(_7524, vec3(0.5))))
                                                                    {
                                                                        _7498 = true;
                                                                    }
                                                                    else
                                                                    {
                                                                        _7498 = any(greaterThan(_7524, vec3(float(Globals.uShadowResolution) - 0.5, float(Globals.uShadowResolution) - 0.5, float(Globals.uShadowResolution) - 0.5)));
                                                                    }
                                                                    if (!(_7498))
                                                                    {
                                                                        _7495 = true;
                                                                        _7496 = ((textureLod(_10474, vec3(_7524.x / float(Globals.uShadowResolution), _7524.y / float(Globals.uShadowResolution), ((float(_7497) * float(Globals.uShadowResolution)) + _7524.z) / float(Globals.uShadowResolution * Globals.uShadowLevels)), 0.0).x * 2.0) - 1.0) * Globals.uClipmapRange.data[_7497];
                                                                        _7509_ladder_break = true;
                                                                    }
                                                                }
                                                            }
                                                            if (_7509_ladder_break)
                                                            {
                                                                break;
                                                            }
                                                            _7497++;
                                                            continue;
                                                        }
                                                        if (!(_7495))
                                                        {
                                                            _7495 = true;
                                                            _7496 = 1000000.0;
                                                        }
                                                    }
                                                    vec3 _7181 = _5175 - _7178;
                                                    _7593 = false;
                                                    {
                                                        int _7595 = 0;
                                                        for (;;)
                                                        {
                                                            bool _7607_ladder_break = false;
                                                            {
                                                                if (!(_7595 < Globals.uShadowLevels))
                                                                {
                                                                    _7607_ladder_break = true;
                                                                }
                                                                else
                                                                {
                                                                    vec3 _7622 = (_7181 - Globals.uClipmapOrigin.data[_7595].xyz) / vec3(Globals.uClipmapOrigin.data[_7595].w);
                                                                    if (any(lessThan(_7622, vec3(0.5))))
                                                                    {
                                                                        _7596 = true;
                                                                    }
                                                                    else
                                                                    {
                                                                        _7596 = any(greaterThan(_7622, vec3(float(Globals.uShadowResolution) - 0.5, float(Globals.uShadowResolution) - 0.5, float(Globals.uShadowResolution) - 0.5)));
                                                                    }
                                                                    if (!(_7596))
                                                                    {
                                                                        _7593 = true;
                                                                        _7594 = ((textureLod(_10474, vec3(_7622.x / float(Globals.uShadowResolution), _7622.y / float(Globals.uShadowResolution), ((float(_7595) * float(Globals.uShadowResolution)) + _7622.z) / float(Globals.uShadowResolution * Globals.uShadowLevels)), 0.0).x * 2.0) - 1.0) * Globals.uClipmapRange.data[_7595];
                                                                        _7607_ladder_break = true;
                                                                    }
                                                                }
                                                            }
                                                            if (_7607_ladder_break)
                                                            {
                                                                break;
                                                            }
                                                            _7595++;
                                                            continue;
                                                        }
                                                        if (!(_7593))
                                                        {
                                                            _7593 = true;
                                                            _7594 = 1000000.0;
                                                        }
                                                    }
                                                    vec3 _7184 = vec3(0.0, 0.0, _7171);
                                                    vec3 _7185 = _5175 + _7184;
                                                    _7691 = false;
                                                    {
                                                        int _7693 = 0;
                                                        for (;;)
                                                        {
                                                            bool _7705_ladder_break = false;
                                                            {
                                                                if (!(_7693 < Globals.uShadowLevels))
                                                                {
                                                                    _7705_ladder_break = true;
                                                                }
                                                                else
                                                                {
                                                                    vec3 _7720 = (_7185 - Globals.uClipmapOrigin.data[_7693].xyz) / vec3(Globals.uClipmapOrigin.data[_7693].w);
                                                                    if (any(lessThan(_7720, vec3(0.5))))
                                                                    {
                                                                        _7694 = true;
                                                                    }
                                                                    else
                                                                    {
                                                                        _7694 = any(greaterThan(_7720, vec3(float(Globals.uShadowResolution) - 0.5, float(Globals.uShadowResolution) - 0.5, float(Globals.uShadowResolution) - 0.5)));
                                                                    }
                                                                    if (!(_7694))
                                                                    {
                                                                        _7691 = true;
                                                                        _7692 = ((textureLod(_10474, vec3(_7720.x / float(Globals.uShadowResolution), _7720.y / float(Globals.uShadowResolution), ((float(_7693) * float(Globals.uShadowResolution)) + _7720.z) / float(Globals.uShadowResolution * Globals.uShadowLevels)), 0.0).x * 2.0) - 1.0) * Globals.uClipmapRange.data[_7693];
                                                                        _7705_ladder_break = true;
                                                                    }
                                                                }
                                                            }
                                                            if (_7705_ladder_break)
                                                            {
                                                                break;
                                                            }
                                                            _7693++;
                                                            continue;
                                                        }
                                                        if (!(_7691))
                                                        {
                                                            _7691 = true;
                                                            _7692 = 1000000.0;
                                                        }
                                                    }
                                                    vec3 _7187 = _5175 - _7184;
                                                    _7789 = false;
                                                    {
                                                        int _7791 = 0;
                                                        for (;;)
                                                        {
                                                            bool _7803_ladder_break = false;
                                                            {
                                                                if (!(_7791 < Globals.uShadowLevels))
                                                                {
                                                                    _7803_ladder_break = true;
                                                                }
                                                                else
                                                                {
                                                                    vec3 _7818 = (_7187 - Globals.uClipmapOrigin.data[_7791].xyz) / vec3(Globals.uClipmapOrigin.data[_7791].w);
                                                                    if (any(lessThan(_7818, vec3(0.5))))
                                                                    {
                                                                        _7792 = true;
                                                                    }
                                                                    else
                                                                    {
                                                                        _7792 = any(greaterThan(_7818, vec3(float(Globals.uShadowResolution) - 0.5, float(Globals.uShadowResolution) - 0.5, float(Globals.uShadowResolution) - 0.5)));
                                                                    }
                                                                    if (!(_7792))
                                                                    {
                                                                        _7789 = true;
                                                                        _7790 = ((textureLod(_10474, vec3(_7818.x / float(Globals.uShadowResolution), _7818.y / float(Globals.uShadowResolution), ((float(_7791) * float(Globals.uShadowResolution)) + _7818.z) / float(Globals.uShadowResolution * Globals.uShadowLevels)), 0.0).x * 2.0) - 1.0) * Globals.uClipmapRange.data[_7791];
                                                                        _7803_ladder_break = true;
                                                                    }
                                                                }
                                                            }
                                                            if (_7803_ladder_break)
                                                            {
                                                                break;
                                                            }
                                                            _7791++;
                                                            continue;
                                                        }
                                                        if (!(_7789))
                                                        {
                                                            _7789 = true;
                                                            _7790 = 1000000.0;
                                                        }
                                                    }
                                                    vec3 _7190 = vec3(_7300 - _7398, _7496 - _7594, _7692 - _7790);
                                                    float _7191 = dot(_7190, _7190);
                                                    if (_7191 < 1.0000000133514319600180897396058e-10)
                                                    {
                                                        _7161 = vec3(0.0);
                                                    }
                                                    else
                                                    {
                                                        _7161 = _7190 * inversesqrt(_7191);
                                                    }
                                                    _7154 = _7161;
                                                }
                                            }
                                            vec3 _5317 = world + Globals.uTraceOrigin;
                                            {
                                                float _10113 = Globals.uShelterMap1.z;
                                                float _10114 = Globals.uShelterMap1.w;
                                                float _10115 = Globals.uShelterMap2.x;
                                                float _10116 = Globals.uShelterMap2.y;
                                                ivec2 _7953 = ivec2(floor((_5317.xy - Globals.uShelterMap0.xy) * Globals.uShelterMap0.z));
                                                vec2 _10159 = Globals.uShelterMap1.xy;
                                                if (Globals.uShelterMap0.w > 0.5)
                                                {
                                                    _7954 = _7953.x >= 0;
                                                }
                                                else
                                                {
                                                    _7954 = false;
                                                }
                                                if (_7954)
                                                {
                                                    _7954 = _7953.y >= 0;
                                                }
                                                else
                                                {
                                                    _7954 = false;
                                                }
                                                if (_7954)
                                                {
                                                    _7954 = _7953.x < int(_10159.x);
                                                }
                                                else
                                                {
                                                    _7954 = false;
                                                }
                                                if (_7954)
                                                {
                                                    _7954 = _7953.y < int(_10159.y);
                                                }
                                                else
                                                {
                                                    _7954 = false;
                                                }
                                                if (!_7954)
                                                {
                                                    _7888 = false;
                                                }
                                                else
                                                {
                                                    vec4 _7937 = texelFetch(_10489, ivec3(_7953, 0).xy, 0);
                                                    float _10163 = _10113;
                                                    float _10164 = _10114;
                                                    float _8004 = floor((_7937.z * 255.0) + 0.5) + (floor((_7937.y * 255.0) + 0.5) * 256.0);
                                                    if (_8004 < 0.5)
                                                    {
                                                        _7992 = -1000000015047466219876688855040.0;
                                                    }
                                                    else
                                                    {
                                                        _7992 = _10163 + ((_8004 - 1.0) * _10164);
                                                    }
                                                    float _7940 = _5317.z;
                                                    float _10168 = _10116;
                                                    if (_7992 > (_7940 + _10115))
                                                    {
                                                        _8017 = _7992 < (_7940 + _10168);
                                                    }
                                                    else
                                                    {
                                                        _8017 = false;
                                                    }
                                                    _7888 = _8017;
                                                }
                                            }
                                            if (!_7888)
                                            {
                                                vec3 _5324 = _5175 + Globals.uTraceOrigin;
                                                {
                                                    float _10051 = Globals.uShelterMap1.z;
                                                    float _10052 = Globals.uShelterMap1.w;
                                                    float _10053 = Globals.uShelterMap2.x;
                                                    float _10054 = Globals.uShelterMap2.y;
                                                    ivec2 _8098 = ivec2(floor((_5324.xy - Globals.uShelterMap0.xy) * Globals.uShelterMap0.z));
                                                    vec2 _10099 = Globals.uShelterMap1.xy;
                                                    if (Globals.uShelterMap0.w > 0.5)
                                                    {
                                                        _8099 = _8098.x >= 0;
                                                    }
                                                    else
                                                    {
                                                        _8099 = false;
                                                    }
                                                    if (_8099)
                                                    {
                                                        _8099 = _8098.y >= 0;
                                                    }
                                                    else
                                                    {
                                                        _8099 = false;
                                                    }
                                                    if (_8099)
                                                    {
                                                        _8099 = _8098.x < int(_10099.x);
                                                    }
                                                    else
                                                    {
                                                        _8099 = false;
                                                    }
                                                    if (_8099)
                                                    {
                                                        _8099 = _8098.y < int(_10099.y);
                                                    }
                                                    else
                                                    {
                                                        _8099 = false;
                                                    }
                                                    if (!_8099)
                                                    {
                                                        _8033 = false;
                                                    }
                                                    else
                                                    {
                                                        vec4 _8082 = texelFetch(_10489, ivec3(_8098, 0).xy, 0);
                                                        float _10103 = _10051;
                                                        float _10104 = _10052;
                                                        float _8149 = floor((_8082.z * 255.0) + 0.5) + (floor((_8082.y * 255.0) + 0.5) * 256.0);
                                                        if (_8149 < 0.5)
                                                        {
                                                            _8137 = -1000000015047466219876688855040.0;
                                                        }
                                                        else
                                                        {
                                                            _8137 = _10103 + ((_8149 - 1.0) * _10104);
                                                        }
                                                        float _8085 = _5324.z;
                                                        float _10108 = _10054;
                                                        if (_8137 > (_8085 + _10053))
                                                        {
                                                            _8162 = _8137 < (_8085 + _10108);
                                                        }
                                                        else
                                                        {
                                                            _8162 = false;
                                                        }
                                                        _8033 = _8162;
                                                    }
                                                }
                                                _5172 = _8033;
                                            }
                                            else
                                            {
                                                _5172 = false;
                                            }
                                            if (_5172)
                                            {
                                                _10242 += 1.0;
                                                _10245 = true;
                                                _5181 = 0.0;
                                            }
                                            else
                                            {
                                                _5181 = smoothstep(_5302, 2.0 * Globals.uClipmapOrigin.data[0].w, length(_5175 - world)) * smoothstep(0.75 * Globals.uClipmapOrigin.data[0].w, _5302, dot(_5175 - world, worldNormal));
                                            }
                                            if (dot(_7154, _5422) > 0.0)
                                            {
                                                _5182 = -_7154;
                                            }
                                            else
                                            {
                                                _5182 = _7154;
                                            }
                                            if (_5181 <= 0.0)
                                            {
                                                _5174 = 0.0;
                                            }
                                            else
                                            {
                                                if (dot(_5182, _5182) > 0.5)
                                                {
                                                    float _5349 = 0.5 * Globals.uClipmapOrigin.data[0].w;
                                                    {
                                                        _5183 = vec3(0.0);
                                                        if (Globals.uLookupBuckets <= 0)
                                                        {
                                                            _8182 = false;
                                                        }
                                                        else
                                                        {
                                                            ivec3 _8221 = ivec3(floor((_5175 - Globals.uLookupMin) / vec3(Globals.uLookupCell)));
                                                            int _8568 = int((((uint(_8221.x) * 2376512323u) ^ (uint(_8221.y) * 3625334849u)) ^ (uint(_8221.z) * 3407524639u)) & uint(Globals.uLookupBuckets - 1));
                                                            uvec2 _8226 = uvec2(textureSize(_10480, 0));
                                                            vec2 _8233 = vec2(float(_8226.x), float(_8226.y));
                                                            int _8185 = 0;
                                                            float _8186 = 0.0;
                                                            for (;;)
                                                            {
                                                                if (!(_8185 <= uCells._member0[Globals.uLookupBuckets].y))
                                                                {
                                                                    break;
                                                                }
                                                                bool _8247 = _8185 == 0;
                                                                if (_8247)
                                                                {
                                                                    _8187 = uCells._member0[_8568];
                                                                }
                                                                else
                                                                {
                                                                    int _8573 = uCells._member0[Globals.uLookupBuckets].x + ((_8185 - 1) * 6);
                                                                    float _8590 = intBitsToFloat(uCellPages_1._member0[_8573 + 3]) + _5349;
                                                                    vec3 _8591 = _5175 - vec3(intBitsToFloat(uCellPages_1._member0[_8573]), intBitsToFloat(uCellPages_1._member0[_8573 + 1]), intBitsToFloat(uCellPages_1._member0[_8573 + 2]));
                                                                    if (dot(_8591, _8591) > (_8590 * _8590))
                                                                    {
                                                                        _8569 = ivec2(0);
                                                                    }
                                                                    else
                                                                    {
                                                                        _8569 = ivec2(uCellPages_1._member0[_8573 + 4], uCellPages_1._member0[_8573 + 5]);
                                                                    }
                                                                    _8187 = _8569;
                                                                }
                                                                int _8188 = 0;
                                                                float _8189 = _8186;
                                                                for (;;)
                                                                {
                                                                    bool _8258_ladder_break = false;
                                                                    {
                                                                        if (!(_8188 < _8187.y))
                                                                        {
                                                                            _8258_ladder_break = true;
                                                                        }
                                                                        else
                                                                        {
                                                                            int _8269 = _8187.x + _8188;
                                                                            int _8272 = uCellPages_1._member0[_8269] * 10;
                                                                            if (!((uPages._member0[_8272 + 5].w > 0.5) == _8247))
                                                                            {
                                                                                int _8281 = _8272 + 9;
                                                                                vec3 _8285 = _5175 - uPages._member0[_8281].xyz;
                                                                                float _8287 = uPages._member0[_8281].w + _5349;
                                                                                if (!(dot(_8285, _8285) > (_8287 * _8287)))
                                                                                {
                                                                                    int _8607 = uCellPages_1._member0[_8269] * 10;
                                                                                    vec4 _8196 = uPages._member0[_8607];
                                                                                    vec4 _8197 = uPages._member0[_8607 + 1];
                                                                                    int _8613 = _8607 + 2;
                                                                                    int _8616 = _8607 + 3;
                                                                                    int _8619 = _8607 + 4;
                                                                                    int _8625 = _8607 + 6;
                                                                                    int _8628 = _8607 + 7;
                                                                                    int _8631 = _8607 + 8;
                                                                                    vec3 _8638 = _5175 - vec3(uPages._member0[_8625].w, uPages._member0[_8628].w, uPages._member0[_8631].w);
                                                                                    vec3 _8651 = vec3(dot(uPages._member0[_8625].xyz, _8638), dot(uPages._member0[_8628].xyz, _8638), dot(uPages._member0[_8631].xyz, _8638)) - uPages._member0[_8613].xyz;
                                                                                    vec2 _8655 = max(vec2(uPages._member0[_8613].w, uPages._member0[_8616].w), vec2(9.9999999747524270787835121154785e-07));
                                                                                    vec2 _8191 = _8655;
                                                                                    vec2 _8190 = vec2(dot(_8651, uPages._member0[_8616].xyz), dot(_8651, uPages._member0[_8619].xyz)) / _8655;
                                                                                    float _8192 = -dot(_8651, uPages._member0[_8607 + 5].xyz);
                                                                                    float _8193 = uPages._member0[_8619].w;
                                                                                    float _8296 = dot(uPages._member0[_8607 + 5].xyz, vec3(dot(uPages._member0[_8625].xyz, _5182), dot(uPages._member0[_8628].xyz, _5182), dot(uPages._member0[_8631].xyz, _5182)));
                                                                                    if (!(_8296 < 0.100000001490116119384765625))
                                                                                    {
                                                                                        float _8301 = Globals.uClipmapOrigin.data[0].w * 0.25;
                                                                                        vec2 _8304 = vec2(_8301) / _8191;
                                                                                        if (abs(_8190.x) > (1.0 + _8304.x))
                                                                                        {
                                                                                            _8198 = true;
                                                                                        }
                                                                                        else
                                                                                        {
                                                                                            _8198 = abs(_8190.y) > (1.0 + _8304.y);
                                                                                        }
                                                                                        if (!(_8198))
                                                                                        {
                                                                                            if (_8192 < (Globals.uClipmapOrigin.data[0].w * (-0.25)))
                                                                                            {
                                                                                                _8199 = true;
                                                                                            }
                                                                                            else
                                                                                            {
                                                                                                _8199 = _8192 > (_8193 + _8301);
                                                                                            }
                                                                                            if (!(_8199))
                                                                                            {
                                                                                                vec2 _8348 = (_8197.xy + (((clamp(_8190, vec2(-1.0), vec2(1.0)) * 0.5) + vec2(0.5)) * _8197.zw)) - vec2(0.5);
                                                                                                vec2 _8349 = floor(_8348);
                                                                                                vec2 _8350 = _8348 - _8349;
                                                                                                vec2 _8356 = _8196.zw / max(_8197.zw, vec2(9.9999999747524270787835121154785e-07));
                                                                                                float _8360 = _8192 / max(_8193, 9.9999999747524270787835121154785e-07);
                                                                                                float _8364 = _5349 / max(0.5 * _8193, 9.9999999747524270787835121154785e-07);
                                                                                                vec4 _8200 = vec4(0.0);
                                                                                                int _8203 = 0;
                                                                                                for (;;)
                                                                                                {
                                                                                                    if (!(_8203 < 4))
                                                                                                    {
                                                                                                        break;
                                                                                                    }
                                                                                                    vec2 _8379 = vec2(float(_8203 & 1), float(_8203 >> 1));
                                                                                                    vec2 _8382 = (_8349 + _8379) + vec2(0.5);
                                                                                                    vec2 _8383 = _8382 / _8233;
                                                                                                    if (textureLod(_10482, _8383, 0.0).w >= 0.5)
                                                                                                    {
                                                                                                        _8204 = 1.0;
                                                                                                    }
                                                                                                    else
                                                                                                    {
                                                                                                        _8204 = 0.0;
                                                                                                    }
                                                                                                    vec2 _8412 = mix(vec2(1.0) - _8350, _8350, _8379);
                                                                                                    _8200[_8203] = ((_8412.x * _8412.y) * _8204) * (1.0 - clamp((abs(_8360 - textureLod(_10484, _8383, 0.0).x) - _8364) / (0.25 * _8364), 0.0, 1.0));
                                                                                                    _8201[_8203] = textureLod(_10486, (floor(_8196.xy + ((_8382 - _8197.xy) * _8356)) + vec2(0.5)) / Globals.uAtlasSize.xy, 0.0).xyz;
                                                                                                    _8203++;
                                                                                                    continue;
                                                                                                }
                                                                                                float _8459 = ((_8200.x + _8200.y) + _8200.z) + _8200.w;
                                                                                                float _8460 = (_8296 * _8296) * _8459;
                                                                                                if (!(_8460 <= 0.0))
                                                                                                {
                                                                                                    _5183 += (max(((((_8201[0] * _8200.x) + (_8201[1] * _8200.y)) + (_8201[2] * _8200.z)) + (_8201[3] * _8200.w)) / vec3(_8459), vec3(0.0)) * _8460);
                                                                                                    _8189 += _8460;
                                                                                                }
                                                                                            }
                                                                                        }
                                                                                    }
                                                                                }
                                                                            }
                                                                        }
                                                                    }
                                                                    if (_8258_ladder_break)
                                                                    {
                                                                        break;
                                                                    }
                                                                    _8188++;
                                                                    continue;
                                                                }
                                                                _8185++;
                                                                _8186 = _8189;
                                                                continue;
                                                            }
                                                            if (_8186 <= 0.0)
                                                            {
                                                                _5183 = vec3(0.0);
                                                                _8182 = false;
                                                            }
                                                            else
                                                            {
                                                                _5183 /= vec3(_8186);
                                                                _8182 = true;
                                                            }
                                                        }
                                                    }
                                                    _5172 = _8182;
                                                }
                                                else
                                                {
                                                    _5172 = false;
                                                }
                                                if (_5172)
                                                {
                                                    vec3 _5380 = _5183 * Globals.uBounceScale;
                                                    float _9047 = max(_5380.x, max(_5380.y, _5380.z));
                                                    if (_9047 > Globals.uMaxRayIntensity)
                                                    {
                                                        _9040 = _5380 * (Globals.uMaxRayIntensity / _9047);
                                                    }
                                                    else
                                                    {
                                                        _9040 = _5380;
                                                    }
                                                    _10239 = _9040;
                                                    _10240 = _5181;
                                                    _10243 = true;
                                                    _5170 = 2;
                                                    _5171 = _5380;
                                                    _5174 = _5181;
                                                }
                                                else
                                                {
                                                    {
                                                        _5179 = vec2(0.0);
                                                        float _5180 = 1000000000.0;
                                                        if (Globals.uLookupBuckets <= 0)
                                                        {
                                                            _8671 = false;
                                                        }
                                                        else
                                                        {
                                                            ivec3 _8712 = ivec3(floor((_5175 - Globals.uLookupMin) / vec3(Globals.uLookupCell)));
                                                            int _8924 = int((((uint(_8712.x) * 2376512323u) ^ (uint(_8712.y) * 3625334849u)) ^ (uint(_8712.z) * 3407524639u)) & uint(Globals.uLookupBuckets - 1));
                                                            uvec2 _8717 = uvec2(textureSize(_10480, 0));
                                                            vec2 _8724 = vec2(float(_8717.x), float(_8717.y));
                                                            bool _8674 = false;
                                                            bool _8675 = false;
                                                            int _8676 = 0;
                                                            for (;;)
                                                            {
                                                                if (_8676 <= uCells._member0[Globals.uLookupBuckets].y)
                                                                {
                                                                    _8677 = !_8674;
                                                                }
                                                                else
                                                                {
                                                                    _8677 = false;
                                                                }
                                                                if (!_8677)
                                                                {
                                                                    break;
                                                                }
                                                                bool _8744 = _8676 == 0;
                                                                if (_8744)
                                                                {
                                                                    _8678 = uCells._member0[_8924];
                                                                }
                                                                else
                                                                {
                                                                    int _8929 = uCells._member0[Globals.uLookupBuckets].x + ((_8676 - 1) * 6);
                                                                    float _8945 = intBitsToFloat(uCellPages_1._member0[_8929 + 3]);
                                                                    vec3 _8947 = _5175 - vec3(intBitsToFloat(uCellPages_1._member0[_8929]), intBitsToFloat(uCellPages_1._member0[_8929 + 1]), intBitsToFloat(uCellPages_1._member0[_8929 + 2]));
                                                                    if (dot(_8947, _8947) > (_8945 * _8945))
                                                                    {
                                                                        _8925 = ivec2(0);
                                                                    }
                                                                    else
                                                                    {
                                                                        _8925 = ivec2(uCellPages_1._member0[_8929 + 4], uCellPages_1._member0[_8929 + 5]);
                                                                    }
                                                                    _8678 = _8925;
                                                                }
                                                                bool _8679 = _8674;
                                                                bool _8680 = _8675;
                                                                int _8681 = 0;
                                                                for (;;)
                                                                {
                                                                    bool _8756_ladder_break = false;
                                                                    {
                                                                        if (_8681 < _8678.y)
                                                                        {
                                                                            _8682 = !_8679;
                                                                        }
                                                                        else
                                                                        {
                                                                            _8682 = false;
                                                                        }
                                                                        if (!_8682)
                                                                        {
                                                                            _8756_ladder_break = true;
                                                                        }
                                                                        else
                                                                        {
                                                                            int _8773 = _8678.x + _8681;
                                                                            int _8776 = uCellPages_1._member0[_8773] * 10;
                                                                            if (!((uPages._member0[_8776 + 5].w > 0.5) == _8744))
                                                                            {
                                                                                int _8785 = _8776 + 9;
                                                                                vec3 _8789 = _5175 - uPages._member0[_8785].xyz;
                                                                                if (!(dot(_8789, _8789) > (uPages._member0[_8785].w * uPages._member0[_8785].w)))
                                                                                {
                                                                                    vec3 _8796 = -_5422;
                                                                                    int _8963 = uCellPages_1._member0[_8773] * 10;
                                                                                    vec4 _8689 = uPages._member0[_8963];
                                                                                    vec4 _8690 = uPages._member0[_8963 + 1];
                                                                                    int _8969 = _8963 + 2;
                                                                                    int _8972 = _8963 + 3;
                                                                                    int _8975 = _8963 + 4;
                                                                                    int _8981 = _8963 + 6;
                                                                                    int _8984 = _8963 + 7;
                                                                                    int _8987 = _8963 + 8;
                                                                                    vec3 _8994 = _5175 - vec3(uPages._member0[_8981].w, uPages._member0[_8984].w, uPages._member0[_8987].w);
                                                                                    vec3 _8688 = vec3(dot(uPages._member0[_8981].xyz, _8796), dot(uPages._member0[_8984].xyz, _8796), dot(uPages._member0[_8987].xyz, _8796));
                                                                                    vec3 _9007 = vec3(dot(uPages._member0[_8981].xyz, _8994), dot(uPages._member0[_8984].xyz, _8994), dot(uPages._member0[_8987].xyz, _8994)) - uPages._member0[_8969].xyz;
                                                                                    vec2 _9017 = vec2(dot(_9007, uPages._member0[_8972].xyz), dot(_9007, uPages._member0[_8975].xyz)) / max(vec2(uPages._member0[_8969].w, uPages._member0[_8972].w), vec2(9.9999999747524270787835121154785e-07));
                                                                                    vec2 _8683 = _9017;
                                                                                    float _8685 = -dot(_9007, uPages._member0[_8963 + 5].xyz);
                                                                                    float _8686 = uPages._member0[_8975].w;
                                                                                    vec3 _8687 = uPages._member0[_8963 + 5].xyz;
                                                                                    if (abs(_9017.x) > 1.0)
                                                                                    {
                                                                                        _8691 = true;
                                                                                    }
                                                                                    else
                                                                                    {
                                                                                        _8691 = abs(_8683.y) > 1.0;
                                                                                    }
                                                                                    if (!(_8691))
                                                                                    {
                                                                                        if (dot(_5422, _5422) > 0.5)
                                                                                        {
                                                                                            _8692 = dot(_8687, _8688) < (-0.0500000007450580596923828125);
                                                                                        }
                                                                                        else
                                                                                        {
                                                                                            _8692 = false;
                                                                                        }
                                                                                        if (!(_8692))
                                                                                        {
                                                                                            if (_8685 < (-0.25))
                                                                                            {
                                                                                                _8693 = true;
                                                                                            }
                                                                                            else
                                                                                            {
                                                                                                _8693 = _8685 > (_8686 + 0.25);
                                                                                            }
                                                                                            if (!(_8693))
                                                                                            {
                                                                                                vec2 _8840 = (_8683 * 0.5) + vec2(0.5);
                                                                                                vec2 _8847 = (_8690.xy + (_8840 * _8690.zw)) / _8724;
                                                                                                if (!(textureLod(_10482, _8847, 0.0).w < 0.5))
                                                                                                {
                                                                                                    float _8867 = abs((textureLod(_10484, _8847, 0.0).x * _8686) - _8685);
                                                                                                    if (_8867 < _5180)
                                                                                                    {
                                                                                                        _5180 = _8867;
                                                                                                        _5179 = (_8689.xy + (_8840 * _8689.zw)) / Globals.uAtlasSize.xy;
                                                                                                        _8694 = _8867 < 0.0199999995529651641845703125;
                                                                                                        _8695 = true;
                                                                                                    }
                                                                                                    else
                                                                                                    {
                                                                                                        _8694 = _8679;
                                                                                                        _8695 = _8680;
                                                                                                    }
                                                                                                    _8679 = _8694;
                                                                                                    _8680 = _8695;
                                                                                                }
                                                                                            }
                                                                                        }
                                                                                    }
                                                                                }
                                                                            }
                                                                        }
                                                                    }
                                                                    if (_8756_ladder_break)
                                                                    {
                                                                        break;
                                                                    }
                                                                    _8681++;
                                                                    continue;
                                                                }
                                                                _8674 = _8679;
                                                                _8675 = _8680;
                                                                _8676++;
                                                                continue;
                                                            }
                                                            _8671 = _8675;
                                                        }
                                                    }
                                                    if (_8671)
                                                    {
                                                        vec3 _5371 = textureLod(_10486, _5179, 0.0).xyz * Globals.uBounceScale;
                                                        float _9029 = max(_5371.x, max(_5371.y, _5371.z));
                                                        if (_9029 > Globals.uMaxRayIntensity)
                                                        {
                                                            _9022 = _5371 * (Globals.uMaxRayIntensity / _9029);
                                                        }
                                                        else
                                                        {
                                                            _9022 = _5371;
                                                        }
                                                        _10239 = _9022;
                                                        _10240 = _5181;
                                                        _10243 = true;
                                                        _5170 = 2;
                                                        _5171 = _5371;
                                                        _5174 = _5181;
                                                    }
                                                    else
                                                    {
                                                        _10242 += _5181;
                                                        _10245 = true;
                                                        _5174 = 0.0;
                                                    }
                                                }
                                            }
                                        }
                                        else
                                        {
                                            if (_5422.z >= 0.0)
                                            {
                                                {
                                                    if (Globals.uSkyShValid == 0)
                                                    {
                                                        _7084 = Globals.uSkyColour;
                                                    }
                                                    else
                                                    {
                                                        vec4 _7095 = vec4(_5422, 1.0);
                                                        vec4 _7098 = _7095.xyzz * _7095.yzzx;
                                                        float _7124 = _5422.x;
                                                        float _7126 = _5422.y;
                                                        _7084 = max(vec3(0.0), (vec3(dot(Globals.uSkySh.data[0], _7095), dot(Globals.uSkySh.data[1], _7095), dot(Globals.uSkySh.data[2], _7095)) + vec3(dot(Globals.uSkySh.data[3], _7098), dot(Globals.uSkySh.data[4], _7098), dot(Globals.uSkySh.data[5], _7098))) + (Globals.uSkySh.data[6].xyz * ((_7124 * _7124) - (_7126 * _7126))));
                                                    }
                                                }
                                                _5171 = _7084;
                                            }
                                            else
                                            {
                                                {
                                                    if (Globals.uSkyShValid == 0)
                                                    {
                                                        _7032 = Globals.uSkyColour;
                                                    }
                                                    else
                                                    {
                                                        vec4 _7043 = vec4(vec3(0.0, 0.0, 1.0), 1.0);
                                                        vec4 _7046 = _7043.xyzz * _7043.yzzx;
                                                        _7032 = max(vec3(0.0), vec3(dot(Globals.uSkySh.data[0], _7043), dot(Globals.uSkySh.data[1], _7043), dot(Globals.uSkySh.data[2], _7043)) + vec3(dot(Globals.uSkySh.data[3], _7046), dot(Globals.uSkySh.data[4], _7046), dot(Globals.uSkySh.data[5], _7046)));
                                                    }
                                                }
                                                _5171 = (_7032 + Globals.uGroundSun) * 0.25;
                                            }
                                            float _7142 = max(_5171.x, max(_5171.y, _5171.z));
                                            if (_7142 > Globals.uMaxRayIntensity)
                                            {
                                                _7135 = _5171 * (Globals.uMaxRayIntensity / _7142);
                                            }
                                            else
                                            {
                                                _7135 = _5171;
                                            }
                                            _10241 = _7135;
                                            _10244 = true;
                                            _5170 = 3;
                                            _5174 = 1.0;
                                        }
                                    }
                                }
                                else
                                {
                                    _5174 = 1.0;
                                }
                                if (Globals.uDebugSource != 0)
                                {
                                    _5172 = _5170 != Globals.uDebugSource;
                                }
                                else
                                {
                                    _5172 = false;
                                }
                                if (_5172)
                                {
                                    _5171 = vec3(0.0);
                                }
                                float _9065 = max(_5171.x, max(_5171.y, _5171.z));
                                if (_9065 > Globals.uMaxRayIntensity)
                                {
                                    _9058 = _5171 * (Globals.uMaxRayIntensity / _9065);
                                }
                                else
                                {
                                    _9058 = _5171;
                                }
                                vec3 _10396 = _9058;
                                float _10397 = _5174;
                                vec3 _10398 = _10239;
                                float _10399 = _10240;
                                vec3 _10400 = _10241;
                                float _10401 = _10242;
                                bool _10403 = _10244;
                                bool _10404 = _10245;
                                if (_10243)
                                {
                                    _10370 += (_10398 * _10399);
                                    _10371 += _10399;
                                }
                                if (_10404)
                                {
                                    _10374 += _10401;
                                }
                                if (_10403)
                                {
                                    _10372 += _10400;
                                    _10373 += 1.0;
                                }
                                _10368 += (_10396 * _10397);
                                _10369 += _10397;
                                _5058++;
                                continue;
                            }
                            bool _9124;
                            if (_10374 > 0.0)
                            {
                                if (Globals.uDebugSource == 0)
                                {
                                    _9124 = true;
                                }
                                else
                                {
                                    _9124 = Globals.uDebugSource == 2;
                                }
                            }
                            else
                            {
                                _9124 = false;
                            }
                            vec3 _9125;
                            float _9126;
                            if (_9124)
                            {
                                if (_10371 > 0.0)
                                {
                                    _9125 = _10370 / vec3(_10371);
                                }
                                else
                                {
                                    if (_10373 > 0.0)
                                    {
                                        _9125 = _10372 / vec3(_10373);
                                    }
                                    else
                                    {
                                        vec3 _9185;
                                        {
                                            if (Globals.uSkyShValid == 0)
                                            {
                                                _9185 = Globals.uSkyColour;
                                            }
                                            else
                                            {
                                                vec4 _9196 = vec4(vec3(0.0, 0.0, 1.0), 1.0);
                                                vec4 _9199 = _9196.xyzz * _9196.yzzx;
                                                _9185 = max(vec3(0.0), vec3(dot(Globals.uSkySh.data[0], _9196), dot(Globals.uSkySh.data[1], _9196), dot(Globals.uSkySh.data[2], _9196)) + vec3(dot(Globals.uSkySh.data[3], _9199), dot(Globals.uSkySh.data[4], _9199), dot(Globals.uSkySh.data[5], _9199)));
                                            }
                                        }
                                        _9125 = _9185;
                                    }
                                    _9125 *= 0.25;
                                }
                                _9126 = _10369 + _10374;
                                _9125 = _10368 + (_9125 * _10374);
                            }
                            else
                            {
                                _9126 = _10369;
                                _9125 = _10368;
                            }
                            _5055 = max(_9125 * (3.1415927410125732421875 / max(_9126, 4.0)), vec3(0.0));
                        }
                    }
                    if (Globals.uDebugSource == 4)
                    {
                        vec3 _3939 = -worldNormal;
                        vec2 litFound;
                        bool _9241;
                        {
                            litFound = vec2(0.0);
                            float agreement = 1000000000.0;
                            if (Globals.uLookupBuckets <= 0)
                            {
                                _9241 = false;
                            }
                            else
                            {
                                ivec3 _9282 = ivec3(floor((world - Globals.uLookupMin) / vec3(Globals.uLookupCell)));
                                int _9494 = int((((uint(_9282.x) * 2376512323u) ^ (uint(_9282.y) * 3625334849u)) ^ (uint(_9282.z) * 3407524639u)) & uint(Globals.uLookupBuckets - 1));
                                uvec2 _9287 = uvec2(textureSize(_10480, 0));
                                vec2 _9294 = vec2(float(_9287.x), float(_9287.y));
                                bool _9244 = false;
                                bool _9245 = false;
                                int _9246 = 0;
                                bool _9247;
                                ivec2 _9248;
                                bool _9252;
                                bool _9261;
                                bool _9262;
                                bool _9263;
                                bool _9264;
                                bool _9265;
                                ivec2 _9495;
                                for (;;)
                                {
                                    if (_9246 <= uCells._member0[Globals.uLookupBuckets].y)
                                    {
                                        _9247 = !_9244;
                                    }
                                    else
                                    {
                                        _9247 = false;
                                    }
                                    if (!_9247)
                                    {
                                        break;
                                    }
                                    bool _9314 = _9246 == 0;
                                    if (_9314)
                                    {
                                        _9248 = uCells._member0[_9494];
                                    }
                                    else
                                    {
                                        int _9499 = uCells._member0[Globals.uLookupBuckets].x + ((_9246 - 1) * 6);
                                        float _9515 = intBitsToFloat(uCellPages_1._member0[_9499 + 3]);
                                        vec3 _9517 = world - vec3(intBitsToFloat(uCellPages_1._member0[_9499]), intBitsToFloat(uCellPages_1._member0[_9499 + 1]), intBitsToFloat(uCellPages_1._member0[_9499 + 2]));
                                        if (dot(_9517, _9517) > (_9515 * _9515))
                                        {
                                            _9495 = ivec2(0);
                                        }
                                        else
                                        {
                                            _9495 = ivec2(uCellPages_1._member0[_9499 + 4], uCellPages_1._member0[_9499 + 5]);
                                        }
                                        _9248 = _9495;
                                    }
                                    bool _9249 = _9244;
                                    bool _9250 = _9245;
                                    int _9251 = 0;
                                    for (;;)
                                    {
                                        bool _9326_ladder_break = false;
                                        {
                                            if (_9251 < _9248.y)
                                            {
                                                _9252 = !_9249;
                                            }
                                            else
                                            {
                                                _9252 = false;
                                            }
                                            if (!_9252)
                                            {
                                                _9326_ladder_break = true;
                                            }
                                            else
                                            {
                                                int _9343 = _9248.x + _9251;
                                                int _9346 = uCellPages_1._member0[_9343] * 10;
                                                if (!((uPages._member0[_9346 + 5].w > 0.5) == _9314))
                                                {
                                                    int _9355 = _9346 + 9;
                                                    vec3 _9359 = world - uPages._member0[_9355].xyz;
                                                    if (!(dot(_9359, _9359) > (uPages._member0[_9355].w * uPages._member0[_9355].w)))
                                                    {
                                                        int _9533 = uCellPages_1._member0[_9343] * 10;
                                                        vec4 _9259 = uPages._member0[_9533];
                                                        vec4 _9260 = uPages._member0[_9533 + 1];
                                                        int _9539 = _9533 + 2;
                                                        int _9542 = _9533 + 3;
                                                        int _9545 = _9533 + 4;
                                                        int _9551 = _9533 + 6;
                                                        int _9554 = _9533 + 7;
                                                        int _9557 = _9533 + 8;
                                                        vec3 _9564 = world - vec3(uPages._member0[_9551].w, uPages._member0[_9554].w, uPages._member0[_9557].w);
                                                        vec3 _9258 = vec3(dot(uPages._member0[_9551].xyz, worldNormal), dot(uPages._member0[_9554].xyz, worldNormal), dot(uPages._member0[_9557].xyz, worldNormal));
                                                        vec3 _9577 = vec3(dot(uPages._member0[_9551].xyz, _9564), dot(uPages._member0[_9554].xyz, _9564), dot(uPages._member0[_9557].xyz, _9564)) - uPages._member0[_9539].xyz;
                                                        vec2 _9587 = vec2(dot(_9577, uPages._member0[_9542].xyz), dot(_9577, uPages._member0[_9545].xyz)) / max(vec2(uPages._member0[_9539].w, uPages._member0[_9542].w), vec2(9.9999999747524270787835121154785e-07));
                                                        vec2 _9253 = _9587;
                                                        float _9255 = -dot(_9577, uPages._member0[_9533 + 5].xyz);
                                                        float _9256 = uPages._member0[_9545].w;
                                                        vec3 _9257 = uPages._member0[_9533 + 5].xyz;
                                                        if (abs(_9587.x) > 1.0)
                                                        {
                                                            _9261 = true;
                                                        }
                                                        else
                                                        {
                                                            _9261 = abs(_9253.y) > 1.0;
                                                        }
                                                        if (!(_9261))
                                                        {
                                                            if (dot(_3939, _3939) > 0.5)
                                                            {
                                                                _9262 = dot(_9257, _9258) < (-0.0500000007450580596923828125);
                                                            }
                                                            else
                                                            {
                                                                _9262 = false;
                                                            }
                                                            if (!(_9262))
                                                            {
                                                                if (_9255 < (-0.25))
                                                                {
                                                                    _9263 = true;
                                                                }
                                                                else
                                                                {
                                                                    _9263 = _9255 > (_9256 + 0.25);
                                                                }
                                                                if (!(_9263))
                                                                {
                                                                    vec2 _9410 = (_9253 * 0.5) + vec2(0.5);
                                                                    vec2 _9417 = (_9260.xy + (_9410 * _9260.zw)) / _9294;
                                                                    if (!(textureLod(_10482, _9417, 0.0).w < 0.5))
                                                                    {
                                                                        float _9437 = abs((textureLod(_10484, _9417, 0.0).x * _9256) - _9255);
                                                                        if (_9437 < agreement)
                                                                        {
                                                                            agreement = _9437;
                                                                            litFound = (_9259.xy + (_9410 * _9259.zw)) / Globals.uAtlasSize.xy;
                                                                            _9264 = _9437 < 0.0199999995529651641845703125;
                                                                            _9265 = true;
                                                                        }
                                                                        else
                                                                        {
                                                                            _9264 = _9249;
                                                                            _9265 = _9250;
                                                                        }
                                                                        _9249 = _9264;
                                                                        _9250 = _9265;
                                                                    }
                                                                }
                                                            }
                                                        }
                                                    }
                                                }
                                            }
                                        }
                                        if (_9326_ladder_break)
                                        {
                                            break;
                                        }
                                        _9251++;
                                        continue;
                                    }
                                    _9244 = _9249;
                                    _9245 = _9250;
                                    _9246++;
                                    continue;
                                }
                                _9241 = _9245;
                            }
                        }
                        if (_9241)
                        {
                            nMin = textureLod(_10486, litFound, 0.0).xyz;
                        }
                        else
                        {
                            nMin = vec3(0.0);
                        }
                    }
                    else
                    {
                        nMin = _5055;
                    }
                }
                else
                {
                    nMin = vec3(0.0);
                }
                if (_4589)
                {
                    nCount = 1.0;
                }
                else
                {
                    nCount = 0.0;
                }
                imageStore(uIndirect, ivec2(gl_GlobalInvocationID.xy), vec4(nMin, nCount));
                vec4 _24;
                if (_4589)
                {
                    _24 = vec4(world - Globals.uCameraToCache, 1.0);
                }
                else
                {
                    _24 = vec4(0.0);
                }
                imageStore(uPosition, ivec2(gl_GlobalInvocationID.xy), _24);
            }
            else
            {
                if (!_4589)
                {
                    imageStore(uIndirect, ivec2(gl_GlobalInvocationID.xy), vec4(0.0));
                    imageStore(uPosition, ivec2(gl_GlobalInvocationID.xy), vec4(0.0));
                }
                else
                {
                    vec3 current = texelFetch(_10492, ivec3(_173, 0).xy, 0).xyz;
                    float _4010 = max(0.3499999940395355224609375, 0.0599999986588954925537109375 * length(viewPosition));
                    nMin = current;
                    vec3 nMax = current;
                    int dy = -1;
                    vec3 nSum = current;
                    nCount = 1.0;
                    bool _30;
                    bool _31;
                    vec3 historySum;
                    vec3 accumulated;
                    int t;
                    float frameSum;
                    float weightSum;
                    vec3 nMin_1;
                    vec3 nMax_1;
                    vec3 nWorld;
                    vec3 nNormal;
                    bool _43;
                    bool _44;
                    vec3 nSum_2;
                    bool _9593;
                    vec3 _9729;
                    bool _9730;
                    vec3 _9773;
                    vec3 _9774;
                    vec3 _9775;
                    for (;;)
                    {
                        if (!(dy <= 1))
                        {
                            break;
                        }
                        historySum = nMin;
                        accumulated = nMax;
                        t = -1;
                        vec3 nSum_1 = nSum;
                        frameSum = nCount;
                        for (;;)
                        {
                            bool _79_ladder_break = false;
                            {
                                if (!(t <= 1))
                                {
                                    _79_ladder_break = true;
                                }
                                else
                                {
                                    if (t == 0)
                                    {
                                        ownMotion = dy == 0;
                                    }
                                    else
                                    {
                                        ownMotion = false;
                                    }
                                    if (ownMotion)
                                    {
                                        nMin_1 = historySum;
                                        nMax_1 = accumulated;
                                    }
                                    else
                                    {
                                        ivec2 n = _173 + ivec2(t, dy);
                                        if (any(lessThan(n, ivec2(0))))
                                        {
                                            _30 = true;
                                        }
                                        else
                                        {
                                            _30 = any(greaterThanEqual(n, grid));
                                        }
                                        if (_30)
                                        {
                                            nMin_1 = historySum;
                                            nMax_1 = accumulated;
                                        }
                                        else
                                        {
                                            vec4 sampled = texelFetch(_10492, ivec3(n, 0).xy, 0);
                                            if (sampled.w > 0.5)
                                            {
                                                {
                                                    nWorld = vec3(0.0);
                                                    nNormal = vec3(0.0, 0.0, 1.0);
                                                    uint _9665 = uint(Globals.uFrameIndex);
                                                    uint _9666 = _9665 & 7u;
                                                    vec2 _9622 = (vec2(min(((n * ivec2(Globals.uProbeStep)) + min(ivec2(floor(((vec2(float(_9666), float((((_9665 & 1u) << uint(2)) | (_9665 & 2u)) | ((_9666 >> uint(2)) & 1u))) + vec2(0.5)) * vec2(0.125)) * float(Globals.uProbeStep))), ivec2(Globals.uProbeStep - 1))), (ivec2(Globals.uScreenWidth, Globals.uScreenHeight) - ivec2(1)))) + vec2(0.5)) / vec2(float(Globals.uScreenWidth), float(Globals.uScreenHeight));
                                                    vec4 _9692 = textureLod(_10465, _9622, 0.0);
                                                    float _9693 = _9692.x;
                                                    if (_9693 <= 0.0)
                                                    {
                                                        _9593 = false;
                                                    }
                                                    else
                                                    {
                                                        float _9712 = ((2.0 * Globals.uUnproject.z) * Globals.uUnproject.w) / ((Globals.uUnproject.w + Globals.uUnproject.z) - ((1.0 - (2.0 * _9693)) * (Globals.uUnproject.w - Globals.uUnproject.z)));
                                                        vec2 _9715 = (_9622 * 2.0) - vec2(1.0);
                                                        vec3 _9727 = vec3((_9715.x * _9712) / Globals.uUnproject.x, ((-1.0) * (_9715.y * _9712)) / Globals.uUnproject.y, _9712);
                                                        vec3 _9645 = ((Globals.uCameraToCache + (Globals.uViewToWorld0 * _9727.x)) + (Globals.uViewToWorld1 * _9727.y)) + (Globals.uViewToWorld2 * _9727.z);
                                                        nWorld = _9645;
                                                        vec3 _9655 = normalize((textureLod(_10467, _9622, 0.0).xyz * 2.0) - vec3(1.0));
                                                        nNormal = _9655;
                                                        {
                                                            vec3 _9735 = -normalize(_9645 - Globals.uCameraToCache);
                                                            float _9736 = dot(_9655, _9735);
                                                            if (_9736 >= 0.0)
                                                            {
                                                                _9729 = _9655;
                                                            }
                                                            else
                                                            {
                                                                {
                                                                    vec2 _9787 = vec2(1.0) / vec2(float(Globals.uScreenWidth), float(Globals.uScreenHeight));
                                                                    vec2 _9789 = vec2(_9787.x, 0.0);
                                                                    vec2 _9790 = _9622 + _9789;
                                                                    float _9872 = ((2.0 * Globals.uUnproject.z) * Globals.uUnproject.w) / ((Globals.uUnproject.w + Globals.uUnproject.z) - ((1.0 - (2.0 * textureLod(_10465, _9790, 0.0).x)) * (Globals.uUnproject.w - Globals.uUnproject.z)));
                                                                    vec2 _9875 = (_9790 * 2.0) - vec2(1.0);
                                                                    vec3 _9887 = vec3((_9875.x * _9872) / Globals.uUnproject.x, ((-1.0) * (_9875.y * _9872)) / Globals.uUnproject.y, _9872);
                                                                    vec2 _9794 = _9622 - _9789;
                                                                    float _9909 = ((2.0 * Globals.uUnproject.z) * Globals.uUnproject.w) / ((Globals.uUnproject.w + Globals.uUnproject.z) - ((1.0 - (2.0 * textureLod(_10465, _9794, 0.0).x)) * (Globals.uUnproject.w - Globals.uUnproject.z)));
                                                                    vec2 _9912 = (_9794 * 2.0) - vec2(1.0);
                                                                    vec3 _9924 = vec3((_9912.x * _9909) / Globals.uUnproject.x, ((-1.0) * (_9912.y * _9909)) / Globals.uUnproject.y, _9909);
                                                                    vec2 _9799 = vec2(0.0, _9787.y);
                                                                    vec2 _9800 = _9622 + _9799;
                                                                    float _9946 = ((2.0 * Globals.uUnproject.z) * Globals.uUnproject.w) / ((Globals.uUnproject.w + Globals.uUnproject.z) - ((1.0 - (2.0 * textureLod(_10465, _9800, 0.0).x)) * (Globals.uUnproject.w - Globals.uUnproject.z)));
                                                                    vec2 _9949 = (_9800 * 2.0) - vec2(1.0);
                                                                    vec3 _9961 = vec3((_9949.x * _9946) / Globals.uUnproject.x, ((-1.0) * (_9949.y * _9946)) / Globals.uUnproject.y, _9946);
                                                                    vec2 _9804 = _9622 - _9799;
                                                                    float _9983 = ((2.0 * Globals.uUnproject.z) * Globals.uUnproject.w) / ((Globals.uUnproject.w + Globals.uUnproject.z) - ((1.0 - (2.0 * textureLod(_10465, _9804, 0.0).x)) * (Globals.uUnproject.w - Globals.uUnproject.z)));
                                                                    vec2 _9986 = (_9804 * 2.0) - vec2(1.0);
                                                                    vec3 _9998 = vec3((_9986.x * _9983) / Globals.uUnproject.x, ((-1.0) * (_9986.y * _9983)) / Globals.uUnproject.y, _9983);
                                                                    float _9809 = _9727.z;
                                                                    if (abs(_9887.z - _9809) < abs(_9809 - _9924.z))
                                                                    {
                                                                        _9774 = _9887 - _9727;
                                                                    }
                                                                    else
                                                                    {
                                                                        _9774 = _9727 - _9924;
                                                                    }
                                                                    if (abs(_9961.z - _9809) < abs(_9809 - _9998.z))
                                                                    {
                                                                        _9775 = _9961 - _9727;
                                                                    }
                                                                    else
                                                                    {
                                                                        _9775 = _9727 - _9998;
                                                                    }
                                                                    vec3 _9833 = _9774;
                                                                    vec3 _9835 = cross(_9833, _9775);
                                                                    float _9836 = dot(_9835, _9835);
                                                                    if (_9836 <= 9.9999996826552253889678874634872e-21)
                                                                    {
                                                                        _9773 = vec3(0.0);
                                                                    }
                                                                    else
                                                                    {
                                                                        vec3 _9841 = _9835 * inversesqrt(_9836);
                                                                        if (dot(_9841, _9727) > 0.0)
                                                                        {
                                                                            _9774 = -_9841;
                                                                        }
                                                                        else
                                                                        {
                                                                            _9774 = _9841;
                                                                        }
                                                                        _9773 = _9774;
                                                                    }
                                                                }
                                                                vec3 _9754 = ((Globals.uViewToWorld0 * _9773.x) + (Globals.uViewToWorld1 * _9773.y)) + (Globals.uViewToWorld2 * _9773.z);
                                                                if (dot(_9754, _9754) < 0.5)
                                                                {
                                                                    _9730 = true;
                                                                }
                                                                else
                                                                {
                                                                    _9730 = dot(_9655, _9754) <= 0.0;
                                                                }
                                                                if (_9730)
                                                                {
                                                                    _9729 = -_9655;
                                                                }
                                                                else
                                                                {
                                                                    _9729 = normalize(_9655 - (_9735 * (2.0 * _9736)));
                                                                }
                                                            }
                                                        }
                                                        nNormal = _9729;
                                                        _9593 = true;
                                                    }
                                                }
                                                _31 = _9593;
                                            }
                                            else
                                            {
                                                _31 = false;
                                            }
                                            if (_31)
                                            {
                                                _43 = distance(nWorld, world) < _4010;
                                            }
                                            else
                                            {
                                                _43 = false;
                                            }
                                            if (_43)
                                            {
                                                _44 = dot(nNormal, worldNormal) > 0.800000011920928955078125;
                                            }
                                            else
                                            {
                                                _44 = false;
                                            }
                                            if (_44)
                                            {
                                                vec3 _4106 = sampled.xyz;
                                                nMin_1 = min(historySum, _4106);
                                                nMax_1 = max(accumulated, _4106);
                                                nSum_2 = nSum_1 + _4106;
                                                weightSum = frameSum + 1.0;
                                            }
                                            else
                                            {
                                                nMin_1 = historySum;
                                                nMax_1 = accumulated;
                                                nSum_2 = nSum_1;
                                                weightSum = frameSum;
                                            }
                                            nSum_1 = nSum_2;
                                            frameSum = weightSum;
                                        }
                                    }
                                }
                            }
                            if (_79_ladder_break)
                            {
                                break;
                            }
                            historySum = nMin_1;
                            accumulated = nMax_1;
                            t++;
                            continue;
                        }
                        nMin = historySum;
                        nMax = accumulated;
                        dy++;
                        nSum = nSum_1;
                        nCount = frameSum;
                        continue;
                    }
                    float _4160 = nCount;
                    vec3 est = nSum / vec3(_4160);
                    float frames;
                    if (Globals.uHasHistory != 0)
                    {
                        vec3 prevView = ((Globals.uPrevWorldToView0 * (world.x - Globals.uPrevCameraToCache.x)) + (Globals.uPrevWorldToView1 * (world.y - Globals.uPrevCameraToCache.y))) + (Globals.uPrevWorldToView2 * (world.z - Globals.uPrevCameraToCache.z));
                        float _4194 = prevView.z;
                        if (_4194 > 0.0)
                        {
                            vec2 prevUv_1 = (vec2((prevView.x * Globals.uPrevUnproject.x) / _4194, ((-1.0) * (prevView.y * Globals.uPrevUnproject.y)) / _4194) * 0.5) + vec2(0.5);
                            vec2 prevUv;
                            if (Globals.uHasVelocity != 0)
                            {
                                uint _10001 = uint(Globals.uFrameIndex);
                                uint _10002 = _10001 & 7u;
                                vec2 screen = vec2(float(Globals.uScreenWidth), float(Globals.uScreenHeight));
                                vec2 probeUv = (vec2(min(((_173 * ivec2(Globals.uProbeStep)) + min(ivec2(floor(((vec2(float(_10002), float((((_10001 & 1u) << uint(2)) | (_10001 & 2u)) | ((_10002 >> uint(2)) & 1u))) + vec2(0.5)) * vec2(0.125)) * float(Globals.uProbeStep))), ivec2(Globals.uProbeStep - 1))), (ivec2(Globals.uScreenWidth, Globals.uScreenHeight) - ivec2(1)))) + vec2(0.5)) / screen;
                                vec2 motion = textureLod(_10494, probeUv, 0.0).xy;
                                vec2 apart = (motion - (probeUv - prevUv_1)) * screen;
                                bool _4253 = dot(apart, apart) > 2.25;
                                if (_4253)
                                {
                                    prevUv = probeUv - motion;
                                }
                                else
                                {
                                    prevUv = prevUv_1;
                                }
                                ownMotion = _4253;
                            }
                            else
                            {
                                prevUv = prevUv_1;
                                ownMotion = false;
                            }
                            if (all(greaterThanEqual(prevUv, vec2(0.0))))
                            {
                                _30 = all(lessThanEqual(prevUv, vec2(1.0)));
                            }
                            else
                            {
                                _30 = false;
                            }
                            if (_30)
                            {
                                uint _10026 = uint(Globals.uFrameIndex - 1);
                                uint _10027 = _10026 & 7u;
                                vec2 tc = (((prevUv * vec2(float(Globals.uScreenWidth), float(Globals.uScreenHeight))) - vec2(min(ivec2(floor(((vec2(float(_10027), float((((_10026 & 1u) << uint(2)) | (_10026 & 2u)) | ((_10027 >> uint(2)) & 1u))) + vec2(0.5)) * vec2(0.125)) * float(Globals.uProbeStep))), ivec2(Globals.uProbeStep - 1)))) - vec2(0.5)) / vec2(float(Globals.uProbeStep));
                                vec2 _4301 = floor(tc);
                                vec2 _4302 = tc - _4301;
                                float _4315 = ((2.0 * viewPosition.z) * float(Globals.uProbeStep)) / (Globals.uUnproject.y * float(Globals.uScreenHeight));
                                t = 0;
                                historySum = vec3(0.0);
                                frameSum = 0.0;
                                weightSum = 0.0;
                                for (;;)
                                {
                                    bool _128_ladder_break = false;
                                    {
                                        if (!(t < 4))
                                        {
                                            _128_ladder_break = true;
                                        }
                                        else
                                        {
                                            int _4326 = t & 1;
                                            int _4328 = t >> 1;
                                            ivec2 tap = ivec2(_4301) + ivec2(_4326, _4328);
                                            if (any(lessThan(tap, ivec2(0))))
                                            {
                                                _30 = true;
                                            }
                                            else
                                            {
                                                _30 = any(greaterThanEqual(tap, grid));
                                            }
                                            if (!(_30))
                                            {
                                                ivec2 _4345 = ivec3(tap, 0).xy;
                                                vec4 sampled_1 = texelFetch(_10470, _4345, 0);
                                                if (!(sampled_1.w <= 0.0))
                                                {
                                                    if (!ownMotion)
                                                    {
                                                        _31 = abs(dot((sampled_1.xyz + Globals.uPrevCameraToCache) - world, worldNormal)) > max(0.0500000007450580596923828125, 0.5 * _4315);
                                                    }
                                                    else
                                                    {
                                                        _31 = false;
                                                    }
                                                    if (!(_31))
                                                    {
                                                        if (_4326 == 0)
                                                        {
                                                            nCount = 1.0 - _4302.x;
                                                        }
                                                        else
                                                        {
                                                            nCount = _4302.x;
                                                        }
                                                        if (_4328 == 0)
                                                        {
                                                            frames = 1.0 - _4302.y;
                                                        }
                                                        else
                                                        {
                                                            frames = _4302.y;
                                                        }
                                                        float w = nCount * frames;
                                                        vec4 sampled_2 = texelFetch(_10497, _4345, 0);
                                                        historySum += (sampled_2.xyz * w);
                                                        frameSum += (sampled_2.w * w);
                                                        weightSum += w;
                                                    }
                                                }
                                            }
                                        }
                                    }
                                    if (_128_ladder_break)
                                    {
                                        break;
                                    }
                                    t++;
                                    continue;
                                }
                                if (weightSum > 0.00999999977648258209228515625)
                                {
                                    vec3 windowCentre = (nMin + nMax) * 0.5;
                                    vec3 _4440 = ((((nMax - nMin) * 0.5) + (windowCentre * 0.25)) + vec3(0.001000000047497451305389404296875)) * 2.0;
                                    float _4452 = min((frameSum / weightSum) + 1.0, float(Globals.uMaxAccumulated));
                                    accumulated = mix(clamp(historySum / vec3(weightSum), windowCentre - _4440, windowCentre + _4440), est, vec3(1.0 / (1.0 + _4452)));
                                    frames = _4452;
                                }
                                else
                                {
                                    accumulated = est;
                                    frames = 0.0;
                                }
                            }
                            else
                            {
                                accumulated = est;
                                frames = 0.0;
                            }
                        }
                        else
                        {
                            accumulated = est;
                            frames = 0.0;
                        }
                    }
                    else
                    {
                        accumulated = est;
                        frames = 0.0;
                    }
                    if (Globals.uDebugSource == 4)
                    {
                        accumulated = current;
                        frames = 1.0;
                    }
                    imageStore(uIndirect, ivec2(gl_GlobalInvocationID.xy), vec4(accumulated, frames));
                    imageStore(uPosition, ivec2(gl_GlobalInvocationID.xy), vec4(world - Globals.uCameraToCache, 1.0));
                }
            }
        }
    }
}

