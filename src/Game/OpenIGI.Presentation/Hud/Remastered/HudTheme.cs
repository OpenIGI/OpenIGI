using OpenIGI.Core.Graphics;
using OpenIGI.Rendering;

namespace OpenIGI.Presentation.Hud.Remastered;

/// <summary>A part of the display a theme places.</summary>
public enum HudElement
{
    /// <summary>The health gauge.</summary>
    Vitals,

    /// <summary>The stance figure.</summary>
    Stance,

    /// <summary>The visibility meter.</summary>
    Visibility,

    /// <summary>The weapon in hand: its name and picture.</summary>
    Weapon,

    /// <summary>The weapon's rounds.</summary>
    Ammo,

    /// <summary>The weapons carried.</summary>
    Inventory,

    /// <summary>The action row.</summary>
    Actions,

    /// <summary>A timed action's bar.</summary>
    Progress,

    /// <summary>The mission's clock.</summary>
    Timer,

    /// <summary>The messages.</summary>
    Messages,

    /// <summary>The objective.</summary>
    Objective,

    /// <summary>The heading strip.</summary>
    Compass,
}

/// <summary>Which edge of the screen an anchor is measured from, across.</summary>
public enum HudEdgeX
{
    /// <summary>From the left edge, rightwards.</summary>
    Left,

    /// <summary>From the middle, rightwards.</summary>
    Centre,

    /// <summary>From the right edge, leftwards.</summary>
    Right,
}

/// <summary>Which edge of the screen an anchor is measured from, down.</summary>
public enum HudEdgeY
{
    /// <summary>From the top, downwards.</summary>
    Top,

    /// <summary>From the middle, downwards.</summary>
    Middle,

    /// <summary>From the bottom, upwards.</summary>
    Bottom,
}

/// <summary>Where an element stands: a point measured from an edge in the title's authored pixels.</summary>
/// <param name="EdgeX">The edge measured from across.</param>
/// <param name="X">How far from it, in authored pixels.</param>
/// <param name="EdgeY">The edge measured from down.</param>
/// <param name="Y">How far from it, in authored pixels.</param>
public readonly record struct HudAnchor(HudEdgeX EdgeX, float X, HudEdgeY EdgeY, float Y)
{
    /// <summary>The point on a screen, in pixels.</summary>
    /// <param name="width">The screen's width in pixels.</param>
    /// <param name="height">The screen's height in pixels.</param>
    /// <param name="scale">The interface's scale.</param>
    /// <returns>The point.</returns>
    public (float X, float Y) Resolve(float width, float height, float scale)
    {
        float x = EdgeX switch
        {
            HudEdgeX.Left => X * scale,
            HudEdgeX.Centre => (width * 0.5f) + (X * scale),
            _ => width - (X * scale),
        };
        float y = EdgeY switch
        {
            HudEdgeY.Top => Y * scale,
            HudEdgeY.Middle => (height * 0.5f) + (Y * scale),
            _ => height - (Y * scale),
        };
        return (x, y);
    }
}

/// <summary>The corners a theme cuts its panels with.</summary>
public enum HudCorner
{
    /// <summary>Cut at forty-five degrees, a field instrument's.</summary>
    Cut,

    /// <summary>A small chamfer, a holographic panel's.</summary>
    Chamfer,
}

/// <summary>A theme's colours, linear in nothing: display colours as the art is drawn.</summary>
/// <param name="Primary">The display's own colour: its text, rules and pictures.</param>
/// <param name="Dim">The same colour, quiet: scales, empty segments, labels.</param>
/// <param name="Highlight">The colour of what is selected or in hand.</param>
/// <param name="Accent">A second colour for what is passing: a reload, the clock.</param>
/// <param name="Danger">The colour of harm: low health, a hit.</param>
/// <param name="Glass">The tint of the panels' glass, with its opacity.</param>
/// <param name="Health">The health gauge's colour at a health from nought to one.</param>
/// <param name="Visibility">The visibility meter's colour at a visibility from nought to one.</param>
public sealed record HudPalette(
    Bgra32 Primary,
    Bgra32 Dim,
    Bgra32 Highlight,
    Bgra32 Accent,
    Bgra32 Danger,
    Bgra32 Glass,
    Func<float, Bgra32> Health,
    Func<float, Bgra32> Visibility);

