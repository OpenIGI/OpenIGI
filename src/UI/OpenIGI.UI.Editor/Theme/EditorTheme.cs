using OpenIGI.Core.Graphics;
using OpenIGI.UI.Text;
using OpenIGI.UI.Widgets;

namespace OpenIGI.UI.Editor.Theme;

/// <summary>The typefaces the editor sets text in.</summary>
public enum EditorFontFace
{
    /// <summary>The interface face: labels, menus, trees, fields.</summary>
    Interface,

    /// <summary>The interface face at a heavier weight: panel titles, headings.</summary>
    InterfaceStrong,

    /// <summary>The monospaced face: code, numbers in a log, hex.</summary>
    Mono,
}

/// <summary>Supplies fonts to the editor, crisp at the size asked for.</summary>
/// <remarks>Implemented by the host, which owns the graphics device the glyph atlases live on.</remarks>
public interface IEditorFonts
{
    /// <summary>Gets a face at a pixel size.</summary>
    /// <param name="face">The face.</param>
    /// <param name="sizePx">The size in pixels, already scaled.</param>
    /// <returns>The font.</returns>
    UiFont Font(EditorFontFace face, float sizePx);

    /// <summary>Gets the icon face at a pixel size, or <see langword="null"/> when there is none and icons are drawn as shapes.</summary>
    /// <param name="sizePx">The size in pixels, already scaled.</param>
    /// <returns>The font, whose glyphs are icons.</returns>
    UiFont? IconFont(float sizePx) => null;

    /// <summary>Gets the character an icon is drawn with in <see cref="IconFont"/>, by its name.</summary>
    /// <param name="name">The icon's name, such as <c>folder</c>.</param>
    /// <returns>The character, or -1 for an unknown name.</returns>
    int IconCodepoint(string name) => -1;
}

/// <summary>
/// The editor's look: colour tokens, metrics and fonts, all at one interface scale.
/// </summary>
/// <remarks>
/// <para>
/// Every widget reads its sizes and colours from here and nowhere else, so the scale slider, the light theme
/// and any later restyle are one change. Metrics are given at scale 1 and multiplied by <see cref="Scale"/>;
/// read them through the properties, which round to whole pixels so hairlines stay sharp.
/// </para>
/// <para>
/// The palette is dark and quiet, with the game's amber as the one accent: selection, focus and the active
/// tool are the only coloured things on screen, so they are what the eye finds.
/// </para>
/// </remarks>
public sealed class EditorTheme
{
    /// <summary>Initializes a new instance of the <see cref="EditorTheme"/> class with the dark palette.</summary>
    /// <param name="fonts">The fonts.</param>
    /// <param name="surfaces">The rounded-corner and shadow sprites, or <see langword="null"/> to draw square and flat.</param>
    /// <param name="scale">The interface scale; 1 at 96 DPI.</param>
    /// <exception cref="ArgumentNullException"><paramref name="fonts"/> is <see langword="null"/>.</exception>
    public EditorTheme(IEditorFonts fonts, UiSurfaces? surfaces = null, float scale = 1f)
    {
        ArgumentNullException.ThrowIfNull(fonts);
        Fonts = fonts;
        Surfaces = surfaces;
        Scale = scale;
        Ui = new UiTheme(fonts.Font(EditorFontFace.Interface, 13f), fonts.Font(EditorFontFace.InterfaceStrong, 13f), fonts.Font(EditorFontFace.Mono, 13f));
        ApplyDark();
    }

    /// <summary>Gets the fonts.</summary>
    public IEditorFonts Fonts { get; }

    /// <summary>Gets the rounded-corner and shadow sprites, or <see langword="null"/> when shapes are drawn square.</summary>
    public UiSurfaces? Surfaces { get; }

    /// <summary>Gets or sets the interface scale.</summary>
    public float Scale
    {
        get;
        set => field = Math.Clamp(value, 0.5f, 4f);
    }

    /// <summary>Gets the shared theme the base library's widgets (scroll views, lists) read.</summary>
    public UiTheme Ui { get; }

    /// <summary>Gets whether the light palette is applied.</summary>
    public bool IsLight { get; private set; }

    // ---- Metrics ----

    /// <summary>Gets the body text size.</summary>
    public float TextSize => Px(13f);

    /// <summary>Gets the small text size: captions, status, badges.</summary>
    public float SmallTextSize => Px(11f);

    /// <summary>Gets the height of a list, tree, menu or property row.</summary>
    public float RowHeight => Px(24f);

    /// <summary>Gets the height of a text field, dropdown or button.</summary>
    public float ControlHeight => Px(24f);

    /// <summary>Gets the height of a panel's tab strip.</summary>
    public float TabHeight => Px(30f);

    /// <summary>Gets the height of the menu bar.</summary>
    public float MenuBarHeight => Px(26f);

    /// <summary>Gets the height of a toolbar.</summary>
    public float ToolbarHeight => Px(32f);

    /// <summary>Gets the height of the status bar.</summary>
    public float StatusBarHeight => Px(22f);

    /// <summary>Gets the base spacing unit.</summary>
    public float Unit => Px(4f);

