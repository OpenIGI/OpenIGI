using OpenIGI.Input;
using OpenIGI.UI.Editor.Input;
using OpenIGI.UI.Editor.Theme;
using OpenIGI.UI.Editor.Widgets;
using OpenIGI.UI.Layout;
using Xunit;

namespace OpenIGI.UI.Tests.Editor;

/// <summary>Dialogs: modal windows a press beside does not dismiss, which still let popups over them close as usual.</summary>
public sealed class DialogTests
{
    /// <summary>A press beside a dialog is taken and leaves it open; Escape closes it.</summary>
    [Fact]
    public void A_press_beside_a_dialog_leaves_it_open()
    {
        EditorHarness ui = new();
        ui.Frame();
        bool answered = false;
        Dialog dialog = Dialog.Ask(ui.Root, "Unsaved Changes", "Save first?", ("Cancel", () => { }), ("Save", () => answered = true));
        ui.Frame();

        ui.Click(new UiVec2(10f, 10f));
        Assert.Single(ui.Root.Popups);
        Assert.False(answered);

        ui.Key(KeyCode.Escape);
        Assert.Empty(ui.Root.Popups);
        _ = dialog;
    }

    /// <summary>An answer runs and closes the dialog.</summary>
    [Fact]
    public void An_answer_runs_and_closes()
    {
        EditorHarness ui = new();
        ui.Frame();
        string? chosen = null;
        Dialog dialog = Dialog.Ask(ui.Root, "Unsaved Changes", "Save first?", ("Cancel", () => chosen = "cancel"), ("Save", () => chosen = "save"));
        ui.Frame();

        // The primary answer sits at the dialog's foot, rightmost.
        UiRect frame = dialog.Bounds;
        ui.Click(new UiVec2(frame.Right - ui.Theme.Px(30f), frame.Bottom - ui.Theme.Px(12f) - (ui.Theme.ControlHeight * 0.5f)));
        Assert.Equal("save", chosen);
        Assert.Empty(ui.Root.Popups);
    }

    /// <summary>A press inside the dialog closes a popup opened over it (a dropdown's list) and keeps the dialog.</summary>
    [Fact]
    public void A_press_inside_closes_only_what_is_over_it()
    {
        EditorHarness ui = new();
        ui.Frame();
        Dialog dialog = Dialog.Ask(ui.Root, "Title", "Message", ("OK", () => { }));
        ui.Frame();
        Listing listing = new(ui.Theme) { Anchor = new UiRect(10f, 10f, 100f, 20f) };
        ui.Root.ShowPopup(listing);
        ui.Frame();
        Assert.Equal(2, ui.Root.Popups.Count);

        ui.Click(new UiVec2(dialog.Bounds.X + ui.Theme.Px(20f), dialog.Bounds.Y + ui.Theme.Px(60f)));
        Assert.Same(dialog, Assert.Single(ui.Root.Popups));
    }

    // A plain popup standing in for a dropdown's list.
    private sealed class Listing(EditorTheme theme) : Popup(theme)
    {
        protected override UiVec2 MeasureContent(LayoutContext context, UiVec2 available) => new(100f, 60f);
    }
}
