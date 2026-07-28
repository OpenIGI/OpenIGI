namespace OpenIGI.Game.Menus;

/// <summary>
/// The two-state switch's per-tick behaviour.
/// </summary>
/// <remarks>
/// <para>
/// Transcribed from <c>ToggleBox_Update</c> (<c>0x41E020</c>). The control is two half-width windows
/// side by side — the left half is <b>on</b> and the right half is <b>off</b>, which is the opposite
/// way round from what the names suggest and is worth stating plainly. The original splits its own
/// rectangle with <c>w &gt;&gt; 1</c> for the left half and <c>w - (w &gt;&gt; 1)</c> for the right,
/// so an odd width gives the extra pixel to the right half.
/// </para>
/// <para>
/// Left and right therefore <em>set</em> the state rather than flipping it, and pressing left on a
/// switch that is already on does nothing at all — not even a sound. That is what makes a still-posted
/// command harmless: the control never clears <c>+80</c>, and only a genuine change is announced.
/// </para>
/// </remarks>
public partial class ToggleBoxTask
{
    /// <summary>Gets the width of the <c>on</c> half of the control.</summary>
    /// <remarks><c>w &gt;&gt; 1</c>, so the truncation goes to this half.</remarks>
    public int OnHalfWidth => LayoutWidth >> 1;

    /// <summary>Gets the width of the <c>off</c> half of the control.</summary>
    public int OffHalfWidth => LayoutWidth - (LayoutWidth >> 1);

    /// <summary>Gets the left edge of the <c>off</c> half.</summary>
    public int OffHalfX => LayoutX + (LayoutWidth >> 1);

    /// <inheritdoc/>
    protected override void OnUpdate()
    {
        ApplyPendingCommand();
        base.OnUpdate();
    }

    /// <summary>Acts on the key the manager posted.</summary>
    /// <remarks>The body of <c>ToggleBox_Update</c>, without the base chain.</remarks>
    public void ApplyPendingCommand()
    {
        if (PendingCommand == MenuWindowCommand.Left)
        {
            if (!IsOn)
            {
                Manager?.PlaySound(MenuManagerTask.MoveSoundName);
                IsOn = true;
            }
        }
        else if (PendingCommand == MenuWindowCommand.Right && IsOn)
        {
            Manager?.PlaySound(MenuManagerTask.MoveSoundName);
            IsOn = false;
        }
    }

    /// <inheritdoc/>
    /// <remarks>
    /// The update plays the sound and flips the flag and nothing else — it does <em>not</em> run the
    /// <c>Set Data Script</c>. That happens on the way out, through <see cref="OnSaveData"/>, so
    /// <c>Config_GameOptionsInputSetInvertMouse(this.isOn)</c> is applied when the screen is left
    /// rather than on each keypress.
    /// </remarks>
    protected override void OnLoadData()
    {
        if (GetDataScript.Length > 0)
        {
            IsOn = Evaluate(GetDataScript) != 0.0;
        }
    }

    /// <inheritdoc/>
    protected override void OnSaveData() => Run(SetDataScript);
}
