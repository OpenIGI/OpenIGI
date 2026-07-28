namespace OpenIGI.Game.Menus;

/// <summary>
/// The six remaining control types' constructors, data wiring and content.
/// </summary>
/// <remarks>
/// <para>
/// Grouped because what they have in common is what matters: <b>every one of their constructors sets
/// both centre flags</b>, and several fix a default size the script never mentions. That is not a
/// detail — a control placed by a <c>HorizontalFillOut</c> frame takes its vertical position from its
/// own centring, so an untranscribed constructor leaves it at the top of the display. It cost a
/// debugging session on <c>ListBox</c> before it was understood, and it is the reason these were read
/// constructor-first.
/// </para>
/// <para>
/// Sizes and parameter counts, from the registrars: <c>PictureBox</c> (<c>0x41B310</c>) 2556 bytes and
/// 4; <c>TextBox</c> (<c>0x41AAF0</c>) 8772 and 5; <c>InputBox</c> (<c>0x41D080</c>) 424 and 6;
/// <c>AsciiBox</c> (<c>0x41C700</c>) 248 and 4; <c>ScrollListBox</c> (<c>0x41B8E0</c>) 5116 and 7;
/// <c>TypeWriterBox</c> (<c>0x41A1E0</c>) 136 and 2.
/// </para>
/// </remarks>
internal static class MenuBoxTasks
{
}

/// <summary>A picture chosen from a collected set by an expression.</summary>
public partial class PictureBoxTask
{
    /// <summary>Initializes a new instance of the <see cref="PictureBoxTask"/> class.</summary>
    /// <remarks>
    /// <c>PictureBox_Construct</c> (<c>0x41B490</c>): centred on the display at (320, 240), 256 by 192,
    /// with both centre flags. The size is a 4:3 box the mission shots are authored to.
    /// </remarks>
    public PictureBoxTask()
    {
        LayoutX = 320;
        LayoutY = 240;
        LayoutWidth = 256;
        LayoutHeight = 192;
        CentreHorizontally = true;
        CentreVertically = true;
    }

    /// <summary>Gets or sets the picture the box is showing, or an empty string.</summary>
    /// <remarks>
    /// Chosen by <c>Get Active Picture</c> out of the set <c>Collect Items</c> built:
    /// <c>Config_FillMissionPictureBox</c> adds one entry per mission keyed by mission id, and the
    /// expression yields the id the mission list is sitting on.
    /// </remarks>
    public string Picture { get; set; } = string.Empty;

    /// <summary>Gets the collected pictures, keyed by the id <see cref="GetActivePicture"/> yields.</summary>
    public IDictionary<int, string> Entries { get; } = new Dictionary<int, string>();

    /// <inheritdoc/>
    protected override void OnLoadData()
    {
        Entries.Clear();
        Run(CollectItems);

        // The original collects every entry and then picks one by id, rather than asking the filler
        // for the selected one — which is why the fill natives walk the whole mission chain.
        int active = (int)Evaluate(GetActivePicture);
        Picture = Entries.TryGetValue(active, out string? picture) ? picture : string.Empty;
    }
}

/// <summary>A block of text collected by an expression.</summary>
public partial class TextBoxTask
{
    /// <summary>Initializes a new instance of the <see cref="TextBoxTask"/> class.</summary>
    /// <remarks>
    /// <c>TextBox_Construct</c> (<c>0x41ACA0</c>): 128 by 128 at (300, 230) — the same position a
    /// <c>MenuText</c> starts at — with both centre flags.
    /// </remarks>
    public TextBoxTask()
    {
        LayoutX = 300;
        LayoutY = 230;
        LayoutWidth = 128;
        LayoutHeight = 128;
        CentreHorizontally = true;
        CentreVertically = true;
    }

    /// <summary>Gets the lines the box is showing.</summary>
    /// <remarks>
    /// The instance is 8772 bytes, nearly all of it the text buffer, so the original holds the whole
    /// block inline. Filled by <c>Config_FillMissionTextBox</c>, one entry per mission keyed by id.
    /// </remarks>
    public IList<string> Lines { get; } = [];

    /// <summary>Gets the collected blocks, keyed by the id <see cref="GetActivePicture"/> yields.</summary>
    public IDictionary<int, string> Entries { get; } = new Dictionary<int, string>();

    /// <inheritdoc/>
    protected override void OnLoadData()
    {
        Lines.Clear();
        Entries.Clear();
        Run(CollectItems);

        int active = (int)Evaluate(GetActivePicture);
        if (Entries.TryGetValue(active, out string? text) && text.Length > 0)
        {
            foreach (string line in text.Split('\n'))
            {
                Lines.Add(line.TrimEnd('\r'));
            }
        }
    }
}

/// <summary>A scrolling view of a plain text file.</summary>
public partial class AsciiBoxTask
{
    /// <summary>Initializes a new instance of the <see cref="AsciiBoxTask"/> class.</summary>
    /// <remarks><c>AsciiBox_Construct</c> (<c>0x41C8D0</c>): 100 by 100, both centre flags.</remarks>
    public AsciiBoxTask()
    {
        LayoutWidth = 100;
        LayoutHeight = 100;
        CentreHorizontally = true;
        CentreVertically = true;
    }

