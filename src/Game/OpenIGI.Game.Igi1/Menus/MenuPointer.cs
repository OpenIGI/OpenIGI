using OpenIGI.Core.Graphics;

namespace OpenIGI.Game.Igi1.Menus;

/// <summary>
/// The pointer state a menu hit-test reads: a position in authored coordinates and three button
/// signals.
/// </summary>
/// <remarks>
/// <para>
/// The manager keeps this block at <c>+10188</c> and refreshes it each tick in state 2 of
/// <c>MenuManager_Update</c> (<c>0x418080</c>). The three flags are derived there from the button and
/// its previous value: down, the pressed edge, and the released edge.
/// </para>
/// <para>
/// The position is converted out of screen space on the way in — <c>x += 320 - (screenWidth &gt;&gt; 1)</c>
/// — which is the exact inverse of the centring the items are drawn with, so the hit-test works in
/// authored 640×480 coordinates whatever the back buffer is.
/// </para>
/// </remarks>
public sealed class MenuPointer
{
    /// <summary>Gets the horizontal position in authored pixels.</summary>
    public int X { get; private set; }

    /// <summary>Gets the vertical position in authored pixels.</summary>
    public int Y { get; private set; }

    /// <summary>Gets a value indicating whether the button is held.</summary>
    public bool IsDown { get; private set; }

    /// <summary>Gets a value indicating whether the button went down this tick.</summary>
    public bool Pressed { get; private set; }

    /// <summary>Gets a value indicating whether the button came up this tick.</summary>
    public bool Released { get; private set; }

    /// <summary>Updates the pointer from a screen-space position and the raw button state.</summary>
    /// <param name="screenX">The horizontal position in back-buffer pixels.</param>
    /// <param name="screenY">The vertical position in back-buffer pixels.</param>
    /// <param name="screenWidth">The back-buffer width in pixels.</param>
    /// <param name="screenHeight">The back-buffer height in pixels.</param>
    /// <param name="buttonDown">Whether the button is held this tick.</param>
    /// <param name="scale">The magnification the menu is drawn at; see <see cref="UiScale"/>.</param>
    public void Update(
        int screenX,
        int screenY,
        int screenWidth,
        int screenHeight,
        bool buttonDown,
        int scale = UiScale.Native)
    {
        bool wasDown = IsDown;

        // The inverse of MenuLayout.ToScreen, so the hit-test sees authored coordinates. It has to
        // undo the magnification too, or the mouse would land somewhere other than what it points at.
        int s = scale < UiScale.Native ? UiScale.Native : scale;
        X = ((screenX - (screenWidth >> 1)) / s) + (MenuLayout.AuthoredWidth / 2);
        Y = ((screenY - (screenHeight >> 1)) / s) + (MenuLayout.AuthoredHeight / 2);

        IsDown = buttonDown;
        Pressed = buttonDown && buttonDown != wasDown;
        Released = !buttonDown && buttonDown != wasDown;
    }

    /// <summary>Updates the pointer from a position already in authored coordinates.</summary>
    /// <param name="x">The horizontal position in authored pixels.</param>
    /// <param name="y">The vertical position in authored pixels.</param>
    /// <param name="buttonDown">Whether the button is held this tick.</param>
    /// <remarks>For a host that has already mapped the position; <see cref="Update"/> does the mapping itself.</remarks>
    public void Set(int x, int y, bool buttonDown)
    {
        bool wasDown = IsDown;

        X = x;
        Y = y;
        IsDown = buttonDown;
        Pressed = buttonDown && buttonDown != wasDown;
        Released = !buttonDown && buttonDown != wasDown;
    }

    /// <summary>Clears the button signals, as the manager's inactive state does.</summary>
    public void ClearButtons()
    {
        IsDown = false;
        Pressed = false;
        Released = false;
    }
}
