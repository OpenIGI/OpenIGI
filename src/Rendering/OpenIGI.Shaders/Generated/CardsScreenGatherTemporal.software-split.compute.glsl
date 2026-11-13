#version 430
layout(local_size_x = 4, local_size_y = 4, local_size_z = 16) in;

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

float _9511;

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
layout(binding = 5) uniform sampler2D _9516;
layout(binding = 6) uniform sampler2D _9518;
layout(binding = 7) uniform sampler2D _9521;
layout(binding = 8) uniform sampler2D _9523;
layout(binding = 3) uniform sampler3D _9525;
layout(binding = 9) uniform sampler2D _9528;
layout(binding = 0) uniform sampler2D _9531;
layout(binding = 1) uniform sampler2D _9533;
layout(binding = 2) uniform sampler2D _9535;
layout(binding = 4) uniform sampler2D _9537;
layout(binding = 10) uniform sampler2D _9540;

shared vec4 sRayRadiance[256];
shared vec4 sRaySurface[256];
shared vec4 sRayOpen[256];
shared float sRayUnplaced[256];

void main()
{
    bool _7267 = false;
    bool _7169 = false;
    bool _7071 = false;
    bool _6973 = false;
    bool _6875 = false;
    bool _6777 = false;
    bool _6679 = false;
    bool _5706 = false;
    bool _5596 = false;
    bool _5515 = false;
    {
        ivec2 probe = (ivec2(gl_WorkGroupID.xy) * ivec2(4)) + ivec2(gl_LocalInvocationID.xy);
        int ray = int(gl_LocalInvocationID.z);
        int slot = ((int(gl_LocalInvocationID.y) * 4) + int(gl_LocalInvocationID.x)) * 16;
        ivec2 grid = (ivec2(Globals.uScreenWidth, Globals.uScreenHeight) + ivec2(Globals.uProbeStep - 1)) / ivec2(Globals.uProbeStep);
        bool inside;
        if (probe.x < grid.x)
        {
            inside = probe.y < grid.y;
        }
        else
        {
            inside = false;
        }
        vec3 world = vec3(0.0);
        vec3 viewPosition = vec3(0.0);
        vec3 worldNormal = vec3(0.0, 0.0, 1.0);
        bool onSurface;
        if (inside)
        {
            bool _4116;
            {
                world = vec3(0.0);
                viewPosition = vec3(0.0);
                worldNormal = vec3(0.0, 0.0, 1.0);
                uint _4188 = uint(Globals.uFrameIndex);
                uint _4189 = _4188 & 7u;
                vec2 _4145 = (vec2(min(((probe * ivec2(Globals.uProbeStep)) + min(ivec2(floor(((vec2(float(_4189), float((((_4188 & 1u) << uint(2)) | (_4188 & 2u)) | ((_4189 >> uint(2)) & 1u))) + vec2(0.5)) * vec2(0.125)) * float(Globals.uProbeStep))), ivec2(Globals.uProbeStep - 1))), (ivec2(Globals.uScreenWidth, Globals.uScreenHeight) - ivec2(1)))) + vec2(0.5)) / vec2(float(Globals.uScreenWidth), float(Globals.uScreenHeight));
                vec4 _4215 = textureLod(_9516, _4145, 0.0);
                float _4216 = _4215.x;
                if (_4216 <= 0.0)
                {
                    _4116 = false;
                }
                else
                {
                    float _4235 = ((2.0 * Globals.uUnproject.z) * Globals.uUnproject.w) / ((Globals.uUnproject.w + Globals.uUnproject.z) - ((1.0 - (2.0 * _4216)) * (Globals.uUnproject.w - Globals.uUnproject.z)));
                    vec2 _4238 = (_4145 * 2.0) - vec2(1.0);
                    vec3 _4250 = vec3((_4238.x * _4235) / Globals.uUnproject.x, ((-1.0) * (_4238.y * _4235)) / Globals.uUnproject.y, _4235);
                    viewPosition = _4250;
                    vec3 _4168 = ((Globals.uCameraToCache + (Globals.uViewToWorld0 * _4250.x)) + (Globals.uViewToWorld1 * _4250.y)) + (Globals.uViewToWorld2 * _4250.z);
                    world = _4168;
                    vec3 _4178 = normalize((textureLod(_9518, _4145, 0.0).xyz * 2.0) - vec3(1.0));
                    worldNormal = _4178;
                    vec3 _4252;
                    {
                        vec3 _4258 = -normalize(_4168 - Globals.uCameraToCache);
                        float _4259 = dot(_4178, _4258);
                        if (_4259 >= 0.0)
                        {
                            _4252 = _4178;
                        }
                        else
                        {
                            vec3 _4296;
                            {
                                vec2 _4310 = vec2(1.0) / vec2(float(Globals.uScreenWidth), float(Globals.uScreenHeight));
                                vec2 _4312 = vec2(_4310.x, 0.0);
                                vec2 _4313 = _4145 + _4312;
                                float _4395 = ((2.0 * Globals.uUnproject.z) * Globals.uUnproject.w) / ((Globals.uUnproject.w + Globals.uUnproject.z) - ((1.0 - (2.0 * textureLod(_9516, _4313, 0.0).x)) * (Globals.uUnproject.w - Globals.uUnproject.z)));
                                vec2 _4398 = (_4313 * 2.0) - vec2(1.0);
                                vec3 _4410 = vec3((_4398.x * _4395) / Globals.uUnproject.x, ((-1.0) * (_4398.y * _4395)) / Globals.uUnproject.y, _4395);
                                vec2 _4317 = _4145 - _4312;
                                float _4432 = ((2.0 * Globals.uUnproject.z) * Globals.uUnproject.w) / ((Globals.uUnproject.w + Globals.uUnproject.z) - ((1.0 - (2.0 * textureLod(_9516, _4317, 0.0).x)) * (Globals.uUnproject.w - Globals.uUnproject.z)));
                                vec2 _4435 = (_4317 * 2.0) - vec2(1.0);
                                vec3 _4447 = vec3((_4435.x * _4432) / Globals.uUnproject.x, ((-1.0) * (_4435.y * _4432)) / Globals.uUnproject.y, _4432);
                                vec2 _4322 = vec2(0.0, _4310.y);
                                vec2 _4323 = _4145 + _4322;
                                float _4469 = ((2.0 * Globals.uUnproject.z) * Globals.uUnproject.w) / ((Globals.uUnproject.w + Globals.uUnproject.z) - ((1.0 - (2.0 * textureLod(_9516, _4323, 0.0).x)) * (Globals.uUnproject.w - Globals.uUnproject.z)));
                                vec2 _4472 = (_4323 * 2.0) - vec2(1.0);
                                vec3 _4484 = vec3((_4472.x * _4469) / Globals.uUnproject.x, ((-1.0) * (_4472.y * _4469)) / Globals.uUnproject.y, _4469);
                                vec2 _4327 = _4145 - _4322;
                                float _4506 = ((2.0 * Globals.uUnproject.z) * Globals.uUnproject.w) / ((Globals.uUnproject.w + Globals.uUnproject.z) - ((1.0 - (2.0 * textureLod(_9516, _4327, 0.0).x)) * (Globals.uUnproject.w - Globals.uUnproject.z)));
                                vec2 _4509 = (_4327 * 2.0) - vec2(1.0);
                                vec3 _4521 = vec3((_4509.x * _4506) / Globals.uUnproject.x, ((-1.0) * (_4509.y * _4506)) / Globals.uUnproject.y, _4506);
                                vec3 _4297;
                                if (abs(_4410.z - _4250.z) < abs(_4250.z - _4447.z))
                                {
                                    _4297 = _4410 - _4250;
                                }
                                else
                                {
                                    _4297 = _4250 - _4447;
                                }
                                vec3 _4298;
                                if (abs(_4484.z - _4250.z) < abs(_4250.z - _4521.z))
                                {
                                    _4298 = _4484 - _4250;
                                }
                                else
                                {
                                    _4298 = _4250 - _4521;
                                }
                                vec3 _4356 = _4297;
                                vec3 _4358 = cross(_4356, _4298);
                                float _4359 = dot(_4358, _4358);
                                if (_4359 <= 9.9999996826552253889678874634872e-21)
                                {
                                    _4296 = vec3(0.0);
                                }
                                else
                                {
                                    vec3 _4364 = _4358 * inversesqrt(_4359);
                                    if (dot(_4364, _4250) > 0.0)
                                    {
                                        _4297 = -_4364;
                                    }
                                    else
                                    {
                                        _4297 = _4364;
                                    }
                                    _4296 = _4297;
                                }
                            }
                            vec3 _4277 = ((Globals.uViewToWorld0 * _4296.x) + (Globals.uViewToWorld1 * _4296.y)) + (Globals.uViewToWorld2 * _4296.z);
                            bool _4253;
                            if (dot(_4277, _4277) < 0.5)
                            {
                                _4253 = true;
                            }
                            else
                            {
                                _4253 = dot(_4178, _4277) <= 0.0;
                            }
                            if (_4253)
                            {
                                _4252 = -_4178;
                            }
                            else
                            {
                                _4252 = normalize(_4178 - (_4258 * (2.0 * _4259)));
                            }
                        }
                    }
                    worldNormal = _4252;
                    _4116 = true;
                }
            }
            onSurface = _4116;
        }
        else
        {
            onSurface = false;
        }
        bool gathers;
        vec3 _9394;
        vec3 _9395;
        vec3 _9479;
        vec3 _9480;
        vec3 _9481;
        if (onSurface)
        {
            vec3 _9359;
            vec3 _9360;
            vec3 _9361;
            {
                vec3 _9374 = worldNormal;
                if (worldNormal.z < (-0.99999988079071044921875))
                {
                    _9359 = vec3(0.0, -1.0, 0.0);
                    _9360 = vec3(-1.0, 0.0, 0.0);
                    _9361 = _9374;
                }
                else
                {
                    float _4555 = 1.0 / (1.0 + worldNormal.z);
                    float _4557 = -worldNormal.x;
                    float _4560 = (_4557 * worldNormal.y) * _4555;
                    _9359 = vec3(1.0 - ((worldNormal.x * worldNormal.x) * _4555), _4560, _4557);
                    _9360 = vec3(_4560, 1.0 - ((worldNormal.y * worldNormal.y) * _4555), -worldNormal.y);
                    _9361 = _9374;
                }
            }
            _9479 = _9359;
            _9480 = _9360;
            _9481 = _9361;
            _9394 = vec3(dot(Globals.uViewToWorld0, worldNormal), dot(Globals.uViewToWorld1, worldNormal), dot(Globals.uViewToWorld2, worldNormal));
            _9395 = world;
            bool _4522;
            if (Globals.uShadowLevels > 0)
            {
                _4522 = Globals.uLookupBuckets > 0;
            }
            else
            {
                _4522 = false;
            }
            gathers = _4522;
        }
        else
        {
            gathers = false;
        }
        float _21;
        if (gathers)
        {
            vec2 _4588;
            {
                if (Globals.uFrameIndex <= 0)
                {
                    _4588 = vec2(0.5);
                }
                else
                {
                    float _4603 = float(((uint(probe.x) * 73856093u) ^ (uint(probe.y) * 19349663u)) & 65535u);
                    uint _4606 = uint(Globals.uFrameIndex & 65535);
                    uint _4623 = (_4606 << 16u) | (_4606 >> 16u);
                    uint _4628 = ((_4623 & 1431655765u) << 1u) | ((_4623 & 2863311530u) >> 1u);
                    uint _4633 = ((_4628 & 858993459u) << 2u) | ((_4628 & 3435973836u) >> 2u);
                    uint _4638 = ((_4633 & 252645135u) << 4u) | ((_4633 & 4042322160u) >> 4u);
                    _4588 = fract(vec2(fract(float(_4606) * 0.61803400516510009765625), float(((_4638 & 16711935u) << 8u) | ((_4638 & 4278255360u) >> 8u)) * 2.3283064365386962890625e-10) + vec2(_4603 * 1.52587890625e-05, fract(_4603 * 2.4689239580766297876834869384766e-05)));
                }
            }
            vec3 _9308 = vec3(0.0);
            float _9309 = 0.0;
            vec3 _9310 = vec3(0.0);
            float _9311 = 0.0;
            bool _9312 = false;
            bool _9313 = false;
            bool _9314 = false;
            vec2 _4685 = (vec2(float(ray - 4 * (ray / 4)), float(ray / 4)) + _4588) * vec2(0.25);
            float _4687 = 6.283185482025146484375 * _4685.x;
            float _4689 = sqrt(_4685.y);
            float _4693 = sqrt(max(0.0, 1.0 - (_4689 * _4689)));
            vec3 _4900 = ((_9479 * (_4693 * cos(_4687))) + (_9480 * (_4693 * sin(_4687)))) + (_9481 * _4689);
            int _4648;
            vec3 _4649;
            bool _4650;
            if (Globals.uScreenTraceSteps > 0)
            {
                vec3 _4912 = vec3(dot(Globals.uViewToWorld0, _4900), dot(Globals.uViewToWorld1, _4900), dot(Globals.uViewToWorld2, _4900));
                vec3 _4651;
                bool _4914;
                {
                    _4651 = vec3(0.0);
                    bool _4915;
                    if (Globals.uScreenTraceSteps <= 0)
                    {
                        _4915 = true;
                    }
                    else
                    {
                        _4915 = _4912.z == 0.0;
                    }
                    if (_4915)
                    {
                        _4914 = false;
                    }
                    else
                    {
                        vec3 _4938 = viewPosition + (_9394 * 0.0199999995529651641845703125);
                        vec3 _4942 = _4938 + (_4912 * Globals.uScreenTraceDistance);
                        vec3 _4916;
                        if (_4942.z < (Globals.uUnproject.z + 0.00999999977648258209228515625))
                        {
                            _4916 = _4938 + (_4912 * (((Globals.uUnproject.z + 0.00999999977648258209228515625) - _4938.z) / _4912.z));
                        }
                        else
                        {
                            _4916 = _4942;
                        }
                        if (_4916.z <= Globals.uUnproject.z)
                        {
                            _4914 = false;
                        }
                        else
                        {
                            float _5305 = _4938.z;
                            vec2 _5316 = (vec2((_4938.x * Globals.uUnproject.x) / _5305, ((-1.0) * (_4938.y * Globals.uUnproject.y)) / _5305) * 0.5) + vec2(0.5);
                            vec2 _4971 = ((vec2((_4916.x * Globals.uUnproject.x) / _4916.z, ((-1.0) * (_4916.y * Globals.uUnproject.y)) / _4916.z) * 0.5) + vec2(0.5)) - _5316;
                            int _4986 = int(clamp(length(_4971 / (vec2(1.0) / vec2(float(Globals.uScreenWidth), float(Globals.uScreenHeight)))), 1.0, float(Globals.uScreenTraceSteps)));
                            float _4988 = 1.0 / float(_4986);
                            float _5005 = fract(52.98291778564453125 * fract(dot((_5316 * vec2(float(Globals.uScreenWidth), float(Globals.uScreenHeight))) + vec2(5.588237762451171875 * float(Globals.uFrameIndex & 63)), vec2(0.067110560834407806396484375, 0.005837149918079376220703125))));
                            float _4917 = 0.0;
                            int _4918 = 1;
                            float _4919;
                            bool _4920;
                            for (;;)
                            {
                                if (!(_4918 <= 256))
                                {
                                    _4919 = -1.0;
                                    break;
                                }
                                if (_4918 > _4986)
                                {
                                    _4919 = -1.0;
                                    break;
                                }
                                float _5022 = _4988 * (float(_4918) + _5005);
                                if (_5022 > 1.0)
                                {
                                    _4919 = -1.0;
                                    break;
                                }
                                vec2 _5027 = _5316 + (_4971 * _5022);
                                if (any(lessThan(_5027, vec2(0.0))))
                                {
                                    _4915 = true;
                                }
                                else
                                {
                                    _4915 = any(greaterThan(_5027, vec2(1.0)));
                                }
                                if (_4915)
                                {
                                    _4919 = -1.0;
                                    break;
                                }
                                float _5038 = _4938.z;
                                float _5357 = ((2.0 * Globals.uUnproject.z) * Globals.uUnproject.w) / ((Globals.uUnproject.w + Globals.uUnproject.z) - ((1.0 - (2.0 * textureLod(_9516, _5027, 0.0).x)) * (Globals.uUnproject.w - Globals.uUnproject.z)));
                                vec2 _5360 = (_5027 * 2.0) - vec2(1.0);
                                vec3 _5372 = vec3(_9511, _9511, _5357);
                                float _5046 = _5372.z;
                                float _5047 = (1.0 / mix(1.0 / _5038, 1.0 / _4916.z, _5022)) - _5046;
                                if (_5047 > 0.0)
                                {
                                    _4920 = _5047 < (Globals.uScreenThickness + (_5046 * 0.0199999995529651641845703125));
                                }
                                else
                                {
                                    _4920 = false;
                                }
                                if (_4920)
                                {
                                    _4919 = _4917;
                                    float _4921 = _5022;
                                    int _4922 = 0;
                                    for (;;)
                                    {
                                        if (!(_4922 < 4))
                                        {
                                            break;
                                        }
                                        float _5069 = _4921;
                                        float _5071 = (_4919 + _5069) * 0.5;
                                        vec2 _5077 = _5316 + (_4971 * _5071);
                                        float _5394 = ((2.0 * Globals.uUnproject.z) * Globals.uUnproject.w) / ((Globals.uUnproject.w + Globals.uUnproject.z) - ((1.0 - (2.0 * textureLod(_9516, _5077, 0.0).x)) * (Globals.uUnproject.w - Globals.uUnproject.z)));
                                        vec2 _5397 = (_5077 * 2.0) - vec2(1.0);
                                        if (((1.0 / mix(1.0 / _5038, 1.0 / _4916.z, _5071)) - vec3(_9511, _9511, _5394).z) > 0.0)
                                        {
                                            _4921 = _5071;
                                        }
                                        else
                                        {
                                            _4919 = _5071;
                                        }
                                        _4922++;
                                        continue;
                                    }
                                    _4919 = _4921;
                                    break;
                                }
                                _4917 = _5022;
                                _4918++;
                                continue;
                            }
                            if (_4919 <= 0.0)
                            {
                                _4914 = false;
                            }
                            else
                            {
                                vec2 _5107 = _5316 + (_4971 * _4919);
                                vec3 _5116 = (textureLod(_9518, _5107, 0.0).xyz * 2.0) - vec3(1.0);
                                vec3 _5421 = vec3(dot(Globals.uViewToWorld0, _5116), dot(Globals.uViewToWorld1, _5116), dot(Globals.uViewToWorld2, _5116));
                                if (dot(_5421, _4912) >= 0.0)
                                {
                                    _4914 = false;
                                }
                                else
                                {
                                    float _5443 = ((2.0 * Globals.uUnproject.z) * Globals.uUnproject.w) / ((Globals.uUnproject.w + Globals.uUnproject.z) - ((1.0 - (2.0 * textureLod(_9516, _5107, 0.0).x)) * (Globals.uUnproject.w - Globals.uUnproject.z)));
                                    vec2 _5446 = (_5107 * 2.0) - vec2(1.0);
                                    vec3 _5458 = vec3((_5446.x * _5443) / Globals.uUnproject.x, ((-1.0) * (_5446.y * _5443)) / Globals.uUnproject.y, _5443);
                                    vec3 _5141 = ((Globals.uCameraToCache + (Globals.uViewToWorld0 * _5458.x)) + (Globals.uViewToWorld1 * _5458.y)) + (Globals.uViewToWorld2 * _5458.z);
                                    float _5144 = _5141.x;
                                    float _5152 = _5141.y;
                                    float _5160 = _5141.z;
                                    vec3 _5165 = ((Globals.uColourWorldToView0 * (_5144 - Globals.uColourCameraToCache.x)) + (Globals.uColourWorldToView1 * (_5152 - Globals.uColourCameraToCache.y))) + (Globals.uColourWorldToView2 * (_5160 - Globals.uColourCameraToCache.z));
                                    if (Globals.uHasHistory == 0)
                                    {
                                        _4915 = true;
                                    }
                                    else
                                    {
                                        _4915 = _5165.z <= Globals.uUnproject.z;
                                    }
                                    if (_4915)
                                    {
                                        _4914 = false;
                                    }
                                    else
                                    {
                                        float _5466 = _5165.z;
                                        vec2 _5477 = (vec2((_5165.x * Globals.uUnproject.x) / _5466, ((-1.0) * (_5165.y * Globals.uUnproject.y)) / _5466) * 0.5) + vec2(0.5);
                                        if (any(lessThan(_5477, vec2(0.0))))
                                        {
                                            _4915 = true;
                                        }
                                        else
                                        {
                                            _4915 = any(greaterThan(_5477, vec2(1.0)));
                                        }
                                        if (_4915)
                                        {
                                            _4914 = false;
                                        }
                                        else
                                        {
                                            vec3 _5210 = ((Globals.uPrevWorldToView0 * (_5144 - Globals.uPrevCameraToCache.x)) + (Globals.uPrevWorldToView1 * (_5152 - Globals.uPrevCameraToCache.y))) + (Globals.uPrevWorldToView2 * (_5160 - Globals.uPrevCameraToCache.z));
                                            float _5211 = _5210.z;
                                            if (_5211 <= Globals.uUnproject.z)
                                            {
                                                _4914 = false;
                                            }
                                            else
                                            {
                                                float _5485 = _5210.z;
                                                vec4 _5244 = texelFetch(_9521, ivec3(clamp(ivec2(((vec2((_5210.x * Globals.uUnproject.x) / _5485, ((-1.0) * (_5210.y * Globals.uUnproject.y)) / _5485) * 0.5) + vec2(0.5)) * vec2(float(Globals.uScreenWidth), float(Globals.uScreenHeight))) / ivec2(Globals.uProbeStep), ivec2(0), ((ivec2(Globals.uScreenWidth, Globals.uScreenHeight) + ivec2(Globals.uProbeStep - 1)) / ivec2(Globals.uProbeStep)) - ivec2(1)), 0).xy, 0);
                                                if (_5244.w <= 0.0)
                                                {
                                                    _4915 = true;
                                                }
                                                else
                                                {
                                                    _4915 = abs(dot((_5244.xyz + Globals.uPrevCameraToCache) - _5141, normalize(((Globals.uViewToWorld0 * _5421.x) + (Globals.uViewToWorld1 * _5421.y)) + (Globals.uViewToWorld2 * _5421.z)))) > max(0.100000001490116119384765625, 2.0 * (((2.0 * _5211) * float(Globals.uProbeStep)) / (Globals.uUnproject.y * float(Globals.uScreenHeight))));
                                                }
                                                if (_4915)
                                                {
                                                    _4914 = false;
                                                }
                                                else
                                                {
                                                    vec4 _5288 = textureLod(_9523, _5477, 0.0);
                                                    if (_5288.w < 0.5)
                                                    {
                                                        _4914 = false;
                                                    }
                                                    else
                                                    {
                                                        vec3 _5294 = _5288.xyz;
                                                        float _5504 = max(_5288.x, max(_5288.y, _5288.z));
                                                        vec3 _5497;
                                                        if (_5504 > Globals.uMaxRayIntensity)
                                                        {
                                                            _5497 = _5294 * (Globals.uMaxRayIntensity / _5504);
                                                        }
                                                        else
                                                        {
                                                            _5497 = _5294;
                                                        }
                                                        _4651 = _5497;
                                                        _4914 = true;
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
                if (_4914)
                {
                    _4648 = 1;
                    _4649 = _4651;
                }
                else
                {
                    _4648 = 0;
                    _4649 = vec3(0.0);
                }
                _4650 = _4914;
            }
            else
            {
                _4650 = false;
                _4648 = 0;
                _4649 = vec3(0.0);
            }
            float _4652;
            if (!_4650)
            {
                _5515 = false;
                vec3 _4653;
                float _5516;
                {
                    _4653 = _9395;
                    if (Globals.uShadowLevels <= 0)
                    {
                        _5515 = true;
                        _5516 = 1.0;
                    }
                    else
                    {
                        vec3 _5539 = _9395 + ((worldNormal * Globals.uClipmapOrigin.data[0].w) * 1.5);
                        float _5550 = (Globals.uClipmapOrigin.data[0].w * float(Globals.uShadowResolution)) * float(1 << (Globals.uShadowLevels - 1));
                        int _5517 = 0;
                        float _5518 = 0.0;
                        bool _5519;
                        bool _5520;
                        float _5522;
                        float _5597;
                        bool _5599;
                        for (;;)
                        {
                            if (_5517 < 64)
                            {
                                _5519 = _5518 < _5550;
                            }
                            else
                            {
                                _5519 = false;
                            }
                            if (!_5519)
                            {
                                break;
                            }
                            vec3 _5567 = _5539 + (_4900 * _5518);
                            _5596 = false;
                            {
                                _5520 = false;
                                _5522 = Globals.uClipmapOrigin.data[0].w;
                                int _5598 = 0;
                                for (;;)
                                {
                                    bool _5610_ladder_break = false;
                                    {
                                        if (!(_5598 < Globals.uShadowLevels))
                                        {
                                            _5610_ladder_break = true;
                                        }
                                        else
                                        {
                                            vec3 _5625 = (_5567 - Globals.uClipmapOrigin.data[_5598].xyz) / vec3(Globals.uClipmapOrigin.data[_5598].w);
                                            if (any(lessThan(_5625, vec3(0.5))))
                                            {
                                                _5599 = true;
                                            }
                                            else
                                            {
                                                _5599 = any(greaterThan(_5625, vec3(float(Globals.uShadowResolution) - 0.5, float(Globals.uShadowResolution) - 0.5, float(Globals.uShadowResolution) - 0.5)));
                                            }
                                            if (!(_5599))
                                            {
                                                _5520 = true;
                                                _5522 = Globals.uClipmapOrigin.data[_5598].w;
                                                _5596 = true;
                                                _5597 = ((textureLod(_9525, vec3(_5625.x / float(Globals.uShadowResolution), _5625.y / float(Globals.uShadowResolution), ((float(_5598) * float(Globals.uShadowResolution)) + _5625.z) / float(Globals.uShadowResolution * Globals.uShadowLevels)), 0.0).x * 2.0) - 1.0) * Globals.uClipmapRange.data[_5598];
                                                _5610_ladder_break = true;
                                            }
                                        }
                                    }
                                    if (_5610_ladder_break)
                                    {
                                        break;
                                    }
                                    _5598++;
                                    continue;
                                }
                                if (!(_5596))
                                {
                                    _5596 = true;
                                    _5597 = 1000000.0;
                                }
                            }
                            if (!_5520)
                            {
                                _5515 = true;
                                _5516 = 1.0;
                                break;
                            }
                            float _5701 = (0.5 * _5522) * clamp(_5518 / max(2.0 * _5522, 9.9999999747524270787835121154785e-07), 0.0, 1.0);
                            float _5523 = max((_5518 + _5597) - _5701, 0.0);
                            if (_5597 < _5701)
                            {
                                _4653 = _5539 + (_4900 * _5523);
                                _5515 = true;
                                _5516 = 0.0;
                                break;
                            }
                            _5517++;
                            _5518 += max(_5597, _5522 * 0.25);
                            continue;
                        }
                        if (!(_5515))
                        {
                            _5515 = true;
                            _5516 = 1.0;
                        }
                    }
                }
                bool _4718 = _5516 <= 0.5;
                vec3 _4720 = world + (worldNormal * 0.100000001490116119384765625);
                vec3 _4723 = _4720 + Globals.uTraceOrigin;
                _5706 = false;
                float _5707;
                {
                    bool _5708;
                    if (Globals.uTerrainMap0.w < 0.5)
                    {
                        _5708 = true;
                    }
                    else
                    {
                        _5708 = _4900.z > (-0.00999999977648258209228515625);
                    }
                    if (_5708)
                    {
                        _5708 = true;
                    }
                    else
                    {
                        _5708 = Globals.uTerrainMap0.z <= 0.0;
                    }
                    if (_5708)
                    {
                        _5706 = true;
                        _5707 = -1.0;
                    }
                    else
                    {
                        float _5737 = 0.5 / Globals.uTerrainMap0.z;
                        float _5709 = 0.0;
                        int _5710 = 1;
                        float _5826;
                        bool _5851;
                        float _5889;
                        float _5914;
                        bool _5939;
                        float _5977;
                        for (;;)
                        {
                            if (!(_5710 <= 64))
                            {
                                break;
                            }
                            float _5749 = min(float(_5710) * _5737, 32.0);
                            vec3 _5751 = _4723 + (_4900 * _5749);
                            float _9284 = Globals.uTerrainMap1.z;
                            float _9285 = Globals.uTerrainMap1.w;
                            vec2 _9286 = Globals.uTerrainMap1.xy;
                            ivec2 _5850 = ivec2(floor((_5751.xy - Globals.uTerrainMap0.xy) * Globals.uTerrainMap0.z));
                            if (Globals.uTerrainMap0.w > 0.5)
                            {
                                _5851 = _5850.x >= 0;
                            }
                            else
                            {
                                _5851 = false;
                            }
                            if (_5851)
                            {
                                _5851 = _5850.y >= 0;
                            }
                            else
                            {
                                _5851 = false;
                            }
                            if (_5851)
                            {
                                _5851 = _5850.x < int(_9286.x);
                            }
                            else
                            {
                                _5851 = false;
                            }
                            if (_5851)
                            {
                                _5851 = _5850.y < int(_9286.y);
                            }
                            else
                            {
                                _5851 = false;
                            }
                            if (_5851)
                            {
                                vec4 _5836 = texelFetch(_9528, ivec3(_5850, 0).xy, 0);
                                float _5901 = floor((_5836.z * 255.0) + 0.5) + (floor((_5836.y * 255.0) + 0.5) * 256.0);
                                if (_5901 < 0.5)
                                {
                                    _5889 = -1000000015047466219876688855040.0;
                                }
                                else
                                {
                                    _5889 = _9284 + ((_5901 - 1.0) * _9285);
                                }
                                _5826 = _5889;
                            }
                            else
                            {
                                _5826 = -1000000015047466219876688855040.0;
                            }
                            if (_5751.z <= _5826)
                            {
                                float _5712 = _5709;
                                float _5713 = _5749;
                                int _5714 = 0;
                                for (;;)
                                {
                                    if (!(_5714 < 4))
                                    {
                                        break;
                                    }
                                    float _5767 = _5713;
                                    float _5769 = (_5712 + _5767) * 0.5;
                                    vec3 _5771 = _4723 + (_4900 * _5769);
                                    float _9296 = Globals.uTerrainMap1.z;
                                    float _9297 = Globals.uTerrainMap1.w;
                                    vec2 _9298 = Globals.uTerrainMap1.xy;
                                    ivec2 _5938 = ivec2(floor((_5771.xy - Globals.uTerrainMap0.xy) * Globals.uTerrainMap0.z));
                                    if (Globals.uTerrainMap0.w > 0.5)
                                    {
                                        _5939 = _5938.x >= 0;
                                    }
                                    else
                                    {
                                        _5939 = false;
                                    }
                                    if (_5939)
                                    {
                                        _5939 = _5938.y >= 0;
                                    }
                                    else
                                    {
                                        _5939 = false;
                                    }
                                    if (_5939)
                                    {
                                        _5939 = _5938.x < int(_9298.x);
                                    }
                                    else
                                    {
                                        _5939 = false;
                                    }
                                    if (_5939)
                                    {
                                        _5939 = _5938.y < int(_9298.y);
                                    }
                                    else
                                    {
                                        _5939 = false;
                                    }
                                    if (_5939)
                                    {
                                        vec4 _5924 = texelFetch(_9528, ivec3(_5938, 0).xy, 0);
                                        float _5989 = floor((_5924.z * 255.0) + 0.5) + (floor((_5924.y * 255.0) + 0.5) * 256.0);
                                        if (_5989 < 0.5)
                                        {
                                            _5977 = -1000000015047466219876688855040.0;
                                        }
                                        else
                                        {
                                            _5977 = _9296 + ((_5989 - 1.0) * _9297);
                                        }
                                        _5914 = _5977;
                                    }
                                    else
                                    {
                                        _5914 = -1000000015047466219876688855040.0;
                                    }
                                    if (_5771.z <= _5914)
                                    {
                                        _5713 = _5769;
                                    }
                                    else
                                    {
                                        _5712 = _5769;
                                    }
                                    _5714++;
                                    continue;
                                }
                                _5706 = true;
                                _5707 = _5713;
                                break;
                            }
                            if (_5749 >= 32.0)
                            {
                                break;
                            }
                            _5709 = _5749;
                            _5710++;
                            continue;
                        }
                        if (!(_5706))
                        {
                            _5706 = true;
                            _5707 = -1.0;
                        }
                    }
                }
                bool _4654;
                if (_5707 >= 0.0)
                {
                    if (!_4718)
                    {
                        _4654 = true;
                    }
                    else
                    {
                        _4654 = length(_4653 - world) > _5707;
                    }
                }
                else
                {
                    _4654 = false;
                }
                if (_4654)
                {
                    vec3 _4741 = _4720 + (_4900 * _5707);
                    float _4747 = 0.5 * Globals.uClipmapOrigin.data[0].w;
                    vec3 _4655;
                    bool _6007;
                    {
                        _4655 = vec3(0.0);
                        if (Globals.uLookupBuckets <= 0)
                        {
                            _6007 = false;
                        }
                        else
                        {
                            ivec3 _6046 = ivec3(floor((_4741 - Globals.uLookupMin) / vec3(Globals.uLookupCell)));
                            int _6393 = int((((uint(_6046.x) * 2376512323u) ^ (uint(_6046.y) * 3625334849u)) ^ (uint(_6046.z) * 3407524639u)) & uint(Globals.uLookupBuckets - 1));
                            uvec2 _6051 = uvec2(textureSize(_9531, 0));
                            vec2 _6058 = vec2(float(_6051.x), float(_6051.y));
                            int _6010 = 0;
                            float _6011 = 0.0;
                            ivec2 _6012;
                            bool _6023;
                            bool _6024;
                            vec3 _6026[4];
                            float _6029;
                            ivec2 _6394;
                            for (;;)
                            {
                                if (!(_6010 <= uCells._member0[Globals.uLookupBuckets].y))
                                {
                                    break;
                                }
                                bool _6072 = _6010 == 0;
                                if (_6072)
                                {
                                    _6012 = uCells._member0[_6393];
                                }
                                else
                                {
                                    int _6398 = uCells._member0[Globals.uLookupBuckets].x + ((_6010 - 1) * 6);
                                    float _6415 = intBitsToFloat(uCellPages_1._member0[_6398 + 3]) + _4747;
                                    vec3 _6416 = _4741 - vec3(intBitsToFloat(uCellPages_1._member0[_6398]), intBitsToFloat(uCellPages_1._member0[_6398 + 1]), intBitsToFloat(uCellPages_1._member0[_6398 + 2]));
                                    if (dot(_6416, _6416) > (_6415 * _6415))
                                    {
                                        _6394 = ivec2(0);
                                    }
                                    else
                                    {
                                        _6394 = ivec2(uCellPages_1._member0[_6398 + 4], uCellPages_1._member0[_6398 + 5]);
                                    }
                                    _6012 = _6394;
                                }
                                int _6013 = 0;
                                float _6014 = _6011;
                                for (;;)
                                {
                                    bool _6083_ladder_break = false;
                                    {
                                        if (!(_6013 < _6012.y))
                                        {
                                            _6083_ladder_break = true;
                                        }
                                        else
                                        {
                                            int _6094 = _6012.x + _6013;
                                            int _6097 = uCellPages_1._member0[_6094] * 10;
                                            if (!((uPages._member0[_6097 + 5].w > 0.5) == _6072))
                                            {
                                                int _6106 = _6097 + 9;
                                                vec3 _6110 = _4741 - uPages._member0[_6106].xyz;
                                                float _6112 = uPages._member0[_6106].w + _4747;
                                                if (!(dot(_6110, _6110) > (_6112 * _6112)))
                                                {
                                                    int _6432 = uCellPages_1._member0[_6094] * 10;
                                                    vec4 _6021 = uPages._member0[_6432];
                                                    vec4 _6022 = uPages._member0[_6432 + 1];
                                                    int _6438 = _6432 + 2;
                                                    int _6441 = _6432 + 3;
                                                    int _6444 = _6432 + 4;
                                                    int _6450 = _6432 + 6;
                                                    int _6453 = _6432 + 7;
                                                    int _6456 = _6432 + 8;
                                                    vec3 _6463 = _4741 - vec3(uPages._member0[_6450].w, uPages._member0[_6453].w, uPages._member0[_6456].w);
                                                    vec3 _6476 = vec3(dot(uPages._member0[_6450].xyz, _6463), dot(uPages._member0[_6453].xyz, _6463), dot(uPages._member0[_6456].xyz, _6463)) - uPages._member0[_6438].xyz;
                                                    vec2 _6480 = max(vec2(uPages._member0[_6438].w, uPages._member0[_6441].w), vec2(9.9999999747524270787835121154785e-07));
                                                    vec2 _6016 = _6480;
                                                    vec2 _6015 = vec2(dot(_6476, uPages._member0[_6441].xyz), dot(_6476, uPages._member0[_6444].xyz)) / _6480;
                                                    float _6017 = -dot(_6476, uPages._member0[_6432 + 5].xyz);
                                                    float _6018 = uPages._member0[_6444].w;
                                                    float _6121 = dot(uPages._member0[_6432 + 5].xyz, vec3(uPages._member0[_6450].z, uPages._member0[_6453].z, uPages._member0[_6456].z));
                                                    if (!(_6121 < 0.100000001490116119384765625))
                                                    {
                                                        float _6126 = Globals.uClipmapOrigin.data[0].w * 0.25;
                                                        vec2 _6129 = vec2(_6126) / _6016;
                                                        if (abs(_6015.x) > (1.0 + _6129.x))
                                                        {
                                                            _6023 = true;
                                                        }
                                                        else
                                                        {
                                                            _6023 = abs(_6015.y) > (1.0 + _6129.y);
                                                        }
                                                        if (!(_6023))
                                                        {
                                                            if (_6017 < (Globals.uClipmapOrigin.data[0].w * (-0.25)))
                                                            {
                                                                _6024 = true;
                                                            }
                                                            else
                                                            {
                                                                _6024 = _6017 > (_6018 + _6126);
                                                            }
                                                            if (!(_6024))
                                                            {
                                                                vec2 _6173 = (_6022.xy + (((clamp(_6015, vec2(-1.0), vec2(1.0)) * 0.5) + vec2(0.5)) * _6022.zw)) - vec2(0.5);
                                                                vec2 _6174 = floor(_6173);
                                                                vec2 _6175 = _6173 - _6174;
                                                                vec2 _6181 = _6021.zw / max(_6022.zw, vec2(9.9999999747524270787835121154785e-07));
                                                                float _6185 = _6017 / max(_6018, 9.9999999747524270787835121154785e-07);
                                                                float _6189 = _4747 / max(0.5 * _6018, 9.9999999747524270787835121154785e-07);
                                                                vec4 _6025 = vec4(0.0);
                                                                int _6028 = 0;
                                                                for (;;)
                                                                {
                                                                    if (!(_6028 < 4))
                                                                    {
                                                                        break;
                                                                    }
                                                                    vec2 _6204 = vec2(float(_6028 & 1), float(_6028 >> 1));
                                                                    vec2 _6207 = (_6174 + _6204) + vec2(0.5);
                                                                    vec2 _6208 = _6207 / _6058;
                                                                    if (textureLod(_9533, _6208, 0.0).w >= 0.5)
                                                                    {
                                                                        _6029 = 1.0;
                                                                    }
                                                                    else
                                                                    {
                                                                        _6029 = 0.0;
                                                                    }
                                                                    vec2 _6237 = mix(vec2(1.0) - _6175, _6175, _6204);
                                                                    _6025[_6028] = ((_6237.x * _6237.y) * _6029) * (1.0 - clamp((abs(_6185 - textureLod(_9535, _6208, 0.0).x) - _6189) / (0.25 * _6189), 0.0, 1.0));
                                                                    _6026[_6028] = textureLod(_9537, (floor(_6021.xy + ((_6207 - _6022.xy) * _6181)) + vec2(0.5)) / Globals.uAtlasSize.xy, 0.0).xyz;
                                                                    _6028++;
                                                                    continue;
                                                                }
                                                                float _6284 = ((_6025.x + _6025.y) + _6025.z) + _6025.w;
                                                                float _6285 = (_6121 * _6121) * _6284;
                                                                if (!(_6285 <= 0.0))
                                                                {
                                                                    _4655 += (max(((((_6026[0] * _6025.x) + (_6026[1] * _6025.y)) + (_6026[2] * _6025.z)) + (_6026[3] * _6025.w)) / vec3(_6284), vec3(0.0)) * _6285);
                                                                    _6014 += _6285;
                                                                }
                                                            }
                                                        }
                                                    }
                                                }
                                            }
                                        }
                                    }
                                    if (_6083_ladder_break)
                                    {
                                        break;
                                    }
                                    _6013++;
                                    continue;
                                }
                                _6010++;
                                _6011 = _6014;
                                continue;
                            }
                            if (_6011 <= 0.0)
                            {
                                _4655 = vec3(0.0);
                                _6007 = false;
                            }
                            else
                            {
                                _4655 /= vec3(_6011);
                                _6007 = true;
                            }
                        }
                    }
                    if (_6007)
                    {
                        vec3 _4753 = _4655 * Globals.uBounceScale;
                        float _6498 = max(_4753.x, max(_4753.y, _4753.z));
                        vec3 _6491;
                        if (_6498 > Globals.uMaxRayIntensity)
                        {
                            _6491 = _4753 * (Globals.uMaxRayIntensity / _6498);
                        }
                        else
                        {
                            _6491 = _4753;
                        }
                        _9308 = _6491;
                        _9309 = 1.0;
                        _9312 = true;
                        _4650 = true;
                        _4648 = 2;
                        _4649 = _4753;
                    }
                    _4654 = false;
                }
                else
                {
                    _4654 = _4718;
                }
                if (_4650)
                {
                    _4652 = 1.0;
                }
                else
                {
                    if (_4654)
                    {
                        float _4780 = 1.5 * Globals.uClipmapOrigin.data[0].w;
                        vec3 _6632;
                        {
                            _6679 = false;
                            bool _6633;
                            float _6635;
                            {
                                _6633 = false;
                                _6635 = Globals.uClipmapOrigin.data[0].w;
                                int _6681 = 0;
                                bool _6682;
                                for (;;)
                                {
                                    bool _6693_ladder_break = false;
                                    {
                                        if (!(_6681 < Globals.uShadowLevels))
                                        {
                                            _6693_ladder_break = true;
                                        }
                                        else
                                        {
                                            vec3 _6708 = (_4653 - Globals.uClipmapOrigin.data[_6681].xyz) / vec3(Globals.uClipmapOrigin.data[_6681].w);
                                            if (any(lessThan(_6708, vec3(0.5))))
                                            {
                                                _6682 = true;
                                            }
                                            else
                                            {
                                                _6682 = any(greaterThan(_6708, vec3(float(Globals.uShadowResolution) - 0.5, float(Globals.uShadowResolution) - 0.5, float(Globals.uShadowResolution) - 0.5)));
                                            }
                                            if (!(_6682))
                                            {
                                                _6633 = true;
                                                _6635 = Globals.uClipmapOrigin.data[_6681].w;
                                                _6679 = true;
                                                _6693_ladder_break = true;
                                            }
                                        }
                                    }
                                    if (_6693_ladder_break)
                                    {
                                        break;
                                    }
                                    _6681++;
                                    continue;
                                }
                                if (!(_6679))
                                {
                                    _6679 = true;
                                }
                            }
                            if (!_6633)
                            {
                                _6632 = vec3(0.0);
                            }
                            else
                            {
                                float _6649 = 0.5 * _6635;
                                vec3 _6650 = vec3(_6649, 0.0, 0.0);
                                vec3 _6651 = _4653 + _6650;
                                _6777 = false;
                                float _6778;
                                {
                                    int _6779 = 0;
                                    bool _6780;
                                    for (;;)
                                    {
                                        bool _6791_ladder_break = false;
                                        {
                                            if (!(_6779 < Globals.uShadowLevels))
                                            {
                                                _6791_ladder_break = true;
                                            }
                                            else
                                            {
                                                vec3 _6806 = (_6651 - Globals.uClipmapOrigin.data[_6779].xyz) / vec3(Globals.uClipmapOrigin.data[_6779].w);
                                                if (any(lessThan(_6806, vec3(0.5))))
                                                {
                                                    _6780 = true;
                                                }
                                                else
                                                {
                                                    _6780 = any(greaterThan(_6806, vec3(float(Globals.uShadowResolution) - 0.5, float(Globals.uShadowResolution) - 0.5, float(Globals.uShadowResolution) - 0.5)));
                                                }
                                                if (!(_6780))
                                                {
                                                    _6777 = true;
                                                    _6778 = ((textureLod(_9525, vec3(_6806.x / float(Globals.uShadowResolution), _6806.y / float(Globals.uShadowResolution), ((float(_6779) * float(Globals.uShadowResolution)) + _6806.z) / float(Globals.uShadowResolution * Globals.uShadowLevels)), 0.0).x * 2.0) - 1.0) * Globals.uClipmapRange.data[_6779];
                                                    _6791_ladder_break = true;
                                                }
                                            }
                                        }
                                        if (_6791_ladder_break)
                                        {
                                            break;
                                        }
                                        _6779++;
                                        continue;
                                    }
                                    if (!(_6777))
                                    {
                                        _6777 = true;
                                        _6778 = 1000000.0;
                                    }
                                }
                                vec3 _6653 = _4653 - _6650;
                                _6875 = false;
                                float _6876;
                                {
                                    int _6877 = 0;
                                    bool _6878;
                                    for (;;)
                                    {
                                        bool _6889_ladder_break = false;
                                        {
                                            if (!(_6877 < Globals.uShadowLevels))
                                            {
                                                _6889_ladder_break = true;
                                            }
                                            else
                                            {
                                                vec3 _6904 = (_6653 - Globals.uClipmapOrigin.data[_6877].xyz) / vec3(Globals.uClipmapOrigin.data[_6877].w);
                                                if (any(lessThan(_6904, vec3(0.5))))
                                                {
                                                    _6878 = true;
                                                }
                                                else
                                                {
                                                    _6878 = any(greaterThan(_6904, vec3(float(Globals.uShadowResolution) - 0.5, float(Globals.uShadowResolution) - 0.5, float(Globals.uShadowResolution) - 0.5)));
                                                }
                                                if (!(_6878))
                                                {
                                                    _6875 = true;
                                                    _6876 = ((textureLod(_9525, vec3(_6904.x / float(Globals.uShadowResolution), _6904.y / float(Globals.uShadowResolution), ((float(_6877) * float(Globals.uShadowResolution)) + _6904.z) / float(Globals.uShadowResolution * Globals.uShadowLevels)), 0.0).x * 2.0) - 1.0) * Globals.uClipmapRange.data[_6877];
                                                    _6889_ladder_break = true;
                                                }
                                            }
                                        }
                                        if (_6889_ladder_break)
                                        {
                                            break;
                                        }
                                        _6877++;
                                        continue;
                                    }
                                    if (!(_6875))
                                    {
                                        _6875 = true;
                                        _6876 = 1000000.0;
                                    }
                                }
                                vec3 _6656 = vec3(0.0, _6649, 0.0);
                                vec3 _6657 = _4653 + _6656;
                                _6973 = false;
                                float _6974;
                                {
                                    int _6975 = 0;
                                    bool _6976;
                                    for (;;)
                                    {
                                        bool _6987_ladder_break = false;
                                        {
                                            if (!(_6975 < Globals.uShadowLevels))
                                            {
                                                _6987_ladder_break = true;
                                            }
                                            else
                                            {
                                                vec3 _7002 = (_6657 - Globals.uClipmapOrigin.data[_6975].xyz) / vec3(Globals.uClipmapOrigin.data[_6975].w);
                                                if (any(lessThan(_7002, vec3(0.5))))
                                                {
                                                    _6976 = true;
                                                }
                                                else
                                                {
                                                    _6976 = any(greaterThan(_7002, vec3(float(Globals.uShadowResolution) - 0.5, float(Globals.uShadowResolution) - 0.5, float(Globals.uShadowResolution) - 0.5)));
                                                }
                                                if (!(_6976))
                                                {
                                                    _6973 = true;
                                                    _6974 = ((textureLod(_9525, vec3(_7002.x / float(Globals.uShadowResolution), _7002.y / float(Globals.uShadowResolution), ((float(_6975) * float(Globals.uShadowResolution)) + _7002.z) / float(Globals.uShadowResolution * Globals.uShadowLevels)), 0.0).x * 2.0) - 1.0) * Globals.uClipmapRange.data[_6975];
                                                    _6987_ladder_break = true;
                                                }
                                            }
                                        }
                                        if (_6987_ladder_break)
                                        {
                                            break;
                                        }
                                        _6975++;
                                        continue;
                                    }
                                    if (!(_6973))
                                    {
                                        _6973 = true;
                                        _6974 = 1000000.0;
                                    }
                                }
                                vec3 _6659 = _4653 - _6656;
                                _7071 = false;
                                float _7072;
                                {
                                    int _7073 = 0;
                                    bool _7074;
                                    for (;;)
                                    {
                                        bool _7085_ladder_break = false;
                                        {
                                            if (!(_7073 < Globals.uShadowLevels))
                                            {
                                                _7085_ladder_break = true;
                                            }
                                            else
                                            {
                                                vec3 _7100 = (_6659 - Globals.uClipmapOrigin.data[_7073].xyz) / vec3(Globals.uClipmapOrigin.data[_7073].w);
                                                if (any(lessThan(_7100, vec3(0.5))))
                                                {
                                                    _7074 = true;
                                                }
                                                else
                                                {
                                                    _7074 = any(greaterThan(_7100, vec3(float(Globals.uShadowResolution) - 0.5, float(Globals.uShadowResolution) - 0.5, float(Globals.uShadowResolution) - 0.5)));
                                                }
                                                if (!(_7074))
                                                {
                                                    _7071 = true;
                                                    _7072 = ((textureLod(_9525, vec3(_7100.x / float(Globals.uShadowResolution), _7100.y / float(Globals.uShadowResolution), ((float(_7073) * float(Globals.uShadowResolution)) + _7100.z) / float(Globals.uShadowResolution * Globals.uShadowLevels)), 0.0).x * 2.0) - 1.0) * Globals.uClipmapRange.data[_7073];
                                                    _7085_ladder_break = true;
                                                }
                                            }
                                        }
                                        if (_7085_ladder_break)
                                        {
                                            break;
                                        }
                                        _7073++;
                                        continue;
                                    }
                                    if (!(_7071))
                                    {
                                        _7071 = true;
                                        _7072 = 1000000.0;
                                    }
                                }
                                vec3 _6662 = vec3(0.0, 0.0, _6649);
                                vec3 _6663 = _4653 + _6662;
                                _7169 = false;
                                float _7170;
                                {
                                    int _7171 = 0;
                                    bool _7172;
                                    for (;;)
                                    {
                                        bool _7183_ladder_break = false;
                                        {
                                            if (!(_7171 < Globals.uShadowLevels))
                                            {
                                                _7183_ladder_break = true;
                                            }
                                            else
                                            {
                                                vec3 _7198 = (_6663 - Globals.uClipmapOrigin.data[_7171].xyz) / vec3(Globals.uClipmapOrigin.data[_7171].w);
                                                if (any(lessThan(_7198, vec3(0.5))))
                                                {
                                                    _7172 = true;
                                                }
                                                else
                                                {
                                                    _7172 = any(greaterThan(_7198, vec3(float(Globals.uShadowResolution) - 0.5, float(Globals.uShadowResolution) - 0.5, float(Globals.uShadowResolution) - 0.5)));
                                                }
                                                if (!(_7172))
                                                {
                                                    _7169 = true;
                                                    _7170 = ((textureLod(_9525, vec3(_7198.x / float(Globals.uShadowResolution), _7198.y / float(Globals.uShadowResolution), ((float(_7171) * float(Globals.uShadowResolution)) + _7198.z) / float(Globals.uShadowResolution * Globals.uShadowLevels)), 0.0).x * 2.0) - 1.0) * Globals.uClipmapRange.data[_7171];
                                                    _7183_ladder_break = true;
                                                }
                                            }
                                        }
                                        if (_7183_ladder_break)
                                        {
                                            break;
                                        }
                                        _7171++;
                                        continue;
                                    }
                                    if (!(_7169))
                                    {
                                        _7169 = true;
                                        _7170 = 1000000.0;
                                    }
                                }
                                vec3 _6665 = _4653 - _6662;
                                _7267 = false;
                                float _7268;
                                {
                                    int _7269 = 0;
                                    bool _7270;
                                    for (;;)
                                    {
                                        bool _7281_ladder_break = false;
                                        {
                                            if (!(_7269 < Globals.uShadowLevels))
                                            {
                                                _7281_ladder_break = true;
                                            }
                                            else
                                            {
                                                vec3 _7296 = (_6665 - Globals.uClipmapOrigin.data[_7269].xyz) / vec3(Globals.uClipmapOrigin.data[_7269].w);
                                                if (any(lessThan(_7296, vec3(0.5))))
                                                {
                                                    _7270 = true;
                                                }
                                                else
                                                {
                                                    _7270 = any(greaterThan(_7296, vec3(float(Globals.uShadowResolution) - 0.5, float(Globals.uShadowResolution) - 0.5, float(Globals.uShadowResolution) - 0.5)));
                                                }
                                                if (!(_7270))
                                                {
                                                    _7267 = true;
                                                    _7268 = ((textureLod(_9525, vec3(_7296.x / float(Globals.uShadowResolution), _7296.y / float(Globals.uShadowResolution), ((float(_7269) * float(Globals.uShadowResolution)) + _7296.z) / float(Globals.uShadowResolution * Globals.uShadowLevels)), 0.0).x * 2.0) - 1.0) * Globals.uClipmapRange.data[_7269];
                                                    _7281_ladder_break = true;
                                                }
                                            }
                                        }
                                        if (_7281_ladder_break)
                                        {
                                            break;
                                        }
                                        _7269++;
                                        continue;
                                    }
                                    if (!(_7267))
                                    {
                                        _7267 = true;
                                        _7268 = 1000000.0;
                                    }
                                }
                                vec3 _6668 = vec3(_6778 - _6876, _6974 - _7072, _7170 - _7268);
                                float _6669 = dot(_6668, _6668);
                                vec3 _6639;
                                if (_6669 < 1.0000000133514319600180897396058e-10)
                                {
                                    _6639 = vec3(0.0);
                                }
                                else
                                {
                                    _6639 = _6668 * inversesqrt(_6669);
                                }
                                _6632 = _6639;
                            }
                        }
                        vec3 _4795 = world + Globals.uTraceOrigin;
                        bool _7366;
                        {
                            float _9182 = Globals.uShelterMap1.z;
                            float _9183 = Globals.uShelterMap1.w;
                            float _9184 = Globals.uShelterMap2.x;
                            float _9185 = Globals.uShelterMap2.y;
                            ivec2 _7431 = ivec2(floor((_4795.xy - Globals.uShelterMap0.xy) * Globals.uShelterMap0.z));
                            vec2 _9228 = Globals.uShelterMap1.xy;
                            bool _7432;
                            if (Globals.uShelterMap0.w > 0.5)
                            {
                                _7432 = _7431.x >= 0;
                            }
                            else
                            {
                                _7432 = false;
                            }
                            if (_7432)
                            {
                                _7432 = _7431.y >= 0;
                            }
                            else
                            {
                                _7432 = false;
                            }
                            if (_7432)
                            {
                                _7432 = _7431.x < int(_9228.x);
                            }
                            else
                            {
                                _7432 = false;
                            }
                            if (_7432)
                            {
                                _7432 = _7431.y < int(_9228.y);
                            }
                            else
                            {
                                _7432 = false;
                            }
                            if (!_7432)
                            {
                                _7366 = false;
                            }
                            else
                            {
                                vec4 _7415 = texelFetch(_9540, ivec3(_7431, 0).xy, 0);
                                float _9232 = _9182;
                                float _9233 = _9183;
                                float _7482 = floor((_7415.z * 255.0) + 0.5) + (floor((_7415.y * 255.0) + 0.5) * 256.0);
                                float _7470;
                                if (_7482 < 0.5)
                                {
                                    _7470 = -1000000015047466219876688855040.0;
                                }
                                else
                                {
                                    _7470 = _9232 + ((_7482 - 1.0) * _9233);
                                }
                                float _7418 = _4795.z;
                                float _9237 = _9185;
                                bool _7495;
                                if (_7470 > (_7418 + _9184))
                                {
                                    _7495 = _7470 < (_7418 + _9237);
                                }
                                else
                                {
                                    _7495 = false;
                                }
                                _7366 = _7495;
                            }
                        }
                        if (!_7366)
                        {
                            vec3 _4802 = _4653 + Globals.uTraceOrigin;
                            bool _7511;
                            {
                                float _9120 = Globals.uShelterMap1.z;
                                float _9121 = Globals.uShelterMap1.w;
                                float _9122 = Globals.uShelterMap2.x;
                                float _9123 = Globals.uShelterMap2.y;
                                ivec2 _7576 = ivec2(floor((_4802.xy - Globals.uShelterMap0.xy) * Globals.uShelterMap0.z));
                                vec2 _9168 = Globals.uShelterMap1.xy;
                                bool _7577;
                                if (Globals.uShelterMap0.w > 0.5)
                                {
                                    _7577 = _7576.x >= 0;
                                }
                                else
                                {
                                    _7577 = false;
                                }
                                if (_7577)
                                {
                                    _7577 = _7576.y >= 0;
                                }
                                else
                                {
                                    _7577 = false;
                                }
                                if (_7577)
                                {
                                    _7577 = _7576.x < int(_9168.x);
                                }
                                else
                                {
                                    _7577 = false;
                                }
                                if (_7577)
                                {
                                    _7577 = _7576.y < int(_9168.y);
                                }
                                else
                                {
                                    _7577 = false;
                                }
                                if (!_7577)
                                {
                                    _7511 = false;
                                }
                                else
                                {
                                    vec4 _7560 = texelFetch(_9540, ivec3(_7576, 0).xy, 0);
                                    float _9172 = _9120;
                                    float _9173 = _9121;
                                    float _7627 = floor((_7560.z * 255.0) + 0.5) + (floor((_7560.y * 255.0) + 0.5) * 256.0);
                                    float _7615;
                                    if (_7627 < 0.5)
                                    {
                                        _7615 = -1000000015047466219876688855040.0;
                                    }
                                    else
                                    {
                                        _7615 = _9172 + ((_7627 - 1.0) * _9173);
                                    }
                                    float _7563 = _4802.z;
                                    float _9177 = _9123;
                                    bool _7640;
                                    if (_7615 > (_7563 + _9122))
                                    {
                                        _7640 = _7615 < (_7563 + _9177);
                                    }
                                    else
                                    {
                                        _7640 = false;
                                    }
                                    _7511 = _7640;
                                }
                            }
                            _4650 = _7511;
                        }
                        else
                        {
                            _4650 = false;
                        }
                        float _4659;
                        if (_4650)
                        {
                            _9311 += 1.0;
                            _9314 = true;
                            _4659 = 0.0;
                        }
                        else
                        {
                            _4659 = smoothstep(_4780, 2.0 * Globals.uClipmapOrigin.data[0].w, length(_4653 - world)) * smoothstep(0.75 * Globals.uClipmapOrigin.data[0].w, _4780, dot(_4653 - world, worldNormal));
                        }
                        vec3 _4660;
                        if (dot(_6632, _4900) > 0.0)
                        {
                            _4660 = -_6632;
                        }
                        else
                        {
                            _4660 = _6632;
                        }
                        if (_4659 <= 0.0)
                        {
                            _4652 = 0.0;
                        }
                        else
                        {
                            vec3 _4661;
                            if (dot(_4660, _4660) > 0.5)
                            {
                                float _4827 = 0.5 * Globals.uClipmapOrigin.data[0].w;
                                bool _7660;
                                {
                                    _4661 = vec3(0.0);
                                    if (Globals.uLookupBuckets <= 0)
                                    {
                                        _7660 = false;
                                    }
                                    else
                                    {
                                        ivec3 _7699 = ivec3(floor((_4653 - Globals.uLookupMin) / vec3(Globals.uLookupCell)));
                                        int _8046 = int((((uint(_7699.x) * 2376512323u) ^ (uint(_7699.y) * 3625334849u)) ^ (uint(_7699.z) * 3407524639u)) & uint(Globals.uLookupBuckets - 1));
                                        uvec2 _7704 = uvec2(textureSize(_9531, 0));
                                        vec2 _7711 = vec2(float(_7704.x), float(_7704.y));
                                        int _7663 = 0;
                                        float _7664 = 0.0;
                                        ivec2 _7665;
                                        bool _7676;
                                        bool _7677;
                                        vec3 _7679[4];
                                        float _7682;
                                        ivec2 _8047;
                                        for (;;)
                                        {
                                            if (!(_7663 <= uCells._member0[Globals.uLookupBuckets].y))
                                            {
                                                break;
                                            }
                                            bool _7725 = _7663 == 0;
                                            if (_7725)
                                            {
                                                _7665 = uCells._member0[_8046];
                                            }
                                            else
                                            {
                                                int _8051 = uCells._member0[Globals.uLookupBuckets].x + ((_7663 - 1) * 6);
                                                float _8068 = intBitsToFloat(uCellPages_1._member0[_8051 + 3]) + _4827;
                                                vec3 _8069 = _4653 - vec3(intBitsToFloat(uCellPages_1._member0[_8051]), intBitsToFloat(uCellPages_1._member0[_8051 + 1]), intBitsToFloat(uCellPages_1._member0[_8051 + 2]));
                                                if (dot(_8069, _8069) > (_8068 * _8068))
                                                {
                                                    _8047 = ivec2(0);
                                                }
                                                else
                                                {
                                                    _8047 = ivec2(uCellPages_1._member0[_8051 + 4], uCellPages_1._member0[_8051 + 5]);
                                                }
                                                _7665 = _8047;
                                            }
                                            int _7666 = 0;
                                            float _7667 = _7664;
                                            for (;;)
                                            {
                                                bool _7736_ladder_break = false;
                                                {
                                                    if (!(_7666 < _7665.y))
                                                    {
                                                        _7736_ladder_break = true;
                                                    }
                                                    else
                                                    {
                                                        int _7747 = _7665.x + _7666;
                                                        int _7750 = uCellPages_1._member0[_7747] * 10;
                                                        if (!((uPages._member0[_7750 + 5].w > 0.5) == _7725))
                                                        {
                                                            int _7759 = _7750 + 9;
                                                            vec3 _7763 = _4653 - uPages._member0[_7759].xyz;
                                                            float _7765 = uPages._member0[_7759].w + _4827;
                                                            if (!(dot(_7763, _7763) > (_7765 * _7765)))
                                                            {
                                                                int _8085 = uCellPages_1._member0[_7747] * 10;
                                                                vec4 _7674 = uPages._member0[_8085];
                                                                vec4 _7675 = uPages._member0[_8085 + 1];
                                                                int _8091 = _8085 + 2;
                                                                int _8094 = _8085 + 3;
                                                                int _8097 = _8085 + 4;
                                                                int _8103 = _8085 + 6;
                                                                int _8106 = _8085 + 7;
                                                                int _8109 = _8085 + 8;
                                                                vec3 _8116 = _4653 - vec3(uPages._member0[_8103].w, uPages._member0[_8106].w, uPages._member0[_8109].w);
                                                                vec3 _8129 = vec3(dot(uPages._member0[_8103].xyz, _8116), dot(uPages._member0[_8106].xyz, _8116), dot(uPages._member0[_8109].xyz, _8116)) - uPages._member0[_8091].xyz;
                                                                vec2 _8133 = max(vec2(uPages._member0[_8091].w, uPages._member0[_8094].w), vec2(9.9999999747524270787835121154785e-07));
                                                                vec2 _7669 = _8133;
                                                                vec2 _7668 = vec2(dot(_8129, uPages._member0[_8094].xyz), dot(_8129, uPages._member0[_8097].xyz)) / _8133;
                                                                float _7670 = -dot(_8129, uPages._member0[_8085 + 5].xyz);
                                                                float _7671 = uPages._member0[_8097].w;
                                                                float _7774 = dot(uPages._member0[_8085 + 5].xyz, vec3(dot(uPages._member0[_8103].xyz, _4660), dot(uPages._member0[_8106].xyz, _4660), dot(uPages._member0[_8109].xyz, _4660)));
                                                                if (!(_7774 < 0.100000001490116119384765625))
                                                                {
                                                                    float _7779 = Globals.uClipmapOrigin.data[0].w * 0.25;
                                                                    vec2 _7782 = vec2(_7779) / _7669;
                                                                    if (abs(_7668.x) > (1.0 + _7782.x))
                                                                    {
                                                                        _7676 = true;
                                                                    }
                                                                    else
                                                                    {
                                                                        _7676 = abs(_7668.y) > (1.0 + _7782.y);
                                                                    }
                                                                    if (!(_7676))
                                                                    {
                                                                        if (_7670 < (Globals.uClipmapOrigin.data[0].w * (-0.25)))
                                                                        {
                                                                            _7677 = true;
                                                                        }
                                                                        else
                                                                        {
                                                                            _7677 = _7670 > (_7671 + _7779);
                                                                        }
                                                                        if (!(_7677))
                                                                        {
                                                                            vec2 _7826 = (_7675.xy + (((clamp(_7668, vec2(-1.0), vec2(1.0)) * 0.5) + vec2(0.5)) * _7675.zw)) - vec2(0.5);
                                                                            vec2 _7827 = floor(_7826);
                                                                            vec2 _7828 = _7826 - _7827;
                                                                            vec2 _7834 = _7674.zw / max(_7675.zw, vec2(9.9999999747524270787835121154785e-07));
                                                                            float _7838 = _7670 / max(_7671, 9.9999999747524270787835121154785e-07);
                                                                            float _7842 = _4827 / max(0.5 * _7671, 9.9999999747524270787835121154785e-07);
                                                                            vec4 _7678 = vec4(0.0);
                                                                            int _7681 = 0;
                                                                            for (;;)
                                                                            {
                                                                                if (!(_7681 < 4))
                                                                                {
                                                                                    break;
                                                                                }
                                                                                vec2 _7857 = vec2(float(_7681 & 1), float(_7681 >> 1));
                                                                                vec2 _7860 = (_7827 + _7857) + vec2(0.5);
                                                                                vec2 _7861 = _7860 / _7711;
                                                                                if (textureLod(_9533, _7861, 0.0).w >= 0.5)
                                                                                {
                                                                                    _7682 = 1.0;
                                                                                }
                                                                                else
                                                                                {
                                                                                    _7682 = 0.0;
                                                                                }
                                                                                vec2 _7890 = mix(vec2(1.0) - _7828, _7828, _7857);
                                                                                _7678[_7681] = ((_7890.x * _7890.y) * _7682) * (1.0 - clamp((abs(_7838 - textureLod(_9535, _7861, 0.0).x) - _7842) / (0.25 * _7842), 0.0, 1.0));
                                                                                _7679[_7681] = textureLod(_9537, (floor(_7674.xy + ((_7860 - _7675.xy) * _7834)) + vec2(0.5)) / Globals.uAtlasSize.xy, 0.0).xyz;
                                                                                _7681++;
                                                                                continue;
                                                                            }
                                                                            float _7937 = ((_7678.x + _7678.y) + _7678.z) + _7678.w;
                                                                            float _7938 = (_7774 * _7774) * _7937;
                                                                            if (!(_7938 <= 0.0))
                                                                            {
                                                                                _4661 += (max(((((_7679[0] * _7678.x) + (_7679[1] * _7678.y)) + (_7679[2] * _7678.z)) + (_7679[3] * _7678.w)) / vec3(_7937), vec3(0.0)) * _7938);
                                                                                _7667 += _7938;
                                                                            }
                                                                        }
                                                                    }
                                                                }
                                                            }
                                                        }
                                                    }
                                                }
                                                if (_7736_ladder_break)
                                                {
                                                    break;
                                                }
                                                _7666++;
                                                continue;
                                            }
                                            _7663++;
                                            _7664 = _7667;
                                            continue;
                                        }
                                        if (_7664 <= 0.0)
                                        {
                                            _4661 = vec3(0.0);
                                            _7660 = false;
                                        }
                                        else
                                        {
                                            _4661 /= vec3(_7664);
                                            _7660 = true;
                                        }
                                    }
                                }
                                _4650 = _7660;
                            }
                            else
                            {
                                _4650 = false;
                            }
                            if (_4650)
                            {
                                vec3 _4858 = _4661 * Globals.uBounceScale;
                                float _8525 = max(_4858.x, max(_4858.y, _4858.z));
                                vec3 _8518;
                                if (_8525 > Globals.uMaxRayIntensity)
                                {
                                    _8518 = _4858 * (Globals.uMaxRayIntensity / _8525);
                                }
                                else
                                {
                                    _8518 = _4858;
                                }
                                _9308 = _8518;
                                _9309 = _4659;
                                _9312 = true;
                                _4648 = 2;
                                _4649 = _4858;
                                _4652 = _4659;
                            }
                            else
                            {
                                vec2 _4657;
                                bool _8149;
                                {
                                    _4657 = vec2(0.0);
                                    float _4658 = 1000000000.0;
                                    if (Globals.uLookupBuckets <= 0)
                                    {
                                        _8149 = false;
                                    }
                                    else
                                    {
                                        ivec3 _8190 = ivec3(floor((_4653 - Globals.uLookupMin) / vec3(Globals.uLookupCell)));
                                        int _8402 = int((((uint(_8190.x) * 2376512323u) ^ (uint(_8190.y) * 3625334849u)) ^ (uint(_8190.z) * 3407524639u)) & uint(Globals.uLookupBuckets - 1));
                                        uvec2 _8195 = uvec2(textureSize(_9531, 0));
                                        vec2 _8202 = vec2(float(_8195.x), float(_8195.y));
                                        bool _8152 = false;
                                        bool _8153 = false;
                                        int _8154 = 0;
                                        bool _8155;
                                        ivec2 _8156;
                                        bool _8160;
                                        bool _8169;
                                        bool _8170;
                                        bool _8171;
                                        bool _8172;
                                        bool _8173;
                                        ivec2 _8403;
                                        for (;;)
                                        {
                                            if (_8154 <= uCells._member0[Globals.uLookupBuckets].y)
                                            {
                                                _8155 = !_8152;
                                            }
                                            else
                                            {
                                                _8155 = false;
                                            }
                                            if (!_8155)
                                            {
                                                break;
                                            }
                                            bool _8222 = _8154 == 0;
                                            if (_8222)
                                            {
                                                _8156 = uCells._member0[_8402];
                                            }
                                            else
                                            {
                                                int _8407 = uCells._member0[Globals.uLookupBuckets].x + ((_8154 - 1) * 6);
                                                float _8423 = intBitsToFloat(uCellPages_1._member0[_8407 + 3]);
                                                vec3 _8425 = _4653 - vec3(intBitsToFloat(uCellPages_1._member0[_8407]), intBitsToFloat(uCellPages_1._member0[_8407 + 1]), intBitsToFloat(uCellPages_1._member0[_8407 + 2]));
                                                if (dot(_8425, _8425) > (_8423 * _8423))
                                                {
                                                    _8403 = ivec2(0);
                                                }
                                                else
                                                {
                                                    _8403 = ivec2(uCellPages_1._member0[_8407 + 4], uCellPages_1._member0[_8407 + 5]);
                                                }
                                                _8156 = _8403;
                                            }
                                            bool _8157 = _8152;
                                            bool _8158 = _8153;
                                            int _8159 = 0;
                                            for (;;)
                                            {
                                                bool _8234_ladder_break = false;
                                                {
                                                    if (_8159 < _8156.y)
                                                    {
                                                        _8160 = !_8157;
                                                    }
                                                    else
                                                    {
                                                        _8160 = false;
                                                    }
                                                    if (!_8160)
                                                    {
                                                        _8234_ladder_break = true;
                                                    }
                                                    else
                                                    {
                                                        int _8251 = _8156.x + _8159;
                                                        int _8254 = uCellPages_1._member0[_8251] * 10;
                                                        if (!((uPages._member0[_8254 + 5].w > 0.5) == _8222))
                                                        {
                                                            int _8263 = _8254 + 9;
                                                            vec3 _8267 = _4653 - uPages._member0[_8263].xyz;
                                                            if (!(dot(_8267, _8267) > (uPages._member0[_8263].w * uPages._member0[_8263].w)))
                                                            {
                                                                vec3 _8274 = -_4900;
                                                                int _8441 = uCellPages_1._member0[_8251] * 10;
                                                                vec4 _8167 = uPages._member0[_8441];
                                                                vec4 _8168 = uPages._member0[_8441 + 1];
                                                                int _8447 = _8441 + 2;
                                                                int _8450 = _8441 + 3;
                                                                int _8453 = _8441 + 4;
                                                                int _8459 = _8441 + 6;
                                                                int _8462 = _8441 + 7;
                                                                int _8465 = _8441 + 8;
                                                                vec3 _8472 = _4653 - vec3(uPages._member0[_8459].w, uPages._member0[_8462].w, uPages._member0[_8465].w);
                                                                vec3 _8166 = vec3(dot(uPages._member0[_8459].xyz, _8274), dot(uPages._member0[_8462].xyz, _8274), dot(uPages._member0[_8465].xyz, _8274));
                                                                vec3 _8485 = vec3(dot(uPages._member0[_8459].xyz, _8472), dot(uPages._member0[_8462].xyz, _8472), dot(uPages._member0[_8465].xyz, _8472)) - uPages._member0[_8447].xyz;
                                                                vec2 _8495 = vec2(dot(_8485, uPages._member0[_8450].xyz), dot(_8485, uPages._member0[_8453].xyz)) / max(vec2(uPages._member0[_8447].w, uPages._member0[_8450].w), vec2(9.9999999747524270787835121154785e-07));
                                                                vec2 _8161 = _8495;
                                                                float _8163 = -dot(_8485, uPages._member0[_8441 + 5].xyz);
                                                                float _8164 = uPages._member0[_8453].w;
                                                                vec3 _8165 = uPages._member0[_8441 + 5].xyz;
                                                                if (abs(_8495.x) > 1.0)
                                                                {
                                                                    _8169 = true;
                                                                }
                                                                else
                                                                {
                                                                    _8169 = abs(_8161.y) > 1.0;
                                                                }
                                                                if (!(_8169))
                                                                {
                                                                    if (dot(_4900, _4900) > 0.5)
                                                                    {
                                                                        _8170 = dot(_8165, _8166) < (-0.0500000007450580596923828125);
                                                                    }
                                                                    else
                                                                    {
                                                                        _8170 = false;
                                                                    }
                                                                    if (!(_8170))
                                                                    {
                                                                        if (_8163 < (-0.25))
                                                                        {
                                                                            _8171 = true;
                                                                        }
                                                                        else
                                                                        {
                                                                            _8171 = _8163 > (_8164 + 0.25);
                                                                        }
                                                                        if (!(_8171))
                                                                        {
                                                                            vec2 _8318 = (_8161 * 0.5) + vec2(0.5);
                                                                            vec2 _8325 = (_8168.xy + (_8318 * _8168.zw)) / _8202;
                                                                            if (!(textureLod(_9533, _8325, 0.0).w < 0.5))
                                                                            {
                                                                                float _8345 = abs((textureLod(_9535, _8325, 0.0).x * _8164) - _8163);
                                                                                if (_8345 < _4658)
                                                                                {
                                                                                    _4658 = _8345;
                                                                                    _4657 = (_8167.xy + (_8318 * _8167.zw)) / Globals.uAtlasSize.xy;
                                                                                    _8172 = _8345 < 0.0199999995529651641845703125;
                                                                                    _8173 = true;
                                                                                }
                                                                                else
                                                                                {
                                                                                    _8172 = _8157;
                                                                                    _8173 = _8158;
                                                                                }
                                                                                _8157 = _8172;
                                                                                _8158 = _8173;
                                                                            }
                                                                        }
                                                                    }
                                                                }
                                                            }
                                                        }
                                                    }
                                                }
                                                if (_8234_ladder_break)
                                                {
                                                    break;
                                                }
                                                _8159++;
                                                continue;
                                            }
                                            _8152 = _8157;
                                            _8153 = _8158;
                                            _8154++;
                                            continue;
                                        }
                                        _8149 = _8153;
                                    }
                                }
                                if (_8149)
                                {
                                    vec3 _4849 = textureLod(_9537, _4657, 0.0).xyz * Globals.uBounceScale;
                                    float _8507 = max(_4849.x, max(_4849.y, _4849.z));
                                    vec3 _8500;
                                    if (_8507 > Globals.uMaxRayIntensity)
                                    {
                                        _8500 = _4849 * (Globals.uMaxRayIntensity / _8507);
                                    }
                                    else
                                    {
                                        _8500 = _4849;
                                    }
                                    _9308 = _8500;
                                    _9309 = _4659;
                                    _9312 = true;
                                    _4648 = 2;
                                    _4649 = _4849;
                                    _4652 = _4659;
                                }
                                else
                                {
                                    _9311 += _4659;
                                    _9314 = true;
                                    _4652 = 0.0;
                                }
                            }
                        }
                    }
                    else
                    {
                        if (_4900.z >= 0.0)
                        {
                            vec3 _6562;
                            {
                                if (Globals.uSkyShValid == 0)
                                {
                                    _6562 = Globals.uSkyColour;
                                }
                                else
                                {
                                    vec4 _6573 = vec4(_4900, 1.0);
                                    vec4 _6576 = _6573.xyzz * _6573.yzzx;
                                    float _6602 = _4900.x;
                                    float _6604 = _4900.y;
                                    _6562 = max(vec3(0.0), (vec3(dot(Globals.uSkySh.data[0], _6573), dot(Globals.uSkySh.data[1], _6573), dot(Globals.uSkySh.data[2], _6573)) + vec3(dot(Globals.uSkySh.data[3], _6576), dot(Globals.uSkySh.data[4], _6576), dot(Globals.uSkySh.data[5], _6576))) + (Globals.uSkySh.data[6].xyz * ((_6602 * _6602) - (_6604 * _6604))));
                                }
                            }
                            _4649 = _6562;
                        }
                        else
                        {
                            vec3 _6510;
                            {
                                if (Globals.uSkyShValid == 0)
                                {
                                    _6510 = Globals.uSkyColour;
                                }
                                else
                                {
                                    vec4 _6521 = vec4(vec3(0.0, 0.0, 1.0), 1.0);
                                    vec4 _6524 = _6521.xyzz * _6521.yzzx;
                                    _6510 = max(vec3(0.0), vec3(dot(Globals.uSkySh.data[0], _6521), dot(Globals.uSkySh.data[1], _6521), dot(Globals.uSkySh.data[2], _6521)) + vec3(dot(Globals.uSkySh.data[3], _6524), dot(Globals.uSkySh.data[4], _6524), dot(Globals.uSkySh.data[5], _6524)));
                                }
                            }
                            _4649 = (_6510 + Globals.uGroundSun) * 0.25;
                        }
                        float _6620 = max(_4649.x, max(_4649.y, _4649.z));
                        vec3 _6613;
                        if (_6620 > Globals.uMaxRayIntensity)
                        {
                            _6613 = _4649 * (Globals.uMaxRayIntensity / _6620);
                        }
                        else
                        {
                            _6613 = _4649;
                        }
                        _9310 = _6613;
                        _9313 = true;
                        _4648 = 3;
                        _4652 = 1.0;
                    }
                }
            }
            else
            {
                _4652 = 1.0;
            }
            if (Globals.uDebugSource != 0)
            {
                _4650 = _4648 != Globals.uDebugSource;
            }
            else
            {
                _4650 = false;
            }
            if (_4650)
            {
                _4649 = vec3(0.0);
            }
            float _8543 = max(_4649.x, max(_4649.y, _4649.z));
            vec3 _8536;
            if (_8543 > Globals.uMaxRayIntensity)
            {
                _8536 = _4649 * (Globals.uMaxRayIntensity / _8543);
            }
            else
            {
                _8536 = _4649;
            }
            if (_9312)
            {
                _21 = 1.0;
            }
            else
            {
                _21 = 0.0;
            }
            float _23;
            if (_9313)
            {
                _23 = 2.0;
            }
            else
            {
                _23 = 0.0;
            }
            float _3672 = _21;
            if (_9314)
            {
                _21 = 4.0;
            }
            else
            {
                _21 = 0.0;
            }
            int _3683 = slot + ray;
            sRayRadiance[_3683] = vec4(_8536, _4652);
            sRaySurface[_3683] = vec4(_9308, _9309);
            sRayOpen[_3683] = vec4(_9310, (_3672 + _23) + _21);
            sRayUnplaced[_3683] = _9311;
        }
        barrier();
        if (ray != 0)
        {
            inside = true;
        }
        else
        {
            inside = !inside;
        }
        if (!(inside))
        {
            vec3 traced;
            if (onSurface)
            {
                if (gathers)
                {
                    vec3 _9406 = vec3(0.0);
                    float _9407 = 0.0;
                    vec3 _9408 = vec3(0.0);
                    float _9409 = 0.0;
                    vec3 _9410 = vec3(0.0);
                    float _9411 = 0.0;
                    float _9412 = 0.0;
                    int k = 0;
                    for (;;)
                    {
                        if (!(k < 16))
                        {
                            break;
                        }
                        int _3757 = slot + k;
                        uint flags = uint(sRayOpen[_3757].w);
                        vec3 _9447 = sRayRadiance[_3757].xyz;
                        float _9448 = sRayRadiance[_3757].w;
                        vec3 _9449 = sRaySurface[_3757].xyz;
                        float _9450 = sRaySurface[_3757].w;
                        vec3 _9451 = sRayOpen[_3757].xyz;
                        float _9452 = sRayUnplaced[_3757];
                        bool _9454 = (flags & 2u) != 0u;
                        bool _9455 = (flags & 4u) != 0u;
                        if ((flags & 1u) != 0u)
                        {
                            _9408 += (_9449 * _9450);
                            _9409 += _9450;
                        }
                        if (_9455)
                        {
                            _9412 += _9452;
                        }
                        if (_9454)
                        {
                            _9410 += _9451;
                            _9411 += 1.0;
                        }
                        _9406 += (_9447 * _9448);
                        _9407 += _9448;
                        k++;
                        continue;
                    }
                    bool _8613;
                    if (_9412 > 0.0)
                    {
                        if (Globals.uDebugSource == 0)
                        {
                            _8613 = true;
                        }
                        else
                        {
                            _8613 = Globals.uDebugSource == 2;
                        }
                    }
                    else
                    {
                        _8613 = false;
                    }
                    vec3 _8614;
                    float _8615;
                    if (_8613)
                    {
                        if (_9409 > 0.0)
                        {
                            _8614 = _9408 / vec3(_9409);
                        }
                        else
                        {
                            if (_9411 > 0.0)
                            {
                                _8614 = _9410 / vec3(_9411);
                            }
                            else
                            {
                                vec3 _8674;
                                {
                                    if (Globals.uSkyShValid == 0)
                                    {
                                        _8674 = Globals.uSkyColour;
                                    }
                                    else
                                    {
                                        vec4 _8685 = vec4(vec3(0.0, 0.0, 1.0), 1.0);
                                        vec4 _8688 = _8685.xyzz * _8685.yzzx;
                                        _8674 = max(vec3(0.0), vec3(dot(Globals.uSkySh.data[0], _8685), dot(Globals.uSkySh.data[1], _8685), dot(Globals.uSkySh.data[2], _8685)) + vec3(dot(Globals.uSkySh.data[3], _8688), dot(Globals.uSkySh.data[4], _8688), dot(Globals.uSkySh.data[5], _8688)));
                                    }
                                }
                                _8614 = _8674;
                            }
                            _8614 *= 0.25;
                        }
                        _8615 = _9407 + _9412;
                        _8614 = _9406 + (_8614 * _9412);
                    }
                    else
                    {
                        _8615 = _9407;
                        _8614 = _9406;
                    }
                    traced = max(_8614 * (3.1415927410125732421875 / max(_8615, 4.0)), vec3(0.0));
                }
                else
                {
                    traced = vec3(0.0);
                }
                if (Globals.uDebugSource == 4)
                {
                    vec3 _3979 = -worldNormal;
                    vec2 litFound;
                    bool _8730;
                    {
                        litFound = vec2(0.0);
                        float agreement = 1000000000.0;
                        if (Globals.uLookupBuckets <= 0)
                        {
                            _8730 = false;
                        }
                        else
                        {
                            ivec3 _8771 = ivec3(floor((world - Globals.uLookupMin) / vec3(Globals.uLookupCell)));
                            int _8983 = int((((uint(_8771.x) * 2376512323u) ^ (uint(_8771.y) * 3625334849u)) ^ (uint(_8771.z) * 3407524639u)) & uint(Globals.uLookupBuckets - 1));
                            uvec2 _8776 = uvec2(textureSize(_9531, 0));
                            vec2 _8783 = vec2(float(_8776.x), float(_8776.y));
                            bool _8733 = false;
                            bool _8734 = false;
                            int _8735 = 0;
                            bool _8736;
                            ivec2 _8737;
                            bool _8741;
                            bool _8750;
                            bool _8751;
                            bool _8752;
                            bool _8753;
                            bool _8754;
                            ivec2 _8984;
                            for (;;)
                            {
                                if (_8735 <= uCells._member0[Globals.uLookupBuckets].y)
                                {
                                    _8736 = !_8733;
                                }
                                else
                                {
                                    _8736 = false;
                                }
                                if (!_8736)
                                {
                                    break;
                                }
                                bool _8803 = _8735 == 0;
                                if (_8803)
                                {
                                    _8737 = uCells._member0[_8983];
                                }
                                else
                                {
                                    int _8988 = uCells._member0[Globals.uLookupBuckets].x + ((_8735 - 1) * 6);
                                    float _9004 = intBitsToFloat(uCellPages_1._member0[_8988 + 3]);
                                    vec3 _9006 = world - vec3(intBitsToFloat(uCellPages_1._member0[_8988]), intBitsToFloat(uCellPages_1._member0[_8988 + 1]), intBitsToFloat(uCellPages_1._member0[_8988 + 2]));
                                    if (dot(_9006, _9006) > (_9004 * _9004))
                                    {
                                        _8984 = ivec2(0);
                                    }
                                    else
                                    {
                                        _8984 = ivec2(uCellPages_1._member0[_8988 + 4], uCellPages_1._member0[_8988 + 5]);
                                    }
                                    _8737 = _8984;
                                }
                                bool _8738 = _8733;
                                bool _8739 = _8734;
                                int _8740 = 0;
                                for (;;)
                                {
                                    bool _8815_ladder_break = false;
                                    {
                                        if (_8740 < _8737.y)
                                        {
                                            _8741 = !_8738;
                                        }
                                        else
                                        {
                                            _8741 = false;
                                        }
                                        if (!_8741)
                                        {
                                            _8815_ladder_break = true;
                                        }
                                        else
                                        {
                                            int _8832 = _8737.x + _8740;
                                            int _8835 = uCellPages_1._member0[_8832] * 10;
                                            if (!((uPages._member0[_8835 + 5].w > 0.5) == _8803))
                                            {
                                                int _8844 = _8835 + 9;
                                                vec3 _8848 = world - uPages._member0[_8844].xyz;
                                                if (!(dot(_8848, _8848) > (uPages._member0[_8844].w * uPages._member0[_8844].w)))
                                                {
                                                    int _9022 = uCellPages_1._member0[_8832] * 10;
                                                    vec4 _8748 = uPages._member0[_9022];
                                                    vec4 _8749 = uPages._member0[_9022 + 1];
                                                    int _9028 = _9022 + 2;
                                                    int _9031 = _9022 + 3;
                                                    int _9034 = _9022 + 4;
                                                    int _9040 = _9022 + 6;
                                                    int _9043 = _9022 + 7;
                                                    int _9046 = _9022 + 8;
                                                    vec3 _9053 = world - vec3(uPages._member0[_9040].w, uPages._member0[_9043].w, uPages._member0[_9046].w);
                                                    vec3 _8747 = vec3(dot(uPages._member0[_9040].xyz, worldNormal), dot(uPages._member0[_9043].xyz, worldNormal), dot(uPages._member0[_9046].xyz, worldNormal));
                                                    vec3 _9066 = vec3(dot(uPages._member0[_9040].xyz, _9053), dot(uPages._member0[_9043].xyz, _9053), dot(uPages._member0[_9046].xyz, _9053)) - uPages._member0[_9028].xyz;
                                                    vec2 _9076 = vec2(dot(_9066, uPages._member0[_9031].xyz), dot(_9066, uPages._member0[_9034].xyz)) / max(vec2(uPages._member0[_9028].w, uPages._member0[_9031].w), vec2(9.9999999747524270787835121154785e-07));
                                                    vec2 _8742 = _9076;
                                                    float _8744 = -dot(_9066, uPages._member0[_9022 + 5].xyz);
                                                    float _8745 = uPages._member0[_9034].w;
                                                    vec3 _8746 = uPages._member0[_9022 + 5].xyz;
                                                    if (abs(_9076.x) > 1.0)
                                                    {
                                                        _8750 = true;
                                                    }
                                                    else
                                                    {
                                                        _8750 = abs(_8742.y) > 1.0;
                                                    }
                                                    if (!(_8750))
                                                    {
                                                        if (dot(_3979, _3979) > 0.5)
                                                        {
                                                            _8751 = dot(_8746, _8747) < (-0.0500000007450580596923828125);
                                                        }
                                                        else
                                                        {
                                                            _8751 = false;
                                                        }
                                                        if (!(_8751))
                                                        {
                                                            if (_8744 < (-0.25))
                                                            {
                                                                _8752 = true;
                                                            }
                                                            else
                                                            {
                                                                _8752 = _8744 > (_8745 + 0.25);
                                                            }
                                                            if (!(_8752))
                                                            {
                                                                vec2 _8899 = (_8742 * 0.5) + vec2(0.5);
                                                                vec2 _8906 = (_8749.xy + (_8899 * _8749.zw)) / _8783;
                                                                if (!(textureLod(_9533, _8906, 0.0).w < 0.5))
                                                                {
                                                                    float _8926 = abs((textureLod(_9535, _8906, 0.0).x * _8745) - _8744);
                                                                    if (_8926 < agreement)
                                                                    {
                                                                        agreement = _8926;
                                                                        litFound = (_8748.xy + (_8899 * _8748.zw)) / Globals.uAtlasSize.xy;
                                                                        _8753 = _8926 < 0.0199999995529651641845703125;
                                                                        _8754 = true;
                                                                    }
                                                                    else
                                                                    {
                                                                        _8753 = _8738;
                                                                        _8754 = _8739;
                                                                    }
                                                                    _8738 = _8753;
                                                                    _8739 = _8754;
                                                                }
                                                            }
                                                        }
                                                    }
                                                }
                                            }
                                        }
                                    }
                                    if (_8815_ladder_break)
                                    {
                                        break;
                                    }
                                    _8740++;
                                    continue;
                                }
                                _8733 = _8738;
                                _8734 = _8739;
                                _8735++;
                                continue;
                            }
                            _8730 = _8734;
                        }
                    }
                    if (_8730)
                    {
                        traced = textureLod(_9537, litFound, 0.0).xyz;
                    }
                    else
                    {
                        traced = vec3(0.0);
                    }
                }
            }
            else
            {
                traced = vec3(0.0);
            }
            uvec2 _3997 = uvec2(probe);
            if (onSurface)
            {
                _21 = 1.0;
            }
            else
            {
                _21 = 0.0;
            }
            imageStore(uIndirect, ivec2(_3997), vec4(traced, _21));
            vec4 _43;
            if (onSurface)
            {
                _43 = vec4(world - Globals.uCameraToCache, 1.0);
            }
            else
            {
                _43 = vec4(0.0);
            }
            imageStore(uPosition, ivec2(_3997), _43);
        }
    }
}