    /// <summary>Gets or sets the first visible line.</summary>
    /// <remarks>Field <c>+212</c>. The readme screen is what uses this.</remarks>
    public int ScrollOffset { get; set; }

    /// <summary>Gets the lines read out of <see cref="AsciiBoxTask.TextFile"/>.</summary>
    public IList<string> Lines { get; } = [];

    /// <summary>Gets the scrollbar the box owns.</summary>
    /// <remarks>
    /// <b>Correction:</b> an earlier revision had this scrolling on left and right like a list. It
    /// does not. <c>AsciiBox_Update</c> (<c>0x41CD20</c>) reads <c>SlideBar_GetIndex</c> on the
    /// slider it holds at <c>+244</c> and walks its line pointer to match — so the bar is the
    /// control and the box just follows it.
    /// </remarks>
    public SlideBarTask ScrollBar { get; } = new() { Horizontal = false };

    /// <inheritdoc/>
    public override IEnumerable<MenuWindowTask> OwnedWindows => [ScrollBar];

    /// <inheritdoc/>
    /// <remarks>The bar takes the selection; the box itself is not clickable.</remarks>
    public override bool IsInteractive => false;

    /// <inheritdoc/>
    /// <remarks>
    /// The offset follows the bar's option index, one stop per line. The original walks a pointer
    /// through the NUL-terminated lines one at a time in whichever direction it has to move; the
    /// result is the same line, and the walk is only there because it has no line array to index.
    /// </remarks>
    protected override void OnUpdate()
    {
        ScrollBar.NumOptions = Math.Max(1, Lines.Count);
        ScrollOffset = Math.Clamp(ScrollBar.GetIndex(), 0, Math.Max(0, Lines.Count - 1));
        base.OnUpdate();
    }
}

/// <summary>A scrolling list whose contents come from an expression.</summary>
public partial class ScrollListBoxTask
{
    /// <summary>Initializes a new instance of the <see cref="ScrollListBoxTask"/> class.</summary>
    /// <remarks><c>ScrollListBox_Construct</c> (<c>0x41BB10</c>): 100 by 100, both centre flags.</remarks>
    public ScrollListBoxTask()
    {
        LayoutWidth = 100;
        LayoutHeight = 100;
        CentreHorizontally = true;
        CentreVertically = true;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// The mission-select screen's list, so it both takes the selection and acts on up and down —
    /// which is what makes it a scrolling list rather than a spinner like <see cref="ListBoxTask"/>.
    /// </remarks>
    protected override void OnUpdate()
    {
        if (Items.Count > 0)
        {
            if (PendingCommand == MenuWindowCommand.Left && Index > 0)
            {
                Index--;
            }
            else if (PendingCommand == MenuWindowCommand.Right && Index < Items.Count - 1)
            {
                Index++;
            }

            ActiveId = Items[Index].Id;
        }

        PendingCommand = MenuWindowCommand.None;
        base.OnUpdate();
    }
}

/// <summary>A box that reveals its text one character at a time.</summary>
public partial class TypeWriterBoxTask
{
    /// <summary>Initializes a new instance of the <see cref="TypeWriterBoxTask"/> class.</summary>
    /// <remarks><c>TypeWriterBox_Construct</c> (<c>0x41A370</c>): 100 by 100, both centre flags.</remarks>
    public TypeWriterBoxTask()
    {
        LayoutWidth = 100;
        LayoutHeight = 100;
        CentreHorizontally = true;
        CentreVertically = true;
    }

    /// <summary>Gets or sets the whole text, of which a prefix is shown.</summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>Gets or sets how many characters have been revealed.</summary>
    public int Revealed { get; set; }

    /// <summary>Gets the text as it should be drawn this tick.</summary>
    public string VisibleText => Text.Length == 0 ? string.Empty : Text[..Math.Clamp(Revealed, 0, Text.Length)];

    /// <summary>The ticks between one revealed character and the next.</summary>
    /// <remarks>
    /// <b>Correction:</b> an earlier revision assumed one character per tick and said so. It is one
    /// every three — <c>TypeWriterBox_Update</c> (<c>0x41A600</c>) reloads its countdown at
    /// <c>+108</c> with 3 after each character, and only reveals when the countdown has run out.
    /// </remarks>
    public const int TicksPerCharacter = 3;

    /// <summary>Gets or sets the ticks left before the next character appears.</summary>
    public int Countdown { get; set; }

    /// <inheritdoc/>
    protected override void OnUpdate()
    {
        Advance();
        base.OnUpdate();
    }

    /// <summary>Counts down, and reveals the next character when the count runs out.</summary>
    public void Advance()
    {
        if (Countdown > 0)
        {
            Countdown--;
            return;
        }

        if (Revealed < Text.Length)
        {
            Revealed++;
            Countdown = TicksPerCharacter;
        }
    }
}
