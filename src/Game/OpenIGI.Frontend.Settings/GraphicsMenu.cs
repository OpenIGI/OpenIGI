namespace OpenIGI.Frontend.Settings;

/// <summary>One row of the graphics page that is a single variable shown as itself.</summary>
/// <param name="Variable">The variable the row drives.</param>
/// <param name="Caption">The row's caption, as the player reads it.</param>
/// <param name="Help">What the row does, in a sentence written for a player.</param>
/// <param name="Choices">
/// What a choice variable's values are called on screen, keyed by the stored value, or null to show them
/// as stored.
/// </param>
/// <remarks>
/// The caption and the help are written here rather than taken from the variable, and that is the whole
/// point of the type. A variable's own <c>Help</c> is written for whoever is reading the renderer — it
/// says things like "windowed inverse square", "L1-SH layout" and "the pi-over-N estimator" — and it is
/// the right text for the console to print. It is the wrong text to put in front of somebody who wants
/// the picture to look nice.
/// </remarks>
public sealed record GraphicsSetting(
    string Variable,
    string Caption,
    string Help,
    IReadOnlyDictionary<string, string>? Choices = null);

/// <summary>A titled run of rows on the graphics page.</summary>
/// <param name="Title">The heading shown above the rows.</param>
/// <param name="Groups">The quality choices in this section, in the order they are shown.</param>
/// <param name="Settings">The single-variable rows in this section, shown after the groups.</param>
public sealed record GraphicsSection(
    string Title,
    IReadOnlyList<QualityGroup> Groups,
    IReadOnlyList<GraphicsSetting> Settings);

