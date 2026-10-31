using OpenIGI.Core.Numerics;

namespace OpenIGI.Presentation;

/// <summary>A line of co-op chat as the HUD shows it.</summary>
/// <param name="Name">Who said it.</param>
/// <param name="Text">What was said.</param>
/// <param name="Opacity">How much of it is left to see, fading at the end of its time.</param>
public readonly record struct HudChatLine(string Name, string Text, float Opacity);

/// <summary>A place a co-op player marked, as the HUD shows it.</summary>
/// <param name="AnchorMetres">Where, in metres.</param>
/// <param name="Name">Who marked it.</param>
/// <param name="Opacity">How much of it is left to see.</param>
public readonly record struct HudMark(Vector3d AnchorMetres, string Name, float Opacity);
