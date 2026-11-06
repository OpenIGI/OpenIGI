using OpenIGI.Input;
using OpenIGI.UI.Editor.Input;
using OpenIGI.UI.Editor.Widgets;
using OpenIGI.UI.Layout;
using Xunit;

namespace OpenIGI.UI.Tests.Editor;

/// <summary>The code editor, driven like a user: typing, indenting, selecting, the clipboard and its own undo.</summary>
public sealed class CodeEditorTests
{
    private static (EditorHarness Ui, CodeEditor Code) Build(string text = "")
    {
        EditorHarness ui = new();
        CodeEditor code = new(ui.Theme) { Text = text };
        ui.Root.Content = code;
        ui.Frame();
        ui.Click(new UiVec2(code.Bounds.Right - 40f, code.Bounds.Bottom - 10f));
        return (ui, code);
    }

    /// <summary>Enter keeps the indent and adds one after an opening brace; tabs become spaces.</summary>
    [Fact]
    public void Enter_indents_like_the_line_above()
    {
        (EditorHarness ui, CodeEditor code) = Build("if (a)\n\t{");
        ui.Key(KeyCode.End, KeyModifiers.Control);
        ui.Key(KeyCode.Return);
        ui.Type("F();");
        Assert.Equal("if (a)\n    {\n        F();", code.Text);
    }

    /// <summary>A selection across lines is replaced by what is typed, and undo and redo walk the edits.</summary>
    [Fact]
    public void Typing_replaces_the_selection_and_undoes()
    {
        (EditorHarness ui, CodeEditor code) = Build("one\ntwo\nthree");
        ui.Key(KeyCode.Home, KeyModifiers.Control);
        ui.Key(KeyCode.Down, KeyModifiers.Shift);
        ui.Key(KeyCode.Right, KeyModifiers.Shift);
        ui.Type("X");
        Assert.Equal("Xwo\nthree", code.Text);

        ui.Key(KeyCode.Z, KeyModifiers.Control);
        Assert.Equal("one\ntwo\nthree", code.Text);
        ui.Key(KeyCode.Y, KeyModifiers.Control);
        Assert.Equal("Xwo\nthree", code.Text);
    }

    /// <summary>Copy and paste go through the root's clipboard, line breaks included.</summary>
    [Fact]
    public void The_clipboard_carries_lines()
    {
        (EditorHarness ui, CodeEditor code) = Build("a\nb");
        ui.Key(KeyCode.A, KeyModifiers.Control);
        ui.Key(KeyCode.C, KeyModifiers.Control);
        Assert.Equal("a\nb", ui.Root.Clipboard.Text);
        ui.Key(KeyCode.End, KeyModifiers.Control);
        ui.Key(KeyCode.V, KeyModifiers.Control);
        Assert.Equal("a\nba\nb", code.Text);
        Assert.Equal(2, code.CaretLine);
    }

    /// <summary>Ctrl+arrows move by words, and Ctrl+Backspace deletes one.</summary>
    [Fact]
    public void Words_move_and_delete()
    {
        (EditorHarness ui, CodeEditor code) = Build("Task_New(-1, \"Box\")");
        ui.Key(KeyCode.Home, KeyModifiers.Control);
        ui.Key(KeyCode.Right, KeyModifiers.Control);
        Assert.Equal(10, code.CaretColumn);
        ui.Key(KeyCode.End);
        ui.Key(KeyCode.Backspace, KeyModifiers.Control);
        Assert.Equal("Task_New(-1, \"", code.Text);
    }

    /// <summary>A read-only editor still moves and copies but never changes.</summary>
    [Fact]
    public void Read_only_refuses_edits()
    {
        (EditorHarness ui, CodeEditor code) = Build("x");
        code.IsReadOnly = true;
        ui.Type("y");
        ui.Key(KeyCode.Backspace);
        Assert.Equal("x", code.Text);
    }
}
