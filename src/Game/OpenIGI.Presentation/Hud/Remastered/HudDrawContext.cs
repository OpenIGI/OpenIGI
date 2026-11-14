using OpenIGI.Core.Graphics;
using OpenIGI.Rendering;
using OpenIGI.UI;
using OpenIGI.UI.Layout;
using OpenIGI.UI.Text;

namespace OpenIGI.Presentation.Hud.Remastered;

/// <summary>The faces the remastered display draws its text in.</summary>
/// <param name="Label">Small labels: tracked, upper case, monospaced.</param>
/// <param name="Value">Figures: the round count, health, the clock.</param>
/// <param name="Name">Names: the weapon's, an action's.</param>
/// <param name="Body">Running text: messages, the objective.</param>
public sealed record HudFonts(UiFont Label, UiFont Value, UiFont Name, UiFont Body);

/// <summary>Everything a widget of the remastered display draws with, for one frame.</summary>
public sealed class HudDrawContext
{
    internal HudDrawContext(
        SpriteBatch batch,
        UiPainter painter,
        HudTheme theme,
        HudFonts fonts,
        IHudIconSource icons,
        HudReactiveState reactive,
        float width,
        float height,
        float scale)
    {
        Batch = batch;
        Painter = painter;
        Theme = theme;
        Fonts = fonts;
        Icons = icons;
        Reactive = reactive;
        Width = width;
        Height = height;
        Scale = scale;
    }

    /// <summary>Gets the batch the title's pictures are drawn into.</summary>
    public SpriteBatch Batch { get; }

    /// <summary>Gets the painter the shapes and text are drawn with.</summary>
    public UiPainter Painter { get; }

    /// <summary>Gets the title's look.</summary>
    public HudTheme Theme { get; }

    /// <summary>Gets the faces.</summary>
    public HudFonts Fonts { get; }

    /// <summary>Gets the title's pictures.</summary>
    public IHudIconSource Icons { get; }

    /// <summary>Gets what changed and how far its animations have run.</summary>
    public HudReactiveState Reactive { get; }

    /// <summary>Gets the screen's width in pixels.</summary>
    public float Width { get; }

    /// <summary>Gets the screen's height in pixels.</summary>
    public float Height { get; }

    /// <summary>Gets how many pixels one of the title's authored pixels is drawn as: the scale its anchors are placed by.</summary>
    public float Scale { get; }

    /// <summary>
    /// Gets how many pixels a design pixel is drawn as: the widgets are drawn in one size for both titles, a 720-line display's
    /// pixels, carried to the title's authored display.
    /// </summary>
    /// <remarks>Times the size the player's layout gives the readout being drawn (<see cref="BeginElement"/>).</remarks>
    public float DesignScale => Scale * Theme.AuthoredHeight / HudTheme.DesignHeight * _elementScale;

    private float _elementScale = 1f;
    private float _elementOpacity = 1f;
    private bool _inElement;

    /// <summary>Gets the player's own arrangement of the readouts, or <see langword="null"/> for the theme's.</summary>
    public HudLayoutOverrides? Layout { get; init; }

    /// <summary>Starts a readout: what is drawn until <see cref="EndElement"/> is at the size and opacity the player's layout gives it.</summary>
    /// <param name="element">The readout.</param>
    public void BeginElement(HudElement element)
    {
        if (Layout is not { } arranged || _inElement)
        {
            return;
        }

        Touch.HudPlacement placement = arranged.Layout.Resolve(element);
        _elementScale = placement.Scale;
        _elementOpacity = placement.Visible ? placement.Opacity : HiddenOpacity;
        _inElement = true;
        Painter.PushOpacity(_elementOpacity);
    }

    /// <summary>Ends the readout <see cref="BeginElement"/> started.</summary>
    public void EndElement()
    {
        if (!_inElement)
        {
            return;
        }

        Painter.PopOpacity();
        _elementScale = 1f;
        _elementOpacity = 1f;
        _inElement = false;
    }

