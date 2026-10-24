using OpenIGI.Core.Configuration;
using OpenIGI.Core.Configuration.Presets;

namespace OpenIGI.Rendering;

/// <summary>The renderer's console variables for the interface laid over play: the HUD, prompts, the map computer, binoculars and messages.</summary>
/// <remarks>One of the renderer's variable containers; <see cref="FrameCVars"/> says how they are reached.</remarks>
[CVarContainer]
public static class HudCVars
{
    /// <summary>Whether the mission's briefing is posted on the nearest wall or fence at the player's start.</summary>
    /// <remarks>
    /// The mission caption proper is laid flat at the start and seen from the shot's altitude; this is
    /// the briefing itself, stood a few centimetres off the nearest vertical surface the start has -- a
    /// fence, a wall, a crate -- in the level's own space, so it is read the way a notice is read: by
    /// walking up to it. With nothing to post it on it stands in the air a few metres ahead.
    /// </remarks>
    public static readonly BoolCVar WorldBriefing = CVar.Bool(
        "r_world_briefing",
        true,
        CVarOptions.Archive,
        "Posts the mission briefing on the nearest wall or fence at the player's start.");

    /// <summary>Whether gameplay uses the remaster HUD instead of IGI1's retail StatusScreen.</summary>
    /// <remarks>
    /// Both presentations stay loaded so this can switch live from the console or settings screen.
    /// Vanilla selects the recovered retail path; the other presets retain the remaster HUD by default.
    /// </remarks>
    public static readonly BoolCVar RemasteredHud = Presets.Define(
        CVar.Bool(
            "r_remastered_hud",
            true,
            CVarOptions.Archive,
            "Draw the remaster HUD. Off uses IGI1's original health, weapon, action and timer display."),
        vanilla: "0",
        enhanced: "1",
        high: "1",
        ultra: "1");

    /// <summary>Whether the remastered HUD is laid over the picture as if projected on a visor.</summary>
    /// <remarks>
    /// The display bends a little with the visor, splits its colours at the edges, carries scanlines, flickers in, glows,
    /// and shakes and reddens at its corners when the player is hit (<see cref="HudFxSettings"/>). Only while
    /// <see cref="RemasteredHud"/> is on; off draws the remastered HUD flat.
    /// </remarks>
    public static readonly BoolCVar HudFx = Presets.Define(
        CVar.Bool(
            "r_hud_fx",
            true,
            CVarOptions.Archive,
            "With the remaster HUD, project it as on a visor: curvature, fringe, scanlines, glow and hit effects."),
        vanilla: "0",
        enhanced: "1",
        high: "1",
        ultra: "1");

    /// <summary>How strongly the remastered HUD's visor effects are applied, nought to one.</summary>
    public static readonly NumericCVar<float> HudFxIntensity = Presets.Define(
        CVar.Float(
            "r_hud_fx_intensity",
            1f,
            0f,
            1f,
            CVarOptions.Archive,
            "Strength of the remaster HUD's visor effects, 0 to 1."),
        vanilla: "0",
        enhanced: "1",
        high: "1",
        ultra: "1");

    /// <summary>How much of the remastered HUD stays on screen.</summary>
    /// <remarks>
    /// Full keeps everything up. Contextual dims the weapon and the inventory when nothing about them changes and brings them
    /// back on a reload, a switch, low rounds, a hit or an alarm. Minimal shows only what is urgent.
    /// </remarks>
    public static readonly EnumCVar<HudDensity> HudDensity = Presets.Define(
        CVar.Choice(
            "r_hud_density",
            global::OpenIGI.Rendering.HudDensity.Contextual,
            CVarOptions.Archive,
            "How much of the remaster HUD stays up: full, contextual or minimal."),
        vanilla: "full",
        enhanced: "contextual",
        high: "contextual",
        ultra: "contextual");

    /// <summary>Whether the remastered HUD trails the view a little as it turns, as a visor's display would.</summary>
    public static readonly BoolCVar HudSway = Presets.Define(
        CVar.Bool(
            "r_hud_sway",
            true,
            CVarOptions.Archive,
            "With the remaster HUD, let it trail the view a few pixels as it turns, then settle."),
        vanilla: "0",
        enhanced: "1",
        high: "1",
        ultra: "1");

