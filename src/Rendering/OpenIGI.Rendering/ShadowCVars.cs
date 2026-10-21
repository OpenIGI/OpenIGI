using OpenIGI.Core.Configuration;
using OpenIGI.Core.Configuration.Presets;

namespace OpenIGI.Rendering;

/// <summary>The renderer's console variables for shadowing: the sun's shadow, contact shadows, ambient occlusion and the screen-space bounce.</summary>
/// <remarks>One of the renderer's variable containers; <see cref="FrameCVars"/> says how they are reached.</remarks>
[CVarContainer]
public static class ShadowCVars
{
    /// <summary>How wide the sun's disc is, in degrees, which is what gives a shadow its penumbra.</summary>
    /// <remarks>
    /// <para>
    /// The sun is not a point. It subtends a little over half a degree from here, and that is the entire
    /// reason a shadow is sharp where it meets its caster and soft a few metres away: near the contact a
    /// surface sees almost all of the disc or almost none of it, and further away the two answers overlap
    /// across several pixels. A point sun has no penumbra at any sample count, so this is not a quality
    /// dial — it is the physical fact the softness is derived from.
    /// </para>
    /// <para>
    /// Every preset carries the same figure for that reason, and it is the real one: 0.5357 degrees, the
    /// sun's mean angular diameter seen from the earth. It only reaches the picture
    /// where the sun's shadow is traced rather than sampled from the cascades, which today means ultra
    /// with <c>r_rt</c> on; the cascaded path has no disc to sample.
    /// </para>
    /// </remarks>
    public static readonly NumericCVar<float> SunAngle = Presets.Define(
        CVar.Float(
            "r_sun_angle",
            0.5357f,
            0f,
            10f,
            CVarOptions.Archive,
            "The sun's angular diameter in degrees; 0 makes it a point and its shadows hard-edged."),
        vanilla: "0.5357",
        enhanced: "0.5357",
        high: "0.5357",
        ultra: "0.5357");

    /// <summary>How many frames the traced sun shadow is averaged over before it is turned into light.</summary>
    /// <remarks>
    /// <para>
    /// The trace fires one ray per pixel per frame, so its answer is a coin toss weighted by how much of
    /// the sun that pixel can see. The spatial reconstruction turns the worst of that into a penumbra, but
    /// a mask built from eight samples of a binary signal still visibly differs frame to frame — and this
    /// is what was seen as distant shadows redrawing themselves.
    /// </para>
    /// <para>
    /// Leaving it to the temporal filter downstream does not work, and the reason is circular: shadow
    /// noise arrives there as a colour that changed, a changed colour reads as a history that no longer
    /// describes the surface, and rejecting the history is precisely refusing to do the averaging that
    /// would have removed the noise. So the mask is averaged here, on its own, where nothing has to be
    /// protected from being blurred — a shadow on static geometry carries no sub-pixel detail.
    /// </para>
    /// <para>
    /// The number is a cap on a running mean rather than a fixed window, so a surface just come into view
    /// is not held back by frames it never had. One switches the averaging off.
    /// </para>
    /// </remarks>
    public static readonly NumericCVar<float> ShadowAccumulate = Presets.Define(
        CVar.Float(
            "r_shadow_accumulate",
            16f,
            1f,
            64f,
            CVarOptions.Archive,
            "How many frames the traced sun shadow is averaged over; 1 disables the averaging."),
        vanilla: "1",
        enhanced: "8",
        high: "16",
        ultra: "16");

