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
    /// <summary>Initializes a new instance of the <see cref="ToggleBoxTask"/> class.</summary>
    /// <remarks>
    /// <c>ToggleBox_Construct</c> (<c>0x41DDD0</c>) fixes the control at 50 by 15 — it never measures
    /// its words — sets both centre flags, and starts <b>on</b>. The default matters: a screen whose
    /// <c>Get Data Script</c> is empty shows Yes rather than No.
    /// </remarks>
    public ToggleBoxTask()
    {
        LayoutWidth = DefaultWidth;
        LayoutHeight = DefaultHeight;
        CentreHorizontally = true;
        CentreVertically = true;
        IsOn = true;
    }

    /// <summary>The control's width in pixels, which is fixed rather than measured.</summary>
    public const int DefaultWidth = 50;

    /// <summary>The control's height in pixels.</summary>
    public const int DefaultHeight = 15;

    /// <summary>The font a switch draws its two words in.</summary>
    /// <remarks>
    /// Hard-coded in <c>ToggleBox_Prepare</c> (<c>0x41DF30</c>), not a parameter — a <c>ToggleBox</c>
    /// declares only its two data scripts, so there is nowhere for a script to say otherwise.
    /// </remarks>
    public const string FontResource = "font4.fnt";

    /// <summary>The language key for the <c>on</c> label.</summary>
    /// <remarks>Looked up through the same table entry labels use, so it translates with everything else.</remarks>
    public const string OnTextResource = "Yes";

    /// <summary>The language key for the <c>off</c> label.</summary>
    public const string OffTextResource = "No";

    /// <summary>Gets the width of the <c>on</c> half of the control.</summary>
    /// <remarks><c>w &gt;&gt; 1</c>, so the truncation goes to this half.</remarks>
    public int OnHalfWidth => LayoutWidth >> 1;

    /// <summary>Gets the width of the <c>off</c> half of the control.</summary>
    public int OffHalfWidth => LayoutWidth - (LayoutWidth >> 1);

    /// <summary>Gets the left edge of the <c>off</c> half.</summary>
    public int OffHalfX => LayoutX + (LayoutWidth >> 1);

    /// <summary>Gets the <c>on</c> half, which is a window so that it can be clicked.</summary>
    /// <remarks>Field <c>+148</c>, a plain <c>MenuWindow</c> the constructor makes with no parent.</remarks>
    public MenuButtonTask OnHalf { get; } = new();

    /// <summary>Gets the <c>off</c> half.</summary>
    /// <remarks>Field <c>+152</c>.</remarks>
    public MenuButtonTask OffHalf { get; } = new();

    /// <inheritdoc/>
    public override IEnumerable<MenuWindowTask> OwnedWindows => [OnHalf, OffHalf];

    /// <summary>Puts the two halves over their sides of the control.</summary>
    /// <remarks>
    /// <c>ToggleBox_Update</c> (<c>0x41E020</c>) does this every tick before hit-testing them, because
    /// the line-up may have moved the control since the last one.
    /// </remarks>
    public void PlaceParts()
    {
        OnHalf.LayoutX = LayoutX;
        OnHalf.LayoutY = LayoutY;
        OnHalf.LayoutWidth = OnHalfWidth;
        OnHalf.LayoutHeight = LayoutHeight;

        OffHalf.LayoutX = OffHalfX;
        OffHalf.LayoutY = LayoutY;
        OffHalf.LayoutWidth = OffHalfWidth;
        OffHalf.LayoutHeight = LayoutHeight;
    }

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
        PlaceParts();

        // Either half's own click, or the posted key — the mouse and the keyboard are one gesture.
        if (OnHalf.WasClicked || PendingCommand == MenuWindowCommand.Left)
        {
            if (!IsOn)
            {
                Manager?.PlaySound(MenuManagerTask.MoveSoundName);
                IsOn = true;
            }
        }
        else if ((OffHalf.WasClicked || PendingCommand == MenuWindowCommand.Right) && IsOn)
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
