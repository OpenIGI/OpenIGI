// 2000 -> today: the picture resolves.
//
// Input: original | remastered side by side (hstack). After DELAY a diagonal front
// crosses the frame. Ahead of it is the original picture; behind it the remaster.
// Inside the front's band the remaster is shown as a mosaic whose blocks shrink from
// BLOCK pixels to one as the front passes, with a scanning light on the leading edge,
// so the new picture appears to be computed into place.

//!HOOK MAIN
//!BIND HOOKED
//!WIDTH HOOKED.w 2 /
//!DESC OpenIGI era resolve

#define FPS 60.0
#define DELAY 1.0
#define DURATION 2.6       // seconds for the front to cross
#define BAND 0.35          // width of the mosaic band, in frame widths
#define BLOCK 48.0         // largest mosaic block, in output pixels
#define EDGE_COLOR vec3(0.35, 0.9, 1.0)

float hash(vec2 p)
{
    return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453);
}

vec4 take(vec2 uv, float half_offset)
{
    float inset = 0.5 / HOOKED_size.x;
    return HOOKED_tex(vec2(clamp(uv.x * 0.5, inset, 0.5 - inset) + half_offset, uv.y));
}

vec4 hook()
{
    vec2 uv = HOOKED_pos;
    float t = float(frame) / FPS;
    vec2 size = vec2(HOOKED_size.x * 0.5, HOOKED_size.y);

    // Coordinate along the sweep: left-bottom to right-top, 0..1 across the frame.
    float along = (uv.x * 0.8 + (1.0 - uv.y) * 0.2);
    float k = clamp((t - DELAY) / DURATION, 0.0, 1.0);
    k = k * k * (3.0 - 2.0 * k);
    float front = mix(-0.02, 1.0 + BAND + 0.02, k);

    // 1 well behind the front (resolved), 0 at the leading edge and ahead of it.
    float behind = clamp((front - along) / BAND, 0.0, 1.0);
    float ahead = step(front, along);

    vec3 original = take(uv, 0.0).rgb;
    vec3 c;
    if (ahead > 0.5)
    {
        c = original;
    }
    else
    {
        float block = max(1.0, floor(mix(BLOCK, 1.0, pow(behind, 0.6))));
        vec2 cell = floor(uv * size / block);
        vec2 q = (cell + 0.5) * block / size;
        vec3 mosaic = take(q, 0.5).rgb;

        // Cells flip from original to remaster in a scattered order near the edge.
        float flip = step(hash(cell + block), behind * 3.0);
        c = mix(original, mosaic, flip);

        // Faint grid lines on the coarse cells.
        vec2 g = fract(uv * size / block);
        float grid = (block > 2.0) ? (1.0 - smoothstep(0.0, 1.5 / block, min(g.x, g.y))) * (1.0 - behind) : 0.0;
        c += grid * EDGE_COLOR * 0.25 * flip;
    }

    // The leading edge glows.
    float dist = (along - front) * size.x;
    c += EDGE_COLOR * exp(-abs(dist) / 6.0) * 0.9 * step(0.001, k) * step(k, 0.999);
    c += vec3(1.0) * exp(-dist * dist / 3.0) * 0.6 * step(0.001, k) * step(k, 0.999);

    return vec4(c, 1.0);
}
