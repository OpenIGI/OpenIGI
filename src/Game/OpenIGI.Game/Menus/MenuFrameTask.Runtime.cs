namespace OpenIGI.Game.Menus;

/// <summary>
/// The frame's per-tick behaviour: a selectable frame hands what the manager posts to it, and its highlight, to its controls.
/// </summary>
/// <remarks>
/// Transcribed from <c>MenuFrame_Update</c> (<c>0x422A50</c>). The selection carries a selectable frame, not the toggle or slider
/// inside it, so a key the manager posts lands on the frame. While it is selectable the frame copies its pending command
/// (<c>+80</c>) and its highlight (<c>+77</c>) onto every menu window under it, then clears its own command before its children
/// update: Left and Right on an options row reach the control in it on the same tick, and the row's label lights with it.
/// </remarks>
public partial class MenuFrameTask
{
    /// <inheritdoc/>
    protected override void OnUpdate()
    {
        if (IsInteractive)
        {
            foreach (MenuWindowTask child in Children.OfType<MenuWindowTask>())
            {
                child.PendingCommand = PendingCommand;
                child.IsHighlighted = IsHighlighted;
            }
        }

        PendingCommand = MenuWindowCommand.None;
        base.OnUpdate();
    }
}