    /// <summary>The side length in texels of each shadow cascade, or 0 for no shadows.</summary>
    /// <remarks>
    /// The cost is per cascade and quadratic: four cascades at 4096 is 268 MB of depth, which is more
    /// video memory than the entire rest of this game uses. Ultra buys its quality with a fourth
    /// cascade rather than with a larger one — more cascades put the texels where the player is
    /// looking, while a larger map spends them evenly over ground they cannot make out.
    /// </remarks>
    public static readonly NumericCVar<int> Shadows = Presets.Define(
        CVar.Int(
            "r_shadows",
            2048,
            0,
            4096,
            CVarOptions.Archive,
            "Sun shadow map resolution per cascade; 0 turns shadows off. Cost is per cascade and quadratic."),
        vanilla: "0",
        enhanced: "1024",
        high: "2048",

        // 4096 at ultra, and the reason is NOT the one this comment used to give. It said the window
        // mullions read as a soft blur at 2048 and came in crisp doubled, which was measured on the
        // cascaded path and is still true there: with the traced sun off, 1024 against 4096 moves 3.7% of
        // the frame and 4.5% of the office's sunlit floor patch.
        //
        // With the traced sun ON -- which is what ultra runs -- the same comparison moves 0.01% of either
        // view. The traced shadow supersedes the cascade everywhere it reaches, so its resolution buys
        // nothing in the picture ultra actually renders, and a reader looking only at the frame would
        // conclude the setting was inert and the memory wasted.
        //
        // It is neither, because r_rt is bound to a key and meant to be toggled in place. The cascade has
        // to be sitting at full resolution the moment the trace is switched off, and a map resized on that
        // keystroke would cost a stall and a frame of wrong shadows exactly when the eye is on them.
        ultra: "4096");

    /// <summary>How many cascades the shadow distance is split across.</summary>
    public static readonly NumericCVar<int> ShadowCascades = Presets.Define(
        CVar.Int(
            "r_shadow_cascades",
            3,
            1,
            4,
            CVarOptions.Archive,
            "How many shadow cascades the view distance is split across."),
        vanilla: "1",
        enhanced: "2",
        high: "3",
        ultra: "4");

    /// <summary>How far from the camera, in metres, shadows are drawn.</summary>
    /// <remarks>
    /// Further is not better past a point: the same texels get spread over more world, so distant
    /// shadows arrive at the cost of the ones under the player's feet.
    /// </remarks>
    public static readonly NumericCVar<float> ShadowDistance = Presets.Define(
        CVar.Float(
            "r_shadow_distance",
            220f,
            20f,
            1000f,
            CVarOptions.Archive,
            "How far from the camera sun shadows are drawn, in metres."),
        vanilla: "220",
        enhanced: "150",
        high: "220",

        // Pulled in from 320 to concentrate the cascades' resolution on the near field, where the window's
        // shadow grid on the floor is -- the sharpness there is worth more than shadows out past the haze,
        // which the fog swallows anyway. The near building and fence shadows outdoors still read.
        ultra: "150");

    /// <summary>How far a screen-space contact ray reaches, in metres. Zero disables it.</summary>
    /// <remarks>
    /// A shadow map has one texel per several centimetres at best, so it cannot resolve a weapon
    /// against the body carrying it or a crate against the floor it stands on. Contact is where its
    /// resolution runs out and where the eye looks first.
    /// </remarks>
    public static readonly NumericCVar<float> ContactShadows = Presets.Define(
        CVar.Float(
            "r_contact_shadows",
            0.35f,
            0f,
            2f,
            CVarOptions.Archive,
            "Reach in metres of the screen-space contact shadow ray; 0 turns it off."),
        vanilla: "0",
        enhanced: "0",
        high: "0.35",
        ultra: "0.5");

    /// <summary>The shadow filter's radius in texels.</summary>
    public static readonly NumericCVar<float> ShadowSoftness = Presets.Define(
        CVar.Float(
            "r_shadow_softness",
            1f,
            1f,
            4f,
            CVarOptions.Archive,
            "Shadow filter radius in texels. Above 1.5 a wider, slower kernel is used."),
        vanilla: "1",
        enhanced: "1",
        high: "1.5",

        // 3 at ultra. A real sun casts a soft penumbra -- a window's shadow grid lands on the floor with
        // edges that widen with distance, not the hard stencil a single-tap shadow gives. A wider filter
        // is what reads as that penumbra; the grid stays defined at this radius rather than smearing
        // away, so the shadows read soft but legible rather than crisp.
        ultra: "3");

