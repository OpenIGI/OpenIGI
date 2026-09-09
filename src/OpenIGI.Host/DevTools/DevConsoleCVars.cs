using OpenIGI.Core.Configuration;

namespace OpenIGI.Host.DevTools;

/// <summary>How the developer tools are coloured.</summary>
public enum ConsolePalette
{
    /// <summary>Near-black panels with an amber accent, matching the shell.</summary>
    Dark,

    /// <summary>A lighter, less contrasty variant for bright rooms.</summary>
    Dim,

    /// <summary>Amber on black, in the lineage of the original's own menus.</summary>
    Amber,
}

/// <summary>
/// The developer console's own console variables.
/// </summary>
/// <remarks>
/// <para>
/// Declared in the desktop application rather than beside the renderer's, because they configure a
/// tool that only the desktop shell has. The application already references the CVar source generator
/// as an analyzer, so the module initialiser that forces these declarations to register is emitted
/// here as it is for every other container — but <see cref="HostConsole.Start"/> must still call it
/// explicitly before the settings file is read, or a saved <c>con_</c> line would be met with
/// "unknown variable" and the reader's layout would silently never come back.
/// </para>
/// <para>
/// Everything here is <see cref="CVarOptions.Archive"/>, because every one of them is a preference a
/// reader sets once and expects to find again. None is a cheat and none affects the simulation.
/// </para>
/// </remarks>
[CVarContainer]
public static class DevConsoleCVars
{
    /// <summary>How large the developer tools draw, as a multiple of real pixels.</summary>
    /// <remarks>
    /// <para>
    /// The console sizes itself in <em>back-buffer pixels</em> rather than the shell's 16:9 design
    /// units, so a wider display shows more console rather than a larger one — which is what a tool
    /// wants and what a menu does not.
    /// </para>
    /// <para>
    /// Resolution is a different question from aspect ratio, though, and raw pixels get that one
    /// wrong: the same panel on a 4K monitor would be half the physical size it is on a 1080p one of
    /// the same width. So a factor derived from the display height is applied underneath, and this is
    /// the reader's multiplier on top of it.
    /// </para>
    /// </remarks>
    public static readonly NumericCVar<float> Scale = CVar.Float(
        "con_scale",
        1f,
        0.5f,
        3f,
        CVarOptions.Archive,
        "How large the developer console draws, as a multiple of its normal pixel size.");

    /// <summary>The log body's text size in pixels, before <see cref="Scale"/>.</summary>
    public static readonly NumericCVar<float> FontSize = CVar.Float(
        "con_font_size",
        17f,
        9f,
        40f,
        CVarOptions.Archive,
        "The developer console's text size in pixels, before con_scale.");

    /// <summary>How opaque the console's panels are over the scene behind them.</summary>
    public static readonly NumericCVar<float> Opacity = CVar.Float(
        "con_opacity",
        0.94f,
        0.3f,
        1f,
        CVarOptions.Archive,
        "How opaque the developer console's panels are, 0.3 transparent to 1 solid.");

    /// <summary>Where each panel sits, as one serialised string.</summary>
    /// <remarks>
    /// One opaque variable rather than five numbers per panel. The settings file records every
    /// archived variable that differs from its default, and thirty geometry numbers would bury the
    /// registry that <c>find</c> and <c>cvarlist</c> exist to make searchable.
    /// </remarks>
    public static readonly StringCVar Layout = CVar.Text(
        "con_layout",
        string.Empty,
        CVarOptions.Archive,
        "Where the developer console's panels sit. Written by the console; edit with care.");

    /// <summary>The variables pinned to the watch panel.</summary>
    public static readonly StringCVar Watch = CVar.Text(
        "con_watch",
        string.Empty,
        CVarOptions.Archive,
        "Space-separated names pinned to the developer console's watch panel.");

    /// <summary>Whether the log shows a timestamp column.</summary>
    public static readonly BoolCVar Timestamps = CVar.Bool(
        "con_timestamps",
        false,
        CVarOptions.Archive,
        "Show the time each console line was written.");

    /// <summary>Whether the log shows the subsystem that wrote each line.</summary>
    public static readonly BoolCVar Categories = CVar.Bool(
        "con_categories",
        true,
        CVarOptions.Archive,
        "Show which subsystem wrote each console line.");

    /// <summary>How near a screen edge a dragged panel snaps flush to it.</summary>
    public static readonly NumericCVar<float> Snap = CVar.Float(
        "con_snap",
        12f,
        0f,
        48f,
        CVarOptions.Archive,
        "How near a screen edge a console panel snaps to it, in pixels. Zero disables snapping.");

    /// <summary>How many completion candidates the dropdown shows at once.</summary>
    public static readonly NumericCVar<int> MaxSuggestions = CVar.Int(
        "con_max_suggestions",
        12,
        1,
        30,
        CVarOptions.Archive,
        "How many completions the developer console offers at once.");

    /// <summary>Which palette the developer tools draw with.</summary>
    public static readonly EnumCVar<ConsolePalette> Palette = CVar.Choice(
        "con_theme",
        ConsolePalette.Dark,
        CVarOptions.Archive,
        "The developer console's palette: dark, dim or amber.");

    /// <summary>Whether the watch panel stays on screen with the console closed.</summary>
    /// <remarks>
    /// Off by default, and deliberately so. Anything drawn while the console is shut is drawn during a
    /// reference capture too, and the remaster's standing gate is that <c>gfx_preset vanilla</c> stays
    /// pixel-identical. A reader who turns this on has chosen to see it; a capture never has.
    /// </remarks>
    public static readonly BoolCVar WatchVisible = CVar.Bool(
        "con_watch_visible",
        false,
        CVarOptions.Archive,
        "Keep the watch panel on screen while the console is closed.");
}
