using OpenIGI.Core.Graphics;
using OpenIGI.Rendering;
using OpenIGI.UI;
using OpenIGI.UI.Drawing;
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
    public float DesignScale => Scale * Theme.AuthoredHeight / HudTheme.DesignHeight;

    /// <summary>Gets or sets how much of the part being drawn shows, at the display's density; the painter's own opacity carries it to shapes and text.</summary>
    public float PartOpacity { get; internal set; } = 1f;

    /// <summary>Draws one of the title's pictures, at the part's opacity.</summary>
    /// <param name="icon">The picture.</param>
    /// <param name="x">Its left edge, in pixels.</param>
    /// <param name="y">Its top edge, in pixels.</param>
    /// <param name="scale">How many pixels each of its own is drawn as.</param>
    /// <param name="colour">Its colour.</param>
    public void DrawIcon(HudIcon icon, float x, float y, float scale, Bgra32 colour) =>
        Icons.Draw(Batch, icon, x, y, scale, Fade(colour, PartOpacity));

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
    public UiVec2? Place(HudElement element) =>
        Theme.Place(element, Width, Height, Scale) is { } at ? new UiVec2(at.X, at.Y) : null;

    /// <summary>A colour at an opacity.</summary>
    /// <param name="colour">The colour.</param>
    /// <param name="opacity">The opacity, nought to one, times the colour's own.</param>
    /// <returns>The colour.</returns>
    public static Bgra32 Fade(Bgra32 colour, float opacity) =>
        colour.WithAlpha((byte)Math.Clamp(colour.A * Math.Clamp(opacity, 0f, 1f), 0f, 255f));
}