    /// <summary>How many directions each pixel searches for its horizons, or 0 for no occlusion.</summary>
    /// <remarks>
    /// Follows the same convention as <c>r_fog_quality</c>: the quality dial doubles as the switch,
    /// because a separate on/off would let the two disagree and a preset would then have to set both.
    /// </remarks>
    public static readonly NumericCVar<int> AmbientOcclusion = Presets.Define(
        CVar.Int(
            "r_ao",
            0,
            0,
            8,
            CVarOptions.Archive,
            "Ambient occlusion: directions searched per pixel; 0 turns it off."),

        // Raised, because these are no longer averaged over frames. The horizon search is dithered
        // per pixel AND per frame on the assumption something converges it; with nothing to do that,
        // the only way to a clean result is to take the samples up front.
        //
        // Ultra was eight and is six. The search cost climbs with the square of this (directions AND the
        // per-direction step count both scale with it), so eight is the most expensive stage on a
        // high-resolution frame -- yet with the temporal accumulation and the edge-aware reconstruct
        // behind it, eight, six and four are indistinguishable in a still (measured: 0.09% mean pixel
        // difference between eight and four). Six keeps ultra at high's quality for roughly half the taps.
        // That measurement is why high and ultra share a count: what actually separates them here is
        // r_ao_radius, not the number of directions.
        vanilla: "0",
        enhanced: "4",
        high: "6",
        ultra: "6");

    /// <summary>How far a surface may occlude another, in metres.</summary>
    public static readonly NumericCVar<float> AmbientOcclusionRadius = Presets.Define(
        CVar.Float(
            "r_ao_radius",
            1.5f,
            0.05f,
            16f,
            CVarOptions.Archive,
            "How far, in metres, one surface can shade another."),
        vanilla: "1.5",

        // About a metre on the presets that gather indirect light: the gather owns the room-sized
        // occlusion -- a probe beside a wall already sees the wall -- and this owns the creases it is too
        // coarse to see. At fourteen metres it darkened whole facades in halos and found nothing small.
        enhanced: "1",
        high: "1",
        ultra: "1");

    /// <summary>How strongly the occlusion is applied, where 1 is the physical amount.</summary>
    /// <remarks>
    /// Above about one and a half this stops being occlusion and starts removing the picture. The radius
    /// this backend uses is room-scale rather than contact-scale, so the term already overlaps what the
    /// indirect gather computes; multiplying that overlap by three darkened an office corner until the
    /// lockers standing in it dissolved into the noise floor, while the sunlit floor it could not reach
    /// stayed too bright — the two errors then hid each other in any average over the frame. Measured
    /// across five regions, three scored 8.2 levels of mean error against 5.6 at the physical amount,
    /// and only the physical amount kept the corner readable.
    /// </remarks>
    public static readonly NumericCVar<float> AmbientOcclusionIntensity = Presets.Define(
        CVar.Float(
            "r_ao_intensity",
            1.2f,
            0f,
            4f,
            CVarOptions.Archive,
            "How deeply ambient occlusion darkens; 1 is the physical amount."),
        vanilla: "1.2",
        enhanced: "1.2",
        high: "1.2",
        ultra: "1.2");

    /// <summary>How many samples gather bounced colour, or 0 for no screen-space bounce.</summary>
    /// <remarks>
    /// Off at every preset. This was the cheap screen-space stand-in for a bounce, and the world-space
    /// radiance cache now carries that bounce on every preset that has one -- which is all of them above
    /// vanilla. Running both stacked a second, screen-space estimate on top of the cache's: its occlusion
    /// term read darker than the cache's own, dragging interiors down a stop, while the per-pixel screen
    /// march added noise the cache does not have. Vanilla has no deferred pass to run it in.
    /// <para>
    /// Kept implemented rather than deleted, and reachable from the console, because it is the only bounce
    /// available to a device that cannot run the surface cache -- and because a stand-in that still works is
    /// what lets the real thing be measured against something.
    /// </para>
    /// </remarks>
    public static readonly NumericCVar<int> ScreenSpaceGi = Presets.Define(
        CVar.Int(
            "r_ssgi",
            0,
            0,
            8,
            CVarOptions.Archive,
            "Screen-space bounced light: samples per direction; 0 turns it off."),
        vanilla: "0",
        enhanced: "0",
        high: "0",
        ultra: "0");

    /// <summary>How strongly the bounced colour is added back.</summary>
    public static readonly NumericCVar<float> ScreenSpaceGiIntensity = Presets.Define(
        CVar.Float(
            "r_ssgi_intensity",
            1f,
            0f,
            4f,
            CVarOptions.Archive,
            "How strongly surfaces bounce their colour onto each other."),
        vanilla: "1",
        enhanced: "1",
        high: "1",
        ultra: "1");

    /// <summary>The registry the renderer's own settings describe themselves to.</summary>
    private static GraphicsPresetRegistry Presets => GraphicsPresetRegistry.Default;
}
