using OpenIGI.Core.Graphics;
using OpenIGI.UI.Layout;
using OpenIGI.UI.Widgets;
using OpenIGI.UI.Animation;

namespace OpenIGI.UI.Kit.Widgets;

/// <summary>
/// The feedback of buttons a row draws on itself rather than holding as controls of their own (<see cref="ActionRow"/>'s, a
/// board row's Watch and Race): the one the pointer is over lit, and the one just pressed flashing for a moment.
/// </summary>
/// <remarks>The row tells it where the pointer is and what was pressed, steps it each frame, and asks it how lit a button is.</remarks>
public sealed class RowButtonFeedback
{
    // How long a press's flash takes to fade, in seconds.
    private const float FlashSeconds = 0.35f;

    private UiVec2? _pointer;
    private UiRect? _pressed;
    private float _flash;

    /// <summary>Notes where the pointer is over the row, or that it has left.</summary>
    /// <param name="point">The pointer, or <see langword="null"/> once it has left the row.</param>
    public void PointerAt(UiVec2? point) => _pointer = point;

    /// <summary>Flashes a button, pressed now.</summary>
    /// <param name="button">Where the button is drawn.</param>
    public void Pressed(UiRect button)
    {
        _pressed = button;
        _flash = 1f;
    }

    /// <summary>Fades the flash by a frame.</summary>
    /// <param name="elapsedSeconds">The frame's seconds.</param>
    public void Update(float elapsedSeconds) => _flash = MathF.Max(0f, _flash - (Math.Max(0f, elapsedSeconds) / FlashSeconds));

    /// <summary>Gets how lit a button is: the pointer over it, or its press's flash.</summary>
    /// <param name="button">Where the button is drawn.</param>
    /// <returns>Nought to one.</returns>
    public float Lit(UiRect button) => MathF.Max(IsOver(button) ? 0.6f : 0f, Flash(button));

    /// <summary>Gets how strongly a button flashes from a press.</summary>
    /// <param name="button">Where the button is drawn.</param>
    /// <returns>Nought to one.</returns>
    public float Flash(UiRect button) => _pressed == button ? _flash : 0f;

    /// <summary>Gets whether the pointer is over a button.</summary>
    /// <param name="button">Where the button is drawn.</param>
    /// <returns>Whether it is.</returns>
    public bool IsOver(UiRect button) => _pointer is { } point && button.Contains(point.X, point.Y);

    /// <summary>Draws a button's lit fill under its outline: a wash of the accent, stronger as it flashes.</summary>
    /// <param name="painter">The painter.</param>
    /// <param name="theme">The theme.</param>
    /// <param name="button">Where the button is drawn.</param>
    /// <param name="appear">The row's entrance.</param>
    public void DrawFill(UiPainter painter, UiTheme theme, UiRect button, float appear)
    {
        ArgumentNullException.ThrowIfNull(painter);
        ArgumentNullException.ThrowIfNull(theme);
        float lit = Lit(button);
        if (lit > 0.001f)
        {
            painter.FillRect(button, UiPalette.Fade(theme.Accent, ((0.14f * lit) + (0.3f * Flash(button))) * appear));
        }
    }

    /// <summary>Gets a button's colour lit: its own, towards the accent as it is lit.</summary>
    /// <param name="colour">The button's colour unlit.</param>
    /// <param name="theme">The theme.</param>
    /// <param name="button">Where the button is drawn.</param>
    /// <returns>The colour.</returns>
    public Bgra32 Tint(Bgra32 colour, UiTheme theme, UiRect button)
    {
        ArgumentNullException.ThrowIfNull(theme);
        return Interpolate.Lerp(colour, theme.AccentHover, MathF.Min(1f, Lit(button) * 1.4f));
    }
}
