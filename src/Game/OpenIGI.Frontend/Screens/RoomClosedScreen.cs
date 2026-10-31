using OpenIGI.Input;
using OpenIGI.UI;
using OpenIGI.UI.Focus;
using OpenIGI.UI.Kit.Widgets;
using OpenIGI.UI.Layout;
using OpenIGI.UI.Widgets;

namespace OpenIGI.Frontend.Screens;

/// <summary>
/// The page a co-op client is shown over its mission when the room it played in is gone: the host left, or the line to it
/// dropped. The way back in is the room's code again, while the host still holds it open.
/// </summary>
public sealed class RoomClosedScreen : ShellScreen
{
    private readonly string _reason;
    private readonly string? _code;

    /// <summary>Initializes a new instance of the <see cref="RoomClosedScreen"/> class.</summary>
    /// <param name="context">The shared shell context.</param>
    /// <param name="reason">Why the room is gone, as the session said it.</param>
    /// <param name="code">The room's code, to join it again by, or <see langword="null"/>.</param>
    public RoomClosedScreen(ShellContext context, string reason, string? code)
        : base(context)
    {
        _reason = reason ?? string.Empty;
        _code = code;
    }

    /// <inheritdoc/>
    protected override ShellTab Tab => ShellTab.Home;

    /// <inheritdoc/>
    protected override IReadOnlyList<KeyHint> Hints => _code is null ? [new("ESC", "Back to menu")] : [new("ENTER", "Rejoin"), new("ESC", "Back to menu")];

    /// <inheritdoc/>
    protected override UiElement? BuildContent(LayoutContext context)
    {
        string eyebrow = _reason.Length > 0 ? char.ToUpperInvariant(_reason[0]) + _reason[1..] : "Disconnected";
        (string, Action) back = ("Back to menu", () => Context.Host.AbandonMission());
        return _code is { } code
            ? MissionActions.Build(
                Context.Theme,
                eyebrow,
                "Room closed",
                Context.Theme.Danger,
                ("Rejoin", () => Context.Host.Coop?.Join(code)),
                [("BACK TO MENU", ButtonAccent.Ghost, back.Item2)])
            : MissionActions.Build(Context.Theme, eyebrow, "Room closed", Context.Theme.Danger, back, []);
    }

    /// <inheritdoc/>
    protected override bool HandleInput(UiNavigation nav, InputState input)
    {
        // A back press nothing wanted leaves the mission, as on the failure page: there is nothing to go back to.
        if (base.HandleInput(nav, input))
        {
            Context.Host.AbandonMission();
        }

        return false;
    }
}
