// Remaster lens.
//
// Input: original | remastered side by side (hstack). A round lens drifts over the
// original picture and shows the remaster through it, its rim refracting the colour
// channels apart like real glass. It opens from nothing after DELAY and closes again
// at FINISH (when set). Outside the lens the original is dimmed by DIM so the eye
// follows the glass; set DIM to 1.0 to leave it untouched.

//!HOOK MAIN
//!BIND HOOKED
//!WIDTH HOOKED.w 2 /
//!DESC OpenIGI compare lens

#define FPS 60.0
#define DELAY 0.6
#define RADIUS 0.26        // of the frame height
#define SPEED 1.0          // drift speed multiplier
#define FINISH -1.0
#define DIM 0.82
#define RIM_COLOR vec3(0.55, 0.85, 1.0)

vec4 take(vec2 uv, float half_offset)
{
    float inset = 0.5 / HOOKED_size.x;
    return HOOKED_tex(vec2(clamp(uv.x * 0.5, inset, 0.5 - inset) + half_offset, clamp(uv.y, 0.0, 1.0)));
}

vec4 hook()
{
    vec2 uv = HOOKED_pos;
    float t = float(frame) / FPS;
    float aspect = (HOOKED_size.x * 0.5) / HOOKED_size.y;

    float open = smoothstep(DELAY, DELAY + 0.9, t);
    if (FINISH >= 0.0)
    {
        open *= 1.0 - smoothstep(FINISH, FINISH + 0.7, t);
    }
    float s = max(t - DELAY, 0.0) * SPEED;
    vec2 centre = vec2(0.5 + 0.30 * sin(s * 0.42), 0.5 + 0.17 * sin(s * 0.67 + 1.3));
    float radius = RADIUS * open;

    vec2 d = vec2((uv.x - centre.x) * aspect, uv.y - centre.y);
    float r = length(d);
    float px = 1.0 / HOOKED_size.y;

    vec3 original = take(uv, 0.0).rgb;
    vec3 c = original * mix(1.0, DIM, open);

    if (open > 0.001 && r < radius + 0.02)
    {
        // Barrel-magnify a little toward the centre and split the channels near the rim.
        float n = clamp(r / max(radius, 1e-4), 0.0, 1.0);
        vec2 dir = r > 0.0 ? d / r : vec2(0.0);
        float bulge = 0.06 * (1.0 - n * n);
        float spread = 0.006 * pow(n, 6.0);
        vec2 base = centre + vec2(d.x / aspect, d.y) * (1.0 - bulge);
        vec2 shift = vec2(dir.x / aspect, dir.y) * spread;
        vec3 lens = vec3(
            take(base - shift, 0.5).r,
            take(base, 0.5).g,
            take(base + shift, 0.5).b);

        float inside = smoothstep(radius + px, radius - px, r);
        c = mix(c, lens, inside);

        // Rim light, a soft specular arc at the top left, and a contact shadow outside.
        float rim = exp(-abs(r - radius) / (px * 1.6));
        float arc = smoothstep(0.55, 1.0, dot(-dir, normalize(vec2(0.6, 0.8)))) * smoothstep(0.7, 0.98, n) * inside;
        float shade = smoothstep(radius + 0.02, radius, r) * (1.0 - inside);
        c *= 1.0 - 0.35 * shade;
        c += open * (rim * RIM_COLOR * 0.9 + arc * vec3(0.25));
    }

    return vec4(c, 1.0);
}
