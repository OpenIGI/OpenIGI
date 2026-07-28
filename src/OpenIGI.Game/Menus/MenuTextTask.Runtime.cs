namespace OpenIGI.Game.Menus;

/// <summary>
/// The entry's per-tick behaviour: it is the type that consumes a choose.
/// </summary>
/// <remarks>
/// <para>
/// Transcribed from <c>MenuText_Update</c> (<c>0x4207E0</c>). A completed pointer click and an
/// <c>Enter</c> posted by the manager are the same event here — <c>if (wasClicked || pending == -7)</c>
/// — which is why the keyboard and the mouse cannot disagree about what "choosing" means.
/// </para>
/// <para>
/// This is also the one control that <b>clears</b> its pending command, and it clears it whether or
/// not it acted. A slider and a switch leave theirs set and rely on their own guards instead.
/// </para>
/// </remarks>
public partial class MenuTextTask
{
    /// <inheritdoc/>
    protected override void OnUpdate()
    {
        ApplyPendingCommand();
        base.OnUpdate();
    }

    /// <summary>Runs the entry's action if it was chosen, then clears the pending command.</summary>
    /// <remarks>The body of <c>MenuText_Update</c>, without the base chain.</remarks>
    public void ApplyPendingCommand()
    {
        bool chosen = WasClicked || PendingCommand == MenuWindowCommand.Choose;

        if (chosen && OnClickScript.Length > 0)
        {
            // Script first, then the sound: the original runs the expression and only then decides
            // between the entry's own sound name and menu_sel.
            Run(OnClickScript);
            Manager?.PlaySound(
                ClickSoundResource.Length > 0 ? ClickSoundResource : MenuManagerTask.ChooseSoundName);
        }

        // Cleared unconditionally, at the end, before the base chain — exactly where the original
        // puts it.
        PendingCommand = MenuWindowCommand.None;
    }
}