/// <summary>How a theme draws its shapes.</summary>
/// <param name="Corner">The corners its panels are cut with.</param>
/// <param name="CornerSize">How large a cut is, in authored pixels.</param>
/// <param name="Hairline">How thick a rule is, in authored pixels.</param>
/// <param name="BracketLength">How long a panel's corner brackets run, in authored pixels; nought for none.</param>
/// <param name="Shimmer">Whether a bright band now and then sweeps across the panels.</param>
/// <param name="DoubleRules">Whether rules are drawn as a pair of thin lines.</param>
public sealed record HudMotifs(HudCorner Corner, float CornerSize, float Hairline, float BracketLength, bool Shimmer, bool DoubleRules);

/// <summary>
/// One title's look for the remastered display: its colours, shapes and type, where each part stands, and how the visor it is
/// projected on looks.
/// </summary>
/// <param name="Name">A name for diagnostics.</param>
/// <param name="AuthoredWidth">The display the layout is measured in, across; the interface scale is taken against it.</param>
/// <param name="AuthoredHeight">The display the layout is measured in, down.</param>
/// <param name="Palette">Its colours.</param>
/// <param name="Motifs">Its shapes.</param>
/// <param name="Fx">The visor: curvature, fringe, scanlines and glow, at full strength.</param>
/// <param name="Anchors">Where each part stands; a part without one is not drawn.</param>
public sealed record HudTheme(
    string Name,
    int AuthoredWidth,
    int AuthoredHeight,
    HudPalette Palette,
    HudMotifs Motifs,
    HudFxSettings Fx,
    IReadOnlyDictionary<HudElement, HudAnchor> Anchors)
{
    /// <summary>The height of the display the widgets' own sizes are given for.</summary>
    public const float DesignHeight = 720f;

    /// <summary>The interface's scale for a screen, by the shared rule (<see cref="UiScale"/>) against the authored display.</summary>
    /// <param name="requested">The scale the player asked for, or <see cref="UiScale.Automatic"/>.</param>
    /// <param name="width">The screen's width in pixels.</param>
    /// <param name="height">The screen's height in pixels.</param>
    /// <returns>The scale.</returns>
    public int ScaleFor(int requested, int width, int height) => UiScale.Resolve(requested, width, height, AuthoredWidth, AuthoredHeight);

    /// <summary>Where a part stands on a screen, or <see langword="null"/> where the theme does not place it.</summary>
    /// <param name="element">The part.</param>
    /// <param name="width">The screen's width in pixels.</param>
    /// <param name="height">The screen's height in pixels.</param>
    /// <param name="scale">The interface's scale.</param>
    /// <returns>The point, in pixels.</returns>
    public (float X, float Y)? Place(HudElement element, float width, float height, float scale) =>
        Anchors.TryGetValue(element, out HudAnchor anchor) ? anchor.Resolve(width, height, scale) : null;
}

/// <summary>Where a title's pictures come from: its own sprites, by name.</summary>
public interface IHudIconSource
{
    /// <summary>Finds a picture.</summary>
    /// <param name="icon">The picture's name.</param>
    /// <param name="width">Receives its width in pixels.</param>
    /// <param name="height">Receives its height in pixels.</param>
    /// <returns>Whether the title has it.</returns>
    bool TryMeasure(HudIcon icon, out float width, out float height);

    /// <summary>Draws a picture as a mask, in one colour, at a place and size.</summary>
    /// <param name="batch">The batch to draw into.</param>
    /// <param name="icon">The picture.</param>
    /// <param name="x">Its left edge, in pixels.</param>
    /// <param name="y">Its top edge, in pixels.</param>
    /// <param name="scale">How many pixels each of its own is drawn as.</param>
    /// <param name="colour">The colour it is drawn in; its own colours are kept where the title's art carries them.</param>
    void Draw(OpenIGI.Rendering.SpriteBatch batch, HudIcon icon, float x, float y, float scale, Bgra32 colour);

    /// <summary>The picture itself, for the display to draw from a smooth copy of it; <see langword="null"/> to have <see cref="Draw"/> draw it.</summary>
    /// <param name="icon">The picture.</param>
    /// <returns>The title's uploaded picture, or <see langword="null"/>.</returns>
    OpenIGI.Presentation.Rendering.SpriteResource? Resolve(HudIcon icon) => null;
}
