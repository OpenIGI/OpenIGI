using OpenIGI.Core.Configuration;
using OpenIGI.Input;
using OpenIGI.UI.Focus;
using OpenIGI.UI.Text;

namespace OpenIGI.Host.DevTools;

/// <summary>
/// The developer console's typing: the held-key repeaters, the blinking caret, the clipboard, and the
/// edits every panel's text field shares.
/// </summary>
/// <remarks>
/// <para>
/// One instance serves every panel, and that is deliberate rather than convenient. The repeaters keep
/// their own timing, so the arrow a reader is holding goes on repeating at the same cadence whichever
/// panel happens to be in front when the frame reads it — exactly as it did when they all lived on the
/// console itself.
/// </para>
/// <para>
/// The caret blinks here because typing is what resets it: any edit restarts the cycle with the caret
/// showing, so it never vanishes in the middle of a word.
/// </para>
/// </remarks>
internal sealed class PromptEditor
{
    private const float BlinkPeriod = 0.53f;

    private readonly KeyRepeater _backspace = new();
    private readonly KeyRepeater _delete = new();
    private readonly KeyRepeater _left = new();
    private readonly KeyRepeater _right = new();

    private readonly DelegateClipboard _clipboard;
    private float _blink;

    /// <summary>Initializes a new instance of the <see cref="PromptEditor"/> class.</summary>
    public PromptEditor() => _clipboard = new DelegateClipboard(this);

    /// <summary>Gets the repeater for the up arrow, shared by every panel's list.</summary>
    public KeyRepeater Up { get; } = new();

    /// <summary>Gets the repeater for the down arrow, shared by every panel's list.</summary>
    public KeyRepeater Down { get; } = new();

    /// <summary>Gets the repeater for page-up in the scrollback.</summary>
    public KeyRepeater PageUp { get; } = new();

    /// <summary>Gets the repeater for page-down in the scrollback.</summary>
    public KeyRepeater PageDown { get; } = new();

    /// <summary>Gets a value indicating whether the caret is in the shown half of its blink.</summary>
    public bool CaretVisible { get; private set; } = true;

    /// <summary>Gets or sets the clipboard reader, so a paste can reach the system clipboard.</summary>
    public Func<string>? ReadClipboard { get; set; }

    /// <summary>Gets or sets the clipboard writer.</summary>
    public Action<string>? WriteClipboard { get; set; }

    /// <summary>Advances the caret's blink by a frame.</summary>
    /// <param name="elapsedSeconds">Real seconds since the last call.</param>
    public void AdvanceCaret(float elapsedSeconds)
    {
        _blink += elapsedSeconds;
        while (_blink >= BlinkPeriod)
        {
            _blink -= BlinkPeriod;
            CaretVisible = !CaretVisible;
        }
    }

    /// <summary>Restarts the blink with the caret showing.</summary>
    public void ResetBlink()
    {
        _blink = 0f;
        CaretVisible = true;
    }

    /// <summary>Puts the clipboard's text into the prompt as one line.</summary>
    /// <param name="model">The console whose prompt receives it.</param>
    public void PasteFromClipboard(ConsoleModel model)
    {
        if (ReadClipboard?.Invoke() is { Length: > 0 } pasted)
        {
            model.Insert(pasted.ReplaceLineEndings(" ").Trim());
        }
    }

