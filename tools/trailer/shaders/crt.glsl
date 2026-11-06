// Year-2000 CRT monitor.
//
// For the "remember this?" opening: the original picture on a curved tube with
// scanlines, an aperture-grille mask, bloom, a rolling hum bar and a little flicker.
// With POWER_ON set the tube switches on at the start (a bright line that opens
// vertically); with POWER_OFF >= 0 it collapses to a dot at that second.

//!HOOK MAIN
//!BIND HOOKED
//!DESC OpenIGI CRT

#define FPS 60.0
#define CURVE 0.10         // barrel distortion
#define SCANLINES 480.0    // visible lines, the original's height
#define MASK 0.22          // aperture-grille strength
#define BLOOM 0.35
#define POWER_ON 1
#define POWER_OFF -1.0
#define BEZEL vec3(0.012, 0.012, 0.014)

vec2 curve(vec2 uv)
{
    uv = uv * 2.0 - 1.0;
    vec2 offset = abs(uv.yx) * CURVE;
    uv = uv + uv * offset * offset * 3.0;
    return uv * 0.5 + 0.5;
}

vec3 sampleTube(vec2 uv)
{
    vec3 c = HOOKED_tex(uv).rgb;

    // A cheap glow: the bright parts of a small neighbourhood bleed into this pixel.
    vec2 px = 1.0 / HOOKED_size;
    vec3 glow = vec3(0.0);
    for (int i = -2; i <= 2; i++)
    {
        for (int j = -2; j <= 2; j++)
        {
            vec3 s = HOOKED_tex(uv + vec2(float(i), float(j)) * px * 2.5).rgb;
            glow += max(s - 0.45, 0.0);
        }
    }
    return c + glow / 25.0 * BLOOM * 4.0;
}

vec4 hook()
{
    float t = float(frame) / FPS;
    vec2 screen = HOOKED_pos;
    vec2 uv = curve(screen);

    // Rounded tube corners; outside is the bezel.
    vec2 corner = abs(uv - 0.5) * 2.0;
    float tube = smoothstep(1.0, 0.985, max(corner.x, corner.y)) * smoothstep(1.0, 0.92, length(max(corner - 0.9, 0.0)) * 10.0);
    if (tube <= 0.0)
    {
        return vec4(BEZEL, 1.0);
    }

    // Slight horizontal channel misconvergence.
    vec2 px = 1.0 / HOOKED_size;
    vec3 c = vec3(
        sampleTube(uv + vec2(px.x * 1.2, 0.0)).r,
        sampleTube(uv).g,
        sampleTube(uv - vec2(px.x * 1.2, 0.0)).b);

    // Scanlines with the beam fattening in bright areas.
    float line = sin(uv.y * SCANLINES * 3.14159);
    float luma = dot(c, vec3(0.299, 0.587, 0.114));
    float beam = mix(0.55, 0.9, luma);
    c *= mix(1.0, beam + (1.0 - beam) * line * line, 0.85);

    // Aperture grille: RGB stripes across the output pixels.
    float stripe = mod(floor(screen.x * HOOKED_size.x), 3.0);
    vec3 mask = vec3(stripe == 0.0 ? 1.0 : 1.0 - MASK, stripe == 1.0 ? 1.0 : 1.0 - MASK, stripe == 2.0 ? 1.0 : 1.0 - MASK);
    c *= mask * (1.0 + MASK * 0.7);

    // Hum bar, flicker and vignette.
    c *= 1.0 - 0.06 * smoothstep(0.0, 0.2, sin((uv.y - t * 0.12) * 6.28318) * 0.5 + 0.5) * smoothstep(0.6, 1.0, sin((uv.y - t * 0.12) * 6.28318));
    c *= 0.97 + 0.03 * sin(t * 113.0);
    vec2 v = uv - 0.5;
    c *= 1.0 - dot(v, v) * 1.4;

    // Switching on and off.
    if (POWER_ON != 0)
    {
        float on = clamp(t / 0.55, 0.0, 1.0);
        float h = mix(0.004, 1.0, pow(on, 2.2));
        float lineMask = smoothstep(h, h * 0.6, abs(uv.y - 0.5) * 2.0);
        c = c * lineMask + vec3(1.0) * lineMask * (1.0 - on) * 2.0;
        c *= smoothstep(0.0, 0.08, t);
    }
    if (POWER_OFF >= 0.0)
    {
        float off = clamp((t - POWER_OFF) / 0.45, 0.0, 1.0);
        float h = mix(1.0, 0.003, smoothstep(0.0, 0.6, off));
        float w = mix(1.0, 0.0, smoothstep(0.55, 1.0, off));
        vec2 a = abs(uv - 0.5) * 2.0;
        float keep = smoothstep(h, h * 0.6, a.y) * smoothstep(w + 0.001, w * 0.8, a.x);
        c = c * keep + vec3(1.0) * keep * off * 1.5;
    }

    return vec4(mix(BEZEL, c, tube), 1.0);
}
