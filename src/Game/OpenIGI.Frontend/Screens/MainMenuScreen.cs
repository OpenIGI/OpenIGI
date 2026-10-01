using OpenIGI.UI;
using OpenIGI.UI.Layout;

namespace OpenIGI.Frontend.Screens;

/// <summary>
/// The shell's landing page: just the persistent top bar over the slowly-orbiting showcase level, with
/// no tab selected. The bar's tabs lead to the sections.
/// </summary>
public sealed class MainMenuScreen : ShellScreen
{
    /// <summary>Initializes a new instance of the <see cref="MainMenuScreen"/> class.</summary>
    /// <param name="context">The shared shell context.</param>
    public MainMenuScreen(ShellContext context)
        : base(context)
    {
    }

    /// <inheritdoc/>
    protected override ShellTab Tab => ShellTab.Home;

    /// <inheritdoc/>
    /// <remarks>The landing page is the level and the bar over it: here the picture stays sharp.</remarks>
    public override float BackdropBlur => 0f;

    /// <inheritdoc/>
    protected override IReadOnlyList<UI.Kit.Widgets.KeyHint> Hints => [new("Q E", "Sections"), new("ENTER", "Select")];

    /// <inheritdoc/>
    protected override UiElement? BuildContent(LayoutContext context) => null;

    /// <inheritdoc/>
    /// <remarks>
    /// Only out of game. Over a live mission the landing page is the <see cref="PauseScreen"/> and the
    /// backdrop is the level being played, so there is nothing here to show off.
    /// </remarks>
    protected override void OnEnter()
    {
        if (!Context.InGame)
        {
            Context.Host.PreviewMission(null); // the default showcase
        }
    }
}
