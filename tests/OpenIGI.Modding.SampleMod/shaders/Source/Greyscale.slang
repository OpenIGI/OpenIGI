// The sample mod's pass: the scene drained of its colour, by as much as the mod's setting says.

#include "Mod/PostPass.slang"

[shader("compute")]
[numthreads(8, 8, 1)]
void main(uint3 id : SV_DispatchThreadID)
{
    if (any(id.xy >= uint2(p.size.xy)))
    {
        return;
    }

    float4 colour = uSource[id.xy];
    float grey = dot(colour.rgb, float3(0.2126, 0.7152, 0.0722));
    uTarget.Store(id.xy, float4(lerp(colour.rgb, grey.xxx, saturate(p.values[0].x)), colour.a));
}
