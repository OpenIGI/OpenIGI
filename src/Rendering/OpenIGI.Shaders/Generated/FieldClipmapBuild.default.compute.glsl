#version 430
layout(local_size_x = 4, local_size_y = 4, local_size_z = 4) in;

layout(binding = 0, std140) uniform Globals_std140
{
    int uResolution;
    int uLevel;
    int uCellsPerAxis;
    float uVoxelSize;
    float uCellSize;
    float uRange;
    vec3 uOrigin;
    int uAtlas;
} g;

layout(binding = 1, std430) restrict readonly buffer StructuredBuffer
{
    int _member0[];
} uCellOffsets;

layout(binding = 2, std430) restrict readonly buffer uCellIndices
{
    int _member0[];
} uCellIndices_1;

layout(binding = 0, std430) restrict readonly buffer uInstances
{
    vec4 _member0[];
} uInstances_1;

layout(binding = 0, r8) uniform restrict writeonly image3D uClipmaps;
layout(binding = 0) uniform sampler3D _377;
layout(binding = 1) uniform sampler3D _379;

void main()
{
    {
        ivec3 _29 = ivec3(gl_GlobalInvocationID);
        if (!(any(greaterThanEqual(_29, ivec3(g.uResolution, g.uResolution, g.uResolution)))))
        {
            vec3 world = g.uOrigin + ((vec3(_29) + vec3(0.5)) * g.uVoxelSize);
            ivec3 _81 = clamp(ivec3(floor((world - g.uOrigin) / vec3(g.uCellSize))), ivec3(0), ivec3(g.uCellsPerAxis - 1, g.uCellsPerAxis - 1, g.uCellsPerAxis - 1));
            int cellIndex = (((_81.z * g.uCellsPerAxis) + _81.y) * g.uCellsPerAxis) + _81.x;
            int _99 = cellIndex + 1;
            float nearest = g.uRange;
            int i = uCellOffsets._member0[cellIndex];
            float _283;
            float _287;
            for (;;)
            {
                if (!(i < uCellOffsets._member0[_99]))
                {
                    break;
                }
                {
                    int _291 = uCellIndices_1._member0[i] * 7;
                    int _294 = _291 + 1;
                    int _297 = _291 + 2;
                    int _300 = _291 + 3;
                    if (uInstances_1._member0[_300].w <= 0.0)
                    {
                        _283 = 1000000.0;
                    }
                    else
                    {
                        vec3 _320 = world - vec3(uInstances_1._member0[_291].w, uInstances_1._member0[_294].w, uInstances_1._member0[_297].w);
                        vec3 _331 = (vec3(dot(uInstances_1._member0[_291].xyz, _320), dot(uInstances_1._member0[_294].xyz, _320), dot(uInstances_1._member0[_297].xyz, _320)) - uInstances_1._member0[_291 + 5].xyz) / uInstances_1._member0[_291 + 6].xyz;
                        vec3 _334 = clamp(_331, vec3(0.5), uInstances_1._member0[_291 + 4].xyz - vec3(0.5));
                        uvec3 _336 = uvec3(textureSize(_377, 0));
                        vec4 _371 = texture(_379, (uInstances_1._member0[_300].xyz + _334) / vec3(float(_336.x), float(_336.y), float(_336.z)));
                        float _358 = length((_334 - _331) * uInstances_1._member0[_291 + 6].xyz);
                        if (_358 > 0.0)
                        {
                            _287 = _358;
                        }
                        else
                        {
                            _287 = ((_371.x * 2.0) - 1.0) * uInstances_1._member0[_300].w;
                        }
                        _283 = _287;
                    }
                }
                nearest = min(nearest, _283);
                i++;
                continue;
            }
            imageStore(uClipmaps, ivec3(uvec3(ivec3(_29.xy, (g.uLevel * g.uResolution) + _29.z))), vec4((clamp(nearest / g.uRange, -1.0, 1.0) * 0.5) + 0.5, 0.0, 0.0, 0.0));
        }
    }
}

