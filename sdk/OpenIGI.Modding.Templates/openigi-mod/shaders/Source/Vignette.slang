// The mod's pass: the picture darkened toward its edges, by as much as the mod's setting says.
//
// It runs over the finished picture (Stage="display" in the project), so its colour is what the player sees: display-encoded,
// nought to one. Over the lit scene instead (Stage="scene"), it would read linear radiance before the tone map. The contract
// it is written against, and what each name holds, is the game's Mod/PostPass.slang.

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

    // From the centre out, nought to one at the corners.
    float2 uv = (float2(id.xy) + 0.5) * p.size.zw;
    float edge = saturate(length(uv - 0.5) * 1.41421356);
    float darkening = saturate(p.values[0].x) * smoothstep(0.4, 1.0, edge);
    uTarget.Store(id.xy, float4(colour.rgb * (1.0 - darkening), colour.a));
}