    /// <summary>Gets the inner padding of panels and fields.</summary>
    public float Padding => Px(8f);

    /// <summary>Gets the indentation of one tree level.</summary>
    public float Indent => Px(16f);

    /// <summary>Gets the size of an icon.</summary>
    public float IconSize => Px(16f);

    /// <summary>Gets the corner radius of fields, buttons and popups.</summary>
    public float Radius => Px(4f);

    /// <summary>Gets the thickness of the gap between docked panels, which is also the splitter's grab area.</summary>
    public float SplitterSize => Px(4f);

    /// <summary>Gets the width of a hairline: borders and separators.</summary>
    public float Hairline => MathF.Max(1f, MathF.Floor(Scale));

    // ---- Colour tokens ----

    /// <summary>Gets the colour behind everything: the gaps between panels.</summary>
    public Bgra32 Background { get; private set; }

    /// <summary>Gets a panel's body colour.</summary>
    public Bgra32 Panel { get; private set; }

    /// <summary>Gets the colour of panel headers, tab strips, the menu bar and toolbars.</summary>
    public Bgra32 Chrome { get; private set; }

    /// <summary>Gets the colour of popups: menus, dropdown lists, tooltips.</summary>
    public Bgra32 Popup { get; private set; }

    /// <summary>Gets the colour inside text fields and other inputs.</summary>
    public Bgra32 Input { get; private set; }

    /// <summary>Gets the colour of buttons at rest.</summary>
    public Bgra32 Button { get; private set; }

    /// <summary>Gets the wash laid over a hovered row or control.</summary>
    public Bgra32 Hover { get; private set; }

    /// <summary>Gets the wash laid over a pressed control.</summary>
    public Bgra32 Pressed { get; private set; }

    /// <summary>Gets the fill of selected rows when their view has focus.</summary>
    public Bgra32 Selection { get; private set; }

    /// <summary>Gets the fill of selected rows when their view does not have focus.</summary>
    public Bgra32 SelectionInactive { get; private set; }

    /// <summary>Gets the colour of borders and separators.</summary>
    public Bgra32 Border { get; private set; }

    /// <summary>Gets the colour of a border that must read clearly: popups, focused fields.</summary>
    public Bgra32 BorderStrong { get; private set; }

    /// <summary>Gets the accent: focus, the active tab, the active tool, links.</summary>
    public Bgra32 Accent { get; private set; }

    /// <summary>Gets text drawn on the accent colour.</summary>
    public Bgra32 OnAccent { get; private set; }

    /// <summary>Gets the main text colour.</summary>
    public Bgra32 Text { get; private set; }

    /// <summary>Gets the colour of secondary text: labels in a property grid, hints.</summary>
    public Bgra32 TextMuted { get; private set; }

    /// <summary>Gets the colour of disabled text and placeholder text.</summary>
    public Bgra32 TextDisabled { get; private set; }

    /// <summary>Gets the colour of icons at rest.</summary>
    public Bgra32 Icon { get; private set; }

    /// <summary>Gets the colour for information.</summary>
    public Bgra32 Info { get; private set; }

    /// <summary>Gets the colour for success.</summary>
    public Bgra32 Success { get; private set; }

    /// <summary>Gets the colour for warnings.</summary>
    public Bgra32 Warning { get; private set; }

    /// <summary>Gets the colour for errors.</summary>
    public Bgra32 Error { get; private set; }

    /// <summary>Gets the colour of the X axis in gizmos and vector fields.</summary>
    public Bgra32 AxisX { get; private set; }

    /// <summary>Gets the colour of the Y axis.</summary>
    public Bgra32 AxisY { get; private set; }

    /// <summary>Gets the colour of the Z axis.</summary>
    public Bgra32 AxisZ { get; private set; }

    /// <summary>Gets the shadow under popups.</summary>
    public Bgra32 Shadow { get; private set; }

    /// <summary>Gets the colour of keywords in code.</summary>
    public Bgra32 SyntaxKeyword { get; private set; }

    /// <summary>Gets the colour of function names in code.</summary>
    public Bgra32 SyntaxFunction { get; private set; }

    /// <summary>Gets the colour of string literals in code.</summary>
    public Bgra32 SyntaxString { get; private set; }

    /// <summary>Gets the colour of numbers in code.</summary>
    public Bgra32 SyntaxNumber { get; private set; }

    /// <summary>Gets the colour of comments in code.</summary>
    public Bgra32 SyntaxComment { get; private set; }

    /// <summary>Gets the colour of named constants in code (<c>TRUE</c>, <c>AIEVENT_IDLE</c>).</summary>
    public Bgra32 SyntaxConstant { get; private set; }

    /// <summary>Gets the interface font at the body size.</summary>
    public UiFont Font => Fonts.Font(EditorFontFace.Interface, TextSize);

    /// <summary>Gets the strong interface font at the body size.</summary>
    public UiFont StrongFont => Fonts.Font(EditorFontFace.InterfaceStrong, TextSize);

