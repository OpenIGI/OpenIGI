namespace OpenIGI.Game.Menus;

/// <summary>
/// The single-line text field: the only control the player types into.
/// </summary>
/// <remarks>
/// <para>
/// From <c>InputBox_Construct</c> (<c>0x41D290</c>), <c>InputBox_Update</c> (<c>0x41D450</c>) and
/// <c>InputBox_ExportVars</c> (<c>0x41DB30</c>). The instance is 424 bytes, of which the text buffer
/// at <c>+84</c> is forty — so a name is truncated at thirty-nine characters however long the
/// <c>String Length</c> parameter says.
/// </para>
/// <para>
/// Its constructor is the only one of the six that sets <c>+76</c> — the interactive flag — directly,
/// rather than leaving it to a parameter. A field is always clickable.
/// </para>
/// </remarks>
public partial class InputBoxTask
{
    /// <summary>Initializes a new instance of the <see cref="InputBoxTask"/> class.</summary>
    /// <remarks>
    /// <c>InputBox_Construct</c>: fifteen pixels tall, both centre flags, and the interactive flag set
    /// outright. The two remaining fields it writes — <c>+416 = 4</c> and <c>+420 = 20</c> — are the
    /// caret's blink period and its width; both are drawn-only and are named here rather than guessed
    /// at elsewhere.
    /// </remarks>
    public InputBoxTask()
    {
        LayoutHeight = DefaultHeight;
        CentreHorizontally = true;
        CentreVertically = true;
    }

    /// <summary>The field's height in pixels, which the constructor fixes.</summary>
    public const int DefaultHeight = 15;

    /// <summary>The blink period the constructor writes to <c>+416</c>, in ticks.</summary>
    public const int CaretBlinkTicks = 4;

    /// <summary>The caret's width in pixels, from <c>+420</c>.</summary>
    public const int CaretWidth = 20;

    /// <summary>The character a hidden field shows instead of what was typed.</summary>
    /// <remarks>The content-control password field sets <c>Hide Input</c>.</remarks>
    public const char MaskCharacter = '*';

    /// <summary>Gets or sets the caret's position within the text.</summary>
    public int Caret { get; set; }

    /// <summary>Gets how many characters the field will hold.</summary>
    /// <remarks>
    /// The smaller of the script's <c>String Length</c> and the buffer's own capacity, because the
    /// buffer is what actually truncates. A script asking for more than thirty-nine gets thirty-nine.
    /// </remarks>
    public int Capacity =>
        StringLength > 0 ? Math.Min(StringLength, DataCapacity - 1) : DataCapacity - 1;

    /// <summary>Gets the text as it should be drawn, masked when the field hides its input.</summary>
    public string VisibleText => HideInput ? new string(MaskCharacter, Data.Length) : Data;

    /// <summary>Appends a typed character, if there is room.</summary>
    /// <param name="character">The character.</param>
    /// <returns><see langword="true"/> when the text changed.</returns>
    /// <remarks>
    /// Silently refuses once the buffer is full, which is what a bounded <c>strncpy</c> into a
    /// forty-byte field amounts to. Runs <c>On Modify Script</c> on every accepted edit.
    /// </remarks>
    public bool TypeCharacter(char character)
    {
        if (Data.Length >= Capacity)
        {
            return false;
        }

        int at = Math.Clamp(Caret, 0, Data.Length);
        Data = Data.Insert(at, character.ToString());
        Caret = at + 1;
        Run(OnModifyScript);
        return true;
    }

    /// <summary>Deletes the character before the caret.</summary>
    /// <returns><see langword="true"/> when the text changed.</returns>
    public bool Backspace()
    {
        int at = Math.Clamp(Caret, 0, Data.Length);
        if (at == 0)
        {
            return false;
        }

        Data = Data.Remove(at - 1, 1);
        Caret = at - 1;
        Run(OnModifyScript);
        return true;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Left and right move the caret rather than doing anything to a neighbouring control, which is
    /// why a field must consume them. The command is cleared, as <c>MenuText</c>'s is.
    /// </remarks>
    protected override void OnUpdate()
    {
        if (PendingCommand == MenuWindowCommand.Left)
        {
            Caret = Math.Max(0, Caret - 1);
        }
        else if (PendingCommand == MenuWindowCommand.Right)
        {
            Caret = Math.Min(Data.Length, Caret + 1);
        }

        PendingCommand = MenuWindowCommand.None;
        base.OnUpdate();
    }

    /// <inheritdoc/>
    protected override void OnLoadData()
    {
        if (GetDataScript.Length > 0)
        {
            Run(GetDataScript);
        }

        Caret = Data.Length;
    }

    /// <inheritdoc/>
    protected override void OnSaveData() => Run(SetDataScript);
}
