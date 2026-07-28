namespace OpenIGI.Game.Menus;

/// <summary>
/// The slider's per-tick behaviour and its value model.
/// </summary>
/// <remarks>
/// <para>
/// Transcribed from <c>SlideBar_Update</c> (<c>0x41FA10</c>), <c>SlideBar_LoadData</c>
/// (<c>0x41FF30</c>), <c>SlideBar_SaveData</c> (<c>0x420020</c>), <c>SlideBar_GetIndex</c>
/// (<c>0x420290</c>) and <c>SlideBar_SetIndex</c> (<c>0x4202D0</c>).
/// </para>
/// <para>
/// The carret position is a <c>double</c> in <c>[0, 1]</c> and everything else is derived from it. The
/// three scripts have distinct jobs and are not interchangeable: <c>Get Data Script</c> seeds the
/// value when the screen is entered, <c>Modify Data Script</c> runs on <em>every</em> change — it is
/// the live preview, which is why the music slider's is <c>Game_SetMusicVolume(this.vValue)</c> — and
/// <c>Set Data Script</c> runs only on the way out, which is where
/// <c>Config_SoundOptionsSetMusicVolume(this.vValue)</c> sits.
/// </para>
/// </remarks>
public partial class SlideBarTask
{
    /// <summary>The step a continuous slider moves per key press, when nothing overrides it.</summary>
    /// <remarks><c>SlideBar_Construct</c> (<c>0x41F5A0</c>) stores <c>0x3D4CCCCD</c>, which is 0.05f.</remarks>
    public const float DefaultStep = 0.05f;

    /// <summary>The bar's length in pixels when the script does not give one.</summary>
    /// <remarks>The constructor's <c>+172 = 50</c>.</remarks>
    public const int DefaultSize = 50;

    /// <summary>Gets or sets the number of discrete stops, or zero when the slider is continuous.</summary>
    /// <remarks>Field <c>+584</c>, evaluated from <c>Set Num Options</c> each time the data is loaded.</remarks>
    public int NumOptions { get; set; }

    /// <summary>Gets or sets the step a continuous slider takes per press.</summary>
    /// <remarks>Field <c>+168</c>. A slider with discrete stops ignores it and uses <c>1 / (n - 1)</c>.</remarks>
    public float Step { get; set; } = DefaultStep;

    /// <summary>Gets the distance one press moves the carret.</summary>
    /// <remarks>
    /// <c>numOptions ? 1 / (numOptions - 1) : step</c>. Note that a slider declaring exactly one option
    /// divides by zero here in the original as much as it would here; no retail script does.
    /// </remarks>
    public double StepSize => NumOptions != 0 ? 1.0 / (NumOptions - 1) : Step;

    /// <inheritdoc/>
    /// <remarks>
    /// A left press steps down and a right press steps up, each only when there is room — the guard is
    /// what stops a still-posted command from running the carret off the end. Every accepted change
    /// plays <c>menu_high</c> and runs the <c>Modify Data Script</c>.
    /// </remarks>
    protected override void OnUpdate()
    {
        // The control does its own work and then chains to the base, which is the order every menu
        // type's update has: the base call is the last statement in all of them.
        ApplyPendingCommand();
        base.OnUpdate();
    }

    /// <summary>Acts on the key the manager posted, then snaps and clamps the carret.</summary>
    /// <remarks>The body of <c>SlideBar_Update</c>, without the base chain.</remarks>
    public void ApplyPendingCommand()
    {
        bool changed = false;

        if (PendingCommand == MenuWindowCommand.Left)
        {
            if (Value > 0.0)
            {
                Value -= StepSize;
                changed = true;
            }
        }
        else if (PendingCommand == MenuWindowCommand.Right && Value < 1.0)
        {
            Value += StepSize;
            changed = true;
        }

        Quantise();

        // The clamp and the snap run whether or not anything moved, but only a real change is
        // announced: the original sets its "changed" flag on the three input paths and nowhere else.
        if (changed)
        {
            Manager?.PlaySound(MenuManagerTask.MoveSoundName);
            Run(ModifyDataScript);
        }
    }

    /// <summary>Snaps the carret to its nearest stop, clamps it, and recomputes the option index.</summary>
    /// <remarks>
    /// The tail of <c>SlideBar_Update</c>. A discrete slider snaps to <c>k / (n - 1)</c>, and the clamp
    /// to <c>[0, 1]</c> runs afterwards for both kinds.
    /// </remarks>
    public void Quantise()
    {
        if (NumOptions != 0)
        {
            double stops = NumOptions - 1;
            Value = Math.Floor((stops * Value) + 0.5) / stops;
        }

        Value = Math.Clamp(Value, 0.0, 1.0);
        Position = GetIndex();
    }

    /// <summary>Gets the option index the current carret position selects.</summary>
    /// <returns>The index, or zero for a continuous slider.</returns>
    /// <remarks>
    /// <c>SlideBar_GetIndex</c> (<c>0x420290</c>): <c>(int)((n - 1) * value + 0.5)</c>. Note that this
    /// is <em>not</em> the inverse of <see cref="SetIndex"/>, which divides by <c>n</c> rather than
    /// <c>n - 1</c>. The asymmetry is the original's and it is left alone.
    /// </remarks>
    public int GetIndex() =>
        NumOptions != 0 ? (int)(((NumOptions - 1) * Value) + 0.5) : 0;

    /// <summary>Places the carret at an option index.</summary>
    /// <param name="index">The index.</param>
    /// <remarks><c>SlideBar_SetIndex</c> (<c>0x4202D0</c>): <c>value = (index + 0.5) / n</c>.</remarks>
    public void SetIndex(int index)
    {
        if (NumOptions != 0)
        {
            Value = (index + 0.5) / NumOptions;
        }
    }

    /// <inheritdoc/>
    /// <remarks>
    /// <c>Set Num Options</c> is read first, because it decides how <c>Get Data Script</c> is
    /// interpreted: a discrete slider's result is an <em>index</em> and goes through
    /// <see cref="SetIndex"/>, a continuous one's is the carret position itself.
    /// </remarks>
    protected override void OnLoadData()
    {
        if (SetNumOptions.Length > 0)
        {
            NumOptions = (int)Evaluate(SetNumOptions);
        }

        if (GetDataScript.Length == 0)
        {
            return;
        }

        if (NumOptions != 0)
        {
            SetIndex((int)Evaluate(GetDataScript));
        }
        else
        {
            Value = Evaluate(GetDataScript);
        }

        Position = GetIndex();
    }

    /// <inheritdoc/>
    protected override void OnSaveData() => Run(SetDataScript);
}
