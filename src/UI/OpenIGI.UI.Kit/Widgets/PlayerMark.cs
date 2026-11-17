using OpenIGI.UI.Drawing;
using OpenIGI.UI.Layout;
using OpenIGI.UI.Widgets;

namespace OpenIGI.UI.Kit.Widgets;

/// <summary>A player's mark on its own: their avatar or initial in a square (<see cref="KitDraw.PlayerMark"/>), sized in design units.</summary>
/// <param name="theme">The theme.</param>
/// <param name="name">The player's name, for the initial.</param>
public sealed class PlayerMark(UiTheme theme, string name) : UiElement
{
    /// <summary>Gets or sets whether the player is signed in.</summary>
    public bool SignedIn { get; set; }

    /// <summary>Gets or sets the avatar and its width over its height, or <see langword="null"/> for the initial.</summary>
    public (UiImage Image, float Aspect)? Avatar { get; set; }

    /// <summary>Gets or sets the square's side, in design units.</summary>
    public float Side { get; set; } = 88f;

    /// <inheritdoc/>
    protected override UiVec2 MeasureContent(LayoutContext context, UiVec2 available) => new(context.Px(Side), context.Px(Side));

    /// <inheritdoc/>
    protected override void OnDraw(UiPainter painter) => KitDraw.PlayerMark(painter, theme, Bounds, name, Avatar, SignedIn);
}