    /// <summary>Whether the remastered HUD marks each soldier who has the player in sight with an eye that fills as he makes the player out.</summary>
    public static readonly BoolCVar HudThreat = Presets.Define(
        CVar.Bool(
            "r_hud_threat",
            true,
            CVarOptions.Archive,
            "With the remaster HUD, mark each soldier who can see you with an eye that fills as he makes you out."),
        vanilla: "0",
        enhanced: "1",
        high: "1",
        ultra: "1");

    /// <summary>Whether the objective being worked on is projected onto its place in the level.</summary>
    public static readonly BoolCVar WorldObjective = Presets.Define(
        CVar.Bool(
            "r_world_objective",
            true,
            CVarOptions.Archive,
            "With the remaster HUD, project the current objective onto its place in the level."),
        vanilla: "0",
        enhanced: "1",
        high: "1",
        ultra: "1");

    /// <summary>Whether a usable object is marked in the world rather than captioned across the screen.</summary>
    /// <remarks>
    /// <para>
    /// The modern presentation of the same offer: a key badge pinned to the door, cabinet or ladder the
    /// activation scan resolved, carrying the control the player actually has bound, the action's name,
    /// and — for the timed states — the hold as a ring that fills. Off returns the remaster HUD to its
    /// single centred line of text.
    /// </para>
    /// <para>
    /// Presentation only, and only while <see cref="RemasteredHud"/> is on. What can be used, from where,
    /// and for how long is decided by the retail activation scan either way; this changes where the
    /// player reads it. Off under vanilla with the rest of the remaster, which keeps the original's
    /// action sprites.
    /// </para>
    /// </remarks>
    public static readonly BoolCVar InteractionPrompt = Presets.Define(
        CVar.Bool(
            "r_interaction_prompt",
            true,
            CVarOptions.Archive,
            "With the remaster HUD, mark usable objects in the world. Off is the centred text prompt."),
        vanilla: "0",
        enhanced: "1",
        high: "1",
        ultra: "1");

    /// <summary>Whether the C-key map computer is the remaster's interface rather than the retail one.</summary>
    /// <remarks>
    /// <para>
    /// Two entirely separate presentations of the same data. Off is the original's: a low-resolution
    /// <c>mapcomputer.mef</c> panel held up in front of the camera, its aperture filled by a 4:3 screen
    /// set in the game's own bitmap faces. On is a full-screen interface set in the shell's Barlow,
    /// reached through a camera move and a phosphor display transform, with no device model at all.
    /// </para>
    /// <para>
    /// Off under vanilla for the reason everything else is: <c>-vanilla</c> and <c>-vanillarender</c>
    /// exist to put the original's picture on screen, and a redesigned computer is not that. The retail
    /// path is kept whole rather than emulated by the new one — it is the regression gate, and a gate
    /// that shares an implementation with the thing it is gating is not a gate.
    /// </para>
    /// </remarks>
    public static readonly BoolCVar TacticalComputer = Presets.Define(
        CVar.Bool(
            "r_computer_tactical",
            true,
            CVarOptions.Archive,
            "Draw the map computer as the full-screen tactical interface. Off is the retail handheld panel."),
        vanilla: "0",
        enhanced: "1",
        high: "1",
        ultra: "1");

    /// <summary>Whether the remastered binocular applies its glass-lens display transform.</summary>
    /// <remarks>
    /// The optics tint, highlight bloom and bezel vignette are a resolve-time display transform over the
    /// finished picture, raised as the device comes up. Off under vanilla for the reason the tactical
    /// computer is: the retail binocular is the regression gate and must stay untouched.
    /// </remarks>
    public static readonly BoolCVar BinocularLens = Presets.Define(
        CVar.Bool(
            "r_binocular_lens",
            true,
            CVarOptions.Archive,
            "Apply the remastered binocular's glass-lens transform (tint, bloom, bezel vignette)."),
        vanilla: "0",
        enhanced: "1",
        high: "1",
        ultra: "1");

