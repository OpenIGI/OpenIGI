using OpenIGI.Core.Graphics;

namespace OpenIGI.UI.Widgets;

/// <summary>
/// The design's colour tokens: one amber accent over dark glass, and a ramp of warm greys for text.
/// </summary>
/// <remarks>
/// <para>
/// Every literal colour in the interface comes from here, so the game's menus, its HUD, the developer
/// console and the launcher read as one product. <see cref="UiTheme"/> takes its defaults from these, and a
/// widget asks its theme for a role rather than naming a colour, so a screen that wants a different look
/// builds a different theme rather than editing a widget.
/// </para>
/// <para>
/// Translucent tokens are given as straight alpha over whatever is behind them — the live scene behind
/// the menu, the launcher's hero image — which is the whole point of the glass: it is never opaque.
/// </para>
/// </remarks>
public static class UiPalette
{
    /// <summary>The page behind everything: <c>#050607</c>.</summary>
    public static Bgra32 Backdrop => Bgra32.FromRgb(5, 6, 7);

    /// <summary>The base of a stage, and the dark text punched out of an accent fill: <c>#0A0C0D</c>.</summary>
    public static Bgra32 Ink => Bgra32.FromRgb(10, 12, 13);

    /// <summary>The accent: amber <c>#E8A33D</c>.</summary>
    public static Bgra32 Accent => Bgra32.FromRgb(232, 163, 61);

    /// <summary>A hovered accent fill: <c>#F4B656</c>.</summary>
    public static Bgra32 AccentHover => Bgra32.FromRgb(244, 182, 86);

    /// <summary>A hovered accent link or text: <c>#F4C173</c>.</summary>
    public static Bgra32 AccentLight => Bgra32.FromRgb(244, 193, 115);

    /// <summary>Selected and hovered text: white.</summary>
    public static Bgra32 TextBright => Bgra32.White;

    /// <summary>Primary text: <c>#E8E6E1</c>.</summary>
    public static Bgra32 Text => Bgra32.FromRgb(232, 230, 225);

    /// <summary>A settings row's caption: <c>#DDDBD6</c>.</summary>
    public static Bgra32 TextLabel => Bgra32.FromRgb(221, 219, 214);

    /// <summary>Prose: a briefing, a description: <c>#CFCDC8</c>.</summary>
    public static Bgra32 TextBody => Bgra32.FromRgb(207, 205, 200);

    /// <summary>An unselected name in a list: <c>#B5B4AF</c>.</summary>
    public static Bgra32 TextSecondary => Bgra32.FromRgb(181, 180, 175);

    /// <summary>An inactive segment or a hint's label: <c>#A9A8A3</c>.</summary>
    public static Bgra32 TextTertiary => Bgra32.FromRgb(169, 168, 163);

    /// <summary>A caption, an inactive tab, a chevron: <c>#8C8E90</c>.</summary>
    public static Bgra32 TextMuted => Bgra32.FromRgb(140, 142, 144);

    /// <summary>A dim mark: an arrow, an empty slot, the off side of a toggle: <c>#6E7073</c>.</summary>
    public static Bgra32 TextDim => Bgra32.FromRgb(110, 112, 115);

    /// <summary>Something that cannot be used: <c>#5A5C5F</c>.</summary>
    public static Bgra32 TextDisabled => Bgra32.FromRgb(90, 92, 95);

    /// <summary>Success, ready, a fix: <c>#7FD49A</c>.</summary>
    public static Bgra32 Success => Bgra32.FromRgb(127, 212, 154);

    /// <summary>Information, a plugin, a change: <c>#8FB8FF</c>.</summary>
    public static Bgra32 Info => Bgra32.FromRgb(143, 184, 255);

    /// <summary>A warning's message text: <c>#F4D29A</c>.</summary>
    public static Bgra32 Warning => Bgra32.FromRgb(244, 210, 154);

    /// <summary>An error: <c>#FF7A66</c>.</summary>
    public static Bgra32 Danger => Bgra32.FromRgb(255, 122, 102);

    /// <summary>A destructive action's caption: <c>#E08A78</c>.</summary>
    public static Bgra32 Destructive => Bgra32.FromRgb(224, 138, 120);

    /// <summary>A destructive action's border: <c>rgba(214,92,72,.5)</c>.</summary>
    public static Bgra32 DestructiveBorder => Bgra32.FromRgba(214, 92, 72, 128);

    /// <summary>A destructive action's hover wash: <c>rgba(214,92,72,.12)</c>.</summary>
    public static Bgra32 DestructiveWash => Bgra32.FromRgba(214, 92, 72, 31);

    /// <summary>A glass panel's tint: <c>rgba(10,12,13,.66)</c>.</summary>
    public static Bgra32 Glass => Bgra32.FromRgba(10, 12, 13, 168);

    /// <summary>A denser glass, for a panel holding a list: <c>rgba(10,12,13,.72)</c>.</summary>
    public static Bgra32 GlassDense => Bgra32.FromRgba(10, 12, 13, 184);

    /// <summary>A floating menu's glass: <c>rgba(10,12,13,.94)</c>.</summary>
    public static Bgra32 GlassMenu => Bgra32.FromRgba(10, 12, 13, 240);

    /// <summary>A panel's hairline border: white at .08.</summary>
    public static Bgra32 Hairline => White(0.08f);

    /// <summary>A header or footer rule: white at .07.</summary>
    public static Bgra32 Rule => White(0.07f);

    /// <summary>A row's hover wash: white at .04.</summary>
    public static Bgra32 HoverWash => White(0.04f);

    /// <summary>A slider's or progress bar's empty track: white at .12.</summary>
    public static Bgra32 Track => White(0.12f);

    /// <summary>A ghost button's border: white at .2.</summary>
    public static Bgra32 GhostBorder => White(0.2f);

    /// <summary>A key cap's border: white at .18.</summary>
    public static Bgra32 KeyBorder => White(0.18f);

    /// <summary>White at an opacity — the design's hairlines and washes are all this.</summary>
    /// <param name="alpha">The opacity, 0 to 1.</param>
    /// <returns>The colour.</returns>
    public static Bgra32 White(float alpha) => Bgra32.FromRgba(255, 255, 255, ToByte(alpha));

    /// <summary>The accent at an opacity — selections, tints and glows are all this.</summary>
    /// <param name="alpha">The opacity, 0 to 1.</param>
    /// <returns>The colour.</returns>
    public static Bgra32 AccentAt(float alpha) => Accent.WithAlpha(ToByte(alpha));

    /// <summary>The page ink at an opacity — the vignettes and scrims are all this.</summary>
    /// <param name="alpha">The opacity, 0 to 1.</param>
    /// <returns>The colour.</returns>
    public static Bgra32 InkAt(float alpha) => Bgra32.FromRgba(6, 8, 9, ToByte(alpha));

    /// <summary>Scales a colour's alpha.</summary>
    /// <param name="color">The colour.</param>
    /// <param name="factor">The factor, 0 to 1.</param>
    /// <returns>The faded colour.</returns>
    public static Bgra32 Fade(Bgra32 color, float factor) => color.WithAlpha((byte)(color.A * Math.Clamp(factor, 0f, 1f)));

    private static byte ToByte(float alpha) => (byte)MathF.Round(Math.Clamp(alpha, 0f, 1f) * 255f);
}
