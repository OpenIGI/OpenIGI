// Original | remastered split that moves in beats.
//
// Input: two takes of the same lockstep flight side by side (hstack), the original on the
// left half. Output: one frame split by a hard vertical line, the original to its left.
// The line travels between keyframes (KT seconds, KX positions across the frame) with a
// smootherstep, so it snaps decisively and then holds still for a real comparison. While it
// moves it glows and leaves a short streak of light behind it; at rest it is a crisp white
// line with a notch at the top and bottom. trailer.ps1 writes KEY_COUNT/KT/KX from the clip's
// length and draws labels that ride either side of the line from the same keyframes.
//
// Neither picture is graded or tinted; only the line and its streak touch pixels.

//!HOOK MAIN
//!BIND HOOKED
//!WIDTH HOOKED.w 2 /
//!DESC OpenIGI compare split

#define FPS 60.0
#define KEY_COUNT 7
#define KT float[KEY_COUNT](0.0, 0.4, 1.0, 3.2, 3.8, 5.6, 6.4)
#define KX float[KEY_COUNT](1.0, 1.0, 0.5, 0.5, 0.28, 0.28, 0.0)
#define ACCENT vec3(1.0, 0.64, 0.22)

float ease(float u)
{
    u = clamp(u, 0.0, 1.0);
    return u * u * u * (u * (u * 6.0 - 15.0) + 10.0);
}

// The line's position at time t, and its signed speed in frame widths per second.
float divider(float t, out float speed)
{
    float kt[KEY_COUNT] = KT;
    float kx[KEY_COUNT] = KX;
    float x = kx[0];
    speed = 0.0;
    for (int i = 0; i < KEY_COUNT - 1; i++)
    {
        float span = max(kt[i + 1] - kt[i], 1e-4);
        float u = (t - kt[i]) / span;
        x += (kx[i + 1] - kx[i]) * ease(u);
        if (u > 0.0 && u < 1.0)
        {
            speed += (kx[i + 1] - kx[i]) * 30.0 * u * u * (u - 1.0) * (u - 1.0) / span;
        }
    }
    return x;
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
    vec2 px = vec2(HOOKED_size.x * 0.5, HOOKED_size.y);

    float speed;
    float edge = divider(t, speed);
    float d = (uv.x - edge) * px.x;                   // signed pixels from the line
    vec3 c = mix(take(uv, 0.0).rgb, take(uv, 0.5).rgb, clamp(d + 0.5, 0.0, 1.0));

    float moving = clamp(abs(speed) * 1.6, 0.0, 1.0);
    float visible = step(0.0, edge) * step(edge, 1.0);
    float scale = px.y / 1080.0;                       // sizes are authored at 1080p

    // A streak of light trailing the line while it travels.
    float behind = -d * sign(speed);
    // Clamped before the exponent: ahead of the line it would overflow to infinity, and nought times infinity is a NaN,
    // which the encoder turns into a solid green band.
    float trail = moving * step(0.0, behind) * exp(-max(behind, 0.0) / (90.0 * scale * moving + 1e-3));
    c += visible * trail * ACCENT * 0.18;

    // The line: a dark keyline for contrast, a white core, warming and widening while it moves.
    float width = (1.5 + 2.5 * moving) * scale;
    float ad = abs(d);
    float core = 1.0 - smoothstep(width - 0.75, width + 0.75, ad);
    float keyline = (1.0 - smoothstep(width + 1.0 * scale, width + 3.0 * scale, ad)) * (1.0 - core);
    vec3 lineColour = mix(vec3(1.0), ACCENT, moving);
    c = mix(c, c * 0.45, visible * keyline * 0.6);
    c = mix(c, lineColour, visible * core);
    c += visible * moving * ACCENT * exp(-ad / (14.0 * scale)) * 0.35;

    // Notches at the top and bottom: small triangles pointing at the line.
    float notch = 14.0 * scale;
    for (int s = 0; s < 2; s++)
    {
        float y = (s == 0 ? uv.y : 1.0 - uv.y) * px.y;
        float tri = step(y, notch - ad);
        c = mix(c, lineColour, visible * tri);
    }

    return vec4(c, 1.0);
}
