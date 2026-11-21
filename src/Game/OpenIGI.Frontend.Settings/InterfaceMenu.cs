namespace OpenIGI.Frontend.Settings;

/// <summary>What the Interface page offers: the HUD, the crosshair, what the game marks and tells, and the optics.</summary>
/// <remarks>
/// Every one of these is a choice about what the player is shown rather than how well it is drawn, so none of them is a
/// graphics quality, and none is touched by the graphics preset.
/// </remarks>
public static class InterfaceMenu
{
    /// <summary>Gets the page's panels, in the order they are shown.</summary>
    public static IReadOnlyList<SettingsPanel> Panels { get; } =
    [
        new SettingsPanel(
            "HUD",
            [],
            [
                new SettingRow(
                    "r_remastered_hud",
                    "Remastered HUD",
                    "Draws the health, ammunition and objective readouts in the remaster's own style. Off returns "
                        + "the original's."),
                new SettingRow(
                    "hud_retail_scale",
                    "Original HUD Size",
                    "How big the original HUD is drawn. Automatic enlarges it to suit your screen; Original draws it at "
                        + "its own size, as the original game does, small and in the corners of a large screen.",
                    new Dictionary<string, string>
                    {
                        ["0"] = "Automatic",
                        ["1"] = "Original",
                        ["2"] = "2x",
                        ["3"] = "3x",
                        ["4"] = "4x",
                    }) { ShownWhen = new("r_remastered_hud", WhileOff: true) },
                new SettingRow(
                    "r_hud_density",
                    "HUD Density",
                    "How much of the remastered HUD stays up. Contextual dims the weapon and inventory until something "
                        + "about them changes; Minimal shows only what is urgent; Full keeps everything on screen.") { ShownWhen = new("r_remastered_hud") },
                new SettingRow(
                    "r_hud_threat",
                    "Alert Status",
                    "Says at the top of the screen how far the soldiers are onto you: searching, suspicious, alarm or detected."),
                new SettingRow(
                    "r_hud_fx",
                    "HUD Visor Effects",
                    "Projects the remastered HUD as if on a visor: a slight bend, colour fringing, scanlines, a glow on "
                        + "bright figures, a flicker as it appears, and a shake and a red wash at the corners when you are hit.") { Advanced = true, ShownWhen = new("r_remastered_hud") },
                new SettingRow(
                    "r_hud_fx_intensity",
                    "HUD Visor Strength",
                    "How strongly the visor effects above are applied.") { Advanced = true, ShownWhen = new("r_hud_fx") },
                new SettingRow(
                    "r_hud_sway",
                    "HUD Sway",
                    "Lets the HUD trail your view by a few pixels as you turn, then settle, as a helmet's display would.") { Advanced = true, ShownWhen = new("r_remastered_hud") },
            ]),

        new SettingsPanel(
            "Crosshair",
            [],
            [
                new SettingRow(
                    "r_crosshair",
                    "Modern Crosshair",
                    "A clean crosshair shaped to the weapon in hand: it opens as you fire and turns red over an enemy. The rows "
                        + "below style it. Off draws the original game's own crosshair."),
                new SettingRow(
                    "r_crosshair_color",
                    "Crosshair Colour",
                    "The crosshair's colour.",
                    new Dictionary<string, string>
                    {
                        ["0"] = "White",
                        ["1"] = "Green",
                        ["2"] = "Yellow",
                        ["3"] = "Cyan",
                        ["4"] = "Magenta",
                        ["5"] = "Red",
                        ["6"] = "Orange",
                    }) { ShownWhen = new("r_crosshair") },
                new SettingRow("r_crosshair_size", "Crosshair Size", "How long the crosshair's ticks are.", Step: 10f) { ShownWhen = new("r_crosshair") },
                new SettingRow(
                    "r_crosshair_dynamic",
                    "Dynamic Crosshair",
                    "The crosshair opens as you move and fire, showing where rounds may land; off, it holds still.") { ShownWhen = new("r_crosshair") },
                new SettingRow("r_crosshair_thickness", "Crosshair Thickness", "How thick the crosshair's ticks are.", Step: 10f) { Advanced = true, ShownWhen = new("r_crosshair") },
                new SettingRow("r_crosshair_gap", "Crosshair Gap", "Extra space between the crosshair's centre and its ticks.", Step: 1f) { Advanced = true, ShownWhen = new("r_crosshair") },
                new SettingRow("r_crosshair_opacity", "Crosshair Opacity", "How solid the crosshair is.", Step: 5f) { Advanced = true, ShownWhen = new("r_crosshair") },
                new SettingRow("r_crosshair_dot", "Crosshair Dot", "A dot at the crosshair's centre.") { Advanced = true, ShownWhen = new("r_crosshair") },
                new SettingRow("r_crosshair_outline", "Crosshair Outline", "A faint dark edge round the crosshair, so it reads on bright ground.") { Advanced = true, ShownWhen = new("r_crosshair") },
            ]),

        new SettingsPanel(
            "Feedback",
            [],
            [
                new SettingRow(
                    "r_hit_markers",
                    "Hit Markers",
                    "A mark on the crosshair when a round hits, and a red cross when it kills; larger for a headshot."),
                new SettingRow(
                    "r_hit_sounds",
                    "Hit Sounds",
                    "A short tick when a round hits and a deeper one when it kills."),
                new SettingRow(
                    "r_ammo_cue",
                    "Ammo Warning",
                    "A word under the crosshair when your weapon runs low: LOW AMMO, then RELOAD when it is empty, NO AMMO when you have none left."),
                new SettingRow(
                    "hud_killfeed",
                    "Kill Feed",
                    "In multiplayer and co-op, the kills listed in the top right corner, with the weapon, and your own announced."),
                new SettingRow(
                    "hud_killfeed_singleplayer",
                    "Kill Feed in Single Player",
                    "The kill feed in single player too: each guard you kill listed in the top right corner, with the weapon and any headshot.") { ShownWhen = new("hud_killfeed") },
            ]),

        new SettingsPanel(
            "Markers & Messages",
            [],
            [
                new SettingRow(
                    "r_interaction_prompt",
                    "Interaction Markers",
                    "Marks a door, ladder or terminal you can use with a badge on the object itself, showing the "
                        + "key you have bound and, where the action takes time, a ring that fills as you hold it. "
                        + "Off, the same prompt is a line of text across the middle of the screen."),
                new SettingRow(
                    "r_world_objective",
                    "Projected Objective",
                    "Projects the objective you are working on onto its place in the level, so you read it where it is."),
                new SettingRow(
                    "r_messages_tactical",
                    "Mission Messages",
                    "Shows objective and radio messages as on-screen cards rather than as plain lines of text."),
                new SettingRow(
                    "r_computer_tactical",
                    "Tactical Computer",
                    "Draws the map computer in the remaster's style, with the level's objectives and your own "
                        + "position on it."),
                new SettingRow(
                    "r_scope_glint",
                    "Scope Glint",
                    "A sniper who has you in his sights shows the glint of his scope, a flash of light where he is."),
            ]),

        new SettingsPanel(
            "Optics",
            [],
            [
                new SettingRow(
                    "r_binocular_lens",
                    "Binocular Optics",
                    "Draws the binoculars with a lens: a round field of view, edge darkening and a little colour "
                        + "fringing towards the rim."),
                new SettingRow(
                    "r_binocular_outline",
                    "Highlight Spotted Enemies",
                    "Outlines soldiers seen through the binoculars, so a distant figure against a hillside can be "
                        + "picked out."),
                new SettingRow(
                    "r_thermal_remastered",
                    "Thermal Optics",
                    "Sees through the thermal goggles and the thermal scope with a modern sensor: the whole world as "
                        + "heat, people glowing and seen through thin cover, the dead cooling. Off is the original's view."),
                new SettingRow(
                    "r_thermal_palette",
                    "Thermal Palette",
                    "How the thermal sensor colours heat: White Hot, Black Hot, Ironbow or Rainbow. The palette key "
                        + "steps through them while the goggles are up.") { ShownWhen = new("r_thermal_remastered") },
                new SettingRow(
                    "r_binocular_silhouette",
                    "Silhouette Highlight",
                    "Draws that highlight as a soft outline over a faint fill, red for enemies, seen through walls "
                        + "and fading towards the edges of the view. Off, it is a coloured glow.") { Advanced = true, ShownWhen = new("r_binocular_outline") },
                new SettingRow(
                    "r_thermal_noise",
                    "Thermal Sensor Noise",
                    "How much grain and fixed-pattern noise the thermal sensor shows. Nought is a clean picture.") { Advanced = true, ShownWhen = new("r_thermal_remastered") },
            ]),

        new SettingsPanel(
            "Accessibility",
            [],
            [
                new SettingRow(
                    "r_colorblind",
                    "Colour Blind Mode",
                    "Draws the interface's paired colours -- alive and down, healthy and hurt, one team and the other -- in colours that "
                        + "stay apart for your kind of colour vision.",
                    new Dictionary<string, string>
                    {
                        ["0"] = "Off",
                        ["1"] = "Deuteranopia",
                        ["2"] = "Protanopia",
                        ["3"] = "Tritanopia",
                    }),
            ]),
    ];

    /// <summary>Gets every row on the page, in the order they are shown.</summary>
    public static IReadOnlyList<SettingRow> Rows { get; } = [.. Panels.SelectMany(panel => panel.Rows)];
}
