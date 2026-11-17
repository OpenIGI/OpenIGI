using OpenIGI.Core.Graphics;
using OpenIGI.UI;
using OpenIGI.UI.Kit;
using OpenIGI.UI.Layout;
using OpenIGI.UI.Widgets;

namespace OpenIGI.Frontend.Screens;

/// <summary>
/// The player's mark on their profile: the initial of their name in a square, outlined in the accent and washed with it
/// once they are signed in, grey while they play without an account.
/// </summary>
public sealed class OperativeMark : UiElement
{
    private readonly UiTheme _theme;
    private readonly string _initial;

    /// <summary>Initializes a new instance of the <see cref="OperativeMark"/> class.</summary>
    /// <param name="theme">The theme.</param>
    /// <param name="name">The name its initial is taken from.</param>
    public OperativeMark(UiTheme theme, string name)
    {
        ArgumentNullException.ThrowIfNull(theme);
        _theme = theme;
        _initial = name.Trim() is { Length: > 0 } trimmed ? trimmed[..1].ToUpperInvariant() : "?";
    }

    /// <summary>Gets or sets whether the player is signed in.</summary>
    public bool SignedIn { get; set; }

    /// <summary>Gets or sets the square's side, in units.</summary>
    public float Side { get; set; } = 88f;

    /// <inheritdoc/>
    protected override UiVec2 MeasureContent(LayoutContext context, UiVec2 available) => new(context.Px(Side), context.Px(Side));

    /// <inheritdoc/>
    protected override void OnDraw(UiPainter painter)
    {
        LayoutContext context = painter.Context;
        Bgra32 ink = SignedIn ? _theme.Accent : _theme.TextMuted;
        if (SignedIn)
        {
            painter.FillRect(Bounds, UiPalette.AccentAt(0.13f));
        }

        painter.StrokeRect(Bounds, context.Px(SignedIn ? 2f : 1f), SignedIn ? _theme.Accent : _theme.Border);
        float size = Bounds.Height * 0.5f;
        float width = _theme.Display.MeasureWidth(_initial, size);
        painter.DrawText(_theme.Display, _initial, new UiVec2(Bounds.Center.X - (width * 0.5f), UiRowStyle.CapsCenterY(_theme.Display, size, Bounds)), size, ink);
    }
}
