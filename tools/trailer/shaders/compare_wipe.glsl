// Retro <-> Remastered wipe.
//
// Input: two takes of the same lockstep flight side by side (hstack), the original
// picture on the left half and the remastered one on the right. Output: one frame,
// half the input's width, split by a slanted glowing divider. The divider enters
// from the right edge so the shot opens on the original, sweeps to REST, drifts
// there, and (when FINISH is set) runs off the left edge to end on the remaster.
//
// Neither picture is graded or tinted; only the pixels on the divider's glow are
// touched, so the comparison stays honest. trailer.ps1 rewrites the #defines.

//!HOOK MAIN
//!BIND HOOKED
//!WIDTH HOOKED.w 2 /
//!DESC OpenIGI compare wipe

#define FPS 60.0
#define DELAY 0.8          // seconds on the original before the sweep starts
#define SWEEP 1.8          // seconds the sweep takes
#define REST 0.5           // where the divider settles, 0..1 of the width
#define SWAY 0.16          // how far it drifts either side of REST
#define SWAY_PERIOD 9.0    // seconds per drift cycle
#define FINISH -1.0        // second the divider leaves to the left; negative holds the split
#define SLANT 0.08         // horizontal lean of the divider across the frame height
#define GLOW_COLOR vec3(1.0, 0.64, 0.22)

float easeOutBack(float x)
{
    const float c1 = 1.25;
    const float c3 = c1 + 1.0;
    x = x - 1.0;
    return 1.0 + c3 * x * x * x + c1 * x * x;
}

float easeInOut(float x)
{
    return x * x * (3.0 - 2.0 * x);
}

vec4 take(float x, float y, float half_offset)
{
    // Clamp half a texel inside the half so bilinear sampling cannot bleed across the seam.
    float inset = 0.5 / HOOKED_size.x;
    return HOOKED_tex(vec2(clamp(x * 0.5, inset, 0.5 - inset) + half_offset, y));
}

vec4 hook()
{
    vec2 uv = HOOKED_pos;
    float t = float(frame) / FPS;
    vec2 px = vec2(HOOKED_size.x * 0.5, HOOKED_size.y);

    // Divider position along the frame's horizontal centre line.
    float start = 1.0 + SLANT + 0.05;
    float k = clamp((t - DELAY) / SWEEP, 0.0, 1.0);
    float p = mix(start, REST, easeOutBack(k));
    float settled = clamp((t - DELAY - SWEEP) / 1.5, 0.0, 1.0);
    p += SWAY * settled * sin(6.28318 * max(t - DELAY - SWEEP, 0.0) / SWAY_PERIOD);
    if (FINISH >= 0.0)
    {
        float out_k = clamp((t - FINISH) / 1.2, 0.0, 1.0);
        p = mix(p, -SLANT - 0.05, easeInOut(out_k) * easeInOut(out_k));
    }

    float edge = p + SLANT * (0.5 - uv.y);
    vec4 original = take(uv.x, uv.y, 0.0);
    vec4 remaster = take(uv.x, uv.y, 0.5);
    float d = (uv.x - edge) * px.x;                 // signed distance in pixels
    float aa = clamp(d + 0.5, 0.0, 1.0);
    vec3 c = mix(original.rgb, remaster.rgb, aa);

    // Light on the seam: a hard white core, a warm halo, and a pulse that runs down it.
    float ad = abs(d);
    float core = exp(-ad * ad / 2.0);
    float halo = exp(-ad / (px.y * 0.012));
    float run = 0.5 + 0.5 * sin(uv.y * 24.0 - t * 7.0);
    float visible = step(-0.02, edge) * step(edge, 1.02);
    c += visible * (core * vec3(1.0) * 0.85 + halo * GLOW_COLOR * (0.35 + 0.25 * run));

    // A diamond handle at mid height with a chevron each side.
    vec2 hp = vec2((uv.x - edge) * px.x, (uv.y - 0.5) * px.y) / px.y;
    float r = abs(hp.x) + abs(hp.y);
    float ring = smoothstep(0.024, 0.021, r) - smoothstep(0.019, 0.016, r);
    float fill = smoothstep(0.019, 0.016, r);
    float chev = 0.0;
    for (int side = -1; side <= 1; side += 2)
    {
        // A '>' (or '<' mirrored) whose tip sits just inside the diamond.
        float qx = hp.x * float(side);
        float ch = abs((0.012 - qx) - abs(hp.y));
        chev += smoothstep(0.0016, 0.0006, ch) * step(0.006, qx) * step(qx, 0.012);
    }
    c = mix(c, c * 0.35, fill * visible);
    c += visible * (ring * GLOW_COLOR * 1.4 + chev * vec3(1.0));

    return vec4(c, 1.0);
}
