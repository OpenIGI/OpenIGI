using OpenIGI.Core.Configuration;
using OpenIGI.Input;
using OpenIGI.UI.Focus;

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

    private float _blink;

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
        if (keyboard.WasPressed(KeyCode.A))
        {
            model.SelectAll();
            return true;
        }

        // A scrollback selection wins over the prompt's, because it is the one the reader can see and
        // the one they just made with the mouse. The prompt's own selection is usually empty anyway.
        if (keyboard.WasPressed(KeyCode.C) && body is { } shown && shown.Selection.IsActive)
        {
            WriteClipboard?.Invoke(shown.SelectedText());
            return true;
        }

        if (keyboard.WasPressed(KeyCode.C) && model.Edit.HasSelection)
        {
            WriteClipboard?.Invoke(model.Edit.SelectedText);
            return true;
        }

        if (keyboard.WasPressed(KeyCode.X) && model.Edit.HasSelection)
        {
            WriteClipboard?.Invoke(model.Edit.SelectedText);
            model.Backspace();
            return true;
        }

        if (keyboard.WasPressed(KeyCode.V))
        {
            if (ReadClipboard?.Invoke() is { Length: > 0 } pasted)
            {
                model.Insert(pasted);
            }

            return true;
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
    /// <returns><see langword="true"/> when anything changed.</returns>
    /// <remarks>
    /// Separate from <see cref="EditText"/> because the prompt is not a plain field: every edit has to
    /// refresh the completion list, which only the model can do.
    /// </remarks>
    public bool EditPrompt(ConsoleModel model, KeyboardState keyboard, float elapsed, bool control, bool shift)
    {
        bool changed = false;

        if (_backspace.Poll(keyboard, KeyCode.Backspace, elapsed))
        {
            model.Backspace();
            changed = true;
        }

        if (_delete.Poll(keyboard, KeyCode.Delete, elapsed))
        {
            model.Delete();
            changed = true;
        }

        if (_left.Poll(keyboard, KeyCode.Left, elapsed))
        {
            if (control)
            {
                model.MoveCaretByWord(-1, shift);
            }
            else
            {
                model.MoveCaret(-1, shift);
            }

            changed = true;
        }

        if (_right.Poll(keyboard, KeyCode.Right, elapsed))
        {
            if (control)
            {
                model.MoveCaretByWord(1, shift);
            }
            else
            {
                model.MoveCaret(1, shift);
            }

            changed = true;
        }

        if (keyboard.WasPressed(KeyCode.Home))
        {
            model.CaretToStart(shift);
            changed = true;
        }

        if (keyboard.WasPressed(KeyCode.End))
        {
            model.CaretToEnd(shift);
            changed = true;
        }

        while (keyboard.TryDequeueCharacter(out char typed))
        {
            if (IsPrintable(typed))
            {
                model.Insert(typed.ToString());
                changed = true;
            }
        }

        return changed;
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
        bool changed = false;

        if (control && keyboard.WasPressed(KeyCode.A))
        {
            edit.SelectAll();
        }

        if (control && keyboard.WasPressed(KeyCode.V) && ReadClipboard?.Invoke() is { Length: > 0 } pasted)
        {
            changed |= edit.Insert(pasted);
        }

        if (_backspace.Poll(keyboard, KeyCode.Backspace, elapsed))
        {
            changed |= edit.Backspace();
        }

        if (_delete.Poll(keyboard, KeyCode.Delete, elapsed))
        {
            changed |= edit.Delete();
        }

        if (_left.Poll(keyboard, KeyCode.Left, elapsed))
        {
            edit.Move(-1, shift);
        }

        if (_right.Poll(keyboard, KeyCode.Right, elapsed))
        {
            edit.Move(1, shift);
        }

        if (keyboard.WasPressed(KeyCode.Home))
        {
            edit.MoveToStart(shift);
        }

        if (keyboard.WasPressed(KeyCode.End))
        {
            edit.MoveToEnd(shift);
        }

        while (keyboard.TryDequeueCharacter(out char typed))
        {
            if (IsPrintable(typed))
            {
                changed |= edit.Insert(typed.ToString());
            }
        }

        if (changed)
        {
            ResetBlink();
        }

        return changed;
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
}
