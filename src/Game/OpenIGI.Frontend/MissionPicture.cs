using OpenIGI.UI.Drawing;

namespace OpenIGI.Frontend;

/// <summary>
/// A picture of a mission's level, taken beforehand and shipped small: what the menu shows of a map where the level itself
/// is not behind it -- a game picked in the server browser, the map chosen to host.
/// </summary>
/// <param name="Image">The picture, as the interface draws it.</param>
/// <param name="Width">Its width in pixels.</param>
/// <param name="Height">Its height in pixels.</param>
public readonly record struct MissionPicture(UiImage Image, int Width, int Height);