/// <summary>
/// What the graphics page offers: the choices a player expects to find in a game's options, each
/// standing in for however many of the renderer's own variables it takes to mean something.
/// </summary>
/// <remarks>
/// <para>
/// The renderer exposes about a hundred graphics variables, and that is the right number for a renderer
/// to expose. It is the wrong number to put in a menu — and, this page having learned it the hard way,
/// it is the wrong number to put in a menu even behind a door marked Advanced. Somebody who wants light
/// to bounce around a room is looking for "Global Illumination". They are not looking for a card atlas
/// density, a distance-field voxel resolution, a relight amortisation window and a probe gather cadence,
/// four values that are meaningless apart and have to move together to mean anything at all.
/// </para>
/// <para>
/// So there is one page and no second half. Every row here is a name for an idea the player already has,
/// and the console remains what it always was: the place where every variable is reachable by anyone who
/// knows what it is called. That is a better advanced interface than a list of a hundred sliders was,
/// because it comes with completion, help text, a search and the ability to say what you mean.
/// </para>
/// <para>
/// The steps a group offers are exactly the values the four graphics presets use, which is what makes the
/// page and the preset agree: a player on High finds every row reading High rather than Custom, and a row
/// moved on its own drops the preset to Custom because the settings really are no longer any of the four.
/// <c>GraphicsMenuPresetAlignmentTests</c> is what keeps that true — it was written because it had
/// silently stopped being true, three rows having drifted from the table they claim to quote.
/// </para>
/// </remarks>
public static class GraphicsMenu
{
    /// <summary>Gets the page's sections, in the order they are shown.</summary>
    public static IReadOnlyList<GraphicsSection> Sections { get; } =
    [
        new GraphicsSection(
            "Lighting",
            [
                new QualityGroup(
                    "Global Illumination",
                    "Whether light bounces. Sunlight through a window filling the whole room, a red wall "
                        + "tinting the floor beside it, and the level's own lamps lighting the rooms they "
                        + "hang in. Off keeps the lighting the original artists painted into the level, "
                        + "which is cheaper and never wrong, but never responds to anything either.",

                    // Off is not "no lighting" -- it is the shipped lightmaps, which is a whole and
                    // legitimate picture. Every step above it is the same real-time stack; what changes is
                    // how much of the frame and of the load it is allowed to cost. The four load and budget
                    // knobs ride along here precisely because none of them means anything by itself.
                    new QualityLevel(
                        "Off",
                        ("r_lighting", "baked"),
                        ("r_cards", "0"),
                        ("r_sdf", "0"),
                        ("r_screen_gi", "0"),
                        ("r_radiance_cache", "0"),
                        ("r_local_light_scale", "0"),
                        ("r_local_light_shadows", "0"),
                        ("r_cards_density", "8"),
                        ("r_sdf_resolution", "32"),
                        ("r_gi_amortize", "12"),
                        ("r_gi_temporal_frames", "8"),
                        ("r_screen_gi_cadence", "1"),
                        ("r_screen_gi_filter", "2")),
                    new QualityLevel(
                        "Enhanced",
                        ("r_lighting", "dynamic"),
                        ("r_cards", "1"),
                        ("r_sdf", "1"),
                        ("r_screen_gi", "1"),
                        ("r_radiance_cache", "1"),
                        ("r_local_light_scale", "6"),
                        ("r_local_light_shadows", "1"),
                        ("r_cards_density", "12"),
                        ("r_sdf_resolution", "32"),
                        ("r_gi_amortize", "48"),
                        ("r_gi_temporal_frames", "16"),
                        ("r_screen_gi_cadence", "2"),
                        ("r_screen_gi_filter", "1")),
                    new QualityLevel(
                        "High",
                        ("r_lighting", "dynamic"),
                        ("r_cards", "1"),
                        ("r_sdf", "1"),
                        ("r_screen_gi", "1"),
                        ("r_radiance_cache", "1"),
                        ("r_local_light_scale", "6"),
                        ("r_local_light_shadows", "1"),
                        ("r_cards_density", "18"),
                        ("r_sdf_resolution", "40"),
                        ("r_gi_amortize", "32"),
                        ("r_gi_temporal_frames", "24"),
                        ("r_screen_gi_cadence", "1"),
                        ("r_screen_gi_filter", "2")),
                    new QualityLevel(
                        "Ultra",
                        ("r_lighting", "dynamic"),
                        ("r_cards", "1"),
                        ("r_sdf", "1"),
                        ("r_screen_gi", "1"),
                        ("r_radiance_cache", "1"),
                        ("r_local_light_scale", "6"),
                        ("r_local_light_shadows", "1"),
                        ("r_cards_density", "23.05"),
                        ("r_sdf_resolution", "46"),
                        ("r_gi_amortize", "24"),
                        ("r_gi_temporal_frames", "32"),
                        ("r_screen_gi_cadence", "2"),
                        ("r_screen_gi_filter", "2"))),

                new QualityGroup(
                    "Shadow Quality",
                    "Sun shadows: how sharp they are, how far they reach, and whether small objects still "
                        + "cast where they touch the ground. Usually the largest single cost in the frame "
                        + "after the lighting itself.",
                    new QualityLevel(
                        "Off",
                        ("r_shadows", "0"),
                        ("r_shadow_cascades", "1"),
                        ("r_shadow_distance", "220"),
                        ("r_contact_shadows", "0"),
                        ("r_shadow_softness", "1"),
                        ("r_shadow_accumulate", "1")),
                    new QualityLevel(
                        "Enhanced",
                        ("r_shadows", "1024"),
                        ("r_shadow_cascades", "2"),
                        ("r_shadow_distance", "150"),
                        ("r_contact_shadows", "0"),
                        ("r_shadow_softness", "1"),
                        ("r_shadow_accumulate", "8")),
                    new QualityLevel(
                        "High",
                        ("r_shadows", "2048"),
                        ("r_shadow_cascades", "3"),
                        ("r_shadow_distance", "220"),
                        ("r_contact_shadows", "0.35"),
                        ("r_shadow_softness", "1.5"),
                        ("r_shadow_accumulate", "16")),
                    new QualityLevel(
                        "Ultra",
                        ("r_shadows", "4096"),
                        ("r_shadow_cascades", "4"),
                        ("r_shadow_distance", "150"),
                        ("r_contact_shadows", "0.5"),
                        ("r_shadow_softness", "3"),
                        ("r_shadow_accumulate", "16"))),

                // Three steps, not four, and High is deliberately the top of them. The High and Ultra
                // presets both search six directions: r_ao's own notes record that eight, six and four
                // measure 0.09% apart in a still once the temporal accumulation and the edge-aware
                // reconstruct are behind them, so six was chosen for ultra on purpose and a fourth rung
                // would be twice the taps for nothing anybody can see.
                //
                // Shipping it anyway as a duplicate of High would be worse than leaving it out. Detect
                // returns the FIRST step whose values all match, so the second of two identical steps can
                // never be read back -- it would sit in the list, be selectable, and then display as the
                // step above it. That is exactly the bug this page had: an Ultra rung of r_ao 8 that no
                // preset ever wrote, so a player on Ultra saw the row say High and had no way to tell
                // whether the setting or the display was wrong.
                new QualityGroup(
                    "Ambient Occlusion",
                    "The soft darkening where surfaces meet — under crates, in corners, along the seam "
                        + "between a wall and the floor. The higher steps search more directions per pixel, "
                        + "so the result is cleaner rather than deeper; the reach stays at contact scale, "
                        + "because the indirect lighting already darkens a room's corners at room scale. "
                        + "High is the top step because more directions than this measure "
                        + "indistinguishable — the Ultra preset uses it too.",
                    new QualityLevel("Off", ("r_ao", "0"), ("r_ao_radius", "1.5"), ("r_ao_intensity", "1.2")),
                    new QualityLevel("Enhanced", ("r_ao", "4"), ("r_ao_radius", "1"), ("r_ao_intensity", "1.2")),
                    new QualityLevel("High", ("r_ao", "6"), ("r_ao_radius", "1"), ("r_ao_intensity", "1.2"))),

                new QualityGroup(
                    "Reflections",
                    "Reflections on wet ground, glass and polished floors. Off still leaves the cheap "
                        + "fallback, so surfaces do not go flat black — they simply stop reflecting what is "
                        + "actually in front of them.",
                    new QualityLevel("Off", ("r_ssr", "0"), ("r_ssr_intensity", "1"), ("r_wetness", "0")),
                    new QualityLevel("Enhanced", ("r_ssr", "32"), ("r_ssr_intensity", "1.7"), ("r_wetness", "0")),
                    new QualityLevel("High", ("r_ssr", "48"), ("r_ssr_intensity", "1.7"), ("r_wetness", "1")),
                    new QualityLevel("Ultra", ("r_ssr", "64"), ("r_ssr_intensity", "1.7"), ("r_wetness", "1"))),

                new QualityGroup(
                    "Volumetric Fog",
                    "Fog that light actually travels through — shafts through windows, haze thickening "
                        + "down a valley. The samples taken along each ray are both the quality and the "
                        + "cost; the fog's SHAPE is the mission's own, authored per level, which is why "
                        + "only the sample count changes across these steps.",
                    new QualityLevel(
                        "Off",
                        ("r_fog_quality", "0"),
                        ("r_fog_distance", "500"),
                        ("r_fog_density", "0"),
                        ("r_fog_height", "24"),
                        ("r_fog_anisotropy", "0.7")),
                    new QualityLevel(
                        "Enhanced",
                        ("r_fog_quality", "32"),
                        ("r_fog_distance", "500"),
                        ("r_fog_density", "0"),
                        ("r_fog_height", "24"),
                        ("r_fog_anisotropy", "0.7")),
                    new QualityLevel(
                        "High",
                        ("r_fog_quality", "64"),
                        ("r_fog_distance", "500"),
                        ("r_fog_density", "0"),
                        ("r_fog_height", "24"),
                        ("r_fog_anisotropy", "0.7")),
                    new QualityLevel(
                        "Ultra",
                        ("r_fog_quality", "96"),
                        ("r_fog_distance", "500"),
                        ("r_fog_density", "0"),
                        ("r_fog_height", "24"),
                        ("r_fog_anisotropy", "0.7"))),
            ],
            []),

        new GraphicsSection(
            "Image",
            [
                new QualityGroup(
                    "Upscaling",
                    "Renders the scene smaller and reconstructs it to your window, which is where most of "
                        + "the frame rate is. The temporal filter builds the missing detail back out of "
                        + "previous frames, so the picture holds up far better than a plain stretch — but "
                        + "the further below native it starts, the more fine edges it has to invent.",

                    // The vocabulary every upscaler ships with, cheapest first, rather than the preset
                    // names: a player looking for this knows it as Quality/Balanced/Performance. Vanilla
                    // and ultra are both native, so they are one step -- see the note on Ambient Occlusion
                    // for why two identical steps must never be shipped as two.
                    new QualityLevel("Balanced (70%)", ("r_scale", "0.7")),
                    new QualityLevel("Quality (85%)", ("r_scale", "0.85")),
                    new QualityLevel("Native (100%)", ("r_scale", "1"))),

                new QualityGroup(
                    "Anti-Aliasing",
                    "Smooths the stair-stepping along edges by averaging each pixel across several frames. "
                        + "It is also what pays for the noise in the fog, the occlusion and the reflections, "
                        + "and what reconstructs detail when Upscaling is below native — so turning it off "
                        + "costs more than jagged edges.",
                    new QualityLevel(
                        "Off",
                        ("r_taa", "1"),
                        ("r_taa_sharpen", "0.25"),
                        ("r_aa", "0"),
                        ("r_aa_threshold", "0.05"),
                        ("r_thin_geometry", "0")),
                    new QualityLevel(
                        "On",
                        ("r_taa", "0.1"),
                        ("r_taa_sharpen", "0.7"),
                        ("r_aa", "0"),
                        ("r_aa_threshold", "0.33"),
                        ("r_thin_geometry", "0.75"))),

                new QualityGroup(
                    "Texture Quality",
                    "How sharp surfaces stay at a distance, and how much relief is derived from the game's "
                        + "flat textures. The cheapest visible setting on the page — the filtering alone "
                        + "decides whether the ground smears towards the horizon.",
                    new QualityLevel(
                        "Original",
                        ("r_material_detail", "0"),
                        ("r_anisotropy", "1")),
                    new QualityLevel(
                        "Enhanced",
                        ("r_material_detail", "0"),
                        ("r_anisotropy", "4")),
                    new QualityLevel(
                        "High",
                        ("r_material_detail", "0.25"),
                        ("r_anisotropy", "8")),
                    new QualityLevel(
                        "Ultra",
                        ("r_material_detail", "0"),
                        ("r_anisotropy", "16"))),

                new QualityGroup(
                    "World Detail",
                    "How finely the ground is subdivided into the distance. Higher keeps hills and ditches "
                        + "their shape further out; Original is the subdivision the game shipped with.",
                    new QualityLevel("Original", ("r_terrain_detail", "0")),
                    new QualityLevel("Enhanced", ("r_terrain_detail", "220")),
                    new QualityLevel("High", ("r_terrain_detail", "320")),
                    new QualityLevel("Ultra", ("r_terrain_detail", "450"))),

                new QualityGroup(
                    "Image Style",
                    "The photographic grade over the finished frame: contrast, colour, the darkening at "
                        + "the corners, the fringing in the lens, the bloom and the sharpening. Taste "
                        + "rather than fidelity — Off is the untouched picture, and Ultra is the neutral "
                        + "grade with nothing added.",
                    new QualityLevel(
                        "Off",
                        ("r_contrast", "1"),
                        ("r_saturation", "1"),
                        ("r_vignette", "0"),
                        ("r_chromatic", "0"),
                        ("r_lut", "0"),
                        ("r_sharpen", "0"),
                        ("r_bloom", "0"),
                        ("r_bloom_threshold", "1")),
                    new QualityLevel(
                        "Enhanced",
                        ("r_contrast", "1.04"),
                        ("r_saturation", "1.05"),
                        ("r_vignette", "0.25"),
                        ("r_chromatic", "0"),
                        ("r_lut", "0.5"),
                        ("r_sharpen", "0.1"),
                        ("r_bloom", "0.25"),
                        ("r_bloom_threshold", "0")),
                    new QualityLevel(
                        "High",
                        ("r_contrast", "1.06"),
                        ("r_saturation", "1.08"),
                        ("r_vignette", "0.3"),
                        ("r_chromatic", "0.8"),
                        ("r_lut", "0.75"),
                        ("r_sharpen", "0.15"),
                        ("r_bloom", "0.35"),
                        ("r_bloom_threshold", "0")),
                    new QualityLevel(
                        "Ultra",
                        ("r_contrast", "1"),
                        ("r_saturation", "1"),
                        ("r_vignette", "0.4"),
                        ("r_chromatic", "0"),
                        ("r_lut", "0"),
                        ("r_sharpen", "0"),
                        ("r_bloom", "0.2"),
                        ("r_bloom_threshold", "0"))),
            ],
            []),

        new GraphicsSection(
            "NVIDIA",
            [],
            [
                new GraphicsSetting(
                    "r_reconstruction",
                    "Reconstruction",
                    "What rebuilds the full-resolution picture each frame. Engine is the game's own temporal "
                        + "filter; DLSS is NVIDIA's network, sharper and usually faster; Ray Reconstruction also "
                        + "cleans up the traced lighting, at a higher cost. Needs an NVIDIA RTX GPU and the "
                        + "NVIDIA DLSS download from the launcher."),
                new GraphicsSetting(
                    "r_dlss_quality",
                    "DLSS Quality",
                    "How far below your window DLSS renders before it rebuilds the picture. DLAA renders at "
                        + "full resolution and only smooths it; Performance renders a quarter of the pixels. "
                        + "Takes the place of Upscaling while DLSS runs."),
                new GraphicsSetting(
                    "r_frame_generation",
                    "Frame Generation",
                    "Shows generated frames between the rendered ones, multiplying the frame rate. Needs an "
                        + "RTX 40-series or newer, and turns NVIDIA Reflex on to keep the controls responsive."),
                new GraphicsSetting(
                    "r_low_latency",
                    "NVIDIA Reflex",
                    "Starts each frame as late as the GPU allows, so what you see answers the mouse sooner. "
                        + "Boost also keeps the GPU's clocks up."),
            ]),

        new GraphicsSection(
            "Camera",
            [],
            [
                new GraphicsSetting(
                    "r_exposure",
                    "Brightness",
                    "How bright the picture is overall. The eye still adapts to each room on its own; this "
                        + "nudges the result up or down to suit your display."),
                new GraphicsSetting(
                    "r_motion_blur",
                    "Motion Blur",
                    "Smears the picture along fast movement, the way a camera shutter does. Off is the "
                        + "sharper and the more responsive-feeling of the two."),
                new GraphicsSetting(
                    "r_grain",
                    "Film Grain",
                    "Adds a fine moving speckle over the frame, like film stock. A little of it also "
                        + "breaks up the banding a smooth sky picks up on an 8-bit display."),
            ]),
    ];

