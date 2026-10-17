
namespace OpenIGI.Game.Igi1.Menus;

/// <summary>
/// The single-line text field: the only control the player types into, and the only one that is modal.
/// </summary>
/// <remarks>
/// <para>
/// From <c>InputBox_Construct</c> (<c>0x41D290</c>) and <c>InputBox_Update</c> (<c>0x41D450</c>). The
/// instance is 424 bytes; the text buffer at <c>+84</c> is forty, so a value is truncated at
/// thirty-nine characters however long <c>String Length</c> says.
/// </para>
/// <para>
/// <b>It takes the keyboard.</b> Choosing a field — by click or by enter — grabs input from the
/// manager, and from then until enter or escape the field drains the raw event queue itself and every
/// other control sees nothing. That is why the field can use left, right, home, end and backspace for
/// editing without the menu navigating away underneath it.
/// </para>
/// </remarks>
public partial class InputBoxTask
{
    /// <summary>Initializes a new instance of the <see cref="InputBoxTask"/> class.</summary>
    /// <remarks>
    /// <c>InputBox_Construct</c>: fifteen pixels tall, both centre flags, the interactive flag set
    /// outright, and a default maximum length of twenty.
    /// </remarks>
    public InputBoxTask()
    {
        LayoutHeight = DefaultHeight;
        CentreHorizontally = true;
        CentreVertically = true;
        MaxLength = DefaultMaxLength;
    }

    /// <summary>The field's height in pixels, which the constructor fixes.</summary>
    public const int DefaultHeight = 15;

    /// <summary>The longest value the constructor allows before a script says otherwise.</summary>
    /// <remarks>Field <c>+420</c>, written as 20.</remarks>
    public const int DefaultMaxLength = 20;

    /// <summary>The character a hidden field shows instead of what was typed.</summary>
    public const char MaskCharacter = '*';

    /// <summary>The sound choosing a field plays.</summary>
    public const string ActivateSound = "menu_k01";

    /// <summary>The one character the field refuses.</summary>
    /// <remarks>
    /// Code 34, the double quote. The value ends up inside a string literal in <c>config.qvm</c>, and
    /// a quote there would break the file the next time it is read — so the field will not accept one
    /// at all.
    /// </remarks>
    public const char RejectedCharacter = '"';

    /// <summary>Gets or sets the caret's position within the text.</summary>
    /// <remarks>
    /// Field <c>+416</c>. <b>Correction:</b> an earlier revision called this the caret's blink period,
    /// having read the constructor's <c>+416 = 4</c> without reading the update. It is the caret.
    /// </remarks>
    public int Caret { get; set; }

    /// <summary>Gets or sets the longest value the field accepts.</summary>
    /// <remarks>
    /// Field <c>+420</c>. <b>Correction:</b> previously read as the caret's width. The check is
    /// <c>length &lt; max - 1</c>, so a field declaring twenty holds nineteen characters.
    /// </remarks>
    public int MaxLength { get; set; }

    /// <summary>Gets a value indicating whether the field currently owns the keyboard.</summary>
    /// <remarks>Field <c>+405</c>. While set, no other control sees any input at all.</remarks>
    public bool IsEditing { get; private set; }

    /// <summary>Gets how many characters the field will hold.</summary>
    /// <remarks>The smaller of the declared maximum and the buffer, because the buffer is what truncates.</remarks>
    public int Capacity => Math.Min(Math.Max(0, MaxLength - 1), DataCapacity - 1);

    /// <summary>Gets the text as it should be drawn, masked when the field hides its input.</summary>
    public string VisibleText => HideInput ? new string(MaskCharacter, Data.Length) : Data;

    /// <summary>Begins editing, as a click or an enter on the field does.</summary>
    /// <remarks>The caret goes to the end, and the field takes the keyboard.</remarks>
    public void BeginEditing()
    {
        IsEditing = true;
        Caret = Data.Length;
        Manager?.PlaySound(ActivateSound);
    }

    /// <summary>Stops editing and gives the keyboard back.</summary>
    /// <param name="accepted">Whether enter ended it, rather than escape.</param>
    /// <remarks>Only enter runs <c>On Modify Script</c>; escape leaves the text as edited but silent.</remarks>
    public void EndEditing(bool accepted)
    {
        IsEditing = false;

        if (accepted)
        {
            Run(OnModifyScript);
        }
    }

    /// <summary>Inserts a typed character at the caret, if there is room.</summary>
    /// <param name="character">The character.</param>
    /// <returns><see langword="true"/> when the text changed.</returns>
    public bool TypeCharacter(char character)
    {
        if (character == RejectedCharacter || char.IsControl(character) || Data.Length >= Capacity)
        {
            return false;
        }

        int at = Math.Clamp(Caret, 0, Data.Length);
        Data = Data.Insert(at, character.ToString());
        Caret = at + 1;
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
        return true;
    }

    /// <summary>Deletes the character at the caret.</summary>
    /// <returns><see langword="true"/> when the text changed.</returns>
    public bool Delete()
    {
        int at = Math.Clamp(Caret, 0, Data.Length);
        if (at >= Data.Length)
        {
            return false;
        }

        Data = Data.Remove(at, 1);
        return true;
    }

    /// <summary>Applies one editing key.</summary>
    /// <param name="input">The key, in the manager's abstract codes.</param>
    /// <remarks>
    /// The whole of <c>InputBox_Update</c>'s editing switch. Up and down are swallowed rather than
    /// acted on, which is what stops the selection moving out from under an open field.
    /// </remarks>
    public void Edit(MenuInputEvent input)
    {
        switch (input)
        {
            case MenuInputEvent.Left:
                Caret = Math.Max(0, Caret - 1);
                break;

            case MenuInputEvent.Right:
                Caret = Math.Min(Data.Length, Caret + 1);
                break;

            case MenuInputEvent.Home:
                Caret = 0;
                break;

            case MenuInputEvent.End:
                Caret = Data.Length;
                break;

            case MenuInputEvent.Backspace:
                Backspace();
                break;

            case MenuInputEvent.Delete:
                Delete();
                break;

            case MenuInputEvent.Enter:
                EndEditing(accepted: true);
                break;

            case MenuInputEvent.Escape:
                EndEditing(accepted: false);
                break;

            default:
                break;
        }
    }

    /// <inheritdoc/>
    /// <remarks>A click or an enter on a field that is not editing starts editing it.</remarks>
    protected override void OnUpdate()
    {
        if (!IsEditing && (WasClicked || PendingCommand == MenuWindowCommand.Choose))
        {
            BeginEditing();
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
