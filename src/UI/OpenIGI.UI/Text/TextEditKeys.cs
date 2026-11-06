using OpenIGI.Core.Configuration;
using OpenIGI.Input;

namespace OpenIGI.UI.Text;

/// <summary>Reads and writes the system clipboard's text.</summary>
public interface IUiClipboard
{
    /// <summary>Gets or sets the clipboard's text; empty when it holds none.</summary>
    string Text { get; set; }
}

/// <summary>A clipboard that lives only in memory, for tests and hosts without a system one.</summary>
public sealed class MemoryClipboard : IUiClipboard
{
    /// <inheritdoc/>
    public string Text { get; set; } = string.Empty;
}

/// <summary>
/// The editing keys of a single-line text field, applied to a <see cref="TextEditState"/>: one key map for
/// every field in the program, the console's prompt and the editor's fields alike.
/// </summary>
public static class TextEditKeys
{
    /// <summary>Applies a key to a field.</summary>
    /// <param name="edit">The field's state.</param>
    /// <param name="key">The key pressed (or repeated).</param>
    /// <param name="control">Whether Control is held: words for the arrows and deletes, the clipboard for C, X and V.</param>
    /// <param name="shift">Whether Shift is held: the caret moves extend the selection.</param>
    /// <param name="clipboard">The clipboard, or <see langword="null"/> when copy and paste are not offered.</param>
    /// <param name="changed">Receives whether the text changed.</param>
    /// <returns>Whether the key is an editing key and was used.</returns>
    public static bool Apply(TextEditState edit, KeyCode key, bool control, bool shift, IUiClipboard? clipboard, out bool changed)
    {
        ArgumentNullException.ThrowIfNull(edit);
        changed = false;
        switch (key)
        {
            case KeyCode.Left when control:
                edit.MoveByWord(-1, shift);
                return true;
            case KeyCode.Right when control:
                edit.MoveByWord(1, shift);
                return true;
            case KeyCode.Left:
                edit.Move(-1, shift);
                return true;
            case KeyCode.Right:
                edit.Move(1, shift);
                return true;
            case KeyCode.Home:
                edit.MoveToStart(shift);
                return true;
            case KeyCode.End:
                edit.MoveToEnd(shift);
                return true;
            case KeyCode.Backspace when control && !edit.HasSelection:
                edit.MoveByWord(-1, extend: true);
                changed = edit.DeleteSelection();
                return true;
            case KeyCode.Delete when control && !edit.HasSelection:
                edit.MoveByWord(1, extend: true);
                changed = edit.DeleteSelection();
                return true;
            case KeyCode.Backspace:
                changed = edit.Backspace();
                return true;
            case KeyCode.Delete:
                changed = edit.Delete();
                return true;
            case KeyCode.A when control:
                edit.SelectAll();
                return true;
            case KeyCode.C when control && clipboard is not null:
                if (edit.HasSelection)
                {
                    clipboard.Text = edit.SelectedText;
                }

                return true;
            case KeyCode.X when control && clipboard is not null:
                if (edit.HasSelection)
                {
                    clipboard.Text = edit.SelectedText;
                    changed = edit.DeleteSelection();
                }

                return true;
            case KeyCode.V when control && clipboard is not null:
                changed = clipboard.Text is { Length: > 0 } pasted && edit.Insert(SingleLine(pasted));
                return true;
            default:
                return false;
        }
    }

    /// <summary>Gets whether a decoded character belongs in a single-line field.</summary>
    /// <param name="c">The character.</param>
    /// <returns><see langword="false"/> for control characters and line breaks.</returns>
    public static bool IsPrintable(char c) => !char.IsControl(c);

    private static string SingleLine(string text) =>
        text.Contains('\n', StringComparison.Ordinal) || text.Contains('\r', StringComparison.Ordinal)
            ? text.ReplaceLineEndings(" ")
            : text;
}