    /// <summary>
    /// Gets the rows that are graphics variables but are not graphics <em>quality</em>, shown on the
    /// Gameplay page instead.
    /// </summary>
    /// <remarks>
    /// Every one of these is an <c>r_</c> variable, so the old page collected them by prefix and put them
    /// among the quality settings. That is where the prefix stops being a good guide: whether the
    /// remastered HUD is drawn, whether spotted enemies are outlined, and whether rounds leave marks are
    /// choices about what the player is shown and what the game does, not about how well it is drawn. They
    /// belong beside the other gameplay and interface options, and a player looking for them looks there.
    /// </remarks>
    public static IReadOnlyList<GraphicsSetting> InterfaceSettings { get; } =
    [
        new GraphicsSetting(
            "r_day_night_cycle",
            "Day/Night Cycle",
            "Lets time pass while you play. A mission still begins at the hour it was written for, and "
                + "the sun goes on from there, so a long mission can end under a different sky. Off, "
                + "every mission stays at its own hour, the way the original's do."),
        new GraphicsSetting(
            "r_day_length",
            "Day Length",
            "How long a whole day takes, in minutes, once the cycle above is on. Forty is a sun you "
                + "notice having moved; a minute or two is a sunrise you can stand and watch."),
        new GraphicsSetting(
            "r_lamp_daylight",
            "Lamps Follow Daylight",
            "Puts the level's lamps on photocells: they go out by day and strike one by one as dusk "
                + "falls, each on its own delay. Outdoors leaves lamps under a roof lit, as a building's "
                + "are; Everywhere includes them; Off keeps every lamp burning, as in the original."),
        new GraphicsSetting(
            "r_remastered_hud",
            "Remastered HUD",
            "Draws the health, ammunition and objective readouts in the remaster's own style. Off returns "
                + "the original's."),
        new GraphicsSetting(
            "r_interaction_prompt",
            "Interaction Markers",
            "Marks a door, ladder or terminal you can use with a badge on the object itself, showing the "
                + "key you have bound and, where the action takes time, a ring that fills as you hold it. "
                + "Off, the same prompt is a line of text across the middle of the screen."),
        new GraphicsSetting(
            "r_computer_tactical",
            "Tactical Computer",
            "Draws the map computer in the remaster's style, with the level's objectives and your own "
                + "position on it."),
        new GraphicsSetting(
            "r_messages_tactical",
            "Mission Messages",
            "Shows objective and radio messages as on-screen cards rather than as plain lines of text."),
        new GraphicsSetting(
            "r_binocular_lens",
            "Binocular Optics",
            "Draws the binoculars with a lens: a round field of view, edge darkening and a little colour "
                + "fringing towards the rim."),
        new GraphicsSetting(
            "r_binocular_outline",
            "Highlight Spotted Enemies",
            "Outlines soldiers seen through the binoculars, so a distant figure against a hillside can be "
                + "picked out."),
        new GraphicsSetting(
            "r_world_briefing",
            "Posted Briefing",
            "Stands the mission briefing against the nearest wall or fence where you start, to be read "
                + "by walking up to it. Off, the briefing is only in the menu."),
        new GraphicsSetting(
            "r_weather_enabled",
            "Weather",
            "Draws the rain or snow the level authors gave each mission."),
        new GraphicsSetting(
            "r_storm",
            "Storm Effects",
            "Adds wind-driven streaks, splashes where the rain lands, and lightning to a level whose "
                + "weather is a storm."),
        new GraphicsSetting(
            "r_time_of_day",
            "Time of Day",
            "The hour the sun starts each mission at. -1 is each level's own authored hour, which is the "
                + "light it was made for; the bracket keys move it while you play. Lasts the session."),
        new GraphicsSetting(
            "r_impact_particles",
            "Impact Effects",
            "Sparks, dust and debris where rounds land, chosen to suit the surface that was hit."),
        new GraphicsSetting(
            "r_bulletmarks",
            "Bullet Marks",
            "Leaves the marks rounds make on walls and floors."),
        new GraphicsSetting(
            "r_muzzle_flash",
            "Muzzle Flash",
            "How bright the flame at the barrel is. The original's is bright enough to swallow the first "
                + "few metres of a tracer."),
        new GraphicsSetting(
            "r_damage_flash",
            "Damage Flash",
            "How strongly the screen washes red when you are hit."),
    ];