    /// <summary>Gets the interface font at the small size.</summary>
    public UiFont SmallFont => Fonts.Font(EditorFontFace.Interface, SmallTextSize);

    /// <summary>Gets the monospaced font at the body size.</summary>
    public UiFont MonoFont => Fonts.Font(EditorFontFace.Mono, TextSize);

    /// <summary>Scales a scale-1 measurement to the current scale, rounded to a whole pixel.</summary>
    /// <param name="value">The measurement at scale 1.</param>
    /// <returns>The measurement now.</returns>
    public float Px(float value) => MathF.Round(value * Scale);

    /// <summary>Gets the colour of an axis by index: 0 X, 1 Y, 2 Z.</summary>
    /// <param name="axis">The axis.</param>
    /// <returns>The colour.</returns>
    public Bgra32 AxisColor(int axis) => axis switch
    {
        0 => AxisX,
        1 => AxisY,
        _ => AxisZ,
    };

    /// <summary>Applies the dark palette, the default.</summary>
    public void ApplyDark()
    {
        IsLight = false;
        // s&box's editor palette (its theme.json: window #181818, widgets #2a2a2a, surfaces #343434, controls #181818,
        // borders #3e3e3e / #484848, secondary text #9e9e9e), with OpenIGI's own orange as the accent.
        Background = Rgb(0x181818);
        Panel = Rgb(0x2A2A2A);
        Chrome = Rgb(0x202020);
        Popup = Rgb(0x343434);
        Input = Rgb(0x1C1C1C);
        Button = Rgb(0x383838);
        Hover = UiPalette.White(0.06f);
        Pressed = UiPalette.White(0.10f);
        Selection = UiPalette.AccentAt(0.28f);
        SelectionInactive = Rgb(0x3E3E3E);
        Border = Rgb(0x3E3E3E);
        BorderStrong = Rgb(0x484848);
        Accent = UiPalette.Accent;
        OnAccent = UiPalette.Ink;
        Text = Rgb(0xFFFFFF);
        TextMuted = Rgb(0x9E9E9E);
        TextDisabled = Rgb(0x666666);
        Icon = Rgb(0xC8C8C8);
        ApplyShared();
    }

    /// <summary>Applies the light palette.</summary>
    public void ApplyLight()
    {
        IsLight = true;
        Background = Rgb(0xBDBDBD);
        Panel = Rgb(0xEDEDED);
        Chrome = Rgb(0xE0E0E0);
        Popup = Rgb(0xFFFFFF);
        Input = Rgb(0xFFFFFF);
        Button = Rgb(0xD6D6D6);
        Hover = Bgra32.FromRgba(0, 0, 0, 14);
        Pressed = Bgra32.FromRgba(0, 0, 0, 26);
        Selection = UiPalette.AccentAt(0.35f);
        SelectionInactive = Bgra32.FromRgba(0, 0, 0, 22);
        Border = Rgb(0xC2C2C2);
        BorderStrong = Rgb(0xA0A0A0);
        Accent = Bgra32.FromRgb(196, 124, 20);
        OnAccent = Bgra32.White;
        Text = Rgb(0x1E1E1E);
        TextMuted = Rgb(0x5E5E5E);
        TextDisabled = Rgb(0x9E9E9E);
        Icon = Rgb(0x4A4A4A);
        ApplyShared();
    }

    private void ApplyShared()
    {
        Info = UiPalette.Info;
        Success = UiPalette.Success;
        Warning = UiPalette.Warning;
        Error = UiPalette.Danger;
        AxisX = Bgra32.FromRgb(232, 84, 84);
        AxisY = Bgra32.FromRgb(118, 196, 72);
        AxisZ = Bgra32.FromRgb(70, 140, 240);
        Shadow = Bgra32.FromRgba(0, 0, 0, IsLight ? (byte)60 : (byte)140);
        SyntaxKeyword = IsLight ? Bgra32.FromRgb(160, 40, 140) : Bgra32.FromRgb(210, 150, 255);
        SyntaxFunction = IsLight ? Bgra32.FromRgb(30, 90, 170) : Bgra32.FromRgb(125, 185, 255);
        SyntaxString = IsLight ? Bgra32.FromRgb(30, 120, 60) : Bgra32.FromRgb(150, 215, 140);
        SyntaxNumber = IsLight ? Bgra32.FromRgb(170, 90, 10) : Bgra32.FromRgb(245, 190, 110);
        SyntaxComment = IsLight ? Bgra32.FromRgb(120, 128, 138) : Bgra32.FromRgb(110, 118, 128);
        SyntaxConstant = IsLight ? Bgra32.FromRgb(20, 130, 140) : Bgra32.FromRgb(110, 210, 200);

        // The base library's widgets share these, so a scroll view inside a panel matches the panel.
        Ui.Text = Text;
        Ui.TextMuted = TextMuted;
        Ui.TextDisabled = TextDisabled;
        Ui.Accent = Accent;
        Ui.Border = Border;
        Ui.Track = Border;
        Ui.Selection = Selection;
        Ui.SurfaceRaised = Popup;
    }

    private static Bgra32 Rgb(uint rgb) => Bgra32.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
}
