
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
    /// <summary>Initializes a new instance of the <see cref="SlideBarTask"/> class.</summary>
    /// <remarks>
    /// <c>SlideBar_Construct</c> (<c>0x41F5A0</c>) starts the control at (300, 230) with both centre
    /// flags set, a horizontal bar 50 pixels long and a step of 0.05 — the same position and centring
    /// a <c>MenuText</c> gets, so a slider drops into a column of entries without the script saying so.
    /// </remarks>
    public SlideBarTask()
    {
        LayoutX = 300;
        LayoutY = 230;
        CentreHorizontally = true;
        CentreVertically = true;
        Size = DefaultSize;
        Horizontal = true;
    }

    /// <inheritdoc/>
    public override IEnumerable<MenuWindowTask> OwnedWindows => [LeftButton, RightButton];

    /// <summary>The step a continuous slider moves per key press, when nothing overrides it.</summary>
    /// <remarks><c>SlideBar_Construct</c> (<c>0x41F5A0</c>) stores <c>0x3D4CCCCD</c>, which is 0.05f.</remarks>
    public const float DefaultStep = 0.05f;

    /// <summary>The bar's length in pixels when the script does not give one.</summary>
    /// <remarks>The constructor's <c>+172 = 50</c>.</remarks>
    public const int DefaultSize = 50;

    /// <summary>Gets the left arrow, which steps the carret down.</summary>
    /// <remarks>Field <c>+152</c>, a <c>MenuButton</c> the constructor makes.</remarks>
    public MenuButtonTask LeftButton { get; } = new();

    /// <summary>Gets the right arrow, which steps the carret up.</summary>
    /// <remarks>Field <c>+156</c>.</remarks>
    public MenuButtonTask RightButton { get; } = new();

    /// <summary>Gets the sprite drawn as the carret, or an empty string.</summary>
    /// <remarks>
    /// <c>SlideBar_Prepare</c> (<c>0x41F740</c>) derives all three sprite names from the one
    /// <c>Sprite Resource</c> parameter: <c>_1.spr</c> is the left arrow, <c>_2.spr</c> the right and
    /// <c>_3.spr</c> the carret. A slider whose resource is <c>slide</c> therefore wants
    /// <c>slide_1.spr</c>, <c>slide_2.spr</c> and <c>slide_3.spr</c> in the menu archive.
    /// </remarks>
    public string CarretSprite => SpriteResource.Length > 0 ? $"{SpriteResource}_3.spr" : string.Empty;

    /// <summary>Gets the pixel size of the carret sprite, filled in by whatever loaded it.</summary>
    /// <remarks>
    /// The measure and the carret's travel both need it, and neither belongs in a renderer. It stays
    /// zero until something resolves the sprite, and a zero-sized carret simply does not move.
    /// </remarks>
    public int CarretWidth { get; set; }

    /// <summary>Gets the pixel height of the carret sprite.</summary>
    public int CarretHeight { get; set; }

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
        (int trackX, int trackY, _, _) = PlaceParts();

        // Exactly one of three inputs, in this order: the left arrow, the right arrow, then dragging
        // the carret. An arrow is checked by its own click flag OR by the posted key, which is what
        // makes the mouse and the keyboard the same gesture.
        if (LeftButton.WasClicked || PendingCommand == MenuWindowCommand.Left)
        {
            if (Value > 0.0)
            {
                Value -= StepSize;
                changed = true;
            }
        }
        else if (RightButton.WasClicked || PendingCommand == MenuWindowCommand.Right)
        {
            if (Value < 1.0)
            {
                Value += StepSize;
                changed = true;
            }
        }
        else if (TryDrag(trackX, trackY, out double dragged))
        {
            Value = dragged;
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

    /// <summary>Reads a carret drag, when the pointer is holding this control.</summary>
    /// <param name="trackX">The track's left edge.</param>
    /// <param name="trackY">The track's top edge.</param>
    /// <param name="value">Receives the position the pointer names.</param>
    /// <returns><see langword="false"/> when nothing is being dragged.</returns>
    /// <remarks>
    /// The travel is the bar less the carret's own size, because the carret is placed by its top-left
    /// corner — so the pointer maps onto <c>(pointer - trackOrigin) / (Size - carretSize)</c>. The
    /// original gates this on the <em>held</em> flag rather than a completed click, which is what
    /// makes it a drag rather than a jump.
    /// </remarks>
    private bool TryDrag(int trackX, int trackY, out double value)
    {
        value = Value;

        if (Manager is not { Pointer: { } pointer } || !IsHeld)
        {
            return false;
        }

        int travel = Horizontal ? Size - CarretWidth : Size - CarretHeight;
        if (travel <= 0)
        {
            return false;
        }

        value = Horizontal
            ? (double)(pointer.X - trackX) / travel
            : (double)(pointer.Y - trackY) / travel;

        return true;
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

    /// <summary>Names the two arrows' sprites and sizes the control around them.</summary>
    /// <remarks>
    /// <c>SlideBar_Prepare</c> (<c>0x41F740</c>). It also reads <c>Carret Speed</c> here rather than at
    /// load time, so a slider's step is fixed once when the screen is built.
    /// </remarks>
    protected override void OnPrepare()
    {
        Step = CarretSpeed.Length > 0 ? (float)Evaluate(CarretSpeed) : DefaultStep;

        if (SpriteResource.Length > 0)
        {
            LeftButton.SpriteResource = $"{SpriteResource}_1.spr";
            RightButton.SpriteResource = $"{SpriteResource}_2.spr";
        }
    }

    /// <summary>Sizes the control from its two arrows, its bar length and its carret.</summary>
    /// <remarks>
    /// The tail of <c>SlideBar_Prepare</c>. Along the bar it is the two arrows plus the bar plus six
    /// pixels of padding; across it, four pixels plus the tallest of the three sprites.
    /// <para>
    /// One quirk is reproduced rather than tidied: the across-axis size falls back to bare padding —
    /// 4 — when no carret sprite is loaded, <em>ignoring the arrows entirely</em>. The original only
    /// consults the arrows inside the branch guarded by the carret being present.
    /// </para>
    /// </remarks>
    public void MeasureFromParts()
    {
        int along = 4;
        int across = 4;

        if (Horizontal)
        {
            along = LeftButton.LayoutWidth + Size + RightButton.LayoutWidth + 6;
            if (CarretWidth > 0 || CarretHeight > 0)
            {
                int arrows = Math.Max(LeftButton.LayoutHeight, RightButton.LayoutHeight);
                across = (arrows <= CarretHeight ? CarretHeight : arrows) + 4;
            }

            LayoutWidth = along;
            LayoutHeight = across;
            return;
        }

        along = LeftButton.LayoutHeight + Size + RightButton.LayoutHeight + 6;
        if (CarretWidth > 0 || CarretHeight > 0)
        {
            int arrows = Math.Max(LeftButton.LayoutWidth, RightButton.LayoutWidth);
            across = (arrows <= CarretWidth ? CarretWidth : arrows) + 4;
        }

        LayoutWidth = across;
        LayoutHeight = along;
    }

    /// <summary>Places the two arrows and reports where the bar and the carret sit.</summary>
    /// <returns>The track origin and the carret's offset along it, in authored pixels.</returns>
    /// <remarks>
    /// <c>SlideBar_Render</c> (<c>0x41FCE0</c>) does this every frame, and the update hit-tests against
    /// the same rectangle — so the arithmetic is here rather than in a renderer, where the update could
    /// not reach it.
    /// </remarks>
    public (int TrackX, int TrackY, int CarretX, int CarretY) PlaceParts()
    {
        int x = LayoutX + 2;
        int y = LayoutY + 2;

        LeftButton.LayoutX = x;
        LeftButton.LayoutY = y;

        if (Horizontal)
        {
            x += LeftButton.LayoutWidth + 1;
            RightButton.LayoutX = Size + x + 1;
            RightButton.LayoutY = y;
            return (x, y, x + (int)((Size - CarretWidth) * Value), y);
        }

        y += LeftButton.LayoutHeight + 1;
        RightButton.LayoutX = x;
        RightButton.LayoutY = Size + y + 1;
        return (x, y, x, y + (int)((Size - CarretHeight) * Value));
    }
}