    /// <summary>Gets the rows shown on the Display page, with captions written for a player.</summary>
    /// <remarks>
    /// <para>
    /// The ones every game's display options have, in the order a player looks for them: what draws, the
    /// window, its size, then how frames reach it. Captioned here rather than by machine from their
    /// variable names, which would turn one of them into "Smooth" — which says nothing.
    /// </para>
    /// <para>
    /// <c>r_resolution</c> and <c>r_frame_limit</c> are not choices their types spell out — one is the
    /// display's modes, the other a number — so the page builds their lists itself; see
    /// <see cref="FrameRateCaps"/>.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<GraphicsSetting> DisplaySettings { get; } =
    [
        new GraphicsSetting(
            "r_renderer",
            "Graphics API",
            "What the game draws through. Automatic is Direct3D 12 on Windows and Vulkan elsewhere; WebGPU "
                + "is the browser version's, and the slowest of the three. Changing it rebuilds the renderer and "
                + "puts you back where you were, which takes a few seconds.",
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["auto"] = "Automatic",
                ["d3d12"] = "Direct3D 12",
                ["vulkan"] = "Vulkan",
                ["webgpu"] = "WebGPU",
            }),
        new GraphicsSetting(
            "r_window_mode",
            "Window Mode",
            "Borderless covers the screen at the desktop's resolution and switches away instantly. Fullscreen "
                + "takes the monitor over at the resolution below. Windowed runs in a window that size.",
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["windowed"] = "Windowed",
                ["borderless"] = "Borderless",
                ["fullscreen"] = "Fullscreen",
            }),
        new GraphicsSetting(
            "r_resolution",
            "Resolution",
            "The window's size, or the monitor's mode in fullscreen. Desktop is your screen's own. To render "
                + "fewer pixels at the same size, lower Upscaling on the Graphics page instead."),
        new GraphicsSetting(
            "r_vsync",
            "Vertical Sync",
            "Waits for the display to be ready before showing each frame. On removes tearing; off uncaps "
                + "the frame rate and lowers the delay between mouse and picture."),
        new GraphicsSetting(
            "r_smooth",
            "Smooth Motion",
            "Draws the world at your display's rate rather than the original's fixed thirty steps a "
                + "second, interpolating between them. Also removes IGI2's mouse filtering and turn-speed cap. "
                + "Off restores the original cadence and IGI2 mouse response."),
        new GraphicsSetting(
            "r_frame_limit",
            "Frame Rate Cap",
            "The most frames a second the game draws. A cap a little under your display's rate keeps the "
                + "motion even and the GPU cooler. Held by NVIDIA Reflex where it runs, which keeps the delay "
                + "low while it does."),
    ];

    /// <summary>Gets the frame-rate caps the Display page offers, in frames a second; nought is none.</summary>
    /// <remarks>The rates displays commonly run at, and the fractions of them a steady cap is chosen from.</remarks>
    public static IReadOnlyList<int> FrameRateCaps { get; } = [0, 30, 60, 75, 90, 100, 120, 144, 165, 180, 240, 360];

    /// <summary>Gets every quality group on the page, in the order they are shown.</summary>
    public static IReadOnlyList<QualityGroup> Groups { get; } =
        [.. Sections.SelectMany(section => section.Groups)];

    /// <summary>Gets every single-variable row on the page, in the order they are shown.</summary>
    public static IReadOnlyList<GraphicsSetting> Settings { get; } =
        [.. Sections.SelectMany(section => section.Settings)];

    /// <summary>
    /// Gets the archived graphics variables the page deliberately does not show, each with the reason.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A list rather than an absence, so that "not on the page" is a decision somebody made and can be
    /// argued with, rather than something that fell off. A new archived <c>r_</c> variable has to be given
    /// a row or a line here; <c>GraphicsMenuPresetAlignmentTests</c> fails until it is.
    /// </para>
    /// <para>
    /// Everything here is still reachable, and reachable the same way it always was: by name, from the
    /// console, with completion and its own help text. That is the advanced interface. What was removed
    /// was a menu page that listed all of them as sliders, which served nobody — a player could not tell
    /// which of "screen probe gather cadence" and "surface-cache relight amortisation" to move, and anyone
    /// who could already knew the console.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<string> ConsoleOnly { get; } =
    [
        // The renderer switch itself. The Preset row is the player's version of this question, and
        // answering it a piece at a time produces combinations that are simply broken.
        "r_hdr",
        "r_deferred",
        "r_dlssg_ui_recomposition", // frame generation's interface handling; on is NVIDIA's recommendation
        "r_msaa",           // inert on the deferred path, where the temporal filter is the anti-aliasing
        "r_autoexposure",
        "r_tonemap",

        // Calibration constants. Each was measured against a reference capture and several are read only
        // by the dynamic lighting model; they are the difference between a correct picture and a
        // mis-exposed one, not between a fast picture and a pretty one. A player moving one has not
        // chosen a quality level, they have broken the calibration.
        "r_white_point",
        "r_light_scale",
        "r_sun_scale",
        "r_atmosphere_sun_scale",
        "r_sky_light",
        "r_sun_angle",
        // Read once when the graphics device is created, because it sizes an image allocated there. A
        // slider that did nothing until the next launch would be worse than no slider.
        "r_reflection_texture",

        // What enters the traced world, which is a correctness control rather than a quality dial: the
        // defaults are what make a reflection the same in two sessions, and a player has no way to judge
        // a radius by looking. See RayTracingCVars.TracedWorldRadius.
        "r_rt_world_radius",
        "r_rt_world_angle",

        "r_gi_bounce",
        "r_gi_boost",
        "r_gi_intensity",
        "r_gi_leak_reject",
        "r_radiance_cache_scale",
        "r_lamp_emissive",
        "r_local_light_source",
        "r_local_light_range",
        "r_ssr_max_roughness",
        "r_binocular_range",

        // The flashlight follows the preset and has its own key; a menu row would be a second switch for
        // the same light. Its shadow and its beam in the air are details of a light the player turns on
        // and off themselves.
        "r_flashlight",
        "r_flashlight_shadows",
        "r_flashlight_fog",
        "r_flashlight_wear", // how worn the torch's switch is: the character of the prop, not a quality
        "r_binocular_range_zoomed",

        // Superseded paths, kept working and kept reachable, but not offered: the real-time stack carries
        // what they carried, and offering both invites stacking two estimates of the same light.
        "r_gi",
        "r_lightmap",
        "r_gi_probe_spacing",
        "r_ssgi",
        "r_ssgi_intensity",
        "r_floor_gloss",

        // Internal budgets and behaviour switches with no axis a player could have an opinion along.
        "r_gi_cache_radius",
        "r_gi_converge",    // a load-time budget, not a look: how far the cache is solved before frame one
        "r_mission_warmup", // how long the level is held back to settle; not a quality the player picks
        "r_gi_lamps",       // whether the level's lamps are detected at all; the GI row decides if they light
        "r_screen_gi_max",
        "r_screen_gi_trace",
        "r_upscale_samples",
        "r_upscale_history", // the upscaler's history resolution, a cost the preset already weighs
        "r_alpha_hash",
        "r_world_cull",
        "r_world_occlusion",
        "r_terrain_occlusion",
        "r_terrain_cache",  // how much streamed ground stays resident; the detail row is the player's version
        "r_gi_far_refresh", // how many frames the far GI cache sweep is spread over; each preset picks its cost

        // The night's calibration. The sky's scatter, the airglow and the moonlight were tuned together
        // until a night read as dark but legible; moving one alone relights every night mission. The
        // stars, the moon's disc and the night-vision shift are the Preset row's (off on vanilla, on
        // above it), not a separate choice.
        "r_night_sky",
        "r_night_airglow",
        "r_moonlight",
        "r_moon_brightness",
        "r_stars",
        "r_night_shift",

        // Which fog the picture wears, which the Preset row has already answered: vanilla draws the
        // original's own per-vertex haze into the level's fog colour, and every tier above it marches
        // the same authored haze through the froxel volume instead. A player who could set this
        // separately could set both at once, which fogs the frame twice.
        "r_fog_retail",

        // The shape of the fog's light rather than its cost: how much it throws back towards the sun and
        // how much the haze breaks up. Tuned against reference captures and set by the Preset row (off on
        // vanilla, on above it); a player moving one has re-lit the level, not chosen a quality level.
        "r_fog_back_scatter",
        "r_fog_back_weight",
        "r_fog_noise",
        "r_sun_shafts",

        // Diagnostics, A/B levers and paths on disk.
        "r_rt",
        "r_rt_shadows",
        "r_rt_lights",
        "r_rt_reflections",
        "r_rt_reflection_clamp",
        "r_rt_reflection_roughness",
        "r_glass_reflection",
        "r_player_body",
        "r_lamp_denoise",
        // Preset-invariant retail impact dust/chips/blood. This is a diagnostic kill switch, while
        // r_impact_particles is the player-facing switch for the remaster's additional burst.
        "r_impact_effects",
        "r_sdf_validate",
        "r_cards_dump",
        "r_cards_probe",
        "r_perflog",
        "r_debug_view",
        "r_lut_table",

        // The level's own authored weather, overridden. The Weather row on the Gameplay page turns the
        // level's weather on and off; choosing a different one than the level ships with is authoring.
        "r_weather_kind",
        "r_weather_force",
        "r_weather_speed",
        "r_storm_intensity",
    ];
}