    /// <summary>Tells the layout's editor where a readout came to stand.</summary>
    /// <param name="element">The readout.</param>
    /// <param name="box">Its box on the screen, in pixels.</param>
    public void Report(HudElement element, UiRect box)
    {
        if (Layout is { } arranged)
        {
            arranged.Boxes[element] = box;
        }
    }

    // How much of a readout the player left out the editor still shows.
    private const float HiddenOpacity = 0.28f;

    /// <summary>Gets or sets how much of the part being drawn shows, at the display's density; the painter's own opacity carries it to shapes and text.</summary>
    public float PartOpacity { get; internal set; } = 1f;

    /// <summary>Draws one of the title's pictures, at the part's opacity.</summary>
    /// <param name="icon">The picture.</param>
    /// <param name="x">Its left edge, in pixels.</param>
    /// <param name="y">Its top edge, in pixels.</param>
    /// <param name="scale">How many pixels each of its own is drawn as.</param>
    /// <param name="colour">Its colour.</param>
    /// <remarks>A picture the title hands over whole is drawn from its smooth copy (<see cref="Smooth"/>) where there is one.</remarks>
    public void DrawIcon(HudIcon icon, float x, float y, float scale, Bgra32 colour)
    {
        if (Smooth is { } smooth && Icons.Resolve(icon) is { Asset.Frames.Count: > 0 } source && smooth.Get(source) is { } copy)
        {
            copy.DrawFrame(Batch, copy.Asset.Frames[0], x, y, scale, scale, Fade(colour, PartOpacity * Behind * _elementOpacity));
            return;
        }

        Icons.Draw(Batch, icon, x, y, scale, Fade(colour, PartOpacity * Behind * _elementOpacity));
    }

    /// <summary>Gets or sets how much of the display under the weapon wheel shows, one with the wheel shut.</summary>
    public float Behind { get; set; } = 1f;

    /// <summary>Gets a value indicating whether the visor pass draws the world behind the weapon wheel, so the wheel draws none.</summary>
    public bool ShaderBackdrop { get; init; }

    /// <summary>Gets the smooth copies of the title's pictures, or <see langword="null"/> to draw the title's own.</summary>
    public HudSmoothSprites? Smooth { get; init; }

    /// <summary>Gets the palette.</summary>
    public HudPalette Palette => Theme.Palette;

    /// <summary>Gets the seconds now.</summary>
    public float Seconds => Reactive.Seconds;

    /// <summary>A length in design pixels, on the screen.</summary>
    /// <param name="design">The length.</param>
    /// <returns>It in pixels.</returns>
    public float Px(float design) => design * DesignScale;

    /// <summary>Where a part stands, or <see langword="null"/> where the theme does not place it.</summary>
    /// <param name="element">The part.</param>
    /// <returns>The point, in pixels.</returns>
    /// <remarks>Moved as the player's layout moves it, and nowhere when the layout leaves it out.</remarks>
    public UiVec2? Place(HudElement element)
    {
        if (Theme.Place(element, Width, Height, Scale) is not { } at)
        {
            return null;
        }

        if (Layout is not { } arranged)
        {
            return new UiVec2(at.X, at.Y);
        }

        Touch.HudPlacement placement = arranged.Layout.Resolve(element);
        if (!placement.Visible && !arranged.Editing)
        {
            return null;
        }

        float unit = HudLayoutOverrides.Unit(Height);
        return new UiVec2(at.X + (placement.Dx * unit), at.Y + (placement.Dy * unit));
    }

    /// <summary>A colour at an opacity.</summary>
    /// <param name="colour">The colour.</param>
    /// <param name="opacity">The opacity, nought to one, times the colour's own.</param>
    /// <returns>The colour.</returns>
    public static Bgra32 Fade(Bgra32 colour, float opacity) =>
        colour.WithAlpha((byte)Math.Clamp(colour.A * Math.Clamp(opacity, 0f, 1f), 0f, 255f));
}