    /// <summary>Whether binocular-detected enemies are given a through-wall silhouette glow.</summary>
    /// <remarks>
    /// A team-coloured selection glow drawn over each scanned soldier while the remastered optics are up,
    /// so a marked contact stays legible through cover. Off under vanilla with the rest of the remaster.
    /// </remarks>
    public static readonly BoolCVar BinocularOutline = Presets.Define(
        CVar.Bool(
            "r_binocular_outline",
            true,
            CVarOptions.Archive,
            "Glow binocular-detected enemies through walls while the remastered optics are up."),
        vanilla: "0",
        enhanced: "1",
        high: "1",
        ultra: "1");

    /// <summary>The remastered binocular detector's range at the default zoom, in metres.</summary>
    /// <remarks>
    /// A modern optics touch: the detector has a finite range rather than tagging every enemy in the
    /// frustum however far off, and that range grows as the operator zooms in -- from this value at the
    /// default field of view up to <see cref="BinocularRangeZoomed"/> at the tightest zoom. The vanilla
    /// optics ignore both and stay retail-uncapped.
    /// </remarks>
    public static readonly NumericCVar<float> BinocularRange = Presets.Define(
        CVar.Float(
            "r_binocular_range",
            150f,
            50f,
            4000f,
            CVarOptions.Archive,
            "Range, in metres, the remastered binocular detector marks a contact at, at the default zoom."),
        vanilla: "150",
        enhanced: "150",
        high: "150",
        ultra: "150");

    /// <summary>The remastered binocular detector's range at the tightest zoom, in metres.</summary>
    /// <remarks>
    /// The range scales from <see cref="BinocularRange"/> at the default field of view to this at the
    /// closest magnification, so leaning into the optics sees further, the way a real scope resolves a
    /// distant figure the naked view cannot.
    /// </remarks>
    public static readonly NumericCVar<float> BinocularRangeZoomed = Presets.Define(
        CVar.Float(
            "r_binocular_range_zoomed",
            250f,
            50f,
            4000f,
            CVarOptions.Archive,
            "Range, in metres, the remastered binocular detector marks a contact at, at the tightest zoom."),
        vanilla: "250",
        enhanced: "250",
        high: "250",
        ultra: "250");

    /// <summary>Whether the mission's status messages are drawn as the remaster's comms cards.</summary>
    /// <remarks>
    /// <para>
    /// Presentation only. Both paths draw the same messages at the same moments, in the same order, with
    /// the same typewriter: when a message arrives is decided by the simulation, and neither renderer can
    /// reach that. Off is the original's placement and bitmap face; on is a card set in the shell's Barlow.
    /// </para>
    /// <para>
    /// This preference only applies while <see cref="RemasteredHud"/> is on. The retail HUD always uses
    /// its own bitmap message block and original display timing.
    /// </para>
    /// </remarks>
    public static readonly BoolCVar TacticalMessages = Presets.Define(
        CVar.Bool(
            "r_messages_tactical",
            true,
            CVarOptions.Archive,
            "With the remaster HUD, draw mission messages as tactical cards. Off is the retail message block."),
        vanilla: "0",
        enhanced: "1",
        high: "1",
        ultra: "1");

    /// <summary>How strongly the screen washes red when the player is hit.</summary>
    /// <remarks>
    /// <para>
    /// A multiplier on the alpha of the full-screen quad <c>HumanPlayer_DrawDamageFlash</c>
    /// (<c>0x4bb770</c>) paints, where 1 is the original's own strength and 0 removes it. The decay is
    /// untouched either way: this changes how red the wash is, not how long it lasts, so the feedback
    /// still tells the player which direction the hit came from and for exactly as long.
    /// </para>
    /// <para>
    /// Below 1 by default outside vanilla. The original's full-strength wash was tuned for a 640x480
    /// display a good distance from the player's face; the same alpha across a modern screen is enough
    /// to hide the thing that is shooting at you, which turns a damage cue into a punishment.
    /// </para>
    /// </remarks>
    public static readonly NumericCVar<float> DamageFlash = Presets.Define(
        CVar.Float(
            "r_damage_flash",
            0.55f,
            0f,
            1f,
            CVarOptions.Archive,
            "How red the screen goes when you are hit. 1 is the original's strength, 0 removes it."),
        vanilla: "1",
        enhanced: "0.55",
        high: "0.55",
        ultra: "0.55");

    /// <summary>The registry the renderer's own settings describe themselves to.</summary>
    private static GraphicsPresetRegistry Presets => GraphicsPresetRegistry.Default;
}
