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
    /// Chosen by <c>Get Active Picture</c> out of the set <c>Collect Items</c> built. Both are filled
    /// by <c>Config_FillMissionPictureBox</c>, which needs mission data that does not exist yet — so
    /// this stays empty and the box draws nothing rather than a stand-in.
    /// </remarks>
    public string Picture { get; set; } = string.Empty;

    /// <inheritdoc/>
    protected override void OnLoadData()
    {
        Run(CollectItems);
        Run(GetActivePicture);
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
    /// block inline. Filled by <c>Config_FillMissionTextBox</c>, which is mission data and not yet
    /// available.
    /// </remarks>
    public IList<string> Lines { get; } = [];

    /// <inheritdoc/>
    protected override void OnLoadData()
    {
        Lines.Clear();
        Run(CollectItems);
        Run(GetActivePicture);
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

    /// <inheritdoc/>
    /// <remarks>Scrolling is what a reader does with it, so it takes the selection.</remarks>
    public override bool IsInteractive => true;

    /// <inheritdoc/>
    /// <remarks>
    /// Up and down scroll rather than moving the selection away, which is the behaviour a text
    /// viewer needs. The command is cleared, as <c>MenuText</c>'s is.
    /// </remarks>
    protected override void OnUpdate()
    {
        if (PendingCommand == MenuWindowCommand.Left && ScrollOffset > 0)
        {
            ScrollOffset--;
        }
        else if (PendingCommand == MenuWindowCommand.Right && ScrollOffset < Math.Max(0, Lines.Count - 1))
        {
            ScrollOffset++;
        }

        PendingCommand = MenuWindowCommand.None;
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

    /// <inheritdoc/>
    /// <remarks>
    /// One character per tick. The original's rate comes from a field this repo has not read, so the
    /// step is a stated assumption rather than a transcription — it is listed in the re-note, and
    /// nothing in the shipped menus animates long enough for the difference to show.
    /// </remarks>
    protected override void OnUpdate()
    {
        Advance();
        base.OnUpdate();
    }

    /// <summary>Reveals the next character.</summary>
    public void Advance()
    {
        if (Revealed < Text.Length)
        {
            Revealed++;
        }
    }
}
