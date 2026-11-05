// Digital glitch burst.
//
// For hard cuts and the live D3D12 / Vulkan / WebGPU switch: around second AT the
// picture tears into displaced slices, its channels split, blocks smear and invert,
// and it snaps back. Outside AT +- LENGTH/2 the frame passes through untouched.
// Several bursts in one clip are several passes with different AT values.

//!HOOK MAIN
//!BIND HOOKED
//!DESC OpenIGI glitch

#define FPS 60.0
#define AT 0.5             // second the burst peaks
#define LENGTH 0.5         // seconds the burst lasts
#define STRENGTH 1.0

float hash(float n)
{
    return fract(sin(n * 12.9898) * 43758.5453);
}

float hash2(vec2 p)
{
    return fract(sin(dot(p, vec2(41.3, 289.1))) * 14375.5964);
}

vec4 hook()
{
    vec2 uv = HOOKED_pos;
    float t = float(frame) / FPS;
    float x = (t - AT) / (LENGTH * 0.5);
    float env = STRENGTH * max(0.0, 1.0 - x * x);
    if (env <= 0.0)
    {
        return HOOKED_tex(uv);
    }

    // The pattern changes every other frame, like a decoder losing sync.
    float seed = floor(float(frame) * 0.5);

    // Horizontal slices pushed sideways.
    float rows = mix(8.0, 40.0, hash(seed + 3.0));
    float row = floor(uv.y * rows);
    float tear = (hash(row + seed * 7.1) - 0.5) * step(1.0 - env * 0.8, hash(row * 1.7 + seed));
    uv.x += tear * 0.18 * env;

    // Coarse blocks that sample from somewhere else.
    vec2 blockGrid = vec2(16.0, 9.0) * (1.0 + floor(hash(seed) * 3.0));
    vec2 cell = floor(uv * blockGrid);
    float hit = step(1.0 - env * 0.25, hash2(cell + seed));
    uv += hit * (vec2(hash2(cell * 3.1 + seed), hash2(cell * 5.7 - seed)) - 0.5) * 0.12;
    uv = fract(uv);

    // Channel split, wider at the burst's peak.
    vec2 split = vec2(0.012 * env * (hash(seed + 11.0) * 2.0 - 1.0), 0.003 * env);
    vec3 c = vec3(
        HOOKED_tex(fract(uv + split)).r,
        HOOKED_tex(uv).g,
        HOOKED_tex(fract(uv - split)).b);

    // Some blocks invert or posterise; a few scanlines drop to white noise.
    float inv = hit * step(0.6, hash2(cell - seed));
    c = mix(c, 1.0 - c, inv);
    c = mix(c, floor(c * 4.0) / 4.0, hit * (1.0 - inv));
    float noiseLine = step(1.0 - env * 0.06, hash(floor(uv.y * 300.0) + seed * 13.0));
    c = mix(c, vec3(hash2(uv * 997.0 + seed)), noiseLine);

    return vec4(c, 1.0);
}
