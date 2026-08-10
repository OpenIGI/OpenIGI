namespace OpenIGI.Frontend.Settings;

/// <summary>
/// What the basic graphics page offers: the handful of choices a player expects to find in a game's
/// options, each standing in for a set of the renderer's own variables.
/// </summary>
/// <remarks>
/// <para>
/// The renderer exposes about fifty graphics variables, and that is the right number for a renderer to
/// expose — every one of them earns its place, and being able to reach any of them is most of why the
/// advanced page exists. It is the wrong number to open a settings screen on. Someone who wants shadows
/// to cost less is looking for "Shadow Quality", not for a map resolution, a cascade count, a distance,
/// a contact-ray reach and a filter radius that have to be moved together to mean anything.
/// </para>
/// <para>
/// So the basic page is a smaller vocabulary over the same variables rather than a smaller set of them.
/// The steps are the values the four graphics presets already use, which is what makes the two agree: a
/// player on the High preset finds every group reading High rather than Custom, and a group moved on its
/// own leaves the preset at Custom because the preset really is no longer any of the four.
/// </para>
/// <para>
/// Nothing here is hidden from the advanced page. Every variable a group names has its own row there, and
/// that is the point — the basic page is a way of driving those rows, not a replacement for them.
/// </para>
/// </remarks>
public static class BasicGraphics
{
    /// <summary>Gets the coarse quality choices the basic page offers, in the order they are shown.</summary>
    public static IReadOnlyList<QualityGroup> Groups { get; } =
    [
        new QualityGroup(
            "Texture Quality",
            "How sharp surfaces stay at a distance, and how much relief is derived from the game's flat "
                + "textures. The cheapest visible setting here — anisotropic filtering alone decides whether "
                + "the ground smears towards the horizon.",
            new QualityLevel("Low", ("r_material_detail", "0"), ("r_anisotropy", "1"), ("r_generate_mipmaps", "0")),
            new QualityLevel("Medium", ("r_material_detail", "0"), ("r_anisotropy", "4"), ("r_generate_mipmaps", "1")),
            new QualityLevel("High", ("r_material_detail", "0.25"), ("r_anisotropy", "8"), ("r_generate_mipmaps", "1")),
            new QualityLevel("Ultra", ("r_material_detail", "0.3"), ("r_anisotropy", "16"), ("r_generate_mipmaps", "1"))),

        new QualityGroup(
            "Shadow Quality",
            "Sun shadows: how sharp they are, how far they reach, and whether small objects still cast "
                + "where they touch the ground. Usually the largest single cost in the frame.",
            new QualityLevel(
                "Off",
                ("r_shadows", "0"),
                ("r_shadow_cascades", "1"),
                ("r_shadow_distance", "220"),
                ("r_contact_shadows", "0"),
                ("r_shadow_softness", "1")),
            new QualityLevel(
                "Low",
                ("r_shadows", "1024"),
                ("r_shadow_cascades", "2"),
                ("r_shadow_distance", "150"),
                ("r_contact_shadows", "0"),
                ("r_shadow_softness", "1")),
            new QualityLevel(
                "High",
                ("r_shadows", "2048"),
                ("r_shadow_cascades", "3"),
                ("r_shadow_distance", "220"),
                ("r_contact_shadows", "0.35"),
                ("r_shadow_softness", "1.5")),
            new QualityLevel(
                "Ultra",
                ("r_shadows", "2048"),
                ("r_shadow_cascades", "4"),
                ("r_shadow_distance", "320"),
                ("r_contact_shadows", "0.5"),
                ("r_shadow_softness", "2.5"))),

        new QualityGroup(
            "Ambient Occlusion",
            "The soft darkening where surfaces meet — under crates, in corners, along the seam between a "
                + "wall and the floor. Higher steps search more directions per pixel, so the result is "
                + "cleaner rather than deeper.",
            new QualityLevel("Off", ("r_ao", "0")),
            new QualityLevel("Low", ("r_ao", "4")),
            new QualityLevel("High", ("r_ao", "6")),
            new QualityLevel("Ultra", ("r_ao", "8"))),

        new QualityGroup(
            "Reflections",
            "Screen-space reflections on wet ground, glass and polished floors. Off still leaves the "
                + "cheap probe fallback, so surfaces do not go flat black — they simply stop reflecting "
                + "what is actually in front of them.",
            new QualityLevel("Off", ("r_ssr", "0")),
            new QualityLevel("High", ("r_ssr", "32")),
            new QualityLevel("Ultra", ("r_ssr", "64"))),

        new QualityGroup(
            "Bounced Light",
            "Lets surfaces throw their own colour onto each other, so a red wall tints the floor beside "
                + "it instead of everything out of the sun reading the same flat ambient.",
            new QualityLevel("Off", ("r_ssgi", "0")),
            new QualityLevel("High", ("r_ssgi", "2")),
            new QualityLevel("Ultra", ("r_ssgi", "4"))),

        new QualityGroup(
            "Volumetric Fog",
            "Fog that light actually travels through — shafts through windows, haze thickening down a "
                + "valley. The samples per ray are both the quality and the cost.",
            new QualityLevel("Off", ("r_fog_quality", "0"), ("r_fog_distance", "500")),
            new QualityLevel("Low", ("r_fog_quality", "32"), ("r_fog_distance", "350")),
            new QualityLevel("High", ("r_fog_quality", "64"), ("r_fog_distance", "500")),
            new QualityLevel("Ultra", ("r_fog_quality", "96"), ("r_fog_distance", "800"))),

        new QualityGroup(
            "Anti-aliasing",
            "How edges are smoothed. Spatial works on the frame alone and never ghosts; temporal averages "
                + "across frames, which is cleaner and is also what pays for the noise in the fog, the "
                + "occlusion and the reflections.",
            new QualityLevel("Off", ("r_aa", "0"), ("r_taa", "1")),
            new QualityLevel("Spatial", ("r_aa", "0.75"), ("r_taa", "1")),
            new QualityLevel("Temporal", ("r_aa", "0"), ("r_taa", "0.15")),
            new QualityLevel("Temporal (Max)", ("r_aa", "0"), ("r_taa", "0.1"))),

        new QualityGroup(
            "Image Style",
            "The photographic grade over the finished frame: contrast, colour, the darkening at the "
                + "corners, the fringing in the lenses and the sharpening. Taste, not fidelity — Off is "
                + "the untouched picture.",
            new QualityLevel(
                "Off",
                ("r_contrast", "1"),
                ("r_saturation", "1"),
                ("r_vignette", "0"),
                ("r_chromatic", "0"),
                ("r_lut", "0"),
                ("r_sharpen", "0")),
            new QualityLevel(
                "Subtle",
                ("r_contrast", "1.04"),
                ("r_saturation", "1.05"),
                ("r_vignette", "0.25"),
                ("r_chromatic", "0"),
                ("r_lut", "0.5"),
                ("r_sharpen", "0.1")),
            new QualityLevel(
                "Standard",
                ("r_contrast", "1.06"),
                ("r_saturation", "1.08"),
                ("r_vignette", "0.3"),
                ("r_chromatic", "0.8"),
                ("r_lut", "0.75"),
                ("r_sharpen", "0.15")),
            new QualityLevel(
                "Strong",
                ("r_contrast", "1.08"),
                ("r_saturation", "1.1"),
                ("r_vignette", "0.35"),
                ("r_chromatic", "1.2"),
                ("r_lut", "1"),
                ("r_sharpen", "0.2"))),
    ];

    /// <summary>Gets the variables the basic page shows as themselves, rather than through a group.</summary>
    /// <remarks>
    /// A setting every game has and every player already understands, which one number says completely.
    /// Wrapping brightness in a Low/Medium/High would be worse than the slider, not better. These are the
    /// only variables the advanced page does not repeat — a second row for the same single number would be
    /// two places to change one thing.
    /// </remarks>
    public static IReadOnlyList<string> DirectVariables { get; } =
    [
        "r_exposure",
        "r_bloom",
        "r_motion_blur",
        "r_grain",
        "r_remastered_hud",
    ];
}