    /// <summary>Applies the control-key clipboard chords to the prompt and the scrollback.</summary>
    /// <param name="model">The console whose prompt is edited.</param>
    /// <param name="body">The console panel, whose scrollback selection may be copied.</param>
    /// <param name="keyboard">The frame's keyboard.</param>
    /// <returns><see langword="true"/> when a chord was handled.</returns>
    public bool HandleClipboardKeys(ConsoleModel model, ConsoleBody? body, KeyboardState keyboard)
    {
        // A scrollback selection wins over the prompt's, because it is the one the reader can see and
        // the one they just made with the mouse. The prompt's own selection is usually empty anyway.
        if (keyboard.WasPressed(KeyCode.C) && body is { } shown && shown.Selection.IsActive)
        {
            WriteClipboard?.Invoke(shown.SelectedText());
            return true;
        }

        if (keyboard.WasPressed(KeyCode.A))
        {
            model.SelectAll();
            return true;
        }

        foreach (KeyCode chord in ClipboardChords)
        {
            if (keyboard.WasPressed(chord))
            {
                model.EditWith(edit => ApplyKey(edit, chord, control: true, shift: false).Changed);
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Applies editing keys and typed text to the console's prompt.
    /// </summary>
    /// <param name="model">The console whose prompt is edited.</param>
    /// <param name="keyboard">The frame's keyboard.</param>
    /// <param name="elapsed">Real seconds since the last call.</param>
    /// <param name="control">Whether a control key is held.</param>
    /// <param name="shift">Whether a shift key is held.</param>
    /// <returns><see langword="true"/> when anything changed, the caret included.</returns>
    /// <remarks>
    /// The prompt is edited through the model so every change refreshes the completion list; the clipboard
    /// chords are <see cref="HandleClipboardKeys"/>'s, so they are left out here.
    /// </remarks>
    public bool EditPrompt(ConsoleModel model, KeyboardState keyboard, float elapsed, bool control, bool shift)
    {
        bool used = false;
        model.EditWith(edit =>
        {
            (bool anyUsed, bool changed) = ApplyKeys(edit, keyboard, elapsed, control, shift, clipboardChords: false);
            used = anyUsed;
            return changed;
        });

        return used;
    }

    /// <summary>Applies editing keys and typed text to a plain field.</summary>
    /// <param name="edit">The field being edited.</param>
    /// <param name="keyboard">The frame's keyboard.</param>
    /// <param name="elapsed">Real seconds since the last call.</param>
    /// <param name="control">Whether a control key is held.</param>
    /// <param name="shift">Whether a shift key is held.</param>
    /// <returns><see langword="true"/> when the text changed.</returns>
    public bool EditText(TextEditState edit, KeyboardState keyboard, float elapsed, bool control, bool shift)
    {
        bool changed = ApplyKeys(edit, keyboard, elapsed, control, shift, clipboardChords: true).Changed;
        if (changed)
        {
            ResetBlink();
        }

        return changed;
    }

    private static readonly KeyCode[] ClipboardChords = [KeyCode.C, KeyCode.X, KeyCode.V];

    // The one key map every field shares (TextEditKeys); the console only decides which keys repeat and reads
    // them off the frame's keyboard rather than from key events.
    private (bool Used, bool Changed) ApplyKeys(
        TextEditState edit, KeyboardState keyboard, float elapsed, bool control, bool shift, bool clipboardChords)
    {
        bool used = false;
        bool changed = false;
        void Key(KeyCode key, bool pressed)
        {
            if (pressed)
            {
                (bool keyUsed, bool keyChanged) = ApplyKey(edit, key, control, shift);
                used |= keyUsed;
                changed |= keyChanged;
            }
        }

        Key(KeyCode.Backspace, _backspace.Poll(keyboard, KeyCode.Backspace, elapsed));
        Key(KeyCode.Delete, _delete.Poll(keyboard, KeyCode.Delete, elapsed));
        Key(KeyCode.Left, _left.Poll(keyboard, KeyCode.Left, elapsed));
        Key(KeyCode.Right, _right.Poll(keyboard, KeyCode.Right, elapsed));
        Key(KeyCode.Home, keyboard.WasPressed(KeyCode.Home));
        Key(KeyCode.End, keyboard.WasPressed(KeyCode.End));
        if (control && clipboardChords)
        {
            Key(KeyCode.A, keyboard.WasPressed(KeyCode.A));
            Key(KeyCode.V, keyboard.WasPressed(KeyCode.V));
        }

        while (keyboard.TryDequeueCharacter(out char typed))
        {
            if (IsPrintable(typed) && edit.Insert(typed.ToString()))
            {
                used = true;
                changed = true;
            }
        }

        return (used, changed);
    }

    private (bool Used, bool Changed) ApplyKey(TextEditState edit, KeyCode key, bool control, bool shift)
    {
        bool used = TextEditKeys.Apply(edit, key, control, shift, _clipboard, out bool changed);
        return (used, changed);
    }

    /// <summary>
    /// Decides whether a decoded character belongs in a field.
    /// </summary>
    /// <param name="c">The character the platform decoded.</param>
    /// <returns><see langword="true"/> when it should be inserted.</returns>
    /// <remarks>
    /// The toggle key is excluded because the platform decodes text before the key event is seen, so
    /// opening the console would otherwise leave a backtick sitting in whatever had focus.
    /// </remarks>
    private static bool IsPrintable(char c) =>
        c is not ('`' or '~' or '\r' or '\n' or '\t' or '\b') && !char.IsControl(c);

    // The shared key map takes a clipboard object; the console's is the pair of delegates the host sets.
    private sealed class DelegateClipboard(PromptEditor owner) : IUiClipboard
    {
        public string Text
        {
            get => owner.ReadClipboard?.Invoke() ?? string.Empty;
            set => owner.WriteClipboard?.Invoke(value);
        }
    }
}
