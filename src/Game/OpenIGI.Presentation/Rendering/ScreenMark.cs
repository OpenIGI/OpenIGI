using OpenIGI.Core.Graphics;
using OpenIGI.UI.Layout;

namespace OpenIGI.Presentation.Rendering;

/// <summary>One mark of the world debug view, projected to the screen.</summary>
/// <param name="From">A bone's joint, or one end of a capsule, in pixels.</param>
/// <param name="To">The joint above it, or the capsule's other end, in pixels.</param>
/// <param name="FromRadius">A capsule's radius on screen at <paramref name="From"/>; unused for a bone.</param>
/// <param name="ToRadius">And at <paramref name="To"/>.</param>
/// <param name="Color">What it is drawn in.</param>
/// <param name="Label">The bone's name, or <see langword="null"/> for a capsule.</param>
public readonly record struct ScreenMark(UiVec2 From, UiVec2 To, float FromRadius, float ToRadius, Bgra32 Color, string? Label);
