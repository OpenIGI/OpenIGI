using OpenIGI.Input;
using OpenIGI.UI;
using OpenIGI.UI.Focus;
using OpenIGI.UI.Kit.Widgets;
using OpenIGI.UI.Layout;
using OpenIGI.UI.Widgets;

namespace OpenIGI.Frontend.Screens;

/// <summary>
/// The page the shell opens when the player dies or the mission is lost: the same shape as the pause menu,
/// but there is nothing to resume, so the primary action is a restart and Back leaves the mission.
/// </summary>
public sealed class MissionFailedScreen : ShellScreen
{
    /// <summary>Initializes a new instance of the <see cref="MissionFailedScreen"/> class.</summary>
    /// <param name="context">The shared shell context.</param>
    public MissionFailedScreen(ShellContext context)
        : base(context)
    {
    }

    /// <inheritdoc/>
    protected override ShellTab Tab => ShellTab.Home;

    /// <inheritdoc/>
    protected override IReadOnlyList<KeyHint> Hints => [new("ENTER", "Restart"), new("ESC", "Abandon")];

    /// <inheritdoc/>
    protected override UiElement? BuildContent(LayoutContext context) =>
        MissionActions.Build(
            Context.Theme,
            "Operation",
            "Mission failed",
            Context.Theme.Danger,
            ("Restart", () => Context.Host.RestartMission()),
            [("ABANDON MISSION", ButtonAccent.Destructive, () => Context.Host.AbandonMission())]);

    /// <inheritdoc/>
    protected override bool HandleInput(UiNavigation nav, InputState input)
    {
        // The same shape as the pause menu's: what ShellScreen hands back is a back press nothing wanted,
        // and here that means leaving the mission — there is nothing to resume.
        if (base.HandleInput(nav, input))
        {
            Context.Host.AbandonMission();
        }

        return false;
    }
}
