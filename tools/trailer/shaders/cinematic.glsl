// Cinematic finish.
//
// The last pass over every remastered shot: letterbox bars that slide in over
// BARS_IN seconds (or sit still), a gentle teal/amber split tone, halation around
// highlights, radial chromatic aberration, a vignette and luminance-weighted film
// grain that changes every frame. Each term has its own strength; 0 turns it off.
// Do not run it over the original picture in a side-by-side, only on whole shots.

//!HOOK MAIN
//!BIND HOOKED
//!DESC OpenIGI cinematic

#define FPS 60.0
#define BARS 2.39          // target aspect; 0 for no bars
#define BARS_IN 0.0        // seconds the bars take to slide in; 0 starts with them in place
#define GRADE 0.35
#define HALATION 0.25
#define ABERRATION 0.0018
#define VIGNETTE 0.35
#define GRAIN 0.045
#define FADE_IN 0.0        // seconds of fade from black at the start
#define FADE_OUT -1.0      // second a 0.6 s fade to black starts; negative for none

float hash(vec3 p)
{
    p = fract(p * 0.1031);
    p += dot(p, p.yzx + 33.33);
    return fract((p.x + p.y) * p.z);
}

vec4 hook()
{
    vec2 uv = HOOKED_pos;
    float t = float(frame) / FPS;
    vec2 px = 1.0 / HOOKED_size;
    vec2 centred = uv - 0.5;

    // Radial chromatic aberration.
    vec2 ca = centred * ABERRATION * 4.0 * length(centred);
    vec3 c = vec3(HOOKED_tex(uv - ca).r, HOOKED_tex(uv).g, HOOKED_tex(uv + ca).b);

    // Halation: a warm, wide bleed of what is bright nearby.
    if (HALATION > 0.0)
    {
        vec3 glow = vec3(0.0);
        for (int i = 0; i < 12; i++)
        {
            float a = float(i) * 0.5236;
            vec2 o = vec2(cos(a), sin(a)) * px * (i % 2 == 0 ? 9.0 : 18.0);
            glow += max(HOOKED_tex(uv + o).rgb - 0.7, 0.0);
        }
        c += glow / 12.0 * vec3(1.0, 0.55, 0.35) * HALATION * 3.0;
    }

    // Split tone: shadows toward teal, highlights toward amber, saturation held.
    float luma = dot(c, vec3(0.2126, 0.7152, 0.0722));
    vec3 shadows = vec3(-0.02, 0.01, 0.035);
    vec3 highlights = vec3(0.04, 0.01, -0.035);
    c += GRADE * mix(shadows, highlights, smoothstep(0.15, 0.75, luma));
    c = mix(vec3(luma), c, 1.0 + GRADE * 0.15);
    c = clamp(c, 0.0, 1.0);

    // Vignette and grain.
    c *= 1.0 - VIGNETTE * smoothstep(0.25, 0.85, length(centred * vec2(1.0, 0.8)) * 1.2);
    float g = hash(vec3(floor(uv * HOOKED_size), float(frame))) - 0.5;
    c += g * GRAIN * (1.0 - 0.6 * luma);

    // Letterbox bars.
    if (BARS > 0.0)
    {
        float frameAspect = HOOKED_size.x / HOOKED_size.y;
        float bar = max(0.0, (1.0 - frameAspect / BARS) * 0.5);
        float slide = BARS_IN > 0.0 ? smoothstep(0.0, 1.0, clamp(t / BARS_IN, 0.0, 1.0)) : 1.0;
        float h = bar * slide;
        float inside = step(h, uv.y) * step(uv.y, 1.0 - h);
        c *= inside;
    }

    float fade = FADE_IN > 0.0 ? clamp(t / FADE_IN, 0.0, 1.0) : 1.0;
    if (FADE_OUT >= 0.0)
    {
        fade *= 1.0 - clamp((t - FADE_OUT) / 0.6, 0.0, 1.0);
    }

    return vec4(c * fade, 1.0);
}
