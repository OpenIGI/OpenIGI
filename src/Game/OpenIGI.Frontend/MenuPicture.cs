using OpenIGI.UI.Drawing;

namespace OpenIGI.Frontend;

/// <summary>
/// A picture the menu draws, made a texture by the host: a mission's level, taken beforehand and shipped small (a game
/// picked in the server browser, the map chosen to host), or the signed-in player's Discord avatar.
/// </summary>
/// <param name="Image">The picture, as the interface draws it.</param>
/// <param name="Width">Its width in pixels.</param>
/// <param name="Height">Its height in pixels.</param>
public readonly record struct MenuPicture(UiImage Image, int Width, int Height)
{
    /// <summary>Gets the picture's width over its height.</summary>
    public float Aspect => Width / (float)Math.Max(1, Height);
}
